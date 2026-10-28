using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Backup;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class CourseRecheckTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string OtherAccount = "9007199254740994";
    private const string Group = "9007199254740995";
    private const string OtherGroup = "9007199254740996";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-course-recheck-" + Guid.NewGuid().ToString("N"));
    private static HashSet<string> Pinned() => [Group];
    private AccountTaskStore Tasks(string account = Account) => new(root, account);
    private AccountMessageStore Messages(string account = Account) => new(root, account);
    private AccountCandidateRouter Router(string account = Account) => new(Messages(account), Tasks(account));
    private AccountWorkspace Workspace(string account = Account, bool pinned = true)
    {
        var workspace = new AccountWorkspace(root, account);
        workspace.SetAvailableGroups([new(Group, "数学课程群"), new(OtherGroup, "另一课程群")], pinned ? Pinned() : []);
        return workspace;
    }
    private static SchoolMessage Message(string id = "1", string text = "明天18点提交统计作业",
        DateTimeOffset? sentAt = null, string account = Account, string group = Group) =>
        new(new(account, group, id), "teacher", sentAt ?? Now.AddMinutes(-1), [new(SegmentKind.Text, text)]);
    private static void Confirm(AccountWorkspace workspace, string? course = "数学", bool excluded = false,
        string group = Group, DateTimeOffset? at = null) =>
        workspace.Execute(new(workspace.AccountId, "confirm-group", group, Guid.NewGuid().ToString("N"), 0,
            Course: course, Excluded: excluded, Generation: workspace.Generation), at ?? Now);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    // Older versions could persist a message as automatically excluded. No human review exists.
    private void SavePreviouslyExcluded(SchoolMessage message)
    {
        var store = Messages(message.Key.AccountId);
        Assert.True(store.TryEnqueue(message, MessageOrigin.History, "old-history", Now.AddDays(-1)));
        var oldContext = new RuleContext(message.Key.AccountId, message.Key.GroupId, "Asia/Shanghai", Now,
            ConfirmedCourse: "旧课程", CourseExcluded: true);
        Assert.True(store.TrySaveExtraction(message.Key, 0, MessageRuleExtractor.Extract(message, oldContext), Now.AddDays(-1)));
        var router = Router(message.Key.AccountId);
        Assert.Equal(1, router.RoutePending(oldContext with { CourseExcluded = false }, new HashSet<string> { message.Key.GroupId }));
        Assert.Equal(RuleDisposition.Ignored, Assert.Single(store.Find(message.Key)!.Extraction!.Candidates).Disposition);
    }

    [Fact]
    public void EmptyGroup_RecordsZeroActualScopeAndPersistsResultOnReopen()
    {
        var workspace = Workspace();
        Confirm(workspace);
        var snapshot = workspace.Snapshot(Now);
        var result = Assert.IsType<CourseRecheckResult>(snapshot.CourseRecheck);
        Assert.Equal(Account, result.AccountId);
        Assert.Equal(Group, result.GroupId);
        Assert.Equal("数学", result.Course);
        Assert.Equal(Now, result.CompletedAt);
        Assert.Equal(0, result.CheckedMessages);
        Assert.Equal(0, result.CheckedTasks);
        Assert.Null(result.FirstMessageAt);
        Assert.Null(result.LastMessageAt);
        Assert.Null(snapshot.CourseRecheckError);
        Assert.Equal(Json(result), Json(Workspace().Snapshot(Now).CourseRecheck));
    }

    [Theory]
    [InlineData("明天18点提交统计作业", "task")]
    [InlineData("明天18点自愿报名讲座", "notice")]
    [InlineData("提交统计作业", "review")]
    public void PreviouslyAutomaticallyExcludedMessage_IsRecheckedUnderCurrentRelation(string text, string kind)
    {
        var workspace = Workspace();
        var message = Message(text: text);
        SavePreviouslyExcluded(message);
        Confirm(workspace);
        var snapshot = workspace.Snapshot(Now);
        var item = Assert.Single(snapshot.Items);
        Assert.Equal(kind, item.Kind);
        Assert.Equal("数学", item.Course);
        Assert.Equal(message.Segments[0].Text, Assert.Single(item.Evidence).Text);
        Assert.Equal(1, snapshot.CourseRecheck!.CheckedMessages);
        Assert.Equal(kind == "task" ? 1 : 0, snapshot.CourseRecheck.AddedTasks);
        Assert.Equal(kind == "notice" ? 1 : 0, snapshot.CourseRecheck.AddedNotices);
        Assert.Equal(kind == "review" ? 1 : 0, snapshot.CourseRecheck.AddedReviews);
    }

    [Fact]
    public void RetainedMessageOlderThanSevenDays_UsesOriginalSendDateForRelativeTime()
    {
        var workspace = Workspace();
        var sent = new DateTimeOffset(2026, 10, 8, 23, 50, 0, TimeSpan.FromHours(8));
        SavePreviouslyExcluded(Message(sentAt: sent));
        Confirm(workspace);
        var snapshot = workspace.Snapshot(Now);
        var task = Assert.Single(snapshot.Items);
        Assert.Equal("2026-10-09", task.Due);
        Assert.Equal("18:00", task.Time);
        Assert.Equal(sent, snapshot.CourseRecheck!.FirstMessageAt);
        Assert.Equal(sent, snapshot.CourseRecheck.LastMessageAt);
    }

    [Fact]
    public void RepeatedSaveAndReopen_DoNotDuplicateRecoveredTaskOrRewriteOriginalMessage()
    {
        var workspace = Workspace();
        var message = Message();
        SavePreviouslyExcluded(message);
        Confirm(workspace);
        var task = Json(Assert.Single(Tasks().ListTasks()));
        var stored = Messages().Find(message.Key)!;
        var historyCount = Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM operations;");
        Confirm(workspace, at: Now.AddMinutes(1));
        var reopened = Workspace();
        Confirm(reopened, at: Now.AddMinutes(2));
        Assert.Equal(task, Json(Assert.Single(Tasks().ListTasks())));
        Assert.Equal(historyCount, Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM operations;"));
        Assert.Equal(Json(stored.Message), Json(Messages().Find(message.Key)!.Message));
        Assert.Equal(0, reopened.Snapshot(Now).CourseRecheck!.AddedTasks);
        Assert.Equal(0, reopened.Snapshot(Now).CourseRecheck!.AddedReviews);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("ignored")]
    [InlineData("rejected")]
    [InlineData("accepted")]
    public void ExistingReviewAndHumanDecision_AreNeverReplayedOrRewritten(string state)
    {
        var workspace = Workspace();
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        var store = Tasks();
        var review = Assert.Single(store.ListReviews());
        if (state == "ignored") store.IgnoreReview(review.Id, "ignore", review.Version, Now);
        if (state == "rejected") store.RejectReview(review.Id, "reject", review.Version, Now);
        if (state == "accepted") store.AcceptReview(review.Id, "accepted-task", "accept", review.Version,
            new(TimeKind.Deadline, new(2026, 10, 29), new(18, 0), "Asia/Shanghai"), Now);
        var originalReview = Json(store.FindReview(review.Id));
        var originalTasks = Json(store.ListTasks(includeMerged: true));
        Confirm(workspace, "物理");
        Confirm(workspace, "物理", at: Now.AddMinutes(1));
        Assert.Equal(originalReview, Json(store.FindReview(review.Id)));
        Assert.Equal(originalTasks, Json(store.ListTasks(includeMerged: true)));
        Assert.DoesNotContain(store.ListReviews(), item => item.Id != review.Id && item.Candidate.TargetTaskId is null);
        Assert.Equal(0, workspace.Snapshot(Now).CourseRecheck!.AddedTasks);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("archived")]
    [InlineData("edited")]
    public void CourseConflict_PreservesTaskFieldsStateUndoAndHistory(string state)
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        var store = Tasks();
        var task = Assert.Single(store.ListTasks());
        if (state == "completed") store.Complete(task.Id, "complete", task.Version, Now);
        if (state == "cancelled") store.Cancel(task.Id, "cancel", task.Version, Now);
        if (state == "archived") store.Archive(task.Id, "archive", task.Version, Now);
        if (state == "edited") store.EditTask(task.Id, "edit", task.Version, "人工改过的标题", "自定义分类",
            new(TimeKind.Start, new(2026, 11, 1), null, "Asia/Shanghai"), 0, Now);
        var original = Json(store.FindTask(task.Id));
        var history = Json(store.ListTaskHistory());
        Confirm(workspace, "物理");
        var snapshot = workspace.Snapshot(Now);
        var conflict = Assert.Single(snapshot.Items, item => item.IsCourseConflict);
        Assert.Equal("review", conflict.Kind);
        Assert.Equal(task.Id, conflict.TargetTaskId);
        Assert.False(conflict.CanAccept);
        Assert.False(conflict.CanAcceptNotice);
        Assert.Equal(original, Json(store.FindTask(task.Id)));
        Assert.Equal(history, Json(store.ListTaskHistory()));
        Assert.Equal(1, snapshot.CourseRecheck!.CheckedTasks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingOrExcludingCourse_CreatesConflictWithoutDeletingTask(bool excluded)
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        var original = Json(Assert.Single(Tasks().ListTasks()));
        Confirm(workspace, excluded ? "数学" : null, excluded);
        Assert.Equal(original, Json(Assert.Single(Tasks().ListTasks())));
        Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DismissedCourseConflict_IsNotRecreatedBySameRelationOrReopen(bool reject)
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        Confirm(workspace, "物理");
        var conflict = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict);
        var store = Tasks();
        if (reject) store.RejectReview(conflict.Id, "keep-original", conflict.Version, Now);
        else store.IgnoreReview(conflict.Id, "ignore-conflict", conflict.Version, Now);
        var original = Json(store.FindReview(conflict.Id));
        Confirm(Workspace(), "物理", at: Now.AddMinutes(2));
        Assert.Equal(original, Json(store.FindReview(conflict.Id)));
        Assert.Single(store.ListReviews());
        Assert.Equal(0, Workspace().Snapshot(Now).CourseRecheck!.AddedReviews);
    }

    [Fact]
    public void RestoringOriginalCourse_ConsolidatesObsoleteConflictAndPreservesItsAudit()
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        var task = Json(Assert.Single(Tasks().ListTasks()));
        Confirm(workspace, "物理");
        var conflict = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict);
        var oldAudit = OperationRows();

        Confirm(workspace, "数学", at: Now.AddMinutes(1));

        var settled = Tasks().FindReview(conflict.Id)!;
        Assert.Equal(ReviewState.Consolidated, settled.State);
        Assert.Equal(conflict.Version + 1, settled.Version);
        Assert.DoesNotContain(workspace.Snapshot(Now).Items, item => item.IsCourseConflict && item.State == "pending");
        Assert.Equal(task, Json(Assert.Single(Tasks().ListTasks())));
        Assert.Equal(oldAudit, OperationRows().Take(oldAudit.Count).ToArray());
        Assert.True(OperationRows().Count > oldAudit.Count);
    }

    [Fact]
    public void ChangingToAnotherConflictingCourse_ConsolidatesOldSuggestionAndKeepsOnlyCurrentPending()
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        Confirm(workspace, "物理");
        var physics = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict);

        Confirm(workspace, "化学", at: Now.AddMinutes(1));

        var chemistry = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict && item.State == "pending");
        Assert.Equal("化学", chemistry.Course);
        Assert.NotEqual(physics.Id, chemistry.Id);
        Assert.Equal(ReviewState.Consolidated, Tasks().FindReview(physics.Id)!.State);
        Assert.Equal(physics.Version + 1, Tasks().FindReview(physics.Id)!.Version);
        Assert.Equal(ReviewState.Pending, Tasks().FindReview(chemistry.Id)!.State);
    }

    [Fact]
    public void ReturningToPreviouslyConflictingCourse_ReopensOnlyAutomaticallyConsolidatedSuggestion()
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        Confirm(workspace, "物理");
        var physics = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict);
        Confirm(workspace, "化学", at: Now.AddMinutes(1));
        var closedPhysics = Tasks().FindReview(physics.Id)!;
        Assert.Equal(ReviewState.Consolidated, closedPhysics.State);
        var chemistry = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict && item.State == "pending");

        Confirm(workspace, "物理", at: Now.AddMinutes(2));

        var reopened = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict && item.State == "pending");
        Assert.Equal(physics.Id, reopened.Id);
        Assert.Equal(closedPhysics.Version + 1, reopened.Version);
        Assert.Equal(ReviewState.Consolidated, Tasks().FindReview(chemistry.Id)!.State);
        Assert.Equal(2, Tasks().ListReviews().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturningToDismissedConflictingCourse_DoesNotReopenHumanIgnoredOrRejectedSuggestion(bool reject)
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        Confirm(workspace, "物理");
        var physics = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict);
        var store = Tasks();
        if (reject) store.RejectReview(physics.Id, "keep-original", physics.Version, Now);
        else store.IgnoreReview(physics.Id, "ignore-conflict", physics.Version, Now);
        var dismissed = Json(store.FindReview(physics.Id));
        Confirm(workspace, "化学", at: Now.AddMinutes(1));
        Confirm(workspace, "物理", at: Now.AddMinutes(2));
        Assert.Equal(dismissed, Json(store.FindReview(physics.Id)));
        Assert.DoesNotContain(workspace.Snapshot(Now).Items,
            item => item.IsCourseConflict && item.State == "pending" && item.Course == "物理");
    }

    [Fact]
    public void CourseConflict_CannotBypassUiAndAcceptAsLinkedTask()
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        Confirm(workspace, "物理");
        var conflict = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict);
        var task = Assert.Single(Tasks().ListTasks());
        Assert.ThrowsAny<Exception>(() => Tasks().AcceptReview(conflict.Id, task.Id, "forged-accept",
            conflict.Version, task.Time, Now, task.Version));
        Assert.Equal(Json(task), Json(Tasks().FindTask(task.Id)));
        Assert.Equal(ReviewState.Pending, Tasks().FindReview(conflict.Id)!.State);
    }

    [Fact]
    public void CourseConflict_CannotBypassUiAndAcceptAsNotice()
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        Confirm(workspace, "物理");
        var conflict = Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict);
        Assert.ThrowsAny<Exception>(() => Router().AcceptReviewAsNotice(conflict.Id, "forged-notice",
            conflict.Version, Now));
        Assert.Empty(Router().ListNotices());
        Assert.Equal(ReviewState.Pending, Tasks().FindReview(conflict.Id)!.State);
    }

    [Fact]
    public void ExistingNotice_IsNotDuplicatedOrConvertedToTask()
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(text: "明天18点自愿报名讲座"), Pinned(), MessageOrigin.History, "history", Now);
        var notice = Json(Assert.Single(Router().ListNotices()));
        Confirm(workspace, "物理");
        Assert.Equal(notice, Json(Assert.Single(Router().ListNotices())));
        Assert.Empty(Tasks().ListTasks());
        Assert.Equal(0, workspace.Snapshot(Now).CourseRecheck!.AddedTasks);
        Assert.Equal(0, workspace.Snapshot(Now).CourseRecheck!.AddedNotices);
    }

    [Fact]
    public void UnpinnedGroup_DoesNotRecoverMessagesButCanFlagItsExistingTask()
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        SavePreviouslyExcluded(Message("2", "明天18点提交另一报告"));
        workspace = Workspace(pinned: false);
        Confirm(workspace, "物理");
        Assert.Single(Tasks().ListTasks());
        Assert.Single(workspace.Snapshot(Now).Items, item => item.IsCourseConflict);
        Assert.True(workspace.Snapshot(Now).CourseRecheck!.SkippedUnpinned);
        Assert.Equal(0, workspace.Snapshot(Now).CourseRecheck!.AddedTasks);
    }

    [Fact]
    public void Recheck_IsLimitedToCurrentAccountAndSelectedGroup()
    {
        var workspace = Workspace();
        SavePreviouslyExcluded(Message());
        SavePreviouslyExcluded(Message("2", account: OtherAccount));
        SavePreviouslyExcluded(Message("3", group: OtherGroup));
        var otherAccount = Json(Messages(OtherAccount).Find(Message("2", account: OtherAccount).Key));
        var otherGroup = Json(Messages().Find(Message("3", group: OtherGroup).Key));
        Confirm(workspace);
        Assert.Single(Tasks().ListTasks());
        Assert.Empty(Tasks(OtherAccount).ListTasks());
        Assert.Equal(otherAccount, Json(Messages(OtherAccount).Find(Message("2", account: OtherAccount).Key)));
        Assert.Equal(otherGroup, Json(Messages().Find(Message("3", group: OtherGroup).Key)));
        Assert.Equal(1, workspace.Snapshot(Now).CourseRecheck!.CheckedMessages);
    }

    [Fact]
    public void MissingEvidence_IsReportedWithoutFabricatingDeletedMessages()
    {
        var workspace = Workspace();
        Confirm(workspace);
        var message = Message();
        workspace.Ingest(message, Pinned(), MessageOrigin.History, "history", Now);
        var task = Json(Assert.Single(Tasks().ListTasks()));
        Execute(Messages().DatabasePath, "DELETE FROM messages;");
        Confirm(workspace);
        var snapshot = workspace.Snapshot(Now);
        Assert.Equal(0, snapshot.CourseRecheck!.CheckedMessages);
        Assert.Equal(1, snapshot.CourseRecheck.MissingEvidenceMessages);
        Assert.Null(Messages().Find(message.Key));
        Assert.Equal(task, Json(Assert.Single(Tasks().ListTasks())));
    }

    [Fact]
    public void ManualTaskWithoutGroupEvidence_IsNotAffectedByGroupRelation()
    {
        var workspace = Workspace();
        var store = Tasks();
        store.CreateManualTask("manual", "口头布置的作业", "数学",
            new(TimeKind.Deadline, new(2026, 10, 30), null, "Asia/Shanghai"), 1, "manual-op", Now);
        var before = Json(store.ListTasks());
        Confirm(workspace, "物理");
        Assert.Equal(before, Json(store.ListTasks()));
        Assert.Empty(store.ListReviews());
        Assert.Equal(0, workspace.Snapshot(Now).CourseRecheck!.CheckedTasks);
    }

    [Fact]
    public void MergedTasks_KeepTheirFieldsEvidenceAndRedirectsAfterRecheck()
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        workspace.Ingest(Message("2", "明天18点提交实验报告"), Pinned(), MessageOrigin.History, "history", Now);
        var store = Tasks();
        var tasks = store.ListTasks();
        Assert.Equal(2, tasks.Count);
        store.MergeTasks(tasks[0].Id, tasks[1].Id, "merge", tasks[0].Version, tasks[1].Version,
            new(tasks[0].Id, tasks[0].Id, tasks[0].Id, tasks[0].Id, tasks[0].Id), Now);
        var before = Json(store.ListTasks(includeMerged: true));
        var history = Json(store.ListTaskHistory());
        Confirm(workspace, "物理");
        Assert.Equal(before, Json(store.ListTasks(includeMerged: true)));
        Assert.Equal(history, Json(store.ListTaskHistory()));
        Assert.Single(store.ListTasks());
        Assert.Equal(tasks[0].Id, store.ResolveTaskId(tasks[1].Id));
        Assert.Equal(0, workspace.Snapshot(Now).CourseRecheck!.AddedTasks);
    }

    [Fact]
    public void ManuallySplitReview_AndItsParentAreNotRecreatedOrRewritten()
    {
        var workspace = Workspace();
        Confirm(workspace);
        workspace.Ingest(Message(), Pinned(), MessageOrigin.History, "history", Now);
        var store = Tasks();
        var parent = Assert.Single(store.ListTasks());
        store.SplitTask(parent.Id, "split-review", "split", parent.Version,
            new("单独核对的要求", "数学", null, 2, TaskState.Active, parent.Evidence), Now);
        var tasks = Json(store.ListTasks(includeMerged: true));
        var review = Json(store.FindReview("split-review"));
        Confirm(workspace, "物理");
        Assert.Equal(tasks, Json(store.ListTasks(includeMerged: true)));
        Assert.Equal(review, Json(store.FindReview("split-review")));
        Assert.Single(store.ListTasks());
        Assert.Equal(0, workspace.Snapshot(Now).CourseRecheck!.AddedTasks);
    }

    [Fact]
    public void RecoveryFailure_RollsBackBusinessWritesAndCanRetryAfterRelationWasSaved()
    {
        var workspace = Workspace();
        var message = Message();
        SavePreviouslyExcluded(message);
        var before = Json(Messages().Find(message.Key));
        var receiptCount = Scalar(Router().RoutingDatabasePath, "SELECT COUNT(*) FROM route_receipts;");
        Execute(Tasks().DatabasePath,
            "CREATE TRIGGER fail_course_task BEFORE INSERT ON tasks BEGIN SELECT RAISE(ABORT, 'injected course recheck failure'); END;");
        _ = Record.Exception(() => Confirm(workspace, "物理"));
        Assert.Equal("物理", workspace.Snapshot(Now).Groups.Single(group => group.GroupId == Group).Course);
        Assert.NotNull(workspace.Snapshot(Now).CourseRecheckError);
        Assert.Empty(Tasks().ListTasks());
        Assert.Empty(Tasks().ListReviews());
        Assert.Equal(before, Json(Messages().Find(message.Key)));
        Assert.Equal(receiptCount, Scalar(Router().RoutingDatabasePath, "SELECT COUNT(*) FROM route_receipts;"));
        Execute(Tasks().DatabasePath, "DROP TRIGGER fail_course_task;");
        Confirm(workspace, "物理", at: Now.AddMinutes(1));
        Assert.Equal("物理", Assert.Single(Tasks().ListTasks()).Course);
        Assert.Null(workspace.Snapshot(Now).CourseRecheckError);
    }

    [Fact]
    public void RestoringOldBackupWithoutRecheckTable_AllowsSnapshotAndFurtherRecheckOnSameWorkspace()
    {
        var workspace = Workspace();
        Confirm(workspace);
        var preferences = new BackupApplicationSettings(false, "dark", "reduce", null);
        var archive = Path.Combine(root, "legacy-backup.zip");
        // Reproduce a valid pre-feature routing database instead of editing the ZIP or its manifest.
        Execute(Router().RoutingDatabasePath, "DROP TABLE course_recheck_result;");
        workspace.ExportBackup(archive, preferences, Now);
        Confirm(workspace, "物理");
        Assert.NotNull(workspace.Snapshot(Now).CourseRecheck);
        using var prepared = workspace.InspectBackup(archive);
        workspace.RestoreBackup(prepared, preferences, Now.AddMinutes(1));
        Assert.Null(workspace.Snapshot(Now).CourseRecheck);
        Assert.Null(workspace.Snapshot(Now).CourseRecheckError);
        Assert.Equal("数学", workspace.Snapshot(Now).Groups.Single(group => group.GroupId == Group).Course);
        Confirm(workspace, "数学", at: Now.AddMinutes(2));
        Assert.NotNull(workspace.Snapshot(Now).CourseRecheck);
        Assert.Equal(Now.AddMinutes(2), workspace.Snapshot(Now).CourseRecheck!.CompletedAt);
    }

    private static void Execute(string path, string sql)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static long Scalar(string path, string sql)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
    private List<string> OperationRows()
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Tasks().DatabasePath, Pooling = false }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT operation_id,request_json,result_kind,result_id,processed_at,before_json,after_json FROM operations ORDER BY sequence;";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(Json(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetString(index)).ToArray()));
        return rows;
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
