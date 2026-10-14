using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MessagePurify.Infrastructure.Tests;

public sealed class AccountWorkspaceTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string Group = "9007199254740995";
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-workspace-" + Guid.NewGuid().ToString("N"));
    private static HashSet<string> Pinned() => [Group];
    private AccountWorkspace Workspace() => new(root, Account);
    private AccountTaskStore Tasks() => new(root, Account);
    private static SchoolMessage Message(string id = "1", string text = "10月13日18点提交统计作业") =>
        new(new(Account, Group, id), "teacher", Now.AddMinutes(-1), [new(SegmentKind.Text, text)]);
    private static WorkspaceCommand Command(string operation, ApplicationItem item, string? operationId = null) =>
        new(Account, operation, item.Id, operationId ?? Guid.NewGuid().ToString("N"), item.Version);
    private static void Groups(AccountWorkspace workspace) =>
        workspace.SetAvailableGroups([new(Group, "数学课程群")], Pinned());
    private static void Confirm(AccountWorkspace workspace, string? course = "数学", bool excluded = false) =>
        workspace.Execute(new(Account, "confirm-group", Group, Guid.NewGuid().ToString("N"), 0, Course: course, Excluded: excluded), Now);
    private static RuleCandidate Candidate(string title = "统计作业", TaskTime? time = null,
        RuleAction action = RuleAction.Submit, RuleDisposition disposition = RuleDisposition.Task,
        string? target = null, TaskTime? suggested = null) =>
        new(title, action, disposition, "数学", time, suggested, target, 3,
            new(new("行动明确", true), new("适用于本人", true), new("课程已确认", true),
                new("时间明确", true), new("来源明确", true), new("关联明确", true)), [], [new(Account, Group, "1")]);
    private static TaskTime Date(int day = 13) => new(TimeKind.Deadline, new(2026, 10, day), null, "Asia/Shanghai");
    private ApplicationItem SeedTask(AccountWorkspace workspace)
    {
        Tasks().CreateTask("seed-task", Candidate(time: Date()), "seed", Now);
        return Assert.Single(workspace.Snapshot(Now).Items);
    }

    [Fact]
    public void EmptyWorkspace_HasNoDemoItemsAndUsesRequestedClock()
    {
        var snapshot = Workspace().Snapshot(Now);
        Assert.Equal(Account, snapshot.AccountId);
        Assert.Equal(Now, snapshot.GeneratedAt);
        Assert.Empty(snapshot.Items);
        Assert.Empty(snapshot.Groups);
    }

    [Fact]
    public void Ingest_UnconfirmedCourseCreatesReviewWithOriginalEvidence()
    {
        var workspace = Workspace();
        Groups(workspace);
        Assert.True(workspace.Ingest(Message(), Pinned(), MessageOrigin.Realtime, "live", Now));
        var item = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("review", item.Kind);
        Assert.Equal("pending", item.State);
        Assert.Equal("数学课程群", item.Group);
        Assert.True(item.Unread);
        var evidence = Assert.Single(item.Evidence);
        Assert.Equal(Message().Segments[0].Text, evidence.Text);
        Assert.Equal("teacher", evidence.SenderId);
        Assert.Equal(Message().SentAt, evidence.SentAt);
        Assert.Equal(0, evidence.ImageCount);
    }

    [Fact]
    public void ConfirmGroup_PersistsRelationshipWithoutReplayingExistingReview()
    {
        var workspace = Workspace();
        Groups(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        Confirm(workspace);
        var reopened = Workspace();
        Groups(reopened);
        var group = Assert.Single(reopened.Snapshot(Now).Groups);
        Assert.Equal("数学", group.Course);
        Assert.Equal("数学课程群", group.SuggestedCourse);
        Assert.True(group.Pinned);
        Assert.Equal(review.Id, Assert.Single(reopened.Snapshot(Now).Items).Id);
        Assert.True(reopened.Ingest(Message("2", "10月14日提交新的报告"), Pinned(), MessageOrigin.Realtime, "live", Now));
        Assert.Contains(reopened.Snapshot(Now).Items, item => item.Kind == "task" && item.Course == "数学");
        Assert.Contains(reopened.Snapshot(Now).Items, item => item.Id == review.Id && item.Kind == "review");
    }

    [Fact]
    public void ConfirmGroup_DoesNotTrustPreviousTaskPublisherForCancellation()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.Realtime, "live", Now);
        var original = Assert.Single(workspace.Snapshot(Now).Items);
        var cancel = Message("2", "统计作业取消") with
        {
            SentAt = Now,
            Segments = [new(SegmentKind.Text, "统计作业取消"), new(SegmentKind.Quote, "原通知", Message().Key)]
        };
        workspace.Ingest(cancel, Pinned(), MessageOrigin.Realtime, "live", Now);
        var snapshot = workspace.Snapshot(Now);
        Assert.Equal("active", snapshot.Items.Single(item => item.Id == original.Id).State);
        Assert.Contains(snapshot.Items, item => item.Kind == "review" && item.Action == "cancel");
    }

    [Fact]
    public void Ingest_RejectsPrivateUnpinnedExcludedAndWrongAccountWithoutSaving()
    {
        var workspace = Workspace();
        Groups(workspace);
        Assert.False(workspace.Ingest(Message() with { IsPrivate = true }, Pinned(), MessageOrigin.Realtime, "live", Now));
        Assert.False(workspace.Ingest(Message(), new HashSet<string>(), MessageOrigin.Realtime, "live", Now));
        Confirm(workspace, excluded: true);
        Assert.False(workspace.Ingest(Message(), Pinned(), MessageOrigin.Realtime, "live", Now));
        Assert.Throws<ArgumentException>(() => workspace.Ingest(Message() with { Key = new("123", Group, "1") },
            Pinned(), MessageOrigin.Realtime, "live", Now));
        Assert.Empty(workspace.Snapshot(Now).Items);
        Assert.Null(new AccountMessageStore(root, Account).Find(Message().Key));
    }

    [Fact]
    public void Enqueue_DefersRoutingAndProcessPendingUsesOriginalMessageOrder()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        var newer = Message("newer", "统计作业取消") with
        {
            SentAt = Now,
            Segments = [new(SegmentKind.Text, "统计作业取消"), new(SegmentKind.Quote, "原通知", Message().Key)]
        };
        Assert.True(workspace.Enqueue(newer, Pinned(), MessageOrigin.Realtime, "live", Now));
        Assert.True(workspace.Enqueue(Message(), Pinned(), MessageOrigin.History, "history", Now));
        Assert.Empty(workspace.Snapshot(Now).Items);
        Assert.Equal(2, workspace.ProcessPending(Pinned(), Now));
        var task = workspace.Snapshot(Now).Items.Single(item => item.Kind == "task");
        var review = workspace.Snapshot(Now).Items.Single(item => item.Kind == "review");
        Assert.Equal(task.Id, review.TargetTaskId);
        Assert.Equal(task.Version, review.TargetTaskVersion);
        Assert.Equal(0, workspace.ProcessPending(Pinned(), Now));
    }

    [Fact]
    public void CompleteAndUndo_PersistAndRejectStaleVersionsAndMissingUndoIdentity()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        var complete = Command("complete", task, "complete");
        workspace.Execute(complete, Now);
        workspace.Execute(complete, Now);
        var completed = Assert.Single(Workspace().Snapshot(Now).Items);
        Assert.Equal("completed", completed.State);
        Assert.Equal("complete", completed.UndoOperationId);
        Assert.Equal(Now.AddHours(1), completed.UndoExpiresAt);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Command("archive", task), Now));
        Assert.ThrowsAny<ArgumentException>(() => workspace.Execute(Command("undo-complete", completed), Now));
        workspace.Execute(Command("undo-complete", completed) with { UndoOperationId = "complete" }, Now.AddMinutes(1));
        Assert.Equal("active", Assert.Single(Workspace().Snapshot(Now).Items).State);
    }

    [Fact]
    public void ArchiveAndRestore_UseStoredTaskState()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        workspace.Execute(Command("archive", task), Now);
        var archived = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("archived", archived.State);
        workspace.Execute(Command("restore", archived), Now);
        Assert.Equal("active", Assert.Single(workspace.Snapshot(Now).Items).State);
    }

    [Fact]
    public void MarkRead_IsDurableAndOldVersionCannotReadLaterChange()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        workspace.Execute(Command("mark-read", task), Now);
        Assert.False(Assert.Single(Workspace().Snapshot(Now).Items).Unread);
        workspace.Execute(Command("complete", task), Now);
        Assert.True(Assert.Single(workspace.Snapshot(Now).Items).Unread);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Command("mark-read", task), Now));
        Assert.True(Assert.Single(workspace.Snapshot(Now).Items).Unread);
        var current = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(Command("mark-read", current), Now);
        Assert.False(Assert.Single(Workspace().Snapshot(Now).Items).Unread);
    }

    [Fact]
    public void ChangeHistory_EditShowsFieldsAndSurvivesReopenWithoutDuplicateOperation()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        workspace.Execute(Command("mark-read", task), Now);
        var command = Command("edit-task", task, "history-edit") with
        { Title = "修订报告", Course = null, Date = "2026-10-20", Time = "18:30", TimeKind = "start", Priority = 1 };
        workspace.Execute(command, Now.AddMinutes(1));
        workspace.Execute(command, Now.AddMinutes(2));
        var changed = Assert.Single(Workspace().Snapshot(Now).Items);
        Assert.True(changed.Unread);
        Assert.Equal("已修改", changed.ChangeLabel);
        var history = Assert.IsAssignableFrom<IReadOnlyList<ApplicationChange>>(changed.Changes);
        Assert.Equal(2, history.Count);
        var edit = history[0];
        Assert.Equal("修改任务", edit.Label);
        Assert.Equal(Now.AddMinutes(1), edit.At);
        Assert.Equal(changed.Version, edit.Version);
        Assert.Contains(edit.Fields, field => field.Field == "标题" && field.Before == "统计作业" && field.After == "修订报告");
        Assert.Contains(edit.Fields, field => field.Field == "课程" && field.Before == "数学" && field.After == "未分类");
        Assert.Contains(edit.Fields, field => field.Field == "正式日期" && field.After == "2026-10-20");
        Assert.Contains(edit.Fields, field => field.Field == "钟点" && field.Before == "未填写" && field.After == "18:30");
        Assert.Contains(edit.Fields, field => field.Field == "时间类型" && field.After == "开始时间");
        Assert.Contains(edit.Fields, field => field.Field == "优先级" && field.Before == "自动分级" && field.After == "P1");
        Assert.Single(history[1].Evidence);
        Assert.Empty(edit.Evidence);
        Assert.Empty(new AccountWorkspace(root, "123").Snapshot(Now).Items);
        workspace.Execute(Command("mark-read", changed), Now);
        var read = Assert.Single(Workspace().Snapshot(Now).Items);
        Assert.False(read.Unread);
        Assert.Null(read.ChangeLabel);
        Assert.Equal(2, read.Changes!.Count);
    }

    [Fact]
    public void ChangeHistory_NoVisibleEditDoesNotCreateImportantUnreadOrHistory()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        workspace.Execute(Command("mark-read", task), Now);
        workspace.Execute(Command("accept-time", task) with { Date = task.Due, TimeKind = task.TimeKind }, Now);
        var changed = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal(task.Version + 1, changed.Version);
        Assert.False(changed.Unread);
        Assert.Null(changed.ChangeLabel);
        Assert.Single(changed.Changes!);
    }

    [Fact]
    public void ChangeHistory_LinkedReviewShowsPendingAndAcceptedSnapshots()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        workspace.Execute(Command("mark-read", task), Now);
        Tasks().SavePending("postpone-review", Candidate("报告延期", Date(20), RuleAction.Postpone,
            RuleDisposition.Change, task.Id), "save-postpone", Now);
        var snapshot = workspace.Snapshot(Now);
        var pendingTask = snapshot.Items.Single(item => item.Kind == "task");
        Assert.Equal("待确认变更", pendingTask.ChangeLabel);
        var proposal = Assert.Single(pendingTask.PendingChanges!);
        Assert.Equal("postpone-review", proposal.Id);
        Assert.Equal("2026-10-20", proposal.Due);
        Assert.Equal("2026-10-13", pendingTask.Due);
        var review = snapshot.Items.Single(item => item.Kind == "review");
        workspace.Execute(Command("accept-review", review) with
        { Date = "2026-10-20", TimeKind = "deadline", TargetTaskVersion = task.Version }, Now.AddMinutes(1));
        var changed = workspace.Snapshot(Now).Items.Single(item => item.Kind == "task");
        Assert.Empty(changed.PendingChanges!);
        Assert.Equal("已延期", changed.ChangeLabel);
        var latest = changed.Changes![0];
        Assert.Equal("确认延期", latest.Label);
        Assert.Contains(latest.Fields, field => field.Field == "正式日期" && field.Before == "2026-10-13" && field.After == "2026-10-20");
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Command("mark-read", task), Now));
        Assert.Equal("已延期", workspace.Snapshot(Now).Items.Single(item => item.Kind == "task").ChangeLabel);
    }

    [Fact]
    public void ChangeHistory_InitialReviewAcceptanceAndRepeatedCreationShowOneCreationRecord()
    {
        var workspace = Workspace();
        var candidate = Candidate(time: null, disposition: RuleDisposition.Pending);
        Tasks().SavePending("new-review", candidate, "save-review", Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(Command("accept-review", review) with { Date = "2026-10-13", TimeKind = "deadline" }, Now);
        var task = workspace.Snapshot(Now).Items.Single(item => item.Kind == "task");
        Assert.Single(task.Changes!);
        Assert.Equal("确认并加入任务", task.Changes![0].Label);
        Assert.Null(task.ChangeLabel);
        Assert.True(task.Unread);
        var other = new AccountWorkspace(Path.Combine(root, "separate"), Account);
        var store = new AccountTaskStore(Path.Combine(root, "separate"), Account);
        store.CreateTask("duplicate-task", Candidate(time: Date()), "first-create", Now);
        store.CreateTask("other-id", Candidate(time: Date()), "duplicate-create", Now.AddMinutes(1));
        Assert.Single(Assert.Single(other.Snapshot(Now).Items).Changes!);
    }

    [Fact]
    public void ChangeHistory_IgnoredProposalClearsPendingMarkerAndReadDoesNotClearUnresolvedProposal()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        Tasks().SavePending("pending-change", Candidate("延期", Date(20), RuleAction.Postpone,
            RuleDisposition.Change, task.Id), "pending-operation", Now);
        workspace.Execute(Command("mark-read", task), Now);
        var snapshot = workspace.Snapshot(Now);
        var current = snapshot.Items.Single(item => item.Kind == "task");
        Assert.False(current.Unread);
        Assert.Equal("待确认变更", current.ChangeLabel);
        var review = snapshot.Items.Single(item => item.Kind == "review");
        workspace.Execute(Command("ignore", review), Now);
        current = workspace.Snapshot(Now).Items.Single(item => item.Kind == "task");
        Assert.Null(current.ChangeLabel);
        Assert.Empty(current.PendingChanges!);
        Assert.Single(current.Changes!);
    }

    [Fact]
    public void AcceptTime_PreservesDateOnlyAndRecalculatesPriorityForActualNow()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        workspace.Execute(Command("accept-time", task) with { Date = "2026-10-20", TimeKind = "start" }, Now);
        var changed = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("2026-10-20", changed.Due);
        Assert.Null(changed.Time);
        Assert.Equal("start", changed.TimeKind);
        Assert.Equal(3, changed.Priority);
        Assert.Equal(0, Assert.Single(workspace.Snapshot(Now.AddDays(8)).Items).Priority);
    }

    [Fact]
    public void EditTask_UpdatesCompleteFormAndPersistsManualPriorityAcrossClockAndTimeChanges()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        workspace.Execute(Command("edit-task", task, "edit-1") with
        {
            Title = " 修订后的统计作业 ", Course = " 统计学 ", Date = "2026-10-20",
            Time = "18:30", TimeKind = "start", Priority = 3
        }, Now);

        var edited = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("修订后的统计作业", edited.Title);
        Assert.Equal("统计学", edited.Course);
        Assert.Equal("2026-10-20", edited.Due);
        Assert.Equal("18:30", edited.Time);
        Assert.Equal("start", edited.TimeKind);
        Assert.Equal(3, edited.Priority);
        Assert.Equal(task.Version + 1, edited.Version);
        Assert.Equal(task.Evidence, edited.Evidence);
        Assert.Equal(3, Assert.Single(Workspace().Snapshot(Now.AddDays(30)).Items).Priority);

        workspace.Execute(Command("accept-time", edited) with { Date = "2026-10-12", TimeKind = "deadline" }, Now);
        Assert.Equal(3, Assert.Single(workspace.Snapshot(Now).Items).Priority);
    }

    [Fact]
    public void EditTask_ClearCourseAndClockKeepsEvidenceAndRejectsStaleVersion()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        var edit = Command("edit-task", task, "edit-1") with
        {
            Title = "重新命名的任务", Course = "", Date = "2026-10-20", TimeKind = "deadline", Priority = 1
        };
        workspace.Execute(edit, Now);
        workspace.Execute(edit, Now.AddMinutes(1));
        var changed = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Null(changed.Course);
        Assert.Null(changed.Time);
        Assert.Equal(task.Version + 1, changed.Version);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(edit with { OperationId = "stale-edit" }, Now));
    }

    [Theory]
    [InlineData(null, "2026-10-20", "deadline", 0)]
    [InlineData("任务", null, "deadline", 0)]
    [InlineData("任务", "2026-10-20", null, 0)]
    [InlineData("任务", "2026-10-20", "deadline", null)]
    [InlineData("任务", "2026-10-20", "deadline", -1)]
    [InlineData("任务", "2026-10-20", "deadline", 4)]
    public void EditTask_RequiresTitleDateTimeKindAndBoundedPriority(string? title, string? date,
        string? timeKind, int? priority)
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        Assert.ThrowsAny<ArgumentException>(() => workspace.Execute(Command("edit-task", task) with
        {
            Title = title, Date = date, TimeKind = timeKind, Priority = priority
        }, Now));
        Assert.Equal(task.Version, Assert.Single(workspace.Snapshot(Now).Items).Version);
    }

    [Theory]
    [InlineData("2026-13-20", null, "deadline")]
    [InlineData("2026-10-20", "25:01", "deadline")]
    [InlineData("2026-10-20", null, "unknown")]
    [InlineData(null, "12:00", "deadline")]
    public void AcceptTime_InvalidInputPreservesTask(string? date, string? time, string? kind)
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Command("accept-time", task) with
            { Date = date, Time = time, TimeKind = kind }, Now));
        Assert.Equal(task.Version, Assert.Single(workspace.Snapshot(Now).Items).Version);
    }

    [Fact]
    public void SuggestedTime_IsShownWithoutFormalTimeAndRequiresExplicitConfirmation()
    {
        var workspace = Workspace();
        Tasks().SavePending("review", Candidate(disposition: RuleDisposition.Pending, suggested: Date(20)), "save-review", Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Null(review.Due);
        Assert.Equal("2026-10-20", review.SuggestedDue);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Command("accept-review", review), Now));
        Assert.Empty(Tasks().ListTasks());
        workspace.Execute(Command("accept-review", review) with { Date = "2026-10-21", TimeKind = "deadline" }, Now);
        var task = Assert.Single(Workspace().Snapshot(Now).Items);
        Assert.Equal("task", task.Kind);
        Assert.Equal("2026-10-21", task.Due);
        Assert.True(Assert.Single(Tasks().ListTasks()).TimeProtected);
    }

    [Fact]
    public void DateOnlyReview_UsesOriginalMessageDayAndSurvivesCompleteUndoAndReopen()
    {
        var workspace = Workspace();
        Groups(workspace);
        var sentAt = new DateTimeOffset(2026, 10, 8, 13, 50, 0, TimeSpan.FromHours(8));
        var message = Message(text: "请大家完成材料明天一并收取") with { SentAt = sentAt };
        Assert.True(workspace.Ingest(message, Pinned(), MessageOrigin.History, "history", Now));
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.True(review.CanAccept);
        Assert.Equal("2026-10-09", review.Due);
        Assert.Null(review.Time);
        Assert.Equal(sentAt, Assert.Single(review.Evidence).SentAt);

        workspace.Execute(Command("accept-review", review) with { Date = review.Due }, Now);
        var task = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("task", task.Kind);
        Assert.Equal("active", task.State);
        Assert.Equal(0, task.Priority);
        Assert.Equal("2026-10-09", task.Due);
        Assert.Null(task.Time);
        Assert.True(Assert.Single(Tasks().ListTasks()).TimeProtected);
        workspace.Execute(Command("complete", task), Now);
        var completed = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("completed", completed.State);
        workspace.Execute(Command("undo-complete", completed) with
            { UndoOperationId = completed.UndoOperationId }, Now.AddMinutes(1));

        var reopened = Assert.Single(Workspace().Snapshot(Now.AddMinutes(2)).Items);
        Assert.Equal(task.Id, reopened.Id);
        Assert.Equal("active", reopened.State);
        Assert.Equal("2026-10-09", reopened.Due);
        Assert.Null(reopened.Time);
        Assert.Equal(sentAt, Assert.Single(reopened.Evidence).SentAt);
        Assert.Equal(ReviewState.Accepted, Tasks().FindReview(review.Id)!.State);
    }

    [Fact]
    public void LinkedAccept_RequiresBothVersionsAndUpdatesOriginalTask()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        Tasks().SavePending("cancel-review", Candidate(action: RuleAction.Cancel, disposition: RuleDisposition.Pending,
            target: task.Id), "save-review", Now);
        var review = workspace.Snapshot(Now).Items.Single(item => item.Kind == "review");
        Assert.Equal(task.Version, review.TargetTaskVersion);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Command("accept-review", review), Now));
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Command("accept-review", review) with
            { TargetTaskVersion = task.Version + 1 }, Now));
        workspace.Execute(Command("accept-review", review) with { TargetTaskVersion = task.Version }, Now);
        var cancelled = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal(task.Id, cancelled.Id);
        Assert.Equal("cancelled", cancelled.State);
    }

    [Theory]
    [InlineData(RuleAction.Inform)]
    [InlineData(RuleAction.Cancel)]
    [InlineData(RuleAction.Postpone)]
    [InlineData(RuleAction.NewRequirement)]
    public void UnsupportedReview_CannotAcceptAndExplainsReason(RuleAction action)
    {
        var workspace = Workspace();
        Tasks().SavePending("review", Candidate(action: action, disposition: RuleDisposition.Pending), "save", Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.False(review.CanAccept);
        Assert.NotEmpty(review.ReviewReasons);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Command("accept-review", review) with { Date = "2026-10-20" }, Now));
        workspace.Execute(Command("reject", review), Now);
        Assert.Empty(workspace.Snapshot(Now).Items);
    }

    [Fact]
    public void IgnoreAndUndo_RequireIdentityAndPreserveExpiry()
    {
        var workspace = Workspace();
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(Command("ignore", review, "ignore"), Now);
        var ignored = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("ignored", ignored.State);
        Assert.Equal(Now.AddDays(1), ignored.UndoExpiresAt);
        Assert.ThrowsAny<ArgumentException>(() => workspace.Execute(Command("undo-ignore", ignored), Now));
        workspace.Execute(Command("undo-ignore", ignored) with { UndoOperationId = "ignore" }, Now.AddMinutes(2));
        Assert.Equal("pending", Assert.Single(workspace.Snapshot(Now).Items).State);
    }

    [Fact]
    public void ResyncAfterIgnoreUndoExpiresDoesNotRestoreIgnoredReview()
    {
        var workspace = Workspace();
        var message = Message();
        Assert.True(workspace.Ingest(message, Pinned(), MessageOrigin.History, "first-sync", Now));
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(Command("ignore", review, "ignore"), Now);
        var saved = JsonSerializer.Serialize(Tasks().FindReview(review.Id));

        workspace = Workspace();
        var later = Now.AddDays(2);
        Assert.False(workspace.Enqueue(message, Pinned(), MessageOrigin.History, "next-sync", later));
        Assert.Equal(0, workspace.ProcessPending(Pinned(), later));
        var ignored = Assert.Single(workspace.Snapshot(later).Items);
        Assert.Equal("ignored", ignored.State);
        Assert.Equal(Now.AddDays(1), ignored.UndoExpiresAt);
        Assert.True(ignored.UndoExpiresAt < later);
        Assert.Equal(saved, JsonSerializer.Serialize(Tasks().FindReview(review.Id)));
    }

    [Fact]
    public void Notice_IngestAndDuplicateReplayShowOnePersistentOriginalNotice()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        var message = Message(text: "10月14日18点自愿报名讲座");
        Assert.True(workspace.Ingest(message, Pinned(), MessageOrigin.History, "history", Now));
        Assert.False(workspace.Ingest(message, Pinned(), MessageOrigin.Realtime, "live", Now));
        var notice = Assert.Single(Workspace().Snapshot(Now).Items);
        Assert.Equal("notice", notice.Kind);
        Assert.Equal(message.Segments[0].Text, Assert.Single(notice.Evidence).Text);
    }

    [Fact]
    public void Notice_ConvertsToTaskWithRequiredDateAndDoesNotDuplicate()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        var message = Message(text: "10月14日18点自愿报名讲座");
        Assert.True(workspace.Ingest(message, Pinned(), MessageOrigin.History, "history", Now));
        var notice = Assert.Single(workspace.Snapshot(Now).Items);

        Assert.Throws<ArgumentException>(() => workspace.Execute(Command("convert-notice", notice), Now));

        workspace.Execute(Command("convert-notice", notice) with { Date = "2026-10-14", Time = "18:00" }, Now);
        var converted = workspace.Snapshot(Now).Items;
        var task = Assert.Single(converted, item => item.Kind == "task");
        var savedNotice = Assert.Single(converted, item => item.Kind == "notice");
        Assert.Equal(notice.Title, task.Title);
        Assert.Equal(notice.Course, task.Course);
        Assert.Equal("2026-10-14", task.Due);
        Assert.Equal("18:00", task.Time);
        Assert.Equal("converted", savedNotice.State);
        Assert.Equal(task.Id, savedNotice.TargetTaskId);

        workspace.Execute(Command("convert-notice", savedNotice) with { Date = "2026-10-14", Time = "18:00" }, Now.AddMinutes(1));
        Assert.Single(workspace.Snapshot(Now.AddMinutes(1)).Items, item => item.Kind == "task");
    }

    [Fact]
    public void NoticeConversion_PreservesActionStartKindAndAllEvidence()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        var quoted = new MessageKey(Account, Group, "quoted");
        var message = Message(text: "10月14日18点自愿参加讲座") with
        {
            Segments = [new(SegmentKind.Text, "10月14日18点自愿参加讲座"),
                new(SegmentKind.Quote, "原通知", quoted)]
        };
        Assert.True(workspace.Ingest(message, Pinned(), MessageOrigin.History, "history", Now));
        var notice = Assert.Single(workspace.Snapshot(Now).Items, item => item.Kind == "notice");

        workspace.Execute(Command("convert-notice", notice) with { Date = "2026-10-14", Time = "18:00" }, Now);

        var task = Assert.Single(Tasks().ListTasks());
        Assert.Equal(RuleAction.Attend, task.Action);
        Assert.Equal(TimeKind.Start, task.Time.Kind);
        Assert.Contains(message.Key, task.Evidence);
        Assert.Contains(quoted, task.Evidence);
    }

    [Fact]
    public void NoticeConversion_KeepsSeparateTasksForSeparateNoticesInOneMessage()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        var message = Message(text: "10月14日18点自愿参加讲座；10月15日18点自愿参加比赛");
        Assert.True(workspace.Ingest(message, Pinned(), MessageOrigin.History, "history", Now));
        var notices = workspace.Snapshot(Now).Items.Where(item => item.Kind == "notice").ToArray();
        Assert.Equal(2, notices.Length);

        workspace.Execute(Command("convert-notice", notices[0]) with { Date = "2026-10-14", Time = "18:00" }, Now);
        var current = workspace.Snapshot(Now).Items.Where(item => item.Kind == "notice").ToArray();
        workspace.Execute(Command("convert-notice", current[1]) with { Date = "2026-10-15", Time = "18:00" }, Now);

        Assert.Equal(2, Tasks().ListTasks().Count);
        var snapshot = workspace.Snapshot(Now);
        Assert.Equal(2, snapshot.Items.Count(item => item.Kind == "task"));
        var converted = snapshot.Items.Where(item => item.Kind == "notice").ToArray();
        Assert.Equal(2, converted.Length);
        Assert.All(converted, item => Assert.Equal("converted", item.State));
        Assert.NotEqual(converted[0].TargetTaskId, converted[1].TargetTaskId);
    }

    [Fact]
    public void Execute_RejectsWrongAccountAndUnknownOperation()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Command("complete", task) with { AccountId = "123" }, Now));
        Assert.Throws<ArgumentException>(() => workspace.Execute(Command("unknown", task), Now));
        Assert.Equal("active", Assert.Single(workspace.Snapshot(Now).Items).State);
    }

    [Fact]
    public void FailedSettingsReplacement_PreservesPreviousDiskAndMemoryState()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        var path = Path.Combine(root, "accounts", Account, "workspace-settings.json");
        var before = File.ReadAllText(path);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Record.Exception(() => Confirm(workspace, "物理"));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal("数学", Assert.Single(workspace.Snapshot(Now).Groups).Course);
        Assert.Equal("数学", Assert.Single(Workspace().Snapshot(Now).Groups).Course);
    }

    [Fact]
    public void Priority_CurrentClockRetainsExplicitConsequenceIncrease()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        workspace.Ingest(Message(text: "10月20日18点提交统计作业，逾期不收"), Pinned(), MessageOrigin.Realtime, "live", Now);
        Assert.Equal(2, Assert.Single(workspace.Snapshot(Now).Items).Priority);
        Assert.Equal(0, Assert.Single(workspace.Snapshot(Now.AddDays(5)).Items).Priority);
    }

    [Fact]
    public void Priority_LegacyTaskJsonWithoutOverrideStillLoadsAndUsesCurrentClock()
    {
        var workspace = Workspace();
        var task = SeedTask(workspace);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Tasks().DatabasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(Tasks().FindTask(task.Id)));
            var legacy = document.RootElement.EnumerateObject().Where(property => property.Name != "PriorityOverride")
                .ToDictionary(property => property.Name, property => property.Value);
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE tasks SET record_json = $json WHERE id = $id;";
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(legacy));
            command.Parameters.AddWithValue("$id", task.Id);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        Assert.Null(Tasks().FindTask(task.Id)!.PriorityOverride);
        Assert.Equal(task.Priority, Assert.Single(Workspace().Snapshot(Now).Items).Priority);
        Assert.Equal(0, Assert.Single(Workspace().Snapshot(Now.AddDays(30)).Items).Priority);
    }

    [Fact]
    public void Priority_P0SaturationDoesNotLoseExplicitConsequenceAfterDateEdit()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        workspace.Ingest(Message(text: "10月12日18点提交统计作业，逾期不收"), Pinned(), MessageOrigin.Realtime, "live", Now);
        var task = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal(0, task.Priority);
        workspace.Execute(Command("accept-time", task) with { Date = "2026-10-20", TimeKind = "deadline" }, Now);
        Assert.Equal(2, Assert.Single(workspace.Snapshot(Now).Items).Priority);
    }

    [Fact]
    public void Priority_NegatedConsequenceDoesNotRaiseEditedTask()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        workspace.Ingest(Message(text: "10月12日18点提交统计作业，不是逾期不收"), Pinned(), MessageOrigin.Realtime, "live", Now);
        var task = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(Command("accept-time", task) with { Date = "2026-10-20", TimeKind = "deadline" }, Now);
        Assert.Equal(3, Assert.Single(workspace.Snapshot(Now).Items).Priority);
    }

    [Fact]
    public void Priority_ConsequenceOfAnotherActionInSameMessageDoesNotRaiseEditedTask()
    {
        var workspace = Workspace();
        Groups(workspace);
        Confirm(workspace);
        workspace.Ingest(Message(text: "10月12日18点提交统计作业，逾期不收；10月12日18点提交英语作业，不扣分"),
            Pinned(), MessageOrigin.Realtime, "live", Now);
        var task = Assert.Single(workspace.Snapshot(Now).Items, item => item.Title.Contains("英语作业", StringComparison.Ordinal));
        workspace.Execute(Command("accept-time", task) with { Date = "2026-10-20", TimeKind = "deadline" }, Now);
        var changed = workspace.Snapshot(Now).Items.Single(item => item.Id == task.Id);
        Assert.Equal(3, changed.Priority);
    }

    [Fact]
    public void Review_ExplicitAcceptCanPreserveUnconfirmedCourseAndDateOnly()
    {
        var workspace = Workspace();
        workspace.Ingest(Message(text: "10月14日提交统计作业"), Pinned(), MessageOrigin.History, "history", Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Null(review.Course);
        workspace.Execute(Command("accept-review", review), Now);
        var task = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("task", task.Kind);
        Assert.Null(task.Course);
        Assert.Equal("2026-10-14", task.Due);
        Assert.Null(task.Time);
    }

    [Fact]
    public void MessageTimeUsesLatestOriginalEvidenceAndManualCreationTime()
    {
        var workspace = Workspace();
        var original = Message(text: "10月20日提交统计作业") with { SentAt = Now.AddDays(-3) };
        Assert.True(workspace.Ingest(original, Pinned(), MessageOrigin.History, "sync", Now));
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal(original.SentAt, review.MessageTime);
        workspace.Execute(Command("ignore", review), Now);
        Assert.Equal(original.SentAt, Assert.Single(workspace.Snapshot(Now).Items).MessageTime);
        var ignored = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(Command("undo-ignore", ignored) with { UndoOperationId = ignored.UndoOperationId }, Now);
        review = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(Command("accept-review", review), Now);
        var task = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal(original.SentAt, task.MessageTime);
        var newer = Message("newer", text: "补充说明") with { SentAt = Now.AddDays(-1) };
        Assert.True(workspace.Enqueue(newer, Pinned(), MessageOrigin.History, "sync", Now));
        var linked = Tasks().SavePending("remind", Candidate(action: RuleAction.Remind,
            disposition: RuleDisposition.Pending, target: task.Id) with { Evidence = [newer.Key] }, "save-remind", Now);
        workspace.Execute(new(Account, "accept-review", linked.Id, "accept-remind", linked.Version,
            TargetTaskVersion: task.Version), Now);
        Assert.Equal(newer.SentAt, Assert.Single(Workspace().Snapshot(Now).Items).MessageTime);
        workspace.Execute(new(Account, "create-task", "manual", "manual-create", 0,
            Date: "2026-12-01", TimeKind: "deadline", Title: "手动任务", Course: "手动课程"), Now);
        Assert.Equal(Now, Workspace().Snapshot(Now).Items.Single(item => item.Id == "manual").MessageTime);
    }

    [Fact]
    public void Evidence_PreservesTextAndImageCountWithoutAcceptingImageDate()
    {
        var workspace = Workspace();
        var message = Message(text: "提交统计作业") with
        {
            Segments = [new(SegmentKind.Text, "提交统计作业"), new(SegmentKind.Image, "10月20日截止", AssetId: "image-1")]
        };
        workspace.Ingest(message, Pinned(), MessageOrigin.History, "history", Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Null(review.Due);
        Assert.Equal("2026-10-20", review.SuggestedDue);
        var evidence = Assert.Single(review.Evidence);
        Assert.Equal("提交统计作业", evidence.Text);
        Assert.Equal(1, evidence.ImageCount);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
