using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Processing;

public sealed record CacheCleanupResult(string AccountId, DateTimeOffset CompletedAt,
    DateTimeOffset Cutoff, int DeletedMessages, int RetainedOldMessages, int RemainingMessages);

public sealed partial class AccountCandidateRouter
{
    public CacheCleanupResult CleanMessageCache(DateTimeOffset now, Func<bool>? canContinue = null)
    {
        CheckContinue();
        using var connection = OpenAttached();
        using var transaction = connection.BeginTransaction();
        foreach (var database in new[] { "main", "inbox", "routing" })
            CheckStore(connection, transaction, database, messages.AccountId);
        var cutoff = now.AddDays(-7);
        var cached = new Dictionary<MessageKey, CachedMessage>();
        var protectedKeys = new HashSet<MessageKey>();
        var references = new Queue<MessageKey>();
        var scanned = 0;

        using (var command = Command(connection, transaction, """
            SELECT m.account_id, m.group_id, m.message_id, m.sent_utc_ticks,
                m.message_json, m.extraction_json, m.processing_version, r.processing_version
            FROM inbox.messages m
            LEFT JOIN routing.route_receipts r ON r.account_id=m.account_id
                AND r.group_id=m.group_id AND r.message_id=m.message_id
            WHERE m.account_id=$account ORDER BY m.sent_utc_ticks, m.rowid;
            """, ("$account", messages.AccountId)))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                CheckScan();
                var key = new MessageKey(reader.GetString(0), reader.GetString(1), reader.GetString(2));
                var message = JsonSerializer.Deserialize<SchoolMessage>(reader.GetString(4))
                    ?? throw new InvalidDataException("缓存消息记录为空。");
                if (message.Key != key) throw new InvalidDataException("缓存消息与存储标识不一致。");
                var extraction = reader.IsDBNull(5) ? null :
                    JsonSerializer.Deserialize<RuleExtractionResult>(reader.GetString(5))
                    ?? throw new InvalidDataException("缓存消息提取结果为空。");
                var sentTicks = reader.GetInt64(3);
                var candidate = sentTicks < cutoff.UtcTicks && extraction is not null &&
                    !reader.IsDBNull(7) && reader.GetInt64(6) == reader.GetInt64(7);
                cached.Add(key, new(message, extraction, sentTicks, candidate));
                if (!candidate) Protect(key);
            }

        using (var command = Command(connection, transaction,
            "SELECT account_id, group_id, message_id FROM main.task_evidence;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                CheckScan();
                Protect(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        using (var command = Command(connection, transaction, "SELECT record_json FROM main.reviews;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                CheckScan();
                var review = JsonSerializer.Deserialize<PendingReview>(reader.GetString(0))
                    ?? throw new InvalidDataException("待确认记录为空。");
                foreach (var key in review.Candidate.Evidence) Protect(key);
            }
        using (var command = Command(connection, transaction, "SELECT notice_json FROM routing.notices;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                CheckScan();
                var notice = JsonSerializer.Deserialize<StoredNotice>(reader.GetString(0))
                    ?? throw new InvalidDataException("通知记录为空。");
                Protect(notice.Source);
                foreach (var key in notice.Candidate.Evidence) Protect(key);
            }

        // 窗口内、尚待处理和长期证据的来源均递归保留完整引用链。
        while (references.TryDequeue(out var source))
        {
            CheckScan();
            if (!cached.TryGetValue(source, out var item)) continue;
            foreach (var segment in item.Message.Segments)
                if (segment.Kind == SegmentKind.Quote && segment.Reference is { } reference) Protect(reference);
            if (item.Extraction is not null)
                foreach (var candidate in item.Extraction.Candidates)
                    foreach (var key in candidate.Evidence) Protect(key);
        }

        var deleted = 0;
        foreach (var (key, item) in cached)
        {
            if (!item.Candidate || protectedKeys.Contains(key)) continue;
            CheckContinue();
            using var command = Command(connection, transaction, """
                DELETE FROM inbox.messages
                WHERE account_id=$account AND group_id=$group AND message_id=$message;
                """, ("$account", key.AccountId), ("$group", key.GroupId), ("$message", key.MessageId));
            deleted += command.ExecuteNonQuery();
        }
        var result = new CacheCleanupResult(messages.AccountId, now, cutoff, deleted,
            cached.Values.Count(item => item.SentTicks < cutoff.UtcTicks) - deleted, cached.Count - deleted);
        using (var command = Command(connection, transaction, """
            INSERT INTO routing.cache_cleanup_result(id, record_json) VALUES(1,$json)
            ON CONFLICT(id) DO UPDATE SET record_json=excluded.record_json;
            """, ("$json", JsonSerializer.Serialize(result))))
            command.ExecuteNonQuery();
        CheckContinue();
        transaction.Commit();
        return result;

        void Protect(MessageKey key)
        {
            if (protectedKeys.Add(key)) references.Enqueue(key);
        }
        void CheckContinue()
        {
            if (canContinue?.Invoke() == false) throw new OperationCanceledException("消息缓存清理已暂停。");
        }
        void CheckScan()
        {
            if (++scanned % 128 == 0) CheckContinue();
        }
    }

    public CacheCleanupResult? ReadCacheCleanupResult()
    {
        using var connection = Open(RoutingDatabasePath);
        CheckStore(connection, null, "main", messages.AccountId);
        using var command = Command(connection, null, "SELECT record_json FROM cache_cleanup_result WHERE id=1;");
        if (command.ExecuteScalar() is not string json) return null;
        var result = JsonSerializer.Deserialize<CacheCleanupResult>(json)
            ?? throw new InvalidDataException("缓存清理结果为空。");
        if (result.AccountId != messages.AccountId) throw new InvalidDataException("缓存清理结果账号不匹配。");
        return result;
    }

    public bool HasRouted(MessageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.AccountId != messages.AccountId) throw new ArgumentException("消息账号与路由存储不匹配。", nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(key.GroupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.MessageId);
        using var connection = Open(RoutingDatabasePath);
        CheckStore(connection, null, "main", messages.AccountId);
        using var command = Command(connection, null, """
            SELECT 1 FROM route_receipts
            WHERE account_id=$account AND group_id=$group AND message_id=$message;
            """, ("$account", key.AccountId), ("$group", key.GroupId), ("$message", key.MessageId));
        return command.ExecuteScalar() is not null;
    }

    private sealed record CachedMessage(SchoolMessage Message, RuleExtractionResult? Extraction,
        long SentTicks, bool Candidate);
}
