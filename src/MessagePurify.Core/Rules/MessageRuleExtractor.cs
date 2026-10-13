using System.Text.RegularExpressions;

namespace MessagePurify.Core;

/// <summary>只生成可审查的候选，不改变正式事项、状态或通知。</summary>
public static class MessageRuleExtractor
{
    public const string RuleVersion = "m2-local-v1";

    public static RuleExtractionResult Extract(SchoolMessage message, RuleContext context)
    {
        message = MessageNormalizer.Normalize(message);
        var text = string.Join("\n", message.Segments.Where(s => s.Kind == SegmentKind.Text).Select(s => s.Text));
        var imageText = string.Join("\n", message.Segments.Where(s => s.Kind == SegmentKind.Image).Select(s => s.Text));
        var evidence = new[] { message.Key }.Concat(message.Segments
            .Where(s => s.Kind == SegmentKind.Quote && s.Reference is not null)
            .Select(s => s.Reference!)).Distinct().ToArray();

        if (message.IsPrivate) return Diagnostic("私聊不在学校群消息范围", RuleDisposition.Ignored);
        var contextMismatch = message.Key.AccountId != context.AccountId || message.Key.GroupId != context.GroupId;
        if (contextMismatch) context = context with
        {
            AccountId = message.Key.AccountId, GroupId = message.Key.GroupId,
            ConfirmedCourse = null, TrustedSenders = null, ExcludedSenders = null,
            CourseExcluded = false, KnownTasks = null
        };
        if (context.ExcludedSenders?.Contains(message.SenderId) == true)
            return Diagnostic("发布账号已由用户确认排除", RuleDisposition.Ignored);
        if (context.CourseExcluded) return Diagnostic("课程已由用户确认排除", RuleDisposition.Ignored);

        var imageOnly = !HasAction(text) && HasAction(imageText);
        var content = imageOnly ? imageText : text;
        var parts = SplitActions(content);
        if (parts.Count == 0) return Diagnostic("没有明确的新行动或学校通知，引用仅保留为证据", RuleDisposition.Ignored);
        var candidates = parts.Select(part => ExtractPart(part.Text, imageOnly, part.Conditional)).ToArray();
        return new(RuleVersion, candidates);

        RuleExtractionResult Diagnostic(string reason, RuleDisposition disposition)
        {
            var assessment = new RuleAssessment(reason, false);
            return new(RuleVersion, [new(text, RuleAction.Inform, disposition, null,
                null, null, null, null, new(assessment, assessment, assessment, assessment,
                    assessment, assessment), [reason], evidence)]);
        }

        RuleCandidate ExtractPart(string part, bool ocr, bool conditional)
        {
            var action = GetAction(part);
            var optional = Match(part, "自愿|有兴趣|感兴趣|自行报名|自由参加|(?<!不)可以(?:参加|报名)|(?<!不)可(?:参加|报名)");
            var question = Match(part, "[?？]|是不是|是否|能否|要不要|有没有|什么时候|几点|啥时候|[吗么呢](?:$|[,，])");
            var negated = Match(part, "(?:不用|无需|不需要|不要求|不必|不要|禁止)(?:再|继续)?(?:提交|交|报名|参加|完成)|不(?:提交|交作业|参加)|不可以(?:参加|报名)|(?:不|未|没有|不会|不能|不必|不要|无需)(?:再)?(?:取消|延期|延后|推迟|补交|重交|重新提交|新增)|(?:取消|延期|推迟)(?:不了|不掉)");
            var uncertain = conditional || Match(part, "可能|也许|大概|似乎|好像|据说|听说|应该|估计|猜测|传闻|(?:老师|同学|有人|别人)说|转述|如果|假如|假设|倘若|要是|希望(?:取消|延期)|考虑(?:取消|延期)");
            var partialCancel = action == RuleAction.Cancel && Match(part, "取消.*(?:格式|纸质版|电子版|打印|附件)");
            var actionReliable = !ocr && !question && !negated && !uncertain && !partialCancel;
            var courseReliable = !string.IsNullOrWhiteSpace(context.ConfirmedCourse);
            var restricted = Match(part, "(?:[0-9一二三四五六七八九十]+)(?:年级|级|班)|大[一二三四]|高[一二三]|初[一二三]|补考同学|选(?:了|修|课)|已选|未选|仅限|只面向|只限|其他课程|外校|非本课程");
            var audienceReliable = courseReliable && !restricted;
            var change = action is RuleAction.Cancel or RuleAction.Postpone or RuleAction.NewRequirement or RuleAction.Remind;
            var target = change ? FindTarget(part, message, context) : null;
            var quotedTarget = target is not null && HasQuotedEvidence(message, target);
            var sourceReliable = context.TrustedSenders?.Contains(message.SenderId) == true ||
                (quotedTarget && target!.ConfirmedPublisherId == message.SenderId);
            var kind = action == RuleAction.Attend ? TimeKind.Start :
                change && target is not null ? target.Time.Kind : TimeKind.Deadline;
            var parsed = MessageDateParser.Parse(WithoutBackgroundDates(part), message.SentAt, context.TimeZoneId, kind);
            var suggestion = MessageDateParser.Parse(imageText, message.SentAt, context.TimeZoneId, kind);
            var formalTime = ocr ? null : parsed.Time;
            var suggestedTime = ocr ? parsed.Time : formalTime is null ? suggestion.Time : null;
            var needsTime = action != RuleAction.Cancel && action != RuleAction.Remind && action != RuleAction.Inform;
            var timeReliable = !needsTime || (!ocr && parsed.Issue == DateIssue.None);
            if (action == RuleAction.Remind && !ocr) formalTime ??= target?.Time;
            var reasons = new List<string>();
            if (contextMismatch) reasons.Add("账号或群上下文不匹配，不能复用已确认关系");
            if (question) reasons.Add("疑问语句不建立新任务");
            if (negated) reasons.Add("否定行动要求不建立新任务");
            if (uncertain) reasons.Add("猜测或无出处转述，需要确认原通知");
            if (partialCancel) reasons.Add("仅取消格式、介质或附件要求，不等于取消整个事项，需要确认要求变更");
            if (ocr) reasons.Add("图片识别仅作建议，需要查看原图确认");
            if (!courseReliable) reasons.Add("课程或群关联尚未确认");
            if (restricted) reasons.Add("明确限定的年级、班级或选课对象尚未确认适用于本人");
            if (!timeReliable) reasons.Add(parsed.Issue switch
            {
                DateIssue.Invalid => "日期或钟点无效，需要确认",
                DateIssue.Ambiguous => "日期或时间角色有歧义，需要确认",
                _ => ocr ? "图片日期不能作为正式时间" : "缺少可可靠解析的正式时间"
            });
            if (change && target is null) reasons.Add("缺少唯一的明确目标事项或引用证据");
            if (change && !sourceReliable) reasons.Add("高影响变更的发布者身份或原通知续发尚未确认");
            if (action == RuleAction.NewRequirement && target?.State is TaskState.Archived or TaskState.Cancelled)
                reasons.Add("已归档或已取消事项的新要求必须由用户确认");

            var disposition = question || negated ? RuleDisposition.Ignored :
                reasons.Count > 0 ? RuleDisposition.Pending :
                change ? RuleDisposition.Change :
                action == RuleAction.Inform || optional
                    ? RuleDisposition.Notice : RuleDisposition.Task;
            var priorityTime = formalTime;
            var priority = priorityTime is not null ? PriorityCalculator.Calculate(priorityTime,
                context.Now, PriorityCalculator.HasClearConsequence(part)) : (int?)null;
            var assessments = new RuleAssessments(
                new(ocr ? "行动来自图片识别，尚未人工确认" : question ? "疑问" : negated ? "否定行动" : uncertain ? "条件、猜测或转述" : partialCancel ? "仅取消部分要求，不能取消整个事项" : $"明确{action}行动", actionReliable),
                new(restricted ? "原文限定对象尚未与本人核对" : courseReliable ? "已确认本人课程关系" : "对象相关性尚未确认", audienceReliable),
                new(courseReliable ? "本账号、本群的已确认课程关系" : "未确认课程关系", courseReliable),
                new(action == RuleAction.Cancel ? "取消不需要新时间" : timeReliable ? "原文时间或不需要正式时间的通知" : parsed.Detail ?? "正式时间尚未确认", timeReliable),
                new(sourceReliable ? "本群已确认发布者或原发布者的明确引用续发" : "发布者尚未确认；普通清楚行动不以此单独排除", sourceReliable),
                new(target is not null ? "同群同课程完整独特名称或同账号同群明确引用关联" : change ? "目标关联不足" : "新行动保留独立消息证据，不按相似文字合并", !change || target is not null));
            return new(part, action, disposition, context.ConfirmedCourse, formalTime,
                suggestedTime, target?.Id, priority, assessments, reasons, evidence);
        }
    }

