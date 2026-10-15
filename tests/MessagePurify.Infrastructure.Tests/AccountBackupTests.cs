using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Backup;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Reminders;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class AccountBackupTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string Group = "9007199254740995";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(8));
    private static readonly TaskTime Time = new(TimeKind.Deadline, new(2026, 10, 20), new(18, 0), "Asia/Shanghai");
    private static readonly BackupApplicationSettings Preferences = new(false, "dark", "reduce",
        "{\"schema\":1,\"tasks\":{\"x\":10,\"y\":20,\"width\":340,\"height\":360},\"pending\":{\"x\":400,\"y\":20,\"width\":340,\"height\":360}}");
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-backup-" + Guid.NewGuid().ToString("N"));
    private string AccountDirectory => Path.Combine(root, "accounts", Account);
    private string Package => Path.Combine(root, "export.zip");
    private string SettingsJson => JsonSerializer.Serialize(new
    {
        AccountId = Account,
        Relationships = new Dictionary<string, object> { [Group] = new { Name = "数学群", Course = "数学", Excluded = false } },
        ReadVersions = new Dictionary<string, long> { ["task:manual"] = 5 }
    });

    [Fact]
    public void ExportRestore_PreservesAllLocalStatesHistoryOverridesAndOriginalUndoDeadline()
    {
        Seed();
        var before = DumpAccount();
        var undo = Tasks().ListTasks(includeMerged: true).Single(task => task.State == TaskState.Completed).UndoExpiresAt;
        var exported = Service().Export(Package, SettingsJson, Preferences, Now);
        Assert.True(exported.IsComplete);
        Assert.Equal(4, exported.TaskCount);
        Assert.Equal(4, exported.ReviewCount);
        Assert.Equal(1, exported.NoticeCount);
        Assert.Equal(exported, Service().GetLastResult());
        Assert.Equal("secret-do-not-export", File.ReadAllText(Path.Combine(root, "napcat-token.txt")));
        using (var zip = ZipFile.OpenRead(Package))
        {
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("token", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("active-account", StringComparison.Ordinal));
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("game-catalog", StringComparison.Ordinal));
        }
        using var prepared = Service().Inspect(Package);
        Assert.Equal(Preferences, prepared.ApplicationSettings);
        Tasks().CreateManualTask("new-after-backup", "新任务", "数学", Time, null, "new-operation", Now.AddMinutes(1));
        var changed = DumpAccount();
        BackupApplicationSettings? applied = null;
        var result = Service().Restore(prepared, SettingsJson, new(true, "light", "system", null), Now.AddDays(2), settings => applied = settings);

        Assert.Equal(before, DumpAccount());
        Assert.Equal(undo, Tasks().ListTasks(includeMerged: true).Single(task => task.State == TaskState.Completed).UndoExpiresAt);
        Assert.Equal(Preferences, applied);
        Assert.NotNull(result.PreRestoreBackupPath);
        Assert.True(File.Exists(result.PreRestoreBackupPath));
        using var automatic = Service().Inspect(result.PreRestoreBackupPath!);
        Assert.Equal(5, automatic.Preview.TaskCount);
        Assert.NotEqual(before, changed);
        Assert.Equal(result, Service().GetLastResult());
        Assert.True(JsonNode.Parse(File.ReadAllText(Path.Combine(root, "interface-preferences.json")))!["theme"]!.GetValue<string>() == "dark");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(100)]
    public void DesktopTransparencySurvivesBackupRestoreAndPreRestoreProtection(int? transparency)
    {
        Seed();
        var saved = Preferences with { DesktopTransparency = transparency };
        Service().Export(Package, SettingsJson, saved, Now);
        using var prepared = Service().Inspect(Package);
        Assert.Equal(saved, prepared.ApplicationSettings);
        var result = Service().Restore(prepared, SettingsJson, Preferences with { DesktopTransparency = 25 }, Now.AddDays(1));
        var preferences = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "interface-preferences.json")))!;
        Assert.Equal(transparency, preferences["desktopTransparency"]?.GetValue<int>());
        using var before = Service().Inspect(result.PreRestoreBackupPath!);
        Assert.Equal(25, before.ApplicationSettings.DesktopTransparency);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidDesktopTransparencyIsRejectedBeforeCreatingABackup(int transparency)
    {
        Seed();
        Assert.Throws<InvalidDataException>(() => Service().Export(Package, SettingsJson,
            Preferences with { DesktopTransparency = transparency }, Now));
        Assert.False(File.Exists(Package));
    }

    [Fact]
    public void Export_UnavailableOriginalImageIsExplicitAndPreservesReference()
    {
        Seed();
        Route("image", RuleDisposition.Pending, image: true);
        var result = Service().Export(Package, SettingsJson, Preferences, Now);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.MissingImageCount);
        using var zip = ZipFile.OpenRead(Package);
        using var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        var manifest = JsonNode.Parse(reader.ReadToEnd())!;
        var image = Assert.Single(manifest["evidence"]!.AsArray());
        Assert.Equal("not-downloaded", image!["status"]!.GetValue<string>());
        Assert.Equal("napcat-file-id", image["assetId"]!.GetValue<string>());
        Assert.Null(image["path"]);
        Assert.Null(image["sha256"]);
        using var prepared = Service().Inspect(Package);
        Assert.False(prepared.Preview.IsComplete);
        Assert.Equal(1, prepared.Preview.MissingImageCount);
    }

    [Fact]
    public async Task Export_FourDatabaseSnapshotKeepsOneCommittedVersionDuringConcurrentWrites()
    {
        Seed();
        var databases = new[] { "tasks.sqlite3", "messages.sqlite3", "routing.sqlite3", "reminders.sqlite3" };
        foreach (var database in databases)
            Execute(Path.Combine(AccountDirectory, database), "CREATE TABLE snapshot_probe(value INTEGER NOT NULL); INSERT INTO snapshot_probe VALUES(0);");
        using var stopping = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(() =>
        {
            using var connection = Open(Path.Combine(AccountDirectory, databases[0]));
            for (var index = 1; index < databases.Length; index++)
            {
                using var attach = connection.CreateCommand();
                attach.CommandText = "ATTACH DATABASE $path AS db" + index + ";";
                attach.Parameters.AddWithValue("$path", Path.Combine(AccountDirectory, databases[index]));
                attach.ExecuteNonQuery();
            }
            var version = 0;
            while (!stopping.IsCancellationRequested)
            {
                using var transaction = connection.BeginTransaction(deferred: false);
                foreach (var alias in new[] { "main", "db1", "db2", "db3" })
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = "UPDATE " + alias + ".snapshot_probe SET value=$version;";
                    update.Parameters.AddWithValue("$version", version);
                    update.ExecuteNonQuery();
                }
                transaction.Commit();
                version++;
                started.TrySetResult();
            }
        });
        try
        {
            await started.Task;
            for (var sample = 0; sample < 4; sample++)
            {
                var package = Path.Combine(root, "concurrent-" + sample + ".zip");
                Service().Export(package, SettingsJson, Preferences, Now);
                var output = Path.Combine(root, "snapshot-" + sample);
                ZipFile.ExtractToDirectory(package, output);
                var values = databases.Select(database =>
                {
                    using var connection = Open(Path.Combine(output, "account", database));
                    using var read = connection.CreateCommand();
                    read.CommandText = "SELECT value FROM snapshot_probe;";
                    return Convert.ToInt64(read.ExecuteScalar());
                }).ToArray();
                Assert.Single(values.Distinct());
            }
        }
        finally { stopping.Cancel(); await writer; }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("hash")]
    [InlineData("account")]
    [InlineData("format")]
    [InlineData("schema")]
    [InlineData("schema-columns")]
    [InlineData("settings-json")]
    [InlineData("task-json")]
    [InlineData("evidence-owner")]
    [InlineData("evidence-missing")]
    [InlineData("reminder-json")]
    public void Inspect_RejectsInvalidPackageAndPreservesCurrentAccount(string corruption)
    {
        Seed();
        Service().Export(Package, SettingsJson, Preferences, Now);
        var before = DumpAccount();
        var unpacked = Path.Combine(root, "edit");
        ZipFile.ExtractToDirectory(Package, unpacked);
        var manifestPath = Path.Combine(unpacked, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        var modifiedPath = "account/tasks.sqlite3";
        switch (corruption)
        {
            case "missing": File.Delete(Path.Combine(unpacked, modifiedPath)); break;
            case "hash": File.AppendAllText(Path.Combine(unpacked, modifiedPath), "corrupt"); break;
            case "account": manifest["accountId"] = "999"; break;
            case "format": manifest["formatVersion"] = 999; break;
            case "schema": Execute(Path.Combine(unpacked, modifiedPath), "PRAGMA user_version=999;"); break;
            case "schema-columns":
                modifiedPath = "account/messages.sqlite3";
                Execute(Path.Combine(unpacked, modifiedPath), "ALTER TABLE messages RENAME COLUMN received_at TO unsupported_received_at;"); break;
            case "settings-json":
                modifiedPath = "account/workspace-settings.json";
                File.WriteAllText(Path.Combine(unpacked, modifiedPath), "{\"AccountId\":\"" + Account + "\"}"); break;
            case "task-json": Execute(Path.Combine(unpacked, modifiedPath), "UPDATE tasks SET record_json='{}' WHERE rowid=1;"); break;
            case "evidence-owner": Execute(Path.Combine(unpacked, modifiedPath), "UPDATE task_evidence SET account_id='999';"); break;
            case "evidence-missing": Execute(Path.Combine(unpacked, modifiedPath), "UPDATE task_evidence SET message_id='missing' WHERE rowid=1;"); break;
            case "reminder-json":
                modifiedPath = "account/reminders.sqlite3";
                Execute(Path.Combine(unpacked, modifiedPath), "UPDATE reminder_state SET record_json='{}';"); break;
        }
        if (corruption is not ("missing" or "hash" or "account" or "format")) Rehash(manifest, unpacked, modifiedPath);
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        File.Delete(Package);
        ZipFile.CreateFromDirectory(unpacked, Package);

        Assert.Throws<InvalidDataException>(() => Service().Inspect(Package));
        Assert.Equal(before, DumpAccount());
    }

    [Fact]
    public void Restore_SettingsCallbackFailureRollsBackAccountAndEveryOriginalSettingsFile()
    {
        Seed();
        Service().Export(Package, SettingsJson, Preferences, Now);
        using var prepared = Service().Inspect(Package);
        Tasks().CreateManualTask("later", "之后的任务", "数学", Time, null, "later-op", Now);
        File.WriteAllText(Path.Combine(root, "app-settings.json"), "{\"AutoStartEnabled\":true}");
        File.WriteAllText(Path.Combine(root, "desktop-layout.json"), "original-layout");
        var original = DumpAccount();
        var originalPreferences = new BackupApplicationSettings(true, "light", "system", Preferences.DesktopLayoutJson);
        var callbacks = new List<BackupApplicationSettings>();

        Assert.Throws<IOException>(() => Service().Restore(prepared, SettingsJson, originalPreferences, Now, settings =>
        {
            callbacks.Add(settings);
            if (settings == Preferences) throw new IOException("settings failed");
        }));

        Assert.Equal(original, DumpAccount());
        Assert.Equal("{\"AutoStartEnabled\":true}", File.ReadAllText(Path.Combine(root, "app-settings.json")));
        Assert.Equal("original-layout", File.ReadAllText(Path.Combine(root, "desktop-layout.json")));
        Assert.False(File.Exists(Path.Combine(root, "interface-preferences.json")));
        Assert.Equal(new[] { Preferences, originalPreferences }, callbacks);
    }

    [Fact]
    public void Restore_OpenOriginalDatabasePreventsSwitchAndPreservesCurrentData()
    {
        Seed();
        Service().Export(Package, SettingsJson, Preferences, Now);
        using var prepared = Service().Inspect(Package);
        var before = DumpAccount();
        using (var locked = new FileStream(Path.Combine(AccountDirectory, "tasks.sqlite3"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => Service().Restore(prepared, SettingsJson, Preferences, Now));
        Assert.Equal(before, DumpAccount());
    }

    [Fact]
    public void Restore_ResultRecordFailureRollsBackBeforeCommittingTheSwitch()
    {
        Seed();
        Service().Export(Package, SettingsJson, Preferences, Now);
        using var prepared = Service().Inspect(Package);
        Tasks().CreateManualTask("later", "之后的任务", "数学", Time, null, "later-op", Now);
        var original = DumpAccount();
        FileStream? lockedHistory = null;
        try
        {
            Assert.Throws<IOException>(() => Service().Restore(prepared, SettingsJson, Preferences, Now, settings =>
            {
                lockedHistory ??= new FileStream(Path.Combine(root, "backup-history.json"), FileMode.Open, FileAccess.Read, FileShare.None);
            }));
            Assert.Equal(original, DumpAccount());
        }
        finally { lockedHistory?.Dispose(); }
        Assert.Null(Service().GetLastResult()!.PreRestoreBackupPath);
        Assert.Contains("pre-restore-", Service().GetLastResult()!.Path, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "backup-restore-" + Account + ".json")));
    }

    [Fact]
    public void Export_CannotReplaceCurrentAccountDatabase()
    {
        Seed();
        var before = DumpAccount();
        Assert.Throws<ArgumentException>(() => Service().Export(Path.Combine(AccountDirectory, "tasks.sqlite3"), SettingsJson, Preferences, Now));
        Assert.Equal(before, DumpAccount());
    }

    [Fact]
    public void Inspect_DisposedPreviewCannotRestore()
    {
        Seed();
        Service().Export(Package, SettingsJson, Preferences, Now);
        var prepared = Service().Inspect(Package);
        prepared.Dispose();
        Assert.Throws<ObjectDisposedException>(() => Service().Restore(prepared, SettingsJson, Preferences, Now));
    }

    [Fact]
    public void RecoverInterrupted_RestoresOriginalDirectoryAndAbsentOrPresentSettings()
    {
        Seed();
        var original = DumpAccount();
        var transactionName = Guid.NewGuid().ToString("N");
        var transaction = Path.Combine(root, ".backup-work", transactionName);
        Directory.CreateDirectory(transaction);
        Directory.Move(AccountDirectory, Path.Combine(transaction, "account-original"));
        Directory.CreateDirectory(AccountDirectory);
        File.WriteAllText(Path.Combine(AccountDirectory, "bad-new-data"), "new");
        File.WriteAllText(Path.Combine(root, "app-settings.json"), "new-settings");
        File.WriteAllText(Path.Combine(root, "interface-preferences.json"), "new-preferences");
        File.WriteAllText(Path.Combine(root, "backup-restore-" + Account + ".json"), JsonSerializer.Serialize(new
        {
            AccountId = Account, TransactionDirectory = transactionName, Committed = false,
            OriginalSettings = new Dictionary<string, string?>
            {
                ["app-settings.json"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("original-settings")),
                ["interface-preferences.json"] = null, ["desktop-layout.json"] = null
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        AccountBackupService.RecoverInterrupted(root);

        Assert.Equal(original, DumpAccount());
        Assert.Equal("original-settings", File.ReadAllText(Path.Combine(root, "app-settings.json")));
        Assert.False(File.Exists(Path.Combine(root, "interface-preferences.json")));
        Assert.False(File.Exists(Path.Combine(root, "backup-restore-" + Account + ".json")));
        AccountBackupService.RecoverInterrupted(root);
        Assert.Equal(original, DumpAccount());
    }

    private AccountBackupService Service() => new(root, Account);
    private AccountTaskStore Tasks() => new(root, Account);

    private void Seed()
    {
        _ = new AccountWorkspace(root, Account);
        foreach (var state in new[] { TaskState.Active, TaskState.Completed, TaskState.Cancelled, TaskState.Archived })
        {
            Route("task-" + state, RuleDisposition.Task);
            var task = Tasks().ListTasks().Single(item => item.Title == "材料task-" + state);
            if (state == TaskState.Completed) Tasks().Complete(task.Id, "complete", task.Version, Now);
            if (state == TaskState.Cancelled) Tasks().Cancel(task.Id, "cancel", task.Version, Now);
            if (state == TaskState.Archived) Tasks().Archive(task.Id, "archive", task.Version, Now);
        }
        foreach (var state in new[] { ReviewState.Pending, ReviewState.Ignored, ReviewState.Accepted, ReviewState.Rejected })
        {
            var target = state == ReviewState.Accepted ? Tasks().ListTasks().Single(task => task.State == TaskState.Active) : null;
            Route("review-" + state, RuleDisposition.Pending, linkedTaskId: target?.Id);
            var review = Tasks().ListReviews().Single(item => item.Candidate.Title == "材料review-" + state);
            if (state == ReviewState.Ignored) Tasks().IgnoreReview(review.Id, "ignore", review.Version, Now);
            if (state == ReviewState.Rejected) Tasks().RejectReview(review.Id, "reject", review.Version, Now);
            if (state == ReviewState.Accepted)
                Tasks().AcceptReview(review.Id, target!.Id, "accept", review.Version, Time, Now, target.Version);
        }
        Route("notice", RuleDisposition.Notice);
        var active = Tasks().ListTasks().Single(item => item.State == TaskState.Active);
        Tasks().EditTask(active.Id, "manual-edit", active.Version, "人工覆盖标题", "数学", Time, 0, Now);
        var reminders = new AccountReminderService(root, Account, Tasks());
        reminders.Synchronize(Now);
        reminders.DispatchDue(Now.AddDays(7), new TestSink());
        File.WriteAllText(Path.Combine(AccountDirectory, "workspace-settings.json"), SettingsJson);
        File.WriteAllText(Path.Combine(root, "napcat-token.txt"), "secret-do-not-export");
        File.WriteAllText(Path.Combine(root, "active-account.txt"), Account);
        File.WriteAllText(Path.Combine(root, "game-catalog.json"), "{}");
    }

    private void Route(string id, RuleDisposition disposition, bool image = false, string? linkedTaskId = null)
    {
        var messages = new AccountMessageStore(root, Account);
        var segments = new List<MessageSegment> { new(SegmentKind.Text, "合成消息") };
        if (image) segments.Add(new(SegmentKind.Image, "", AssetId: "napcat-file-id"));
        var message = new SchoolMessage(new(Account, Group, id), "12345", Now.AddDays(-1), segments);
        Assert.True(messages.TryEnqueue(message, MessageOrigin.History, "synthetic", Now));
        var reliable = new RuleAssessment("合成可靠信息", true);
        var candidate = new RuleCandidate("材料" + id, disposition == RuleDisposition.Notice ? RuleAction.Inform : RuleAction.Complete,
            disposition, "数学", Time, null, linkedTaskId, 1, new(reliable, reliable, reliable, reliable, reliable, reliable), [], [message.Key]);
        Assert.True(messages.TrySaveExtraction(message.Key, 0, new(MessageRuleExtractor.RuleVersion, [candidate]), Now));
        Assert.Equal(1, new AccountCandidateRouter(messages, Tasks()).RoutePending(new(Account, Group, "Asia/Shanghai", Now, "数学"), new HashSet<string> { Group }));
    }

    private string DumpAccount()
    {
        var values = new List<string>();
        foreach (var filename in new[] { "messages.sqlite3", "tasks.sqlite3", "routing.sqlite3", "reminders.sqlite3" })
        {
            using var connection = Open(Path.Combine(AccountDirectory, filename));
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            var tables = new List<string>();
            using (var reader = command.ExecuteReader()) while (reader.Read()) tables.Add(reader.GetString(0));
            foreach (var table in tables)
            {
                command.CommandText = "SELECT * FROM " + table + " ORDER BY rowid;";
                using var reader = command.ExecuteReader();
                while (reader.Read()) values.Add(filename + ":" + table + ":" + string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i).ToString())));
            }
        }
        values.Add(File.ReadAllText(Path.Combine(AccountDirectory, "workspace-settings.json")));
        return string.Join("\n", values);
    }

    private static void Rehash(JsonNode manifest, string directory, string path)
    {
        var file = manifest["files"]!.AsArray().Single(node => node!["path"]!.GetValue<string>() == path)!;
        var bytes = File.ReadAllBytes(Path.Combine(directory, path));
        file["length"] = bytes.LongLength;
        file["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = false }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(string path, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private sealed class TestSink : IReminderSink
    {
        public ReminderSubmission Submit(ReminderDelivery delivery) => new("submitted", "synthetic-notification");
        public void Remove(string notificationId) { }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
