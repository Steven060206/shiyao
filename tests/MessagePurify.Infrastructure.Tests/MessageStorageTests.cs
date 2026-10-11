using MessagePurify.Core;
using MessagePurify.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class MessageStorageTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string OtherAccount = "9007199254740994";
    private const string Group = "9007199254740995";
    private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "MessagePurify-storage-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Received = new(2026, 10, 11, 23, 10, 11, TimeSpan.FromHours(8));

    [Fact]
    public void Enqueue_ReopeningPreservesLargeIdsOriginalTimeSegmentsAndOrigin()
    {
        var store = Store();
        var sent = new DateTimeOffset(2026, 10, 10, 20, 30, 40, TimeSpan.FromHours(8)).AddTicks(1234);
        var message = Message("-9007199254740997") with
        {
            SentAt = sent,
            Segments = [
                new(SegmentKind.Text, "明天交作业\n保留原文"),
                new(SegmentKind.Image, "OCR 尚未执行", AssetId: "local/学校通知.png"),
                new(SegmentKind.Quote, "引用", new(Account, Group, "0009007199254740998"))]
        };

        Assert.True(store.TryEnqueue(message, MessageOrigin.History, "history-1", Received));

        var reopened = Store();
        var saved = Assert.IsType<StoredMessage>(reopened.Find(message.Key));
        Assert.Equal(Account, reopened.AccountId);
        Assert.Equal(Path.Combine(dataRoot, "accounts", Account, "messages.sqlite3"), reopened.DatabasePath);
        Assert.True(File.Exists(reopened.DatabasePath));
        Assert.Equal(message.Key, saved.Message.Key);
        Assert.Equal(message.SenderId, saved.Message.SenderId);
        Assert.Equal(sent, saved.Message.SentAt);
        Assert.Equal(sent.Offset, saved.Message.SentAt.Offset);
        Assert.Equal(message.Segments, saved.Message.Segments);
        Assert.False(saved.Message.IsPrivate);
        Assert.Equal(MessageOrigin.History, saved.Origin);
        Assert.Equal("history-1", saved.BatchId);
        Assert.Equal(Received, saved.ReceivedAt);
        Assert.Equal(Received.Offset, saved.ReceivedAt.Offset);
        Assert.Equal(0, saved.ProcessingVersion);
        Assert.Null(saved.Extraction);
        Assert.Null(saved.ProcessedAt);
        Assert.Single(reopened.GetPending());
        Assert.Equal(1L, Scalar(reopened.DatabasePath, "PRAGMA user_version;"));
    }

    [Theory]
    [InlineData(MessageOrigin.Realtime, MessageOrigin.History)]
    [InlineData(MessageOrigin.History, MessageOrigin.Realtime)]
    public void Enqueue_RealtimeHistoryOverlapKeepsOriginalMessageAndSavedResult(MessageOrigin firstOrigin, MessageOrigin duplicateOrigin)
    {
        var store = Store();
        var message = Message();
        Assert.True(store.TryEnqueue(message, firstOrigin, "first-batch", Received));
        var result = Result(Candidate(message.Key));
        Assert.True(store.TrySaveExtraction(message.Key, 0, result, Received.AddMinutes(1)));

        var changed = message with { Segments = [new(SegmentKind.Text, "不应该覆盖的重读正文")] };
        Assert.False(Store().TryEnqueue(changed, duplicateOrigin, "duplicate-batch", Received.AddDays(1)));

        var saved = Assert.IsType<StoredMessage>(Store().Find(message.Key));
        Assert.Equal(message.Segments, saved.Message.Segments);
        Assert.Equal(firstOrigin, saved.Origin);
        Assert.Equal("first-batch", saved.BatchId);
        Assert.Equal(Received, saved.ReceivedAt);
        Assert.Equal(1, saved.ProcessingVersion);
        Assert.Equal(Received.AddMinutes(1), saved.ProcessedAt);
        AssertExtraction(result, saved.Extraction);
        Assert.Empty(Store().GetPending());
    }

    [Fact]
    public void Accounts_UseSeparateDatabasesAndDoNotLeakSameMessageKeyParts()
    {
        var first = Store();
        var second = Store(OtherAccount);
        var firstMessage = Message();
        var secondMessage = Message(account: OtherAccount) with { Segments = [new(SegmentKind.Text, "另一个账号的正文")] };

        Assert.True(first.TryEnqueue(firstMessage, MessageOrigin.Realtime, "live", Received));
        Assert.True(second.TryEnqueue(secondMessage, MessageOrigin.History, "history", Received));
        Assert.NotEqual(first.DatabasePath, second.DatabasePath);
        Assert.Equal(firstMessage.Segments, Assert.IsType<StoredMessage>(Store().Find(firstMessage.Key)).Message.Segments);
        Assert.Equal(secondMessage.Segments, Assert.IsType<StoredMessage>(Store(OtherAccount).Find(secondMessage.Key)).Message.Segments);
        Assert.Single(first.GetPending());
        Assert.Single(second.GetPending());
    }

    [Fact]
    public void Enqueue_RejectsPrivateAndWrongAccountWithoutWritingAnything()
    {
        var store = Store();
        Assert.Throws<ArgumentException>(() => store.TryEnqueue(Message() with { IsPrivate = true }, MessageOrigin.Realtime, "live", Received));
        Assert.Throws<ArgumentException>(() => store.TryEnqueue(Message(account: OtherAccount), MessageOrigin.Realtime, "live", Received));
        Assert.Empty(store.GetPending());
        Assert.Null(store.Find(Message().Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../123")]
    [InlineData("123/456")]
    [InlineData("１２３")]
    [InlineData("١٢٣")]
    [InlineData(" 123")]
    [InlineData("+123")]
    public void Constructor_RejectsNonAsciiAccountIds(string account)
    {
        Assert.Throws<ArgumentException>(() => Store(account));
        Assert.False(Directory.Exists(dataRoot));
    }

    [Fact]
    public void Enqueue_MessageIdUniquenessIncludesGroup()
    {
        var store = Store();
        var first = Message();
        var second = Message(group: "9007199254740996");
        Assert.True(store.TryEnqueue(first, MessageOrigin.Realtime, "live", Received));
        Assert.True(store.TryEnqueue(second, MessageOrigin.Realtime, "live", Received));
        Assert.Equal(2, store.GetPending().Count);
        Assert.NotNull(store.Find(first.Key));
        Assert.NotNull(store.Find(second.Key));
    }

    [Fact]
    public void GetPending_ExcludesProcessedRowsAndHonorsGroupAndLimit()
    {
        var store = Store();
        var processed = Message("processed");
        var pending = Message("pending");
        var otherGroup = Message("elsewhere", group: "9007199254740996");
        foreach (var message in new[] { processed, pending, otherGroup })
            Assert.True(store.TryEnqueue(message, MessageOrigin.History, "batch", Received));
        Assert.True(store.TrySaveExtraction(processed.Key, 0, Result(), Received.AddSeconds(1)));

        Assert.Equal(2, store.GetPending().Count);
        Assert.Equal(pending.Key, Assert.Single(store.GetPending(groupId: Group)).Message.Key);
        Assert.Single(store.GetPending(limit: 1));
        Assert.Empty(store.GetPending(groupId: "missing-group"));
        Assert.Null(store.Find(new(Account, Group, "missing-message")));
    }

    [Fact]
    public void GetPending_UsesOriginalSendingInstantWhenArrivalOrderAndOffsetsDiffer()
    {
        var store = Store();
        var late = Message("late") with { SentAt = Received.AddMinutes(1).ToOffset(TimeSpan.FromHours(-7)) };
        var sameInstantFirst = Message("same-first") with { SentAt = Received };
        var early = Message("early") with { SentAt = Received.AddMinutes(-1).ToOffset(TimeSpan.FromHours(12)) };
        var sameInstantSecond = Message("same-second") with { SentAt = Received.ToOffset(TimeSpan.FromHours(-5)) };
        foreach (var message in new[] { late, sameInstantFirst, early, sameInstantSecond })
            Assert.True(store.TryEnqueue(message, MessageOrigin.History, "batch", Received));

        var pending = Store().GetPending();

        Assert.Equal(new[] { early.Key, sameInstantFirst.Key, sameInstantSecond.Key, late.Key }, pending.Select(row => row.Message.Key));
        Assert.Equal(new[] { early.Key, sameInstantFirst.Key }, store.GetPending(limit: 2).Select(row => row.Message.Key));
        Assert.Equal(sameInstantSecond.SentAt.Offset, pending[2].Message.SentAt.Offset);
    }

    [Fact]
    public void SaveExtraction_ReopeningPreservesMultipleCandidatesEvidenceAndTimePrecision()
    {
        var store = Store();
        var message = Message();
        store.TryEnqueue(message, MessageOrigin.History, "history", Received);
        var dateOnly = new TaskTime(TimeKind.Deadline, new(2026, 10, 16), null, "Asia/Shanghai");
        var exact = new TaskTime(TimeKind.Start, new(2026, 10, 17), new(14, 30, 15), "Asia/Shanghai");
        var quotedEvidence = new MessageKey(Account, Group, "0009007199254740998");
        var result = Result(
            Candidate(message.Key) with { Time = dateOnly, Evidence = [message.Key, quotedEvidence] },
            Candidate(message.Key) with { Title = "图片待确认", Disposition = RuleDisposition.Pending, Time = null, SuggestedTime = exact,
                Priority = null, TargetTaskId = "existing-task", ReviewReasons = ["请查看原图", "时间角色不明"], Evidence = [quotedEvidence, message.Key] });
        var processedAt = Received.AddSeconds(2).AddTicks(5678);

        Assert.True(store.TrySaveExtraction(message.Key, 0, result, processedAt));

        var saved = Assert.IsType<StoredMessage>(Store().Find(message.Key));
        Assert.Equal(1, saved.ProcessingVersion);
        Assert.Equal(processedAt, saved.ProcessedAt);
        Assert.Equal(processedAt.Offset, saved.ProcessedAt!.Value.Offset);
        AssertExtraction(result, saved.Extraction);
        Assert.Null(saved.Extraction!.Candidates[0].Time!.Time);
        Assert.Null(saved.Extraction.Candidates[0].Time!.GetExactInstant());
        Assert.Null(saved.Extraction.Candidates[1].Time);
        Assert.Equal(exact, saved.Extraction.Candidates[1].SuggestedTime);
        Assert.Empty(Store().GetPending());
    }

    [Fact]
    public void SaveExtraction_TwoStoresRejectStaleVersionWithoutReplacingResult()
    {
        var first = Store();
        var second = Store();
        var message = Message();
        first.TryEnqueue(message, MessageOrigin.Realtime, "live", Received);
        var firstRead = Assert.IsType<StoredMessage>(first.Find(message.Key));
        var secondRead = Assert.IsType<StoredMessage>(second.Find(message.Key));
        var original = Result(Candidate(message.Key));
        var replacement = Result(Candidate(message.Key) with { Title = "重新分析" });

        Assert.True(first.TrySaveExtraction(message.Key, firstRead.ProcessingVersion, original, Received.AddSeconds(1)));
        Assert.False(second.TrySaveExtraction(message.Key, secondRead.ProcessingVersion, replacement, Received.AddSeconds(2)));
        var saved = Assert.IsType<StoredMessage>(second.Find(message.Key));
        Assert.Equal(1, saved.ProcessingVersion);
        Assert.Equal(Received.AddSeconds(1), saved.ProcessedAt);
        AssertExtraction(original, saved.Extraction);
        Assert.True(second.TrySaveExtraction(message.Key, saved.ProcessingVersion, replacement, Received.AddSeconds(3)));
        Assert.Equal(2, Assert.IsType<StoredMessage>(first.Find(message.Key)).ProcessingVersion);
        AssertExtraction(replacement, Assert.IsType<StoredMessage>(first.Find(message.Key)).Extraction);
    }

    [Fact]
    public void SaveExtraction_RejectsForeignAccountEvidenceAndLeavesMessagePending()
    {
        var store = Store();
        var message = Message();
        store.TryEnqueue(message, MessageOrigin.Realtime, "live", Received);
        var foreign = Candidate(message.Key) with { Evidence = [message.Key, new(OtherAccount, Group, "foreign")] };

        Assert.Throws<ArgumentException>(() => store.TrySaveExtraction(message.Key, 0, Result(Candidate(message.Key), foreign), Received));

        var saved = Assert.Single(store.GetPending());
        Assert.Equal(0, saved.ProcessingVersion);
        Assert.Null(saved.Extraction);
        Assert.Null(saved.ProcessedAt);
    }

    [Fact]
    public void SaveExtraction_DatabaseFailureRollsBackAndCanBeRetried()
    {
        var store = Store();
        var message = Message();
        store.TryEnqueue(message, MessageOrigin.History, "batch", Received);
        var result = Result(Candidate(message.Key), Candidate(message.Key) with { Title = "第二个候选" });
        var table = InboxTable(store.DatabasePath);
        Execute(store.DatabasePath, $"CREATE TRIGGER fail_extraction BEFORE UPDATE ON \"{table}\" BEGIN SELECT RAISE(ABORT, 'test storage failure'); END;");

        Assert.Throws<SqliteException>(() => store.TrySaveExtraction(message.Key, 0, result, Received.AddMinutes(1)));

        var saved = Assert.Single(Store().GetPending());
        Assert.Equal(0, saved.ProcessingVersion);
        Assert.Null(saved.Extraction);
        Assert.Null(saved.ProcessedAt);
        Execute(store.DatabasePath, "DROP TRIGGER fail_extraction;");
        Assert.True(Store().TrySaveExtraction(message.Key, 0, result, Received.AddMinutes(2)));
        AssertExtraction(result, Assert.IsType<StoredMessage>(Store().Find(message.Key)).Extraction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(77)]
    public void Constructor_UnknownSchemaVersionDoesNotReplaceDatabase(int version)
    {
        var path = DatabasePath(Account);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Execute(path, $"PRAGMA user_version = {version}; CREATE TABLE sentinel (value TEXT); INSERT INTO sentinel VALUES ('preserve me');");

        Assert.Throws<InvalidDataException>(() => Store());

        Assert.Equal((long)version, Scalar(path, "PRAGMA user_version;"));
        Assert.Equal("preserve me", Scalar(path, "SELECT value FROM sentinel;"));
    }

    [Fact]
    public void Constructor_CorruptDatabaseDoesNotOverwriteOriginalBytes()
    {
        var path = DatabasePath(Account);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = "This is not a SQLite database."u8.ToArray();
        File.WriteAllBytes(path, original);

        Assert.Throws<SqliteException>(() => Store());

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void Constructor_DatabaseOwnerMismatchIsRejectedWithoutDiscardingData()
    {
        var first = Store();
        var message = Message();
        first.TryEnqueue(message, MessageOrigin.History, "batch", Received);
        Execute(first.DatabasePath, "PRAGMA wal_checkpoint(TRUNCATE);");
        var copiedPath = DatabasePath(OtherAccount);
        Directory.CreateDirectory(Path.GetDirectoryName(copiedPath)!);
        File.Copy(first.DatabasePath, copiedPath);

        Assert.Throws<InvalidDataException>(() => Store(OtherAccount));

        Assert.Equal(1L, Scalar(copiedPath, "PRAGMA user_version;"));
        var table = InboxTable(copiedPath);
        Assert.Equal(1L, Scalar(copiedPath, $"SELECT COUNT(*) FROM \"{table}\";"));
        Assert.NotNull(Store().Find(message.Key));
    }

    private AccountMessageStore Store(string account = Account) => new(dataRoot, account);
    private string DatabasePath(string account) => Path.Combine(dataRoot, "accounts", account, "messages.sqlite3");

    private static SchoolMessage Message(string id = "9007199254740997", string account = Account, string group = Group) =>
        new(new(account, group, id), "9007199254740999", Received.AddHours(-1), [new(SegmentKind.Text, "周五交实验报告")]);

    private static RuleCandidate Candidate(MessageKey evidence) => new("提交实验报告", RuleAction.Submit, RuleDisposition.Task,
        "高等数学", new(TimeKind.Deadline, new(2026, 10, 16), null, "Asia/Shanghai"), null, null, 2,
        new(new("需要提交", true), new("全体同学", true), new("课程已确认", true), new("只有日期", true),
            new("可信发送者", true), new("首次发现", false)), ["关联需核对"], [evidence]);

    private static RuleExtractionResult Result(params RuleCandidate[] candidates) => new("local-rules-test-v1", candidates);

    private static void AssertExtraction(RuleExtractionResult expected, RuleExtractionResult? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.RuleVersion, actual.RuleVersion);
        Assert.Equal(expected.Candidates.Count, actual.Candidates.Count);
        for (var i = 0; i < expected.Candidates.Count; i++)
        {
            var left = expected.Candidates[i];
            var right = actual.Candidates[i];
            Assert.Equal(left.Title, right.Title);
            Assert.Equal(left.Action, right.Action);
            Assert.Equal(left.Disposition, right.Disposition);
            Assert.Equal(left.Course, right.Course);
            Assert.Equal(left.Time, right.Time);
            Assert.Equal(left.SuggestedTime, right.SuggestedTime);
            Assert.Equal(left.TargetTaskId, right.TargetTaskId);
            Assert.Equal(left.Priority, right.Priority);
            Assert.Equal(left.Assessments, right.Assessments);
            Assert.Equal(left.ReviewReasons, right.ReviewReasons);
            Assert.Equal(left.Evidence, right.Evidence);
        }
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

    private static string InboxTable(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND sql LIKE '%group_id%' AND sql LIKE '%message_id%';";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read(), "The message inbox table must exist.");
        var table = reader.GetString(0);
        Assert.False(reader.Read(), "Only the message inbox is expected in this storage stage.");
        return table.Replace("\"", "\"\"");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dataRoot))
            Directory.Delete(dataRoot, true);
    }
}
