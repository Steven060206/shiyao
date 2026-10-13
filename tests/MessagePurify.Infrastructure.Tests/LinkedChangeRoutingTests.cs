using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using MessagePurify.Infrastructure.Application;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class LinkedChangeRoutingTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string Group = "9007199254740995";
    private const string OtherGroup = "9007199254741000";
    private const string Sender = "9007199254740996";
    private static readonly DateTimeOffset SentAt = new(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Now = SentAt.AddHours(1);
    private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "MessagePurify-linked-routing-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ChangeHistory_AutomaticReminderKeepsReadWhileNewRequirementWithSameTimeBecomesUnread()
    {
        var original = CreateOriginal();
        var workspace = new AccountWorkspace(dataRoot, Account);
        var initial = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(new(Account, "mark-read", initial.Id, "read-initial", initial.Version), Now);
        var reminder = Change("history-reminder", RuleAction.Remind, original.Evidence[0], SentAt.AddMinutes(1));
        Enqueue(reminder);
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        var reminded = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.False(reminded.Unread);
        Assert.Null(reminded.ChangeLabel);
        Assert.Equal("催交补充", reminded.Changes![0].Label);
        Assert.Equal(Sender, reminded.Changes[0].Evidence[0].SenderId);
        Assert.Empty(reminded.Changes[0].Fields);
        var requirement = Change("same-time-requirement", RuleAction.NewRequirement, original.Evidence[0], SentAt.AddMinutes(2)) with
        {
            Segments = [new(SegmentKind.Text, "统计作业新增要求：10月13日18点补交实验数据"),
                new(SegmentKind.Quote, "原通知", original.Evidence[0])]
        };
        Enqueue(requirement);
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        var changed = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal(initial.Title, changed.Title);
        Assert.Equal(initial.Due, changed.Due);
        Assert.Equal(initial.Time, changed.Time);
        Assert.True(changed.Unread);
        Assert.Equal("新要求", changed.ChangeLabel);
        Assert.Equal("新增要求", changed.Changes![0].Label);
        Assert.Single(changed.Changes[0].Evidence);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(
            new(Account, "mark-read", reminded.Id, "stale-read", reminded.Version), Now));
        Assert.True(Assert.Single(workspace.Snapshot(Now).Items).Unread);
        Assert.Equal(0, Router().ProcessAndRoutePending(Context(), Pinned()));
        Assert.Equal(3, new AccountWorkspace(dataRoot, Account).Snapshot(Now).Items.Single().Changes!.Count);
    }

    [Theory]
    [InlineData(RuleAction.Postpone, "已延期", "任务延期")]
    [InlineData(RuleAction.Cancel, "已取消", "任务取消")]
    public void ChangeHistory_AutomaticChangesHaveReadableEvidenceAndImportantLabels(RuleAction action, string badge, string label)
    {
        var original = CreateOriginal();
        var workspace = new AccountWorkspace(dataRoot, Account);
        var initial = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(new(Account, "mark-read", initial.Id, "read-initial", initial.Version), Now);
        var change = Change("history-change", action, original.Evidence[0], SentAt.AddMinutes(1));
        Enqueue(change);
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        var updated = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.True(updated.Unread);
        Assert.Equal(badge, updated.ChangeLabel);
        var history = updated.Changes![0];
        Assert.Equal(label, history.Label);
        Assert.Single(history.Evidence);
        Assert.Contains(history.Fields, field => field.Field == (action == RuleAction.Cancel ? "状态" : "正式日期"));
    }

    [Fact]
    public void ProcessAndRoutePending_ReverseEnqueueRoutesOriginalPostponementThenCancellation()
    {
        var original = Original();
        var postponed = Change("postpone", RuleAction.Postpone, original.Key, SentAt.AddMinutes(1));
        var cancelled = Change("cancel", RuleAction.Cancel, original.Key, SentAt.AddMinutes(2));
        Enqueue(cancelled);
        Enqueue(postponed);
        Enqueue(original);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned(), limit: 1));
        var task = Assert.Single(Tasks().ListTasks());
        Assert.Equal(new DateOnly(2026, 10, 13), task.Time.Date);
        Assert.Equal(original.Key, Assert.Single(task.Evidence));
        Assert.Null(Messages().Find(postponed.Key)!.Extraction);
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned(), limit: 1));
        var updated = Tasks().FindTask(task.Id)!;
        Assert.Equal(TaskState.Active, updated.State);
        Assert.Equal(new DateOnly(2026, 10, 20), updated.Time.Date);
        Assert.Equal(task.TimeVersion + 1, updated.TimeVersion);
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned(), limit: 1));

        var final = Assert.Single(Tasks().ListTasks());
        Assert.Equal(task.Id, final.Id);
        Assert.Equal(TaskState.Cancelled, final.State);
        Assert.Equal(updated.Time, final.Time);
        Assert.Equal(new[] { original.Key, postponed.Key, cancelled.Key }, final.Evidence);
        Assert.Empty(Tasks().ListReviews());
        Assert.Equal(3L, Scalar(Router().RoutingDatabasePath, "SELECT COUNT(*) FROM route_receipts;"));
        Assert.Equal(0, Router().ProcessAndRoutePending(Context(), Pinned()));
    }

    [Fact]
    public void RoutePending_BatchExtractedPendingChangesAssociateWithTaskCreatedInSamePass()
    {
        var original = Original();
        var postponed = Change("postpone", RuleAction.Postpone, original.Key, SentAt.AddMinutes(1));
        var cancelled = Change("cancel", RuleAction.Cancel, original.Key, SentAt.AddMinutes(2));
        Enqueue(cancelled);
        Enqueue(postponed);
        Enqueue(original);
        Assert.Equal(3, new LocalMessageProcessor(Messages()).ProcessPending(Context()));
        Assert.Equal(RuleDisposition.Pending, Candidate(postponed).Disposition);
        Assert.Null(Candidate(postponed).TargetTaskId);

        Assert.Equal(3, Router().RoutePending(Context(), Pinned()));

        var task = Assert.Single(Tasks().ListTasks());
        Assert.Equal(TaskState.Cancelled, task.State);
        Assert.Equal(new DateOnly(2026, 10, 20), task.Time.Date);
        Assert.Equal(new[] { original.Key, postponed.Key, cancelled.Key }, task.Evidence);
        Assert.Empty(Tasks().ListReviews());
    }

    [Fact]
    public void RoutePending_EarlierRawMessageBlocksLaterSavedCandidate()
    {
        var original = Original();
        var cancelled = Change("cancel", RuleAction.Cancel, original.Key, SentAt.AddMinutes(1));
        SaveExtracted(cancelled);
        Enqueue(original);

        Assert.Equal(0, Router().RoutePending(Context(), Pinned()));

        Assert.Empty(Tasks().ListTasks());
        Assert.Empty(Tasks().ListReviews());
        Assert.Null(Messages().Find(original.Key)!.Extraction);
        Assert.NotNull(Messages().Find(cancelled.Key)!.Extraction);
        Assert.Equal(2, Router().ProcessAndRoutePending(Context(), Pinned()));
        Assert.Equal(TaskState.Cancelled, Assert.Single(Tasks().ListTasks()).State);
        Assert.Empty(Tasks().ListReviews());
    }

    [Theory]
    [InlineData(RuleAction.Postpone)]
    [InlineData(RuleAction.Cancel)]
    public void ProtectedTime_ConflictingPostponementNeedsReviewButCancellationApplies(RuleAction action)
    {
        var original = CreateOriginal();
        var protectedTask = Tasks().AcceptTime(original.Id, "protect-time", original.Version, original.Time, Now.AddMinutes(1));
        var change = Change("protected-change", action, original.Evidence[0], SentAt.AddMinutes(1));
        Enqueue(change);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));

        var current = Assert.Single(Tasks().ListTasks());
        Assert.Equal(protectedTask.Time, current.Time);
        Assert.Equal(protectedTask.TimeVersion, current.TimeVersion);
        Assert.True(current.TimeProtected);
        if (action == RuleAction.Postpone)
        {
            AssertTask(protectedTask, current);
            var review = Assert.Single(Tasks().ListReviews());
            Assert.Equal(ReviewState.Pending, review.State);
            Assert.Equal(original.Id, review.Candidate.TargetTaskId);
            Assert.Equal(new DateOnly(2026, 10, 20), review.Candidate.Time!.Date);
        }
        else
        {
            Assert.Equal(TaskState.Cancelled, current.State);
            Assert.Equal(protectedTask.Version + 1, current.Version);
            Assert.Contains(change.Key, current.Evidence);
            Assert.Empty(Tasks().ListReviews());
        }
    }

    [Fact]
    public void EditedTask_AutomaticNewRequirementKeepsManualTitleCourseAndPriority()
    {
        var original = CreateOriginal();
        var edited = Tasks().EditTask(original.Id, "edit-1", original.Version,
            "我的报告标题", "手动课程", original.Time, 3, Now.AddMinutes(1));
        var context = Context();
        var change = Change("new-requirement", RuleAction.NewRequirement, original.Evidence[0], SentAt.AddMinutes(1)) with
        {
            Segments = [new(SegmentKind.Text, "统计作业新增要求：10月13日18点补交实验数据"),
                new(SegmentKind.Quote, "原通知", original.Evidence[0])]
        };
        Enqueue(change, context);

        Assert.Equal(1, Router().ProcessAndRoutePending(context, Pinned()));

        var current = Tasks().FindTask(original.Id)!;
        Assert.Equal(edited.Title, current.Title);
        Assert.Equal(edited.Course, current.Course);
        Assert.Equal(3, current.PriorityOverride);
        Assert.Equal(edited.Time, current.Time);
        Assert.Contains(change.Key, current.Evidence);
        Assert.Empty(Tasks().ListReviews());
    }

    [Theory]
    [InlineData("手动课程", RuleAction.Cancel)]
    [InlineData(null, RuleAction.Cancel)]
    [InlineData("手动课程", RuleAction.Remind)]
    [InlineData(null, RuleAction.Remind)]
    public void EditedTitleAndCourse_OriginalGroupQuoteStillAssociatesCancellationAndReminder(
        string? course, RuleAction action)
    {
        var original = CreateOriginal();
        var edited = Tasks().EditTask(original.Id, "edit-1", original.Version,
            "我重新命名的事项", course, original.Time, 3, Now.AddMinutes(1));
        var change = Change("quoted-change", action, original.Evidence[0], SentAt.AddMinutes(1));
        Enqueue(change);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));

        var candidate = Candidate(change);
        Assert.Equal(original.Id, candidate.TargetTaskId);
        Assert.Equal(RuleDisposition.Change, candidate.Disposition);
        var current = Tasks().FindTask(original.Id)!;
        Assert.Equal(action == RuleAction.Cancel ? TaskState.Cancelled : TaskState.Active, current.State);
        Assert.Equal(edited.Title, current.Title);
        Assert.Equal(edited.Course, current.Course);
        Assert.Equal(edited.Time, current.Time);
        Assert.Equal(edited.TimeVersion, current.TimeVersion);
        Assert.Equal(3, current.PriorityOverride);
        Assert.Contains(change.Key, current.Evidence);
        Assert.Empty(Tasks().ListReviews());
    }

    [Fact]
    public void CompletedPostponement_PreservesUndoDeadlineAndUndoUsesLatestTime()
    {
        var original = CreateOriginal();
        var completed = Tasks().Complete(original.Id, "complete-original", original.Version, Now.AddMinutes(1));
        Enqueue(Change("postpone", RuleAction.Postpone, original.Evidence[0], SentAt.AddMinutes(1)));

        Assert.Equal(1, Router().ProcessAndRoutePending(Context() with { Now = Now.AddMinutes(2) }, Pinned()));

        var postponed = Tasks().FindTask(original.Id)!;
        Assert.Equal(TaskState.Completed, postponed.State);
        Assert.Equal(completed.CompletedAt, postponed.CompletedAt);
        Assert.Equal(completed.UndoOperationId, postponed.UndoOperationId);
        Assert.Equal(completed.UndoExpiresAt, postponed.UndoExpiresAt);
        Assert.Equal(new DateOnly(2026, 10, 20), postponed.Time.Date);
        Assert.Empty(Tasks().ListReviews());
        var restored = Tasks().UndoComplete(original.Id, "undo-complete", postponed.Version,
            Now.AddMinutes(3), completedOperationId: completed.UndoOperationId);
        Assert.Equal(TaskState.Active, restored.State);
        Assert.Equal(postponed.Time, restored.Time);
        Assert.Null(restored.UndoExpiresAt);
    }

    [Fact]
    public void CompletedNewRequirement_RestoresActiveKeepsTitleAndCompletionHistory()
    {
        var original = CreateOriginal();
        var completed = Tasks().Complete(original.Id, "complete-original", original.Version, Now.AddMinutes(1));
        var newAction = Change("new-requirement", RuleAction.NewRequirement, original.Evidence[0], SentAt.AddMinutes(1));
        Enqueue(newAction);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context() with { Now = Now.AddMinutes(2) }, Pinned()));

        var restored = Assert.Single(Tasks().ListTasks());
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(TaskState.Active, restored.State);
        Assert.Equal(new DateOnly(2026, 10, 20), restored.Time.Date);
        Assert.Contains(newAction.Key, restored.Evidence);
        Assert.Null(restored.CompletedAt);
        Assert.Null(restored.UndoExpiresAt);
        Assert.Null(restored.UndoOperationId);
        Assert.Empty(Tasks().ListReviews());
        using var oldHistory = JsonDocument.Parse((string)Scalar(Tasks().DatabasePath,
            "SELECT after_json FROM operations WHERE operation_id = 'complete-original';")!);
        Assert.Equal(completed.CompletedAt, oldHistory.RootElement.GetProperty("CompletedAt").GetDateTimeOffset());
        using var request = JsonDocument.Parse((string)Scalar(Tasks().DatabasePath,
            "SELECT request_json FROM operations ORDER BY sequence DESC LIMIT 1;")!);
        Assert.True(ContainsText(request.RootElement, Candidate(newAction).Title));
        using var before = JsonDocument.Parse((string)Scalar(Tasks().DatabasePath,
            "SELECT before_json FROM operations ORDER BY sequence DESC LIMIT 1;")!);
        Assert.True(ContainsTaskState(before.RootElement, TaskState.Completed));
    }

    [Fact]
    public void AcceptOldRemindAfterAutomaticPostponement_DoesNotRestoreStaleTime()
    {
        var original = CreateOriginal();
        var completed = SetState(original, TaskState.Completed);
        var reminder = Change("old-remind", RuleAction.Remind, original.Evidence[0], SentAt.AddMinutes(1));
        Enqueue(reminder);
        Assert.Equal(1, Router().ProcessAndRoutePending(Context() with { TrustedSenders = null }, Pinned()));
        var review = Assert.Single(Tasks().ListReviews());
        Assert.Equal(original.Time, review.Candidate.Time);
        var postponement = Change("new-postpone", RuleAction.Postpone, original.Evidence[0], SentAt.AddMinutes(2));
        Enqueue(postponement);
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        var postponed = Tasks().FindTask(original.Id)!;

        var accepted = Tasks().AcceptReview(review.Id, original.Id, "accept-old-remind", review.Version,
            null, Now.AddMinutes(3), expectedTaskVersion: postponed.Version);

        Assert.Equal(postponed.Time, accepted.Time);
        Assert.Equal(postponed.TimeVersion, accepted.TimeVersion);
        Assert.Equal(completed.State, accepted.State);
        Assert.Equal(completed.CompletedAt, accepted.CompletedAt);
        Assert.Equal(completed.UndoExpiresAt, accepted.UndoExpiresAt);
        Assert.Equal(completed.UndoOperationId, accepted.UndoOperationId);
        Assert.Contains(reminder.Key, accepted.Evidence);
        Assert.Contains(postponement.Key, accepted.Evidence);
        Assert.Equal(ReviewState.Accepted, Tasks().FindReview(review.Id)!.State);
    }

    [Theory]
    [InlineData(TaskState.Archived)]
    [InlineData(TaskState.Cancelled)]
    public void ArchivedOrCancelledNewRequirement_RequiresReviewAndPreservesRecord(TaskState state)
    {
        var original = CreateOriginal();
        var inactive = SetState(original, state);
        var message = Change("new-requirement", RuleAction.NewRequirement, original.Evidence[0], SentAt.AddMinutes(1));
        Enqueue(message);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));

        AssertTask(inactive, Assert.Single(Tasks().ListTasks()));
        var review = Assert.Single(Tasks().ListReviews());
        Assert.Equal(ReviewState.Pending, review.State);
        Assert.Equal(inactive.Id, review.Candidate.TargetTaskId);
        Assert.Equal(RuleAction.NewRequirement, review.Candidate.Action);
        Assert.Contains(message.Key, review.Candidate.Evidence);
    }

    [Theory]
    [InlineData(TaskState.Active)]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Archived)]
    public void Remind_OnlyAddsEvidenceWithoutRestoringOrCopyingExpiredFormalTime(TaskState state)
    {
        var original = CreateOriginal();
        var previous = SetState(original, state);
        var reminder = Change("remind", RuleAction.Remind, original.Evidence[0], SentAt.AddDays(2));
        Enqueue(reminder);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context() with { Now = SentAt.AddDays(2).AddHours(1) }, Pinned()));

        var current = Assert.Single(Tasks().ListTasks());
        Assert.Equal(previous.State, current.State);
        Assert.Equal(previous.Title, current.Title);
        Assert.Equal(previous.Time, current.Time);
        Assert.Equal(previous.TimeVersion, current.TimeVersion);
        Assert.Equal(previous.CompletedAt, current.CompletedAt);
        Assert.Equal(previous.ArchivedAt, current.ArchivedAt);
        Assert.Equal(previous.UndoOperationId, current.UndoOperationId);
        Assert.Equal(previous.UndoExpiresAt, current.UndoExpiresAt);
        Assert.Contains(reminder.Key, current.Evidence);
        Assert.Empty(Tasks().ListReviews());
    }

    [Theory]
    [InlineData("untrusted")]
    [InlineData("question")]
    [InlineData("ocr")]
    public void UnreliableLinkedChange_DoesNotAutomaticallyUpdateTask(string reason)
    {
        var original = CreateOriginal();
        var context = Context();
        var change = Change("unreliable", RuleAction.Postpone, original.Evidence[0], SentAt.AddMinutes(1));
        if (reason == "untrusted")
        {
            SaveExtracted(change, context with { KnownTasks = [Known(original, Sender)] });
            Assert.Equal(RuleDisposition.Change, Candidate(change).Disposition);
            context = context with { TrustedSenders = null };
        }
        else
        {
            var text = reason == "question" ? "统计作业延期到10月20日18点提交吗？" : "统计作业延期到10月20日18点提交";
            change = change with { Segments = [new(reason == "ocr" ? SegmentKind.Image : SegmentKind.Text, text,
                AssetId: reason == "ocr" ? "postponement.png" : null), new(SegmentKind.Quote, "原通知", original.Evidence[0])] };
            Enqueue(change);
        }

        Assert.Equal(1, Router().ProcessAndRoutePending(context, Pinned()));

        AssertTask(original, Assert.Single(Tasks().ListTasks()));
        if (reason == "question") Assert.Empty(Tasks().ListReviews());
        else
        {
            var review = Assert.Single(Tasks().ListReviews());
            Assert.Equal(ReviewState.Pending, review.State);
            if (reason == "untrusted") Assert.False(review.Candidate.Assessments.Source.Reliable);
            if (reason == "ocr")
            {
                Assert.Null(review.Candidate.Time);
                Assert.Equal(new DateOnly(2026, 10, 20), review.Candidate.SuggestedTime!.Date);
            }
        }
    }

    [Theory]
    [InlineData(RuleAction.Cancel)]
    [InlineData(RuleAction.Postpone)]
    [InlineData(RuleAction.NewRequirement)]
    public void LateOlderChange_ReviewsWithoutReplacingLatestStateOrFields(RuleAction action)
    {
        var original = CreateOriginal();
        var latest = Change("latest-postpone", RuleAction.Postpone, original.Evidence[0], SentAt.AddMinutes(2));
        Enqueue(latest);
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        var postponed = Tasks().FindTask(original.Id)!;
        var completed = Tasks().Complete(original.Id, "complete-latest", postponed.Version, Now.AddMinutes(1));
        var older = Change("late-old-change", action, original.Evidence[0], SentAt.AddMinutes(1));
        if (action == RuleAction.Postpone)
            older = older with { Segments = [new(SegmentKind.Text, "统计作业延期到10月18日18点提交"), new(SegmentKind.Quote, "原通知", original.Evidence[0])] };
        Enqueue(older);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));

        AssertTask(completed, Assert.Single(Tasks().ListTasks()));
        var review = Assert.Single(Tasks().ListReviews());
        Assert.Equal(original.Id, review.Candidate.TargetTaskId);
        Assert.Equal(action, review.Candidate.Action);
        Assert.Contains(older.Key, review.Candidate.Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameSecondDifferentMessages_ReviewWhenSendingOrderCannotBeEstablished(bool sameAsLatestChange)
    {
        var original = CreateOriginal();
        var current = original;
        var conflictAt = SentAt;
        if (sameAsLatestChange)
        {
            conflictAt = SentAt.AddMinutes(1);
            Enqueue(Change("first-change", RuleAction.Postpone, original.Evidence[0], conflictAt));
            Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
            current = Tasks().FindTask(original.Id)!;
        }
        var conflict = Change("same-second-cancel", RuleAction.Cancel, original.Evidence[0], conflictAt);
        Enqueue(conflict);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));

        AssertTask(current, Assert.Single(Tasks().ListTasks()));
        Assert.Equal(original.Id, Assert.Single(Tasks().ListReviews()).Candidate.TargetTaskId);
    }

    [Fact]
    public void MissingOriginalSendingTime_RequiresReviewForOtherwiseReliableChange()
    {
        var message = Original();
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message, Context()).Candidates);
        var original = Tasks().CreateTask("imported-task", candidate, "imported-create", Now);
        Enqueue(Change("cancel", RuleAction.Cancel, message.Key, SentAt.AddMinutes(1)));

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));

        AssertTask(original, Assert.Single(Tasks().ListTasks()));
        Assert.Equal(original.Id, Assert.Single(Tasks().ListReviews()).Candidate.TargetTaskId);
    }

    [Fact]
    public void MissingAdditionalEvidenceTime_IsNotHiddenByAnotherKnownOriginalMessage()
    {
        var original = CreateOriginal();
        var missing = Change("missing-evidence", RuleAction.Remind, original.Evidence[0], SentAt.AddSeconds(30));
        var candidate = Assert.Single(MessageRuleExtractor.Extract(missing,
            Context() with { KnownTasks = [Known(original)] }).Candidates);
        var review = Tasks().SavePending("manual-linked-review", candidate, "save-linked-review", Now.AddMinutes(1));
        var current = Tasks().AcceptReview(review.Id, original.Id, "accept-linked-review", review.Version,
            null, Now.AddMinutes(2), expectedTaskVersion: original.Version);
        Assert.Contains(missing.Key, current.Evidence);
        Assert.Null(Messages().Find(missing.Key));
        Enqueue(Change("cancel", RuleAction.Cancel, original.Evidence[0], SentAt.AddMinutes(1)));

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));

        AssertTask(current, Assert.Single(Tasks().ListTasks()));
        Assert.Equal(original.Id, Assert.Single(Tasks().ListReviews(ReviewState.Pending)).Candidate.TargetTaskId);
    }

    [Fact]
    public void CrossGroupQuoteAndCallerKnownTask_CannotAuthorizeCancellation()
    {
        var original = CreateOriginal();
        var context = Context(OtherGroup) with { KnownTasks = [Known(original, Sender)] };
        var change = Change("cross-group-cancel", RuleAction.Cancel, original.Evidence[0], SentAt.AddMinutes(1), OtherGroup);
        Enqueue(change, context);

        Assert.Equal(1, Router().ProcessAndRoutePending(context, Pinned(OtherGroup)));

        AssertTask(original, Assert.Single(Tasks().ListTasks()));
        var review = Assert.Single(Tasks().ListReviews());
        Assert.Null(review.Candidate.TargetTaskId);
        Assert.False(review.Candidate.Assessments.Association.Reliable);
    }

    [Fact]
    public void CallerKnownTasks_AreIgnoredInFavorOfCurrentDatabaseTaskProjection()
    {
        var original = CreateOriginal();
        var forged = Known(original, Sender) with
        {
            Id = "caller-stale-task", Time = original.Time with { Date = new(2026, 11, 1) }, State = TaskState.Cancelled
        };
        var context = Context() with { KnownTasks = [forged] };
        Enqueue(Change("cancel", RuleAction.Cancel, original.Evidence[0], SentAt.AddMinutes(1)), context);

        Assert.Equal(1, Router().ProcessAndRoutePending(context, Pinned()));

        var cancelled = Assert.Single(Tasks().ListTasks());
        Assert.Equal(original.Id, cancelled.Id);
        Assert.Equal(TaskState.Cancelled, cancelled.State);
        Assert.Equal(original.Time, cancelled.Time);
        Assert.Empty(Tasks().ListReviews());
    }

    [Fact]
    public void ChangedCourseBeforeRouting_DoesNotReextractOldPendingChangeIntoAnotherCourse()
    {
        var original = CreateOriginal();
        var change = Change("cancel", RuleAction.Cancel, original.Evidence[0], SentAt.AddMinutes(1));
        SaveExtracted(change);
        var saved = Candidate(change);
        Assert.Equal(RuleDisposition.Pending, saved.Disposition);
        Assert.Null(saved.TargetTaskId);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context() with { ConfirmedCourse = "物理" }, Pinned()));

        AssertTask(original, Assert.Single(Tasks().ListTasks()));
        var review = Assert.Single(Tasks().ListReviews());
        Assert.Equal("数学", review.Candidate.Course);
        Assert.Null(review.Candidate.TargetTaskId);
        Assert.Equal(saved.Title, review.Candidate.Title);
    }

    [Fact]
    public void Receipt_PreventsRestartAndReanalysisFromReplayingNewAction()
    {
        var original = CreateOriginal();
        var newAction = Change("new-action", RuleAction.NewRequirement, original.Evidence[0], SentAt.AddMinutes(1));
        Enqueue(newAction);
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        var changed = Tasks().FindTask(original.Id)!;
        var completed = Tasks().Complete(original.Id, "complete-new-action", changed.Version, Now.AddMinutes(1));
        var saved = Messages().Find(newAction.Key)!;
        var reanalysis = MessageRuleExtractor.Extract(newAction with
        {
            Segments = [new(SegmentKind.Text, "统计作业新增要求：10月25日18点重新提交附件"), new(SegmentKind.Quote, "原通知", original.Evidence[0])]
        }, Context() with { KnownTasks = [Known(completed)] });
        Assert.True(Messages().TrySaveExtraction(newAction.Key, saved.ProcessingVersion, reanalysis, Now.AddMinutes(2)));

        Assert.Equal(0, Router().ProcessAndRoutePending(Context(), Pinned()));
        Assert.Equal(0, Router().RoutePending(Context(), Pinned()));

        AssertTask(completed, Assert.Single(Tasks().ListTasks()));
        Assert.Empty(Tasks().ListReviews());
        Assert.Equal(2L, Scalar(Router().RoutingDatabasePath, "SELECT COUNT(*) FROM route_receipts;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReceiptFailure_RollsBackExtractionTaskEvidenceAndOperationAndCanRetry(bool preExtracted)
    {
        var original = CreateOriginal();
        var change = Change("postpone", RuleAction.Postpone, original.Evidence[0], SentAt.AddMinutes(1));
        if (preExtracted) SaveExtracted(change);
        else Enqueue(change);
        var beforeMessage = Messages().Find(change.Key)!;
        var operationCount = Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM operations;");
        var evidenceCount = Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM task_evidence;");
        var router = Router();
        Execute(router.RoutingDatabasePath,
            "CREATE TRIGGER fail_receipt BEFORE INSERT ON route_receipts BEGIN SELECT RAISE(ABORT, 'injected linked routing failure'); END;");

        Assert.Throws<SqliteException>(() => router.ProcessAndRoutePending(Context(), Pinned()));

        AssertTask(original, Assert.Single(Tasks().ListTasks()));
        Assert.Equal(operationCount, Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM operations;"));
        Assert.Equal(evidenceCount, Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM task_evidence;"));
        Assert.Equal(1L, Scalar(router.RoutingDatabasePath, "SELECT COUNT(*) FROM route_receipts;"));
        Assert.Equal(beforeMessage.ProcessingVersion, Messages().Find(change.Key)!.ProcessingVersion);
        Assert.Equal(beforeMessage.Extraction is null, Messages().Find(change.Key)!.Extraction is null);
        Assert.Empty(Tasks().ListReviews());
        Execute(router.RoutingDatabasePath, "DROP TRIGGER fail_receipt;");

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        var updated = Assert.Single(Tasks().ListTasks());
        Assert.Equal(new DateOnly(2026, 10, 20), updated.Time.Date);
        Assert.Contains(change.Key, updated.Evidence);
        Assert.Equal(2L, Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM task_evidence;"));
        Assert.Equal(0, Router().ProcessAndRoutePending(Context(), Pinned()));
    }

    [Fact]
    public void OrdinaryPendingCandidate_IsNotUpgradedAfterCourseConfirmation()
    {
        var message = Original();
        SaveExtracted(message, Context() with { ConfirmedCourse = null });
        Assert.Equal(RuleDisposition.Pending, Candidate(message).Disposition);

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));

        Assert.Empty(Tasks().ListTasks());
        var review = Assert.Single(Tasks().ListReviews());
        Assert.Null(review.Candidate.Course);
        Assert.False(review.Candidate.Assessments.Course.Reliable);
    }

    private AccountMessageStore Messages() => new(dataRoot, Account);
    private AccountTaskStore Tasks() => new(dataRoot, Account);
    private AccountCandidateRouter Router() => new(Messages(), Tasks());
    private static HashSet<string> Pinned(string group = Group) => [group];
    private static RuleContext Context(string group = Group) =>
        new(Account, group, "Asia/Shanghai", Now, "数学", new HashSet<string> { Sender });
    private static SchoolMessage Original() => new(new(Account, Group, "original"), Sender, SentAt,
        [new(SegmentKind.Text, "10月13日18点提交统计作业")]);
    private static SchoolMessage Change(string id, RuleAction action, MessageKey target, DateTimeOffset sentAt,
        string group = Group) => new(new(Account, group, id), Sender, sentAt,
        [new(SegmentKind.Text, action switch
        {
            RuleAction.Cancel => "统计作业取消",
            RuleAction.Postpone => "统计作业延期到10月20日18点提交",
            RuleAction.NewRequirement => "统计作业新增要求：10月20日18点补交实验数据",
            RuleAction.Remind => "提醒提交统计作业",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        }), new(SegmentKind.Quote, "原通知", target)]);
    private static KnownTask Known(TaskItem task, string? publisher = null) =>
        new(task.Id, task.Title, task.Course!, task.Time, task.State, task.Evidence, publisher);
    private RuleCandidate Candidate(SchoolMessage message) => Assert.Single(Messages().Find(message.Key)!.Extraction!.Candidates);

    private void Enqueue(SchoolMessage message, RuleContext? context = null)
    {
        context ??= Context(message.Key.GroupId);
        Assert.True(new LocalMessageProcessor(Messages()).Ingest(message, Pinned(message.Key.GroupId), context,
            MessageOrigin.History, "linked-batch", context.Now));
    }

    private void SaveExtracted(SchoolMessage message, RuleContext? context = null)
    {
        context ??= Context(message.Key.GroupId);
        Enqueue(message, context);
        var stored = Messages().Find(message.Key)!;
        Assert.True(Messages().TrySaveExtraction(message.Key, stored.ProcessingVersion,
            MessageRuleExtractor.Extract(message, context), context.Now));
    }

    private TaskItem CreateOriginal()
    {
        Enqueue(Original());
        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));
        return Assert.Single(Tasks().ListTasks());
    }

    private TaskItem SetState(TaskItem task, TaskState state) => state switch
    {
        TaskState.Active => task,
        TaskState.Completed => Tasks().Complete(task.Id, "complete-original", task.Version, Now.AddMinutes(1)),
        TaskState.Archived => Tasks().Archive(task.Id, "archive-original", task.Version, Now.AddMinutes(1)),
        TaskState.Cancelled => Tasks().Cancel(task.Id, "cancel-original", task.Version, Now.AddMinutes(1)),
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static void AssertTask(TaskItem expected, TaskItem actual)
    {
        Assert.Equal(expected with { Evidence = actual.Evidence }, actual);
        Assert.Equal(expected.Evidence, actual.Evidence);
    }

    private static bool ContainsText(JsonElement element, string expected)
    {
        if (element.ValueKind == JsonValueKind.Object)
            return element.EnumerateObject().Any(property => ContainsText(property.Value, expected));
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(value => ContainsText(value, expected));
        if (element.ValueKind != JsonValueKind.String) return false;
        var value = element.GetString()!;
        if (value.Contains(expected, StringComparison.Ordinal)) return true;
        if (!value.StartsWith('{')) return false;
        using var nested = JsonDocument.Parse(value);
        return ContainsText(nested.RootElement, expected);
    }

    private static bool ContainsTaskState(JsonElement element, TaskState state)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(value => ContainsTaskState(value, state));
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (element.TryGetProperty("State", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == (int)state)
            return true;
        return element.EnumerateObject().Any(property => ContainsTaskState(property.Value, state));
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static object? Scalar(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
    }
}
