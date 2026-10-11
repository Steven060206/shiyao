using System.Text;
using MessagePurify.Core;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.QqProbe;

public sealed record ProbeFailure(string Code, string? Action = null, int? Retcode = null, int? GroupIndex = null);
public sealed record HistoryGroupResult(int GroupIndex, int PagesRead, int UniqueMessages, string StopReason);

public sealed class ProbeReport
{
    private readonly object sync = new();
    private readonly HashSet<MessageKey> messageKeys = [];
    private readonly Dictionary<string, int> segmentCounts = new(StringComparer.Ordinal);
    private readonly List<ProbeFailure> failures = [];
    private readonly List<HistoryGroupResult> historyResults = [];
    private static readonly HashSet<string> knownSegments = new(["text", "image", "reply", "at", "face", "mface", "file", "record", "video", "forward", "node", "json", "xml", "markdown", "music", "poke", "dice", "rps", "contact", "location", "onlinefile", "flashtransfer"]);
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public bool TransportConnected { get; internal set; }
    public bool? QqOnline { get; internal set; }
    public bool AccountVerified { get; internal set; }
    public OneBotVersion? Version { get; internal set; }
    public int GroupCount { get; internal set; }
    public int HistoryGroupsAttempted { get; internal set; }
    public int HistoryGroupsSucceeded { get; internal set; }
    public int HistoryDays { get; internal set; }
    public int HistoryPageLimit { get; internal set; } = 1;
    public int HistoryRetries { get; internal set; }
    public IReadOnlyList<HistoryGroupResult> HistoryResults => historyResults;
    public int HistoryMessages { get; private set; }
    public int RealtimeGroupMessages { get; private set; }
    public int PrivateEventsIgnored { get; internal set; }
    public int OtherEvents { get; internal set; }
    public int UniqueMessages => messageKeys.Count;
    public bool Success => TransportConnected && QqOnline == true && AccountVerified && failures.Count == 0;
    public IReadOnlyList<ProbeFailure> Failures => failures;

    internal void Record(ParsedOneBotMessage message, bool history)
    {
        lock (sync)
        {
            if (history) HistoryMessages++; else RealtimeGroupMessages++;
            if (!messageKeys.Add(message.Message.Key)) return;
            foreach (var segment in message.ProtocolSegments)
            {
                var type = knownSegments.Contains(segment.Type) ? segment.Type : "unknown";
                segmentCounts[type] = segmentCounts.GetValueOrDefault(type) + 1;
            }
        }
    }

    internal void Fail(ProbeFailure failure) { lock (sync) failures.Add(failure); }
    internal void FinishHistoryGroup(HistoryGroupResult result)
    {
        historyResults.Add(result);
        if (result.StopReason is "sampled" or "empty-page" or "date-boundary") HistoryGroupsSucceeded++;
    }

    public string ToMarkdown()
    {
        var output = new StringBuilder();
        output.AppendLine("# OneBot 只读集成探针");
        output.AppendLine();
        output.AppendLine($"时间（UTC）：{StartedAt:O}");
        output.AppendLine($"结果：{(Success ? "本次限定范围通过" : "存在失败或依赖未就绪")}");
        output.AppendLine();
        output.AppendLine("| 项目 | 实际结果 |");
        output.AppendLine("| --- | --- |");
        output.AppendLine($"| WebSocket | {(TransportConnected ? "已建立" : "未建立")} |");
        output.AppendLine($"| QQ 状态 | {(QqOnline.HasValue ? QqOnline.Value ? "在线" : "离线" : "未知")} |");
        output.AppendLine($"| 账号 | {(AccountVerified ? "已读取并核对，标识已脱敏" : "未核对")} |");
        output.AppendLine($"| NapCat / 协议版本 | {SafeVersion(Version?.AppVersion)} / {SafeVersion(Version?.ProtocolVersion)} |");
        output.AppendLine($"| 群总数 | {GroupCount} |");
        output.AppendLine($"| 历史群成功 / 尝试 | {HistoryGroupsSucceeded} / {HistoryGroupsAttempted} |");
        output.AppendLine($"| 历史窗口 / 每群页上限 / 重试次数 | {(HistoryDays == 0 ? "未限定天数" : HistoryDays + " 天")} / {HistoryPageLimit} / {HistoryRetries} |");
        output.AppendLine($"| 历史消息 / 实时群消息 | {HistoryMessages} / {RealtimeGroupMessages} |");
        output.AppendLine($"| 去重后的消息 | {UniqueMessages} |");
        output.AppendLine($"| 已排除私聊事件 / 其他事件 | {PrivateEventsIgnored} / {OtherEvents} |");
        output.AppendLine();
        output.AppendLine("## 本次读取的群历史范围");
        output.AppendLine();
        output.AppendLine("群序号仅对应本次群列表顺序，不是群 ID；消息数为窗口内去重后的历史消息。短页不视为读取结束。");
        output.AppendLine();
        output.AppendLine("| 群序号 | 成功读取页数 | 窗口内消息 | 停止原因 |");
        output.AppendLine("| --- | --- | --- | --- |");
        foreach (var result in historyResults)
            output.AppendLine($"| {result.GroupIndex} | {result.PagesRead} | {result.UniqueMessages} | {result.StopReason} |");
        output.AppendLine();
        output.AppendLine("## 去重后的消息段统计");
        output.AppendLine();
        if (segmentCounts.Count == 0) output.AppendLine("没有可统计的消息段。");
        else foreach (var entry in segmentCounts.OrderBy(entry => entry.Key)) output.AppendLine($"- {entry.Key}: {entry.Value}");
        output.AppendLine();
        output.AppendLine("## 失败记录");
        output.AppendLine();
        if (failures.Count == 0) output.AppendLine("无。");
        else foreach (var failure in failures)
            output.AppendLine($"- {failure.Code}{(failure.Action is null ? "" : "; action=" + failure.Action)}{(failure.Retcode is null ? "" : "; retcode=" + failure.Retcode)}{(failure.GroupIndex is null ? "" : "; group-index=" + failure.GroupIndex)}");
        output.AppendLine();
        if (HistoryDays == 0 && HistoryPageLimit == 1)
            output.AppendLine("范围：只读接口和限定时间内事件统计；每个选择的群只读一页。未验证七天历史完整性、历史分页、断线补漏、正式持久化或任务生成。");
        else
            output.AppendLine("范围：只读接口与本次实际返回的分页历史；date-boundary 表示返回消息已越过窗口边界，empty-page 表示接口成功返回空页，均不保证 QQ 提供了完整历史。history-page-limit、history-no-progress、游标不足或接口失败表示该群读取未完成；1200 不等同于空页。未验证断线补漏、正式持久化或任务生成。");
        output.AppendLine("历史消息计数包含页面重叠，去重数量另列。没有群名、聊天正文、账号号、历史游标或 Token。没有观察到群事件时，不能据此认定实时接收已验收。");
        return output.ToString();
    }

    private static string SafeVersion(string? version) => version is { Length: > 0 and <= 40 }
        && version.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
        ? version : "未知";
}
