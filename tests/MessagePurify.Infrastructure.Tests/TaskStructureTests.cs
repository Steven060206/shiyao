using MessagePurify.Core;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MessagePurify.Infrastructure.Tests;

public sealed class TaskStructureTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string Group = "9007199254740995";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-structure-" + Guid.NewGuid().ToString("N"));
    private AccountTaskStore Store() => new(root, Account);
    private static TaskTime Time(int day = 20) => new(TimeKind.Deadline, new(2026, 10, day), null, "Asia/Shanghai");
    private static RuleCandidate Candidate(string title, string messageId, string course = "数学", int day = 20) =>
        new(title, RuleAction.Submit, RuleDisposition.Task, course, Time(day), null, null, 2,
            new(new("明确行动", true), new("本人相关", true), new("课程已确认", true),
                new("可靠时间", true), new("原消息", true), new("独立事项", true)), [], [new(Account, Group, messageId)]);
    private TaskItem Create(string id, string message, int day = 20) => Store().CreateTask(id,
        Candidate("作业" + id, message, day: day), "create-" + id, Now);
    private static TaskMergeSelection Choose(string id) => new(id, id, id, id, id);
    private static TaskSplitDraft Draft(TaskItem task, TaskTime? time = null, TaskState state = TaskState.Active) =>
        new("拆出实验", "物理", time, 1, state, task.Evidence);

    [Fact]
    public void Merge_SelectsEveryFieldIndependentlyAndPreservesOriginalEvidenceAndHistory()
    {
        var keep = Create("keep", "m1");
        var other = Store().CreateTask("other", Candidate("参加讲座", "m2", "物理", 22) with { Action = RuleAction.Attend }, "create-other", Now);
        other = Store().EditTask(other.Id, "edit-other", other.Version, other.Title, other.Course, other.Time, 0, Now);
        other = Store().Complete(other.Id, "complete-other", other.Version, Now);
        var result = Store().MergeTasks(keep.Id, other.Id, "merge", keep.Version, other.Version,
            new(other.Id, keep.Id, other.Id, other.Id, keep.Id), Now.AddMinutes(1));

        Assert.Equal("参加讲座", result.Title);
        Assert.Equal(RuleAction.Attend, result.Action);
        Assert.Equal("数学", result.Course);
        Assert.Equal(Time(22), result.Time);
        Assert.True(result.TimeProtected);
        Assert.Equal(0, result.PriorityOverride);
        Assert.Equal(TaskState.Active, result.State);
        Assert.Null(result.CompletedAt);
        Assert.Equal(2, result.Version);
        Assert.Equal(2, result.TimeVersion);
        Assert.Equal(2, result.Evidence.Count);
        Assert.Single(Store().ListTasks());
        Assert.Equal(2, Store().ListTasks(includeMerged: true).Count);
        var source = Store().FindTask("other")!;
        Assert.Equal("keep", source.MergedIntoId);
        Assert.Equal("参加讲座", source.Title);
        Assert.Equal(TaskState.Completed, source.State);
        Assert.Single(source.Evidence);
        Assert.Equal("keep", Store().ResolveTaskId("other"));
        Assert.Contains("other", result.RelatedItemIds!);
        Assert.Equal(3L, Count("task_evidence"));
        Assert.Contains(Store().ListTaskHistory(), entry => entry.Kind == "merge-tasks" && entry.After.Id == "keep");
        Assert.Contains(Store().ListTaskHistory(), entry => entry.Kind == "merge-tasks" && entry.After.Id == "other");
    }

    [Fact]
    public void Merge_ConfirmedDisplayedPrioritySurvivesChoosingAnotherTasksLaterDateAndReplay()
    {
        var keep = Store().CreateManualTask("keep", "今天任务", "数学", Time(13), null, "create-keep", Now);
        var future = new TaskTime(TimeKind.Deadline, new(2026, 11, 20), null, "Asia/Shanghai");
        var other = Store().CreateManualTask("other", "下月任务", "数学", future, null, "create-other", Now);
        Assert.Equal(0, keep.Priority);
        Assert.Equal(3, other.Priority);
        Assert.Null(keep.PriorityOverride);
        var selection = Choose("keep") with { TimeTaskId = "other" };

        var merged = Store().MergeTasks("keep", "other", "merge", 1, 1, selection, Now, confirmedPriority: 0);

        Assert.Equal(future, merged.Time);
        Assert.Equal(0, merged.Priority);
        Assert.Equal(0, merged.PriorityOverride);
        Assert.Equal(0, Store().MergeTasks("keep", "other", "merge", 1, 1, selection,
            Now.AddDays(1), confirmedPriority: 0).PriorityOverride);
        Assert.Throws<InvalidOperationException>(() => Store().MergeTasks("keep", "other", "merge", 1, 1,
            selection, Now, confirmedPriority: 3));
        Assert.Equal(3L, Count("operations"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Merge_InvalidConfirmedPriorityCannotMutateTasksOrSaveOperation(int priority)
    {
        Create("keep", "m1"); Create("other", "m2");
        Assert.Throws<ArgumentException>(() => Store().MergeTasks("keep", "other", "merge", 1, 1,
            Choose("keep"), Now, confirmedPriority: priority));
        Assert.Equal(2, Store().ListTasks().Count);
        Assert.Equal(1, Store().FindTask("keep")!.Version);
        Assert.Equal(2L, Count("operations"));
    }

    [Theory]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.Archived)]
    public void Merge_ChosenStateCarriesItsTimestampAndInvalidatesOldUndo(TaskState state)
    {
        var keep = Create("keep", "m1");
        var other = Create("other", "m2");
        other = ChangeState(other, state);
        var merged = Store().MergeTasks("keep", "other", "merge", keep.Version, other.Version,
            Choose("keep") with { StateTaskId = "other" }, Now.AddMinutes(1));
        Assert.Equal(state, merged.State);
        Assert.Null(merged.UndoOperationId);
        Assert.Null(Store().FindTask("other")!.UndoOperationId);
        Assert.Equal(other.CompletedAt, merged.CompletedAt);
        Assert.Equal(other.CancelledAt, merged.CancelledAt);
        Assert.Equal(other.ArchivedAt, merged.ArchivedAt);
        if (state == TaskState.Completed)
            Assert.Equal(TaskState.Completed, Store().UndoComplete("keep", "old-undo", merged.Version,
                Now.AddMinutes(2), "state-other").State);
        Assert.Throws<InvalidOperationException>(() => Store().UndoComplete("other", "source-undo",
            Store().FindTask("other")!.Version, Now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Merge_StaleVersionRollsBackBothTasksAndAudit(bool staleKeep)
    {
        Create("keep", "m1"); Create("other", "m2");
        Assert.Throws<InvalidOperationException>(() => Store().MergeTasks("keep", "other", "merge",
            staleKeep ? 0 : 1, staleKeep ? 1 : 0, Choose("keep"), Now));
        Assert.Equal(2, Store().ListTasks().Count);
        Assert.Equal(1, Store().FindTask("keep")!.Version);
        Assert.Equal(1, Store().FindTask("other")!.Version);
        Assert.Equal(2L, Count("operations"));
    }

    [Fact]
    public void Merge_ReplayReturnsCurrentSurvivorWithoutRevertingLaterEdits()
    {
        Create("keep", "m1"); Create("other", "m2");
        var merged = Store().MergeTasks("keep", "other", "merge", 1, 1, Choose("keep"), Now);
        Store().EditTask("keep", "edit", merged.Version, "之后修改", "数学", Time(25), 1, Now);
        Assert.Equal("之后修改", Store().MergeTasks("keep", "other", "merge", 1, 1, Choose("keep"), Now).Title);
        Assert.Throws<InvalidOperationException>(() => Store().MergeTasks("keep", "other", "merge", 1, 1, Choose("other"), Now));
        Assert.Equal(4L, Count("operations"));
    }

    [Fact]
    public void Merge_RejectsSameTaskForeignChoiceAndAlreadyMergedSource()
    {
        Create("keep", "m1"); Create("other", "m2"); Create("third", "m3");
        Assert.Throws<ArgumentException>(() => Store().MergeTasks("keep", "keep", "same", 1, 1, Choose("keep"), Now));
        Assert.Throws<ArgumentException>(() => Store().MergeTasks("keep", "other", "bad", 1, 1,
            Choose("keep") with { TimeTaskId = "third" }, Now));
        Store().MergeTasks("keep", "other", "merge", 1, 1, Choose("keep"), Now);
        Assert.Throws<InvalidOperationException>(() => Store().MergeTasks("third", "other", "merge-again", 1, 2, Choose("third"), Now));
        Assert.Equal(2, Store().ListTasks().Count);
    }

    [Fact]
    public void Merge_ResolvesMultipleRedirectsAndOriginalCandidateCannotRecreateSource()
    {
        Create("first", "m1"); Create("second", "m2"); Create("third", "m3");
        Store().MergeTasks("second", "first", "merge1", 1, 1, Choose("second"), Now);
        Store().MergeTasks("third", "second", "merge2", 1, 2, Choose("third"), Now);
        Assert.Equal("third", Store().ResolveTaskId("first"));
        Assert.Equal("third", Store().ResolveTaskId("second"));
        Assert.Equal("third", Store().CreateTask("new-copy", Candidate("作业first", "m1"), "rerun", Now).Id);
        Assert.Single(Store().ListTasks());
        Assert.Null(Store().FindTask("new-copy"));
    }

    [Fact]
    public void Merge_AccountDatabaseCannotUseOtherAccountsTask()
    {
        Create("keep", "m1");
        new AccountTaskStore(root, "10002").CreateManualTask("other", "作业", "物理", Time(), 1, "manual", Now);
        Assert.Throws<KeyNotFoundException>(() => Store().MergeTasks("keep", "other", "merge", 1, 1, Choose("keep"), Now));
        Assert.Single(Store().ListTasks());
        Assert.Single(new AccountTaskStore(root, "10002").ListTasks());
    }

    [Fact]
    public void Split_CreatesIndependentTaskKeepsAllParentEvidenceAndRecordsRelatedHistory()
    {
        var parent = Create("parent", "m1");
        parent = Store().Complete(parent.Id, "complete", 1, Now);
        var result = Store().SplitTask(parent.Id, "child", "split", parent.Version, Draft(parent, Time(23)), Now);
        var child = Assert.IsType<TaskItem>(result.Task);
        Assert.Null(result.Review);
        Assert.Equal("拆出实验", child.Title);
        Assert.Equal("物理", child.Course);
        Assert.Equal(RuleAction.Submit, child.Action);
        Assert.Equal(TaskState.Active, child.State);
        Assert.True(child.TimeProtected);
        Assert.Equal(1, child.PriorityOverride);
        Assert.Equal(parent.Evidence, child.Evidence);
        Assert.Equal(parent.Evidence, result.Parent.Evidence);
        Assert.Equal(TaskState.Completed, result.Parent.State);
        Assert.Equal(3, result.Parent.Version);
        Assert.Null(result.Parent.UndoOperationId);
        Assert.Contains("child", result.Parent.RelatedItemIds!);
        Assert.Contains("parent", child.RelatedItemIds!);
        Assert.Equal(2L, Count("task_evidence"));
        Assert.Contains(Store().ListTaskHistory(), entry => entry.Kind == "split-task" && entry.After.Id == "parent");
        Assert.Contains(Store().ListTaskHistory(), entry => entry.Kind == "split-task" && entry.After.Id == "child");
    }

    [Theory]
    [InlineData(TaskState.Active)]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.Archived)]
    public void Split_UsesExplicitChildStateAndPreservesItThroughPendingConfirmation(TaskState state)
    {
        var parent = Create("parent", "m1");
        var result = Store().SplitTask(parent.Id, "review", "split", 1, Draft(parent, state: state), Now);
        Assert.Null(result.Task);
        var review = Assert.IsType<PendingReview>(result.Review);
        Assert.Equal(ReviewState.Pending, review.State);
        Assert.Null(review.Candidate.Time);
        Assert.Null(review.Candidate.TargetTaskId);
        Assert.Equal(parent.Evidence, review.Candidate.Evidence);
        Assert.Contains("review", result.Parent.RelatedItemIds!);
        Assert.Throws<ArgumentException>(() => Store().AcceptReview("review", "child", "accept-bad", 1, null, Now));
        var accepted = Store().AcceptReview("review", "child", "accept", 1, Time(24), Now);
        Assert.Equal(state, accepted.State);
        Assert.Contains("parent", accepted.RelatedItemIds!);
        Assert.Equal(TaskState.Active, Store().FindTask("parent")!.State);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("unrelated")]
    [InlineData("empty")]
    public void Split_RejectsEvidenceOutsideOriginalTask(string kind)
    {
        var parent = Create("parent", "m1");
        IReadOnlyList<MessageKey> evidence = kind switch
        {
            "foreign" => [new("10002", Group, "m1")],
            "unrelated" => [new(Account, Group, "new-message")],
            _ => []
        };
        Assert.Throws<ArgumentException>(() => Store().SplitTask(parent.Id, "child", "split", 1,
            Draft(parent, Time()) with { Evidence = evidence }, Now));
        Assert.Single(Store().ListTasks());
        Assert.Empty(Store().ListReviews());
        Assert.Equal(1, Store().FindTask(parent.Id)!.Version);
    }

    [Fact]
    public void Split_ManualTasksMayHaveNoEvidenceButRequireDate()
    {
        var parent = Store().CreateManualTask("parent", "手工事项", "数学", Time(), null, "create", Now);
        Assert.Throws<ArgumentException>(() => Store().SplitTask("parent", "undated", "no-date", 1, Draft(parent), Now));
        var result = Store().SplitTask("parent", "child", "split", 1, Draft(parent, Time(23)), Now);
        Assert.Equal("manual", result.Task!.Source);
        Assert.Empty(result.Task.Evidence);
        Assert.Empty(Store().ListReviews());
        Assert.Equal(0L, Count("task_evidence"));
    }

    [Fact]
    public void Split_ReplayReturnsCurrentItemsAndStaleOrConflictingIdsDoNotMutateParent()
    {
        var parent = Create("parent", "m1");
        var draft = Draft(parent, Time());
        var result = Store().SplitTask("parent", "child", "split", 1, draft, Now);
        Store().Complete("child", "complete-child", 1, Now);
        Assert.Equal(TaskState.Completed, Store().SplitTask("parent", "child", "split", 1, draft, Now).Task!.State);
        Assert.Throws<InvalidOperationException>(() => Store().SplitTask("parent", "other", "split", 1, draft, Now));
        Assert.Throws<InvalidOperationException>(() => Store().SplitTask("parent", "other", "stale", 1, draft, Now));
        Assert.Throws<InvalidOperationException>(() => Store().SplitTask("parent", "child", "duplicate", result.Parent.Version, draft, Now));
        Assert.Equal(2, Store().FindTask("parent")!.Version);
        Assert.Equal(2, Store().ListTasks().Count);
    }

    [Fact]
    public void Split_PendingReplayDoesNotUndoIgnoreOrCreateAnotherReview()
    {
        var parent = Create("parent", "m1"); var draft = Draft(parent);
        Store().SplitTask("parent", "review", "split", 1, draft, Now);
        Store().IgnoreReview("review", "ignore", 1, Now);
        Assert.Equal(ReviewState.Ignored, Store().SplitTask("parent", "review", "split", 1, draft, Now).Review!.State);
        Assert.Single(Store().ListReviews());
    }

    [Fact]
    public void KeepSeparate_PersistsBothSidesAndExplicitMergeMayReplaceDecision()
    {
        Create("keep", "m1"); Create("other", "m2");
        var result = Store().KeepSeparate("keep", "other", "separate", 1, 1, Now);
        Assert.Contains("other", result.SeparateTaskIds!);
        Assert.Contains("keep", Store().FindTask("other")!.SeparateTaskIds!);
        Assert.Equal(2, result.Version);
        Assert.Equal(2, Store().FindTask("other")!.Version);
        Assert.Equal(2, Store().KeepSeparate("keep", "other", "separate", 1, 1, Now).Version);
        Assert.Contains(Store().ListTaskHistory(), entry => entry.Kind == "keep-separate" && entry.After.Id == "other");
        var merged = Store().MergeTasks("keep", "other", "merge", 2, 2, Choose("keep"), Now);
        Assert.DoesNotContain("other", merged.SeparateTaskIds!);
        Assert.Contains(Store().ListTaskHistory(), entry => entry.Kind == "keep-separate");
    }

    [Fact]
    public void AcceptReview_PreexistingSuggestionResolvesMergedTargetAndChecksSurvivorVersion()
    {
        Create("keep", "m1"); Create("other", "m2");
        var suggestion = Candidate("原作业取消", "m3") with
        { Action = RuleAction.Cancel, Disposition = RuleDisposition.Change, TargetTaskId = "other", Time = null };
        Store().SavePending("review", suggestion, "save-review", Now);
        var merged = Store().MergeTasks("keep", "other", "merge", 1, 1, Choose("keep"), Now);
        Assert.Throws<InvalidOperationException>(() => Store().AcceptReview("review", "keep", "stale", 1, null, Now, 1));
        var result = Store().AcceptReview("review", "other", "accept", 1, null, Now, merged.Version);
        Assert.Equal(TaskState.Cancelled, result.State);
        Assert.Equal("keep", Store().FindReview("review")!.AcceptedTaskId);
        Assert.Equal(TaskState.Active, Store().FindTask("other")!.State);
    }

    [Fact]
    public void Split_OnlySelectedEvidenceIsCopiedAndParentKeepsItsWholeEvidence()
    {
        Create("parent", "m1"); Create("other", "m2");
        var parent = Store().MergeTasks("parent", "other", "merge", 1, 1, Choose("parent"), Now);
        var result = Store().SplitTask("parent", "child", "split", parent.Version,
            Draft(parent, Time()) with { Evidence = [parent.Evidence[1]] }, Now);
        Assert.Equal(new MessageKey(Account, Group, "m2"), Assert.Single(result.Task!.Evidence));
        Assert.Equal(2, result.Parent.Evidence.Count);
        Assert.Equal(4L, Count("task_evidence"));
    }

    [Fact]
    public void Merge_DatabaseFailureRollsBackSurvivorSourceAndEvidence()
    {
        Create("keep", "m1"); Create("other", "m2");
        ExecuteSql("CREATE TRIGGER fail_structure BEFORE UPDATE ON tasks WHEN NEW.id = 'other' BEGIN SELECT RAISE(ABORT, 'synthetic'); END;");
        Assert.Throws<SqliteException>(() => Store().MergeTasks("keep", "other", "merge", 1, 1, Choose("keep"), Now));
        Assert.Equal(2, Store().ListTasks().Count);
        Assert.Equal(1, Store().FindTask("keep")!.Version);
        Assert.Single(Store().FindTask("keep")!.Evidence);
        Assert.Null(Store().FindTask("other")!.MergedIntoId);
        Assert.Equal(2L, Count("operations"));
        Assert.Equal(2L, Count("task_evidence"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Split_DatabaseFailureRollsBackChildOrReviewAndParent(bool dated)
    {
        var parent = Create("parent", "m1");
        ExecuteSql("CREATE TRIGGER fail_structure BEFORE UPDATE ON tasks WHEN NEW.id = 'parent' BEGIN SELECT RAISE(ABORT, 'synthetic'); END;");
        Assert.Throws<SqliteException>(() => Store().SplitTask("parent", "child", "split", 1,
            Draft(parent, dated ? Time() : null), Now));
        Assert.Single(Store().ListTasks());
        Assert.Empty(Store().ListReviews());
        Assert.Equal(1, Store().FindTask("parent")!.Version);
        Assert.Equal(1L, Count("operations"));
        Assert.Equal(1L, Count("task_evidence"));
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("split")]
    [InlineData("keep-separate")]
    public void StructureChange_InvalidatesOldIgnoredLinkedReviewUndo(string operation)
    {
        var parent = Create("parent", "m1"); Create("other", "m2");
        var candidate = Candidate("旧延期建议", "m3") with
        { Action = RuleAction.Postpone, TargetTaskId = "parent", Disposition = RuleDisposition.Change };
        Store().SavePending("review", candidate, "save-review", Now);
        var ignored = Store().IgnoreReview("review", "ignore", 1, Now);
        if (operation == "merge") Store().MergeTasks("parent", "other", "structure", 1, 1, Choose("parent"), Now);
        else if (operation == "split") Store().SplitTask("parent", "child", "structure", 1, Draft(parent, Time()), Now);
        else Store().KeepSeparate("parent", "other", "structure", 1, 1, Now);
        Assert.Throws<InvalidOperationException>(() => Store().UndoIgnored("review", "undo-stale", ignored.Version, Now, "ignore"));
        var current = Store().FindReview("review")!;
        Assert.Equal(ReviewState.Ignored, current.State);
        Assert.Null(current.UndoOperationId);
        Assert.Equal(ReviewState.Ignored, Store().UndoIgnored("review", "undo-current", current.Version, Now, "ignore").State);
    }

    [Fact]
    public void Route_MergedSourceQuoteUpdatesSurvivorOnceAndLeavesSourceHistoryIntact()
    {
        var first = RouteOriginal("m1", "10月20日18点提交数学作业", Now.AddHours(-2));
        var second = RouteOriginal("m2", "10月22日18点提交实验报告", Now.AddHours(-1));
        var merged = Store().MergeTasks(first.Id, second.Id, "merge", first.Version, second.Version, Choose(first.Id), Now);
        Ingest(new(new(Account, Group, "cancel"), "teacher", Now,
            [new(SegmentKind.Text, "数学作业取消"), new(SegmentKind.Quote, "原通知", second.Evidence[0])]));
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        Assert.Equal(TaskState.Cancelled, Store().FindTask(first.Id)!.State);
        Assert.Equal(TaskState.Active, Store().FindTask(second.Id)!.State);
        Assert.Equal(first.Id, Store().FindTask(second.Id)!.MergedIntoId);
        Assert.Equal(merged.Version + 1, Store().FindTask(first.Id)!.Version);
        Assert.Empty(Store().ListReviews());
        Assert.Equal(0, Router().ProcessAndRoutePending(Context(), Pinned()));
        Assert.Single(Store().ListTasks());
    }

    [Fact]
    public void Route_ReferenceSharedBySplitTasksStaysPendingAndReplayKeepsManualDecision()
    {
        var parent = RouteOriginal("m1", "10月20日18点提交数学作业", Now.AddHours(-2));
        var split = Store().SplitTask(parent.Id, "child", "split", parent.Version, Draft(parent, Time()) with { Course = "数学" }, Now);
        Ingest(new(new(Account, Group, "cancel"), "teacher", Now,
            [new(SegmentKind.Text, "数学作业取消"), new(SegmentKind.Quote, "原通知", parent.Evidence[0])]));
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        var review = Assert.Single(Store().ListReviews());
        Assert.Equal(ReviewState.Pending, review.State);
        Assert.Equal(TaskState.Active, Store().FindTask(parent.Id)!.State);
        Assert.Equal(split.Parent.Version, Store().FindTask(parent.Id)!.Version);
        Assert.Equal(TaskState.Active, Store().FindTask("child")!.State);
        Assert.Equal(0, Router().ProcessAndRoutePending(Context(), Pinned()));
        Assert.Equal(2, Store().ListTasks().Count);
        Assert.Single(Store().ListReviews());
    }

    [Fact]
    public void Split_UndatedStartKindSurvivesRestartAndConfirmation()
    {
        var parent = Create("parent", "m1");
        Store().SplitTask("parent", "review", "split", 1, Draft(parent) with { TimeKind = TimeKind.Start }, Now);
        Assert.Equal(TimeKind.Start, Store().FindReview("review")!.SplitTimeKind);
        var task = Store().AcceptReview("review", "child", "accept", 1, Time() with { Kind = TimeKind.Start }, Now);
        Assert.Equal(TimeKind.Start, task.Time.Kind);
    }

    [Fact]
    public void Merge_ManualAndQqEvidenceKeepsQqSourceAndEvidence()
    {
        var manual = Store().CreateManualTask("manual", "手工补录", "数学", Time(), null, "create-manual", Now);
        var qq = Create("qq", "m1");
        var merged = Store().MergeTasks("manual", "qq", "merge", manual.Version, qq.Version, Choose("manual"), Now);
        Assert.Equal("qq", merged.Source);
        Assert.Single(merged.Evidence);
        var split = Store().SplitTask("manual", "child", "split", merged.Version, Draft(merged, Time()), Now);
        Assert.Equal("qq", split.Task!.Source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Split_ConvertedInformNoticeProducesAcceptableCompleteTask(bool dated)
    {
        var notice = Candidate("普通活动通知", "m1") with { Action = RuleAction.Inform, Disposition = RuleDisposition.Notice };
        var parent = Store().CreateTaskFromNotice("parent", "notice", notice, Time(), 2, "convert", Now);
        var split = Store().SplitTask("parent", "child", "split", parent.Version, Draft(parent, dated ? Time() : null), Now);
        var child = split.Task ?? Store().AcceptReview("child", "accepted-child", "accept", 1, Time(), Now);
        Assert.Equal(RuleAction.Complete, child.Action);
        Assert.Equal(TaskState.Active, child.State);
        Assert.Equal(RuleAction.Inform, Store().FindTask("parent")!.Action);
    }

    private AccountMessageStore Messages() => new(root, Account);
    private AccountCandidateRouter Router() => new(Messages(), Store());
    private static HashSet<string> Pinned() => [Group];
    private static RuleContext Context() => new(Account, Group, "Asia/Shanghai", Now, "数学", new HashSet<string> { "teacher" });
    private void Ingest(SchoolMessage message) => Assert.True(new LocalMessageProcessor(Messages()).Ingest(message,
        Pinned(), Context(), MessageOrigin.Realtime, "structure-test", Now));
    private TaskItem RouteOriginal(string messageId, string text, DateTimeOffset sentAt)
    {
        Ingest(new(new(Account, Group, messageId), "teacher", sentAt, [new(SegmentKind.Text, text)]));
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        return Assert.Single(Store().ListTasks(), task => task.Evidence.Any(key => key.MessageId == messageId));
    }

    private void ExecuteSql(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Store().DatabasePath, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private TaskItem ChangeState(TaskItem task, TaskState state) => state switch
    {
        TaskState.Completed => Store().Complete(task.Id, "state-" + task.Id, task.Version, Now),
        TaskState.Cancelled => Store().Cancel(task.Id, "state-" + task.Id, task.Version, Now),
        TaskState.Archived => Store().Archive(task.Id, "state-" + task.Id, task.Version, Now),
        _ => task
    };

    private long Count(string table)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Store().DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table;
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
