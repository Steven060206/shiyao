using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Backup;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class MessageReviewCompatibilityTests : IDisposable
{
    private const string Account = "12345";
    private const string Group = "200";
    private const string Raw = "明天交实验报告，格式为PDF；下周一交课程论文";
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(8));
    private static readonly BackupApplicationSettings Preferences = new(false, "dark", "reduce", null);
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-review-compat-" + Guid.NewGuid().ToString("N"));
    private AccountWorkspace Workspace(string account = Account) => new(root, account);
    private AccountTaskStore Tasks(string account = Account) => new(root, account);
    private AccountMessageStore Messages(string account = Account) => new(root, account);
    private static HashSet<string> Pinned() => [Group];
    private static SchoolMessage Message(string id = "original", string group = Group, string account = Account) =>
        new(new(account, group, id), "teacher", Now.AddMinutes(-1), [new(SegmentKind.Text, Raw)]);
    private static TaskTime Date(int day) => new(TimeKind.Deadline, new(2026, 10, day), null, "Asia/Shanghai");
    private static string ReviewId(SchoolMessage message, int index) => "review-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Key = message.Key, Index = index })))).ToLowerInvariant();
    private static RuleCandidate Fragment(SchoolMessage message, int index, IReadOnlyList<MessageKey>? evidence = null)
    {
        var reliable = new RuleAssessment("旧规则已识别", true);
        return new(index == 0 ? "明天交实验报告，格式为PDF" : $"第{index + 1}项交课程论文", RuleAction.Submit,
            RuleDisposition.Pending, null, Date(13 + index), null, null, 2,
            new(reliable, reliable, new("课程未确认", false), reliable, reliable, reliable),
            ["课程未确认"], evidence ?? [message.Key]);
    }

    private PendingReview[] SeedLegacy(SchoolMessage message, int count = 2, bool saveReviews = true,
        IReadOnlyList<MessageKey>? evidence = null)
    {
        var messages = Messages(message.Key.AccountId);
        Assert.True(messages.TryEnqueue(message, MessageOrigin.History, "legacy-import", Now));
        var candidates = Enumerable.Range(0, count).Select(index => Fragment(message, index, evidence)).ToArray();
        Assert.True(messages.TrySaveExtraction(message.Key, 0, new("m2-local-v1", candidates), Now));
        if (!saveReviews) return [];
        var tasks = Tasks(message.Key.AccountId);
        // Insert in reverse order: the keeper is the original extraction's first pending index.
        foreach (var index in Enumerable.Range(0, count).Reverse())
            tasks.SavePending(ReviewId(message, index), candidates[index], "legacy-save-" + ReviewId(message, index), Now);
        return Enumerable.Range(0, count).Select(index => tasks.FindReview(ReviewId(message, index))!).ToArray();
    }

    [Fact]
    public void Ingest_OneOriginalBecomesOneCompleteReviewAndAcceptsAsOneSplittableTask()
    {
        var workspace = Workspace();
        var message = Message();
        Assert.True(workspace.Ingest(message, Pinned(), MessageOrigin.Realtime, "live", Now));

        var review = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("review", review.Kind);
        Assert.Equal("pending", review.State);
        Assert.Equal(Raw, review.Title);
        Assert.Equal(Raw, Assert.Single(review.Evidence).Text);
        Assert.Null(review.Due);
        Assert.Null(review.SuggestedDue);
        var candidate = Assert.Single(Tasks().ListReviews()).Candidate;
        Assert.Null(candidate.Time);
        Assert.Null(candidate.SuggestedTime);
        Assert.Null(candidate.TargetTaskId);
        Assert.Single(Messages().Find(message.Key)!.Extraction!.Candidates);

        workspace.Execute(new(Account, "accept-review", review.Id, "accept-complete-original", review.Version,
            Date: "2026-10-20", TimeKind: "deadline"), Now);

        var task = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("task", task.Kind);
        Assert.Equal(Raw, task.Title);
        Assert.Equal("2026-10-20", task.Due);
        Assert.Single(Tasks().ListTasks());
        Assert.Equal(ReviewState.Accepted, Tasks().FindReview(review.Id)!.State);
        var split = Tasks().SplitTask(task.Id, "manual-part", "split-after-accept", task.Version,
            new("单独管理课程论文", null, null, null, TaskState.Active, [message.Key]), Now);
        Assert.Equal(task.Id, split.Review!.SplitParentTaskId);
        Assert.Equal("单独管理课程论文", split.Review.Candidate.Title);
    }

    [Fact]
    public void Ingest_IgnoredWholeOriginalStaysSingleAfterReopenAndReplay()
    {
        var workspace = Workspace();
        var message = Message();
        workspace.Ingest(message, Pinned(), MessageOrigin.Realtime, "live", Now);
        var pending = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(new(Account, "ignore", pending.Id, "ignore-original", pending.Version), Now);
        var before = JsonSerializer.Serialize(Assert.Single(Tasks().ListReviews()));

        var reopened = Workspace();
        Assert.False(reopened.Ingest(message, Pinned(), MessageOrigin.History, "replay", Now.AddMinutes(1)));

        var ignored = Assert.Single(reopened.Snapshot(Now).Items);
        Assert.Equal(pending.Id, ignored.Id);
        Assert.Equal("ignored", ignored.State);
        Assert.Equal(Raw, ignored.Title);
        Assert.Equal(before, JsonSerializer.Serialize(Assert.Single(Tasks().ListReviews())));
        Assert.Empty(Tasks().ListTasks());
    }

    [Fact]
    public void LegacyPendingFragments_ConsolidateWithStableIdRetainedChildrenAndImmutableAudit()
    {
        var message = Message();
        var old = SeedLegacy(message);
        var oldOperations = Operations();

        var workspace = Workspace();

        var item = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal(old[0].Id, item.Id);
        Assert.Equal(Raw, item.Title);
        Assert.Null(item.Due);
        Assert.Null(item.SuggestedDue);
        var keeper = Tasks().FindReview(old[0].Id)!;
        var child = Tasks().FindReview(old[1].Id)!;
        Assert.Equal(2, Tasks().ListReviews().Count);
        Assert.Equal(ReviewState.Pending, keeper.State);
        Assert.True(keeper.Version > old[0].Version);
        Assert.Equal(old[0].CreatedAt, keeper.CreatedAt);
        Assert.Equal("Consolidated", child.State.ToString());
        Assert.True(child.Version > old[1].Version);
        Assert.Equal(old[1].CreatedAt, child.CreatedAt);
        Assert.Equal(JsonSerializer.Serialize(old[1].Candidate), JsonSerializer.Serialize(child.Candidate));
        Assert.Equal(old[0].Id, JsonSerializer.SerializeToElement(child).GetProperty("ConsolidatedIntoId").GetString());

        var operations = Operations();
        foreach (var (id, operation) in oldOperations) Assert.Equal(operation, operations[id]);
        var added = operations.Where(entry => !oldOperations.ContainsKey(entry.Key)).Select(entry => entry.Value).ToArray();
        Assert.NotEmpty(added);
        Assert.Contains(added, entry => entry.Before is not null && entry.Before.Contains(old[0].Id) && entry.Before.Contains(old[1].Id));
        Assert.Contains(added, entry => entry.After.Contains(old[0].Id) && entry.After.Contains(old[1].Id));
        var reviewsBeforeReopen = JsonSerializer.Serialize(Tasks().ListReviews());

        Assert.Equal(item.Id, Assert.Single(Workspace().Snapshot(Now.AddMinutes(1)).Items).Id);
        Assert.Equal(reviewsBeforeReopen, JsonSerializer.Serialize(Tasks().ListReviews()));
        Assert.Equal(operations, Operations());
        Assert.Equal(2, Messages().Find(message.Key)!.Extraction!.Candidates.Count);
    }

    [Fact]
    public void LegacySingletonFragment_IsExpandedToTheWholeOriginalWithoutLosingItsId()
    {
        var old = Assert.Single(SeedLegacy(Message(), count: 1));

        var item = Assert.Single(Workspace().Snapshot(Now).Items);

        Assert.Equal(old.Id, item.Id);
        Assert.Equal(Raw, item.Title);
        Assert.Null(item.Due);
        Assert.True(item.Version > old.Version);
        Assert.Single(Tasks().ListReviews());
    }

    [Fact]
    public void LegacyMigration_PreservesHandledReviewsManualSplitAndAcceptedTask()
    {
        var message = Message();
        var old = SeedLegacy(message, count: 5);
        var tasks = Tasks();
        tasks.IgnoreReview(old[2].Id, "legacy-ignore", old[2].Version, Now);
        var accepted = tasks.AcceptReview(old[3].Id, "accepted-legacy", "legacy-accept", old[3].Version, Date(16), Now);
        tasks.RejectReview(old[4].Id, "legacy-reject", old[4].Version, Now);
        var manual = tasks.SplitTask(accepted.Id, ReviewId(message, 5), "legacy-manual-split", accepted.Version,
            new("用户明确拆出的独立项目", null, null, null, TaskState.Active, [message.Key]), Now).Review!;
        var stored = Messages().Find(message.Key)!;
        Assert.True(Messages().TrySaveExtraction(message.Key, stored.ProcessingVersion,
            stored.Extraction! with { Candidates = stored.Extraction!.Candidates.Append(manual.Candidate).ToArray() }, Now));
        var preserved = old.Skip(2).Select(review => review.Id).Append(manual.Id)
            .ToDictionary(id => id, id => JsonSerializer.Serialize(tasks.FindReview(id)));
        var taskBefore = JsonSerializer.Serialize(tasks.FindTask(accepted.Id));

        var workspace = Workspace();

        foreach (var (id, json) in preserved) Assert.Equal(json, JsonSerializer.Serialize(tasks.FindReview(id)));
        Assert.Equal(taskBefore, JsonSerializer.Serialize(tasks.FindTask(accepted.Id)));
        Assert.Equal(6, tasks.ListReviews().Count);
        Assert.Equal(2, tasks.ListReviews(ReviewState.Pending).Count);
        Assert.Equal(Raw, tasks.FindReview(old[0].Id)!.Candidate.Title);
        Assert.Contains(workspace.Snapshot(Now).Items, item => item.Id == manual.Id && item.State == "pending");
    }

    [Fact]
    public void LegacyMigration_UsesActualOriginalDespiteSortedQuotedEvidenceAndSeparatesSourcesAndAccounts()
    {
        var quoted = Message("000-quoted");
        SeedLegacy(quoted);
        var sources = new[] { Message("z-original"), Message("z-second"), Message("z-original", "300") };
        foreach (var source in sources)
            SeedLegacy(source with { Segments = [new(SegmentKind.Text, Raw), new(SegmentKind.Quote, "引用原通知", quoted.Key)] },
                evidence: [source.Key, quoted.Key]);
        var other = Message(account: "67890");
        SeedLegacy(other);
        var otherBefore = JsonSerializer.Serialize(Tasks(other.Key.AccountId).ListReviews());

        var workspace = Workspace();

        Assert.Equal(4, workspace.Snapshot(Now).Items.Count);
        foreach (var source in sources.Prepend(quoted))
        {
            var keeper = Tasks().FindReview(ReviewId(source, 0))!;
            Assert.Equal(ReviewState.Pending, keeper.State);
            Assert.Equal(Raw, keeper.Candidate.Title);
            Assert.Equal("Consolidated", Tasks().FindReview(ReviewId(source, 1))!.State.ToString());
            if (source.Key != quoted.Key) Assert.Equal(quoted.Key, keeper.Candidate.Evidence[0]);
        }
        Assert.Equal(otherBefore, JsonSerializer.Serialize(Tasks(other.Key.AccountId).ListReviews()));
    }

    [Fact]
    public void ProcessPending_OldUnroutedExtractionCreatesOneCompleteReview()
    {
        var message = Message();
        SeedLegacy(message, saveReviews: false);
        var workspace = Workspace();

        Assert.Equal(1, workspace.ProcessPending(Pinned(), Now));

        var item = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("review", item.Kind);
        Assert.Equal(Raw, item.Title);
        Assert.Null(item.Due);
        Assert.Null(item.SuggestedDue);
        Assert.Single(Tasks().ListReviews());
        Assert.Empty(Tasks().ListTasks());
        Assert.Equal(0, workspace.ProcessPending(Pinned(), Now));
    }

    [Fact]
    public void RestoreBackup_ConsolidatesLegacyReviewsOnTheAlreadyOpenWorkspace()
    {
        var workspace = Workspace();
        var old = SeedLegacy(Message());
        var archive = Path.Combine(root, "legacy.zip");
        workspace.ExportBackup(archive, Preferences, Now);
        Assert.Single(Workspace().Snapshot(Now).Items);
        using var prepared = workspace.InspectBackup(archive);

        workspace.RestoreBackup(prepared, Preferences, Now.AddMinutes(1));

        var item = Assert.Single(workspace.Snapshot(Now.AddMinutes(1)).Items);
        Assert.Equal(old[0].Id, item.Id);
        Assert.Equal(Raw, item.Title);
        Assert.Equal("Consolidated", Tasks().FindReview(old[1].Id)!.State.ToString());
        Assert.Equal(1, workspace.Generation);
    }

    [Fact]
    public void RestoreBackup_PreservesConsolidatedChildAndHistoryWithoutRepeatingConsolidation()
    {
        var old = SeedLegacy(Message());
        var workspace = Workspace();
        var before = JsonSerializer.Serialize(Tasks().ListReviews());
        var operations = Operations();
        var archive = Path.Combine(root, "consolidated.zip");
        workspace.ExportBackup(archive, Preferences, Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(new(Account, "ignore", review.Id, "ignore-after-backup", review.Version), Now);
        using var prepared = workspace.InspectBackup(archive);

        workspace.RestoreBackup(prepared, Preferences, Now.AddMinutes(1));

        Assert.Equal(before, JsonSerializer.Serialize(Tasks().ListReviews()));
        Assert.Equal(operations, Operations());
        Assert.Equal(old[0].Id, Assert.Single(workspace.Snapshot(Now).Items).Id);
        Assert.Equal(old[0].Id, Tasks().FindReview(old[1].Id)!.ConsolidatedIntoId);
    }

    private sealed record Operation(string Request, string ResultKind, string ResultId,
        string ProcessedAt, string? Before, string After);
    private Dictionary<string, Operation> Operations()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Tasks().DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT operation_id, request_json, result_kind, result_id, processed_at, before_json, after_json FROM operations ORDER BY sequence;";
        using var reader = command.ExecuteReader();
        var operations = new Dictionary<string, Operation>();
        while (reader.Read()) operations.Add(reader.GetString(0), new(reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6)));
        return operations;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
