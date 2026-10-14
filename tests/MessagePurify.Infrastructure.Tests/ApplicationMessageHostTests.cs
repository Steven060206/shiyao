using System.Diagnostics;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class ApplicationMessageHostTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string First = "9007199254740995";
    private const string Second = "9007199254740996";
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-host-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunOnceReadsOnlyPinnedSevenDayHistoryAndClosesConnection()
    {
        await using var server = new LoopbackOneBotServer
        {
            GroupCount = 2, EmitHistoryEvents = false,
            PinnedResponder = (_, _) => (0, Pins(First)),
            HistoryResponder = (_, _) => (0, BoundaryHistory(Message("within-seven-days")))
        };
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var running = host.RunOnceAsync(cancellation.Token);
        try
        {
            await running.WaitAsync(TimeSpan.FromSeconds(2));
            await UntilAsync(() => !server.HasActiveConnection);
            Assert.Equal("complete", host.Status.History);
            Assert.Equal("disconnected", host.Status.Transport);
            Assert.NotNull(host.Status.LastSuccessfulSyncAt);
            Assert.Equal("task", Assert.Single(workspace.Snapshot(DateTimeOffset.UtcNow).Items).Kind);
            Assert.Equal(1, StoredCount());
            Assert.Equal(First, Assert.Single(server.HistoryParameters).GetProperty("group_id").GetString());
            Assert.Equal(1, server.ConnectionCount);
            Assert.DoesNotContain(server.Actions, action => action.StartsWith("send", StringComparison.Ordinal));
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData("active")]
    [InlineData("completed")]
    [InlineData("archived")]
    public async Task ReopenedRunOnceSkipsIgnoredMessagesAndPreservesAcceptedTasks(string state)
    {
        var ignoredMessage = Message("ignored", text: "10月20日提交忽略作业");
        var acceptedMessage = Message("accepted", text: "10月21日提交保留作业");
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            HistoryResponder = (_, _) => (0, BoundaryHistory(ignoredMessage, acceptedMessage))
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var workspace = Workspace();
        await new NapCatMessageHost(workspace, server.Url).RunOnceAsync(cancellation.Token);
        var now = DateTimeOffset.UtcNow;
        var reviews = workspace.Snapshot(now).Items;
        var ignored = Assert.Single(reviews, item => item.Title.Contains("忽略作业", StringComparison.Ordinal));
        var accepted = Assert.Single(reviews, item => item.Title.Contains("保留作业", StringComparison.Ordinal));
        workspace.Execute(new(Account, "ignore", ignored.Id, "ignore", ignored.Version), now);
        workspace.Execute(new(Account, "accept-review", accepted.Id, "accept", accepted.Version,
            Date: "2026-11-01"), now);
        var task = Assert.Single(workspace.Snapshot(now).Items, item => item.Kind == "task");
        workspace.Execute(new(Account, "edit-task", task.Id, "edit", task.Version,
            Date: "2026-11-02", TimeKind: "deadline", Course: "手动课程", Title: "手动保留标题", Priority: 2), now);
        task = Assert.Single(workspace.Snapshot(now).Items, item => item.Kind == "task");
        if (state != "active")
            workspace.Execute(new(Account, state == "completed" ? "complete" : "archive",
                task.Id, "set-state", task.Version), now);
        var store = new AccountTaskStore(root, Account);
        var savedTask = JsonSerializer.Serialize(store.FindTask(task.Id));
        var savedIgnored = JsonSerializer.Serialize(store.FindReview(ignored.Id));
        var savedAccepted = JsonSerializer.Serialize(store.FindReview(accepted.Id));
        var savedHistory = JsonSerializer.Serialize(store.ListTaskHistory());

        for (var replay = 0; replay < 2; replay++)
        {
            if (replay == 1)
                server.HistoryResponder = (_, _) => (0, BoundaryHistory(ignoredMessage, acceptedMessage,
                    Message("new", text: "10月22日提交新增作业")));
            workspace = Workspace();
            var host = new NapCatMessageHost(workspace, server.Url);
            await host.RunOnceAsync(cancellation.Token);
            Assert.Equal("complete", host.Status.History);
            var items = workspace.Snapshot(now).Items;
            Assert.Equal(state, Assert.Single(items, item => item.Kind == "task").State);
            Assert.Equal("ignored", Assert.Single(items, item => item.Id == ignored.Id).State);
            Assert.DoesNotContain(items, item => item.Id == accepted.Id);
            Assert.Equal(replay, items.Count(item => item.Kind == "review" && item.State == "pending"));
            Assert.Equal(2 + replay, StoredCount());
            store = new(root, Account);
            Assert.Equal(savedTask, JsonSerializer.Serialize(store.FindTask(task.Id)));
            Assert.Equal(savedIgnored, JsonSerializer.Serialize(store.FindReview(ignored.Id)));
            Assert.Equal(savedAccepted, JsonSerializer.Serialize(store.FindReview(accepted.Id)));
            Assert.Equal(savedHistory, JsonSerializer.Serialize(store.ListTaskHistory()));
        }
        Assert.Equal(3, server.ConnectionCount);
        Assert.DoesNotContain(server.Actions, action => action.StartsWith("send", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunOnceReportsPartialHistoryWithoutConsumingScheduledRequests()
    {
        await using var server = new LoopbackOneBotServer
        { EmitHistoryEvents = false, HistoryResponder = (_, _) => (1200, null) };
        var controller = new ResidentSyncController();
        var request = new SyncRequest(SyncRequestKind.Scheduled, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        controller.Request(request);
        var host = new NapCatMessageHost(Workspace(), server.Url, syncController: controller);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await host.RunOnceAsync(cancellation.Token);

        Assert.Equal("partial", host.Status.History);
        Assert.Equal("api-failed-1200", Assert.Single(host.Status.HistoryFailures).Reason);
        Assert.Null(host.Status.LastSuccessfulSyncAt);
        Assert.Equal(request, await controller.WaitAsync(cancellation.Token));
        Assert.Equal(1, server.ConnectionCount);
        Assert.Single(server.HistoryParameters);
        Assert.Equal(0, StoredCount());
    }

    [Theory]
    [InlineData("account", "account-mismatch")]
    [InlineData("offline", "qq-offline")]
    [InlineData("pins", "pinned-state-unavailable")]
    public async Task RunOncePropagatesInitialConnectionFailuresWithoutRetry(string scenario, string expected)
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            Online = scenario != "offline",
            AccountForConnection = _ => scenario == "account" ? "123456" : Account,
            PinnedResponder = (_, _) => scenario == "pins" ? (1200, null) : (0, Pins(First))
        };
        var host = new NapCatMessageHost(Workspace(), server.Url);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<OneBotProtocolException>(() => host.RunOnceAsync(cancellation.Token));

        Assert.Equal(expected, error.Code);
        Assert.Equal(expected, host.Status.Error);
        Assert.Equal("disconnected", host.Status.Transport);
        Assert.Equal(1, server.ConnectionCount);
        Assert.Empty(server.HistoryParameters);
    }

    [Fact]
    public async Task RunOncePropagatesMalformedHistoryProtocolInsteadOfReportingCompletedBatch()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            HistoryResponder = (_, _) => (0, new { messages = new[]
            { new { message_type = "group", self_id = Account, group_id = First, message_id = "broken" } } })
        };
        var host = new NapCatMessageHost(Workspace(), server.Url);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<OneBotProtocolException>(() => host.RunOnceAsync(cancellation.Token));

        Assert.Equal("missing-field", error.Code);
        Assert.Equal("missing-field", host.Status.Error);
        Assert.Null(host.Status.LastSuccessfulSyncAt);
        Assert.Equal(0, StoredCount());
        Assert.Equal(1, server.ConnectionCount);
    }

    [Theory]
    [InlineData(true, true, "game-active")]
    [InlineData(false, false, "game-catalog-unavailable")]
    public async Task RunOnceBlockedByGameDoesNotOpenConnection(bool active, bool ready, string expected)
    {
        await using var server = new LoopbackOneBotServer();
        var detector = new MutableGameDetector { Active = active, IsReady = ready };
        var host = new NapCatMessageHost(Workspace(), server.Url, syncController: new(detector));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<OneBotProtocolException>(() => host.RunOnceAsync(cancellation.Token));

        Assert.Equal(expected, error.Code);
        Assert.Equal(expected, host.Status.Error);
        Assert.Equal(0, server.ConnectionCount);
        Assert.Empty(server.Actions);
    }

    [Fact]
    public async Task RunOnceCancelsInFlightHistoryWhenGameStartsWithoutSavingPage()
    {
        var detector = new MutableGameDetector();
        var responseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false, HistoryResponseGate = responseGate.Task,
            HistoryResponder = (_, _) =>
            {
                detector.Active = true;
                return (0, BoundaryHistory(Message("must-not-save")));
            }
        };
        var host = new NapCatMessageHost(Workspace(), server.Url, syncController: new(detector));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<OneBotProtocolException>(() =>
            host.RunOnceAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Equal("game-active", error.Code);
        Assert.Equal("game-active", host.Status.Error);
        Assert.Null(host.Status.LastSuccessfulSyncAt);
        Assert.Equal(0, StoredCount());
        Assert.Single(server.HistoryParameters);
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public void HostStartsPausedWithoutFabricatedConnection()
    {
        var host = new NapCatMessageHost(Workspace(), new("ws://127.0.0.1:3001/"));
        Assert.Equal("disconnected", host.Status.Transport);
        Assert.Equal("unknown", host.Status.Qq);
        Assert.Equal("paused", host.Status.Receiving);
        Assert.Equal("pending", host.Status.History);
        Assert.False(host.Status.Scheduled);
        Assert.Null(host.Status.LastSuccessfulSyncAt);
        Assert.Null(host.Status.NextScheduledSyncAt);
    }

    [Fact]
    public async Task CompletedBatchAutomaticallyCleansOldTemporaryMessages()
    {
        await using var server = new LoopbackOneBotServer
        { EmitHistoryEvents = false, HistoryResponder = (_, _) => (0, BoundaryHistory()) };
        var workspace = Workspace();
        var old = new SchoolMessage(new(Account, First, "old-chat"), "teacher", DateTimeOffset.UtcNow.AddDays(-10),
            [new(SegmentKind.Text, "大家早上好")]);
        workspace.Ingest(old, new HashSet<string> { First }, MessageOrigin.History, "prior", DateTimeOffset.UtcNow);
        var host = new NapCatMessageHost(workspace, server.Url);

        await ObserveAsync(host, () => host.Status.History == "complete");

        Assert.Null(Messages().Find(old.Key));
        Assert.Equal(1, JsonSerializer.SerializeToElement(workspace.Snapshot(DateTimeOffset.UtcNow))
            .GetProperty("CacheCleanup").GetProperty("DeletedMessages").GetInt32());
        Assert.NotNull(host.Status.LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task CleanupFailureIsSeparateFromSuccessfulHistorySync()
    {
        await using var server = new LoopbackOneBotServer
        { EmitHistoryEvents = false, HistoryResponder = (_, _) => (0, BoundaryHistory()) };
        var workspace = Workspace();
        var old = new SchoolMessage(new(Account, First, "old-chat"), "teacher", DateTimeOffset.UtcNow.AddDays(-10),
            [new(SegmentKind.Text, "大家早上好")]);
        workspace.Ingest(old, new HashSet<string> { First }, MessageOrigin.History, "prior", DateTimeOffset.UtcNow);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Messages().DatabasePath }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE messages SET message_json = 'invalid-json' WHERE message_id = 'old-chat';";
            command.ExecuteNonQuery();
        }
        var host = new NapCatMessageHost(workspace, server.Url);

        var observed = await ObserveAsync(host, () => host.Status.History == "complete");

        Assert.Equal("connected", observed.Transport);
        Assert.NotNull(observed.LastSuccessfulSyncAt);
        Assert.Null(observed.Error);
        Assert.False(string.IsNullOrWhiteSpace(JsonSerializer.SerializeToElement(workspace.Snapshot(DateTimeOffset.UtcNow))
            .GetProperty("CacheCleanupError").GetString()));
        Assert.Equal(1, StoredCount());
    }

    [Fact]
    public async Task GamePauseDefersCleanupUntilResumedBatchCompletes()
    {
        await using var server = new LoopbackOneBotServer
        { EmitHistoryEvents = false, HistoryResponder = (_, _) => (0, BoundaryHistory()) };
        var workspace = Workspace();
        var old = new SchoolMessage(new(Account, First, "old-chat"), "teacher", DateTimeOffset.UtcNow.AddDays(-10),
            [new(SegmentKind.Text, "大家早上好")]);
        workspace.Ingest(old, new HashSet<string> { First }, MessageOrigin.History, "prior", DateTimeOffset.UtcNow);
        var detector = new MutableGameDetector { Active = true };
        var controller = new ResidentSyncController(detector);
        controller.Request(new(SyncRequestKind.Scheduled, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var host = new NapCatMessageHost(workspace, server.Url, syncController: controller);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            await UntilAsync(() => host.Status.Transport == "connected" && host.Status.Receiving == "paused");
            Assert.NotNull(Messages().Find(old.Key));
            Assert.Empty(server.HistoryParameters);
            detector.Active = false;
            await UntilAsync(() => host.Status.History == "complete");
            Assert.Null(Messages().Find(old.Key));
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ResidentHostKeepsConnectionIdleUntilScheduledRequestThenRunsOneBatch()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            HistoryResponder = (_, _) => (0, BoundaryHistory(Message("scheduled")))
        };
        var controller = new ResidentSyncController();
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url, syncController: controller);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            await UntilAsync(() => host.Status.Transport == "connected" && host.Status.Receiving == "paused");
            Assert.Empty(server.HistoryParameters);
            Assert.True(host.Status.Scheduled);
            Assert.Equal("pending", host.Status.History);
            Assert.Null(host.Status.LastSuccessfulSyncAt);
            Assert.True(host.Status.NextScheduledSyncAt > host.Status.UpdatedAt);
            var startedAt = DateTimeOffset.UtcNow;
            controller.Request(new(SyncRequestKind.Scheduled,
                DateTimeOffset.Parse("2026-10-12T08:00:00+08:00"), DateTimeOffset.UtcNow));
            await UntilAsync(() => host.Status.History == "complete" && host.Status.Receiving == "paused"
                && server.HistoryParameters.Count == 1);
            Assert.Equal("task", Assert.Single(workspace.Snapshot(DateTimeOffset.UtcNow).Items).Kind);
            Assert.Equal("paused", host.Status.Receiving);
            Assert.InRange(host.Status.LastSuccessfulSyncAt!.Value, startedAt, DateTimeOffset.UtcNow);
            Assert.Equal(host.Status.UpdatedAt, host.Status.LastSuccessfulSyncAt);
        }
        finally
        {
            cancellation.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task FailedBatchesNeverFabricateOrReplaceLastSuccessfulSyncTime()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            HistoryResponder = (_, read) => read == 2 ? (0, BoundaryHistory(Message("success"))) : (1200, null)
        };
        var controller = new ResidentSyncController();
        var host = new NapCatMessageHost(Workspace(confirmed: true), server.Url, syncController: controller);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            for (var batch = 1; batch <= 3; batch++)
            {
                controller.Request(new(SyncRequestKind.Scheduled, DateTimeOffset.UtcNow.AddSeconds(batch), DateTimeOffset.UtcNow));
                await UntilAsync(() => server.HistoryParameters.Count == batch && host.Status.Receiving == "paused"
                    && host.Status.History == (batch == 2 ? "complete" : "partial"));
                if (batch == 1) Assert.Null(host.Status.LastSuccessfulSyncAt);
                if (batch == 2) Assert.NotNull(host.Status.LastSuccessfulSyncAt);
            }
            var successfulAt = host.Status.LastSuccessfulSyncAt;
            Assert.NotNull(successfulAt);
            Assert.True(host.Status.UpdatedAt > successfulAt);
            Assert.Equal("api-failed-1200", Assert.Single(host.Status.HistoryFailures).Reason);
            Assert.Null(new NapCatMessageHost(Workspace(), server.Url, syncController: new()).Status.LastSuccessfulSyncAt);
            server.Disconnect();
            await UntilAsync(() => server.ConnectionCount == 2 && host.Status.Transport == "connected");
            Assert.Equal(successfulAt, host.Status.LastSuccessfulSyncAt);
            Assert.Equal("partial", host.Status.History);
            Assert.Single(host.Status.HistoryFailures);
            Assert.Equal(3, server.HistoryParameters.Count);
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task IdleReconnectAndGamePauseKeepSuccessTimeWithoutStartingAnotherBatch()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false, HistoryResponder = (_, _) => (0, BoundaryHistory())
        };
        var detector = new MutableGameDetector();
        var controller = new ResidentSyncController(detector);
        var host = new NapCatMessageHost(Workspace(), server.Url, syncController: controller);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            controller.Request(new(SyncRequestKind.Startup, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            await UntilAsync(() => host.Status.LastSuccessfulSyncAt is not null && host.Status.Receiving == "paused");
            var successfulAt = host.Status.LastSuccessfulSyncAt;
            detector.Active = true;
            await UntilAsync(() => host.Status.Error == "game-active");
            Assert.Equal(successfulAt, host.Status.LastSuccessfulSyncAt);
            detector.Active = false;
            await UntilAsync(() => host.Status.Error is null);
            Assert.Equal(successfulAt, host.Status.LastSuccessfulSyncAt);
            Assert.Equal("complete", host.Status.History);
            Assert.True(host.Status.UpdatedAt > successfulAt);
            server.Disconnect();
            await UntilAsync(() => server.ConnectionCount == 2 && host.Status.Transport == "connected");
            Assert.Equal(successfulAt, host.Status.LastSuccessfulSyncAt);
            Assert.Equal("complete", host.Status.History);
            Assert.Single(server.HistoryParameters);
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ResidentHostDefersAlreadyQueuedRequestsDuringGameAndMergesThemOnResume()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            HistoryResponder = (_, _) => (0, BoundaryHistory(Message("after-game")))
        };
        var detector = new MutableGameDetector { Active = true };
        var controller = new ResidentSyncController(detector);
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url, syncController: controller);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            foreach (var hour in new[] { 8, 12, 20 })
                controller.Request(new(SyncRequestKind.Scheduled,
                    new DateTimeOffset(2026, 10, 12, hour, 0, 0, TimeSpan.FromHours(8)), DateTimeOffset.UtcNow));
            await UntilAsync(() => host.Status.Error == "game-active");
            Assert.Null(host.Status.LastSuccessfulSyncAt);
            Assert.Empty(server.HistoryParameters);
            Assert.Equal(0, StoredCount());
            detector.Active = false;
            await UntilAsync(() => host.Status.History == "complete" && host.Status.Receiving == "paused"
                && workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count == 1);
            Assert.Single(server.HistoryParameters);
            Assert.Equal(1, server.ConnectionCount);
            Assert.NotNull(host.Status.LastSuccessfulSyncAt);
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ResidentHostDiscardsInflightPageWhenGameStartsAndRecoversNewestHistoryAfterGame()
    {
        var detector = new MutableGameDetector();
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = true,
            HistoryResponder = (_, read) =>
            {
                if (read == 1) detector.Active = true;
                return (0, BoundaryHistory(Message(read == 1 ? "inflight" : "new-after-game")));
            }
        };
        var controller = new ResidentSyncController(detector);
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url, syncController: controller);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            controller.Request(new(SyncRequestKind.Scheduled, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            await UntilAsync(() => host.Status.Error == "game-active");
            Assert.Null(host.Status.LastSuccessfulSyncAt);
            Assert.Single(server.HistoryParameters);
            Assert.Equal(0, StoredCount());
            Assert.Empty(workspace.Snapshot(DateTimeOffset.UtcNow).Items);
            detector.Active = false;
            await UntilAsync(() => host.Status.History == "complete" && host.Status.Receiving == "paused");
            Assert.Equal(2, server.HistoryParameters.Count);
            Assert.Equal(1, StoredCount());
            Assert.Single(workspace.Snapshot(DateTimeOffset.UtcNow).Items);
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ResidentHostReconnectsUnfinishedBatchWithoutAnotherScheduledRequest()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            PinnedResponder = (connection, read) => connection == 1 && read >= 4
                ? (1200, null) : (0, Pins(First)),
            HistoryResponder = (_, _) => (0, BoundaryHistory(Message("recovered")))
        };
        var controller = new ResidentSyncController();
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url, syncController: controller);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            controller.Request(new(SyncRequestKind.Scheduled, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            await UntilAsync(() => server.ConnectionCount >= 2 && host.Status.History == "complete"
                && workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count == 1);
            Assert.Equal(2, server.HistoryParameters.Count);
            Assert.Equal(1, StoredCount());
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public void PendingRoutingStopsBetweenMessagesAndCanResumeWithoutLosingRawMessages()
    {
        var workspace = Workspace(confirmed: true);
        var pins = new HashSet<string> { First };
        var now = DateTimeOffset.UtcNow;
        foreach (var id in new[] { "one", "two" })
        {
            var packet = JsonSerializer.SerializeToElement(Message(id, text: $"10月13日18点提交{id}作业"));
            workspace.Enqueue(OneBotMessageParser.ParseGroupMessage(packet, Account, First).Message,
                pins, MessageOrigin.History, "synthetic", now);
        }
        Assert.Equal(1, workspace.ProcessPending(pins, now, () => workspace.Snapshot(now).Items.Count == 0));
        Assert.Single(workspace.Snapshot(now).Items);
        Assert.Equal(2, StoredCount());
        Assert.Equal(1, workspace.ProcessPending(pins, now));
        Assert.Equal(2, workspace.Snapshot(now).Items.Count);
    }

    private sealed class MutableGameDetector : IGameActivityDetector
    {
        private int active;
        public bool IsReady { get; set; } = true;
        public bool Active { get => Volatile.Read(ref active) == 1; set => Volatile.Write(ref active, value ? 1 : 0); }
        public bool IsGameRunning() => Active;
    }

    [Fact]
    public async Task ConfirmedCoursePersistsOneTaskAcrossHistoryAndRealtimeDuplicates()
    {
        var message = Message("task");
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false, EventsOnFirstPinnedRead = [message, message],
            HistoryResponder = (_, _) => (0, BoundaryHistory(message))
        };
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url, "synthetic-test-token");
        var observed = await ObserveAsync(host, () => host.Status.History == "complete"
            && workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count == 1);
        Assert.Equal("connected", observed.Transport);
        Assert.Equal("active", observed.Receiving);
        var task = Assert.Single(Workspace().Snapshot(DateTimeOffset.UtcNow).Items);
        Assert.Equal("task", task.Kind);
        Assert.Equal("数学", task.Course);
        Assert.Contains("提交统计作业", Assert.Single(task.Evidence).Text);
        Assert.Equal(1, StoredCount());
        Assert.Equal("Bearer synthetic-test-token", server.ObservedAuthorization);
        Assert.DoesNotContain(server.Actions, action => action.StartsWith("send", StringComparison.Ordinal));
        Assert.Equal(100, Assert.Single(server.HistoryParameters).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task UnknownCoursePreservesReviewAndSourceBody()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false, HistoryResponder = (_, _) => (0, BoundaryHistory(Message("unknown")))
        };
        var workspace = Workspace();
        var host = new NapCatMessageHost(workspace, server.Url);
        await ObserveAsync(host, () => host.Status.History == "complete");
        var review = Assert.Single(workspace.Snapshot(DateTimeOffset.UtcNow).Items);
        Assert.Equal("review", review.Kind);
        Assert.Equal("pending", review.State);
        Assert.Contains("提交统计作业", Assert.Single(review.Evidence).Text);
    }

    [Fact]
    public async Task MalformedPrivateUnknownAndUnpinnedBodiesNeverReachParserOrStorage()
    {
        await using var server = new LoopbackOneBotServer
        {
            GroupCount = 2, EmitHistoryEvents = false,
            PinnedResponder = (_, _) => (0, Pins(First)),
            EventsOnFirstPinnedRead =
            [
                new { post_type = "message", message_type = "private", self_id = Account, message = "malformed-private" },
                new { post_type = "message", message_type = "unsupported", self_id = Account, message = "malformed-unknown" },
                new { post_type = "message", message_type = "group", self_id = Account, group_id = Second, message = "malformed-unpinned" }
            ],
            HistoryResponder = (_, _) => (0, BoundaryHistory())
        };
        var host = new NapCatMessageHost(Workspace(), server.Url);
        var observed = await ObserveAsync(host, () => host.Status.History == "complete" && server.PinnedReads >= 5);
        Assert.Null(observed.Error);
        Assert.Equal("connected", observed.Transport);
        Assert.Equal(0, StoredCount());
        Assert.Equal(First, Assert.Single(server.HistoryParameters).GetProperty("group_id").GetString());
    }

    [Theory]
    [InlineData(1404, false, "pinned-state-unavailable")]
    [InlineData(0, true, "pinned-state-invalid")]
    public async Task UnavailablePinnedStatePausesBeforeAnyHistoryOrBody(int retcode, bool malformed, string error)
    {
        await using var server = new LoopbackOneBotServer
        {
            GroupCount = 2, EmitHistoryEvents = false,
            PinnedResponder = (_, _) => (retcode, malformed ? new { self_id = Account } : null),
            EventsOnFirstPinnedRead = [Message("should-not-save")]
        };
        var host = new NapCatMessageHost(Workspace(), server.Url);
        var observed = await ObserveAsync(host, () => host.Status.Error == error);
        Assert.Equal("paused", observed.Receiving);
        Assert.Empty(server.HistoryParameters);
        Assert.Equal(0, StoredCount());
    }

    [Theory]
    [InlineData("login")]
    [InlineData("pins")]
    [InlineData("event")]
    [InlineData("history")]
    public async Task AccountMismatchStopsCurrentConnectionWithoutSwitching(string source)
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            AccountForConnection = source == "login" ? _ => "123456" : null,
            PinnedResponder = source == "pins" ? (_, _) => (0, new { self_id = "123456", group_ids = new[] { new { group_id = First } } }) : null,
            EventsOnFirstPinnedRead = source == "event" ? [Message("wrong-account", account: "123456")] : [],
            HistoryResponder = (_, _) => (0, BoundaryHistory(source == "history" ? Message("wrong-account", account: "123456") : Message("correct")))
        };
        var workspace = Workspace();
        var host = new NapCatMessageHost(workspace, server.Url);
        var observed = await ObserveAsync(host, () => host.Status.Error == "account-mismatch");
        Assert.Equal("paused", observed.Receiving);
        Assert.Equal(Account, workspace.AccountId);
        Assert.Empty(workspace.Snapshot(DateTimeOffset.UtcNow).Items);
        if (source is "login" or "pins" or "history") Assert.Equal(0, StoredCount());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnpinningInFlightExcludesMalformedHistoryAndRealtimeBody(bool realtime)
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            PinnedResponder = (_, read) => (0, read <= (realtime ? 1 : 2) ? Pins(First) : Pins()),
            EventsOnFirstPinnedRead = realtime ? [new { post_type = "message", message_type = "group", self_id = Account, group_id = First }] : [],
            HistoryResponder = (_, _) => (0, new { messages = new[] { new { message_type = "group", group_id = First, message = "bad" } } })
        };
        var workspace = Workspace();
        var host = new NapCatMessageHost(workspace, server.Url);
        var observed = await ObserveAsync(host, () => host.Status.History == "partial");
        Assert.Null(observed.Error);
        Assert.Equal("unpinned", Assert.Single(observed.HistoryFailures).Reason);
        Assert.False(Assert.Single(workspace.Snapshot(DateTimeOffset.UtcNow).Groups).Pinned);
        Assert.Equal(0, StoredCount());
    }

    [Fact]
    public async Task PeriodicRefreshFindsNewPinnedGroupWithoutEvents()
    {
        var pinnedSecond = false;
        await using var server = new LoopbackOneBotServer
        {
            GroupCount = 2, EmitHistoryEvents = false,
            PinnedResponder = (_, _) => (0, Volatile.Read(ref pinnedSecond) ? Pins(First, Second) : Pins(First)),
            HistoryResponder = (parameters, _) => (0, new { messages = new[]
            {
                Message("task-" + parameters.GetProperty("group_id").GetString(), group: parameters.GetProperty("group_id").GetString()!),
                Message("boundary", group: parameters.GetProperty("group_id").GetString()!, ago: TimeSpan.FromDays(10))
            } })
        };
        var workspace = Workspace();
        var host = new NapCatMessageHost(workspace, server.Url);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            await UntilAsync(() => host.Status.History == "complete" && workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count == 1);
            Volatile.Write(ref pinnedSecond, true);
            await UntilAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count == 2 && host.Status.History == "complete");
            Assert.True(workspace.Snapshot(DateTimeOffset.UtcNow).Groups.Single(group => group.GroupId == Second).Pinned);
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal([First, Second], server.HistoryParameters.Select(item => item.GetProperty("group_id").GetString()));
        Assert.True(server.Actions.Count(action => action == "get_login_info") >= 2);
    }

    [Fact]
    public async Task HistoryFailureRetainsPriorPageAndReportsGroupReason()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            HistoryResponder = (_, request) => request == 1
                ? (0, new { messages = new[] { Message("partial") } }) : (1200, null)
        };
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url);
        var observed = await ObserveAsync(host, () => host.Status.History == "partial");
        Assert.Equal(new HistoryFailure(First, "api-failed-1200"), Assert.Single(observed.HistoryFailures));
        Assert.Equal("task", Assert.Single(workspace.Snapshot(DateTimeOffset.UtcNow).Items).Kind);
        Assert.NotNull(Messages().Find(new(Account, First, "partial")));
        Assert.Equal(2, server.HistoryParameters.Count);
        Assert.True(server.HistoryParameters[1].GetProperty("reverse_order").GetBoolean());
    }

    [Fact]
    public async Task LosingPinnedStateMidHistoryRetainsRawPageWithoutRoutingOldScope()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            PinnedResponder = (_, read) => read <= 3 ? (0, Pins(First)) : (1200, null),
            HistoryResponder = (_, _) => (0, new { messages = new[] { Message("durable-raw") } })
        };
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url);
        var observed = await ObserveAsync(host, () => host.Status.Error == "pinned-state-unavailable");
        Assert.Equal("paused", observed.Receiving);
        Assert.Equal(new HistoryFailure(First, "pinned-state-unavailable"), Assert.Single(observed.HistoryFailures));
        Assert.Empty(workspace.Snapshot(DateTimeOffset.UtcNow).Items);
        Assert.Null(Messages().Find(new(Account, First, "durable-raw"))!.Extraction);
        Assert.Single(server.HistoryParameters);
    }

    [Fact]
    public async Task PeriodicLoginCheckRejectsAccountChangeWithoutIncomingEvents()
    {
        var changeAccount = false;
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            AccountForConnection = _ => Volatile.Read(ref changeAccount) ? "123456" : Account,
            HistoryResponder = (_, _) => (0, BoundaryHistory())
        };
        var workspace = Workspace();
        var host = new NapCatMessageHost(workspace, server.Url);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            await UntilAsync(() => host.Status.History == "complete");
            Volatile.Write(ref changeAccount, true);
            await UntilAsync(() => host.Status.Error == "account-mismatch");
            Assert.Equal("paused", host.Status.Receiving);
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(Account, workspace.AccountId);
        Assert.Equal(0, StoredCount());
    }

    [Fact]
    public async Task HistoryPageCapReportsIncompleteAndPreservesAllReturnedRecords()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            HistoryResponder = (_, request) => (0, new
            { messages = new[] { Message("page-" + request, text: "普通闲聊", ago: TimeSpan.FromSeconds(request)) } })
        };
        var host = new NapCatMessageHost(Workspace(), server.Url);
        var observed = await ObserveAsync(host, () => host.Status.History == "partial");
        Assert.Equal("history-page-limit", Assert.Single(observed.HistoryFailures).Reason);
        Assert.Equal(100, server.HistoryParameters.Count);
        Assert.Equal(100, StoredCount());
    }

    [Theory]
    [InlineData("empty", "history-empty-page")]
    [InlineData("repeat", "history-no-progress")]
    [InlineData("cursor", "history-cursor-missing")]
    public async Task IncompleteHistoryNeverClaimsDateBoundary(string scenario, string reason)
    {
        var missingCursor = JsonSerializer.SerializeToElement(new
        {
            message_type = "group", self_id = Account, group_id = First, message_id = "cursorless", user_id = "teacher",
            time = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            message = new[] { new { type = "text", data = new { text = "10月13日提交作业" } } }
        });
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            HistoryResponder = (_, _) => (0, new { messages = scenario switch
            {
                "empty" => Array.Empty<object>(), "cursor" => [missingCursor], _ => [Message("repeated")]
            } })
        };
        var host = new NapCatMessageHost(Workspace(), server.Url);
        var observed = await ObserveAsync(host, () => host.Status.History == "partial");
        Assert.Equal(reason, Assert.Single(observed.HistoryFailures).Reason);
        Assert.Null(observed.Error);
    }

    [Fact]
    public async Task ReconnectRetriesHistoryAndDoesNotCreateDuplicateTasks()
    {
        var failFirstConnection = false;
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            PinnedResponder = (connection, _) => connection == 1 && Volatile.Read(ref failFirstConnection)
                ? (1200, null) : (0, Pins(First)),
            HistoryResponder = (_, _) => (0, BoundaryHistory(Message("same-task")))
        };
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try
        {
            await UntilAsync(() => host.Status.History == "complete");
            Volatile.Write(ref failFirstConnection, true);
            await UntilAsync(() => server.ConnectionCount >= 2 && host.Status.History == "complete");
        }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(2, server.ConnectionCount);
        Assert.Equal(2, server.HistoryParameters.Count);
        Assert.Equal("task", Assert.Single(workspace.Snapshot(DateTimeOffset.UtcNow).Items).Kind);
        Assert.Equal(1, StoredCount());
    }

    [Fact]
    public async Task HistoricalPagesRouteInOriginalOrderBeforeNewerRealtimeChange()
    {
        var old = Message("original", text: "10月13日18点提交统计作业", ago: TimeSpan.FromDays(2));
        var changed = Message("change", text: "统计作业取消", ago: TimeSpan.FromDays(1), quote: "original");
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false, EventsOnFirstPinnedRead = [changed],
            HistoryResponder = (_, request) => request == 1
                ? (0, new { messages = new[] { changed } }) : (0, BoundaryHistory(old))
        };
        var workspace = Workspace(confirmed: true);
        var host = new NapCatMessageHost(workspace, server.Url);
        await ObserveAsync(host, () => host.Status.History == "complete");
        var items = workspace.Snapshot(DateTimeOffset.UtcNow).Items;
        var task = Assert.Single(items, item => item.Kind == "task");
        var review = Assert.Single(items, item => item.Kind == "review");
        Assert.Equal(task.Id, review.TargetTaskId);
        Assert.Equal(2, StoredCount());
    }

    [Fact]
    public async Task OfflineQqAndCancelledReconnectDrainPromptly()
    {
        await using var server = new LoopbackOneBotServer { Online = false, EmitHistoryEvents = false };
        var host = new NapCatMessageHost(Workspace(), server.Url);
        var observed = await ObserveAsync(host, () => host.Status.Error == "qq-offline");
        Assert.Equal("offline", observed.Qq);
        Assert.Equal("paused", host.Status.Receiving);
        Assert.Equal("disconnected", host.Status.Transport);
        Assert.Empty(server.HistoryParameters);
        // A later run can start after the cancelled run has drained.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await host.RunAsync(cancelled.Token);
    }

    [Fact]
    public async Task CancellationDuringLongHistoryReadStopsAllLoops()
    {
        using var cancellation = new CancellationTokenSource();
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            HistoryResponder = (_, _) => { cancellation.Cancel(); return (0, BoundaryHistory()); }
        };
        var host = new NapCatMessageHost(Workspace(), server.Url);
        await host.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("disconnected", host.Status.Transport);
        Assert.Equal("paused", host.Status.Receiving);
        Assert.Single(server.HistoryParameters);
    }

    private AccountMessageStore Messages() => new(root, Account);
    private AccountWorkspace Workspace(bool confirmed = false)
    {
        var workspace = new AccountWorkspace(root, Account);
        if (confirmed)
        {
            workspace.SetAvailableGroups([new(First, "数学课程群")], new HashSet<string> { First });
            workspace.Execute(new(Account, "confirm-group", First, Guid.NewGuid().ToString("N"), 0, Course: "数学"), DateTimeOffset.UtcNow);
        }
        return workspace;
    }

    private long StoredCount()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Messages().DatabasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM messages;";
        return (long)command.ExecuteScalar()!;
    }

    private static object Message(string id, string group = First, string account = Account,
        string text = "10月13日18点提交统计作业", TimeSpan? ago = null, string? quote = null)
    {
        var segments = new List<object> { new { type = "text", data = new { text } } };
        if (quote is not null) segments.Add(new { type = "reply", data = new { id = quote } });
        return new
        {
            post_type = "message", message_type = "group", self_id = account, group_id = group,
            message_id = id, message_seq = id, user_id = "teacher", time = DateTimeOffset.UtcNow.Subtract(ago ?? TimeSpan.FromDays(1)).ToUnixTimeSeconds(),
            message = segments.ToArray()
        };
    }

    private static object BoundaryHistory(params object[] messages) => new
    { messages = messages.Append(Message("boundary", ago: TimeSpan.FromDays(10))).ToArray() };
    private static object Pins(params string[] ids) => new
    { self_id = Account, group_ids = ids.Select(group_id => new { group_id }).ToArray() };

    private static async Task<MessageHostStatus> ObserveAsync(NapCatMessageHost host, Func<bool> complete)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = host.RunAsync(cancellation.Token);
        try { await UntilAsync(complete); return host.Status; }
        finally { cancellation.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Host did not reach the expected observable state.");
            await Task.Delay(20);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
