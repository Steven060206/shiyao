namespace MessagePurify.Core;

/// <summary>一条原消息只形成一个完整审核项；内部行动分析不自动拆出独立事项。</summary>
public static class MessageReviewCandidate
{
    public static RuleCandidate Combine(SchoolMessage message, IReadOnlyList<RuleCandidate> candidates)
    {
        if (candidates.Count == 0 || candidates.Any(candidate => candidate.Disposition == RuleDisposition.Ignored))
            throw new ArgumentException("合并输入必须包含至少一个非忽略业务候选。", nameof(candidates));

        var first = candidates[0];
        var evidence = new[] { message.Key }.Concat(candidates.SelectMany(candidate => candidate.Evidence))
            .Concat(message.Segments.Where(segment => segment.Kind == SegmentKind.Quote && segment.Reference is not null)
                .Select(segment => segment.Reference!)).Distinct().ToArray();
        var complete = first with { Title = FullContent(message), Evidence = evidence };
        if (candidates.Count == 1) return complete;

        var reasons = candidates.SelectMany(candidate => candidate.ReviewReasons).Distinct().ToList();
        reasons.Add("同一条原消息包含多个行动，保留完整内容统一确认；需要独立管理时请手动拆分");
        var sameAction = candidates.All(candidate => candidate.Action == first.Action);
        var hasChanges = candidates.Any(candidate => IsChange(candidate.Action));
        var sameTarget = first.TargetTaskId is not null && candidates.All(candidate => candidate.TargetTaskId == first.TargetTaskId);
        var safeChange = hasChanges && sameAction && sameTarget;
        var unsafeChange = hasChanges && !safeChange;
        var action = safeChange || (!hasChanges && sameAction) ? first.Action :
            candidates.All(candidate => candidate.Action is RuleAction.Submit or RuleAction.Complete)
                ? RuleAction.Submit : RuleAction.Complete;
        if (unsafeChange)
            reasons.Add("原消息包含不同变更行动或目标，不能作为单个关联变更应用；请核对完整内容并按需手动拆分");

        var sameTime = candidates.All(candidate => candidate.Time == first.Time);
        var sameSuggestion = candidates.All(candidate => candidate.SuggestedTime == first.SuggestedTime);
        var sameTimes = sameTime && sameSuggestion;
        if (!sameTimes)
            reasons.Add("多个行动的时间不同、角色不同或尚不完整，不能自动选择其中一个时间");
        var sameCourse = candidates.All(candidate => candidate.Course == first.Course);
        if (!sameCourse) reasons.Add("多个行动的课程关系不一致，需要确认");

        var assessments = new RuleAssessments(
            Merge(candidate => candidate.Assessments.Action),
            Merge(candidate => candidate.Assessments.Audience),
            Merge(candidate => candidate.Assessments.Course),
            Merge(candidate => candidate.Assessments.Time),
            Merge(candidate => candidate.Assessments.Source),
            Merge(candidate => candidate.Assessments.Association));
        if (!sameTimes)
            assessments = assessments with { Time = new("多个行动不能确定一个共同正式时间或建议时间", false) };
        if (!sameCourse)
            assessments = assessments with { Course = new("多个行动的课程关系不一致", false) };
        if (unsafeChange)
            assessments = assessments with { Association = new("多个变更行动或目标不能安全关联到单个事项", false) };

        return complete with
        {
            Action = action,
            Disposition = candidates.All(candidate => candidate.Disposition == RuleDisposition.Notice)
                ? RuleDisposition.Notice : RuleDisposition.Pending,
            Course = sameCourse ? first.Course : null,
            Time = sameTimes ? first.Time : null,
            SuggestedTime = sameTimes ? first.SuggestedTime : null,
            TargetTaskId = safeChange ? first.TargetTaskId : null,
            Priority = sameTimes && first.Time is not null && candidates.All(candidate => candidate.Priority == first.Priority)
                ? first.Priority : null,
            Assessments = assessments,
            ReviewReasons = reasons
        };

        RuleAssessment Merge(Func<RuleCandidate, RuleAssessment> select) => new(
            string.Join("；", candidates.Select(candidate => select(candidate).Description).Distinct()),
            candidates.All(candidate => select(candidate).Reliable));
    }

    /// <summary>为待确认保留可靠日期和单一钟点，不替用户选择多个钟点或确认事项。</summary>
    public static RuleCandidate ResolveTime(SchoolMessage message, RuleCandidate candidate, string timeZoneId)
    {
        if (candidate.Time is not null || candidate.Disposition != RuleDisposition.Pending ||
            candidate.TargetTaskId is not null || IsChange(candidate.Action)) return candidate;
        var text = string.Join("\n", MessageNormalizer.Normalize(message).Segments
            .Where(segment => segment.Kind == SegmentKind.Text).Select(segment => segment.Text));
        if (!MessageRuleExtractor.HasAction(text)) return candidate;
        var time = MessageRuleExtractor.ResolveReviewTime(text,
            message.SentAt, timeZoneId, candidate.Action == RuleAction.Attend ? TimeKind.Start : TimeKind.Deadline);
        if (time is null) return candidate;
        return candidate with
        {
            Time = time,
            SuggestedTime = null,
            ReviewReasons = candidate.ReviewReasons.Where(reason => reason != "缺少可可靠解析的正式时间" &&
                reason != "多个行动的时间不同、角色不同或尚不完整，不能自动选择其中一个时间")
                .Append(time.Time is null
                    ? "已按原消息发送时间识别日期，请核对；多个钟点或时间角色需分别查看原文，钟点可不填"
                    : "已按原消息发送时间识别日期和时间，请核对后确认")
                .Distinct().ToArray()
        };
    }

    internal static string FullContent(SchoolMessage message)
    {
        var normalized = MessageNormalizer.Normalize(message);
        var text = string.Join("\n", normalized.Segments.Where(segment => segment.Kind == SegmentKind.Text).Select(segment => segment.Text));
        var images = string.Join("\n", normalized.Segments.Where(segment => segment.Kind == SegmentKind.Image).Select(segment => segment.Text));
        var kind = !MessageRuleExtractor.HasAction(text) && MessageRuleExtractor.HasAction(images) ? SegmentKind.Image : SegmentKind.Text;
        return string.Join("\n", message.Segments.Where(segment => segment.Kind == kind).Select(segment => segment.Text));
    }

    private static bool IsChange(RuleAction action) => action is RuleAction.Cancel or RuleAction.Postpone or RuleAction.NewRequirement or RuleAction.Remind;
}
