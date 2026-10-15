using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePurify.Core;

namespace MessagePurify.RuleBaseline;

// Shared by xUnit and the standalone CLI; this file contains no classification rules.
public static class RuleFixtureRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static FixtureCorpus Load(string path)
    {
        var corpus = JsonSerializer.Deserialize<FixtureCorpus>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("样本文件为空。");
        if (!corpus.Synthetic || corpus.Cases.Count == 0)
            throw new InvalidDataException("本工具只运行明确标记 synthetic=true 的非空合成样本。");
        if (corpus.Cases.Any(c => string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Category)) ||
            corpus.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != corpus.Cases.Count)
            throw new InvalidDataException("样本 ID 必须唯一，ID 和类别不能为空。");
        foreach (var item in corpus.Cases)
        foreach (var expected in item.ExpectedCandidates)
        {
            if ((expected.Date is null) != (expected.TimeKind is null) ||
                (expected.SuggestedDate is null) != (expected.SuggestedTimeKind is null) ||
                (expected.Date is null && expected.Clock is not null) ||
                (expected.SuggestedDate is null && expected.SuggestedClock is not null))
                throw new InvalidDataException($"{item.Id}: gold 日期、钟点和时间类型不一致。");
            _ = expected.ToTime(false, item.Context.TimeZoneId);
            _ = expected.ToTime(true, item.Context.TimeZoneId);
        }
        return corpus;
    }

    public static FixtureReport Run(string path) => Evaluate(Load(path));

    public static FixtureReport Evaluate(FixtureCorpus corpus,
        Func<SchoolMessage, RuleContext, RuleExtractionResult>? extract = null)
    {
        extract ??= MessageRuleExtractor.Extract;
        var results = new List<FixtureCaseResult>();
        foreach (var item in corpus.Cases)
        {
            var message = BuildMessage(item);
            var context = BuildContext(item.Context);
            try
            {
                var result = extract(message, context);
                // Ignored candidates are diagnostics, not independently actionable business candidates.
                var actual = result.Candidates.Where(c => c.Disposition != RuleDisposition.Ignored).ToArray();
                var errors = new List<string>();
                if (string.IsNullOrWhiteSpace(result.RuleVersion)) errors.Add("缺少规则版本。");
                if (actual.Length != item.ExpectedCandidates.Count)
                    errors.Add($"业务候选数量：期待 {item.ExpectedCandidates.Count}，实际 {actual.Length}。");
                for (var i = 0; i < actual.Length; i++)
                {
                    if (i >= item.ExpectedCandidates.Count || !Exact(item.ExpectedCandidates[i], actual[i], context.TimeZoneId))
                        errors.Add($"候选 {i + 1}：期待 {Describe(i < item.ExpectedCandidates.Count ? item.ExpectedCandidates[i] : null)}；实际 {Describe(actual[i])}。");
                    if (!HasAssessmentEvidence(actual[i]) || !actual[i].Evidence.Contains(message.Key))
                        errors.Add($"候选 {i + 1} 缺少六类判定依据或本条消息证据。");
                    if (actual[i].Disposition == RuleDisposition.Pending && actual[i].ReviewReasons.Count == 0)
                        errors.Add($"候选 {i + 1} 待确认缺少具体原因。");
                }
                results.Add(new(item, message.Key, actual, errors, result.RuleVersion));
            }
            catch (Exception exception)
            {
                results.Add(new(item, message.Key, [], [$"提取异常：{exception.GetType().Name}: {exception.Message}"]));
            }
        }
        return new FixtureReport(corpus, results);
    }

    internal static bool Exact(ExpectedCandidate expected, RuleCandidate actual, string zone) =>
        expected.Action == actual.Action && expected.Disposition == actual.Disposition &&
        expected.Course == actual.Course && expected.Target == actual.TargetTaskId &&
        expected.ToTime(false, zone) == actual.Time && expected.ToTime(true, zone) == actual.SuggestedTime;

    internal static bool Recalled(ExpectedCandidate expected, RuleCandidate actual, string zone)
    {
        if (actual.Disposition is not (RuleDisposition.Task or RuleDisposition.Pending or RuleDisposition.Change) ||
            expected.Action != actual.Action) return false;
        if (actual.Disposition != RuleDisposition.Pending) return Exact(expected, actual, zone);
        // Pending may omit uncertain fields, but may not invent a different date, course or target.
        return (actual.Course is null || actual.Course == expected.Course) &&
            (actual.TargetTaskId is null || actual.TargetTaskId == expected.Target) &&
            (actual.Time is null || actual.Time == expected.ToTime(false, zone)) &&
            (actual.SuggestedTime is null || actual.SuggestedTime == expected.ToTime(true, zone));
    }

    private static bool HasAssessmentEvidence(RuleCandidate candidate)
    {
        var axes = candidate.Assessments;
        return new[] { axes.Action, axes.Audience, axes.Course, axes.Time, axes.Source, axes.Association }
            .All(a => a is not null && !string.IsNullOrWhiteSpace(a.Description));
    }

    public static SchoolMessage BuildMessage(FixtureCase item)
    {
        var input = item.Message;
        var key = new MessageKey(input.AccountId, input.GroupId, input.MessageId ?? item.Id);
        var segments = input.Segments is { } inputs
            ? inputs.Select(s => new MessageSegment(s.Kind, s.Text, s.Reference?.ToKey(), s.AssetId)).ToArray()
            : [new MessageSegment(SegmentKind.Text, input.Text ?? "")];
        return new SchoolMessage(key, input.SenderId, ParseInstant(input.SentAt), segments, input.IsPrivate);
    }

    public static RuleContext BuildContext(ContextInput input) => new(input.AccountId, input.GroupId,
        input.TimeZoneId, ParseInstant(input.Now), input.ConfirmedCourse,
        input.TrustedSenders.ToHashSet(StringComparer.Ordinal), input.ExcludedSenders.ToHashSet(StringComparer.Ordinal),
        input.CourseExcluded, input.KnownTasks.Select(t => new KnownTask(t.Id, t.Title, t.Course,
            new TaskTime(t.TimeKind, ParseDate(t.Date), ParseClock(t.Clock), input.TimeZoneId),
            t.State, t.Evidence.Select(e => e.ToKey()).ToArray(), t.ConfirmedPublisherId)).ToArray());

    private static DateTimeOffset ParseInstant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    internal static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    internal static TimeOnly? ParseClock(string? value) => value is null ? null : TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    internal static string Describe(ExpectedCandidate? expected) => expected is null ? "无候选" :
        $"{expected.Action}/{expected.Disposition}，课程={expected.Course ?? "无"}，正式={expected.TimeKind}:{expected.Date ?? "无"} {expected.Clock ?? "仅日期/无"}，建议={expected.SuggestedTimeKind}:{expected.SuggestedDate ?? "无"} {expected.SuggestedClock ?? "仅日期/无"}，目标={expected.Target ?? "无"}";

    internal static string Describe(RuleCandidate actual) =>
        $"{actual.Action}/{actual.Disposition}，课程={actual.Course ?? "无"}，正式={DescribeTime(actual.Time)}，建议={DescribeTime(actual.SuggestedTime)}，目标={actual.TargetTaskId ?? "无"}";

    private static string DescribeTime(TaskTime? time) => time is null ? "无" :
        $"{time.Kind}:{time.Date:yyyy-MM-dd} {time.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "仅日期"}";
}