    private sealed record ActionPart(string Text, bool Conditional);

    private static List<ActionPart> SplitActions(string text)
    {
        var result = new List<ActionPart>();
        foreach (var sentence in Regex.Split(text, @"[;；。\n]+|(?<=[?!？！])"))
        {
            var prefix = "";
            var conditional = false;
            foreach (var clause in Regex.Split(sentence, @"[,，]|(?:以及|同时|并且|然后|并|和)(?=.{0,12}(?:提交|交|完成|考试|参加|报名))"))
            {
                var value = clause.Trim();
                if (value.Length == 0) continue;
                conditional |= Match(value, "如果|假如|假设|倘若|要是");
                // A bare participation preference describes the preceding event, not another event.
                var preference = result.Count > 0 && (GetAction(result[^1].Text) is RuleAction.Register or RuleAction.Attend) &&
                    Match(value, "^(?:大家)?(?:自愿|自行|自由|可自愿)(?:参加|报名)[!！]?$" );
                var background = Match(value, "原定|原截止|原计划|原来") &&
                    GetAction(value) is not (RuleAction.Postpone or RuleAction.Cancel or RuleAction.NewRequirement);
                if (HasAction(value) && !preference && !background)
                {
                    result.Add(new((prefix + value).Trim(), conditional));
                    prefix = "";
                }
                else if (result.Count > 0)
                {
                    result[^1] = result[^1] with { Text = result[^1].Text + "，" + value,
                        Conditional = result[^1].Conditional || conditional };
                    if (preference && result.Count > 1 && GetAction(result[^1].Text) == RuleAction.Attend &&
                        GetAction(result[^2].Text) == RuleAction.Register)
                        result[^2] = result[^2] with { Text = result[^2].Text + "，" + value };
                }
                else prefix += value + "，";
            }
        }
        return result;
    }

