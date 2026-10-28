using MessagePurify.Core;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Processing;

public sealed partial class AccountCandidateRouter
{
    /// <summary>OCR只补未有任何业务记录的消息为待确认；不回写原始提取和人工历史。</summary>
    public bool ReanalyzeImage(SchoolMessage enriched, RuleContext context, IReadOnlySet<string> pins)
    {
        if (context.AccountId != messages.AccountId || enriched.Key.AccountId != context.AccountId ||
            enriched.Key.GroupId != context.GroupId || enriched.IsPrivate) throw new ArgumentException("图片分析范围无效。");
        if (!pins.Contains(context.GroupId) || context.CourseExcluded) return false;
        using var db = OpenAttached();
        using var tx = db.BeginTransaction();
        var key = enriched.Key;
        using (var existing = Command(db, tx, """
            SELECT 1 FROM main.task_evidence WHERE account_id=$account AND group_id=$group AND message_id=$message
            UNION ALL SELECT 1 FROM main.reviews r, json_each(r.record_json,'$.Candidate.Evidence') e
                WHERE json_extract(e.value,'$.AccountId')=$account AND json_extract(e.value,'$.GroupId')=$group AND json_extract(e.value,'$.MessageId')=$message
            UNION ALL SELECT 1 FROM routing.notices n, json_each(n.notice_json,'$.Candidate.Evidence') e
                WHERE json_extract(e.value,'$.AccountId')=$account AND json_extract(e.value,'$.GroupId')=$group AND json_extract(e.value,'$.MessageId')=$message LIMIT 1;
            """, ("$account", key.AccountId), ("$group", key.GroupId), ("$message", key.MessageId)))
            if (existing.ExecuteScalar() is not null) return false;
        // raw仍须存在；异步完成时不得为已清理消息补造证据。
        if (messages.Find(key) is null) return false;
        var candidate = MessageRuleExtractor.Extract(enriched, CurrentContext(db, tx, context)).Candidates.Single();
        if (candidate.Disposition == RuleDisposition.Ignored) return false;
        candidate = candidate with { Disposition = RuleDisposition.Pending, SuggestedTime = candidate.SuggestedTime ?? candidate.Time,
            Time = null, ReviewReasons = candidate.ReviewReasons.Append("图片识别仅作建议，请查看原图并确认。").Distinct().ToArray() };
        tasks.SavePendingInTransaction(db, tx, ReviewId(key, 0), candidate, "ocr-review-" + StableId(key, 0), context.Now);
        tx.Commit();
        return true;
    }
}