public sealed class FixtureCorpus
{
    public bool Synthetic { get; set; }
    public string Description { get; set; } = "";
    public List<FixtureCase> Cases { get; set; } = [];
}

public sealed class FixtureCase
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public MessageInput Message { get; set; } = new();
    public ContextInput Context { get; set; } = new();
    public List<ExpectedCandidate> ExpectedCandidates { get; set; } = [];
}

public sealed class MessageInput
{
    public string AccountId { get; set; } = "a";
    public string GroupId { get; set; } = "g";
    public string? MessageId { get; set; }
    public string SenderId { get; set; } = "teacher";
    public string SentAt { get; set; } = "2026-10-10T10:00:00+08:00";
    public string? Text { get; set; }
    public List<SegmentInput>? Segments { get; set; }
    public bool IsPrivate { get; set; }
}

public sealed class SegmentInput
{
    public SegmentKind Kind { get; set; }
    public string Text { get; set; } = "";
    public KeyInput? Reference { get; set; }
    public string? AssetId { get; set; }
}

public sealed class KeyInput
{
    public string AccountId { get; set; } = "a";
    public string GroupId { get; set; } = "g";
    public string MessageId { get; set; } = "";
    public MessageKey ToKey() => new(AccountId, GroupId, MessageId);
}

public sealed class ContextInput
{
    public string AccountId { get; set; } = "a";
    public string GroupId { get; set; } = "g";
    public string TimeZoneId { get; set; } = "China Standard Time";
    public string Now { get; set; } = "2026-10-10T10:00:00+08:00";
    public string? ConfirmedCourse { get; set; } = "高等数学";
    public string[] TrustedSenders { get; set; } = ["teacher"];
    public string[] ExcludedSenders { get; set; } = [];
    public bool CourseExcluded { get; set; }
    public List<KnownTaskInput> KnownTasks { get; set; } = [];
}

