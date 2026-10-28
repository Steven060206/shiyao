using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MessagePurify.Infrastructure.Images;

/// <summary>顺序复用本地 OCR 子进程；失败不向调用方暴露图片、路径或子进程原始输出。</summary>
public sealed class LocalOcrProcess : IImageTextRecognizer, IAsyncDisposable
{
    private const int MaximumResponseCharacters = 2_000_000;
    private readonly string pythonPath;
    private readonly string workerPath;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private Process? process;
    private Task? stderr;
    private int disposed;

    public LocalOcrProcess(string pythonPath, string workerPath, TimeSpan? timeout = null)
    {
        this.pythonPath = Path.GetFullPath(pythonPath);
        this.workerPath = Path.GetFullPath(workerPath);
        this.timeout = timeout ?? TimeSpan.FromSeconds(90);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<ImageRecognition> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (string.IsNullOrWhiteSpace(imagePath) || !Path.IsPathFullyQualified(imagePath) || imagePath.Length > 32_768)
            throw new ArgumentException("ocr-invalid-image-path", nameof(imagePath));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
            requestCancellation.CancelAfter(timeout);
            try
            {
                EnsureStarted();
                var requestId = Guid.NewGuid().ToString("N");
                var request = JsonSerializer.Serialize(new { id = requestId, imagePath });
                await process!.StandardInput.WriteLineAsync(request.AsMemory(), requestCancellation.Token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(requestCancellation.Token).ConfigureAwait(false);
                var response = await ReadResponseAsync(process.StandardOutput, requestCancellation.Token).ConfigureAwait(false);
                return ParseResponse(response, requestId);
            }
            catch (OperationCanceledException)
            {
                await StopProcessAsync().ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                throw new IOException("ocr-timeout");
            }
            catch (Exception error) when (error is not ObjectDisposedException)
            {
                await StopProcessAsync().ConfigureAwait(false);
                var code = error is IOException && error.Message is "ocr-unavailable" or "ocr-invalid-response" or "ocr-failed"
                    ? error.Message : "ocr-failed";
                throw new IOException(code);
            }
        }
        finally { gate.Release(); }
    }

    private void EnsureStarted()
    {
        if (process is not null && !process.HasExited) return;
        process?.Dispose();
        process = null;
        if (!File.Exists(pythonPath) || !File.Exists(workerPath)) throw new IOException("ocr-unavailable");
        var start = new ProcessStartInfo(pythonPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false), WorkingDirectory = Path.GetDirectoryName(workerPath)!
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(workerPath);
        start.Environment.Remove("PYTHONHOME");
        start.Environment.Remove("PYTHONPATH");
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        try { process = Process.Start(start) ?? throw new IOException("ocr-unavailable"); }
        catch { throw new IOException("ocr-unavailable"); }
        stderr = DrainErrorAsync(process.StandardError);
    }

    private static async Task DrainErrorAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        try { while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { } }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
    }

    private static async Task<string> ReadResponseAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        {
            if (character[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= MaximumResponseCharacters) throw new IOException("ocr-invalid-response");
            text.Append(character[0]);
        }
        throw new IOException("ocr-invalid-response");
    }

    private static ImageRecognition ParseResponse(string text, string requestId)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.GetProperty("id").GetString() != requestId) throw new IOException("ocr-invalid-response");
        if (!root.GetProperty("success").GetBoolean()) throw new IOException("ocr-failed");
        var result = root.GetProperty("result");
        var recognized = result.GetProperty("text").GetString();
        var rawLines = result.GetProperty("lines");
        if (recognized is null || recognized.Length > 250_000 || rawLines.ValueKind != JsonValueKind.Array || rawLines.GetArrayLength() > 10_000)
            throw new IOException("ocr-invalid-response");
        var lines = new List<OcrTextLine>();
        foreach (var raw in rawLines.EnumerateArray())
        {
            var line = raw.GetProperty("text").GetString();
            var confidence = raw.GetProperty("confidence").GetDouble();
            var box = raw.GetProperty("box");
            if (line is null || line.Length > 250_000 || !double.IsFinite(confidence) || confidence is < 0 or > 1 ||
                box.ValueKind != JsonValueKind.Array || box.GetArrayLength() != 4) throw new IOException("ocr-invalid-response");
            var points = new List<IReadOnlyList<double>>();
            foreach (var point in box.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2) throw new IOException("ocr-invalid-response");
                var coordinates = point.EnumerateArray().Select(value => value.GetDouble()).ToArray();
                if (coordinates.Any(value => !double.IsFinite(value))) throw new IOException("ocr-invalid-response");
                points.Add(coordinates);
            }
            lines.Add(new(line, points, confidence));
        }
        return new(recognized, lines);
    }

    private async Task StopProcessAsync()
    {
        var previous = process;
        process = null;
        if (previous is null) return;
        try
        {
            if (!previous.HasExited) previous.Kill(entireProcessTree: true);
            await previous.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (stderr is not null) await stderr.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or IOException) { }
        finally { previous.Dispose(); stderr = null; }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopping.Cancel();
        await gate.WaitAsync().ConfigureAwait(false);
        try { await StopProcessAsync().ConfigureAwait(false); }
        finally { gate.Release(); }
    }
}
