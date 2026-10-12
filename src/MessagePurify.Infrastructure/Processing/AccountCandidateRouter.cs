using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Processing;

/// <summary>将消息的首轮提取结果分流，单条消息的全部结果与回执原子提交。</summary>
public sealed class AccountCandidateRouter
{
    private readonly AccountMessageStore messages;
    private readonly AccountTaskStore tasks;
    public string RoutingDatabasePath { get; }

    public AccountCandidateRouter(AccountMessageStore messages, AccountTaskStore tasks)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tasks);
        if (messages.AccountId != tasks.AccountId || !string.Equals(
            Path.GetDirectoryName(messages.DatabasePath), Path.GetDirectoryName(tasks.DatabasePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("消息与事项必须属于同一账号及数据目录。", nameof(tasks));
        this.messages = messages;
        this.tasks = tasks;
        RoutingDatabasePath = Path.Combine(Path.GetDirectoryName(tasks.DatabasePath)!, "routing.sqlite3");
        Initialize();
    }

    public int RoutePending(RuleContext context, IReadOnlySet<string> pinnedGroupIds, int limit = 100)
        => Route(context, pinnedGroupIds, limit, processUnextracted: false);

    /// <summary>按原发送时间在同一消息事务中提取、核对当前事项并路由首轮结果。</summary>
    public int ProcessAndRoutePending(RuleContext context, IReadOnlySet<string> pinnedGroupIds, int limit = 100,
        Func<bool>? canContinue = null)
        => Route(context, pinnedGroupIds, limit, processUnextracted: true, canContinue);

    private int Route(RuleContext context, IReadOnlySet<string> pinnedGroupIds, int limit, bool processUnextracted,
        Func<bool>? canContinue = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pinnedGroupIds);
        if (context.AccountId != messages.AccountId)
            throw new ArgumentException("规则上下文不属于当前账号。", nameof(context));
        ArgumentException.ThrowIfNullOrWhiteSpace(context.GroupId);
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        if (context.CourseExcluded || !pinnedGroupIds.Contains(context.GroupId)) return 0;

        using var connection = OpenAttached();
        var processed = 0;
        while (processed < limit && pinnedGroupIds.Contains(context.GroupId) && canContinue?.Invoke() != false)
        {
            using var transaction = connection.BeginTransaction();
            CheckStore(connection, transaction, "main", tasks.AccountId);
            CheckStore(connection, transaction, "inbox", messages.AccountId);
            CheckStore(connection, transaction, "routing", messages.AccountId);
            var pending = NextMessage(connection, transaction, context);
            if (pending is null || pending.Extraction is null && !processUnextracted) break;

            ValidateMessage(pending, context);
            var extraction = pending.Extraction ?? MessageRuleExtractor.Extract(pending.Message,
                CurrentContext(connection, transaction, context));
            var candidates = extraction.Candidates.ToArray();
            var notices = new List<(string Id, RuleCandidate Candidate)>();
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                ValidateEvidence(candidate, pending.Message.Key);
                if (candidate.Disposition is RuleDisposition.Pending or RuleDisposition.Change &&
                    candidate.Action is RuleAction.Cancel or RuleAction.Postpone or RuleAction.NewRequirement or RuleAction.Remind &&
                    !string.IsNullOrWhiteSpace(context.ConfirmedCourse) && candidate.Course == context.ConfirmedCourse)
                {
                    if (extraction.RuleVersion == MessageRuleExtractor.RuleVersion)
                    {
                        var refreshed = MessageRuleExtractor.Extract(pending.Message, CurrentContext(connection, transaction, context));
                        if (refreshed.Candidates.Count == candidates.Length && refreshed.Candidates[index].Action == candidate.Action)
                            candidates[index] = candidate = refreshed.Candidates[index];
                        else candidate = AsPending(candidate, "关联提取结果已改变，需要确认");
                    }
                    else candidate = AsPending(candidate, "关联规则版本不同，需要确认");
                }
                if (candidate.Disposition is RuleDisposition.Task or RuleDisposition.Notice or RuleDisposition.Change &&
                    (string.IsNullOrWhiteSpace(context.ConfirmedCourse) || candidate.Course != context.ConfirmedCourse))
                    candidate = candidate with
                    {
                        Disposition = RuleDisposition.Pending,
                        Assessments = candidate.Assessments with
                        {
                            Course = new("课程关联已改变，需要确认", false),
                            Audience = new("当前课程关系尚未核对适用于本人", false)
                        },
                        ReviewReasons = [.. candidate.ReviewReasons, "课程关联已改变，需要确认"]
                    };
                var identity = StableId(pending.Message.Key, index);
                switch (candidate.Disposition)
                {
                    case RuleDisposition.Task:
                        tasks.CreateTaskInTransaction(connection, transaction, "task-" + identity,
                            candidate, "route-task-" + identity, context.Now);
                        break;
                    case RuleDisposition.Pending:
                        tasks.SavePendingInTransaction(connection, transaction, "review-" + identity,
                            candidate, "route-review-" + identity, context.Now);
                        break;
                    case RuleDisposition.Change:
                        var reviewReason = OrderReviewReason(connection, transaction, candidate.TargetTaskId, pending.Message);
                        if (reviewReason is null)
                            reviewReason = tasks.ApplyAutomaticChangeInTransaction(connection, transaction, candidate,
                                "route-change-" + identity, context.Now).ReviewReason;
                        if (reviewReason is not null)
                            tasks.SavePendingInTransaction(connection, transaction, "review-" + identity,
                                AsPending(candidate, reviewReason), "route-review-" + identity, context.Now);
                        break;
                    case RuleDisposition.Notice:
                        tasks.ValidateCandidate(candidate);
                        notices.Add(("notice-" + identity, candidate));
                        break;
                    case RuleDisposition.Ignored:
                        break;
                    default:
                        throw new ArgumentException("候选分类无效。", nameof(candidate));
                }
            }
            var routedExtraction = extraction with { Candidates = candidates };
            var processingVersion = SaveExtraction(connection, transaction, pending, routedExtraction, context.Now);
            foreach (var item in notices)
            {
                var notice = new StoredNotice(item.Id, messages.AccountId, item.Candidate,
                    pending.Message.Key, processingVersion, pending.Origin, pending.BatchId, pending.Message.SentAt, context.Now);
                using var command = Command(connection, transaction,
                    "INSERT INTO routing.notices(id, notice_json) VALUES($id, $json);",
                    ("$id", notice.Id), ("$json", JsonSerializer.Serialize(notice)));
                command.ExecuteNonQuery();
            }
            using (var receipt = Command(connection, transaction, """
                INSERT INTO routing.route_receipts(account_id, group_id, message_id, processing_version, rule_version, routed_at)
                VALUES($account, $group, $message, $version, $rules, $at);
                """, ("$account", messages.AccountId), ("$group", pending.Message.Key.GroupId),
                ("$message", pending.Message.Key.MessageId), ("$version", processingVersion),
                ("$rules", extraction.RuleVersion), ("$at", context.Now.ToString("O", CultureInfo.InvariantCulture))))
                receipt.ExecuteNonQuery();
            transaction.Commit();
            processed++;
        }
        return processed;
    }

    private static RuleCandidate AsPending(RuleCandidate candidate, string reason) => candidate with
    {
        Disposition = RuleDisposition.Pending,
        ReviewReasons = candidate.ReviewReasons.Append(reason).Distinct().ToArray()
    };

    private static RuleContext CurrentContext(SqliteConnection connection, SqliteTransaction transaction, RuleContext context)
    {
        using var command = Command(connection, transaction, "SELECT record_json FROM main.tasks ORDER BY rowid;");
        using var reader = command.ExecuteReader();
        var known = new List<KnownTask>();
        while (reader.Read())
        {
            var task = JsonSerializer.Deserialize<TaskItem>(reader.GetString(0))
                ?? throw new InvalidDataException("事项记录为空。");
            if (task.AccountId == context.AccountId && task.Course == context.ConfirmedCourse &&
                task.Evidence.Any(key => key.AccountId == context.AccountId && key.GroupId == context.GroupId))
                known.Add(new(task.Id, task.Title, task.Course!, task.Time, task.State, task.Evidence));
        }
        // 历史发送者不等于已确认发布者；只使用当前群上下文的 TrustedSenders。
        return context with { KnownTasks = known };
    }

    private static string? OrderReviewReason(SqliteConnection connection, SqliteTransaction transaction,
        string? taskId, SchoolMessage incoming)
    {
        if (taskId is null) return "缺少明确目标事项，需要确认";
        using var command = Command(connection, transaction, """
            SELECT COUNT(*), COALESCE(SUM(CASE WHEN m.sent_utc_ticks IS NULL THEN 1 ELSE 0 END), 0), MAX(m.sent_utc_ticks)
            FROM main.task_evidence e
            LEFT JOIN inbox.messages m ON e.account_id = m.account_id AND e.group_id = m.group_id AND e.message_id = m.message_id
            WHERE e.task_id = $task AND NOT (e.account_id = $account AND e.group_id = $group AND e.message_id = $message);
            """, ("$task", taskId), ("$account", incoming.Key.AccountId),
            ("$group", incoming.Key.GroupId), ("$message", incoming.Key.MessageId));
        using var reader = command.ExecuteReader();
        reader.Read();
        if (reader.GetInt64(1) > 0) return "原事项证据缺少可核对的原消息时间，需要确认";
        if (reader.GetInt64(0) > 0)
            return incoming.SentAt.UtcTicks <= reader.GetInt64(2) ? "原消息先后需核对，不能自动覆盖" : null;
        reader.Close();
        using var sameSource = Command(connection, transaction, """
            SELECT COUNT(*) FROM main.task_evidence
            WHERE task_id = $task AND account_id = $account AND group_id = $group AND message_id = $message;
            """, ("$task", taskId), ("$account", incoming.Key.AccountId),
            ("$group", incoming.Key.GroupId), ("$message", incoming.Key.MessageId));
        return Convert.ToInt64(sameSource.ExecuteScalar(), CultureInfo.InvariantCulture) > 0
            ? null : "原事项缺少可核对的原消息顺序，需要确认";
    }

    private static long SaveExtraction(SqliteConnection connection, SqliteTransaction transaction,
        StoredMessage pending, RuleExtractionResult extraction, DateTimeOffset now)
    {
        var json = JsonSerializer.Serialize(extraction);
        if (pending.Extraction is not null && json == JsonSerializer.Serialize(pending.Extraction))
            return pending.ProcessingVersion;
        using var command = Command(connection, transaction, """
            UPDATE inbox.messages SET extraction_json = $json, rule_version = $rules,
                processed_at = $at, processing_version = processing_version + 1
            WHERE account_id = $account AND group_id = $group AND message_id = $message AND processing_version = $version;
            """, ("$json", json), ("$rules", extraction.RuleVersion),
            ("$at", now.ToString("O", CultureInfo.InvariantCulture)), ("$account", pending.Message.Key.AccountId),
            ("$group", pending.Message.Key.GroupId), ("$message", pending.Message.Key.MessageId), ("$version", pending.ProcessingVersion));
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("消息处理版本已改变，停止当前事务。");
        return pending.ProcessingVersion + 1;
    }

    public IReadOnlyList<StoredNotice> ListNotices()
    {
        using var connection = Open(RoutingDatabasePath);
        CheckStore(connection, null, "main", messages.AccountId);
        using var command = Command(connection, null, "SELECT notice_json FROM notices ORDER BY rowid;");
        using var reader = command.ExecuteReader();
        var result = new List<StoredNotice>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<StoredNotice>(reader.GetString(0))
            ?? throw new InvalidDataException("通知记录为空。"));
        return result;
    }

    private static StoredMessage? NextMessage(SqliteConnection connection, SqliteTransaction transaction, RuleContext context)
    {
        var excluded = context.ExcludedSenders?.ToArray() ?? [];
        var exclusions = excluded.Length == 0 ? "" :
            "AND json_extract(m.message_json, '$.SenderId') NOT IN (" +
            string.Join(",", excluded.Select((_, index) => "$sender" + index)) + ")";
        using var command = Command(connection, transaction, $"""
            SELECT m.message_json, m.origin, m.batch_id, m.received_at,
                m.processing_version, m.extraction_json, m.processed_at
            FROM inbox.messages m
            LEFT JOIN routing.route_receipts r ON r.account_id = m.account_id
                AND r.group_id = m.group_id AND r.message_id = m.message_id
            WHERE m.account_id = $account AND m.group_id = $group
                AND r.message_id IS NULL {exclusions}
            ORDER BY m.sent_utc_ticks, m.rowid LIMIT 1;
            """, ("$account", context.AccountId), ("$group", context.GroupId));
        for (var index = 0; index < excluded.Length; index++)
            command.Parameters.AddWithValue("$sender" + index, excluded[index]);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new(JsonSerializer.Deserialize<SchoolMessage>(reader.GetString(0))
            ?? throw new InvalidDataException("消息记录为空。"), (MessageOrigin)reader.GetInt32(1),
            reader.GetString(2), ParseTime(reader.GetString(3)), reader.GetInt64(4),
            reader.IsDBNull(5) ? null : JsonSerializer.Deserialize<RuleExtractionResult>(reader.GetString(5))
                ?? throw new InvalidDataException("提取结果为空。"), reader.IsDBNull(6) ? null : ParseTime(reader.GetString(6)));
    }

    private static void ValidateMessage(StoredMessage stored, RuleContext context)
    {
        var message = stored.Message;
        if (message.Key.AccountId != context.AccountId || message.Key.GroupId != context.GroupId || message.IsPrivate ||
            context.ExcludedSenders?.Contains(message.SenderId) == true)
            throw new InvalidDataException("已保存消息不符合当前账号及群范围。");
        if (stored.ProcessingVersion < 0 || stored.Extraction is not null && stored.ProcessingVersion < 1)
            throw new InvalidDataException("提取结果缺少有效处理版本。");
        if (stored.Extraction is not null) ArgumentException.ThrowIfNullOrWhiteSpace(stored.Extraction.RuleVersion);
    }

    private void ValidateEvidence(RuleCandidate candidate, MessageKey source)
    {
        if (!Enum.IsDefined(candidate.Action) || !Enum.IsDefined(candidate.Disposition) ||
            !candidate.Evidence.Contains(source))
            throw new ArgumentException("候选必须保留源消息证据及有效分类。", nameof(candidate));
        foreach (var key in candidate.Evidence)
        {
            if (key.AccountId != messages.AccountId)
                throw new ArgumentException("候选证据账号不匹配。", nameof(candidate));
            ArgumentException.ThrowIfNullOrWhiteSpace(key.GroupId);
            ArgumentException.ThrowIfNullOrWhiteSpace(key.MessageId);
        }
    }

    // 持久化身份须在重启/重试后保持一致，编码完整字符串键避免分隔符及大整数歧义。
    private static string StableId(MessageKey key, int index) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Key = key, Index = index })))).ToLowerInvariant();
    private static DateTimeOffset ParseTime(string time) =>
        DateTimeOffset.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private SqliteConnection OpenAttached()
    {
        var connection = Open(tasks.DatabasePath);
        try
        {
            if (!File.Exists(messages.DatabasePath) || !File.Exists(RoutingDatabasePath))
                throw new InvalidDataException("消息或路由数据库不存在，停止处理并保留其他数据。");
            using (var inbox = Command(connection, null, "ATTACH DATABASE $path AS inbox;", ("$path", messages.DatabasePath)))
                inbox.ExecuteNonQuery();
            using (var routing = Command(connection, null, "ATTACH DATABASE $path AS routing;", ("$path", RoutingDatabasePath)))
                routing.ExecuteNonQuery();
            // 跨文件事务仅使用磁盘 rollback journal；WAL/内存/关闭日志不提供所需的原子提交保障。
            foreach (var database in new[] { "main", "inbox", "routing" })
            {
                using var journal = Command(connection, null, $"PRAGMA {database}.journal_mode;");
                if (journal.ExecuteScalar() is not string mode || mode is not ("delete" or "truncate" or "persist"))
                    throw new InvalidDataException("数据库日志模式不支持跨库原子路由，保留原库并停止处理。");
            }
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private void Initialize()
    {
        using var connection = Open(RoutingDatabasePath, create: true);
        using var transaction = connection.BeginTransaction();
        using var versionCommand = Command(connection, transaction, "PRAGMA user_version;");
        var version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version == 0)
        {
            using var tables = Command(connection, transaction,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';");
            if (Convert.ToInt64(tables.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("已有路由数据库缺少可识别版本，保留原数据并停止打开。");
            using var schema = Command(connection, transaction, """
                CREATE TABLE store_metadata(account_id TEXT PRIMARY KEY NOT NULL);
                CREATE TABLE route_receipts(
                    account_id TEXT NOT NULL REFERENCES store_metadata(account_id),
                    group_id TEXT NOT NULL, message_id TEXT NOT NULL,
                    processing_version INTEGER NOT NULL CHECK(processing_version > 0),
                    rule_version TEXT NOT NULL, routed_at TEXT NOT NULL,
                    PRIMARY KEY(account_id, group_id, message_id));
                CREATE TABLE notices(id TEXT PRIMARY KEY NOT NULL, notice_json TEXT NOT NULL);
                INSERT INTO store_metadata(account_id) VALUES($account);
                PRAGMA user_version = 1;
                """, ("$account", messages.AccountId));
            schema.ExecuteNonQuery();
        }
        CheckStore(connection, transaction, "main", messages.AccountId);
        transaction.Commit();
    }

    private static void CheckStore(SqliteConnection connection, SqliteTransaction? transaction, string database, string accountId)
    {
        using var version = Command(connection, transaction, $"PRAGMA {database}.user_version;");
        if (Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
            throw new InvalidDataException("数据库结构版本尚不支持，保留原数据并停止打开。");
        using var command = Command(connection, transaction, $"SELECT account_id FROM {database}.store_metadata;");
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetString(0) != accountId || reader.Read())
            throw new InvalidDataException("数据库账号归属不匹配，保留原数据并停止打开。");
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static SqliteConnection Open(string path, bool create = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            ForeignKeys = true, Pooling = false
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }
}