public sealed class KnownTaskInput
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Course { get; set; } = "高等数学";
    public string Date { get; set; } = "2026-10-11";
    public string? Clock { get; set; }
    public TimeKind TimeKind { get; set; } = TimeKind.Deadline;
    public TaskState State { get; set; } = TaskState.Active;
    public List<KeyInput> Evidence { get; set; } = [];
    public string? ConfirmedPublisherId { get; set; }
}

public sealed class ExpectedCandidate
{
    public bool RequiresAction { get; set; }
    public RuleAction Action { get; set; }
    public RuleDisposition Disposition { get; set; }
    public string? Course { get; set; }
    public string? Date { get; set; }
    public string? Clock { get; set; }
    public TimeKind? TimeKind { get; set; }
    public string? SuggestedDate { get; set; }
    public string? SuggestedClock { get; set; }
    public TimeKind? SuggestedTimeKind { get; set; }
    public string? Target { get; set; }

    internal TaskTime? ToTime(bool suggested, string zone)
    {
        var date = suggested ? SuggestedDate : Date;
        var clock = suggested ? SuggestedClock : Clock;
        var kind = suggested ? SuggestedTimeKind : TimeKind;
        return date is null ? null : new TaskTime(kind ?? throw new InvalidDataException("期待时间缺少类型。"),
            RuleFixtureRunner.ParseDate(date), RuleFixtureRunner.ParseClock(clock), zone);
    }
}

public sealed record FixtureCaseResult(FixtureCase Case, MessageKey Key,
    IReadOnlyList<RuleCandidate> Actual, IReadOnlyList<string> Errors, string RuleVersion = "");

public sealed record FixtureMetric(int Numerator, int Denominator)
{
    public string Display => Denominator == 0 ? "N/A (0/0)" :
        $"{Numerator}/{Denominator} ({(100d * Numerator / Denominator).ToString("0.0", CultureInfo.InvariantCulture)}%)";
}

public sealed class FixtureReport
{
    public FixtureCorpus Corpus { get; }
    public IReadOnlyList<FixtureCaseResult> Results { get; }
    public bool Success => Results.All(r => r.Errors.Count == 0);
    public FixtureMetric FormalPrecision { get; }
    public FixtureMetric ActionRecall { get; }
    public FixtureMetric AutomaticCoverage { get; }
    public FixtureMetric PendingShare { get; }
    public IReadOnlyList<string> HighImpactErrors { get; }

