using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class QqSyncSessionTests
{
    [Fact]
    public async Task SynchronizationClosesOrdinaryQqThenRestoresItExactlyOnce()
    {
        var processes = new FakeProcesses();
        var session = new QqSyncSession(processes);
        await session.StartAsync(CancellationToken.None);
        Assert.Equal(["prepare", "capture", "stop-ordinary", "start-framework"], processes.Calls);
        await session.RestoreAsync();
        await session.RestoreAsync();
        await session.DisposeAsync();
        Assert.Equal(["prepare", "capture", "stop-ordinary", "start-framework", "stop-framework", "start-ordinary"], processes.Calls);
        Assert.True(processes.NormalRunning);
        Assert.False(processes.FrameworkRunning);
    }

    [Fact]
    public async Task MissingDependenciesDoNotCloseOrStartQq()
    {
        var processes = new FakeProcesses { MissingDependencies = true };
        await using var session = new QqSyncSession(processes);
        await Assert.ThrowsAsync<FileNotFoundException>(() => session.StartAsync(CancellationToken.None));
        await session.RestoreAsync();
        Assert.Equal(["prepare"], processes.Calls);
        Assert.True(processes.NormalRunning);
    }

    [Fact]
    public async Task FrameworkStartupFailureRestoresNormalQqIncludingPartialLaunch()
    {
        var processes = new FakeProcesses { FrameworkStartFails = true };
        await using var session = new QqSyncSession(processes);
        await Assert.ThrowsAsync<IOException>(() => session.StartAsync(CancellationToken.None));
        Assert.True(processes.NormalRunning);
        Assert.False(processes.FrameworkRunning);
        Assert.Equal(["prepare", "capture", "stop-ordinary", "start-framework", "stop-framework", "start-ordinary"], processes.Calls);
    }

    [Theory]
    [InlineData("stop-ordinary")]
    [InlineData("start-framework")]
    public async Task CancellationDuringSwitchRestoresQqWithoutUsingCancelledToken(string at)
    {
        using var cancellation = new CancellationTokenSource();
        var processes = new FakeProcesses { Cancellation = cancellation, CancelAt = at };
        await using var session = new QqSyncSession(processes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.StartAsync(cancellation.Token));
        Assert.True(processes.NormalRunning);
        Assert.False(processes.FrameworkRunning);
        Assert.Equal("start-ordinary", processes.Calls[^1]);
    }

    [Fact]
    public async Task NoInitialQqStillReturnsToOrdinaryQqAfterSync()
    {
        var processes = new FakeProcesses { NormalRunning = false };
        await using var session = new QqSyncSession(processes);
        await session.StartAsync(CancellationToken.None);
        await session.RestoreAsync();
        Assert.True(processes.NormalRunning);
        Assert.False(processes.FrameworkRunning);
    }

    [Fact]
    public async Task SecondStartIsRejectedWithoutASecondSwitch()
    {
        var processes = new FakeProcesses();
        await using var session = new QqSyncSession(processes);
        await session.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(CancellationToken.None));
        Assert.Single(processes.Calls, call => call == "start-framework");
    }

    [Fact]
    public async Task CancelledBeforePreparationDoesNotTouchQq()
    {
        var processes = new FakeProcesses();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var session = new QqSyncSession(processes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.StartAsync(cancellation.Token));
        Assert.Empty(processes.Calls);
    }

    [Fact]
    public async Task CancellationDuringPreparationPreservesOriginalQq()
    {
        using var cancellation = new CancellationTokenSource();
        var processes = new FakeProcesses { Cancellation = cancellation, CancelAt = "prepare" };
        await using var session = new QqSyncSession(processes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.StartAsync(cancellation.Token));
        Assert.Equal(["prepare"], processes.Calls);
        Assert.True(processes.NormalRunning);
    }

    [Fact]
    public async Task FailedRestorationCanBeRetriedWithoutRestartingFramework()
    {
        var processes = new FakeProcesses { RestoreFailsOnce = true };
        await using var session = new QqSyncSession(processes);
        await session.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(session.RestoreAsync);
        await session.RestoreAsync();
        Assert.True(processes.NormalRunning);
        Assert.Single(processes.Calls, call => call == "start-framework");
    }

    private sealed class FakeProcesses : IQqSyncProcessAdapter
    {
        public List<string> Calls { get; } = [];
        public bool NormalRunning { get; set; } = true;
        public bool FrameworkRunning { get; private set; }
        public bool MissingDependencies { get; init; }
        public bool FrameworkStartFails { get; init; }
        public bool RestoreFailsOnce { get; set; }
        public CancellationTokenSource? Cancellation { get; init; }
        public string? CancelAt { get; init; }

        public Task ValidateAndPrepareAsync(CancellationToken cancellationToken)
        {
            Record("prepare", cancellationToken);
            if (MissingDependencies) throw new FileNotFoundException();
            return Task.CompletedTask;
        }
        public void CaptureOrdinaryQq() => Calls.Add("capture");
        public Task StopOrdinaryQqAsync(CancellationToken cancellationToken)
        {
            NormalRunning = false;
            Record("stop-ordinary", cancellationToken);
            return Task.CompletedTask;
        }
        public Task StartFrameworkAsync(CancellationToken cancellationToken)
        {
            FrameworkRunning = true;
            Record("start-framework", cancellationToken);
            if (FrameworkStartFails) throw new IOException();
            return Task.CompletedTask;
        }
        public Task StopFrameworkAsync()
        {
            Calls.Add("stop-framework");
            FrameworkRunning = false;
            return Task.CompletedTask;
        }
        public Task StartOrdinaryQqAsync()
        {
            Calls.Add("start-ordinary");
            if (RestoreFailsOnce) { RestoreFailsOnce = false; throw new IOException(); }
            NormalRunning = true;
            return Task.CompletedTask;
        }
        private void Record(string call, CancellationToken cancellationToken)
        {
            Calls.Add(call);
            if (CancelAt == call) Cancellation!.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
