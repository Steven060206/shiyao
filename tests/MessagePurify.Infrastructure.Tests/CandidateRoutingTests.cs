using MessagePurify.Core;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class CandidateRoutingTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string OtherAccount = "9007199254740994";
    private const string Group = "9007199254740995";
    private const string Sender = "9007199254740996";
    private static readonly DateTimeOffset SentAt = new(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Now = SentAt.AddHours(1);
    private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "MessagePurify-routing-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RoutePending_OrdinaryReliableTaskDoesNotRequireTrustedPublisher()
    {
        var message = Message();
        SaveExtracted(message);
        var candidate = Assert.Single(Messages().Find(message.Key)!.Extraction!.Candidates);
        Assert.Equal(RuleDisposition.Task, candidate.Disposition);
        Assert.False(candidate.Assessments.Source.Reliable);

        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));

        var task = Assert.Single(Tasks().ListTasks());
        Assert.Equal(Account, task.AccountId);
        Assert.Equal(candidate.Title, task.Title);
        Assert.Equal("数学", task.Course);
        Assert.Equal(TaskState.Active, task.State);
        Assert.Equal(new TaskTime(TimeKind.Deadline, new(2026, 10, 13), new(18, 0), "Asia/Shanghai"), task.Time);
        Assert.False(task.TimeProtected);
        Assert.Equal(message.Key, Assert.Single(task.Evidence));
        Assert.Empty(Tasks().ListReviews());
        Assert.Empty(Router().ListNotices());
    }

    [Fact]
    public void RoutePending_UnconfirmedCoursePreservesCandidateAsPendingReview()
    {
        var message = Message();
        var context = Context(course: null);
        SaveExtracted(message, context);
        var candidate = Assert.Single(Messages().Find(message.Key)!.Extraction!.Candidates);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);

        Assert.Equal(1, Router().RoutePending(context, Pinned()));

        var review = Assert.Single(Tasks().ListReviews());
        Assert.Equal(ReviewState.Pending, review.State);
        Assert.Equal(Account, review.AccountId);
        AssertCandidate(candidate, review.Candidate);
        Assert.Null(review.Candidate.Course);
        Assert.Empty(Tasks().ListTasks());
        Assert.Empty(Router().ListNotices());
    }

    [Theory]
    [InlineData("10月13日18点提交作业", RuleDisposition.Task, null)]
    [InlineData("10月13日18点提交作业", RuleDisposition.Task, "物理")]
    [InlineData("10月14日18点自愿报名讲座", RuleDisposition.Notice, null)]
    [InlineData("10月14日18点自愿报名讲座", RuleDisposition.Notice, "物理")]
    public void CourseRelationChangedBeforeRouting_DowngradesOldTaskOrNoticeToPending(
        string text, RuleDisposition originalDisposition, string? currentCourse)
    {
        var message = Message(text: text);
        SaveExtracted(message);
        var original = Assert.Single(Messages().Find(message.Key)!.Extraction!.Candidates);
        Assert.Equal(originalDisposition, original.Disposition);

        Assert.Equal(1, Router().RoutePending(Context(course: currentCourse), Pinned()));

        var review = Assert.Single(Tasks().ListReviews());
        Assert.Equal(ReviewState.Pending, review.State);
        Assert.Equal(RuleDisposition.Pending, review.Candidate.Disposition);
        Assert.Equal(original.Title, review.Candidate.Title);
        Assert.Equal(original.Course, review.Candidate.Course);
        Assert.Equal(original.Time, review.Candidate.Time);
        Assert.Equal(original.SuggestedTime, review.Candidate.SuggestedTime);
        Assert.Equal(original.Evidence, review.Candidate.Evidence);
        Assert.Equal(original.Assessments.Action, review.Candidate.Assessments.Action);
        Assert.Equal(original.Assessments.Time, review.Candidate.Assessments.Time);
        Assert.False(review.Candidate.Assessments.Course.Reliable);
        Assert.False(review.Candidate.Assessments.Audience.Reliable);
        Assert.Contains("课程关联已改变，需要确认", review.Candidate.ReviewReasons);
        AssertCandidate(original, Assert.Single(Messages().Find(message.Key)!.Extraction!.Candidates));
        Assert.Empty(Tasks().ListTasks());
        Assert.Empty(Router().ListNotices());
        Assert.Equal(0, Router().RoutePending(Context(), Pinned()));
        Assert.Equal(review.Id, Assert.Single(Tasks().ListReviews()).Id);
    }

    [Fact]
    public void RoutePending_OcrDateRemainsSuggestedUntilUserConfirms()
    {
        var message = Message() with
        {
            Segments = [new(SegmentKind.Image, "10月13日18点提交作业", AssetId: "school-notice.png")]
        };
        SaveExtracted(message);

        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));

        var review = Assert.Single(Tasks().ListReviews());
        Assert.Equal(RuleDisposition.Pending, review.Candidate.Disposition);
        Assert.Null(review.Candidate.Time);
        Assert.Equal(new TaskTime(TimeKind.Deadline, new(2026, 10, 13), new(18, 0), "Asia/Shanghai"), review.Candidate.SuggestedTime);
        Assert.Equal(message.Key, Assert.Single(review.Candidate.Evidence));
        Assert.Equal("school-notice.png", Assert.Single(Messages().Find(message.Key)!.Message.Segments).AssetId);
        Assert.Empty(Tasks().ListTasks());
        Assert.Empty(Router().ListNotices());
    }

    [Fact]
    public void RoutePending_NoticePersistsFullCandidateSourceAndImportMetadata()
    {
        var original = Message("0009007199254740998", "10月14日18点自愿报名讲座") with
        {
            SentAt = SentAt.ToOffset(TimeSpan.FromHours(-7)).AddTicks(1234),
            Segments = [new(SegmentKind.Text, "10月14日18点自愿报名讲座"),
                new(SegmentKind.Quote, "原通知", new(Account, Group, "-9007199254740999"))]
        };
        SaveExtracted(original, origin: MessageOrigin.History, batchId: "history-import-1");
        var savedMessage = Messages().Find(original.Key)!;
        var candidate = Assert.Single(savedMessage.Extraction!.Candidates);
        Assert.Equal(RuleDisposition.Notice, candidate.Disposition);

        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));

        var notice = Assert.Single(Router().ListNotices());
        Assert.False(string.IsNullOrWhiteSpace(notice.Id));
        Assert.Equal(Account, notice.AccountId);
        AssertCandidate(candidate, notice.Candidate);
        Assert.Equal(original.Key, notice.Source);
        Assert.Equal(savedMessage.ProcessingVersion, notice.ProcessingVersion);
        Assert.Equal(MessageOrigin.History, notice.Origin);
        Assert.Equal("history-import-1", notice.BatchId);
        Assert.Equal(original.SentAt, notice.SentAt);
        Assert.Equal(original.SentAt.Offset, notice.SentAt.Offset);
        Assert.Equal(Now, notice.CreatedAt);
        Assert.Equal(0, Router().RoutePending(Context() with { Now = Now.AddHours(1) }, Pinned()));
        Assert.Equal(notice.Id, Assert.Single(Router().ListNotices()).Id);
        Assert.Empty(Tasks().ListTasks());
        Assert.Empty(Tasks().ListReviews());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessAndRoutePending_NoticeUsesFinalPersistedExtractionVersion(bool preExtracted)
    {
        var original = Message("original", "10月13日18点提交统计作业");
        SaveExtracted(original);
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        var context = Context() with { TrustedSenders = new HashSet<string> { Sender } };
        var message = Message("notice", "10月16日18点自愿报名讲座") with
        {
            SentAt = SentAt.AddMinutes(1),
            Segments = [new(SegmentKind.Text, "10月16日18点自愿报名讲座"),
                new(SegmentKind.Quote, "原通知", original.Key)]
        };
        if (preExtracted) SaveExtracted(message, context);
        else Assert.True(new LocalMessageProcessor(Messages()).Ingest(message, Pinned(), context,
            MessageOrigin.Realtime, "live-1", Now));

        Assert.Equal(1, Router().ProcessAndRoutePending(context, Pinned()));

        var saved = Messages().Find(message.Key)!;
        Assert.True(saved.ProcessingVersion > 0);
        var notice = Assert.Single(Router().ListNotices());
        Assert.Equal(saved.ProcessingVersion, notice.ProcessingVersion);
        Assert.Equal(message.Key, notice.Source);
        Assert.Equal(new DateOnly(2026, 10, 13), Assert.Single(Tasks().ListTasks()).Time.Date);
    }

    [Fact]
    public void RoutePending_IgnoredMessageOnlyRecordsReceipt()
    {
        var message = Message(text: "不用提交这份作业");
        SaveExtracted(message);
        Assert.Equal(RuleDisposition.Ignored, Assert.Single(Messages().Find(message.Key)!.Extraction!.Candidates).Disposition);

        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        Assert.Equal(0, Router().RoutePending(Context(), Pinned()));

        Assert.Equal(1L, Scalar(RoutingPath(), "SELECT COUNT(*) FROM route_receipts;"));
        Assert.Empty(Tasks().ListTasks());
        Assert.Empty(Tasks().ListReviews());
        Assert.Empty(Router().ListNotices());
    }

    [Fact]
    public void RoutePending_MultipleActionsCreateOneWholeReviewForOneMessage()
    {
        var message = MixedMessage();
        SaveExtracted(message);
        var candidates = Messages().Find(message.Key)!.Extraction!.Candidates;
        var candidate = Assert.Single(candidates);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Equal(message.Segments[0].Text, candidate.Title);

        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));

        Assert.Empty(Tasks().ListTasks());
        AssertCandidate(candidate, Assert.Single(Tasks().ListReviews()).Candidate);
        Assert.Empty(Router().ListNotices());
        Assert.Equal(1L, Scalar(RoutingPath(), "SELECT COUNT(*) FROM route_receipts;"));
        Assert.Equal(0, Router().RoutePending(Context(), Pinned()));
        Assert.Empty(Tasks().ListTasks());
        Assert.Single(Tasks().ListReviews());
        Assert.Empty(Router().ListNotices());
    }

    [Fact]
    public void Reopening_DoesNotRecreateCompletedTaskOrIgnoredReview()
    {
        SaveExtracted(Message("task", "10月13日18点提交统计作业"));
        SaveExtracted(Message("review", "提交统计报告"));
        SaveExtracted(Message("notice", "10月16日18点自愿报名讲座"));
        var router = Router();
        Assert.Equal(3, router.RoutePending(Context(), Pinned()));
        var task = Assert.Single(Tasks().ListTasks());
        var completed = Tasks().Complete(task.Id, "user-complete", task.Version, Now.AddMinutes(1));
        var review = Assert.Single(Tasks().ListReviews());
        var ignored = Tasks().IgnoreReview(review.Id, "user-ignore", review.Version, Now.AddMinutes(1));
        var noticeId = Assert.Single(router.ListNotices()).Id;

        Assert.Equal(0, Router().RoutePending(Context() with { Now = Now.AddMinutes(2) }, Pinned()));

        var reopenedTask = Assert.Single(Tasks().ListTasks());
        Assert.Equal(completed.Id, reopenedTask.Id);
        Assert.Equal(TaskState.Completed, reopenedTask.State);
        Assert.Equal(completed.Version, reopenedTask.Version);
        Assert.Equal(completed.UndoOperationId, reopenedTask.UndoOperationId);
        var reopenedReview = Assert.Single(Tasks().ListReviews());
        Assert.Equal(ignored.Id, reopenedReview.Id);
        Assert.Equal(ReviewState.Ignored, reopenedReview.State);
        Assert.Equal(ignored.Version, reopenedReview.Version);
        Assert.Equal(noticeId, Assert.Single(Router().ListNotices()).Id);
    }

    [Theory]
    [InlineData(MessageOrigin.History, MessageOrigin.Realtime)]
    [InlineData(MessageOrigin.Realtime, MessageOrigin.History)]
    public void HistoryRealtimeOverlap_DoesNotCreateDuplicateTask(MessageOrigin first, MessageOrigin replay)
    {
        var message = Message();
        SaveExtracted(message, origin: first, batchId: "first-batch");
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        var originalTask = Assert.Single(Tasks().ListTasks());
        var processor = new LocalMessageProcessor(Messages());

        Assert.False(processor.Ingest(message with { Segments = [new(SegmentKind.Text, "10月20日提交新的作业")] },
            Pinned(), Context(), replay, "replay-batch", Now.AddMinutes(1)));
        Assert.Equal(0, processor.ProcessPending(Context()));
        Assert.Equal(0, Router().RoutePending(Context(), Pinned()));

        var current = Assert.Single(Tasks().ListTasks());
        Assert.Equal(originalTask.Id, current.Id);
        Assert.Equal(originalTask.Time, current.Time);
        Assert.Equal(originalTask.Evidence, current.Evidence);
        Assert.Equal(first, Messages().Find(message.Key)!.Origin);
        Assert.Equal("first-batch", Messages().Find(message.Key)!.BatchId);
    }

    [Fact]
    public void RoutePending_OnlyUsesCurrentGroupsContext()
    {
        var first = Message("group-one");
        var second = Message("group-two", group: "9007199254741000");
        SaveExtracted(first);
        SaveExtracted(second, Context(second.Key.GroupId, "物理"));
        var bothPinned = Pinned();
        bothPinned.Add(second.Key.GroupId);

        Assert.Equal(1, Router().RoutePending(Context(), bothPinned));

        var math = Assert.Single(Tasks().ListTasks());
        Assert.Equal("数学", math.Course);
        Assert.Equal(first.Key, Assert.Single(math.Evidence));
        Assert.Equal(1, Router().RoutePending(Context(second.Key.GroupId, "物理"), bothPinned));
        Assert.Equal(2, Tasks().ListTasks().Count);
        Assert.Contains(Tasks().ListTasks(), task => task.Course == "物理" && task.Evidence.Contains(second.Key));
        Assert.Equal(0, Router().RoutePending(Context(), bothPinned));
    }

    [Fact]
    public void RoutePending_LimitUsesOriginalSendingTimeAndContinuesNextCall()
    {
        var later = Message("late", "10月14日提交后发作业") with { SentAt = SentAt.AddMinutes(1) };
        var earlier = Message("early", "10月13日提交先发作业") with { SentAt = SentAt.AddMinutes(-1) };
        SaveExtracted(later);
        SaveExtracted(earlier);

        Assert.Equal(1, Router().RoutePending(Context(), Pinned(), limit: 1));

        Assert.Equal(earlier.Key, Assert.Single(Assert.Single(Tasks().ListTasks()).Evidence));
        Assert.Equal(1, Router().RoutePending(Context(), Pinned(), limit: 1));
        Assert.Equal(0, Router().RoutePending(Context(), Pinned(), limit: 1));
        Assert.Equal(2, Tasks().ListTasks().Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void RoutePending_InvalidLimitDoesNotConsumeMessage(int limit)
    {
        SaveExtracted(Message());

        Assert.Throws<ArgumentOutOfRangeException>(() => Router().RoutePending(Context(), Pinned(), limit));

        Assert.Empty(Tasks().ListTasks());
        Assert.Equal(1, Router().RoutePending(Context(), Pinned(), limit: 1));
    }

    [Fact]
    public void Unpinning_PreservesExistingTaskAndUnroutedMessageUntilPinnedAgain()
    {
        SaveExtracted(Message("first"));
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        var existing = Assert.Single(Tasks().ListTasks());
        var waiting = Message("waiting", "10月14日提交下一份作业");
        SaveExtracted(waiting);

        Assert.Equal(0, Router().RoutePending(Context(), new HashSet<string>()));

        Assert.Equal(existing.Id, Assert.Single(Tasks().ListTasks()).Id);
        Assert.NotNull(Messages().Find(waiting.Key)!.Extraction);
        Assert.Equal(1L, Scalar(RoutingPath(), "SELECT COUNT(*) FROM route_receipts;"));
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        Assert.Equal(2, Tasks().ListTasks().Count);
    }

    [Theory]
    [InlineData("sender")]
    [InlineData("course")]
    public void ChangedExclusion_PreservesExistingTaskAndLeavesUnroutedMessageForLater(string excluded)
    {
        SaveExtracted(Message("first"));
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        var original = Assert.Single(Tasks().ListTasks());
        var waiting = Message("waiting", "10月14日提交下一份作业");
        SaveExtracted(waiting);
        var context = excluded == "sender"
            ? Context() with { ExcludedSenders = new HashSet<string> { Sender } }
            : Context() with { CourseExcluded = true };

        Assert.Equal(0, Router().RoutePending(context, Pinned()));

        Assert.Equal(original.Id, Assert.Single(Tasks().ListTasks()).Id);
        Assert.NotNull(Messages().Find(waiting.Key)!.Extraction);
        Assert.Equal(1L, Scalar(RoutingPath(), "SELECT COUNT(*) FROM route_receipts;"));
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        Assert.Equal(2, Tasks().ListTasks().Count);
    }

    [Fact]
    public void Constructor_RejectsMismatchedStoreAccounts()
    {
        Assert.Throws<ArgumentException>(() => new AccountCandidateRouter(Messages(), Tasks(OtherAccount)));

        Assert.False(File.Exists(RoutingPath()));
    }

    [Fact]
    public void Constructor_RejectsSameAccountStoresFromDifferentDataRoots()
    {
        var separateTasks = new AccountTaskStore(Path.Combine(dataRoot, "other-root"), Account);

        Assert.Throws<ArgumentException>(() => new AccountCandidateRouter(Messages(), separateTasks));

        Assert.False(File.Exists(RoutingPath()));
    }

    [Fact]
    public void RoutePending_RejectsWrongContextAccountWithoutConsumingMessage()
    {
        SaveExtracted(Message());

        Assert.Throws<ArgumentException>(() => Router().RoutePending(Context(account: OtherAccount), Pinned()));

        Assert.Empty(Tasks().ListTasks());
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
    }

    [Fact]
    public void RoutePending_UnprocessedMessageWaitsForExtraction()
    {
        var message = Message();
        var processor = new LocalMessageProcessor(Messages());
        Assert.True(processor.Ingest(message, Pinned(), Context(), MessageOrigin.History, "history-1", Now));

        Assert.Equal(0, Router().RoutePending(Context(), Pinned()));

        Assert.Empty(Tasks().ListTasks());
        Assert.Null(Messages().Find(message.Key)!.Extraction);
        Assert.Equal(1, processor.ProcessPending(Context()));
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        Assert.Single(Tasks().ListTasks());
    }

    [Fact]
    public void RoutePending_EarlierUnprocessedMessageBlocksLaterExtractedMessage()
    {
        var earlier = Message("earlier");
        var later = Message("later", "10月14日提交另一份作业") with { SentAt = SentAt.AddMinutes(1) };
        var processor = new LocalMessageProcessor(Messages());
        Assert.True(processor.Ingest(earlier, Pinned(), Context(), MessageOrigin.History, "history-1", Now));
        Assert.True(processor.Ingest(later, Pinned(), Context(), MessageOrigin.History, "history-1", Now));
        Assert.True(Messages().TrySaveExtraction(later.Key, 0, MessageRuleExtractor.Extract(later, Context()), Now));

        Assert.Equal(0, Router().RoutePending(Context(), Pinned()));

        Assert.Empty(Tasks().ListTasks());
        Assert.Equal(1, processor.ProcessPending(Context()));
        Assert.Equal(2, Router().RoutePending(Context(), Pinned()));
    }

    [Fact]
    public void ProcessAndRoutePending_ExtractsAndCreatesTaskInOneCall()
    {
        var message = Message();
        Assert.True(new LocalMessageProcessor(Messages()).Ingest(message, Pinned(), Context(),
            MessageOrigin.Realtime, "live-1", Now));

        Assert.Equal(1, Router().ProcessAndRoutePending(Context(), Pinned()));

        Assert.NotNull(Messages().Find(message.Key)!.Extraction);
        Assert.Equal(message.Key, Assert.Single(Assert.Single(Tasks().ListTasks()).Evidence));
        Assert.Equal(0, Router().ProcessAndRoutePending(Context(), Pinned()));
    }

    [Fact]
    public void ReanalysisAfterReceipt_DoesNotReplayDifferentCandidateOrRestoreCompletedTask()
    {
        var message = Message();
        SaveExtracted(message);
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        var task = Assert.Single(Tasks().ListTasks());
        var completed = Tasks().Complete(task.Id, "user-complete", task.Version, Now.AddMinutes(1));
        var currentMessage = Messages().Find(message.Key)!;
        var updatedMessage = message with { Segments = [new(SegmentKind.Text, "10月20日18点提交重新分析的材料")] };
        var updated = MessageRuleExtractor.Extract(updatedMessage, Context());
        Assert.True(Messages().TrySaveExtraction(message.Key, currentMessage.ProcessingVersion, updated, Now.AddMinutes(2)));
        Assert.Equal(2, Messages().Find(message.Key)!.ProcessingVersion);

        Assert.Equal(0, Router().RoutePending(Context() with { Now = Now.AddMinutes(3) }, Pinned()));

        var saved = Assert.Single(Tasks().ListTasks());
        Assert.Equal(completed.Id, saved.Id);
        Assert.Equal(TaskState.Completed, saved.State);
        Assert.Equal(completed.Version, saved.Version);
        Assert.Equal(completed.Time, saved.Time);
        Assert.Empty(Tasks().ListReviews());
        Assert.Empty(Router().ListNotices());
        Assert.Equal(1L, Scalar(RoutingPath(), "SELECT COUNT(*) FROM route_receipts;"));
    }

    [Fact]
    public void RoutePending_ReliableLinkedChangeAppliesCancellationToOriginalTask()
    {
        SaveExtracted(Message("original", "10月13日18点提交统计作业"));
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        var original = Assert.Single(Tasks().ListTasks());
        var known = new KnownTask(original.Id, original.Title, original.Course!, original.Time,
            original.State, original.Evidence, Sender);
        var context = Context() with { TrustedSenders = new HashSet<string> { Sender }, KnownTasks = [known] };
        var cancelMessage = Message("cancel", "统计作业取消") with
        {
            SentAt = SentAt.AddMinutes(1),
            Segments = [new(SegmentKind.Text, "统计作业取消"), new(SegmentKind.Quote, "原通知", original.Evidence[0])]
        };
        SaveExtracted(cancelMessage, context);
        var candidate = Assert.Single(Messages().Find(cancelMessage.Key)!.Extraction!.Candidates);
        Assert.Equal(RuleDisposition.Change, candidate.Disposition);
        Assert.Equal(original.Id, candidate.TargetTaskId);

        Assert.Equal(1, Router().RoutePending(context, Pinned()));

        var current = Assert.Single(Tasks().ListTasks());
        Assert.Equal(original.Id, current.Id);
        Assert.Equal(TaskState.Cancelled, current.State);
        Assert.Equal(original.Version + 1, current.Version);
        Assert.Equal(original.Time, current.Time);
        Assert.Contains(cancelMessage.Key, current.Evidence);
        Assert.Empty(Tasks().ListReviews());
        Assert.Empty(Router().ListNotices());
    }

    [Fact]
    public void ReceiptInsertFailure_RollsBackWholeMessageAndRetryReusesDeterministicOperations()
    {
        SaveExtracted(MixedMessage());
        var router = Router();
        Execute(router.RoutingDatabasePath, "CREATE TRIGGER fail_receipt BEFORE INSERT ON route_receipts BEGIN SELECT RAISE(ABORT, 'injected routing failure'); END;");

        Assert.Throws<SqliteException>(() => router.RoutePending(Context(), Pinned()));

        Assert.Empty(Tasks().ListTasks());
        Assert.Empty(Tasks().ListReviews());
        Assert.Empty(router.ListNotices());
        Assert.Equal(0L, Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM operations;"));
        Assert.Equal(0L, Scalar(router.RoutingDatabasePath, "SELECT COUNT(*) FROM route_receipts;"));
        Assert.NotNull(Messages().Find(MixedMessage().Key)!.Extraction);
        Assert.Equal(1, Messages().Find(MixedMessage().Key)!.ProcessingVersion);
        Execute(router.RoutingDatabasePath, "DROP TRIGGER fail_receipt;");

        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        Assert.Empty(Tasks().ListTasks());
        Assert.Single(Tasks().ListReviews());
        Assert.Empty(Router().ListNotices());
        Assert.Equal(1L, Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM operations;"));
        Assert.Equal(1L, Scalar(router.RoutingDatabasePath, "SELECT COUNT(*) FROM route_receipts;"));
        Assert.Equal(0, Router().RoutePending(Context(), Pinned()));
    }

    [Fact]
    public void RoutePending_WalDatabaseIsRejectedWithoutWritingResults()
    {
        SaveExtracted(MixedMessage());
        var router = Router();
        Assert.Equal("wal", Scalar(Messages().DatabasePath, "PRAGMA journal_mode=WAL;"));

        Assert.Throws<InvalidDataException>(() => router.RoutePending(Context(), Pinned()));

        Assert.Empty(Tasks().ListTasks());
        Assert.Empty(Tasks().ListReviews());
        Assert.Empty(router.ListNotices());
        Assert.Equal(0L, Scalar(Tasks().DatabasePath, "SELECT COUNT(*) FROM operations;"));
        Assert.Equal(0L, Scalar(router.RoutingDatabasePath, "SELECT COUNT(*) FROM route_receipts;"));
        Assert.NotNull(Messages().Find(MixedMessage().Key)!.Extraction);
        Assert.Equal("delete", Scalar(Messages().DatabasePath, "PRAGMA journal_mode=DELETE;"));
        Assert.Equal(1, router.RoutePending(Context(), Pinned()));
    }

    [Fact]
    public void RouterInitialization_UsesIndependentAccountDatabaseAndKeepsExistingSchemas()
    {
        var router = Router();

        Assert.Equal(RoutingPath(), router.RoutingDatabasePath);
        Assert.True(File.Exists(router.RoutingDatabasePath));
        Assert.Equal(1L, Scalar(router.RoutingDatabasePath, "PRAGMA user_version;"));
        Assert.Equal(1L, Scalar(Messages().DatabasePath, "PRAGMA user_version;"));
        Assert.Equal(1L, Scalar(Tasks().DatabasePath, "PRAGMA user_version;"));
        Assert.Equal(new[] { "cache_cleanup_result", "course_recheck_result", "notices", "route_receipts", "store_metadata" }, Tables(router.RoutingDatabasePath));
        Assert.NotEqual(router.RoutingDatabasePath, Router(OtherAccount).RoutingDatabasePath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(77)]
    public void RouterInitialization_UnknownSchemaPreservesExistingDatabase(int version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RoutingPath())!);
        Execute(RoutingPath(), $"PRAGMA user_version = {version}; CREATE TABLE sentinel(value TEXT); INSERT INTO sentinel VALUES ('preserve me');");

        Assert.Throws<InvalidDataException>(() => Router());

        Assert.Equal((long)version, Scalar(RoutingPath(), "PRAGMA user_version;"));
        Assert.Equal("preserve me", Scalar(RoutingPath(), "SELECT value FROM sentinel;"));
        Assert.Equal(new[] { "sentinel" }, Tables(RoutingPath()));
    }

    [Fact]
    public void RouterInitialization_CopiedOtherAccountDatabaseRejectsOwnerAndPreservesNotice()
    {
        SaveExtracted(Message(text: "10月14日18点自愿报名讲座"));
        Assert.Equal(1, Router().RoutePending(Context(), Pinned()));
        var originalNotice = Assert.Single(Router().ListNotices());
        Execute(RoutingPath(), "PRAGMA wal_checkpoint(TRUNCATE);");
        var copiedPath = Path.Combine(dataRoot, "accounts", OtherAccount, "routing.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(copiedPath)!);
        File.Copy(RoutingPath(), copiedPath);
        var copiedBytes = File.ReadAllBytes(copiedPath);

        Assert.Throws<InvalidDataException>(() => Router(OtherAccount));

        Assert.Equal(copiedBytes, File.ReadAllBytes(copiedPath));
        var notice = Assert.Single(Router().ListNotices());
        Assert.Equal(originalNotice.Id, notice.Id);
        Assert.Equal(Account, notice.AccountId);
        AssertCandidate(originalNotice.Candidate, notice.Candidate);
    }

    private AccountMessageStore Messages(string account = Account) => new(dataRoot, account);
    private AccountTaskStore Tasks(string account = Account) => new(dataRoot, account);
    private AccountCandidateRouter Router(string account = Account) => new(Messages(account), Tasks(account));
    private string RoutingPath() => Path.Combine(dataRoot, "accounts", Account, "routing.sqlite3");
    private static HashSet<string> Pinned(string group = Group) => [group];
    private static RuleContext Context(string group = Group, string? course = "数学", string account = Account) =>
        new(account, group, "Asia/Shanghai", Now, course);
    private static SchoolMessage Message(string id = "-9007199254740997", string text = "10月13日18点提交作业",
        string group = Group, string account = Account) =>
        new(new(account, group, id), Sender, SentAt, [new(SegmentKind.Text, text)]);
    private static SchoolMessage MixedMessage() => Message(text:
        "10月13日18点提交实验报告；需要提交材料；10月14日18点自愿报名讲座；不用提交这份作业");

    private void SaveExtracted(SchoolMessage message, RuleContext? context = null,
        MessageOrigin origin = MessageOrigin.Realtime, string batchId = "live-1")
    {
        context ??= Context(message.Key.GroupId, account: message.Key.AccountId);
        var processor = new LocalMessageProcessor(Messages(message.Key.AccountId));
        Assert.True(processor.Ingest(message, Pinned(message.Key.GroupId), context, origin, batchId, Now));
        Assert.Equal(1, processor.ProcessPending(context));
    }

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

    private static IReadOnlyList<string> Tables(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));
        return tables;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
    }
}