    public FixtureReport(FixtureCorpus corpus, IReadOnlyList<FixtureCaseResult> results)
    {
        Corpus = corpus;
        Results = results;
        var formalCount = 0;
        var correctFormal = 0;
        var needsAction = 0;
        var recalled = 0;
        var automatic = 0;
        var pending = 0;
        var candidateCount = 0;
        var highImpact = new List<string>();
        foreach (var result in results)
        {
            var zone = result.Case.Context.TimeZoneId;
            candidateCount += result.Actual.Count;
            pending += result.Actual.Count(a => a.Disposition == RuleDisposition.Pending);
            for (var i = 0; i < result.Actual.Count; i++)
            {
                var actual = result.Actual[i];
                var gold = i < result.Case.ExpectedCandidates.Count ? result.Case.ExpectedCandidates[i] : null;
                var exact = gold is not null && RuleFixtureRunner.Exact(gold, actual, zone);
                if (actual.Disposition == RuleDisposition.Task)
                {
                    formalCount++;
                    if (exact && gold!.RequiresAction) correctFormal++;
                }
                if (actual.Disposition is RuleDisposition.Task or RuleDisposition.Change)
                {
                    var unsafeChange = actual.Disposition == RuleDisposition.Change && !exact;
                    var unsafeTask = actual.Disposition == RuleDisposition.Task && (gold is null ||
                        !gold.RequiresAction || gold.Disposition != RuleDisposition.Task || gold.Course != actual.Course ||
                        gold.Target != actual.TargetTaskId || gold.ToTime(false, zone) != actual.Time);
                    if (unsafeChange || unsafeTask)
                        highImpact.Add($"{result.Case.Id} #{i + 1}: 自动日期、对象、目标或变更与 gold 不符；{RuleFixtureRunner.Describe(actual)}");
                }
            }
            for (var i = 0; i < result.Case.ExpectedCandidates.Count; i++)
            {
                var gold = result.Case.ExpectedCandidates[i];
                if (!gold.RequiresAction) continue;
                needsAction++;
                if (i >= result.Actual.Count) continue;
                var actual = result.Actual[i];
                if (RuleFixtureRunner.Recalled(gold, actual, zone)) recalled++;
                if (actual.Disposition is RuleDisposition.Task or RuleDisposition.Change &&
                    RuleFixtureRunner.Exact(gold, actual, zone)) automatic++;
            }
        }
        FormalPrecision = new(correctFormal, formalCount);
        ActionRecall = new(recalled, needsAction);
        AutomaticCoverage = new(automatic, needsAction);
        PendingShare = new(pending, candidateCount);
        HighImpactErrors = highImpact;
    }

