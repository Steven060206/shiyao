using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Backup;
using MessagePurify.Infrastructure.Images;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class ImageOcrRuntimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Shiyao-ocr-runtime-" + Guid.NewGuid().ToString("N"));
    private static readonly BackupApplicationSettings Settings = new(false, "system", "system", null);
    private static readonly ImageRecognition Recognition = new("明天18点前提交数学作业", []);
    private static MessageHostStatus Success => new("disconnected", "unknown", "paused", "complete", null, DateTimeOffset.UtcNow, []);

    [Fact]
    public async Task RecognitionWaitsForNormalQqRestoreThenRefreshesSavedResult()
    {
        var workspace = await DownloadedWorkspace();
        var restoring = Signal(); var restored = Signal(); var recognizedPage = Signal();
        var recognizer = new Recognizer((_, _) => Task.FromResult(Recognition));
        var runtime = Create(workspace, recognizer, session: () => new Session(async () =>
        { restoring.TrySetResult(); await restored.Task; }));
        runtime.Changed += () =>
        {
            if (Image(workspace).Status == "recognized") recognizedPage.TrySetResult();
        };
        runtime.Start();
        try
        {
            await restoring.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await Task.Delay(200);
            Assert.Equal(0, recognizer.Calls);
            Assert.Equal("downloaded", Image(workspace).Status);
            restored.SetResult();
            await recognizedPage.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.Equal(1, recognizer.Calls);
            Assert.Equal("review", Assert.Single(workspace.Snapshot(DateTimeOffset.UtcNow).Items).Kind);
        }
        finally { restored.TrySetResult(); await runtime.StopAsync(); }
    }

    [Fact]
    public async Task GameCancelsInFlightRecognitionAndRetainsQueueUntilExit()
    {
        var workspace = await DownloadedWorkspace(); var game = new Game();
        var entered = Signal(); var cancelled = Signal();
        var recognizer = new Recognizer(async (call, token) =>
        {
            if (call == 1)
            {
                entered.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { cancelled.SetResult(); }
            }
            return Recognition;
        });
        var runtime = Create(workspace, recognizer, game);
        runtime.Start(syncOnStartup: false);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            game.Active = true;
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(600);
            Assert.Equal(1, recognizer.Calls);
            Assert.Equal("downloaded", Image(workspace).Status);
            game.Active = false;
            await Until(() => Image(workspace).Status == "recognized");
            Assert.Equal(2, recognizer.Calls);
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task SyncWaitsForCancelledWorkerCleanupBeforeStartingQq()
    {
        var workspace = await DownloadedWorkspace(); var entered = Signal();
        var cancelling = Signal(); var cleanup = Signal(); var qqStarted = Signal();
        var recognizer = new Recognizer(async (call, token) =>
        {
            if (call == 1)
            {
                entered.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelling.SetResult(); await cleanup.Task; throw; }
            }
            return Recognition;
        });
        var runtime = Create(workspace, recognizer, session: () => new Session(start: () => qqStarted.SetResult()));
        runtime.Start(syncOnStartup: false);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.True(runtime.RequestSync());
            await cancelling.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(qqStarted.Task.IsCompleted);
            cleanup.SetResult();
            await qqStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Until(() => Image(workspace).Status == "recognized");
            Assert.Equal(2, recognizer.Calls);
        }
        finally { cleanup.TrySetResult(); await runtime.StopAsync(); }
    }

    [Fact]
    public async Task BackupRestoreCancelsWorkerAndWaitsBeforeReplacingFiles()
    {
        var workspace = await DownloadedWorkspace(); var entered = Signal();
        var cancelling = Signal(); var cleanup = Signal(); var applied = Signal(); var game = new Game();
        var package = Path.Combine(root, "snapshot.zip");
        workspace.ExportBackup(package, Settings, DateTimeOffset.UtcNow);
        using var prepared = workspace.InspectBackup(package);
        var recognizer = new Recognizer(async (call, token) =>
        {
            if (call == 1)
            {
                entered.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelling.SetResult(); await cleanup.Task; throw; }
            }
            return Recognition;
        });
        var runtime = Create(workspace, recognizer, game);
        runtime.Start(syncOnStartup: false);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var restoring = runtime.RestoreBackupAsync(prepared, Settings, _ => applied.SetResult());
            await cancelling.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(runtime.IsRestoring);
            Assert.False(applied.Task.IsCompleted);
            game.Active = true;
            cleanup.SetResult();
            await restoring.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.True(applied.Task.IsCompleted);
            Assert.Equal(1, workspace.Generation);
            Assert.Equal("downloaded", Image(workspace).Status);
            game.Active = false;
            await Until(() => Image(workspace).Status == "recognized");
            Assert.Equal(2, recognizer.Calls);
        }
        finally { cleanup.TrySetResult(); await runtime.StopAsync(); }
    }

    [Fact]
    public async Task StopWaitsForInFlightWorkerAndAsyncDisposal()
    {
        var workspace = await DownloadedWorkspace(); var entered = Signal();
        var cancelling = Signal(); var cleanup = Signal(); var disposing = Signal(); var disposed = Signal();
        var recognizer = new Recognizer(async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelling.SetResult(); await cleanup.Task; throw; }
            return Recognition;
        }, async () => { disposing.SetResult(); await disposed.Task; });
        var runtime = Create(workspace, recognizer);
        runtime.Start(syncOnStartup: false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        var stopping = runtime.StopAsync();
        try
        {
            await cancelling.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(stopping.IsCompleted);
            cleanup.SetResult();
            await disposing.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(stopping.IsCompleted);
            disposed.SetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("downloaded", Image(workspace).Status);
        }
        finally { cleanup.TrySetResult(); disposed.TrySetResult(); await stopping; }
    }

    [Fact]
    public async Task FailedQqRestorePreventsOcrUntilSuccessfulSessionRestore()
    {
        var workspace = await DownloadedWorkspace(); var sessions = 0;
        var recognizer = new Recognizer((_, _) => Task.FromResult(Recognition));
        var runtime = Create(workspace, recognizer, session: () =>
            Interlocked.Increment(ref sessions) == 1
                ? new Session(() => Task.FromException(new IOException("synthetic-restore-failure"))) : new Session());
        runtime.Start();
        try
        {
            await Until(() => runtime.Status.Error == "qq-restore-failed");
            await Task.Delay(700);
            Assert.Equal(0, recognizer.Calls);
            Assert.True(runtime.RequestSync());
            await Until(() => Image(workspace).Status == "recognized");
            Assert.Equal(2, sessions);
            Assert.Equal(1, recognizer.Calls);
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task OcrFailureRefreshesPageAndDoesNotRetryWithoutUserAction()
    {
        var workspace = await DownloadedWorkspace(); var failedPage = Signal();
        var recognizer = new Recognizer((_, _) => Task.FromException<ImageRecognition>(new IOException("synthetic-failure")));
        var runtime = Create(workspace, recognizer);
        runtime.Changed += () => { if (Image(workspace).Status == "ocr-failed") failedPage.TrySetResult(); };
        runtime.Start(syncOnStartup: false);
        try
        {
            await failedPage.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await Task.Delay(650);
            Assert.Equal(1, recognizer.Calls);
            Assert.NotNull(Image(workspace).RelativePath);
        }
        finally { await runtime.StopAsync(); }
    }

    private AppRuntime Create(AccountWorkspace workspace, Recognizer recognizer, Game? game = null,
        Func<IQqSyncSession>? session = null) => new(root, workspace, session ?? (() => new Session()),
            game ?? new Game(), _ => Task.FromResult(Success), TimeSpan.FromSeconds(4), recognizer);

    private async Task<AccountWorkspace> DownloadedWorkspace()
    {
        var workspace = new AccountWorkspace(root, "12345"); var pins = new HashSet<string> { "100" };
        workspace.SetAvailableGroups([new("100", "数学群")], pins);
        workspace.Execute(new("12345", "confirm-group", "100", "course", 0, Course: "数学"), DateTimeOffset.UtcNow);
        workspace.Ingest(new(new("12345", "100", "1"), "67890", DateTimeOffset.UtcNow,
            [new(SegmentKind.Image, "", AssetId: "synthetic-image")]), pins, MessageOrigin.History, "test", DateTimeOffset.UtcNow);
        var source = Path.Combine(root, "source.png");
        File.WriteAllBytes(source, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXs8AAAAASUVORK5CYII="));
        await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(source)), DateTimeOffset.UtcNow);
        return workspace;
    }

    private ImageEvidenceRecord Image(AccountWorkspace workspace) =>
        Assert.Single(new AccountImageStore(root, workspace.AccountId).List());
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private sealed class Game : IGameActivityDetector
    {
        public volatile bool Active;
        public bool IsGameRunning() => Active;
    }
    private sealed class Recognizer(Func<int, CancellationToken, Task<ImageRecognition>> recognize,
        Func<Task>? dispose = null) : IImageTextRecognizer, IAsyncDisposable
    {
        public int Calls;
        public Task<ImageRecognition> RecognizeAsync(string path, CancellationToken token = default) =>
            recognize(Interlocked.Increment(ref Calls), token);
        public async ValueTask DisposeAsync() { if (dispose is not null) await dispose(); }
    }
    private sealed class Session(Func<Task>? restore = null, Action? start = null) : IQqSyncSession
    {
        public Task StartAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); start?.Invoke(); return Task.CompletedTask; }
        public Task RestoreAsync() => restore?.Invoke() ?? Task.CompletedTask;
        public async ValueTask DisposeAsync() => await RestoreAsync();
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
