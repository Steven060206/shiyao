using MessagePurify.Core;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Storage;

namespace MessagePurify.Infrastructure.Tests;

public sealed class LocalMessageProcessorTests : IDisposable
{
    private const string AccountId = "10001";
    private const string GroupId = "20001";
    private const string SenderId = "30001";
    private static readonly DateTimeOffset SentAt = new(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Now = SentAt.AddHours(1);
    private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "MessagePurify.ProcessorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Ingest_PersistsEligibleMessageBeforeExtraction()
    {
        var store = CreateStore();
        var message = Message();
        var processor = new LocalMessageProcessor(store);

        Assert.True(processor.Ingest(message, Pinned(), Context(), MessageOrigin.Realtime, "live-1", Now));

        var pending = Assert.Single(store.GetPending());
        Assert.Equal(message.Key, pending.Message.Key);
        Assert.Equal(MessageOrigin.Realtime, pending.Origin);
        Assert.Equal("live-1", pending.BatchId);
        Assert.Equal(Now, pending.ReceivedAt);
        Assert.Equal(0L, pending.ProcessingVersion);
        Assert.Null(pending.Extraction);
        Assert.Null(pending.ProcessedAt);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("unpinned")]
    [InlineData("excluded-sender")]
    public void Ingest_DoesNotPersistMessagesOutsideConfirmedScope(string excludedBy)
    {
        var store = CreateStore();
        var message = Message() with { IsPrivate = excludedBy == "private" };
        var pinned = excludedBy == "unpinned" ? new HashSet<string>() : Pinned();
        var context = excludedBy == "excluded-sender"
            ? Context() with { ExcludedSenders = new HashSet<string> { SenderId } }
            : Context();

        Assert.False(new LocalMessageProcessor(store).Ingest(
            message, pinned, context, MessageOrigin.Realtime, "live-1", Now));

        Assert.Null(store.Find(message.Key));
        Assert.Empty(store.GetPending());
    }

    [Theory]
    [InlineData("message-account")]
    [InlineData("context-account")]
    [InlineData("message-and-context-account")]
    [InlineData("context-group")]
    public void Ingest_RejectsMismatchedAccountOrGroupBeforePersisting(string mismatch)
    {
        var store = CreateStore();
        var message = Message();
        var context = Context();
        if (mismatch is "message-account" or "message-and-context-account") message = message with { Key = message.Key with { AccountId = "10002" } };
        if (mismatch is "context-account" or "message-and-context-account") context = context with { AccountId = "10002" };
        if (mismatch == "context-group") context = context with { GroupId = "20002" };

        Assert.Throws<ArgumentException>(() => new LocalMessageProcessor(store).Ingest(
            message, Pinned(), context, MessageOrigin.History, "history-1", Now));

        Assert.Empty(store.GetPending());
    }

    [Fact]
    public void ProcessPending_RejectsContextForAnotherAccount()
    {
        var store = CreateStore();
        var message = Message();
        Assert.True(new LocalMessageProcessor(store).Ingest(
            message, Pinned(), Context(), MessageOrigin.History, "history-1", Now));

        Assert.Throws<ArgumentException>(() => new LocalMessageProcessor(store).ProcessPending(
            Context() with { AccountId = "10002" }));

        Assert.Null(store.Find(message.Key)!.Extraction);
        Assert.Single(store.GetPending());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ProcessPending_RejectsMissingGroupInsteadOfProcessingAcrossGroups(string? groupId)
    {
        var store = CreateStore();
        var message = Message();
        var processor = new LocalMessageProcessor(store);
        Assert.True(processor.Ingest(message, Pinned(), Context(), MessageOrigin.History, "history-1", Now));

        Assert.Throws<ArgumentException>(() => processor.ProcessPending(Context() with { GroupId = groupId! }));

        Assert.Null(store.Find(message.Key)!.Extraction);
        Assert.Single(store.GetPending());
    }

    [Fact]
    public void ProcessPending_AfterReopeningPersistsReliableCandidateAndEvidence()
    {
        var message = Message();
        Assert.True(new LocalMessageProcessor(CreateStore()).Ingest(
            message, Pinned(), Context(), MessageOrigin.History, "history-1", Now));

        var reopened = CreateStore();
        Assert.Equal(1, new LocalMessageProcessor(reopened).ProcessPending(Context()));

        var processed = CreateStore().Find(message.Key)!;
        var candidate = Assert.Single(processed.Extraction!.Candidates);
        Assert.Equal(RuleDisposition.Task, candidate.Disposition);
        Assert.Equal("数学", candidate.Course);
        Assert.Equal(new TaskTime(TimeKind.Deadline, new DateOnly(2026, 10, 12), new TimeOnly(18, 0), "Asia/Shanghai"), candidate.Time);
        Assert.Equal(message.Key, Assert.Single(candidate.Evidence));
        Assert.Empty(candidate.ReviewReasons);
        Assert.Equal(1L, processed.ProcessingVersion);
        Assert.Equal(Now, processed.ProcessedAt);
        Assert.Empty(reopened.GetPending());
    }

    [Fact]
    public void ProcessPending_WithoutConfirmedCourseRemainsPendingCandidate()
    {
        var store = CreateStore();
        var message = Message();
        var context = Context(course: null);
        var processor = new LocalMessageProcessor(store);
        Assert.True(processor.Ingest(message, Pinned(), context, MessageOrigin.Realtime, "live-1", Now));

        Assert.Equal(1, processor.ProcessPending(context));

        var candidate = Assert.Single(store.Find(message.Key)!.Extraction!.Candidates);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Null(candidate.Course);
        Assert.False(candidate.Assessments.Course.Reliable);
    }

    [Fact]
    public void ProcessPending_ImageDateStaysSuggestedWithoutFormalTime()
    {
        var store = CreateStore();
        var message = Message() with
        {
            Segments = [new MessageSegment(SegmentKind.Image, "10月12日18点提交作业", AssetId: "notice.png")]
        };
        var processor = new LocalMessageProcessor(store);
        Assert.True(processor.Ingest(message, Pinned(), Context(), MessageOrigin.History, "history-1", Now));

        Assert.Equal(1, processor.ProcessPending(Context()));

        var saved = CreateStore().Find(message.Key)!;
        var candidate = Assert.Single(saved.Extraction!.Candidates);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Null(candidate.Time);
        Assert.Equal(new TaskTime(TimeKind.Deadline, new DateOnly(2026, 10, 12), new TimeOnly(18, 0), "Asia/Shanghai"), candidate.SuggestedTime);
        Assert.Equal("notice.png", Assert.Single(saved.Message.Segments).AssetId);
        Assert.False(candidate.Assessments.Time.Reliable);
    }

    [Theory]
    [InlineData(MessageOrigin.Realtime, MessageOrigin.History)]
    [InlineData(MessageOrigin.History, MessageOrigin.Realtime)]
    public void ReplayAcrossHistoryAndRealtimeDoesNotProcessAgainOrReplaceEvidence(MessageOrigin first, MessageOrigin replay)
    {
        var store = CreateStore();
        var message = Message();
        var processor = new LocalMessageProcessor(store);
        Assert.True(processor.Ingest(message, Pinned(), Context(), first, "first", Now));
        Assert.False(processor.Ingest(message, Pinned(), Context(), replay, "replay-before", Now.AddSeconds(1)));
        Assert.Equal(1, processor.ProcessPending(Context()));

        var reopened = CreateStore();
        var afterRestart = new LocalMessageProcessor(reopened);
        var alteredReplay = message with { Segments = [new MessageSegment(SegmentKind.Text, "10月20日提交别的作业")] };
        Assert.False(afterRestart.Ingest(alteredReplay, Pinned(), Context(), replay, "replay-after", Now.AddSeconds(2)));
        Assert.Equal(0, afterRestart.ProcessPending(Context()));

        var saved = reopened.Find(message.Key)!;
        Assert.Equal(first, saved.Origin);
        Assert.Equal("first", saved.BatchId);
        Assert.Equal("10月12日18点提交作业", Assert.Single(saved.Message.Segments).Text);
        Assert.Equal(1L, saved.ProcessingVersion);
        Assert.Single(saved.Extraction!.Candidates);
    }

    [Fact]
    public void ProcessPending_UsesOnlyTheCurrentGroupsConfirmedContext()
    {
        var store = CreateStore();
        var first = Message();
        var other = Message(groupId: "20002");
        var processor = new LocalMessageProcessor(store);
        Assert.True(processor.Ingest(first, Pinned(), Context(), MessageOrigin.History, "history-1", Now));
        Assert.True(processor.Ingest(other, Pinned("20002"), Context("20002", "物理"), MessageOrigin.History, "history-1", Now));

        Assert.Equal(1, processor.ProcessPending(Context()));

        Assert.Equal("数学", Assert.Single(store.Find(first.Key)!.Extraction!.Candidates).Course);
        Assert.Null(store.Find(other.Key)!.Extraction);
        Assert.Equal(other.Key, Assert.Single(store.GetPending()).Message.Key);
        Assert.Equal(1, processor.ProcessPending(Context("20002", "物理")));
        Assert.Equal("物理", Assert.Single(store.Find(other.Key)!.Extraction!.Candidates).Course);
    }

    [Fact]
    public void ProcessPending_RespectsLimitAndContinuesOnNextCall()
    {
        var store = CreateStore();
        var processor = new LocalMessageProcessor(store);
        Assert.True(processor.Ingest(Message("1"), Pinned(), Context(), MessageOrigin.History, "history-1", Now));
        Assert.True(processor.Ingest(Message("2"), Pinned(), Context(), MessageOrigin.History, "history-1", Now.AddSeconds(1)));

        Assert.Equal(1, processor.ProcessPending(Context(), limit: 1));
        Assert.Single(store.GetPending());
        Assert.Equal(1, processor.ProcessPending(Context(), limit: 1));
        Assert.Equal(0, processor.ProcessPending(Context(), limit: 1));
    }

    [Fact]
    public void UnpinningStopsNewIngestionWithoutDiscardingAlreadyQueuedEvidence()
    {
        var store = CreateStore();
        var processor = new LocalMessageProcessor(store);
        var pinned = Pinned();
        var queued = Message("1");
        var later = Message("2");
        Assert.True(processor.Ingest(queued, pinned, Context(), MessageOrigin.Realtime, "live-1", Now));
        pinned.Clear();

        Assert.False(processor.Ingest(later, pinned, Context(), MessageOrigin.Realtime, "live-1", Now.AddSeconds(1)));
        Assert.Equal(1, processor.ProcessPending(Context()));

        Assert.NotNull(CreateStore().Find(queued.Key)!.Extraction);
        Assert.Null(store.Find(later.Key));
    }

    [Fact]
    public void ProcessPending_InvalidTimeZoneLeavesMessageForRetryAfterRestart()
    {
        var store = CreateStore();
        var message = Message();
        var processor = new LocalMessageProcessor(store);
        Assert.True(processor.Ingest(message, Pinned(), Context(), MessageOrigin.History, "history-1", Now));

        Assert.Throws<TimeZoneNotFoundException>(() => processor.ProcessPending(
            Context() with { TimeZoneId = "MessagePurify/InvalidTimeZone" }));

        var reopened = CreateStore();
        Assert.Equal(message.Key, Assert.Single(reopened.GetPending()).Message.Key);
        Assert.Null(reopened.Find(message.Key)!.Extraction);
        Assert.Equal(0L, reopened.Find(message.Key)!.ProcessingVersion);
        Assert.Equal(1, new LocalMessageProcessor(reopened).ProcessPending(Context()));
        Assert.Empty(reopened.GetPending());
    }

    [Fact]
    public void ProcessPending_ExtractorFailureKeepsUncommittedMessageAndEarlierSuccessfulResult()
    {
        var store = CreateStore();
        var first = Message("1");
        var reminder = Message("2") with { Segments = [new MessageSegment(SegmentKind.Text, "提醒提交统计作业")] };
        var invalidTime = new TaskTime(TimeKind.Deadline, new DateOnly(2026, 10, 12), null, "MessagePurify/InvalidTimeZone");
        var known = new KnownTask("task-1", "统计作业", "数学", invalidTime, TaskState.Active,
            [new MessageKey(AccountId, GroupId, "original")]);
        var context = Context() with { KnownTasks = [known] };
        var processor = new LocalMessageProcessor(store);
        Assert.True(processor.Ingest(first, Pinned(), context, MessageOrigin.History, "history-1", Now));
        Assert.True(processor.Ingest(reminder, Pinned(), context, MessageOrigin.History, "history-1", Now.AddSeconds(1)));

        Assert.Throws<TimeZoneNotFoundException>(() => processor.ProcessPending(context));

        var reopened = CreateStore();
        Assert.NotNull(reopened.Find(first.Key)!.Extraction);
        Assert.Null(reopened.Find(reminder.Key)!.Extraction);
        Assert.Equal(reminder.Key, Assert.Single(reopened.GetPending()).Message.Key);
        var corrected = context with { KnownTasks = [known with { Time = invalidTime with { TimeZoneId = "Asia/Shanghai" } }] };
        Assert.Equal(1, new LocalMessageProcessor(reopened).ProcessPending(corrected));
        Assert.Empty(reopened.GetPending());
    }

    private AccountMessageStore CreateStore() => new(dataRoot, AccountId);
    private static HashSet<string> Pinned(string groupId = GroupId) => [groupId];
    private static RuleContext Context(string groupId = GroupId, string? course = "数学") =>
        new(AccountId, groupId, "Asia/Shanghai", Now, course);
    private static SchoolMessage Message(string id = "1", string groupId = GroupId) =>
        new(new MessageKey(AccountId, groupId, id), SenderId, SentAt,
            [new MessageSegment(SegmentKind.Text, "10月12日18点提交作业")]);

    public void Dispose()
    {
        var ownedDirectory = Path.GetFullPath(dataRoot);
        var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MessagePurify.ProcessorTests")) + Path.DirectorySeparatorChar;
        if (!ownedDirectory.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("测试目录超出临时测试范围。");
        if (Directory.Exists(ownedDirectory)) Directory.Delete(ownedDirectory, recursive: true);
    }
}
