using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class CacheCleanupTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string OtherAccount = "9007199254740994";
    private const string Group = "9007199254740995";
    private const string OtherGroup = "9007199254740996";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(8));
    private static readonly TaskTime Time = new(TimeKind.Deadline, new(2026, 10, 20), new(18, 0), "Asia/Shanghai");
    private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "MessagePurify-cache-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Cleanup_UsesStrictOriginalSentTimeBoundaryAndRetainsReceipts()
    {
        var cutoff = Now.AddDays(-7);
        var expired = Route("expired", sentAt: cutoff.AddTicks(-1));
        var boundary = Route("boundary", sentAt: cutoff);
        var inside = Route("inside", sentAt: cutoff.AddTicks(1));

        var result = Router().CleanMessageCache(Now);

        Assert.Equal(new CacheCleanupResult(Account, Now, cutoff, 1, 0, 2), result);
        Assert.Null(Messages().Find(expired.Key));
        Assert.NotNull(Messages().Find(boundary.Key));
        Assert.NotNull(Messages().Find(inside.Key));
        Assert.True(Router().HasRouted(expired.Key));
        Assert.Equal(3, Count(RoutingPath(), "route_receipts"));
        Assert.Equal(result, Router().ReadCacheCleanupResult());
    }

    [Theory]
    [InlineData(TaskState.Active)]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.Archived)]
    public void Cleanup_RetainsEveryTaskLifecycleAndOperation(TaskState state)
    {
        var source = Route("task", RuleDisposition.Task);
        var tasks = Tasks();
        var task = Assert.Single(tasks.ListTasks());
        if (state == TaskState.Completed) tasks.Complete(task.Id, "complete", task.Version, Now);
        if (state == TaskState.Cancelled) tasks.Cancel(task.Id, "cancel", task.Version, Now);
        if (state == TaskState.Archived) tasks.Archive(task.Id, "archive", task.Version, Now);
        var history = Count(tasks.DatabasePath, "operations");
        Route("temporary");

        var result = Router().CleanMessageCache(Now.AddDays(1));

        Assert.Equal(1, result.DeletedMessages);
        Assert.Equal(1, result.RetainedOldMessages);
        Assert.NotNull(Messages().Find(source.Key));
        Assert.Equal(state, Assert.Single(Tasks().ListTasks()).State);
        Assert.Equal(history, Count(tasks.DatabasePath, "operations"));
        Assert.Equal(1, Count(tasks.DatabasePath, "task_evidence"));
    }

    [Theory]
    [InlineData(ReviewState.Pending)]
    [InlineData(ReviewState.Ignored)]
    [InlineData(ReviewState.Accepted)]
    [InlineData(ReviewState.Rejected)]
    public void Cleanup_RetainsEveryReviewStateAfterUndoExpiry(ReviewState state)
    {
        var source = Route("review", RuleDisposition.Pending);
        var tasks = Tasks();
        var review = Assert.Single(tasks.ListReviews());
        if (state == ReviewState.Ignored) tasks.IgnoreReview(review.Id, "ignore", review.Version, Now);
        if (state == ReviewState.Rejected) tasks.RejectReview(review.Id, "reject", review.Version, Now);
        if (state == ReviewState.Accepted) tasks.AcceptReview(review.Id, "accepted", "accept", review.Version, Time, Now);
        Route("temporary");

        var result = Router().CleanMessageCache(Now.AddDays(2));

        Assert.Equal(1, result.DeletedMessages);
        Assert.Equal(1, result.RetainedOldMessages);
        Assert.NotNull(Messages().Find(source.Key));
        Assert.Equal(state, Assert.Single(Tasks().ListReviews()).State);
    }

    [Fact]
    public void Cleanup_RetainsNoticeSourceAndEvidenceSeparately()
    {
        var evidence = Route("evidence", group: OtherGroup);
        var source = Route("notice", RuleDisposition.Notice, evidence: [evidence.Key]);
        var notice = Assert.Single(Router().ListNotices());
        // Source is an independent persisted link, even if a stored candidate lacks it.
        var changed = notice with { Candidate = notice.Candidate with { Evidence = [evidence.Key] } };
        Execute(RoutingPath(), "UPDATE notices SET notice_json=$json WHERE id=$id;",
            ("$json", JsonSerializer.Serialize(changed)), ("$id", notice.Id));
        Route("temporary");

        var result = Router().CleanMessageCache(Now);

        Assert.Equal(1, result.DeletedMessages);
        Assert.Equal(2, result.RetainedOldMessages);
        Assert.NotNull(Messages().Find(source.Key));
        Assert.NotNull(Messages().Find(evidence.Key));
        Assert.Equal(JsonSerializer.Serialize(changed), JsonSerializer.Serialize(Assert.Single(Router().ListNotices())));
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("separate")]
    [InlineData("split-task")]
    [InlineData("split-review")]
    public void Cleanup_RetainsMergeAliasesSeparateDecisionsAndSplitFamilies(string operation)
    {
        var firstSource = Route("first", RuleDisposition.Task);
        var secondSource = Route("second", RuleDisposition.Task);
        var tasks = Tasks();
        var items = tasks.ListTasks();
        var first = items[0];
        var second = items[1];
        if (operation == "merge")
            tasks.MergeTasks(first.Id, second.Id, "merge", first.Version, second.Version,
                new(first.Id, first.Id, first.Id, first.Id, first.Id), Now);
        if (operation == "separate") tasks.KeepSeparate(first.Id, second.Id, "separate", first.Version, second.Version, Now);
        if (operation.StartsWith("split", StringComparison.Ordinal))
            tasks.SplitTask(first.Id, "child", "split", first.Version,
                new("拆出任务", "数学", operation == "split-task" ? Time : null, 1, TaskState.Active, first.Evidence), Now);
        var savedTasks = tasks.ListTasks(includeMerged: true).Select(item => JsonSerializer.Serialize(item)).ToArray();
        var savedReviews = tasks.ListReviews().Select(item => JsonSerializer.Serialize(item)).ToArray();
        var history = Count(tasks.DatabasePath, "operations");
        Route("temporary");

        var result = Router().CleanMessageCache(Now);

        Assert.Equal(1, result.DeletedMessages);
        Assert.NotNull(Messages().Find(firstSource.Key));
        Assert.NotNull(Messages().Find(secondSource.Key));
        Assert.Equal(savedTasks, Tasks().ListTasks(includeMerged: true).Select(item => JsonSerializer.Serialize(item)).ToArray());
        Assert.Equal(savedReviews, Tasks().ListReviews().Select(item => JsonSerializer.Serialize(item)).ToArray());
        Assert.Equal(history, Count(tasks.DatabasePath, "operations"));
    }

    [Fact]
    public void Cleanup_FollowsQuoteChainsAndCyclesFromRetainedWindowMessages()
    {
        var firstKey = new MessageKey(Account, Group, "first");
        var second = Route("second", quotes: [firstKey]);
        var first = Route("first", quotes: [second.Key]);
        var head = Route("head", sentAt: Now.AddDays(-1), quotes: [first.Key]);
        Route("temporary");

        var result = Router().CleanMessageCache(Now);

        Assert.Equal(1, result.DeletedMessages);
        Assert.Equal(2, result.RetainedOldMessages);
        Assert.Equal(3, result.RemainingMessages);
        foreach (var key in new[] { first.Key, second.Key, head.Key }) Assert.NotNull(Messages().Find(key));
    }

    [Fact]
    public void Cleanup_FollowsExtractionEvidenceAcrossGroupsUsingCompleteKeys()
    {
        var tail = Route("same-id", group: OtherGroup);
        var middle = Route("middle", evidence: [tail.Key]);
        var head = Route("head", sentAt: Now.AddDays(-1), evidence: [middle.Key]);
        var unrelated = Route("same-id");

        var result = Router().CleanMessageCache(Now);

        Assert.Equal(1, result.DeletedMessages);
        Assert.Null(Messages().Find(unrelated.Key));
        foreach (var key in new[] { tail.Key, middle.Key, head.Key }) Assert.NotNull(Messages().Find(key));
    }

    [Fact]
    public void Cleanup_RetainsUnextractedUnroutedAndChangedProcessingVersionWithReferences()
    {
        var quoteSource = Route("quote-source");
        var evidenceSource = Route("evidence-source");
        var revised = Route("revised");
        Route("temporary");
        var raw = Enqueue("raw", quotes: [quoteSource.Key]);
        var unrouted = SaveExtraction("unrouted", evidence: [evidenceSource.Key]);
        var revisedStore = Messages().Find(revised.Key)!;
        Assert.True(Messages().TrySaveExtraction(revised.Key, revisedStore.ProcessingVersion,
            revisedStore.Extraction!, Now));

        var result = Router().CleanMessageCache(Now);

        Assert.Equal(1, result.DeletedMessages);
        Assert.Equal(5, result.RetainedOldMessages);
        foreach (var key in new[] { quoteSource.Key, evidenceSource.Key, raw.Key, unrouted.Key, revised.Key })
            Assert.NotNull(Messages().Find(key));
        Assert.Null(Messages().Find(raw.Key)!.Extraction);
        Assert.False(Router().HasRouted(unrouted.Key));
        Assert.Equal(2, Messages().Find(revised.Key)!.ProcessingVersion);
    }

    [Fact]
    public void Cleanup_IsAccountIsolatedAndRejectsForeignReceiptLookup()
    {
        var own = Route("same-id");
        var other = Route("same-id", account: OtherAccount);

        Assert.Equal(1, Router().CleanMessageCache(Now).DeletedMessages);

        Assert.Null(Messages().Find(own.Key));
        Assert.NotNull(Messages(OtherAccount).Find(other.Key));
        Assert.Null(Router(OtherAccount).ReadCacheCleanupResult());
        Assert.True(Router(OtherAccount).HasRouted(other.Key));
        Assert.Throws<ArgumentException>(() => Router().HasRouted(other.Key));
    }

    [Fact]
    public void HasRouted_UsesAccountGroupAndMessageAndSurvivesCleanup()
    {
        var source = Route("same-id");
        var router = Router();
        Assert.True(router.HasRouted(source.Key));
        Assert.False(router.HasRouted(source.Key with { GroupId = OtherGroup }));
        Assert.False(router.HasRouted(source.Key with { MessageId = "missing" }));
        Assert.Throws<ArgumentException>(() => router.HasRouted(source.Key with { MessageId = "" }));

        router.CleanMessageCache(Now);

        Assert.Null(Messages().Find(source.Key));
        Assert.True(Router().HasRouted(source.Key));
    }

    [Fact]
    public void Cleanup_ResultReopensAndSuccessfulEmptyRunReplacesIt()
    {
        Assert.Null(Router().ReadCacheCleanupResult());
        Route("temporary");
        var previous = Router().CleanMessageCache(Now);

        Assert.Equal(previous, Router().ReadCacheCleanupResult());
        var latest = Router().CleanMessageCache(Now.AddHours(1));

        Assert.Equal(new CacheCleanupResult(Account, Now.AddHours(1), Now.AddHours(1).AddDays(-7), 0, 0, 0), latest);
        Assert.Equal(latest, Router().ReadCacheCleanupResult());
    }

    [Fact]
    public void Cleanup_DeleteFailureRollsBackEarlierDeletesAndPreservesPreviousResult()
    {
        var previous = SavePreviousResult();
        var first = Route("first");
        var second = Route("second");
        Execute(Messages().DatabasePath, """
            CREATE TRIGGER fail_delete BEFORE DELETE ON messages WHEN OLD.message_id='second'
            BEGIN SELECT RAISE(ABORT, 'injected deletion failure'); END;
            """);

        Assert.Throws<SqliteException>(() => Router().CleanMessageCache(Now.AddMinutes(1)));

        Assert.NotNull(Messages().Find(first.Key));
        Assert.NotNull(Messages().Find(second.Key));
        Assert.Equal(previous, Router().ReadCacheCleanupResult());
        Assert.Equal(3, Count(RoutingPath(), "route_receipts"));
    }

    [Fact]
    public void Cleanup_ResultWriteFailureRollsBackDeletesAndPreservesPreviousResult()
    {
        var previous = SavePreviousResult();
        var source = Route("temporary");
        Execute(RoutingPath(), """
            CREATE TRIGGER fail_result BEFORE UPDATE ON cache_cleanup_result
            BEGIN SELECT RAISE(ABORT, 'injected result failure'); END;
            """);

        Assert.Throws<SqliteException>(() => Router().CleanMessageCache(Now.AddMinutes(1)));

        Assert.NotNull(Messages().Find(source.Key));
        Assert.Equal(previous, Router().ReadCacheCleanupResult());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void Cleanup_PauseBeforeDuringOrAfterDeletionRollsBackAndKeepsResult(int pauseAt)
    {
        var previous = SavePreviousResult();
        var first = Route("first");
        var second = Route("second");
        var checks = 0;

        Assert.Throws<OperationCanceledException>(() => Router().CleanMessageCache(Now.AddMinutes(1),
            () => ++checks < pauseAt));

        Assert.NotNull(Messages().Find(first.Key));
        Assert.NotNull(Messages().Find(second.Key));
        Assert.Equal(previous, Router().ReadCacheCleanupResult());
        Assert.True(Router().HasRouted(first.Key));
        Assert.True(Router().HasRouted(second.Key));
    }

    private CacheCleanupResult SavePreviousResult()
    {
        Route("previous");
        return Router().CleanMessageCache(Now);
    }

    private AccountMessageStore Messages(string account = Account) => new(dataRoot, account);
    private AccountTaskStore Tasks(string account = Account) => new(dataRoot, account);
    private AccountCandidateRouter Router(string account = Account) => new(Messages(account), Tasks(account));
    private string RoutingPath() => Path.Combine(dataRoot, "accounts", Account, "routing.sqlite3");

    private SchoolMessage Enqueue(string id, DateTimeOffset? sentAt = null, string group = Group,
        string account = Account, MessageKey[]? quotes = null)
    {
        var segments = new List<MessageSegment> { new(SegmentKind.Text, "合成消息") };
        segments.AddRange((quotes ?? []).Select(key => new MessageSegment(SegmentKind.Quote, "引用", key)));
        var message = new SchoolMessage(new(account, group, id), "12345", sentAt ?? Now.AddDays(-8), segments);
        Assert.True(Messages(account).TryEnqueue(message, MessageOrigin.History, "synthetic", Now));
        return message;
    }

    private SchoolMessage SaveExtraction(string id, RuleDisposition disposition = RuleDisposition.Ignored,
        DateTimeOffset? sentAt = null, string group = Group, string account = Account,
        MessageKey[]? quotes = null, MessageKey[]? evidence = null)
    {
        var message = Enqueue(id, sentAt, group, account, quotes);
        var reliable = new RuleAssessment("合成可靠信息", true);
        var candidate = new RuleCandidate("材料" + id,
            disposition == RuleDisposition.Notice ? RuleAction.Inform : RuleAction.Complete,
            disposition, "数学", Time, null, null, 1,
            new(reliable, reliable, reliable, reliable, reliable, reliable), [],
            new[] { message.Key }.Concat(evidence ?? []).Distinct().ToArray());
        Assert.True(Messages(account).TrySaveExtraction(message.Key, 0,
            new(MessageRuleExtractor.RuleVersion, [candidate]), Now));
        return message;
    }

    private SchoolMessage Route(string id, RuleDisposition disposition = RuleDisposition.Ignored,
        DateTimeOffset? sentAt = null, string group = Group, string account = Account,
        MessageKey[]? quotes = null, MessageKey[]? evidence = null)
    {
        var message = SaveExtraction(id, disposition, sentAt, group, account, quotes, evidence);
        Assert.Equal(1, Router(account).RoutePending(new(account, group, "Asia/Shanghai", Now, "数学"),
            new HashSet<string> { group }));
        return message;
    }

    private static long Count(string path, string table)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Execute(string path, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
    }
}
