using MessagePurify.Infrastructure.Application;
using System.Threading.Channels;

namespace MessagePurify.Infrastructure.Tests;

public sealed class ScheduledSyncTests
{
    [Fact]
    public void UnpreparedCatalogBlocksSyncWithoutPretendingAGameIsRunning()
    {
        var controller = new ResidentSyncController(new UnpreparedGameDetector());
        Assert.False(controller.GameActive);
        Assert.False(controller.CanSync);
        Assert.Equal("game-catalog-unavailable", controller.PauseReason);
    }

    private sealed class UnpreparedGameDetector : IGameActivityDetector
    {
        public bool IsReady => false;
        public bool IsGameRunning() => false;
    }

    [Fact]
    public void StartupImmediatelyBeforeEightDoesNotConsumeTheFixedSlot()
    {
        var planner = new ScheduledSyncPlanner(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));

        var startup = planner.Evaluate(DateTimeOffset.Parse("2026-10-12T07:59:59+08:00"), gameActive: false);
        Assert.NotNull(startup);
        Assert.Equal(SyncRequestKind.Startup, startup.Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-10-11T20:00:00+08:00"), startup.SlotAt);

        var morning = planner.Evaluate(DateTimeOffset.Parse("2026-10-12T00:00:00Z"), gameActive: false);
        Assert.NotNull(morning);
        Assert.Equal(SyncRequestKind.Scheduled, morning.Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T08:00:00+08:00"), morning.SlotAt);
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T08:00:01+08:00"), gameActive: false));
        var noon = planner.Evaluate(DateTimeOffset.Parse("2026-10-12T12:00:00+08:00"), gameActive: false);
        Assert.NotNull(noon);
        Assert.Equal(SyncRequestKind.Scheduled, noon.Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T12:00:00+08:00"), noon.SlotAt);
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T12:00:01+08:00"), gameActive: false));
    }

    [Theory]
    [InlineData("2026-10-12T08:01:00+08:00", "2026-10-12T08:00:00+08:00", "2026-10-12T12:00:00+08:00")]
    [InlineData("2026-10-12T10:00:00+08:00", "2026-10-12T08:00:00+08:00", "2026-10-12T12:00:00+08:00")]
    [InlineData("2026-10-12T20:01:00+08:00", "2026-10-12T20:00:00+08:00", "2026-10-13T08:00:00+08:00")]
    [InlineData("2026-10-13T00:01:00+08:00", "2026-10-12T20:00:00+08:00", "2026-10-13T08:00:00+08:00")]
    public void MorningAndNightStartupKeepTheNextFixedPoint(string start, string covered, string next)
    {
        var planner = new ScheduledSyncPlanner(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        Assert.Equal(DateTimeOffset.Parse(covered), planner.Evaluate(DateTimeOffset.Parse(start), false)!.SlotAt);
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse(next).AddSeconds(-1), false));
        var scheduled = planner.Evaluate(DateTimeOffset.Parse(next), false);
        Assert.NotNull(scheduled);
        Assert.Equal(SyncRequestKind.Scheduled, scheduled.Kind);
        Assert.Equal(DateTimeOffset.Parse(next), scheduled.SlotAt);
    }

    [Fact]
    public void EveryDayKeepsAllThreeFixedPointsIncludingTheNextDay()
    {
        var planner = new ScheduledSyncPlanner(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        _ = planner.Evaluate(DateTimeOffset.Parse("2026-10-12T07:00:00+08:00"), false);
        foreach (var date in new[] { "2026-10-12", "2026-10-13" })
        foreach (var clock in new[] { "08:00:00", "12:00:00", "20:00:00" })
        {
            var instant = DateTimeOffset.Parse($"{date}T{clock}+08:00");
            Assert.Null(planner.Evaluate(instant.AddSeconds(-1), false));
            var scheduled = planner.Evaluate(instant, false);
            Assert.NotNull(scheduled);
            Assert.Equal(SyncRequestKind.Scheduled, scheduled.Kind);
            Assert.Equal(instant, scheduled.SlotAt);
            Assert.Null(planner.Evaluate(instant.AddSeconds(1), false));
        }
    }

    [Fact]
    public void StartupExactlyAtFixedPointDoesNotEmitASecondBatchForTheSameSlot()
    {
        var planner = new ScheduledSyncPlanner(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        var instant = DateTimeOffset.Parse("2026-10-12T08:00:00+08:00");
        Assert.Equal(instant, planner.Evaluate(instant, false)!.SlotAt);
        Assert.Null(planner.Evaluate(instant.AddSeconds(1), false));
        Assert.Equal(SyncRequestKind.Scheduled,
            planner.Evaluate(DateTimeOffset.Parse("2026-10-12T12:00:00+08:00"), false)!.Kind);
    }

    [Fact]
    public void GameDefersMultipleSlotsIntoOneCatchUp()
    {
        var planner = new ScheduledSyncPlanner(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        _ = planner.Evaluate(DateTimeOffset.Parse("2026-10-12T07:00:00+08:00"), gameActive: false);

        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T07:30:00+08:00"), gameActive: true));
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T12:30:00+08:00"), gameActive: true));
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T20:30:00+08:00"), gameActive: true));

        var catchUp = planner.Evaluate(DateTimeOffset.Parse("2026-10-12T21:00:00+08:00"), gameActive: false);
        Assert.NotNull(catchUp);
        Assert.Equal(SyncRequestKind.CatchUp, catchUp.Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T20:00:00+08:00"), catchUp.SlotAt);
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T21:00:01+08:00"), gameActive: false));
    }

    [Fact]
    public void GameEndingAfterANewerSlotMergesTheDeferredBatchWithThatSlot()
    {
        var planner = new ScheduledSyncPlanner(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        _ = planner.Evaluate(DateTimeOffset.Parse("2026-10-12T07:00:00+08:00"), false);
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T08:05:00+08:00"), true));
        var caughtUp = planner.Evaluate(DateTimeOffset.Parse("2026-10-12T12:01:00+08:00"), false);
        Assert.NotNull(caughtUp);
        Assert.Equal(SyncRequestKind.CatchUp, caughtUp.Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T12:00:00+08:00"), caughtUp.SlotAt);
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T12:01:01+08:00"), false));
    }

    [Fact]
    public void StartingWhileGamingWaitsAndCatchesUpOnlyOnce()
    {
        var planner = new ScheduledSyncPlanner(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T07:59:59+08:00"), true));
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T08:00:00+08:00"), true));
        var caughtUp = planner.Evaluate(DateTimeOffset.Parse("2026-10-12T08:05:00+08:00"), false);
        Assert.NotNull(caughtUp);
        Assert.Equal(SyncRequestKind.CatchUp, caughtUp.Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T08:00:00+08:00"), caughtUp.SlotAt);
        Assert.Null(planner.Evaluate(DateTimeOffset.Parse("2026-10-12T08:05:01+08:00"), false));
    }

    [Fact]
    public async Task ResidentControllerDeliversRequestsWithoutTouchingQq()
    {
        var controller = new ResidentSyncController();
        var request = new SyncRequest(SyncRequestKind.Scheduled,
            DateTimeOffset.Parse("2026-10-12T20:00:00+08:00"),
            DateTimeOffset.Parse("2026-10-12T20:00:01+08:00"));
        controller.Request(request);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        Assert.Equal(request, await controller.WaitAsync(cancellation.Token));
    }

    [Fact]
    public async Task ControllerRetainsAnUnacknowledgedBatchAcrossConnectionCancellation()
    {
        var controller = new ResidentSyncController();
        var request = RequestAt("2026-10-12T08:00:00+08:00");
        controller.Request(request);
        using var firstConnection = new CancellationTokenSource();
        Assert.Equal(request, await controller.WaitAsync(firstConnection.Token));
        firstConnection.Cancel();
        Assert.Equal(request, await controller.WaitAsync(CancellationToken.None));
        controller.Acknowledge(request);
        controller.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => controller.WaitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ControllerMergesMultiplePausedSlotsAndAcknowledgesOnlyOneBatch()
    {
        var game = new FakeGameDetector { Active = false };
        var controller = new ResidentSyncController(game);
        var morning = RequestAt("2026-10-12T08:00:00+08:00");
        controller.Request(morning);
        Assert.Equal(morning, await controller.WaitAsync(CancellationToken.None));
        game.Active = true;
        Assert.True(controller.GameActive);
        Assert.False(controller.CanSync);
        controller.Request(RequestAt("2026-10-12T12:00:00+08:00"));
        var evening = RequestAt("2026-10-12T20:00:00+08:00", SyncRequestKind.CatchUp);
        controller.Request(evening);
        game.Active = false;
        Assert.True(controller.CanSync);
        var resumed = controller.CoalescePending(morning);
        Assert.Equal(evening, resumed);
        controller.Acknowledge(resumed);
        controller.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => controller.WaitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ReconnectingCoalescesWaitingSlotsAndAnOldAcknowledgementCannotClearTheNewBatch()
    {
        var controller = new ResidentSyncController();
        var morning = RequestAt("2026-10-12T08:00:00+08:00");
        controller.Request(morning);
        Assert.Equal(morning, await controller.WaitAsync(CancellationToken.None));
        var evening = RequestAt("2026-10-12T20:00:00+08:00");
        controller.Request(evening);
        controller.Request(RequestAt("2026-10-12T12:00:00+08:00"));
        Assert.Equal(evening, await controller.WaitAsync(CancellationToken.None));
        controller.Acknowledge(morning);
        Assert.Equal(evening, await controller.WaitAsync(CancellationToken.None));
        controller.Acknowledge(evening);
        controller.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => controller.WaitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AcknowledgementDropsOlderDuplicatesAndKeepsANewerScheduledSlot()
    {
        var controller = new ResidentSyncController();
        var morning = RequestAt("2026-10-12T08:00:00+08:00");
        controller.Request(morning);
        Assert.Equal(morning, await controller.WaitAsync(CancellationToken.None));
        controller.Request(morning);
        controller.Request(RequestAt("2026-10-11T20:00:00+08:00"));
        var noon = RequestAt("2026-10-12T12:00:00+08:00");
        controller.Request(noon);
        controller.Acknowledge(morning);
        Assert.Equal(noon, await controller.WaitAsync(CancellationToken.None));
        controller.Acknowledge(noon);
        controller.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => controller.WaitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResumedBatchCoversACatchUpEnqueuedAfterResumeButBeforeAcknowledgement()
    {
        var controller = new ResidentSyncController();
        var morning = RequestAt("2026-10-12T08:00:00+08:00");
        controller.Request(morning);
        Assert.Equal(morning, await controller.WaitAsync(CancellationToken.None));
        Assert.Equal(morning, controller.CoalescePending(morning));
        controller.Request(RequestAt("2026-10-12T20:00:00+08:00", SyncRequestKind.CatchUp));

        controller.Acknowledge(morning, includeCatchUpRequests: true,
            completedAt: DateTimeOffset.Parse("2026-10-12T21:00:00+08:00"));
        controller.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => controller.WaitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResumedBatchAlsoCoversACatchUpFirstObservedAfterAcknowledgement()
    {
        var controller = new ResidentSyncController();
        var morning = RequestAt("2026-10-12T08:00:00+08:00");
        controller.Request(morning);
        Assert.Equal(morning, await controller.WaitAsync(CancellationToken.None));
        var completed = DateTimeOffset.Parse("2026-10-12T21:00:00+08:00");
        controller.Acknowledge(morning, includeCatchUpRequests: true, completedAt: completed);
        controller.Request(new(SyncRequestKind.CatchUp,
            DateTimeOffset.Parse("2026-10-12T20:00:00+08:00"), completed.AddSeconds(1)));
        controller.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => controller.WaitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResumedBatchDoesNotDiscardANewerOrdinaryScheduledRequest()
    {
        var controller = new ResidentSyncController();
        var morning = RequestAt("2026-10-12T08:00:00+08:00");
        controller.Request(morning);
        Assert.Equal(morning, await controller.WaitAsync(CancellationToken.None));
        var evening = RequestAt("2026-10-12T20:00:00+08:00");
        controller.Request(evening);
        controller.Acknowledge(morning, includeCatchUpRequests: true,
            completedAt: DateTimeOffset.Parse("2026-10-12T21:00:00+08:00"));
        Assert.Equal(evening, await controller.WaitAsync(CancellationToken.None));
        controller.Acknowledge(evening);
        controller.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => controller.WaitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task OrdinaryCompletionCannotCoverAGameCatchUpThatHasNotBeenRead()
    {
        var controller = new ResidentSyncController();
        var morning = RequestAt("2026-10-12T08:00:00+08:00");
        controller.Request(morning);
        Assert.Equal(morning, await controller.WaitAsync(CancellationToken.None));
        var catchUp = RequestAt("2026-10-12T20:00:00+08:00", SyncRequestKind.CatchUp);
        controller.Request(catchUp);
        controller.Acknowledge(morning);
        Assert.Equal(catchUp, await controller.WaitAsync(CancellationToken.None));
        controller.Acknowledge(catchUp);
        controller.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => controller.WaitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task LaterGameCatchUpBeyondTheCompletedResumedPeriodRemainsPending()
    {
        var controller = new ResidentSyncController();
        var morning = RequestAt("2026-10-12T08:00:00+08:00");
        controller.Request(morning);
        Assert.Equal(morning, await controller.WaitAsync(CancellationToken.None));
        controller.Acknowledge(morning, includeCatchUpRequests: true,
            completedAt: DateTimeOffset.Parse("2026-10-12T21:00:00+08:00"));
        var nextDay = RequestAt("2026-10-13T08:00:00+08:00", SyncRequestKind.CatchUp);
        controller.Request(nextDay);
        Assert.Equal(nextDay, await controller.WaitAsync(CancellationToken.None));
        controller.Acknowledge(nextDay);
        controller.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => controller.WaitAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public void ControllerWithoutGameDetectorPreservesIndependentCallerBehavior()
    {
        var controller = new ResidentSyncController();
        Assert.True(controller.CanSync);
        Assert.False(controller.GameActive);
    }

    [Fact]
    public void ProcessDetectorNormalizesExeNames()
    {
        var detector = new ProcessGameActivityDetector(["GameA.exe", " gameb "]);
        Assert.Equal(["gamea", "gameb"], detector.ProcessNames.OrderBy(name => name));
    }

    private static SyncRequest RequestAt(string instant, SyncRequestKind kind = SyncRequestKind.Scheduled)
    {
        var slot = DateTimeOffset.Parse(instant);
        return new(kind, slot, slot.AddSeconds(1));
    }

    private sealed class FakeGameDetector : IGameActivityDetector
    {
        public bool Active { get; set; }
        public bool IsGameRunning() => Active;
    }
}
