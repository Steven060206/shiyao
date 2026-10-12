using MessagePurify.Core;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MessagePurify.Infrastructure.Tests;

public sealed class TaskLifecycleTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string OtherAccount = "9007199254740994";
    private const string Group = "9007199254740995";
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "MessagePurify-lifecycle-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateTask_ReliableTaskUsesFormalDateOnlyTimeAndPreservesEvidence()
    {
        var candidate = TaskCandidate();

        var created = Store().CreateTask("task-1", candidate, "create-1", Now);

        Assert.Equal("task-1", created.Id);
        Assert.Equal(Account, created.AccountId);
        Assert.Equal("提交高等数学实验报告", created.Title);
        Assert.Equal("高等数学", created.Course);
        Assert.Equal(RuleAction.Submit, created.Action);
        Assert.Equal(TaskState.Active, created.State);
        Assert.Equal(FormalTime(), created.Time);
        Assert.Null(created.Time.Time);
        Assert.Null(created.Time.GetExactInstant());
        Assert.False(created.TimeProtected);
        Assert.Equal(2, created.Priority);
        Assert.Equal(candidate.Evidence, created.Evidence);
        Assert.Equal(Now, created.CreatedAt);
        Assert.Null(created.CompletedAt);
        Assert.Null(created.CancelledAt);
        Assert.Null(created.ArchivedAt);
        Assert.Null(created.UndoExpiresAt);
        Assert.Single(Store().ListTasks(TaskState.Active));
        Assert.Empty(Store().ListReviews());
        Assert.True(File.Exists(DatabasePath()));
        Assert.Equal(1L, Scalar("PRAGMA user_version;"));
    }

    [Fact]
    public void CreateTask_OrdinaryClearActionDoesNotRequireConfirmedPublisher()
    {
        var candidate = TaskCandidate();
        candidate = candidate with
        {
            Assessments = candidate.Assessments with
            {
                Source = new("普通清楚行动无需可信发布者身份", false)
            }
        };

        var task = Store().CreateTask("task-1", candidate, "create-1", Now);

        Assert.Equal(TaskState.Active, task.State);
        Assert.Equal(candidate.Time, task.Time);
        Assert.Equal(candidate.Evidence, task.Evidence);
    }

    [Fact]
    public void CreateTask_SuggestedTimeCannotReplaceExistingFormalTime()
    {
        var candidate = TaskCandidate() with { SuggestedTime = SuggestedTime() };

        var task = Store().CreateTask("task-1", candidate, "create-1", Now);

        Assert.Equal(FormalTime(), task.Time);
        Assert.False(task.TimeProtected);
    }

    [Theory]
    [InlineData(RuleDisposition.Pending)]
    [InlineData(RuleDisposition.Notice)]
    [InlineData(RuleDisposition.Ignored)]
    [InlineData(RuleDisposition.Change)]
    public void CreateTask_NonTaskDispositionDoesNotCreateFormalTask(RuleDisposition disposition)
    {
        var store = Store();
        var candidate = TaskCandidate() with { Disposition = disposition };

        Assert.Throws<ArgumentException>(() => store.CreateTask("task-1", candidate, "create-1", Now));

        Assert.Empty(store.ListTasks());
        Assert.Empty(store.ListReviews());
    }

    [Fact]
    public void CreateTask_OnlySuggestedTimeDoesNotCreateFormalTask()
    {
        var store = Store();
        var candidate = TaskCandidate() with { Time = null, SuggestedTime = SuggestedTime() };

        Assert.Throws<ArgumentException>(() => store.CreateTask("task-1", candidate, "create-1", Now));

        Assert.Empty(Store().ListTasks());
    }

    [Fact]
    public void SavePending_PreservesCandidateAndOcrSuggestionWithoutCreatingTask()
    {
        var candidate = PendingCandidate();

        var review = Store().SavePending("review-1", candidate, "pending-1", Now);

        Assert.Equal("review-1", review.Id);
        Assert.Equal(Account, review.AccountId);
        Assert.Equal(ReviewState.Pending, review.State);
        Assert.Equal(Now, review.CreatedAt);
        Assert.Null(review.IgnoredAt);
        Assert.Null(review.UndoExpiresAt);
        Assert.Null(review.AcceptedTaskId);
        AssertCandidate(candidate, review.Candidate);
        Assert.Null(review.Candidate.Time);
        Assert.Equal(SuggestedTime(), review.Candidate.SuggestedTime);
        Assert.Single(Store().ListReviews(ReviewState.Pending));
        Assert.Empty(Store().ListTasks());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writes_RejectForeignEvidenceWithoutSavingAnything(bool pending)
    {
        var store = Store();
        var candidate = (pending ? PendingCandidate() : TaskCandidate()) with
        {
            Evidence = [new(Account, Group, "message-1"), new(OtherAccount, Group, "foreign-message")]
        };

        if (pending)
            Assert.Throws<ArgumentException>(() => store.SavePending("review-1", candidate, "write-1", Now));
        else
            Assert.Throws<ArgumentException>(() => store.CreateTask("task-1", candidate, "write-1", Now));

        Assert.Empty(Store().ListTasks());
        Assert.Empty(Store().ListReviews());
    }

    [Fact]
    public void Accounts_IsolateSameTaskAndReviewIdsAndRejectAnotherAccountsCandidate()
    {
        var first = Store();
        var second = Store(OtherAccount);
        first.CreateTask("same-task", TaskCandidate(), "same-create", Now);
        first.SavePending("same-review", PendingCandidate(), "same-pending", Now);
        var otherTask = TaskCandidate(OtherAccount) with { Title = "另一个账号的实验报告" };
        var otherReview = PendingCandidate(OtherAccount) with { Title = "另一个账号的图片候选" };
        second.CreateTask("same-task", otherTask, "same-create", Now);
        second.SavePending("same-review", otherReview, "same-pending", Now);

        Assert.Throws<ArgumentException>(() => second.CreateTask("wrong-account", TaskCandidate(), "foreign-task", Now));
        Assert.Throws<ArgumentException>(() => second.SavePending("wrong-account", PendingCandidate(), "foreign-review", Now));

        Assert.Equal("提交高等数学实验报告", Assert.IsType<TaskItem>(Store().FindTask("same-task")).Title);
        Assert.Equal(otherTask.Title, Assert.IsType<TaskItem>(Store(OtherAccount).FindTask("same-task")).Title);
        Assert.Equal(OtherAccount, Assert.Single(Store(OtherAccount).ListTasks()).AccountId);
        AssertCandidate(otherReview, Assert.IsType<PendingReview>(Store(OtherAccount).FindReview("same-review")).Candidate);
        Assert.Single(Store().ListTasks());
        Assert.Single(Store().ListReviews());
        Assert.Null(second.FindTask("wrong-account"));
        Assert.Null(second.FindReview("wrong-account"));
    }

    [Fact]
    public void Reopening_PreservesTaskReviewAllEvidenceAndOriginalTimestampOffsets()
    {
        var createdAt = Now.ToOffset(TimeSpan.FromHours(-7)).AddTicks(1234);
        var task = Store().CreateTask("task-1", TaskCandidate(), "create-1", createdAt);
        var review = Store().SavePending("review-1", PendingCandidate(), "pending-1", createdAt.AddSeconds(2));

        var reopened = Store();
        var savedTask = Assert.IsType<TaskItem>(reopened.FindTask("task-1"));
        var savedReview = Assert.IsType<PendingReview>(reopened.FindReview("review-1"));

        AssertTask(task, savedTask);
        Assert.Equal(createdAt.Offset, savedTask.CreatedAt.Offset);
        AssertReview(review, savedReview);
        Assert.Equal(createdAt.Offset, savedReview.CreatedAt.Offset);
        Assert.Null(reopened.FindTask("missing-task"));
        Assert.Null(reopened.FindReview("missing-review"));
    }

    [Theory]
    [InlineData(-1, TaskState.Active)]
    [InlineData(0, TaskState.Completed)]
    [InlineData(1, TaskState.Completed)]
    public void UndoComplete_ExpiresAtExactOneHourBoundary(long ticksFromExpiry, TaskState expectedState)
    {
        var task = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var completed = Store().Complete(task.Id, "complete-1", task.Version, Now.AddMinutes(5));
        Assert.Equal(Now.AddMinutes(5).AddHours(1), completed.UndoExpiresAt);
        var undoAt = Now.AddMinutes(5).AddHours(1).AddTicks(ticksFromExpiry);

        var current = Store().UndoComplete(task.Id, "undo-1", completed.Version, undoAt);

        Assert.Equal(expectedState, current.State);
        Assert.Equal(task.Time, current.Time);
        Assert.Equal(task.Evidence, current.Evidence);
        Assert.Equal(expectedState == TaskState.Active ? completed.Version + 1 : completed.Version, current.Version);
        AssertTask(current, Assert.IsType<TaskItem>(Store().FindTask(task.Id)));
        if (expectedState == TaskState.Active)
        {
            Assert.Null(current.CompletedAt);
            Assert.Null(current.UndoExpiresAt);
        }
        else Assert.Equal(Now.AddMinutes(5), current.CompletedAt);
    }

    [Fact]
    public void Cancel_AfterCompletionInvalidatesUndoAndKeepsEvidenceAndProtectedTime()
    {
        var store = Store();
        var task = store.CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var protectedTask = store.AcceptTime(task.Id, "protect-time", task.Version, FormalTime(), Now.AddMinutes(1));
        var completed = store.Complete(task.Id, "complete-1", protectedTask.Version, Now.AddMinutes(2));
        var cancelled = store.Cancel(task.Id, "cancel-1", completed.Version, Now.AddMinutes(3));

        Assert.Throws<InvalidOperationException>(() => store.UndoComplete(task.Id, "old-undo", completed.Version, Now.AddMinutes(4)));
        var current = store.UndoComplete(task.Id, "current-undo", cancelled.Version, Now.AddMinutes(4));

        AssertTask(cancelled, current);
        Assert.Equal(TaskState.Cancelled, current.State);
        Assert.Equal(Now.AddMinutes(3), current.CancelledAt);
        Assert.Null(current.UndoExpiresAt);
        Assert.True(current.TimeProtected);
        Assert.Equal(task.Time, current.Time);
        Assert.Equal(task.Evidence, current.Evidence);
        Assert.Empty(Store().ListTasks(TaskState.Active));
        Assert.Single(Store().ListTasks(TaskState.Cancelled));
    }

    [Fact]
    public void NewCompletion_OldUndoReplayCannotUndoLatestCompletion()
    {
        var store = Store();
        var task = store.CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var firstComplete = store.Complete(task.Id, "complete-1", task.Version, Now.AddMinutes(1));
        var firstUndo = store.UndoComplete(task.Id, "undo-1", firstComplete.Version, Now.AddMinutes(2));
        var secondComplete = store.Complete(task.Id, "complete-2", firstUndo.Version, Now.AddMinutes(3));

        var replay = Store().UndoComplete(task.Id, "undo-1", firstComplete.Version, Now.AddMinutes(4));

        AssertTask(secondComplete, replay);
        Assert.Equal(Now.AddMinutes(3), replay.CompletedAt);
        Assert.Throws<InvalidOperationException>(() => Store().UndoComplete(task.Id, "stale-undo", firstComplete.Version, Now.AddMinutes(4)));
        var secondUndo = Store().UndoComplete(task.Id, "undo-2", secondComplete.Version, Now.AddMinutes(5));
        Assert.Equal(TaskState.Active, secondUndo.State);
    }

    [Theory]
    [InlineData(-1, ReviewState.Pending)]
    [InlineData(0, ReviewState.Ignored)]
    [InlineData(1, ReviewState.Ignored)]
    public void UndoIgnored_ExpiresAtExactTwentyFourHourBoundary(long ticksFromExpiry, ReviewState expectedState)
    {
        var review = Store().SavePending("review-1", PendingCandidate(), "pending-1", Now);
        var ignored = Store().IgnoreReview(review.Id, "ignore-1", review.Version, Now.AddMinutes(5));

        Assert.Equal(ReviewState.Ignored, ignored.State);
        Assert.Equal(Now.AddMinutes(5), ignored.IgnoredAt);
        Assert.Equal(Now.AddMinutes(5).AddHours(24), ignored.UndoExpiresAt);
        var current = Store().UndoIgnored(review.Id, "undo-ignore-1", ignored.Version,
            Now.AddMinutes(5).AddHours(24).AddTicks(ticksFromExpiry));

        Assert.Equal(expectedState, current.State);
        AssertCandidate(review.Candidate, current.Candidate);
        Assert.Equal(expectedState == ReviewState.Pending ? ignored.Version + 1 : ignored.Version, current.Version);
        AssertReview(current, Assert.IsType<PendingReview>(Store().FindReview(review.Id)));
        if (expectedState == ReviewState.Pending)
        {
            Assert.Null(current.IgnoredAt);
            Assert.Null(current.UndoExpiresAt);
        }
        else Assert.Equal(ignored.UndoExpiresAt, current.UndoExpiresAt);
        Assert.Single(Store().ListReviews(expectedState));
        Assert.Empty(Store().ListTasks());
    }

    [Fact]
    public void IgnoreAgain_OldUndoReplayDoesNotUndoLatestIgnore()
    {
        var store = Store();
        var review = store.SavePending("review-1", PendingCandidate(), "pending-1", Now);
        var firstIgnore = store.IgnoreReview(review.Id, "ignore-1", review.Version, Now.AddMinutes(1));
        var firstUndo = store.UndoIgnored(review.Id, "undo-ignore-1", firstIgnore.Version, Now.AddMinutes(2));
        var secondIgnore = store.IgnoreReview(review.Id, "ignore-2", firstUndo.Version, Now.AddMinutes(3));

        var replay = Store().UndoIgnored(review.Id, "undo-ignore-1", firstIgnore.Version, Now.AddMinutes(4));

        AssertReview(secondIgnore, replay);
        Assert.Throws<InvalidOperationException>(() => Store().UndoIgnored(review.Id, "stale-undo", firstIgnore.Version, Now.AddMinutes(4)));
    }

    [Fact]
    public void ArchiveAndRestore_UseLatestFormalFieldsAfterReopening()
    {
        var task = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var archived = Store().Archive(task.Id, "archive-1", task.Version, Now.AddMinutes(1));
        Assert.Equal(TaskState.Archived, archived.State);
        Assert.Equal(Now.AddMinutes(1), archived.ArchivedAt);
        Assert.Empty(Store().ListTasks(TaskState.Active));
        Assert.Single(Store().ListTasks(TaskState.Archived));
        var latestTime = new TaskTime(TimeKind.Deadline, new(2026, 10, 20), new(17, 45), "Asia/Shanghai");
        var corrected = Store().AcceptTime(task.Id, "accept-time-1", archived.Version, latestTime, Now.AddMinutes(2));

        var restored = Store().RestoreArchived(task.Id, "restore-1", corrected.Version, Now.AddMinutes(3));

        Assert.Equal(TaskState.Active, restored.State);
        Assert.Null(restored.ArchivedAt);
        Assert.Equal(latestTime, restored.Time);
        Assert.True(restored.TimeProtected);
        Assert.Equal(corrected.TimeVersion, restored.TimeVersion);
        Assert.Equal(corrected.Version + 1, restored.Version);
        Assert.Equal(task.Title, restored.Title);
        Assert.Equal(task.Evidence, restored.Evidence);
        Assert.Single(Store().ListTasks(TaskState.Active));
        Assert.Empty(Store().ListTasks(TaskState.Archived));
    }

    [Fact]
    public void AcceptTime_ProtectsSameTimeWithoutInventingClockOrIncreasingTimeVersion()
    {
        var task = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);

        var confirmed = Store().AcceptTime(task.Id, "accept-time-1", task.Version, FormalTime(), Now.AddMinutes(1));

        Assert.True(confirmed.TimeProtected);
        Assert.Null(confirmed.Time.Time);
        Assert.Equal(task.TimeVersion, confirmed.TimeVersion);
        Assert.Equal(task.Version + 1, confirmed.Version);
        AssertTask(confirmed, Assert.IsType<TaskItem>(Store().FindTask(task.Id)));
        var again = Store().AcceptTime(task.Id, "accept-time-2", confirmed.Version, FormalTime(), Now.AddMinutes(2));
        AssertTask(confirmed, again);
    }

    [Fact]
    public void AcceptTime_ChangesTimeAndProtectionAndUndoKeepsLatestConfirmedTime()
    {
        var task = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var completed = Store().Complete(task.Id, "complete-1", task.Version, Now.AddMinutes(1));
        var later = new TaskTime(TimeKind.Deadline, new(2026, 10, 20), new(18, 0), "Asia/Shanghai");
        var corrected = Store().AcceptTime(task.Id, "accept-time-1", completed.Version, later, Now.AddMinutes(2));

        var restored = Store().UndoComplete(task.Id, "undo-1", corrected.Version, Now.AddMinutes(3));

        Assert.Equal(TaskState.Active, restored.State);
        Assert.Equal(later, restored.Time);
        Assert.True(restored.TimeProtected);
        Assert.Equal(completed.UndoExpiresAt, corrected.UndoExpiresAt);
        Assert.Null(restored.UndoExpiresAt);
        Assert.Equal(task.TimeVersion + 1, restored.TimeVersion);
        Assert.Equal(corrected.Version + 1, restored.Version);
        Assert.Equal(task.Evidence, restored.Evidence);
    }

    [Fact]
    public void StaleTaskVersion_CannotReplaceLatestStateOrTimeFromAnotherStore()
    {
        var first = Store();
        var second = Store();
        var task = first.CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var stale = Assert.IsType<TaskItem>(second.FindTask(task.Id));
        var latestTime = new TaskTime(TimeKind.Deadline, new(2026, 10, 20), null, "Asia/Shanghai");
        var latest = first.AcceptTime(task.Id, "accept-time-1", task.Version, latestTime, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => second.Complete(task.Id, "stale-complete", stale.Version, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => second.Cancel(task.Id, "stale-cancel", stale.Version, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => second.Archive(task.Id, "stale-archive", stale.Version, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => second.AcceptTime(task.Id, "stale-time", stale.Version, FormalTime(), Now.AddMinutes(2)));

        AssertTask(latest, Assert.IsType<TaskItem>(Store().FindTask(task.Id)));
        var completed = second.Complete(task.Id, "stale-complete", latest.Version, Now.AddMinutes(3));
        Assert.Equal(TaskState.Completed, completed.State);
        Assert.Equal(latestTime, completed.Time);
    }

    [Fact]
    public void StaleReviewVersion_CannotIgnoreOrAcceptChangedReview()
    {
        var store = Store();
        var review = store.SavePending("review-1", PendingCandidate(), "pending-1", Now);
        var ignored = store.IgnoreReview(review.Id, "ignore-1", review.Version, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => Store().IgnoreReview(review.Id, "stale-ignore", review.Version, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => Store().AcceptReview(review.Id, "task-1", "stale-accept", review.Version, SuggestedTime(), Now.AddMinutes(2)));

        AssertReview(ignored, Assert.IsType<PendingReview>(Store().FindReview(review.Id)));
        Assert.Empty(Store().ListTasks());
    }

    [Fact]
    public void OperationReplay_ReturnsCurrentTaskAndDoesNotOverwriteProtectedTimeOrEvidence()
    {
        var store = Store();
        var candidate = TaskCandidate();
        var created = store.CreateTask("task-1", candidate, "create-1", Now);
        var completed = store.Complete(created.Id, "complete-1", created.Version, Now.AddMinutes(1));
        var corrected = store.AcceptTime(created.Id, "accept-time-1", completed.Version,
            new(TimeKind.Deadline, new(2026, 10, 20), null, "Asia/Shanghai"), Now.AddMinutes(2));
        var cancelled = store.Cancel(created.Id, "cancel-1", corrected.Version, Now.AddMinutes(3));

        AssertTask(cancelled, Store().CreateTask(created.Id, candidate, "create-1", Now.AddMinutes(4)));
        AssertTask(cancelled, Store().Complete(created.Id, "complete-1", created.Version, Now.AddMinutes(4)));
        AssertTask(cancelled, Store().AcceptTime(created.Id, "accept-time-1", completed.Version, corrected.Time, Now.AddMinutes(4)));
        AssertTask(cancelled, Store().Cancel(created.Id, "cancel-1", corrected.Version, Now.AddMinutes(4)));
        Assert.Single(Store().ListTasks());
        Assert.True(cancelled.TimeProtected);
        Assert.Equal(candidate.Evidence, cancelled.Evidence);
    }

    [Fact]
    public void PendingOperationReplay_ReturnsCurrentReviewWithoutReopeningIgnoredCandidate()
    {
        var store = Store();
        var candidate = PendingCandidate();
        var review = store.SavePending("review-1", candidate, "pending-1", Now);
        var ignored = store.IgnoreReview(review.Id, "ignore-1", review.Version, Now.AddMinutes(1));

        AssertReview(ignored, Store().SavePending(review.Id, candidate, "pending-1", Now.AddMinutes(2)));
        AssertReview(ignored, Store().IgnoreReview(review.Id, "ignore-1", review.Version, Now.AddMinutes(2)));
        Assert.Single(Store().ListReviews());
        Assert.Empty(Store().ListReviews(ReviewState.Pending));
    }

    [Fact]
    public void SameTaskId_NewCreateOperationDoesNotReplaceExistingTask()
    {
        var store = Store();
        var candidate = TaskCandidate();
        var task = store.CreateTask("task-1", candidate, "create-1", Now);

        Assert.Throws<InvalidOperationException>(() => store.CreateTask(task.Id,
            candidate with { Title = "不应覆盖的标题" }, "create-2", Now.AddMinutes(1)));

        AssertTask(task, Assert.IsType<TaskItem>(Store().FindTask(task.Id)));
        Assert.Single(Store().ListTasks());
    }

    [Fact]
    public void AcceptReview_ConfirmedSuggestionCreatesProtectedTaskAndMarksReviewAccepted()
    {
        var candidate = PendingCandidate();
        var review = Store().SavePending("review-1", candidate, "pending-1", Now);
        var confirmed = new TaskTime(TimeKind.Deadline, new(2026, 10, 19), new(16, 30), "Asia/Shanghai");

        var task = Store().AcceptReview(review.Id, "task-1", "accept-1", review.Version, confirmed, Now.AddMinutes(1));

        Assert.Equal("task-1", task.Id);
        Assert.Equal(Account, task.AccountId);
        Assert.Equal(candidate.Title, task.Title);
        Assert.Equal(candidate.Course, task.Course);
        Assert.Equal(candidate.Action, task.Action);
        Assert.Equal(TaskState.Active, task.State);
        Assert.Equal(confirmed, task.Time);
        Assert.NotEqual(candidate.SuggestedTime, task.Time);
        Assert.True(task.TimeProtected);
        Assert.Equal(candidate.Evidence, task.Evidence);
        Assert.Equal(Now.AddMinutes(1), task.CreatedAt);
        var accepted = Assert.IsType<PendingReview>(Store().FindReview(review.Id));
        Assert.Equal(ReviewState.Accepted, accepted.State);
        Assert.Equal(task.Id, accepted.AcceptedTaskId);
        Assert.Equal(review.Version + 1, accepted.Version);
        AssertCandidate(candidate, accepted.Candidate);
        Assert.Empty(Store().ListReviews(ReviewState.Pending));
        Assert.Single(Store().ListReviews(ReviewState.Accepted));
        Assert.Single(Store().ListTasks(TaskState.Active));
    }

    [Fact]
    public void AcceptReview_WithoutConfirmedTimeRejectsSuggestionAndLeavesCandidatePending()
    {
        var store = Store();
        var review = store.SavePending("review-1", PendingCandidate(), "pending-1", Now);

        Assert.Throws<ArgumentException>(() => store.AcceptReview(review.Id, "task-1", "accept-1", review.Version, null, Now.AddMinutes(1)));

        AssertReview(review, Assert.IsType<PendingReview>(Store().FindReview(review.Id)));
        Assert.Empty(Store().ListTasks());
        var task = Store().AcceptReview(review.Id, "task-1", "accept-1", review.Version, SuggestedTime(), Now.AddMinutes(2));
        Assert.True(task.TimeProtected);
        Assert.Equal(SuggestedTime(), task.Time);
    }

    [Fact]
    public void AcceptReview_ConflictingTaskIdDoesNotConsumeReviewOrOverwriteTask()
    {
        var store = Store();
        var original = store.CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var review = store.SavePending("review-1", PendingCandidate(), "pending-1", Now);

        Assert.Throws<InvalidOperationException>(() => store.AcceptReview(review.Id, original.Id, "accept-1", review.Version, SuggestedTime(), Now.AddMinutes(1)));

        AssertTask(original, Assert.IsType<TaskItem>(Store().FindTask(original.Id)));
        AssertReview(review, Assert.IsType<PendingReview>(Store().FindReview(review.Id)));
        Assert.Single(Store().ListTasks());
        var accepted = store.AcceptReview(review.Id, "task-2", "accept-1", review.Version, SuggestedTime(), Now.AddMinutes(2));
        Assert.Equal("task-2", accepted.Id);
    }

    [Fact]
    public void AcceptReview_DatabaseFailureRollsBackTaskReviewEvidenceAndOperationKey()
    {
        var store = Store();
        var review = store.SavePending("review-1", PendingCandidate(), "pending-1", Now);
        var triggers = InstallUpdateFailureTriggers();

        Assert.Throws<SqliteException>(() => store.AcceptReview(review.Id, "task-1", "accept-1", review.Version, SuggestedTime(), Now.AddMinutes(1)));

        AssertReview(review, Assert.IsType<PendingReview>(Store().FindReview(review.Id)));
        Assert.Null(Store().FindTask("task-1"));
        Assert.Empty(Store().ListTasks());
        foreach (var trigger in triggers) Execute($"DROP TRIGGER \"{trigger}\";");

        var task = Store().AcceptReview(review.Id, "task-1", "accept-1", review.Version, SuggestedTime(), Now.AddMinutes(2));
        Assert.Equal(review.Candidate.Evidence, task.Evidence);
        Assert.Equal(ReviewState.Accepted, Store().FindReview(review.Id)!.State);
        Assert.Single(Store().ListTasks());
    }

    [Fact]
    public void AcceptReview_OperationReplayReturnsCurrentTaskAndCannotCreateSecondTask()
    {
        var review = Store().SavePending("review-1", PendingCandidate(), "pending-1", Now);
        var task = Store().AcceptReview(review.Id, "task-1", "accept-1", review.Version, SuggestedTime(), Now.AddMinutes(1));
        var completed = Store().Complete(task.Id, "complete-1", task.Version, Now.AddMinutes(2));

        var replay = Store().AcceptReview(review.Id, "task-1", "accept-1", review.Version, SuggestedTime(), Now.AddMinutes(3));

        AssertTask(completed, replay);
        Assert.Equal(ReviewState.Accepted, Store().FindReview(review.Id)!.State);
        Assert.Single(Store().ListTasks());
        Assert.Throws<InvalidOperationException>(() => Store().AcceptReview(review.Id, "task-2", "accept-2",
            Store().FindReview(review.Id)!.Version, SuggestedTime(), Now.AddMinutes(3)));
        Assert.Null(Store().FindTask("task-2"));
    }

    [Fact]
    public void CreateTask_SameCandidateUnderNewIdReturnsOriginalTaskAndKeepsProtectedEdits()
    {
        var candidate = TaskCandidate();
        var original = Store().CreateTask("task-1", candidate, "create-1", Now);
        var confirmed = Store().AcceptTime(original.Id, "accept-time-1", original.Version,
            new(TimeKind.Deadline, new(2026, 10, 20), null, "Asia/Shanghai"), Now.AddMinutes(1));
        var duplicate = candidate with
        {
            Evidence = [candidate.Evidence[1], candidate.Evidence[0], candidate.Evidence[1]]
        };

        var replay = Store().CreateTask("task-2", duplicate, "create-2", Now.AddMinutes(2));

        AssertTask(confirmed, replay);
        Assert.Null(Store().FindTask("task-2"));
        Assert.Single(Store().ListTasks());
        Assert.Equal(2, replay.Evidence.Count);
        AssertTask(confirmed, Store().CreateTask("task-2", duplicate, "create-2", Now.AddMinutes(3)));
    }

    [Fact]
    public void SavePending_SameCandidateUnderNewIdReturnsOriginalIgnoredReview()
    {
        var candidate = PendingCandidate();
        var original = Store().SavePending("review-1", candidate, "pending-1", Now);
        var ignored = Store().IgnoreReview(original.Id, "ignore-1", original.Version, Now.AddMinutes(1));
        var duplicate = candidate with
        {
            Evidence = [candidate.Evidence[1], candidate.Evidence[0], candidate.Evidence[0]]
        };

        var replay = Store().SavePending("review-2", duplicate, "pending-2", Now.AddMinutes(2));

        AssertReview(ignored, replay);
        Assert.Null(Store().FindReview("review-2"));
        Assert.Single(Store().ListReviews());
        Assert.Equal(2, replay.Candidate.Evidence.Count);
        AssertReview(ignored, Store().SavePending("review-2", duplicate, "pending-2", Now.AddMinutes(3)));
    }

    [Fact]
    public void CreateOperationId_ConflictingTargetOrCandidateIsRejected()
    {
        var candidate = TaskCandidate();
        var task = Store().CreateTask("task-1", candidate, "shared-operation", Now);

        Assert.Throws<InvalidOperationException>(() => Store().CreateTask("task-2", candidate, "shared-operation", Now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => Store().CreateTask(task.Id,
            candidate with { Title = "修改了相同操作的参数" }, "shared-operation", Now.AddMinutes(1)));

        AssertTask(task, Assert.IsType<TaskItem>(Store().FindTask(task.Id)));
        Assert.Null(Store().FindTask("task-2"));
        Assert.Single(Store().ListTasks());
    }

    [Fact]
    public void StateOperationId_ConflictingTargetOrOperationKindIsRejected()
    {
        var first = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var second = Store().CreateTask("task-2", TaskCandidate() with
            { Title = "第二项作业", Evidence = [new(Account, Group, "second-task-message")] }, "create-2", Now);
        var completed = Store().Complete(first.Id, "shared-operation", first.Version, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => Store().Complete(second.Id, "shared-operation", second.Version, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => Store().Cancel(first.Id, "shared-operation", completed.Version, Now.AddMinutes(2)));

        AssertTask(completed, Assert.IsType<TaskItem>(Store().FindTask(first.Id)));
        AssertTask(second, Assert.IsType<TaskItem>(Store().FindTask(second.Id)));
    }

    [Fact]
    public void AcceptTimeOperationId_ChangedTimeParameterCannotOverwriteEarlierConfirmation()
    {
        var task = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var firstTime = new TaskTime(TimeKind.Deadline, new(2026, 10, 20), null, "Asia/Shanghai");
        var current = Store().AcceptTime(task.Id, "accept-time-1", task.Version, firstTime, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => Store().AcceptTime(task.Id, "accept-time-1", task.Version,
            firstTime with { Date = new(2026, 10, 21) }, Now.AddMinutes(2)));

        AssertTask(current, Assert.IsType<TaskItem>(Store().FindTask(task.Id)));
    }

    [Fact]
    public void AcceptReviewOperationId_ChangedTaskTargetOrTimeIsRejected()
    {
        var review = Store().SavePending("review-1", PendingCandidate(), "pending-1", Now);
        var confirmedTime = SuggestedTime();
        var task = Store().AcceptReview(review.Id, "task-1", "accept-1", review.Version, confirmedTime, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => Store().AcceptReview(review.Id, "task-2", "accept-1",
            review.Version, confirmedTime, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => Store().AcceptReview(review.Id, task.Id, "accept-1",
            review.Version, confirmedTime with { Date = new(2026, 10, 20) }, Now.AddMinutes(2)));

        AssertTask(task, Assert.IsType<TaskItem>(Store().FindTask(task.Id)));
        Assert.Null(Store().FindTask("task-2"));
        Assert.Equal(task.Id, Store().FindReview(review.Id)!.AcceptedTaskId);
    }

    [Fact]
    public void LinkedCancellation_WithoutTimeKeepsProtectedFieldsAndAppendsEvidenceOnce()
    {
        var original = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var protectedTask = Store().AcceptTime(original.Id, "protect-time", original.Version, original.Time, Now.AddMinutes(1));
        var candidate = LinkedCandidate(RuleAction.Cancel) with { Time = null, SuggestedTime = null };
        var review = Store().SavePending("review-1", candidate, "pending-1", Now.AddMinutes(2));

        var cancelled = Store().AcceptReview(review.Id, protectedTask.Id, "accept-1", review.Version,
            null, Now.AddMinutes(3), expectedTaskVersion: protectedTask.Version);

        Assert.Equal(TaskState.Cancelled, cancelled.State);
        Assert.Equal(protectedTask.Time, cancelled.Time);
        Assert.True(cancelled.TimeProtected);
        Assert.Equal(protectedTask.TimeVersion, cancelled.TimeVersion);
        Assert.Equal(protectedTask.Title, cancelled.Title);
        Assert.Equal(protectedTask.Action, cancelled.Action);
        Assert.Equal(Now.AddMinutes(3), cancelled.CancelledAt);
        Assert.Null(cancelled.UndoExpiresAt);
        Assert.Equal(3, cancelled.Evidence.Count);
        Assert.All(original.Evidence, key => Assert.Contains(key, cancelled.Evidence));
        Assert.Contains(new MessageKey(Account, Group, "linked-change-message"), cancelled.Evidence);
        Assert.Equal(cancelled.Evidence.Count, cancelled.Evidence.Distinct().Count());
        var accepted = Store().FindReview(review.Id)!;
        Assert.Equal(ReviewState.Accepted, accepted.State);
        Assert.Equal(original.Id, accepted.AcceptedTaskId);
        Assert.Single(Store().ListTasks());
        AssertTask(cancelled, Store().AcceptReview(review.Id, protectedTask.Id, "accept-1", review.Version,
            null, Now.AddMinutes(4), expectedTaskVersion: protectedTask.Version));
    }

    [Fact]
    public void LinkedReview_TargetMismatchDoesNotChangeEitherTaskOrReview()
    {
        var original = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var other = Store().CreateTask("task-2", TaskCandidate() with
            { Title = "另一任务", Evidence = [new(Account, Group, "other-task-message")] }, "create-2", Now);
        var review = Store().SavePending("review-1", LinkedCandidate(RuleAction.Cancel) with { Time = null }, "pending-1", Now);

        Assert.Throws<ArgumentException>(() => Store().AcceptReview(review.Id, other.Id, "accept-1",
            review.Version, null, Now.AddMinutes(1), expectedTaskVersion: other.Version));

        AssertTask(original, Store().FindTask(original.Id)!);
        AssertTask(other, Store().FindTask(other.Id)!);
        AssertReview(review, Store().FindReview(review.Id)!);
    }

    [Theory]
    [InlineData(RuleAction.Cancel)]
    [InlineData(RuleAction.Postpone)]
    public void LinkedReview_StaleTaskVersionDoesNotConsumeReviewOrReplaceCurrentFields(RuleAction action)
    {
        var original = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var candidate = LinkedCandidate(action) with
        {
            Time = action == RuleAction.Cancel ? null : FormalTime() with { Date = new(2026, 10, 22) }
        };
        var review = Store().SavePending("review-1", candidate, "pending-1", Now.AddMinutes(1));
        var latest = Store().AcceptTime(original.Id, "accept-time-1", original.Version,
            FormalTime() with { Date = new(2026, 10, 20) }, Now.AddMinutes(2));
        var confirmedTime = action == RuleAction.Cancel ? null : candidate.Time;

        Assert.Throws<InvalidOperationException>(() => Store().AcceptReview(review.Id, original.Id, "accept-1",
            review.Version, confirmedTime, Now.AddMinutes(3), expectedTaskVersion: original.Version));

        AssertTask(latest, Store().FindTask(original.Id)!);
        AssertReview(review, Store().FindReview(review.Id)!);
        var accepted = Store().AcceptReview(review.Id, original.Id, "accept-1", review.Version,
            confirmedTime, Now.AddMinutes(4), expectedTaskVersion: latest.Version);
        Assert.Equal(action == RuleAction.Cancel ? TaskState.Cancelled : TaskState.Active, accepted.State);
        Assert.Equal(ReviewState.Accepted, Store().FindReview(review.Id)!.State);
    }

    [Fact]
    public void LinkedReview_MissingExpectedTaskVersionIsRejected()
    {
        var original = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var review = Store().SavePending("review-1", LinkedCandidate(RuleAction.Cancel) with { Time = null }, "pending-1", Now);

        Assert.Throws<ArgumentException>(() => Store().AcceptReview(review.Id, original.Id, "accept-1",
            review.Version, null, Now.AddMinutes(1)));

        AssertTask(original, Store().FindTask(original.Id)!);
        AssertReview(review, Store().FindReview(review.Id)!);
    }

    [Fact]
    public void LinkedPostponement_UnprotectedTimeAcceptsFormalCandidateTimeAndPreservesEvidence()
    {
        var original = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var later = FormalTime() with { Date = new(2026, 10, 22) };
        var review = Store().SavePending("review-1", LinkedCandidate(RuleAction.Postpone) with { Time = later }, "pending-1", Now);

        var changed = Store().AcceptReview(review.Id, original.Id, "accept-1", review.Version, null,
            Now.AddMinutes(1), expectedTaskVersion: original.Version);

        Assert.Equal(later, changed.Time);
        Assert.False(changed.TimeProtected);
        Assert.Equal(original.TimeVersion + 1, changed.TimeVersion);
        Assert.Equal(TaskState.Active, changed.State);
        Assert.All(original.Evidence, key => Assert.Contains(key, changed.Evidence));
        Assert.Contains(new MessageKey(Account, Group, "linked-change-message"), changed.Evidence);
        Assert.Single(Store().ListTasks());
    }

    [Fact]
    public void LinkedPostponement_ProtectedTimeRequiresExplicitConfirmation()
    {
        var original = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var protectedTask = Store().AcceptTime(original.Id, "protect-time", original.Version, original.Time, Now.AddMinutes(1));
        var proposedTime = FormalTime() with { Date = new(2026, 10, 22) };
        var review = Store().SavePending("review-1", LinkedCandidate(RuleAction.Postpone) with { Time = proposedTime }, "pending-1", Now.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() => Store().AcceptReview(review.Id, original.Id, "accept-1",
            review.Version, null, Now.AddMinutes(3), expectedTaskVersion: protectedTask.Version));

        AssertTask(protectedTask, Store().FindTask(original.Id)!);
        AssertReview(review, Store().FindReview(review.Id)!);
        var chosenTime = proposedTime with { Date = new(2026, 10, 23), Time = new(19, 30) };
        var changed = Store().AcceptReview(review.Id, original.Id, "accept-1", review.Version, chosenTime,
            Now.AddMinutes(4), expectedTaskVersion: protectedTask.Version);
        Assert.Equal(chosenTime, changed.Time);
        Assert.True(changed.TimeProtected);
        Assert.Equal(protectedTask.TimeVersion + 1, changed.TimeVersion);
        Assert.All(original.Evidence, key => Assert.Contains(key, changed.Evidence));
        Assert.Equal(ReviewState.Accepted, Store().FindReview(review.Id)!.State);
    }

    [Theory]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Archived)]
    [InlineData(TaskState.Cancelled)]
    public void LinkedNewRequirement_RestoresCompletedOrArchivedAndKeepsCancellation(TaskState initialState)
    {
        var original = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var changed = initialState switch
        {
            TaskState.Completed => Store().Complete(original.Id, "state-1", original.Version, Now.AddMinutes(1)),
            TaskState.Archived => Store().Archive(original.Id, "state-1", original.Version, Now.AddMinutes(1)),
            _ => Store().Cancel(original.Id, "state-1", original.Version, Now.AddMinutes(1))
        };
        var review = Store().SavePending("review-1", LinkedCandidate(RuleAction.NewRequirement), "pending-1", Now.AddMinutes(2));

        var accepted = Store().AcceptReview(review.Id, original.Id, "accept-1", review.Version, null,
            Now.AddMinutes(3), expectedTaskVersion: changed.Version);

        Assert.Equal(initialState == TaskState.Cancelled ? TaskState.Cancelled : TaskState.Active, accepted.State);
        Assert.Equal(original.Time, accepted.Time);
        Assert.Null(accepted.UndoExpiresAt);
        Assert.All(original.Evidence, key => Assert.Contains(key, accepted.Evidence));
        Assert.Contains(new MessageKey(Account, Group, "linked-change-message"), accepted.Evidence);
        Assert.Equal(ReviewState.Accepted, Store().FindReview(review.Id)!.State);
        Assert.Equal(original.Id, Store().FindReview(review.Id)!.AcceptedTaskId);
        Assert.Single(Store().ListTasks());
        if (initialState == TaskState.Completed) Assert.Null(accepted.CompletedAt);
        if (initialState == TaskState.Archived) Assert.Null(accepted.ArchivedAt);
        if (initialState == TaskState.Cancelled) Assert.Equal(changed.CancelledAt, accepted.CancelledAt);
    }

    [Fact]
    public void RejectReview_EndsCandidateWithoutChangingLinkedTask()
    {
        var task = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var review = Store().SavePending("review-1", LinkedCandidate(RuleAction.Cancel) with { Time = null }, "pending-1", Now);

        var rejected = Store().RejectReview(review.Id, "reject-1", review.Version, Now.AddMinutes(1));

        Assert.Equal(ReviewState.Rejected, rejected.State);
        Assert.Equal(review.Version + 1, rejected.Version);
        Assert.Null(rejected.AcceptedTaskId);
        AssertCandidate(review.Candidate, rejected.Candidate);
        AssertTask(task, Store().FindTask(task.Id)!);
        AssertReview(rejected, Store().FindReview(review.Id)!);
        Assert.Single(Store().ListReviews(ReviewState.Rejected));
        Assert.Empty(Store().ListReviews(ReviewState.Pending));
        AssertReview(rejected, Store().RejectReview(review.Id, "reject-1", review.Version, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => Store().AcceptReview(review.Id, task.Id, "accept-1",
            rejected.Version, null, Now.AddMinutes(2), expectedTaskVersion: task.Version));
        AssertTask(task, Store().FindTask(task.Id)!);
    }

    [Fact]
    public void IgnoredReview_CannotBeAcceptedUntilIgnoreIsUndone()
    {
        var review = Store().SavePending("review-1", PendingCandidate(), "pending-1", Now);
        var ignored = Store().IgnoreReview(review.Id, "ignore-1", review.Version, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => Store().AcceptReview(review.Id, "task-1", "accept-1",
            ignored.Version, SuggestedTime(), Now.AddMinutes(2)));

        AssertReview(ignored, Store().FindReview(review.Id)!);
        Assert.Empty(Store().ListTasks());
        var pending = Store().UndoIgnored(review.Id, "undo-ignore-1", ignored.Version, Now.AddMinutes(3));
        var task = Store().AcceptReview(review.Id, "task-1", "accept-1", pending.Version, SuggestedTime(), Now.AddMinutes(4));
        Assert.Equal(TaskState.Active, task.State);
        Assert.Equal(task.Id, Store().FindReview(review.Id)!.AcceptedTaskId);
    }

    [Fact]
    public void RejectReview_StaleVersionCannotReplaceCurrentIgnoredReview()
    {
        var review = Store().SavePending("review-1", PendingCandidate(), "pending-1", Now);
        var ignored = Store().IgnoreReview(review.Id, "ignore-1", review.Version, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => Store().RejectReview(review.Id, "reject-1", review.Version, Now.AddMinutes(2)));

        AssertReview(ignored, Store().FindReview(review.Id)!);
        Assert.Empty(Store().ListTasks());
    }

    [Fact]
    public void UndoComplete_CurrentVersionWithOldTicketCannotUndoNewCompletion()
    {
        var task = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var firstComplete = Store().Complete(task.Id, "complete-1", task.Version, Now.AddMinutes(1));
        var oldTicket = firstComplete.UndoOperationId;
        Assert.Equal("complete-1", oldTicket);
        var restored = Store().UndoComplete(task.Id, "undo-1", firstComplete.Version,
            Now.AddMinutes(2), completedOperationId: oldTicket);
        var latest = Store().Complete(task.Id, "complete-2", restored.Version, Now.AddMinutes(3));
        Assert.Equal("complete-2", latest.UndoOperationId);

        var ignoredOldTicket = Store().UndoComplete(task.Id, "old-ticket-undo", latest.Version,
            Now.AddMinutes(4), completedOperationId: oldTicket);

        AssertTask(latest, ignoredOldTicket);
        AssertTask(latest, Store().FindTask(task.Id)!);
        var current = Store().UndoComplete(task.Id, "latest-ticket-undo", latest.Version,
            Now.AddMinutes(5), completedOperationId: latest.UndoOperationId);
        Assert.Equal(TaskState.Active, current.State);
        Assert.Equal(latest.Version + 1, current.Version);
        Assert.Null(current.UndoOperationId);
        Assert.Null(current.UndoExpiresAt);
    }

    [Fact]
    public void UndoIgnored_CurrentVersionWithOldTicketCannotUndoNewIgnore()
    {
        var review = Store().SavePending("review-1", PendingCandidate(), "pending-1", Now);
        var firstIgnore = Store().IgnoreReview(review.Id, "ignore-1", review.Version, Now.AddMinutes(1));
        var oldTicket = firstIgnore.UndoOperationId;
        Assert.Equal("ignore-1", oldTicket);
        var restored = Store().UndoIgnored(review.Id, "undo-1", firstIgnore.Version,
            Now.AddMinutes(2), ignoredOperationId: oldTicket);
        var latest = Store().IgnoreReview(review.Id, "ignore-2", restored.Version, Now.AddMinutes(3));
        Assert.Equal("ignore-2", latest.UndoOperationId);

        var ignoredOldTicket = Store().UndoIgnored(review.Id, "old-ticket-undo", latest.Version,
            Now.AddMinutes(4), ignoredOperationId: oldTicket);

        AssertReview(latest, ignoredOldTicket);
        AssertReview(latest, Store().FindReview(review.Id)!);
        var current = Store().UndoIgnored(review.Id, "latest-ticket-undo", latest.Version,
            Now.AddMinutes(5), ignoredOperationId: latest.UndoOperationId);
        Assert.Equal(ReviewState.Pending, current.State);
        Assert.Equal(latest.Version + 1, current.Version);
        Assert.Null(current.UndoOperationId);
        Assert.Null(current.UndoExpiresAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(77)]
    public void Constructor_UnknownSchemaVersionPreservesExistingDatabase(int version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath())!);
        Execute($"PRAGMA user_version = {version}; CREATE TABLE sentinel(value TEXT); INSERT INTO sentinel VALUES ('preserve me');");

        Assert.Throws<InvalidDataException>(() => Store());

        Assert.Equal((long)version, Scalar("PRAGMA user_version;"));
        Assert.Equal("preserve me", Scalar("SELECT value FROM sentinel;"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"));
    }

    [Fact]
    public void Constructor_CopiedDatabaseOwnerMismatchPreservesBothAccountsData()
    {
        var task = Store().CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var review = Store().SavePending("review-1", PendingCandidate(), "pending-1", Now);
        Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        var copiedPath = Path.Combine(dataRoot, "accounts", OtherAccount, "tasks.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(copiedPath)!);
        File.Copy(DatabasePath(), copiedPath);
        var copiedBytes = File.ReadAllBytes(copiedPath);

        Assert.Throws<InvalidDataException>(() => Store(OtherAccount));

        Assert.Equal(copiedBytes, File.ReadAllBytes(copiedPath));
        AssertTask(task, Store().FindTask(task.Id)!);
        AssertReview(review, Store().FindReview(review.Id)!);
        Assert.Single(Store().ListTasks());
        Assert.Single(Store().ListReviews());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeavingCompleted_ClearsCurrentCompletionButKeepsCompletionHistory(bool archive)
    {
        var store = Store();
        var task = store.CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var completed = store.Complete(task.Id, "complete-1", task.Version, Now.AddMinutes(1));

        var current = archive
            ? store.Archive(task.Id, "archive-1", completed.Version, Now.AddMinutes(2))
            : store.Cancel(task.Id, "cancel-1", completed.Version, Now.AddMinutes(2));

        Assert.Equal(archive ? TaskState.Archived : TaskState.Cancelled, current.State);
        Assert.Null(current.CompletedAt);
        Assert.Null(current.UndoExpiresAt);
        Assert.Null(current.UndoOperationId);
        Assert.Equal(task.Evidence, current.Evidence);
        AssertTask(current, Store().FindTask(task.Id)!);
        using var history = JsonDocument.Parse((string)Scalar("SELECT after_json FROM operations WHERE operation_id = 'complete-1';")!);
        Assert.Equal(Now.AddMinutes(1), history.RootElement.GetProperty("CompletedAt").GetDateTimeOffset());
    }

    [Fact]
    public void LinkedCancel_ArchivedTaskClearsCurrentArchiveButKeepsArchiveHistory()
    {
        var store = Store();
        var task = store.CreateTask("task-1", TaskCandidate(), "create-1", Now);
        var archived = store.Archive(task.Id, "archive-1", task.Version, Now.AddMinutes(1));
        var candidate = PendingCandidate() with { TargetTaskId = task.Id, Action = RuleAction.Cancel, Time = null };
        var review = store.SavePending("review-1", candidate, "pending-1", Now.AddMinutes(2));

        var cancelled = store.AcceptReview(review.Id, task.Id, "accept-1", review.Version, null,
            Now.AddMinutes(3), archived.Version);

        Assert.Equal(TaskState.Cancelled, cancelled.State);
        Assert.Null(cancelled.ArchivedAt);
        Assert.Null(cancelled.CompletedAt);
        Assert.Null(cancelled.UndoExpiresAt);
        Assert.Equal(Now.AddMinutes(3), cancelled.CancelledAt);
        Assert.Equal(task.Evidence.Concat(candidate.Evidence).Distinct(), cancelled.Evidence);
        AssertTask(cancelled, Store().FindTask(task.Id)!);
        using var history = JsonDocument.Parse((string)Scalar("SELECT after_json FROM operations WHERE operation_id = 'archive-1';")!);
        Assert.Equal(Now.AddMinutes(1), history.RootElement.GetProperty("ArchivedAt").GetDateTimeOffset());
    }

    private AccountTaskStore Store(string account = Account) => new(dataRoot, account);
    private string DatabasePath() => Path.Combine(dataRoot, "accounts", Account, "tasks.sqlite3");
    private static TaskTime FormalTime() => new(TimeKind.Deadline, new(2026, 10, 16), null, "Asia/Shanghai");
    private static TaskTime SuggestedTime() => new(TimeKind.Deadline, new(2026, 10, 18), new(18, 0), "Asia/Shanghai");

    private static RuleCandidate TaskCandidate(string account = Account) => new(
        "提交高等数学实验报告", RuleAction.Submit, RuleDisposition.Task, "高等数学",
        FormalTime(), null, null, 2,
        new(new("需要提交", true), new("全体同学", true), new("课程已确认", true),
            new("提交截止日期明确", true), new("已确认发布者", true), new("新事项", false)),
        [], [new(account, Group, "-9007199254740997"), new(account, Group, "0009007199254740998")]);

    private static RuleCandidate PendingCandidate(string account = Account) => new(
        "提交图片中的实验材料", RuleAction.Submit, RuleDisposition.Pending, "高等数学",
        null, SuggestedTime(), null, null,
        new(new("需要提交", true), new("全体同学", true), new("课程已确认", true),
            new("图片日期未经确认", false), new("已确认发布者", true), new("新事项", false)),
        ["请查看原图", "需要确认图片中的截止日期"],
        [new(account, Group, "-9007199254740999"), new(account, Group, "0009007199254741000")]);

    private static RuleCandidate LinkedCandidate(RuleAction action) => TaskCandidate() with
    {
        Title = action == RuleAction.Cancel ? "实验报告已取消" : "实验报告的新要求",
        Action = action,
        Disposition = RuleDisposition.Pending,
        TargetTaskId = "task-1",
        ReviewReasons = ["需要确认对已有事项的变更"],
        Evidence = [new(Account, Group, "-9007199254740997"), new(Account, Group, "linked-change-message")]
    };

    private static void AssertCandidate(RuleCandidate expected, RuleCandidate actual)
    {
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Action, actual.Action);
        Assert.Equal(expected.Disposition, actual.Disposition);
        Assert.Equal(expected.Course, actual.Course);
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.SuggestedTime, actual.SuggestedTime);
        Assert.Equal(expected.TargetTaskId, actual.TargetTaskId);
        Assert.Equal(expected.Priority, actual.Priority);
        Assert.Equal(expected.Assessments, actual.Assessments);
        Assert.Equal(expected.ReviewReasons, actual.ReviewReasons);
        Assert.Equal(expected.Evidence, actual.Evidence);
    }

    private static void AssertTask(TaskItem expected, TaskItem actual)
    {
        Assert.Equal(expected with { Evidence = actual.Evidence }, actual);
        Assert.Equal(expected.Evidence, actual.Evidence);
    }

    private static void AssertReview(PendingReview expected, PendingReview actual)
    {
        Assert.Equal(expected with { Candidate = actual.Candidate }, actual);
        AssertCandidate(expected.Candidate, actual.Candidate);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = DatabasePath(), Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private object? Scalar(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void Execute(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private IReadOnlyList<string> InstallUpdateFailureTriggers()
    {
        var tables = new List<string>();
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = command.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }
        Assert.NotEmpty(tables);
        var triggers = new List<string>();
        for (var i = 0; i < tables.Count; i++)
        {
            var name = "fail_accept_review_" + i;
            var quotedTable = tables[i].Replace("\"", "\"\"");
            Execute($"CREATE TRIGGER \"{name}\" BEFORE UPDATE ON \"{quotedTable}\" BEGIN SELECT RAISE(ABORT, 'injected lifecycle storage failure'); END;");
            triggers.Add(name);
        }
        return triggers;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
    }
}
