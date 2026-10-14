using System.Globalization;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;

namespace MessagePurify.Windows.App;

public sealed record DesktopCardsSnapshot(string? AccountId, IReadOnlyList<ApplicationItem> Tasks,
    IReadOnlyList<ApplicationItem> Pending, string ConnectionText, string? Error);

public static class DesktopCardProjection
{
    internal static string RenderSignature(DesktopCardsSnapshot snapshot, string theme, string? operationError,
        string? layoutError, DateTimeOffset now) => JsonSerializer.Serialize(new { theme, operationError,
            snapshot.AccountId, snapshot.ConnectionText, layoutError,
            Tasks = snapshot.Tasks.Select(i => new { i.Id, i.Version, i.Priority, i.Unread, i.ChangeLabel, i.Title, i.Due, i.Time,
                i.TimeKind, i.Course, i.Group, i.Source, Past = IsPast(i, now) }),
            Pending = snapshot.Pending.Select(i => new { i.Id, i.Kind, i.Version, i.Unread, i.ChangeLabel, i.Title,
                i.Course, i.Group, i.Source, i.ReviewReasons, i.MergeSuggestion }) });

    internal static bool IsPast(ApplicationItem item, DateTimeOffset now)
    {
        if (!DateOnly.TryParseExact(item.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        TimeOnly? time = TimeOnly.TryParseExact(item.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock) ? clock : null;
        return new TaskTime(item.TimeKind == "start" ? TimeKind.Start : TimeKind.Deadline, date, time, "Asia/Shanghai").IsPast(now);
    }

    public static DesktopCardsSnapshot Build(ApplicationSnapshot? snapshot, MessageHostStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var items = snapshot?.Items ?? [];
        return new(snapshot?.AccountId,
            Sort(items.Where(item => item.Kind == "task" && item.State == "active")),
            Sort(items.Where(item => item.Kind is "review" or "merge-review" && item.State == "pending")),
            ConnectionText(snapshot, status), status.Error);
    }

    private static ApplicationItem[] Sort(IEnumerable<ApplicationItem> items) => items
        .OrderByDescending(item => item.MessageTime ?? item.Evidence.Max(evidence => evidence.SentAt))
        .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();

    private static string ConnectionText(ApplicationSnapshot? snapshot, MessageHostStatus status)
    {
        if (status.Error == "account-mismatch") return "QQ 账号不匹配，已暂停接收";
        if (status.SyncPhase is not null) return status.SyncPhase switch
        {
            "queued" => "等待同步",
            "starting" => "正在切换 QQ",
            "syncing" => "正在同步置顶群消息",
            "restoring" => "正在切回普通 QQ",
            "deferred" when status.Error == "game-active" => "游戏中，同步已延后",
            "deferred" when status.Error == "game-catalog-unavailable" => "检测名单未就绪，同步已暂停",
            "deferred" => "同步已延后",
            "failed" => "同步失败，可重试",
            "idle" when status.History == "partial" || status.HistoryFailures.Count != 0 => "同步未补全，已切回 QQ",
            "idle" when status.LastSuccessfulSyncAt is not null => "同步完成，已切回 QQ",
            _ => "等待同步"
        };
        if (status.Transport == "connecting") return "正在连接 QQ";
        if (status.Transport != "connected") return "QQ 连接断开，正在重连";
        if (status.Qq != "online") return "QQ 未登录，已暂停接收";
        if (status.Scheduled)
        {
            if (status.Error == "game-active") return "游戏中，同步已延后";
            if (status.Error == "game-catalog-unavailable") return "检测名单未就绪，同步已暂停";
            if (status.Error is not null) return "同步已暂停";
            if (status.History == "reading") return "正在同步置顶群消息";
            if (status.History == "partial" || status.HistoryFailures.Count != 0) return "同步未补全";
            if (status.Receiving == "active") return "正在同步置顶群消息";
            if (status.Receiving == "paused" && status.History is "complete" or "pending") return "等待下次同步";
            return "同步状态待确认";
        }
        if (status.Receiving != "active") return "QQ 消息接收已暂停";
        return snapshot is null ? "等待 QQ 账号数据" : "QQ 已连接，正在接收消息";
    }
}
