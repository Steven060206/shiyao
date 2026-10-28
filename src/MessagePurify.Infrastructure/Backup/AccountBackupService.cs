using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Reminders;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Backup;

/// <summary>调用者持有账号工作区锁；只备份已存在的本地数据。</summary>
public sealed class AccountBackupService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, RespectRequiredConstructorParameters = true };
    private static readonly JsonSerializerOptions StoredJson = new() { RespectRequiredConstructorParameters = true };
    private static readonly string[] Databases = ["messages.sqlite3", "tasks.sqlite3", "routing.sqlite3", "reminders.sqlite3"];
    private static readonly string[] SettingsFiles = ["app-settings.json", "interface-preferences.json", "desktop-layout.json"];
    private readonly string dataRoot;
    private readonly string accountId;
    private string AccountDirectory => Path.Combine(dataRoot, "accounts", accountId);
    private string JournalPath => Path.Combine(dataRoot, "backup-restore-" + accountId + ".json");

    public AccountBackupService(string dataRoot, string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ValidateAccount(accountId);
        this.dataRoot = Path.GetFullPath(dataRoot);
        this.accountId = accountId;
    }

    public BackupResult Export(string destination, string workspaceSettingsJson,
        BackupApplicationSettings currentSettings, DateTimeOffset now)
    {
        destination = Path.GetFullPath(destination);
        RejectDataDestination(destination);
        ValidateWorkspaceSettings(workspaceSettingsJson, accountId);
        ValidateApplicationSettings(currentSettings);
        var stage = NewWorkingDirectory();
        var temporaryZip = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var accountStage = Path.Combine(stage, "account");
            Directory.CreateDirectory(accountStage);
            SnapshotDatabases(accountStage);
            File.WriteAllText(Path.Combine(accountStage, "workspace-settings.json"), workspaceSettingsJson);
            WriteSettings(Path.Combine(stage, "settings"), currentSettings);
            var content = ValidateAccountDirectory(accountStage, accountId);
            var files = PayloadPaths(currentSettings.DesktopLayoutJson is not null)
                .Select(path => DescribeFile(stage, path)).ToArray();
            var manifest = new Manifest(1, 1, typeof(AccountBackupService).Assembly.GetName().Version?.ToString() ?? "1.0.0.0",
                MessageRuleExtractor.RuleVersion, accountId, now.ToUniversalTime(), content.Images.Count == 0,
                content.TaskCount, content.ReviewCount, content.NoticeCount, files, content.Images);
            WriteJson(Path.Combine(stage, "manifest.json"), manifest);
            _ = ValidatePackageDirectory(stage, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            ZipFile.CreateFromDirectory(stage, temporaryZip);
            File.Move(temporaryZip, destination, overwrite: true);
            var result = Result(manifest, destination);
            SaveLastResult(result);
            return result;
        }
        finally
        {
            if (File.Exists(temporaryZip)) File.Delete(temporaryZip);
            DeleteWorkingDirectory(stage);
        }
    }

    public PreparedAccountBackup Inspect(string source)
    {
        source = Path.GetFullPath(source);
        var stage = NewWorkingDirectory();
        try
        {
            using (var archive = ZipFile.OpenRead(source))
            {
                var allowed = PayloadPaths(includeLayout: true).Append("manifest.json").ToHashSet(StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in archive.Entries)
                {
                    if (!allowed.Contains(entry.FullName) || !seen.Add(entry.FullName))
                        throw new InvalidDataException("备份包含未知或重复文件。");
                    var target = Path.Combine(stage, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                }
            }
            var (manifest, settings) = ValidatePackageDirectory(stage, source);
            return new(Result(manifest, source), settings, stage);
        }
        catch (Exception error) when (error is JsonException or SqliteException or ArgumentException or FormatException)
        {
            DeleteWorkingDirectory(stage);
            throw new InvalidDataException("备份格式或数据无效，当前账号数据已保留。", error);
        }
        catch
        {
            DeleteWorkingDirectory(stage);
            throw;
        }
    }

    public BackupResult Restore(PreparedAccountBackup prepared, string workspaceSettingsJson,
        BackupApplicationSettings currentSettings, DateTimeOffset now,
        Action<BackupApplicationSettings>? applySettings = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ObjectDisposedException.ThrowIf(prepared.IsDisposed, prepared);
        if (prepared.Preview.AccountId != accountId) throw new InvalidDataException("备份账号与当前账号不一致。");
        var (manifest, settings) = ValidatePackageDirectory(prepared.DirectoryPath, prepared.Preview.Path);
        if (File.Exists(JournalPath)) throw new InvalidOperationException("上次恢复尚未结束，请重新打开应用完成回滚。");
        var automaticPath = Path.Combine(dataRoot, "backups", accountId,
            "pre-restore-" + now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".zip");
        Export(automaticPath, workspaceSettingsJson, currentSettings, now);
        var transaction = NewWorkingDirectory();
        var restored = Path.Combine(transaction, "account-restored");
        var original = Path.Combine(transaction, "account-original");
        var historyPath = Path.Combine(dataRoot, "backup-history.json");
        var journal = new RestoreJournal(accountId, Path.GetFileName(transaction), ReadOriginalSettings(dataRoot), false,
            File.Exists(historyPath) ? Convert.ToBase64String(File.ReadAllBytes(historyPath)) : null);
        var committed = false;
        var historySaved = false;
        try
        {
            Directory.CreateDirectory(restored);
            foreach (var filename in Databases.Append("workspace-settings.json"))
                File.Copy(Path.Combine(prepared.DirectoryPath, "account", filename), Path.Combine(restored, filename));
            _ = ValidateAccountDirectory(restored, accountId);
            WriteJson(JournalPath, journal);
            Directory.Move(AccountDirectory, original);
            Directory.Move(restored, AccountDirectory);
            WriteSettings(dataRoot, settings);
            applySettings?.Invoke(settings);
            var result = Result(manifest, prepared.Preview.Path) with { PreRestoreBackupPath = automaticPath };
            SaveLastResult(result);
            historySaved = true;
            WriteJson(JournalPath, journal with { Committed = true });
            committed = true;
            CleanupCommitted(transaction, JournalPath);
            return result;
        }
        catch
        {
            if (!committed)
            {
                Rollback(dataRoot, journal, restoreHistory: historySaved);
                try { applySettings?.Invoke(currentSettings); }
                finally
                {
                    DeleteWorkingDirectory(transaction);
                    if (File.Exists(JournalPath)) File.Delete(JournalPath);
                }
            }
            throw;
        }
        finally
        {
            if (!File.Exists(JournalPath)) DeleteWorkingDirectory(transaction);
        }
    }

    public static void RecoverInterrupted(string dataRoot)
    {
        dataRoot = Path.GetFullPath(dataRoot);
        if (!Directory.Exists(dataRoot)) return;
        foreach (var path in Directory.EnumerateFiles(dataRoot, "backup-restore-*.json"))
        {
            var journal = ReadJson<RestoreJournal>(path);
            ValidateAccount(journal.AccountId);
            if (!Guid.TryParseExact(journal.TransactionDirectory, "N", out _) ||
                Path.GetFileName(path) != "backup-restore-" + journal.AccountId + ".json" || journal.OriginalSettings is null ||
                !journal.OriginalSettings.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(SettingsFiles))
                throw new InvalidDataException("未完成的备份恢复记录无效，原目录已保留。");
            var transaction = Path.Combine(dataRoot, ".backup-work", journal.TransactionDirectory);
            if (!journal.Committed) Rollback(dataRoot, journal);
            DeleteWorkingDirectory(transaction);
            File.Delete(path);
        }
    }

    public BackupResult? GetLastResult()
    {
        var path = Path.Combine(dataRoot, "backup-history.json");
        if (!File.Exists(path)) return null;
        return ReadJson<Dictionary<string, BackupResult>>(path).GetValueOrDefault(accountId);
    }

    private void SaveLastResult(BackupResult result)
    {
        var path = Path.Combine(dataRoot, "backup-history.json");
        var history = File.Exists(path) ? ReadJson<Dictionary<string, BackupResult>>(path) : [];
        history[accountId] = result;
        WriteJson(path, history);
    }

    private void SnapshotDatabases(string destination)
    {
        using var coordinator = OpenDatabase(Path.Combine(AccountDirectory, "tasks.sqlite3"), SqliteOpenMode.ReadWrite);
        foreach (var (filename, alias) in new[] { ("messages.sqlite3", "inbox"), ("routing.sqlite3", "routing"), ("reminders.sqlite3", "reminders") })
        {
            using var attach = coordinator.CreateCommand();
            attach.CommandText = "ATTACH DATABASE $path AS " + alias + ";";
            attach.Parameters.AddWithValue("$path", Path.Combine(AccountDirectory, filename));
            attach.ExecuteNonQuery();
        }
        foreach (var alias in new[] { "main", "inbox", "routing", "reminders" })
            if (Scalar(coordinator, "PRAGMA " + alias + ".journal_mode;") is not string mode || mode is not ("delete" or "truncate" or "persist"))
                throw new InvalidDataException("数据库日志模式不支持一致账号备份。");
        // 同时取得四库保留锁阻止写入；Backup API 的源使用独立只读连接，避免写事务中备份死锁。
        using var transaction = coordinator.BeginTransaction(deferred: false);
        foreach (var filename in Databases)
        {
            using var source = OpenDatabase(Path.Combine(AccountDirectory, filename), SqliteOpenMode.ReadOnly);
            using var target = OpenDatabase(Path.Combine(destination, filename), SqliteOpenMode.ReadWriteCreate);
            source.BackupDatabase(target);
        }
        transaction.Rollback();
    }

    private (Manifest Manifest, BackupApplicationSettings Settings) ValidatePackageDirectory(string directory, string source)
    {
        var manifest = ReadJson<Manifest>(Path.Combine(directory, "manifest.json"));
        if (manifest.FormatVersion != 1 || manifest.SchemaVersion != 1 || manifest.AccountId != accountId ||
            string.IsNullOrWhiteSpace(manifest.ApplicationVersion) || string.IsNullOrWhiteSpace(manifest.RuleVersion) ||
            manifest.CreatedAt == default || manifest.Files is null || manifest.Evidence is null)
            throw new InvalidDataException("备份版本、账号或清单不受支持。");
        var includeLayout = manifest.Files.Any(file => file.Path == "settings/desktop-layout.json");
        var expected = PayloadPaths(includeLayout).ToHashSet(StringComparer.Ordinal);
        if (manifest.Files.Count != expected.Count || !manifest.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            throw new InvalidDataException("备份文件清单缺失或重复。");
        foreach (var entry in manifest.Files)
        {
            var path = Path.Combine(directory, entry.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Length || DescribeFile(directory, entry.Path).Sha256 != entry.Sha256)
                throw new InvalidDataException("备份文件缺失或校验失败：" + entry.Path);
        }
        ValidateWorkspaceSettings(File.ReadAllText(Path.Combine(directory, "account", "workspace-settings.json")), accountId);
        var startup = ReadJson<StartupSettings>(Path.Combine(directory, "settings", "app-settings.json"));
        var preferences = ReadJson<InterfacePreferences>(Path.Combine(directory, "settings", "interface-preferences.json"));
        var settings = new BackupApplicationSettings(startup.AutoStartEnabled, preferences.Theme, preferences.Motion,
            includeLayout ? File.ReadAllText(Path.Combine(directory, "settings", "desktop-layout.json")) : null, preferences.DesktopTransparency);
        ValidateApplicationSettings(settings);
        var content = ValidateAccountDirectory(Path.Combine(directory, "account"), accountId);
        if (manifest.TaskCount != content.TaskCount || manifest.ReviewCount != content.ReviewCount || manifest.NoticeCount != content.NoticeCount ||
            manifest.IsComplete != (content.Images.Count == 0) || !manifest.Evidence.SequenceEqual(content.Images))
            throw new InvalidDataException("备份内容与清单不一致。");
        return (manifest, settings);
    }

    private static AccountContent ValidateAccountDirectory(string directory, string owner)
    {
        var allMessages = new Dictionary<MessageKey, SchoolMessage>();
        var images = new List<ImageEvidence>();
        using (var db = ValidateDatabase(Path.Combine(directory, "messages.sqlite3"), owner, ["store_metadata", "messages"]))
        using (var command = Command(db, "SELECT account_id,group_id,message_id,message_json,extraction_json FROM messages ORDER BY rowid;"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var message = ParseStored<SchoolMessage>(reader.GetString(3));
                var key = new MessageKey(reader.GetString(0), reader.GetString(1), reader.GetString(2));
                ValidateKey(key, owner);
                if (message.Key != key || message.IsPrivate || string.IsNullOrWhiteSpace(message.SenderId) || message.Segments is null)
                    throw new InvalidDataException("备份消息归属或内容无效。");
                for (var i = 0; i < message.Segments.Count; i++)
                {
                    var segment = message.Segments[i];
                    if (segment is null || !Enum.IsDefined(segment.Kind) || segment.Text is null) throw new InvalidDataException("备份消息段无效。");
                    if (segment.Reference is { } reference) ValidateKey(reference, owner);
                    if (segment.Kind == SegmentKind.Image) images.Add(new(key, i, segment.AssetId, "not-downloaded", null, null));
                }
                if (!reader.IsDBNull(4))
                {
                    var extraction = ParseStored<RuleExtractionResult>(reader.GetString(4));
                    if (string.IsNullOrWhiteSpace(extraction.RuleVersion) || extraction.Candidates is null) throw new InvalidDataException("备份提取结果无效。");
                    foreach (var candidate in extraction.Candidates) ValidateCandidate(candidate, owner, null, allowEmptyIgnoredTitle: true);
                }
                allMessages.Add(key, message);
            }
        }
        var tasks = new Dictionary<string, TaskItem>(StringComparer.Ordinal);
        var reviewCount = 0;
        using (var db = ValidateDatabase(Path.Combine(directory, "tasks.sqlite3"), owner, ["store_metadata", "tasks", "reviews", "task_evidence", "operations"]))
        {
            using (var command = Command(db, "SELECT id,record_json FROM tasks ORDER BY rowid;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    var task = ParseStored<TaskItem>(reader.GetString(1));
                    ValidateTask(task, owner, allMessages);
                    if (task.Id != reader.GetString(0)) throw new InvalidDataException("备份事项标识不一致。");
                    tasks.Add(task.Id, task);
                }
            using (var command = Command(db, "SELECT id,record_json FROM reviews ORDER BY rowid;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    var review = ParseStored<PendingReview>(reader.GetString(1));
                    ValidateReview(review, owner, allMessages);
                    if (review.Id != reader.GetString(0)) throw new InvalidDataException("备份待确认标识不一致。");
                    reviewCount++;
                }
            var evidence = new HashSet<(string, MessageKey)>();
            using (var command = Command(db, "SELECT task_id,account_id,group_id,message_id FROM task_evidence;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    var key = new MessageKey(reader.GetString(1), reader.GetString(2), reader.GetString(3));
                    ValidateEvidence([key], owner, allMessages);
                    if (!tasks.TryGetValue(reader.GetString(0), out var task) || !task.Evidence.Contains(key)) throw new InvalidDataException("备份证据关联不一致。");
                    evidence.Add((task.Id, key));
                }
            if (!evidence.SetEquals(tasks.Values.SelectMany(task => task.Evidence.Select(key => (task.Id, key)))))
                throw new InvalidDataException("备份事项证据关联缺失。");
            using (var command = Command(db, "SELECT request_json,before_json,after_json FROM operations ORDER BY sequence;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    using var request = JsonDocument.Parse(reader.GetString(0));
                    if (request.RootElement.ValueKind != JsonValueKind.Object || !request.RootElement.TryGetProperty("Kind", out var kind) || string.IsNullOrWhiteSpace(kind.GetString()))
                        throw new InvalidDataException("备份操作审计无效。");
                    ValidateAudit(request.RootElement, owner, allMessages);
                    if (!reader.IsDBNull(1)) { using var before = JsonDocument.Parse(reader.GetString(1)); ValidateAudit(before.RootElement, owner, allMessages); }
                    using var after = JsonDocument.Parse(reader.GetString(2));
                    ValidateAudit(after.RootElement, owner, allMessages);
                }
        }
        var noticeCount = 0;
        using (var db = ValidateDatabase(Path.Combine(directory, "routing.sqlite3"), owner, ["store_metadata", "route_receipts", "notices"]))
        {
            using (var command = Command(db, "SELECT id,notice_json FROM notices ORDER BY rowid;"))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    var notice = ParseStored<StoredNotice>(reader.GetString(1));
                    if (notice.AccountId != owner || notice.Id != reader.GetString(0) || notice.ProcessingVersion < 1) throw new InvalidDataException("备份通知归属无效。");
                    ValidateEvidence([notice.Source], owner, allMessages);
                    ValidateCandidate(notice.Candidate, owner, allMessages);
                    noticeCount++;
                }
            using var receipts = Command(db, "SELECT account_id,group_id,message_id FROM route_receipts;");
            using var receiptReader = receipts.ExecuteReader();
            while (receiptReader.Read()) ValidateKey(new(receiptReader.GetString(0), receiptReader.GetString(1), receiptReader.GetString(2)), owner);
        }
        using (var db = ValidateDatabase(Path.Combine(directory, "reminders.sqlite3"), owner, ["store_metadata", "reminder_state"]))
        {
            var json = Scalar(db, "SELECT record_json FROM reminder_state WHERE id=1;") as string ?? throw new InvalidDataException("备份提醒状态缺失。");
            var state = ParseStored<ReminderStoreData>(json);
            if (state.Plans is null || state.Deliveries is null || state.Operations is null) throw new InvalidDataException("备份提醒状态无效。");
            foreach (var (id, plan) in state.Plans)
                if (plan is null || plan.TaskId != id || plan.Preference is null || plan.Nodes is null || plan.TimeVersion < 1 || plan.PlanVersion < 1 ||
                    plan.Nodes.Any(node => node is null || string.IsNullOrWhiteSpace(node.Id) || node.State is not ("pending" or "sending" or "submitted" or "blocked" or "failed" or "unknown" or "covered")))
                    throw new InvalidDataException("备份提醒计划无效。");
            foreach (var (id, record) in state.Deliveries)
                if (record?.Delivery is not { } delivery || delivery.Id != id || delivery.AccountId != owner || delivery.Members is null)
                    throw new InvalidDataException("备份提醒发送历史无效。");
            foreach (var record in state.Operations.Values)
                if (record is null || string.IsNullOrWhiteSpace(record.Request) || record.Result is null) throw new InvalidDataException("备份提醒操作历史无效。");
        }
        return new(tasks.Count, reviewCount, noticeCount, images);
    }

    private static void ValidateAudit(JsonElement element, string owner, IReadOnlyDictionary<MessageKey, SchoolMessage> messages)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("AccountId", out var account) && account.GetString() != owner) throw new InvalidDataException("备份历史归属无效。");
            if (element.TryGetProperty("AccountId", out _) && element.TryGetProperty("TimeVersion", out _))
                ValidateTask(element.Deserialize<TaskItem>() ?? throw new InvalidDataException("备份事项历史无效。"), owner, messages);
            else if (element.TryGetProperty("AccountId", out _) && element.TryGetProperty("Candidate", out _))
                ValidateReview(element.Deserialize<PendingReview>() ?? throw new InvalidDataException("备份待确认历史无效。"), owner, messages);
            if (element.TryGetProperty("Evidence", out var evidence)) ValidateEvidence(evidence.Deserialize<MessageKey[]>() ?? throw new InvalidDataException("备份历史证据无效。"), owner, messages);
            foreach (var property in element.EnumerateObject()) ValidateAudit(property.Value, owner, messages);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var member in element.EnumerateArray()) ValidateAudit(member, owner, messages);
    }

    private static void ValidateTask(TaskItem task, string owner, IReadOnlyDictionary<MessageKey, SchoolMessage> messages)
    {
        if (task.AccountId != owner || string.IsNullOrWhiteSpace(task.Id) || string.IsNullOrWhiteSpace(task.Title) || task.Time is null ||
            !Enum.IsDefined(task.State) || !Enum.IsDefined(task.Action) || task.Version < 1 || task.TimeVersion < 1 || task.Evidence is null)
            throw new InvalidDataException("备份事项内容无效。");
        ValidateTime(task.Time);
        ValidateEvidence(task.Evidence, owner, messages);
    }

    private static void ValidateReview(PendingReview review, string owner, IReadOnlyDictionary<MessageKey, SchoolMessage> messages)
    {
        if (review.AccountId != owner || string.IsNullOrWhiteSpace(review.Id) || review.Version < 1 || !Enum.IsDefined(review.State))
            throw new InvalidDataException("备份待确认内容无效。");
        ValidateCandidate(review.Candidate, owner, messages);
    }

    private static void ValidateCandidate(RuleCandidate candidate, string owner, IReadOnlyDictionary<MessageKey, SchoolMessage>? messages,
        bool allowEmptyIgnoredTitle = false)
    {
        // 无文本消息的忽略诊断可保留空标题；实际待确认和通知仍须有标题。
        if (candidate is null || (string.IsNullOrWhiteSpace(candidate.Title) && !(allowEmptyIgnoredTitle && candidate.Disposition == RuleDisposition.Ignored)) ||
            !Enum.IsDefined(candidate.Action) || !Enum.IsDefined(candidate.Disposition) ||
            candidate.Evidence is null || candidate.Assessments is null || candidate.Assessments.Action is null || candidate.Assessments.Audience is null ||
            candidate.Assessments.Course is null || candidate.Assessments.Time is null || candidate.Assessments.Source is null || candidate.Assessments.Association is null || candidate.ReviewReasons is null)
            throw new InvalidDataException("备份候选内容无效。");
        if (candidate.Time is { } time) ValidateTime(time);
        if (candidate.SuggestedTime is { } suggested) ValidateTime(suggested);
        ValidateEvidence(candidate.Evidence, owner, messages);
    }

    private static void ValidateTime(TaskTime time)
    {
        if (!Enum.IsDefined(time.Kind) || time.Date == default || string.IsNullOrWhiteSpace(time.TimeZoneId)) throw new InvalidDataException("备份事项日期无效。");
    }

    private static void ValidateEvidence(IEnumerable<MessageKey> keys, string owner, IReadOnlyDictionary<MessageKey, SchoolMessage>? messages)
    {
        foreach (var key in keys)
        {
            ValidateKey(key, owner);
            if (messages is not null && !messages.ContainsKey(key)) throw new InvalidDataException("备份必要消息证据缺失。");
        }
    }

    private static void ValidateKey(MessageKey key, string owner)
    {
        if (key is null || key.AccountId != owner || string.IsNullOrWhiteSpace(key.GroupId) || string.IsNullOrWhiteSpace(key.MessageId))
            throw new InvalidDataException("备份消息证据归属无效。");
    }

    private static SqliteConnection ValidateDatabase(string path, string owner, string[] tables)
    {
        var connection = OpenDatabase(path, SqliteOpenMode.ReadOnly);
        try
        {
            if (Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture) != 1 ||
                Scalar(connection, "PRAGMA integrity_check;") as string != "ok") throw new InvalidDataException("备份数据库版本或完整性无效。");
            using (var command = Command(connection, "SELECT account_id FROM store_metadata;"))
            using (var reader = command.ExecuteReader())
                if (!reader.Read() || reader.GetString(0) != owner || reader.Read()) throw new InvalidDataException("备份数据库账号归属无效。");
            foreach (var table in tables)
            {
                if (Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='" + table + "';"), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidDataException("备份数据库缺少必要表。");
                var columns = table switch
                {
                    "store_metadata" => "account_id",
                    "messages" => "account_id,group_id,message_id,sent_utc_ticks,message_json,origin,batch_id,received_at,processing_version,extraction_json,rule_version,processed_at",
                    "tasks" or "reviews" => "id,record_json,source_key",
                    "task_evidence" => "task_id,account_id,group_id,message_id",
                    "operations" => "sequence,operation_id,request_json,result_kind,result_id,processed_at,before_json,after_json",
                    "route_receipts" => "account_id,group_id,message_id,processing_version,rule_version,routed_at",
                    "notices" => "id,notice_json",
                    "reminder_state" => "id,record_json",
                    _ => throw new InvalidDataException("备份数据库表不受支持。")
                };
                using var checkColumns = Command(connection, "SELECT " + columns + " FROM " + table + " LIMIT 0;");
                using var checkedColumns = checkColumns.ExecuteReader();
            }
            using (var foreignKeys = Command(connection, "PRAGMA foreign_key_check;"))
            using (var reader = foreignKeys.ExecuteReader())
                if (reader.Read()) throw new InvalidDataException("备份数据库关联损坏。");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static void ValidateWorkspaceSettings(string text, string owner)
    {
        var settings = ParseStored<WorkspaceSettings>(text);
        if (settings.AccountId != owner || settings.Relationships is null || settings.ReadVersions is null ||
            settings.Relationships.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || string.IsNullOrWhiteSpace(pair.Value.Name)) ||
            settings.ReadVersions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
            throw new InvalidDataException("备份工作区设置归属或内容无效。");
    }

    private static void ValidateApplicationSettings(BackupApplicationSettings settings)
    {
        if (settings.Theme is not ("system" or "light" or "dark") || settings.Motion is not ("system" or "reduce")) throw new InvalidDataException("备份界面偏好无效。");
        if (settings.DesktopTransparency is < 0 or > 100) throw new InvalidDataException("备份桌面透明度无效。");
        if (settings.DesktopLayoutJson is { } layout)
        {
            var stored = JsonSerializer.Deserialize<DesktopLayout>(layout, Json) ?? throw new InvalidDataException("备份桌面布局为空。");
            if (stored.Schema != 1 || !ValidRectangle(stored.Tasks) || !ValidRectangle(stored.Pending)) throw new InvalidDataException("备份桌面布局无效。");
        }
    }

    private static bool ValidRectangle(Rectangle? rectangle) => rectangle is not null && double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Y) &&
        double.IsFinite(rectangle.Width) && double.IsFinite(rectangle.Height) && rectangle.Width > 0 && rectangle.Height > 0;

    private static void WriteSettings(string directory, BackupApplicationSettings settings)
    {
        Directory.CreateDirectory(directory);
        // AppStartupPreferences 使用 PascalCase；页面偏好和桌面布局沿用 Web JSON。
        AtomicWrite(Path.Combine(directory, "app-settings.json"), JsonSerializer.Serialize(new StartupSettings(settings.AutoStartEnabled)));
        WriteJson(Path.Combine(directory, "interface-preferences.json"), new InterfacePreferences(settings.Theme, settings.Motion, settings.DesktopTransparency));
        var layoutPath = Path.Combine(directory, "desktop-layout.json");
        if (settings.DesktopLayoutJson is { } layout) AtomicWrite(layoutPath, layout);
        else if (File.Exists(layoutPath)) File.Delete(layoutPath);
    }

    private static Dictionary<string, string?> ReadOriginalSettings(string root) => SettingsFiles.ToDictionary(filename => filename,
        filename => File.Exists(Path.Combine(root, filename)) ? Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, filename))) : null, StringComparer.Ordinal);

    private static void Rollback(string root, RestoreJournal journal, bool restoreHistory = true)
    {
        var original = Path.Combine(root, ".backup-work", journal.TransactionDirectory, "account-original");
        var target = Path.Combine(root, "accounts", journal.AccountId);
        if (Directory.Exists(original))
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(original, target);
        }
        foreach (var filename in SettingsFiles)
        {
            var targetFile = Path.Combine(root, filename);
            if (journal.OriginalSettings[filename] is { } encoded) AtomicWriteBytes(targetFile, Convert.FromBase64String(encoded));
            else if (File.Exists(targetFile)) File.Delete(targetFile);
        }
        if (restoreHistory)
        {
            var historyPath = Path.Combine(root, "backup-history.json");
            if (journal.OriginalBackupHistory is { } history) AtomicWriteBytes(historyPath, Convert.FromBase64String(history));
            else if (File.Exists(historyPath)) File.Delete(historyPath);
        }
    }

    private void RejectDataDestination(string path)
    {
        foreach (var directory in new[] { Path.Combine(dataRoot, "accounts"), Path.Combine(dataRoot, ".backup-work") })
            if (path.Equals(directory, StringComparison.OrdinalIgnoreCase) || path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("备份文件不能覆盖账号数据或备份暂存目录。", nameof(path));
        if (SettingsFiles.Append("backup-history.json").Append("active-account.txt").Append("game-catalog.json").Any(file => path.Equals(Path.Combine(dataRoot, file), StringComparison.OrdinalIgnoreCase)) || path.Equals(JournalPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("备份文件不能覆盖应用数据。", nameof(path));
    }

    private string NewWorkingDirectory()
    {
        var directory = Path.Combine(dataRoot, ".backup-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteWorkingDirectory(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private static void CleanupCommitted(string transaction, string journal)
    {
        try { DeleteWorkingDirectory(transaction); File.Delete(journal); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static IEnumerable<string> PayloadPaths(bool includeLayout)
    {
        foreach (var database in Databases) yield return "account/" + database;
        yield return "account/workspace-settings.json";
        yield return "settings/app-settings.json";
        yield return "settings/interface-preferences.json";
        if (includeLayout) yield return "settings/desktop-layout.json";
    }

    private static FileEntry DescribeFile(string directory, string path)
    {
        using var file = File.OpenRead(Path.Combine(directory, path));
        return new(path, file.Length, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant());
    }

    private static BackupResult Result(Manifest manifest, string source) => new(manifest.AccountId, source, manifest.CreatedAt,
        manifest.IsComplete, manifest.Evidence.Count, manifest.TaskCount, manifest.ReviewCount, manifest.NoticeCount);

    private static void ValidateAccount(string account)
    {
        if (string.IsNullOrEmpty(account) || account.Any(c => c is < '0' or > '9')) throw new ArgumentException("QQ 账号须为 ASCII 数字标识。", nameof(account));
    }

    private static T ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("备份记录为空。");
    private static T ParseStored<T>(string json) => JsonSerializer.Deserialize<T>(json, StoredJson) ?? throw new InvalidDataException("备份数据记录为空。");
    private static void WriteJson<T>(string path, T value) => AtomicWrite(path, JsonSerializer.Serialize(value, Json));
    private static void AtomicWrite(string path, string text) => AtomicWriteBytes(path, System.Text.Encoding.UTF8.GetBytes(text));

    private static void AtomicWriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static SqliteConnection OpenDatabase(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false, ForeignKeys = true }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static SqliteCommand Command(SqliteConnection connection, string text)
    {
        var command = connection.CreateCommand();
        command.CommandText = text;
        return command;
    }

    private static object? Scalar(SqliteConnection connection, string text)
    {
        using var command = Command(connection, text);
        return command.ExecuteScalar();
    }

    private sealed record FileEntry(string Path, long Length, string Sha256);
    private sealed record ImageEvidence(MessageKey MessageKey, int SegmentIndex, string? AssetId, string Status, string? Path, string? Sha256);
    private sealed record Manifest(int FormatVersion, int SchemaVersion, string ApplicationVersion, string RuleVersion, string AccountId,
        DateTimeOffset CreatedAt, bool IsComplete, int TaskCount, int ReviewCount, int NoticeCount, IReadOnlyList<FileEntry> Files, IReadOnlyList<ImageEvidence> Evidence);
    private sealed record AccountContent(int TaskCount, int ReviewCount, int NoticeCount, IReadOnlyList<ImageEvidence> Images);
    private sealed record StartupSettings(bool AutoStartEnabled);
    private sealed record InterfacePreferences(string Theme, string Motion, int? DesktopTransparency = null);
    private sealed record GroupRelationship(string Name, string? Course, bool Excluded);
    private sealed record WorkspaceSettings(string AccountId, Dictionary<string, GroupRelationship> Relationships, Dictionary<string, long> ReadVersions);
    private sealed record Rectangle(double X, double Y, double Width, double Height);
    private sealed record DesktopLayout(int Schema, Rectangle Tasks, Rectangle Pending);
    private sealed record ReminderDeliveryRecord(ReminderDelivery Delivery, int Attempts = 0, bool RemovalRequired = false, IReadOnlyList<ReminderMember>? OriginalMembers = null);
    private sealed record ReminderOperationRecord(string Request, TaskReminderPlan Result);
    private sealed record ReminderStoreData(Dictionary<string, TaskReminderPlan> Plans, Dictionary<string, ReminderDeliveryRecord> Deliveries, Dictionary<string, ReminderOperationRecord> Operations);
    private sealed record RestoreJournal(string AccountId, string TransactionDirectory, Dictionary<string, string?> OriginalSettings,
        bool Committed, string? OriginalBackupHistory = null);
}
