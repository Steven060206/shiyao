using MessagePurify.Infrastructure.Application;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class DesktopCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void ManualTasksUseTheSameCardRulesAndKeepTheirSource()
    {
        var task = Item("manual-task", due: "2026-10-13", time: null) with
        {
            Source = "manual", Course = "口头事项", Group = "手动添加", Evidence = []
        };
        var cards = DesktopCardProjection.Build(Snapshot(task), Connected());
        Assert.Same(task, Assert.Single(cards.Tasks));
        Assert.Equal("manual", cards.Tasks[0].Source);
        Assert.Empty(cards.Tasks[0].Evidence);
        Assert.False(DesktopCardProjection.IsPast(task, Now));
        Assert.True(DesktopCardProjection.IsPast(task, Now.AddDays(2)));
        Assert.Empty(DesktopCardProjection.Build(Snapshot(task with { State = "completed" }), Connected()).Tasks);
        Assert.NotEqual(DesktopCardProjection.RenderSignature(cards, "dark", null, null, Now),
            DesktopCardProjection.RenderSignature(DesktopCardProjection.Build(Snapshot(task with { Source = "qq" }), Connected()),
                "dark", null, null, Now));
    }

    [Theory]
    [InlineData("task", "active")]
    [InlineData("review", "pending")]
    public void ChangeLabelAloneRefreshesTheCardWithoutChangingTaskVersion(string kind, string state)
    {
        var item = Item("changed", kind, state);
        var original = DesktopCardProjection.Build(Snapshot(item), Connected());
        var changed = DesktopCardProjection.Build(Snapshot(item with { ChangeLabel = "待确认变更" }), Connected());
        var cleared = DesktopCardProjection.Build(Snapshot(item with { ChangeLabel = null }), Connected());

        Assert.NotEqual(DesktopCardProjection.RenderSignature(original, "dark", null, null, Now),
            DesktopCardProjection.RenderSignature(changed, "dark", null, null, Now));
        Assert.Equal(DesktopCardProjection.RenderSignature(original, "dark", null, null, Now),
            DesktopCardProjection.RenderSignature(cleared, "dark", null, null, Now));
        Assert.Equal(item.Version, kind == "task" ? changed.Tasks.Single().Version : changed.Pending.Single().Version);
    }

    [Fact]
    public void ReadChangeLabelsAndHistoryDoNotChangeCardFilteringOrSourceData()
    {
        var task = Item("new-requirement") with { ChangeLabel = "新要求", Unread = true };
        var cancelled = Item("cancelled", state: "cancelled") with { ChangeLabel = "已取消" };
        var cards = DesktopCardProjection.Build(Snapshot(task, cancelled), Connected());

        Assert.Same(task, Assert.Single(cards.Tasks));
        Assert.Equal("新要求", cards.Tasks.Single().ChangeLabel);
        Assert.Equal("已取消", cancelled.ChangeLabel);
        Assert.Empty(cards.Pending);
    }

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
    public void CrossGroupMergeSuggestionsJoinPendingWithoutAddingReminderTasks()
    {
        var first = Item("first-task");
        var second = Item("second-task") with { Group = "另一课程群" };
        var review = Item("date-review", "review", "pending");
        var suggestion = Item("merge-suggestion", "merge-review", "pending", title: "可能重复：Task") with
        {
            Group = "课程群 / 另一课程群", ReviewReasons = ["两个已确认课程群中的原文一致，请核对后合并。"]
        };

        var cards = DesktopCardProjection.Build(Snapshot(first, second, review, suggestion), Connected());

        Assert.Equal(["first-task", "second-task"], cards.Tasks.Select(item => item.Id));
        Assert.Equal(2, cards.Pending.Count);
        Assert.Contains(review, cards.Pending);
        Assert.Same(suggestion, cards.Pending.Single(item => item.Id == suggestion.Id));
        Assert.Equal("merge-review", cards.Pending.Single(item => item.Id == suggestion.Id).Kind);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("ignored")]
    [InlineData("resolved")]
    public void ProcessedMergeSuggestionsLeaveThePendingCard(string state)
    {
        var first = Item("first-task");
        var second = Item("second-task");
        var suggestion = Item("merge-suggestion", "merge-review", "pending");
        var pending = DesktopCardProjection.Build(Snapshot(first, second, suggestion), Connected());
        var processed = DesktopCardProjection.Build(Snapshot(first, second, suggestion with { State = state }), Connected());

        Assert.Same(suggestion, Assert.Single(pending.Pending));
        Assert.Empty(processed.Pending);
        Assert.Equal(pending.Tasks, processed.Tasks);
        Assert.NotEqual(DesktopCardProjection.RenderSignature(pending, "dark", null, null, Now),
            DesktopCardProjection.RenderSignature(processed, "dark", null, null, Now));
    }

    [Theory]
    [InlineData("course")]
    [InlineData("group")]
    [InlineData("reasons")]
    public void MergeSuggestionDetailsRefreshThePendingCard(string field)
    {
        var suggestion = Item("merge-suggestion", "merge-review", "pending");
        var changed = field switch
        {
            "course" => suggestion with { Course = "数学" },
            "group" => suggestion with { Group = "课程群 / 另一课程群" },
            _ => suggestion with { ReviewReasons = ["当前正式时间不同，核对双方原文。"] }
        };
        var original = new DesktopCardsSnapshot("account-a", [], [suggestion], "已连接", null);
        var updated = original with { Pending = [changed] };

        Assert.NotEqual(DesktopCardProjection.RenderSignature(original, "dark", null, null, Now),
            DesktopCardProjection.RenderSignature(updated, "dark", null, null, Now));
        Assert.Equal(suggestion.Version, changed.Version);
    }

    [Theory]
    [InlineData("first-id")]
    [InlineData("first-version")]
    [InlineData("second-id")]
    [InlineData("second-version")]
    public void MergeSuggestionPairChangesRefreshThePendingCard(string field)
    {
        var pair = new ApplicationMergeSuggestion("first-task", 2, "second-task", 3);
        var item = Item("merge-suggestion", "merge-review", "pending") with { MergeSuggestion = pair };
        var changedPair = field switch
        {
            "first-id" => pair with { FirstTaskId = "other-first" },
            "first-version" => pair with { FirstTaskVersion = 4 },
            "second-id" => pair with { SecondTaskId = "other-second" },
            _ => pair with { SecondTaskVersion = 5 }
        };
        var original = new DesktopCardsSnapshot("account-a", [], [item], "已连接", null);
        var changed = original with { Pending = [item with { MergeSuggestion = changedPair }] };

        Assert.NotEqual(DesktopCardProjection.RenderSignature(original, "dark", null, null, Now),
            DesktopCardProjection.RenderSignature(changed, "dark", null, null, Now));
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

    [Theory]
    [InlineData("connected", "online", "paused", "complete", null, "等待下次同步")]
    [InlineData("connected", "online", "paused", "pending", null, "等待下次同步")]
    [InlineData("connected", "online", "active", "reading", null, "正在同步置顶群消息")]
    [InlineData("connected", "online", "paused", "partial", null, "同步未补全")]
    [InlineData("connected", "online", "paused", "partial", "game-active", "游戏中，同步已延后")]
    [InlineData("connected", "online", "paused", "complete", "game-catalog-unavailable", "检测名单未就绪，同步已暂停")]
    [InlineData("connected", "online", "paused", "complete", "pinned-state-unavailable", "同步已暂停")]
    [InlineData("connecting", "unknown", "paused", "complete", null, "正在连接 QQ")]
    [InlineData("disconnected", "unknown", "paused", "complete", "connection-failed", "QQ 连接断开，正在重连")]
    [InlineData("connected", "offline", "paused", "complete", "qq-offline", "QQ 未登录，已暂停接收")]
    [InlineData("connected", "online", "paused", "complete", "account-mismatch", "QQ 账号不匹配，已暂停接收")]
    public void ScheduledCardsDistinguishWaitingSyncingGameAndFailures(string transport, string qq,
        string receiving, string history, string? error, string expected)
    {
        var task = Item("persisted");
        var status = new MessageHostStatus(transport, qq, receiving, history, error, Now, []) { Scheduled = true };

        var cards = DesktopCardProjection.Build(Snapshot(task), status);

        Assert.Equal(expected, cards.ConnectionText);
        Assert.Equal(error, cards.Error);
        Assert.Same(task, Assert.Single(cards.Tasks));
    }

    [Fact]
    public void ScheduledHistoryFailuresRemainIncompleteUntilNewBatchStartsReading()
    {
        var status = Connected() with { Scheduled = true, Receiving = "paused", HistoryFailures = [new("group-a", "history-timeout")] };

        Assert.Equal("同步未补全", DesktopCardProjection.Build(Snapshot(), status).ConnectionText);
        Assert.Single(status.HistoryFailures);
        Assert.Equal("正在同步置顶群消息", DesktopCardProjection.Build(Snapshot(), status with { Receiving = "active", History = "reading" }).ConnectionText);
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
