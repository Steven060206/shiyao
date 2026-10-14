using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class SyncFlowRuntimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-sync-flow-" + Guid.NewGuid().ToString("N"));
    private static MessageHostStatus Success => new("disconnected", "unknown", "paused", "complete", null, DateTimeOffset.UtcNow, [])
    { LastSuccessfulSyncAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task StartupSyncRestoresQqThenWaitsWithoutReconnecting()
    {
        var sessions = new List<Session>();
        var calls = 0;
        var runtime = Create(sessions, new Game(), _ => { Interlocked.Increment(ref calls); return Task.FromResult(Success); });
        runtime.Start();
        try
        {
            await Until(() => runtime.Status.SyncPhase == "idle" && runtime.Status.LastSuccessfulSyncAt is not null);
            Assert.Single(sessions);
            Assert.Equal(1, sessions[0].Restores);
            Assert.False(runtime.Status.Scheduled);
            Assert.Null(runtime.Status.NextScheduledSyncAt);
            await Task.Delay(150);
            Assert.Equal(1, calls);
            Assert.Null(runtime.Status.Error);
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task RepeatedClicksDuringSyncAreCoalescedAndNextClickStartsNewBatch()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new List<Session>();
        var runtime = Create(sessions, new Game(), async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return Success; });
        runtime.Start();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(runtime.RequestSync());
            Assert.False(runtime.RequestSync());
            release.SetResult();
            await Until(() => runtime.Status.SyncPhase == "idle");
            Assert.Single(sessions);
            Assert.True(runtime.RequestSync());
            await Until(() => sessions.Count == 2 && runtime.Status.SyncPhase == "idle");
            Assert.All(sessions, s => Assert.Equal(1, s.Restores));
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task FailureRestoresQqAndRequiresAnotherClick()
    {
        var sessions = new List<Session>();
        var runtime = Create(sessions, new Game(), _ => throw new OneBotProtocolException("account-mismatch"));
        runtime.Start();
        try
        {
            await Until(() => runtime.Status.SyncPhase == "failed");
            Assert.Single(sessions);
            Assert.Equal(1, sessions[0].Restores);
            Assert.Equal("account-mismatch", runtime.Status.Error);
            Assert.Null(runtime.Status.LastSuccessfulSyncAt);
            await Task.Delay(150);
            Assert.Single(sessions);
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task GameDefersAllQqOperationsThenRunsOneBatchAfterExit()
    {
        var game = new Game { Active = true };
        var sessions = new List<Session>();
        var runtime = Create(sessions, game, _ => Task.FromResult(Success));
        runtime.Start();
        try
        {
            await Until(() => runtime.Status.SyncPhase == "deferred");
            Assert.Empty(sessions);
            Assert.Equal("game-active", runtime.Status.Error);
            Assert.False(runtime.RequestSync());
            game.Active = false;
            await Until(() => runtime.Status.SyncPhase == "idle");
            Assert.Single(sessions);
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task GameStartingDuringSyncRestoresQqAndRetainsOneRequest()
    {
        var game = new Game();
        var sessions = new List<Session>();
        var calls = 0;
        var runtime = Create(sessions, game, _ =>
        {
            if (Interlocked.Increment(ref calls) == 1) { game.Active = true; throw new OneBotProtocolException("game-active"); }
            return Task.FromResult(Success);
        });
        runtime.Start();
        try
        {
            await Until(() => runtime.Status.SyncPhase == "deferred");
            Assert.Single(sessions);
            Assert.Equal(1, sessions[0].Restores);
            game.Active = false;
            await Until(() => runtime.Status.SyncPhase == "idle");
            Assert.Equal(2, calls);
            Assert.Equal(2, sessions.Count);
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task ReadOnlyDiagnosticDoesNotStartOrStopQq()
    {
        var sessions = new List<Session>();
        var runtime = Create(sessions, new Game(), _ => Task.FromResult(Success));
        runtime.StartReadOnlyCheck();
        try
        {
            await Until(() => runtime.Status.LastSuccessfulSyncAt is not null);
            Assert.Empty(sessions);
            Assert.Null(runtime.Status.SyncPhase);
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task BackgroundStartupShowsLocalDataWithoutOpeningQqUntilClicked()
    {
        var sessions = new List<Session>();
        var runtime = Create(sessions, new Game(), _ => Task.FromResult(Success));
        runtime.Start(syncOnStartup: false);
        try
        {
            await Task.Delay(150);
            Assert.Empty(sessions);
            Assert.Equal("idle", runtime.Status.SyncPhase);
            Assert.True(runtime.RequestSync());
            await Until(() => sessions.Count == 1 && runtime.Status.SyncPhase == "idle");
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task GameCancellationRetainsRequestEvenIfGameExitsBeforeCatchRuns()
    {
        var game = new Game();
        var sessions = new List<Session>();
        var calls = 0;
        var runtime = Create(sessions, game, async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                game.Active = true;
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { game.Active = false; throw; }
            }
            return Success;
        });
        runtime.Start();
        try
        {
            await Until(() => calls == 2 && runtime.Status.SyncPhase == "idle");
            Assert.Equal(2, sessions.Count);
            Assert.All(sessions, s => Assert.Equal(1, s.Restores));
            Assert.Null(runtime.Status.Error);
        }
        finally { await runtime.StopAsync(); }
    }

    [Fact]
    public async Task StoppingAnInFlightBatchRestoresQqBeforeReturning()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new List<Session>();
        var runtime = Create(sessions, new Game(), async ct => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Success; });
        runtime.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await runtime.StopAsync();
        Assert.Single(sessions);
        Assert.Equal(1, sessions[0].Restores);
    }

    private AppRuntime Create(List<Session> sessions, Game game, Func<CancellationToken, Task<MessageHostStatus>> batch) =>
        new(root, new AccountWorkspace(root, "12345"), () => { var s = new Session(); sessions.Add(s); return s; }, game, batch, TimeSpan.FromSeconds(3));

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class Game : IGameActivityDetector
    {
        public volatile bool Active;
        public bool IsGameRunning() => Active;
    }
    private sealed class Session : IQqSyncSession
    {
        public int Restores;
        public Task StartAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task RestoreAsync() { Interlocked.CompareExchange(ref Restores, 1, 0); return Task.CompletedTask; }
        public async ValueTask DisposeAsync() => await RestoreAsync();
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
