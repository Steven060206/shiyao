using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Processing;

public sealed partial class AccountCandidateRouter
{
    /// <summary>通知保存、审核状态和人工确认历史在同一跨库事务中提交。</summary>
    public StoredNotice AcceptReviewAsNotice(string reviewId, string operationId, long expectedVersion, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewId);
        using var connection = OpenAttached();
        using var transaction = connection.BeginTransaction();
        foreach (var database in new[] { "main", "inbox", "routing" })
            CheckStore(connection, transaction, database, messages.AccountId);
        var noticeId = "notice-review-" + reviewId;
        tasks.AcceptNoticeInTransaction(connection, transaction, reviewId, operationId, expectedVersion, now, review =>
        {
            var source = ReviewSource(connection, transaction, review);
            var notice = new StoredNotice(noticeId, messages.AccountId,
                review.Candidate with { Disposition = RuleDisposition.Notice }, source.Message.Key,
                source.ProcessingVersion, source.Origin, source.BatchId, source.Message.SentAt, now);
            using var insert = Command(connection, transaction,
                "INSERT INTO routing.notices(id, notice_json) VALUES($id, $json);",
                ("$id", noticeId), ("$json", JsonSerializer.Serialize(notice)));
            insert.ExecuteNonQuery();
        });
        using var query = Command(connection, transaction,
            "SELECT notice_json FROM routing.notices WHERE id = $id;", ("$id", noticeId));
        var saved = query.ExecuteScalar() is string json ? JsonSerializer.Deserialize<StoredNotice>(json) : null;
        if (saved is null || saved.AccountId != messages.AccountId)
            throw new InvalidDataException("已确认通知记录缺失或账号不匹配。");
        transaction.Commit();
        return saved;
    }

    private static StoredMessage ReviewSource(SqliteConnection connection, SqliteTransaction transaction, PendingReview review)
    {
        // 候选证据会排序；通过原路由身份核对来源，不能把排在前面的引用消息当成原消息。
        foreach (var key in review.Candidate.Evidence)
        {
            using var query = Command(connection, transaction, """
                SELECT message_json, origin, batch_id, received_at, processing_version, extraction_json, processed_at
                FROM inbox.messages WHERE account_id = $account AND group_id = $group AND message_id = $message;
                """, ("$account", key.AccountId), ("$group", key.GroupId), ("$message", key.MessageId));
            using var reader = query.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(5)) continue;
            var extraction = JsonSerializer.Deserialize<RuleExtractionResult>(reader.GetString(5));
            if (extraction is null || !Enumerable.Range(0, extraction.Candidates.Count).Any(index => ReviewId(key, index) == review.Id))
                continue;
            var source = new StoredMessage(JsonSerializer.Deserialize<SchoolMessage>(reader.GetString(0))
                ?? throw new InvalidDataException("原消息记录为空。"), (MessageOrigin)reader.GetInt32(1),
                reader.GetString(2), ParseTime(reader.GetString(3)), reader.GetInt64(4), extraction,
                reader.IsDBNull(6) ? null : ParseTime(reader.GetString(6)));
            if (source.Message.Key != key || source.Message.Key.AccountId != review.AccountId || source.ProcessingVersion < 1)
                throw new InvalidDataException("原消息身份或处理版本不匹配。");
            return source;
        }
        throw new InvalidDataException("待确认通知缺少可核对的原消息来源，原记录已保留。");
    }
}
