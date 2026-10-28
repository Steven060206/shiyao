using System.Diagnostics;
using MessagePurify.Infrastructure.Images;

namespace MessagePurify.Infrastructure.Tests;

public sealed class LocalOcrProcessTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "OCR 宿主 tests " + Guid.NewGuid().ToString("N"));
    private readonly string python;

    public LocalOcrProcessTests()
    {
        Directory.CreateDirectory(folder);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ocr", "worker.py"))) root = root.Parent;
        Assert.NotNull(root);
        python = Path.Combine(root.FullName, ".tools", "ocr", "portable", "python", "python.exe");
        if (!File.Exists(python)) python = Path.Combine(root.FullName, ".tools", "ocr", "venv", "Scripts", "python.exe");
        Assert.True(File.Exists(python), "Prepare the project OCR Python runtime before running process tests.");
    }

    private string Script(string body)
    {
        var path = Path.Combine(folder, "worker.py");
        File.WriteAllText(path, "import sys,json,os,time\n" + body);
        return path;
    }

    private const string ValidLoop = """
count=0
for raw in sys.stdin:
    request=json.loads(raw);count+=1
    print(json.dumps({'id':request['id'],'success':True,'result':{'text':str(os.getpid())+':'+str(count),'lines':[{'text':'合成文字','box':[[1,2],[3,2],[3,4],[1,4]],'confidence':0.98}]}},ensure_ascii=False),flush=True)
""";

    [Fact]
    public async Task SequentialRequestsReuseOneProcessAndReadUtf8Lines()
    {
        await using var host = new LocalOcrProcess(python, Script(ValidLoop));
        var first = await host.RecognizeAsync(Path.Combine(folder, "原图一.png"));
        var second = await host.RecognizeAsync(Path.Combine(folder, "原图二.png"));
        Assert.Equal(first.Text.Split(':')[0], second.Text.Split(':')[0]);
        Assert.EndsWith(":1", first.Text);
        Assert.EndsWith(":2", second.Text);
        var line = Assert.Single(first.Lines);
        Assert.Equal("合成文字", line.Text);
        Assert.Equal(4, line.Box.Count);
        Assert.Equal(0.98, line.Confidence);
    }

    [Fact]
    public async Task ConcurrentRequestsAreSerializedOnOneWorker()
    {
        await using var host = new LocalOcrProcess(python, Script(ValidLoop));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => host.RecognizeAsync(Path.Combine(folder, index + ".png"))));
        Assert.Single(results.Select(result => result.Text.Split(':')[0]).Distinct());
        Assert.Equal(Enumerable.Range(1, 8), results.Select(result => int.Parse(result.Text.Split(':')[1])).Order());
    }

    [Fact]
    public async Task TimeoutKillsWorkerAndNextRequestStartsFreshProcess()
    {
        var marker = Path.Combine(folder, "started.txt");
        var script = Script("""
from pathlib import Path
marker=Path(__file__).with_name('started.txt')
if not marker.exists():
    marker.write_text(str(os.getpid()))
    time.sleep(30)
""" + "\n" + ValidLoop);
        await using var host = new LocalOcrProcess(python, script, TimeSpan.FromSeconds(1));
        var error = await Assert.ThrowsAsync<IOException>(() => host.RecognizeAsync(Path.Combine(folder, "private-original.png")));
        Assert.Equal("ocr-timeout", error.Message);
        var oldId = int.Parse(File.ReadAllText(marker));
        Assert.False(IsRunning(oldId));
        var result = await host.RecognizeAsync(Path.Combine(folder, "next.png"));
        Assert.NotEqual(oldId.ToString(), result.Text.Split(':')[0]);
    }

    [Fact]
    public async Task CallerCancellationKillsWorkerWithoutLeakingInputPath()
    {
        var marker = Path.Combine(folder, "started.txt");
        var script = Script("from pathlib import Path\nPath(__file__).with_name('started.txt').write_text(str(os.getpid()))\ntime.sleep(30)\n");
        await using var host = new LocalOcrProcess(python, script);
        using var cancellation = new CancellationTokenSource();
        var request = host.RecognizeAsync(Path.Combine(folder, "private-original.png"), cancellation.Token);
        await WaitForFile(marker);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.DoesNotContain(folder, error.ToString());
        Assert.False(IsRunning(int.Parse(File.ReadAllText(marker))));
    }

    [Theory]
    [InlineData("wrong-id")]
    [InlineData("bad-confidence")]
    [InlineData("bad-box")]
    [InlineData("oversized")]
    [InlineData("invalid-json")]
    [InlineData("worker-failure")]
    [InlineData("exit")]
    public async Task InvalidResponseIsSafeAndNextRequestCanRestart(string mode)
    {
        var prelude = $$$"""
from pathlib import Path
marker=Path(__file__).with_name('failed-once')
if not marker.exists():
    marker.write_text('yes')
    request=json.loads(sys.stdin.readline())
    mode='{{{mode}}}'
    if mode=='exit':sys.exit(3)
    if mode=='invalid-json':print('private leaked original {',flush=True)
    elif mode=='oversized':print('x'*2100000,flush=True)
    elif mode=='worker-failure':print(json.dumps({'id':request['id'],'success':False,'error':{'message':'private original path secret'}}),flush=True)
    else:
        box=[[1,2],[3,2],[3,4],[1,4]] if mode!='bad-box' else [[1,2]]
        print(json.dumps({'id':'wrong' if mode=='wrong-id' else request['id'],'success':True,'result':{'text':'text','lines':[{'text':'line','box':box,'confidence':2 if mode=='bad-confidence' else 0.9}]}}),flush=True)
    time.sleep(30)
""";
        await using var host = new LocalOcrProcess(python, Script(prelude + "\n" + ValidLoop), TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<IOException>(() => host.RecognizeAsync(Path.Combine(folder, "private-input.png")));
        Assert.StartsWith("ocr-", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private", error.ToString());
        Assert.EndsWith(":1", (await host.RecognizeAsync(Path.Combine(folder, "next.png"))).Text);
    }

    [Fact]
    public async Task DisposalStopsIdleWorkerAndRejectsNewRequests()
    {
        var host = new LocalOcrProcess(python, Script(ValidLoop));
        var result = await host.RecognizeAsync(Path.Combine(folder, "synthetic.png"));
        var processId = int.Parse(result.Text.Split(':')[0]);
        await host.DisposeAsync();
        Assert.False(IsRunning(processId));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.RecognizeAsync(Path.Combine(folder, "next.png")));
    }

    [Fact]
    public async Task DisposalCancelsActiveRecognitionAndLeavesNoProcess()
    {
        var marker = Path.Combine(folder, "started.txt");
        var host = new LocalOcrProcess(python, Script("from pathlib import Path\nPath(__file__).with_name('started.txt').write_text(str(os.getpid()))\ntime.sleep(30)\n"));
        var request = host.RecognizeAsync(Path.Combine(folder, "image.png"));
        await WaitForFile(marker);
        await host.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => request);
        Assert.False(IsRunning(int.Parse(File.ReadAllText(marker))));
    }

    [Fact]
    public async Task RealLocalWorkerRecognizesSyntheticChineseDate()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ocr", "worker.py"))) root = root.Parent;
        Assert.NotNull(root);
        await using var host = new LocalOcrProcess(python, Path.Combine(root.FullName, "ocr", "worker.py"));
        var original = Path.Combine(root.FullName, "tests", "fixtures", "ocr", "synthetic-notice.png");
        var before = File.ReadAllBytes(original);
        var result = await host.RecognizeAsync(original);
        Assert.Contains("2026年10月12日18:00", result.Text);
        Assert.NotEmpty(result.Lines);
        Assert.Equal(before, File.ReadAllBytes(original));
    }

    [Fact]
    public async Task MissingInterpreterReturnsOnlySafeErrorCode()
    {
        await using var host = new LocalOcrProcess(Path.Combine(folder, "private-python.exe"), Script(ValidLoop));
        var error = await Assert.ThrowsAsync<IOException>(() => host.RecognizeAsync(Path.Combine(folder, "image.png")));
        Assert.Equal("ocr-unavailable", error.Message);
        Assert.Null(error.InnerException);
    }

    private static bool IsRunning(int id)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task WaitForFile(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(File.Exists(path));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);
}