    public string ToMarkdown()
    {
        var text = new StringBuilder();
        text.AppendLine("# M2 本地规则合成样本基线").AppendLine();
        text.AppendLine(Corpus.Description).AppendLine();
        text.AppendLine($"规则版本：{string.Join(", ", Results.Select(r => r.RuleVersion).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal))}。").AppendLine();
        text.AppendLine("**范围：合成调试回归，尚未运行独立真实 QQ 群消息验收。** 默认评估时刻为北京时间 2026-10-10 10:00；每条相对日期以样本原发送时刻解释。无网络、AI、QQ 或用户状态写入。").AppendLine();
        text.AppendLine($"样本：{Results.Count}；完整 gold 匹配：{Results.Count(r => r.Errors.Count == 0)}/{Results.Count}；失败：{Results.Count(r => r.Errors.Count > 0)}。").AppendLine();
        text.AppendLine("| 指标 | 分子/分母（比例） | 分母定义 |").AppendLine("| --- | --- | --- |");
        text.AppendLine($"| 正式任务准确率 | {FormalPrecision.Display} | 实际自动 Task 候选；行动、去向、课程、正式/建议时间及目标全部匹配才正确 |");
        text.AppendLine($"| 需本人行动召回（含正确待确认） | {ActionRecall.Display} | gold RequiresAction=true；Pending 已填字段不得错误 |");
        text.AppendLine($"| 直接自动覆盖率 | {AutomaticCoverage.Display} | gold RequiresAction=true；只有完整匹配的 Task/Change 自动候选计入分子 |");
        text.AppendLine($"| 待确认占比 | {PendingShare.Display} | 实际业务候选，排除 Ignored 诊断 |");
        text.AppendLine($"| 高影响错误 | {HighImpactErrors.Count} | 自动 Task 的错误日期/对象/目标，及不完全匹配 gold 的自动 Change |").AppendLine();
        text.AppendLine("完整匹配按候选顺序检查；不按标题猜测目标，不自动合并跨群副本。召回与自动覆盖分开统计，全部转为待确认时不会得到自动覆盖。0 分母记 N/A。上述数字不能替代真实样本的 95% 准确率/召回率及高影响零错误验收，也不能估计每日待确认数量或处理时间。").AppendLine();
        text.AppendLine("## 适用表达与边界").AppendLine();
        text.AppendLine("- 日期支持 YYYY-MM-DD、YYYY/MM/DD、YYYY年M月D日、M月D日、M/D、今天/明天/后天、本周/这周/下周星期和下月明确日，以及阿拉伯数字钟点；相对表达始终使用原发送时刻及时区，无年份绝对日期使用发送年，仅日期不虚构钟点。");
        text.AppendLine("- 分析提交、完成、考试/参加、报名、催交、延期、取消和明确新要求；一条原消息最多一个完整业务候选，保留完整原文。多行动统一待确认（全部为普通通知时保留通知），不同日期或 Deadline/Start 角色不自动选定，需独立管理时由用户手动拆分。");
        text.AppendLine("- 正式候选依赖本账号、本群已确认的课程和对象；高影响变更还需本群可信发布者或已确认原发布者的明确引用续发，以及唯一目标。自愿报名/参加为通知。");
        text.AppendLine("- 模糊下周、今晚无钟点、未覆盖的中文钟点、冲突/非法日期、限定对象、未确认课程及来源/目标不足进入待确认。OCR 时间只作建议，旧引用不独立形成新行动。");
        text.AppendLine("- 复杂省略、同一动词多个交付物和跨句指代仍需真实样本增强，规则不能理解任意自然语言。本报告未验收真实课程名单提出、真实 OCR/QQ 接入、任务状态提交或通知投递；M2 只返回候选，不改用户状态。").AppendLine();
        text.AppendLine("## 类别数量").AppendLine();
        text.AppendLine("| 类别 | 样本数 | 失败数 |").AppendLine("| --- | --- | --- |");
        foreach (var group in Results.GroupBy(r => r.Case.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
            text.AppendLine($"| {Escape(group.Key)} | {group.Count()} | {group.Count(r => r.Errors.Count > 0)} |");
        text.AppendLine().AppendLine("## 按账号/群统计").AppendLine();
        text.AppendLine("| 账号/群 | 样本数 | 自动任务误报 | 需行动漏召回 |").AppendLine("| --- | --- | --- | --- |");
        foreach (var group in Results.GroupBy(r => $"{r.Key.AccountId}/{r.Key.GroupId}").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var falsePositive = 0;
            var missed = 0;
            foreach (var result in group)
            {
                var zone = result.Case.Context.TimeZoneId;
                for (var i = 0; i < result.Actual.Count; i++)
                    if (result.Actual[i].Disposition == RuleDisposition.Task && (i >= result.Case.ExpectedCandidates.Count ||
                        !result.Case.ExpectedCandidates[i].RequiresAction ||
                        !RuleFixtureRunner.Exact(result.Case.ExpectedCandidates[i], result.Actual[i], zone))) falsePositive++;
                for (var i = 0; i < result.Case.ExpectedCandidates.Count; i++)
                    if (result.Case.ExpectedCandidates[i].RequiresAction && (i >= result.Actual.Count ||
                        !RuleFixtureRunner.Recalled(result.Case.ExpectedCandidates[i], result.Actual[i], zone))) missed++;
            }
            text.AppendLine($"| {Escape(group.Key)} | {group.Count()} | {falsePositive} | {missed} |");
        }
        text.AppendLine().AppendLine("## 高影响错误").AppendLine();
        if (HighImpactErrors.Count == 0) text.AppendLine("本次合成样本中未发现高影响错误；不代表真实独立样本已验收。");
        foreach (var error in HighImpactErrors) text.AppendLine($"- {Escape(error)}");
        text.AppendLine().AppendLine("## 失败例子").AppendLine();
        if (Success) text.AppendLine("无。所有合成样本完整匹配手写 gold。");
        foreach (var result in Results.Where(r => r.Errors.Count > 0))
        {
            text.AppendLine($"### {Escape(result.Case.Id)} ({Escape(result.Case.Category)})").AppendLine();
            var source = result.Case.Message.Text ?? string.Join(" / ", result.Case.Message.Segments?.Select(s => $"{s.Kind}: {s.Text}") ?? []);
            text.AppendLine($"原文：{Escape(source)}").AppendLine();
            foreach (var error in result.Errors) text.AppendLine($"- {Escape(error)}");
            text.AppendLine();
        }
        return text.ToString();
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