    private static bool HasAction(string text) => GetAction(text) != RuleAction.Inform ||
        Match(text, "通知|放假|停课|开课|安排|课程调整|(?:作业|报告|论文).*(?:格式|要求)");

    private static RuleAction GetAction(string text)
    {
        if (Match(text, "取消(?!资格)|停止(?:提交|报名|考试|活动)")) return RuleAction.Cancel;
        if (Match(text, "延期|延后|推迟|延至|改到|调整到")) return RuleAction.Postpone;
        if (Match(text, "补交|重新(?:提交|交|做)|重交|补做|新增要求|新要求|还需|追加|再交")) return RuleAction.NewRequirement;
        if (Match(text, "催交|提醒.*(?:交|提交)|尚未.*(?:交|提交)|还没.*交|尽快.*(?:交|提交)")) return RuleAction.Remind;
        if (Match(text, "提交|上交|交齐|交.{0,12}(?:作业|报告|论文|实验|材料|表|文件|习题|笔记|总结)|(?:作业|报告|论文|练习|材料).*(?:截止|交)|交第|交费|缴费|缴纳|上传|递交")) return RuleAction.Submit;
        if (Match(text, "完成|做.*(?:作业|习题|练习)|写.*(?:作业|报告|论文)")) return RuleAction.Complete;
        if (Match(text, "报名|登记|填报")) return RuleAction.Register;
        if (Match(text, "考试|测验|签到|参加|活动.*(?:开始|举行)|开会|集合|讲座.*(?:开始|举行)|比赛.*(?:开始|举行)")) return RuleAction.Attend;
        return RuleAction.Inform;
    }

    private static KnownTask? FindTarget(string text, SchoolMessage message, RuleContext context)
    {
        // Mentioning a complete title in an exclusion or contrast is not an affirmative target.
        if (Match(text, "除了|以外|除外|不包括|不是|而非")) return null;
        var eligible = (context.KnownTasks ?? []).Where(task =>
            task.Evidence.Any(key => key.AccountId == message.Key.AccountId && key.GroupId == message.Key.GroupId)).ToArray();
        var named = eligible.Where(task => task.Course == context.ConfirmedCourse &&
            task.Title.Length > 0 && text.Contains(task.Title, StringComparison.Ordinal)).ToArray();
        var quoted = eligible.Where(task => HasQuotedEvidence(message, task)).ToArray();
        if (quoted.Length > 0)
        {
            var identified = named.Length > 0 ? quoted.Intersect(named).ToArray() : quoted;
            return identified.Length == 1 ? identified[0] : null;
        }
        return named.Length == 1 ? named[0] : null;
    }

    private static bool HasQuotedEvidence(SchoolMessage message, KnownTask target) =>
        message.Segments.Any(segment => segment.Kind == SegmentKind.Quote &&
            segment.Reference is not null && segment.Reference.AccountId == message.Key.AccountId &&
            segment.Reference.GroupId == message.Key.GroupId && target.Evidence.Contains(segment.Reference));

    private static string WithoutBackgroundDates(string text)
    {
        const string date = @"(?:\d{4}[-/]\d{1,2}[-/]\d{1,2}|(?:\d{4}年)?\d{1,2}月\d{1,2}[日号]|\d{1,2}/\d{1,2}|今天|明天|后天|(?:本周|这周|下周|星期|周)[一二三四五六日天])";
        // Keep the original candidate title/evidence; remove only explicitly labelled background dates for parsing.
        text = Regex.Replace(text, date + @"(?:\d{1,2}(?:点|:)(?:\d{1,2})?)?\s*(?:布置|发布|发出)(?:的)?", "");
        return Regex.Replace(text, @"(?:原定|原截止|原计划|原来|上次|之前)\s*" + date + @"(?:\d{1,2}(?:点|:)(?:\d{1,2})?)?(?:截止)?", "");
    }

    private static bool Match(string text, string pattern) => Regex.IsMatch(text, pattern);
}
