using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class ReviewDateTests : IDisposable
{
    private const string Account = "12345678";
    private const string Group = "10001";
    private const string Text = "本周六上午8点15分进行签到，地点：南区活动中心一楼，北4教学楼进行签到，比赛9点开始，各组准备好5份产品计划书";
    private static readonly DateTimeOffset SentAt = DateTimeOffset.Parse("2026-10-15T12:00:00+08:00");
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-review-date-" + Guid.NewGuid().ToString("N"));
    private AccountWorkspace Workspace() => new(root, Account);
    private AccountTaskStore Tasks() => new(root, Account);
    private AccountMessageStore Messages() => new(root, Account);

    private PendingReview SeedLegacy(string text = Text, bool image = false)
    {
        var workspace = Workspace();
        var quote = new SchoolMessage(new(Account, Group, "000-quote"), "teacher", SentAt.AddDays(-7),
            [new(SegmentKind.Text, "本周六签到")]);
        Messages().TryEnqueue(quote, MessageOrigin.History, "history", SentAt);
        var message = new SchoolMessage(new(Account, Group, "source"), "teacher", SentAt,
            [new(image ? SegmentKind.Image : SegmentKind.Text, text), new(SegmentKind.Quote, "原通知", quote.Key)]);
        workspace.Ingest(message, new HashSet<string> { Group }, MessageOrigin.History, "history", SentAt);
        var review = Tasks().ListReviews().Single(item => item.Candidate.Title == text);
        review = review with { Candidate = review.Candidate with { Time = null } };
        using var connection = new SqliteConnection("Data Source=" + Tasks().DatabasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE reviews SET record_json = $json WHERE id = $id";
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(review));
        command.Parameters.AddWithValue("$id", review.Id);
        Assert.Equal(1, command.ExecuteNonQuery());
        return review;
    }

    [Fact]
    public void LegacyReviewPrefillsSourceSaturdayWithoutRewritingOriginalOrAudit()
    {
        var stored = SeedLegacy();
        var original = JsonSerializer.Serialize(Messages().Find(new(Account, Group, "source")));
        var history = JsonSerializer.Serialize(Tasks().ListTaskHistory());
        var review = Workspace().Snapshot(SentAt.AddMonths(1)).Items.Single(item => item.Id == stored.Id);
        Assert.Equal("2026-10-17", review.Due);
        Assert.Null(review.Time);
        Assert.Equal("start", review.TimeKind);
        Assert.True(review.CanAccept);
        Assert.Contains(review.Evidence, item => item.Text.StartsWith(Text) && item.SentAt == SentAt);
        Assert.Equal(JsonSerializer.Serialize(stored), JsonSerializer.Serialize(Tasks().FindReview(stored.Id)));
        Assert.Equal(original, JsonSerializer.Serialize(Messages().Find(new(Account, Group, "source"))));
        Assert.Equal(history, JsonSerializer.Serialize(Tasks().ListTaskHistory()));
        Assert.Empty(Tasks().ListTasks());
    }

    [Fact]
    public void LegacyReviewConfirmsPrefilledDateWithDefaultStartKindAndRetry()
    {
        var stored = SeedLegacy();
        var workspace = Workspace();
        var review = workspace.Snapshot(SentAt).Items.Single(item => item.Id == stored.Id);
        var command = new WorkspaceCommand(Account, "accept-review", review.Id, "confirm", review.Version,
            Date: review.Due, Time: review.Time);
        workspace.Execute(command, SentAt);
        Workspace().Execute(command, SentAt.AddMinutes(1));
        var task = Assert.Single(Tasks().ListTasks());
        Assert.Equal(new DateOnly(2026, 10, 17), task.Time.Date);
        Assert.Null(task.Time.Time);
        Assert.Equal(TimeKind.Start, task.Time.Kind);
        Assert.Equal(Text, task.Title);
        Assert.Equal(JsonSerializer.Serialize(stored.Candidate), JsonSerializer.Serialize(Tasks().FindReview(stored.Id)!.Candidate));
    }

    [Theory]
    [InlineData("本周六8点签到，下周一9点参加比赛", false)]
    [InlineData("本周六25点签到，比赛9点开始", false)]
    [InlineData(Text, true)]
    public void LegacyReviewDoesNotPromoteAmbiguousInvalidOrImageDate(string text, bool image)
    {
        var stored = SeedLegacy(text, image);
        var review = Workspace().Snapshot(SentAt.AddMonths(1)).Items.Single(item => item.Id == stored.Id);
        Assert.Null(review.Due);
        Assert.Empty(Tasks().ListTasks());
    }

    [Fact]
    public void LegacyReviewWithOneClockPrefillsDateAndClock()
    {
        var stored = SeedLegacy("本周六上午8点15分签到，北楼签到");
        var review = Workspace().Snapshot(SentAt.AddMonths(1)).Items.Single(item => item.Id == stored.Id);
        Assert.Equal("2026-10-17", review.Due);
        Assert.Equal("08:15", review.Time);
        Assert.Equal("start", review.TimeKind);
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
