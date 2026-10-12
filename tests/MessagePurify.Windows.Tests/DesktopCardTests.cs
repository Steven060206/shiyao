using MessagePurify.Infrastructure.Application;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class DesktopCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(8));

    [Theory]
    [InlineData("deadline", "12:00", "2026-10-12T11:59:59+08:00", "2026-10-12T12:00:00+08:00")]
    [InlineData("start", "12:00", "2026-10-12T03:59:59Z", "2026-10-12T04:00:00Z")]
    [InlineData("deadline", null, "2026-10-12T23:59:59+08:00", "2026-10-13T00:00:00+08:00")]
    public void ClockBoundaryRefreshesLabelsWithoutTaskOrPriorityChanges(string kind, string? time,
        string before, string after)
    {
        var task = Item("clock", time: time, priority: 0) with { TimeKind = kind };
        var cards = DesktopCardProjection.Build(Snapshot(task), Connected());
        var earlier = DateTimeOffset.Parse(before);
        var later = DateTimeOffset.Parse(after);
        string Signature(DateTimeOffset now) => DesktopCardProjection.RenderSignature(cards, "dark", null, null, now);

        Assert.Equal(Signature(earlier), Signature(earlier.AddSeconds(-1)));
        Assert.NotEqual(Signature(earlier), Signature(later));
        Assert.Equal(Signature(later), Signature(later.AddSeconds(1)));
        Assert.Equal(1, task.Version);
        Assert.Equal(0, task.Priority);
    }

    [Fact]
    public void CardsContainOnlyActiveTasksAndPendingReviews()
    {
        var task = Item("task", "task", "active");
        var review = Item("review", "review", "pending");
        var snapshot = Snapshot(task, review,
            Item("completed", "task", "completed"), Item("cancelled", "task", "cancelled"),
            Item("archived", "task", "archived"), Item("ignored", "review", "ignored"),
            Item("accepted", "review", "accepted"), Item("notice", "notice", "active"));

        var cards = DesktopCardProjection.Build(snapshot, Connected());

        Assert.Equal("account-a", cards.AccountId);
        Assert.Same(task, Assert.Single(cards.Tasks));
        Assert.Same(review, Assert.Single(cards.Pending));
    }

    [Fact]
    public void TasksSortByPriorityThenDateTimeTitleAndId()
    {
        var snapshot = Snapshot(
            Item("p3", priority: 3, due: "2026-10-12"),
            Item("later-date", priority: 0, due: "2026-10-13"),
            Item("later-time", priority: 0, time: "15:00"),
            Item("b", title: "Same", priority: 0, time: "09:00"),
            Item("a", title: "Same", priority: 0, time: "09:00"),
            Item("earliest-title", title: "First", priority: 0, time: "09:00"),
            Item("no-time", priority: 0, time: null),
            Item("no-date", priority: 0, due: null),
            Item("no-priority", priority: null));

        var ids = DesktopCardProjection.Build(snapshot, Connected()).Tasks.Select(item => item.Id);

        Assert.Equal(["earliest-title", "a", "b", "later-time", "no-time", "later-date", "no-date", "p3", "no-priority"], ids);
    }

    [Fact]
    public void PendingReviewsHaveStableOrdering()
    {
        var a = Item("a", "review", "pending", title: "Same", due: null, priority: null);
        var b = a with { Id = "b" };

        Assert.Equal(["a", "b"], DesktopCardProjection.Build(Snapshot(b, a), Connected()).Pending.Select(item => item.Id));
        Assert.Equal(["a", "b"], DesktopCardProjection.Build(Snapshot(a, b), Connected()).Pending.Select(item => item.Id));
    }

    [Theory]
    [InlineData("connected", "online", "active", null, "QQ 已连接，正在接收消息")]
    [InlineData("connecting", "unknown", "paused", null, "正在连接 QQ")]
    [InlineData("disconnected", "unknown", "paused", "connection-failed", "QQ 连接断开，正在重连")]
    [InlineData("connected", "offline", "paused", "qq-offline", "QQ 未登录，已暂停接收")]
    [InlineData("connected", "online", "paused", "pinned-state-invalid", "QQ 消息接收已暂停")]
    [InlineData("disconnected", "unknown", "paused", "account-mismatch", "QQ 账号不匹配，已暂停接收")]
    public void ConnectionStateIsPlainChineseAndCarriesHostError(string transport, string qq,
        string receiving, string? error, string expected)
    {
        var status = new MessageHostStatus(transport, qq, receiving, "complete", error, Now, []);

        var cards = DesktopCardProjection.Build(Snapshot(), status);

        Assert.Equal(expected, cards.ConnectionText);
        Assert.Equal(error, cards.Error);
    }

    [Fact]
    public void WaitingForAccountDataDiffersFromAnEmptyConnectedAccount()
    {
        var waiting = DesktopCardProjection.Build(null, Connected());
        var empty = DesktopCardProjection.Build(Snapshot(), Connected());

        Assert.Null(waiting.AccountId);
        Assert.Empty(waiting.Tasks);
        Assert.Empty(waiting.Pending);
        Assert.Equal("等待 QQ 账号数据", waiting.ConnectionText);
        Assert.Equal("account-a", empty.AccountId);
        Assert.Equal("QQ 已连接，正在接收消息", empty.ConnectionText);
    }

    [Fact]
    public void DisconnectedCardKeepsPersistedAccountTasksVisible()
    {
        var task = Item("saved-task");
        var status = Connected() with { Transport = "disconnected", Receiving = "paused", Error = "connection-failed" };

        var cards = DesktopCardProjection.Build(Snapshot(task), status);

        Assert.Equal("account-a", cards.AccountId);
        Assert.Same(task, Assert.Single(cards.Tasks));
        Assert.Equal("QQ 连接断开，正在重连", cards.ConnectionText);
    }

    [Fact]
    public void ProjectionDoesNotMutateSourceOrderOrDependOnHistoryFailures()
    {
        var original = new[] { Item("b", title: "Same"), Item("a", title: "Same") };
        var snapshot = Snapshot(original);
        var status = Connected() with { History = "partial", HistoryFailures = [new("group-a", "api-failed-1200")] };

        var cards = DesktopCardProjection.Build(snapshot, status);

        Assert.Equal(["b", "a"], snapshot.Items.Select(item => item.Id));
        Assert.Equal(["a", "b"], cards.Tasks.Select(item => item.Id));
        Assert.Equal("QQ 已连接，正在接收消息", cards.ConnectionText);
    }

    private static MessageHostStatus Connected() => new("connected", "online", "active", "complete", null, Now, []);
    private static ApplicationSnapshot Snapshot(params ApplicationItem[] items) => new("account-a", items, [], Now, []);
    private static ApplicationItem Item(string id, string kind = "task", string state = "active", string title = "Task",
        string? due = "2026-10-12", string? time = "12:00", int? priority = 1) =>
        new(id, kind, title, "Course", "Group", state, due, time, "deadline", priority, 1,
            null, null, null, null, [], null, null, [], true, "submit", true);
}
