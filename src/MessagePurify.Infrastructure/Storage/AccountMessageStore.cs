using System.Globalization;
using System.Text.Json;
using MessagePurify.Core;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Storage;

/// <summary>每账号独立的消息收件箱。每次操作使用独立连接，结果与处理进度原子提交。</summary>
public sealed class AccountMessageStore
{
    private const int SchemaVersion = 1;
    private const string Columns = "message_json, origin, batch_id, received_at, processing_version, extraction_json, processed_at";

    public string AccountId { get; }
    public string DatabasePath { get; }

    public AccountMessageStore(string dataRoot, string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        if (string.IsNullOrEmpty(accountId) || accountId.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("QQ 账号须为 ASCII 数字标识。", nameof(accountId));

        AccountId = accountId;
        DatabasePath = Path.Combine(Path.GetFullPath(dataRoot), "accounts", accountId, "messages.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Initialize();
    }

    public bool TryEnqueue(SchoolMessage message, MessageOrigin origin, string batchId,
        DateTimeOffset receivedAt)
    {
        ValidateKey(message.Key);
        if (message.IsPrivate)
            throw new ArgumentException("收件箱只保存群聊消息。", nameof(message));
        ArgumentException.ThrowIfNullOrWhiteSpace(message.SenderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        if (!Enum.IsDefined(origin)) throw new ArgumentOutOfRangeException(nameof(origin));

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO messages(account_id, group_id, message_id, sent_utc_ticks,
                message_json, origin, batch_id, received_at)
            VALUES($account, $group, $message, $sent, $json, $origin, $batch, $received)
            ON CONFLICT(account_id, group_id, message_id) DO NOTHING;
            """;
        BindKey(command, message.Key);
        command.Parameters.AddWithValue("$sent", message.SentAt.UtcTicks);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(message));
        command.Parameters.AddWithValue("$origin", (int)origin);
        command.Parameters.AddWithValue("$batch", batchId);
        command.Parameters.AddWithValue("$received", FormatTime(receivedAt));
        return command.ExecuteNonQuery() == 1;
    }

    public StoredMessage? Find(MessageKey key)
    {
        ValidateKey(key);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM messages WHERE account_id = $account AND group_id = $group AND message_id = $message;";
        BindKey(command, key);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<StoredMessage> GetPending(int limit = 100, string? groupId = null)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        if (groupId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM messages
            WHERE account_id = $account AND extraction_json IS NULL
                {(groupId is null ? "" : "AND group_id = $group")}
            ORDER BY sent_utc_ticks, rowid LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$account", AccountId);
        command.Parameters.AddWithValue("$limit", limit);
        if (groupId is not null) command.Parameters.AddWithValue("$group", groupId);
        var messages = new List<StoredMessage>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) messages.Add(Read(reader));
        return messages;
    }

    public bool TrySaveExtraction(MessageKey key, long expectedVersion,
        RuleExtractionResult extraction, DateTimeOffset processedAt)
    {
        ValidateKey(key);
        if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(extraction.RuleVersion);
        foreach (var candidate in extraction.Candidates)
            foreach (var evidence in candidate.Evidence) ValidateKey(evidence);

        var json = JsonSerializer.Serialize(extraction);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE messages SET extraction_json = $result, rule_version = $rules,
                processed_at = $processed, processing_version = processing_version + 1
            WHERE account_id = $account AND group_id = $group AND message_id = $message
                AND processing_version = $expected;
            """;
        BindKey(command, key);
        command.Parameters.AddWithValue("$result", json);
        command.Parameters.AddWithValue("$rules", extraction.RuleVersion);
        command.Parameters.AddWithValue("$processed", FormatTime(processedAt));
        command.Parameters.AddWithValue("$expected", expectedVersion);
        var saved = command.ExecuteNonQuery() == 1;
        transaction.Commit();
        return saved;
    }

    private void Initialize()
    {
        using var connection = Open(create: true);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version == 0)
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("已有数据库缺少可识别的结构版本，保留原数据并停止打开。");
            command.CommandText = """
                CREATE TABLE store_metadata(account_id TEXT PRIMARY KEY NOT NULL);
                CREATE TABLE messages(
                    account_id TEXT NOT NULL REFERENCES store_metadata(account_id),
                    group_id TEXT NOT NULL,
                    message_id TEXT NOT NULL,
                    sent_utc_ticks INTEGER NOT NULL,
                    message_json TEXT NOT NULL,
                    origin INTEGER NOT NULL CHECK(origin IN (0, 1)),
                    batch_id TEXT NOT NULL,
                    received_at TEXT NOT NULL,
                    processing_version INTEGER NOT NULL DEFAULT 0 CHECK(processing_version >= 0),
                    extraction_json TEXT,
                    rule_version TEXT,
                    processed_at TEXT,
                    PRIMARY KEY(account_id, group_id, message_id),
                    CHECK((extraction_json IS NULL AND rule_version IS NULL AND processed_at IS NULL AND processing_version = 0)
                        OR (extraction_json IS NOT NULL AND rule_version IS NOT NULL AND processed_at IS NOT NULL AND processing_version > 0))
                );
                CREATE INDEX pending_message_order ON messages(account_id, group_id, sent_utc_ticks)
                    WHERE extraction_json IS NULL;
                INSERT INTO store_metadata(account_id) VALUES($account);
                PRAGMA user_version = 1;
                """;
            command.Parameters.AddWithValue("$account", AccountId);
            command.ExecuteNonQuery();
        }
        else if (version != SchemaVersion)
            throw new InvalidDataException("数据库结构版本尚不支持，保留原数据并停止打开。");

        command.CommandText = "SELECT account_id FROM store_metadata;";
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != AccountId || reader.Read())
                throw new InvalidDataException("数据库账号归属不匹配，保留原数据并停止打开。");
        }
        transaction.Commit();
    }

    private SqliteConnection Open(bool create = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void ValidateKey(MessageKey key)
    {
        if (key.AccountId != AccountId)
            throw new ArgumentException("消息或证据账号与收件箱不匹配。", nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(key.GroupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.MessageId);
    }

    private static void BindKey(SqliteCommand command, MessageKey key)
    {
        command.Parameters.AddWithValue("$account", key.AccountId);
        command.Parameters.AddWithValue("$group", key.GroupId);
        command.Parameters.AddWithValue("$message", key.MessageId);
    }

    private static string FormatTime(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTime(string time) => DateTimeOffset.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static StoredMessage Read(SqliteDataReader reader) => new(
        JsonSerializer.Deserialize<SchoolMessage>(reader.GetString(0)) ?? throw new InvalidDataException("消息记录为空。"),
        (MessageOrigin)reader.GetInt32(1), reader.GetString(2), ParseTime(reader.GetString(3)), reader.GetInt64(4),
        reader.IsDBNull(5) ? null : JsonSerializer.Deserialize<RuleExtractionResult>(reader.GetString(5))
            ?? throw new InvalidDataException("规则结果为空。"),
        reader.IsDBNull(6) ? null : ParseTime(reader.GetString(6)));
}
