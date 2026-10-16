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

public sealed class NoticeReviewAcceptanceTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string Group = "9007199254740995";
    private const string Text = "关于合成餐饮比赛赛前交流工作的通知：\n时间：下周二(10.20)下午1点～4点\n地点：合成楼1-101";
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 16, 0, 0, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-notice-review-" + Guid.NewGuid().ToString("N"));
    private AccountWorkspace Workspace() => new(root, Account);
    private AccountTaskStore Tasks() => new(root, Account);
    private AccountMessageStore Messages() => new(root, Account);
    private AccountCandidateRouter Router() => new(Messages(), Tasks());
    private static HashSet<string> Pinned() => [Group];
    private static SchoolMessage Message(string id = "9", string text = Text) =>
        new(new(Account, Group, id), "teacher", Now.AddMinutes(-17), [new(SegmentKind.Text, text)]);
    private static WorkspaceCommand Accept(ApplicationItem review, string operation = "accept-notice") =>
        new(Account, operation, review.Id, "confirm-notice-" + review.Id, review.Version);
    private ApplicationItem Ingest(AccountWorkspace workspace, SchoolMessage? message = null)
    {
        workspace.SetAvailableGroups([new OneBotGroup(Group, "合成比赛通知群")], Pinned());
        Assert.True(workspace.Ingest(message ?? Message(), Pinned(), MessageOrigin.History, "history", Now));
        return Assert.Single(workspace.Snapshot(Now).Items, item => item.Kind == "review" && item.State == "pending");
    }

    [Fact]
    public void ExtractedInform_ConfirmsWithoutDateOrCourseAndPreservesOriginalAudit()
    {
        var workspace = Workspace();
        var review = Ingest(workspace);
        Assert.Equal("inform", review.Action);
        Assert.False(review.CanAccept);
        Assert.True(review.CanAcceptNotice);
        var before = Tasks().FindReview(review.Id)!;
        var messageBefore = JsonSerializer.Serialize(Messages().Find(Message().Key));

        workspace.Execute(Accept(review), Now);

        var notice = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("notice", notice.Kind);
        Assert.Equal("notice-review-" + review.Id, notice.Id);
        Assert.Equal(review.Title, notice.Title);
        Assert.Null(notice.Course);
        Assert.Equal(Text, Assert.Single(notice.Evidence).Text);
        Assert.Equal(ReviewState.Accepted, Tasks().FindReview(review.Id)!.State);
        Assert.Equal(before.Version + 1, Tasks().FindReview(review.Id)!.Version);
        Assert.Null(Tasks().FindReview(review.Id)!.AcceptedTaskId);
        Assert.Equal(JsonSerializer.Serialize(before.Candidate), JsonSerializer.Serialize(Tasks().FindReview(review.Id)!.Candidate));
        Assert.Empty(Tasks().ListTasks());
        Assert.Null(Assert.Single(workspace.Snapshot(Now).Groups).Course);
        Assert.Equal(messageBefore, JsonSerializer.Serialize(Messages().Find(Message().Key)));
        var stored = Assert.Single(Router().ListNotices());
        Assert.Equal(RuleDisposition.Notice, stored.Candidate.Disposition);
        Assert.Equal(Message().Key, stored.Source);
        Assert.Equal(MessageOrigin.History, stored.Origin);
        Assert.Equal("history", stored.BatchId);
        Assert.Equal(Message().SentAt, stored.SentAt);
        Assert.Equal(Messages().Find(Message().Key)!.ProcessingVersion, stored.ProcessingVersion);
        var acceptedJson = JsonSerializer.Serialize(Tasks().FindReview(review.Id));
        using var connection = Open(Tasks().DatabasePath);
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT before_json, after_json, result_kind FROM operations WHERE operation_id = $id";
        query.Parameters.AddWithValue("$id", Accept(review).OperationId);
        using var reader = query.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(JsonSerializer.Serialize(before), reader.GetString(0));
        Assert.Equal(acceptedJson, reader.GetString(1));
        Assert.Equal("review", reader.GetString(2));
    }

    [Fact]
    public void ExistingReview_ReopenAndSameRequestRetryKeepOneNoticeAndOneAcceptance()
    {
        var workspace = Workspace();
        var review = Ingest(workspace);
        workspace = Workspace();
        workspace.Execute(Accept(review), Now);
        var saved = JsonSerializer.Serialize(Tasks().FindReview(review.Id));
        workspace = Workspace();
        workspace.Execute(Accept(review), Now.AddMinutes(1));
        Assert.Equal(saved, JsonSerializer.Serialize(Tasks().FindReview(review.Id)));
        Assert.Single(workspace.Snapshot(Now).Items, item => item.Kind == "notice");
        Assert.Empty(Tasks().ListTasks());
        Assert.False(workspace.Enqueue(Message(), Pinned(), MessageOrigin.Realtime, "repeat", Now));
        Assert.Equal(0, workspace.ProcessPending(Pinned(), Now));
        Assert.Equal(2, Count(Tasks().DatabasePath, "operations"));
    }

    [Theory]
    [InlineData("routing", "notices", "INSERT")]
    [InlineData("tasks", "reviews", "UPDATE")]
    [InlineData("tasks", "operations", "INSERT")]
    public void AtomicFailure_RollsBackNoticeReviewAndAuditAndAllowsRetry(string database, string table, string action)
    {
        var workspace = Workspace();
        var review = Ingest(workspace);
        var before = JsonSerializer.Serialize(Tasks().FindReview(review.Id));
        var databasePath = database == "routing" ? Router().RoutingDatabasePath : Tasks().DatabasePath;
        using (var connection = Open(databasePath))
        using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText = $"CREATE TRIGGER fail_confirmation BEFORE {action} ON {table} BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;";
            trigger.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => workspace.Execute(Accept(review), Now));
        Assert.Equal(before, JsonSerializer.Serialize(Tasks().FindReview(review.Id)));
        Assert.Empty(Router().ListNotices());
        Assert.Equal(1, Count(Tasks().DatabasePath, "operations"));
        Assert.Empty(Tasks().ListTasks());
        using (var connection = Open(databasePath))
        using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText = "DROP TRIGGER fail_confirmation;";
            trigger.ExecuteNonQuery();
        }
        Workspace().Execute(Accept(review), Now);
        Assert.Single(Router().ListNotices());
        Assert.Equal(ReviewState.Accepted, Tasks().FindReview(review.Id)!.State);
    }

    [Fact]
    public void WrongAccountStaleVersionAndRestoredGenerationCannotConfirm()
    {
        var workspace = Workspace();
        var review = Ingest(workspace);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Accept(review) with { AccountId = "123" }, Now));
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Accept(review) with { Version = review.Version + 1 }, Now));
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Accept(review) with { Generation = 1 }, Now));
        Assert.Empty(Router().ListNotices());
        Assert.Equal(review.Version, Tasks().FindReview(review.Id)!.Version);
    }

    [Theory]
    [InlineData("ignore")]
    [InlineData("reject")]
    public void ProcessedReviewCannotConfirmEvenWithCurrentVersion(string operation)
    {
        var workspace = Workspace();
        var review = Ingest(workspace);
        workspace.Execute(Accept(review, operation), Now);
        var current = Tasks().FindReview(review.Id)!;
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Accept(review) with
            { OperationId = "late-accept", Version = current.Version }, Now));
        Assert.Empty(Router().ListNotices());
    }

    [Fact]
    public void ActionableReviewCannotBeConfirmedAsNotice()
    {
        var workspace = Workspace();
        var review = Ingest(workspace, Message(text: "10月20日18点提交报告"));
        Assert.Equal("submit", review.Action);
        Assert.False(review.CanAcceptNotice);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Accept(review), Now));
        Assert.Empty(Router().ListNotices());
        Assert.Empty(Tasks().ListTasks());
    }

    [Theory]
    [InlineData("下周一18:00自愿报名讲座", "register")]
    [InlineData("自愿参加讲座", "attend")]
    [InlineData("有兴趣的同学明天提交竞赛报名表", "submit")]
    public void OptionalActionInExistingPendingCanBeKeptAsNotice(string text, string action)
    {
        var workspace = Workspace();
        var review = Ingest(workspace, Message(text: text));
        workspace = Workspace();
        review = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal(action, review.Action);
        Assert.False(review.CanAccept);
        Assert.True(review.CanAcceptNotice);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Accept(review, "accept-review") with { Date = "2026-10-20" }, Now));
        workspace.Execute(Accept(review), Now);
        Workspace().Execute(Accept(review), Now);
        Assert.Equal("notice", Assert.Single(Workspace().Snapshot(Now).Items).Kind);
        Assert.Empty(Tasks().ListTasks());
        Assert.Equal(text, Assert.Single(Router().ListNotices()).Candidate.Title);
    }

    [Theory]
    [InlineData("明天必须参加考试；后天自愿参加讲座")]
    [InlineData("明天交实验报告，自愿参加")]
    public void MixedMandatoryAndOptionalMessageCannotBeKeptAsNotice(string text)
    {
        var workspace = Workspace();
        var review = Ingest(workspace, Message(text: text));
        Assert.True(review.CanAccept);
        Assert.False(review.CanAcceptNotice);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Accept(review), Now));
        Assert.Empty(Router().ListNotices());
    }

    [Fact]
    public void AcceptedReview_RejectsChangedRetriesAndFreshRequestsWithoutDuplicating()
    {
        var workspace = Workspace();
        var review = Ingest(workspace);
        var command = Accept(review);
        workspace.Execute(command, Now);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(command with { Version = review.Version + 1 }, Now));
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(command with { OperationId = "fresh-old-version" }, Now));
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(command with
            { OperationId = "fresh-current-version", Version = review.Version + 1 }, Now));
        Assert.Single(Router().ListNotices());
        Assert.Equal(2, Count(Tasks().DatabasePath, "operations"));
    }

    [Fact]
    public void NoticeAcceptance_DoesNotSilentlyApplyTaskDate()
    {
        var workspace = Workspace();
        var review = Ingest(workspace);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Accept(review) with { Date = "2026-10-20" }, Now));
        Assert.Empty(Router().ListNotices());
        Assert.Equal(ReviewState.Pending, Tasks().FindReview(review.Id)!.State);
    }

    [Fact]
    public void QuotedEvidenceBeforeSource_PreservesCorrectOriginalMessageAndAllEvidence()
    {
        var workspace = Workspace();
        var quoted = Message("1", "原通知：周末道路维修，请注意绕行");
        workspace.Ingest(quoted, Pinned(), MessageOrigin.Realtime, "quoted", Now);
        var source = Message() with { Segments = [new(SegmentKind.Text, Text), new(SegmentKind.Quote, "原通知", quoted.Key)] };
        workspace.Ingest(source, Pinned(), MessageOrigin.History, "source", Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items, item => item.Kind == "review" && item.Title.Contains("合成餐饮"));
        Assert.Equal(quoted.Segments[0].Text, review.Evidence[0].Text);
        workspace.Execute(Accept(review), Now);
        var notice = Assert.Single(Router().ListNotices());
        Assert.Equal(source.Key, notice.Source);
        Assert.Equal(MessageOrigin.History, notice.Origin);
        Assert.Equal("source", notice.BatchId);
        Assert.Contains(quoted.Key, notice.Candidate.Evidence);
        Assert.Contains(source.Key, notice.Candidate.Evidence);
    }

    [Fact]
    public void ConfirmedNoticeAndAcceptedReview_SurviveBackupRestoreAndOldMessageCleanup()
    {
        var workspace = Workspace();
        var review = Ingest(workspace);
        workspace.Execute(Accept(review), Now);
        var audit = JsonSerializer.Serialize(Tasks().FindReview(review.Id));
        var archive = Path.Combine(root, "notices.zip");
        var preferences = new BackupApplicationSettings(false, "dark", "reduce", null);
        workspace.ExportBackup(archive, preferences, Now);
        using var prepared = workspace.InspectBackup(archive);
        workspace.RestoreBackup(prepared, preferences, Now.AddMinutes(1));
        workspace.CleanMessageCache(Now.AddDays(10));
        Assert.Equal(audit, JsonSerializer.Serialize(Tasks().FindReview(review.Id)));
        var notice = Assert.Single(workspace.Snapshot(Now.AddDays(10)).Items);
        Assert.Equal("notice", notice.Kind);
        Assert.Equal(Text, Assert.Single(notice.Evidence).Text);
        Assert.NotNull(Messages().Find(Message().Key));
        Assert.Empty(Tasks().ListTasks());
    }

    [Fact]
    public void AcceptedNotice_CanConvertToTaskWithoutRemovingNoticeOriginalOrReviewHistory()
    {
        var workspace = Workspace();
        var source = Message() with { Segments = [new(SegmentKind.Text, Text), new(SegmentKind.Image, "image-id")] };
        var review = Ingest(workspace, source);
        workspace.Execute(Accept(review), Now);
        var notice = Assert.Single(workspace.Snapshot(Now).Items);
        var accepted = JsonSerializer.Serialize(Tasks().FindReview(review.Id));
        var convert = new WorkspaceCommand(Account, "convert-notice", notice.Id, "convert", notice.Version,
            Date: "2026-10-20", Time: "13:00", TimeKind: "start");
        workspace.Execute(convert, Now);
        Workspace().Execute(convert, Now.AddMinutes(1));
        var snapshot = Workspace().Snapshot(Now);
        var task = Assert.Single(snapshot.Items, item => item.Kind == "task");
        var savedNotice = Assert.Single(snapshot.Items, item => item.Kind == "notice");
        Assert.Equal("converted", savedNotice.State);
        Assert.Equal(task.Id, savedNotice.TargetTaskId);
        Assert.Equal(Text, Assert.Single(task.Evidence).Text);
        Assert.Equal(1, Assert.Single(task.Evidence).ImageCount);
        Assert.Equal("2026-10-20", task.Due);
        Assert.Equal("13:00", task.Time);
        Assert.Equal(accepted, JsonSerializer.Serialize(Tasks().FindReview(review.Id)));
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
    private static long Count(string path, string table)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
