using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class NoticeTimeTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string Group = "9007199254740995";
    private const string Text = "关于合成餐饮比赛赛前打磨工作的通知：\n时间：下周二(10.20)下午1点～4点\n地点：北4-302";
    private static readonly DateTimeOffset SentAt = new(2026, 10, 16, 15, 43, 30, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-notice-time-" + Guid.NewGuid().ToString("N"));
    private AccountWorkspace Workspace() => new(root, Account);
    private AccountMessageStore Messages() => new(root, Account);
    private AccountTaskStore Tasks() => new(root, Account);
    private AccountCandidateRouter Router() => new(Messages(), Tasks());

    private StoredNotice SeedLegacy(string text = Text, bool image = false)
    {
        var workspace = Workspace();
        HashSet<string> pinned = [Group];
        workspace.SetAvailableGroups([new OneBotGroup(Group, "合成比赛群")], pinned);
        var original = new SchoolMessage(new(Account, Group, "source"), "teacher", SentAt,
            [new(image ? SegmentKind.Image : SegmentKind.Text, text)]);
        workspace.Ingest(original, pinned, MessageOrigin.History, "history", SentAt);
        var review = Assert.Single(workspace.Snapshot(SentAt).Items);
        workspace.Execute(new(Account, "accept-notice", review.Id, "accept", review.Version), SentAt);
        var stored = Assert.Single(Router().ListNotices());
        // Reproduce an already accepted notice written by the old parser.
        stored = stored with { Candidate = stored.Candidate with { Time = null } };
        using var connection = new SqliteConnection("Data Source=" + Router().RoutingDatabasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE notices SET notice_json = $json WHERE id = $id";
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(stored));
        command.Parameters.AddWithValue("$id", stored.Id);
        Assert.Equal(1, command.ExecuteNonQuery());
        return stored;
    }

    [Fact]
    public void OldNotice_ResolvesOriginalActivityStartWithoutRewritingCandidateOrAudit()
    {
        var stored = SeedLegacy();
        var original = JsonSerializer.Serialize(Messages().Find(stored.Source));
        var reviews = JsonSerializer.Serialize(Tasks().ListReviews());
        var history = JsonSerializer.Serialize(Tasks().ListTaskHistory());
        var notice = Assert.Single(Workspace().Snapshot(SentAt.AddMonths(1)).Items);
        Assert.Equal("2026-10-20", notice.Due);
        Assert.Equal("13:00", notice.Time);
        Assert.Equal("start", notice.TimeKind);
        Assert.Equal(Text, Assert.Single(notice.Evidence).Text);
        Assert.Equal(JsonSerializer.Serialize(stored), JsonSerializer.Serialize(Router().FindNotice(stored.Id)));
        Assert.Equal(original, JsonSerializer.Serialize(Messages().Find(stored.Source)));
        Assert.Equal(reviews, JsonSerializer.Serialize(Tasks().ListReviews()));
        Assert.Equal(history, JsonSerializer.Serialize(Tasks().ListTaskHistory()));
        Assert.Empty(Tasks().ListTasks());
    }

    [Fact]
    public void OldNotice_ConvertsUsingConfirmedDateAndResolvedStartKindWithIdempotentRetry()
    {
        var stored = SeedLegacy();
        var workspace = Workspace();
        var notice = Assert.Single(workspace.Snapshot(SentAt).Items);
        var command = new WorkspaceCommand(Account, "convert-notice", notice.Id, "convert", notice.Version,
            Date: notice.Due, Time: notice.Time);
        workspace.Execute(command, SentAt);
        Workspace().Execute(command, SentAt.AddMinutes(1));
        var task = Assert.Single(Tasks().ListTasks());
        Assert.Equal(new DateOnly(2026, 10, 20), task.Time.Date);
        Assert.Equal(new TimeOnly(13, 0), task.Time.Time);
        Assert.Equal(TimeKind.Start, task.Time.Kind);
        Assert.Equal(stored.Candidate.Evidence, task.Evidence);
        var converted = Assert.Single(Workspace().Snapshot(SentAt).Items, item => item.Kind == "notice");
        Assert.Equal("converted", converted.State);
        Assert.Equal(task.Id, converted.TargetTaskId);
        Assert.Equal(JsonSerializer.Serialize(stored), JsonSerializer.Serialize(Router().FindNotice(stored.Id)));
    }

    [Theory]
    [InlineData("活动通知：下午1点～4点，地点北4-302")]
    [InlineData("活动通知：10月20日或10月21日下午1点～4点")]
    [InlineData("活动通知：下周二(10.21)下午1点～4点")]
    public void OldNotice_UnresolvedDatesRemainBlankAndCannotConvertWithoutConfirmation(string text)
    {
        SeedLegacy(text);
        var workspace = Workspace();
        var notice = Assert.Single(workspace.Snapshot(SentAt).Items);
        Assert.Null(notice.Due);
        Assert.Null(notice.Time);
        Assert.Throws<ArgumentException>(() => workspace.Execute(
            new(Account, "convert-notice", notice.Id, "missing-date", notice.Version), SentAt));
        Assert.Empty(Tasks().ListTasks());
    }

    [Fact]
    public void OldImageNotice_DoesNotPromoteOcrIntoFormalTime()
    {
        SeedLegacy(Text, image: true);
        var notice = Assert.Single(Workspace().Snapshot(SentAt).Items);
        Assert.Null(notice.Due);
        Assert.Null(notice.Time);
        Assert.Equal("2026-10-20", notice.SuggestedDue);
        Assert.Equal("13:00", notice.SuggestedTime);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
