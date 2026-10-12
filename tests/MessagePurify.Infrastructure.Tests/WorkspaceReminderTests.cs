using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Reminders;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class WorkspaceReminderTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string Group = "9007199254740995";
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-reminder-workspace-" + Guid.NewGuid().ToString("N"));
    private AccountWorkspace Workspace(string account = Account) => new(root, account);
    private AccountTaskStore Tasks(string account = Account) => new(root, account);
    private static TaskTime Exact(int day = 13, int hour = 18) =>
        new(TimeKind.Deadline, new(2026, 10, day), new(hour, 0), "Asia/Shanghai");
    private static RuleCandidate Candidate(string title = "统计作业", TaskTime? time = null,
        RuleAction action = RuleAction.Submit, RuleDisposition disposition = RuleDisposition.Task,
        string? target = null, TaskTime? suggested = null, string account = Account) =>
        new(title, action, disposition, "数学", time, suggested, target, 3,
            new(new("行动明确", true), new("适用于本人", true), new("课程已确认", true),
                new("时间明确", true), new("来源明确", true), new("关联明确", true)), [], [new(account, Group, "1")]);
    private ApplicationItem Seed(AccountWorkspace workspace, string id = "task", string title = "统计作业",
        TaskTime? time = null, string account = Account)
    {
        Tasks(account).CreateTask(id, Candidate(title, time ?? Exact(), account: account), "create-" + id, Now);
        return workspace.Snapshot(Now).Items.Single(item => item.Id == id);
    }
    private static WorkspaceCommand Command(string operation, ApplicationItem item, string? operationId = null,
        string account = Account) => new(account, operation, item.Id, operationId ?? Guid.NewGuid().ToString("N"), item.Version);
    private static TaskReminderPlan Plan(AccountWorkspace workspace, string id = "task", DateTimeOffset? now = null) =>
        workspace.Snapshot(now ?? Now).Reminders.Single(plan => plan.TaskId == id);
    private static WorkspaceCommand Configure(ApplicationItem item, TaskReminderPlan plan, string mode,
        double? hours = null, int? days = null, string? clock = null, string? operationId = null) =>
        Command("set-reminder", item, operationId) with
        {
            ReminderMode = mode, ReminderHours = hours, ReminderDays = days,
            ReminderClock = clock, ReminderVersion = plan.PlanVersion
        };

    private WorkspaceCommand Change(AccountWorkspace workspace, ApplicationItem item, string operation)
    {
        if (operation == "postpone") return Command("accept-time", item) with { Date = "2026-10-15", Time = "18:00" };
        if (operation != "cancel") return Command(operation, item);
        var review = Tasks().SavePending("cancel-" + item.Id, Candidate(action: RuleAction.Cancel,
            disposition: RuleDisposition.Pending, target: item.Id), "save-cancel-" + item.Id, Now);
        return new(Account, "accept-review", review.Id, "accept-cancel-" + item.Id,
            review.Version, TargetTaskVersion: item.Version);
    }

    [Fact]
    public void NewTask_SnapshotHasDefaultExactTimePlan()
    {
        var workspace = Workspace();
        var task = Tasks().CreateTask("task", Candidate(time: Exact()), "create", Now);
        var plan = Assert.Single(workspace.Snapshot(Now).Reminders);
        Assert.Equal(task.Id, plan.TaskId);
        Assert.Equal(task.TimeVersion, plan.TimeVersion);
        Assert.Equal("default", plan.Preference.Mode);
        Assert.True(plan.Active);
        Assert.False(plan.NeedsReview);
        Assert.Equal([Now.AddHours(8), Now.AddHours(30)], plan.Nodes.Select(node => node.ScheduledAt));
    }

    [Fact]
    public void RoutedNewTask_CreatesDefaultPlanFromFormalTime()
    {
        var workspace = Workspace();
        HashSet<string> pinned = [Group];
        workspace.SetAvailableGroups([new OneBotGroup(Group, "数学课程群")], pinned);
        workspace.Execute(new(Account, "confirm-group", Group, "course", 0, Course: "数学"), Now);
        var message = new SchoolMessage(new(Account, Group, "new-task"), "teacher", Now,
            [new(SegmentKind.Text, "10月13日18点提交统计作业")]);
        Assert.True(workspace.Ingest(message, pinned, MessageOrigin.Realtime, "live", Now));
        var snapshot = workspace.Snapshot(Now);
        var task = Assert.Single(snapshot.Items);
        var plan = Assert.Single(snapshot.Reminders);
        Assert.Equal("task", task.Kind);
        Assert.Equal(task.Id, plan.TaskId);
        Assert.Equal("default", plan.Preference.Mode);
        Assert.Equal([Now.AddHours(8), Now.AddHours(30)], plan.Nodes.Select(node => node.ScheduledAt));
    }

    [Fact]
    public void ImageSuggestedTime_HasNoPlanUntilExplicitReviewAcceptance()
    {
        var workspace = Workspace();
        HashSet<string> pinned = [Group];
        var message = new SchoolMessage(new(Account, Group, "image"), "teacher", Now,
            [new(SegmentKind.Text, "提交统计作业"), new(SegmentKind.Image, "10月20日截止", AssetId: "image")]);
        Assert.True(workspace.Ingest(message, pinned, MessageOrigin.History, "history", Now));
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("review", review.Kind);
        Assert.Null(review.Due);
        Assert.Equal("2026-10-20", review.SuggestedDue);
        Assert.Empty(workspace.Snapshot(Now).Reminders);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Command("accept-review", review), Now));
        Assert.Empty(workspace.Snapshot(Now).Reminders);
        workspace.Execute(Command("accept-review", review) with { Date = "2026-10-21" }, Now);
        var reopened = Workspace().Snapshot(Now);
        var task = Assert.Single(reopened.Items);
        var plan = Assert.Single(reopened.Reminders);
        Assert.Equal(task.Id, plan.TaskId);
        Assert.Equal("default", plan.Preference.Mode);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(8)), Assert.Single(plan.Nodes).ScheduledAt);
        Assert.Equal("2026-10-21", task.Due);
        Assert.Null(task.Time);
    }

    [Theory]
    [InlineData("hours", 7.5, null, null)]
    [InlineData("calendar", null, 2, "09:30")]
    [InlineData("off", null, null, null)]
    public void Configure_ReopenPreservesPreferenceWithoutChangingFormalTime(string mode, double? hours,
        int? days, string? clock)
    {
        var workspace = Workspace();
        var item = Seed(workspace);
        var before = Tasks().FindTask(item.Id)!;
        workspace.Execute(Configure(item, Plan(workspace), mode, hours, days, clock), Now);
        var after = Tasks().FindTask(item.Id)!;
        var plan = Plan(Workspace());
        Assert.Equal(new ReminderPreference(mode, hours, days, clock), plan.Preference);
        Assert.Equal(before.Time, after.Time);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.TimeVersion, after.TimeVersion);
        Assert.Equal(after.TimeVersion, plan.TimeVersion);
        if (mode == "hours") Assert.Equal(Exact().GetExactInstant()!.Value.AddHours(-7.5), Assert.Single(plan.Nodes).ScheduledAt);
        if (mode == "calendar") Assert.Equal(new DateTimeOffset(2026, 10, 11, 9, 30, 0, TimeSpan.FromHours(8)), Assert.Single(plan.Nodes).ScheduledAt);
        if (mode == "off") Assert.DoesNotContain(plan.Nodes, node => node.State == "pending");
    }

    [Fact]
    public void Configure_RejectsStalePlanVersionAndKeepsSavedPreference()
    {
        var workspace = Workspace();
        var item = Seed(workspace);
        var old = Plan(workspace);
        workspace.Execute(Configure(item, old, "hours", hours: 4), Now);
        var saved = Plan(workspace);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Configure(item, old, "off"), Now));
        var reopened = Plan(Workspace());
        Assert.Equal(saved.Preference, reopened.Preference);
        Assert.Equal(saved.PlanVersion, reopened.PlanVersion);
        Assert.Equal(saved.Nodes.Select(node => node.Id), reopened.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void Configure_RejectsStaleTaskVersionEvenWithCurrentPlanVersion()
    {
        var workspace = Workspace();
        var oldItem = Seed(workspace);
        workspace.Execute(Configure(oldItem, Plan(workspace), "hours", hours: 4), Now);
        var review = Tasks().SavePending("new-requirement", Candidate("补充统计作业", Exact(), RuleAction.NewRequirement,
            RuleDisposition.Pending, oldItem.Id), "save-new-requirement", Now);
        workspace.Execute(new(Account, "accept-review", review.Id, "accept-new-requirement", review.Version,
            TargetTaskVersion: oldItem.Version), Now);
        var saved = Plan(workspace);
        var current = workspace.Snapshot(Now).Items.Single(item => item.Id == oldItem.Id);
        Assert.True(current.Version > oldItem.Version);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Configure(oldItem, saved, "off"), Now));
        Assert.Equal(saved.Preference, Plan(Workspace()).Preference);
        Assert.Equal(saved.PlanVersion, Plan(Workspace()).PlanVersion);
    }

    [Fact]
    public void Configure_OperationRetryIsIdempotentAndReusedIdentityWithOtherParametersFails()
    {
        var workspace = Workspace();
        var item = Seed(workspace);
        var command = Configure(item, Plan(workspace), "hours", hours: 4, operationId: "configure-once");
        workspace.Execute(command, Now);
        var saved = Plan(workspace);
        Workspace().Execute(command, Now.AddMinutes(1));
        var retried = Plan(Workspace());
        Assert.Equal(saved.PlanVersion, retried.PlanVersion);
        Assert.Equal(saved.Nodes.Select(node => node.Id), retried.Nodes.Select(node => node.Id));
        Assert.Throws<InvalidOperationException>(() => Workspace().Execute(command with { ReminderHours = 6 }, Now));
        Assert.Equal(saved.Preference, Plan(Workspace()).Preference);
    }

    [Fact]
    public void DisabledReminder_PostponePreservesDisabledChoiceAndSubmitsNothing()
    {
        var workspace = Workspace();
        var item = Seed(workspace);
        workspace.Execute(Configure(item, Plan(workspace), "off"), Now);
        workspace.Execute(Change(workspace, item, "postpone"), Now);
        var plan = Plan(Workspace());
        Assert.Equal("off", plan.Preference.Mode);
        Assert.DoesNotContain(plan.Nodes, node => node.State == "pending");
        var sink = new RecordingSink();
        Assert.Empty(workspace.DispatchReminders(sink, Now.AddDays(3), recovering: true));
        Assert.Empty(sink.Submissions);
    }

    [Fact]
    public void HoursReminder_LosingTimePrecisionPreservesChoiceAndShowsDefaultFallbackForReview()
    {
        var workspace = Workspace();
        var item = Seed(workspace);
        workspace.Execute(Configure(item, Plan(workspace), "hours", hours: 4), Now);
        workspace.Execute(Command("accept-time", item) with { Date = "2026-10-15" }, Now);
        var snapshot = Workspace().Snapshot(Now);
        var changed = Assert.Single(snapshot.Items);
        var plan = Assert.Single(snapshot.Reminders);
        Assert.Equal("2026-10-15", changed.Due);
        Assert.Null(changed.Time);
        Assert.Equal(new ReminderPreference("hours", 4), plan.Preference);
        Assert.True(plan.NeedsReview);
        Assert.True(plan.Active);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(8)), Assert.Single(plan.Nodes).ScheduledAt);
    }

    [Fact]
    public void ContentVersionChange_DoesNotRegenerateOrReplaySubmittedNodes()
    {
        var workspace = Workspace();
        var item = Seed(workspace);
        var original = Plan(workspace);
        var sink = new RecordingSink();
        Assert.Single(workspace.DispatchReminders(sink, Now.AddHours(8)));
        var review = Tasks().SavePending("new-requirement", Candidate("补充统计作业", Exact(), RuleAction.NewRequirement,
            RuleDisposition.Pending, item.Id), "save-new-requirement", Now);
        workspace.Execute(new(Account, "accept-review", review.Id, "accept-new-requirement", review.Version,
            TargetTaskVersion: item.Version), Now.AddHours(8));
        var changed = Plan(workspace, now: Now.AddHours(8));
        Assert.Equal(original.TimeVersion, changed.TimeVersion);
        Assert.Equal(original.Nodes.Select(node => node.Id), changed.Nodes.Select(node => node.Id));
        Assert.Empty(workspace.DispatchReminders(sink, Now.AddHours(8)));
        Assert.Single(sink.Submissions);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("archive")]
    [InlineData("postpone")]
    public void ChangeFinishesFirst_DispatchCannotSubmitOldTimeNodes(string operation)
    {
        var workspace = Workspace();
        var item = Seed(workspace);
        var oldTimeVersion = Plan(workspace).TimeVersion;
        var command = Change(workspace, item, operation);
        var sink = new RecordingSink();
        using var dispatchAllowed = new ManualResetEventSlim();
        IReadOnlyList<ReminderDelivery>? deliveries = null;
        using var dispatch = new Worker(() =>
        {
            if (!dispatchAllowed.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("操作未释放投递线程。");
            deliveries = workspace.DispatchReminders(sink, Now.AddHours(8));
        });
        using var mutation = new Worker(() => workspace.Execute(command, Now.AddHours(8)));
        try
        {
            mutation.Wait();
            Assert.True(Tasks().FindTask(item.Id)!.Version > item.Version);
        }
        finally { dispatchAllowed.Set(); }
        dispatch.Wait();
        Assert.Empty(deliveries!);
        Assert.Empty(sink.Submissions);
        var plan = Plan(workspace, now: Now.AddHours(8));
        if (operation == "postpone") Assert.True(plan.TimeVersion > oldTimeVersion);
        else Assert.False(plan.Active);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("archive")]
    [InlineData("postpone")]
    public void SubmissionStartsFirst_ChangeWaitsThenRemovesOldNotification(string operation)
    {
        var workspace = Workspace();
        var item = Seed(workspace);
        var command = Change(workspace, item, operation);
        using var sink = new BlockingSink();
        IReadOnlyList<ReminderDelivery>? deliveries = null;
        using var dispatch = new Worker(() => deliveries = workspace.DispatchReminders(sink, Now.AddHours(8)));
        Assert.True(sink.Entered.Wait(TimeSpan.FromSeconds(10)), "投递端未进入暂停点。");
        using var mutation = new Worker(() => workspace.Execute(command, Now.AddHours(8)));
        try
        {
            Assert.True(SpinWait.SpinUntil(() => mutation.IsBlocked || mutation.Completed.IsSet,
                TimeSpan.FromSeconds(10)), "操作线程未到达工作区锁。");
            Assert.False(mutation.Completed.IsSet, "提交进行中时事项操作须等待同一工作区锁。");
            Assert.Equal(item.Version, Tasks().FindTask(item.Id)!.Version);
        }
        finally { sink.Release.Set(); }
        dispatch.Wait();
        mutation.Wait();
        var delivery = Assert.Single(deliveries!);
        Assert.Equal("submitted", delivery.State);
        Assert.Contains(delivery.NotificationId!, sink.Removed);
        Assert.Empty(sink.Active);
        Assert.Equal(item.Id, Assert.Single(delivery.Members).TaskId);
        Assert.Empty(workspace.DispatchReminders(sink, Now.AddHours(8)));
    }

    [Fact]
    public void Recovery_ReopenDoesNotResubmitPreviouslySubmittedNodes()
    {
        var workspace = Workspace();
        Seed(workspace);
        var sink = new RecordingSink();
        var first = Assert.Single(workspace.DispatchReminders(sink, Now.AddHours(8)));
        var recovered = Assert.Single(Workspace().DispatchReminders(sink, Now.AddHours(31), recovering: true));
        Assert.Equal("recovery", recovered.Kind);
        var firstNode = Assert.Single(Assert.Single(first.Members).NodeIds);
        var recoveredNode = Assert.Single(Assert.Single(recovered.Members).NodeIds);
        Assert.NotEqual(firstNode, recoveredNode);
        Assert.Empty(Workspace().DispatchReminders(sink, Now.AddHours(31), recovering: true));
        Assert.Equal(2, sink.Submissions.Count);
        Assert.Equal(2, Workspace().ReminderDeliveries().Count(delivery => delivery.State == "submitted"));
    }

    [Fact]
    public void RecoverySummary_MemberInvalidationClearsOldSummaryAndKeepsOtherMembersLedger()
    {
        var workspace = Workspace();
        var first = Seed(workspace);
        var second = Seed(workspace, "second", "英语作业");
        var sink = new RecordingSink();
        var summary = Assert.Single(workspace.DispatchReminders(sink, Now.AddHours(31), recovering: true));
        Assert.Equal("recovery", summary.Kind);
        Assert.Equal(2, summary.Members.Count);
        Assert.All(summary.Members, member => Assert.Equal(2, member.NodeIds.Count));
        var remaining = summary.Members.Single(member => member.TaskId == second.Id);
        workspace.Execute(Command("complete", first), Now.AddHours(31));
        Assert.Contains(summary.NotificationId!, sink.Removed);
        Assert.Empty(sink.Active);
        Assert.Equal("active", workspace.Snapshot(Now.AddHours(31)).Items.Single(item => item.Id == second.Id).State);
        var otherPlan = Plan(workspace, second.Id, Now.AddHours(31));
        Assert.Equal(remaining.NodeIds.Order(), otherPlan.Nodes.Select(node => node.Id).Order());
        Assert.All(otherPlan.Nodes, node => Assert.Equal("submitted", node.State));
        Assert.Contains(workspace.ReminderDeliveries().SelectMany(delivery => delivery.Members), member => member.TaskId == second.Id);
        var count = sink.Submissions.Count;
        Assert.Empty(Workspace().DispatchReminders(sink, Now.AddHours(31), recovering: true));
        Assert.Equal(count, sink.Submissions.Count);
    }

    [Fact]
    public void RecoverySummary_InFlightCompletionWaitsAndCannotLeaveInvalidMemberInNotification()
    {
        var workspace = Workspace();
        var first = Seed(workspace);
        var second = Seed(workspace, "second", "英语作业");
        using var sink = new BlockingSink();
        using var dispatch = new Worker(() => workspace.DispatchReminders(sink, Now.AddHours(31), recovering: true));
        Assert.True(sink.Entered.Wait(TimeSpan.FromSeconds(10)));
        using var mutation = new Worker(() => workspace.Execute(Command("complete", first), Now.AddHours(31)));
        try
        {
            Assert.True(SpinWait.SpinUntil(() => mutation.IsBlocked || mutation.Completed.IsSet, TimeSpan.FromSeconds(10)));
            Assert.False(mutation.Completed.IsSet);
            Assert.Equal(first.Version, Tasks().FindTask(first.Id)!.Version);
        }
        finally { sink.Release.Set(); }
        dispatch.Wait();
        mutation.Wait();
        Assert.Empty(sink.Active);
        Assert.Contains(Assert.Single(sink.Submissions).NotificationId!, sink.Removed);
        Assert.Equal("active", workspace.Snapshot(Now.AddHours(31)).Items.Single(item => item.Id == second.Id).State);
        Assert.All(Plan(workspace, second.Id, Now.AddHours(31)).Nodes, node => Assert.Equal("submitted", node.State));
        Assert.Contains(workspace.ReminderDeliveries().SelectMany(delivery => delivery.Members), member => member.TaskId == second.Id);
        Assert.Empty(Workspace().DispatchReminders(sink, Now.AddHours(31), recovering: true));
        Assert.Single(sink.Submissions);
    }

    [Fact]
    public void SharedSink_AccountMutationDoesNotMixOrRemoveOtherAccountsNotifications()
    {
        const string otherAccount = "9007199254740997";
        var workspace = Workspace();
        var other = Workspace(otherAccount);
        var item = Seed(workspace);
        Seed(other, account: otherAccount);
        var sink = new RecordingSink();
        var own = Assert.Single(workspace.DispatchReminders(sink, Now.AddHours(31), recovering: true));
        var foreign = Assert.Single(other.DispatchReminders(sink, Now.AddHours(31), recovering: true));
        Assert.Equal(Account, own.AccountId);
        Assert.Equal(otherAccount, foreign.AccountId);
        Assert.Single(own.Members);
        Assert.Single(foreign.Members);
        workspace.Execute(Command("complete", item), Now.AddHours(31));
        Assert.Contains(own.NotificationId!, sink.Removed);
        Assert.DoesNotContain(foreign.NotificationId!, sink.Removed);
        Assert.Equal(otherAccount, Assert.Single(sink.Active).AccountId);
        Assert.All(workspace.ReminderDeliveries(), delivery => Assert.Equal(Account, delivery.AccountId));
        Assert.All(other.ReminderDeliveries(), delivery => Assert.Equal(otherAccount, delivery.AccountId));
    }

    private class RecordingSink : IReminderSink
    {
        private readonly object gate = new();
        private readonly List<ReminderDelivery> submissions = [];
        private readonly List<string> removed = [];
        private readonly Dictionary<string, ReminderDelivery> active = new(StringComparer.Ordinal);
        public IReadOnlyList<ReminderDelivery> Submissions { get { lock (gate) return submissions.ToArray(); } }
        public IReadOnlyList<string> Removed { get { lock (gate) return removed.ToArray(); } }
        public IReadOnlyList<ReminderDelivery> Active { get { lock (gate) return active.Values.ToArray(); } }
        public virtual ReminderSubmission Submit(ReminderDelivery delivery)
        {
            lock (gate)
            {
                var notificationId = "test-notification-" + (submissions.Count + 1);
                var submitted = delivery with { State = "submitted", NotificationId = notificationId };
                submissions.Add(submitted);
                active[notificationId] = submitted;
                return new("submitted", notificationId);
            }
        }
        public void Remove(string notificationId)
        {
            lock (gate)
            {
                removed.Add(notificationId);
                active.Remove(notificationId);
            }
        }
    }

    private sealed class BlockingSink : RecordingSink, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public override ReminderSubmission Submit(ReminderDelivery delivery)
        {
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("通知暂停点未释放。");
            return base.Submit(delivery);
        }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    private sealed class Worker : IDisposable
    {
        private readonly Thread thread;
        private Exception? failure;
        public ManualResetEventSlim Completed { get; } = new();
        public bool IsBlocked => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0;
        public Worker(Action action)
        {
            thread = new(() =>
            {
                try { action(); }
                catch (Exception error) { failure = error; }
                finally { Completed.Set(); }
            }) { IsBackground = true };
            thread.Start();
        }
        public void Wait()
        {
            Assert.True(Completed.Wait(TimeSpan.FromSeconds(10)), "工作线程未在期限内结束。");
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "工作线程结束事件后未退出。");
            Assert.Null(failure);
        }
        public void Dispose()
        {
            if (thread.Join(TimeSpan.FromSeconds(10))) Completed.Dispose();
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
