using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Reminders;

/// <summary>账号独立的时间计划及投递台账。工作区用同一锁协调事项变更和外部提交。</summary>
public sealed class AccountReminderService
{
    private const int MaximumAttempts = 3;
    private readonly object gate = new();
    private readonly AccountTaskStore tasks;
    private readonly string accountId;
    public string DatabasePath { get; }

    public AccountReminderService(string dataRoot, string accountId, AccountTaskStore tasks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        if (string.IsNullOrEmpty(accountId) || accountId.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("QQ 账号须为 ASCII 数字标识。", nameof(accountId));
        ArgumentNullException.ThrowIfNull(tasks);
        if (tasks.AccountId != accountId) throw new ArgumentException("事项存储属于另一个账号。", nameof(tasks));
        this.tasks = tasks;
        this.accountId = accountId;
        DatabasePath = Path.Combine(Path.GetFullPath(dataRoot), "accounts", accountId, "reminders.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Initialize();
    }

    public IReadOnlyList<TaskReminderPlan> Synchronize(DateTimeOffset now)
    {
        lock (gate) return Mutate(data => Synchronize(data, now));
    }

    public TaskReminderPlan Configure(string taskId, long expectedTaskVersion,
        long expectedPlanVersion, ReminderPreference preference, string operationId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(preference);
        lock (gate) return Mutate(data =>
        {
            var request = JsonSerializer.Serialize(new { taskId, expectedTaskVersion, expectedPlanVersion, preference });
            if (data.Operations.TryGetValue(operationId, out var operation))
            {
                if (operation.Request != request) throw new InvalidOperationException("操作 ID 已用于其他提醒设置。");
                return operation.Result;
            }
            Synchronize(data, now);
            var task = tasks.FindTask(taskId) ?? throw new KeyNotFoundException("事项不存在。");
            var current = data.Plans[taskId];
            if (task.Version != expectedTaskVersion || current.PlanVersion != expectedPlanVersion)
                throw new InvalidOperationException("事项或提醒计划已变化，请刷新后重试。");
            var choice = Choice(preference);
            if (choice is HoursBefore && task.Time.Time is null)
                throw new ArgumentException("只有日期的信息不能设置提前小时提醒。", nameof(preference));
            var calculated = ReminderPlanner.Plan(task.Time, choice);
            var result = preference == current.Preference ? current : MakePlan(task, current.PlanVersion + 1, preference, calculated, now);
            data.Plans[taskId] = result;
            data.Operations.Add(operationId, new(request, result));
            return result;
        });
    }

    public IReadOnlyList<ReminderDelivery> DispatchDue(DateTimeOffset now, IReminderSink sink, bool recovering = false)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (gate)
        {
            // Only an explicit recovery interprets abandoned sending records as uncertain.
            // Merely opening/reading the store must not alter a live submission in another reader.
            Mutate(data =>
            {
                Synchronize(data, now);
                if (recovering)
                {
                    foreach (var pair in data.Deliveries.ToArray())
                        if (pair.Value.Delivery.State == "sending")
                        {
                            var old = pair.Value;
                            data.Deliveries[pair.Key] = old with { Delivery = old.Delivery with { State = "unknown", Error = "上次提交结果无法确认。" } };
                            SetNodeStates(data, old.Delivery, old.Delivery.Kind == "recovery" ? "covered" : "unknown");
                        }
                    CoalescePendingRecovery(data, now);
                }
                return 0;
            });
            ReconcileNotifications(sink, now);
            var submittedIds = new List<string>();
            // Failed summaries retain their original unsent members and one persisted attempt budget.
            // A later completion hides that member; undo can resume it without replaying successful members.
            var retries = Read().Deliveries.Values.Where(r => r.Attempts < MaximumAttempts &&
                (r.Delivery.State is "pending" or "failed" || r.OriginalMembers is not null &&
                    r.Delivery.State is "submitted" or "removed" or "superseded"))
                .Select(r => r.Delivery.Id).ToArray();
            foreach (var id in retries)
                if (Submit(id, sink, now)) submittedIds.Add(id);

            var groups = Mutate(data =>
            {
                Synchronize(data, now);
                var members = DueMembers(data, now, recovering);
                return recovering && members.Length > 0 ? new[] { members } : members.Select(m => new[] { m }).ToArray();
            });
            foreach (var members in groups)
            {
                var id = Mutate(data =>
                {
                    Synchronize(data, now);
                    var valid = members.Where(m => MemberValid(data, m) && m.NodeIds.All(n =>
                        data.Plans[m.TaskId].Nodes.Any(node => node.Id == n && (node.State == "pending" || recovering && node.State == "unknown"))))
                        .ToArray();
                    if (valid.Length == 0) return null;
                    var kind = recovering ? "recovery" : "time";
                    var deliveryId = Identity(new { accountId, kind, Nodes = valid.SelectMany(m => m.NodeIds).Order(StringComparer.Ordinal).ToArray() });
                    if (data.Deliveries.TryGetValue(deliveryId, out var previous) && previous.Delivery.State != "superseded") return null;
                    var delivery = new ReminderDelivery(deliveryId, accountId, kind, valid, "pending");
                    data.Deliveries[deliveryId] = new(delivery, previous?.Attempts ?? 0);
                    return deliveryId;
                });
                if (id is not null && Submit(id, sink, now)) submittedIds.Add(id);
            }
            var latest = Read();
            return submittedIds.Select(id => latest.Deliveries[id].Delivery).ToArray();
        }
    }

    public void ReconcileNotifications(IReminderSink sink, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (gate)
        {
            var cleanup = Mutate(data =>
            {
                Synchronize(data, now);
                var ids = new List<string>();
                foreach (var pair in data.Deliveries.ToArray())
                {
                    var record = pair.Value;
                    if (record.Delivery.State == "removed") continue;
                    if (record.RemovalRequired || !record.Delivery.Members.All(m => MemberValid(data, m)))
                    {
                        if (record.Delivery.State is "submitted" or "unknown" || record.Delivery.NotificationId is not null)
                        {
                            data.Deliveries[pair.Key] = record with { RemovalRequired = true };
                            ids.Add(pair.Key);
                        }
                        else if (record.Delivery.State is "pending" or "failed" &&
                            !record.Delivery.Members.All(m => MemberMatchesPlan(data, m)) &&
                            !(record.OriginalMembers?.Any(m => MemberMatchesPlan(data, m)) ?? false))
                            data.Deliveries[pair.Key] = record with { Delivery = record.Delivery with { State = "superseded" } };
                    }
                }
                return ids;
            });
            foreach (var id in cleanup)
            {
                var record = Read().Deliveries[id];
                try
                {
                    sink.Remove(record.Delivery.NotificationId ?? id);
                    Mutate(data =>
                    {
                        var latest = data.Deliveries[id];
                        data.Deliveries[id] = latest with { RemovalRequired = false, Delivery = latest.Delivery with { State = "removed", Error = null } };
                        return 0;
                    });
                }
                catch (Exception error)
                {
                    Mutate(data =>
                    {
                        var latest = data.Deliveries[id];
                        data.Deliveries[id] = latest with { RemovalRequired = true, Delivery = latest.Delivery with { Error = "通知清理失败：" + error.Message } };
                        return 0;
                    });
                }
            }
        }
    }

    public IReadOnlyList<ReminderDelivery> Deliveries()
    {
        lock (gate) return Read().Deliveries.Values.Select(r => r.Delivery).ToArray();
    }

    private ReminderMember[] DueMembers(StoreData data, DateTimeOffset now, bool recovering)
        => data.Plans.Values.Where(p => p.Active).OrderBy(p => p.TaskId, StringComparer.Ordinal)
            .Select(p => new ReminderMember(p.TaskId, tasks.FindTask(p.TaskId)!.Title, p.TimeVersion, p.PlanVersion,
                p.Nodes.Where(n => n.ScheduledAt <= now && (n.State == "pending" || recovering && n.State == "unknown"))
                    .OrderBy(n => n.ScheduledAt).Select(n => n.Id).ToArray()))
            .Where(m => m.NodeIds.Count > 0).ToArray();

    private void CoalescePendingRecovery(StoreData data, DateTimeOffset now)
    {
        var pending = data.Deliveries.Values.Where(r => r.Delivery.State == "pending").ToArray();
        var original = pending.FirstOrDefault(r => r.Delivery.Kind == "recovery");
        // A pending record has not reached the external sink. Its nodes can join this recovery.
        foreach (var record in pending)
            data.Deliveries[record.Delivery.Id] = record with { Delivery = record.Delivery with { State = "superseded" } };
        if (original is null) return;
        var members = DueMembers(data, now, recovering: true);
        if (members.Length > 0)
            data.Deliveries[original.Delivery.Id] = original with
            {
                Delivery = original.Delivery with { Members = members, NotificationId = null, Error = null }
            };
    }

    private bool Submit(string id, IReminderSink sink, DateTimeOffset now)
    {
        var sending = Mutate(data =>
        {
            Synchronize(data, now);
            var record = data.Deliveries[id];
            var members = RetryMembers(data, record, now);
            if (record.Attempts >= MaximumAttempts || members.Length == 0) return null;
            var delivery = record.Delivery with
            {
                State = "sending", Error = null,
                Members = members
            };
            data.Deliveries[id] = record with { Delivery = delivery, Attempts = record.Attempts + 1 };
            SetNodeStates(data, delivery, "sending");
            return delivery;
        });
        if (sending is null) return false;
        // The database commit above is complete before calling the non-transactional external sink.
        // The workspace lock spans this check, the call and reconciliation.
        var stillValid = Mutate(data =>
        {
            Synchronize(data, now);
            return sending.Members.All(m => MemberValid(data, m));
        });
        if (!stillValid)
        {
            Mutate(data =>
            {
                data.Deliveries[id] = data.Deliveries[id] with { Delivery = sending with { State = "superseded" } };
                SetNodeStates(data, sending, "pending");
                return 0;
            });
            return false;
        }
        ReminderSubmission result;
        try { result = sink.Submit(sending); }
        catch (Exception error) { result = new("unknown", Error: error.Message); }
        if (result is null || result.State is not ("submitted" or "blocked" or "failed" or "unknown"))
            result = new("unknown", Error: "投递端返回了无法确认的结果。");
        Mutate(data =>
        {
            var record = data.Deliveries[id];
            var completed = sending with { State = result.State, NotificationId = result.NotificationId, Error = result.Error };
            data.Deliveries[id] = record with
            {
                Delivery = completed,
                OriginalMembers = record.OriginalMembers ?? (result.State == "failed" ? sending.Members : null)
            };
            SetNodeStates(data, completed, completed.Kind == "recovery" && result.State == "unknown" ? "covered" : result.State);
            return 0;
        });
        // Direct task-store writes do not share the workspace lock. Re-read them after the external call too.
        ReconcileNotifications(sink, now);
        return true;
    }

    private ReminderMember[] RetryMembers(StoreData data, DeliveryRecord record, DateTimeOffset now)
        => (record.OriginalMembers ?? record.Delivery.Members).Where(m => MemberValid(data, m))
            .Select(m => m with
            {
                Title = tasks.FindTask(m.TaskId)!.Title,
                NodeIds = m.NodeIds.Where(id => data.Plans[m.TaskId].Nodes.Any(n => n.Id == id && n.ScheduledAt <= now &&
                    (n.State is "pending" or "failed" || record.Delivery.State == "pending" && n.State == "unknown"))).ToArray()
            }).Where(m => m.NodeIds.Count > 0).ToArray();

    private IReadOnlyList<TaskReminderPlan> Synchronize(StoreData data, DateTimeOffset now)
    {
        var latest = tasks.ListTasks();
        var ids = latest.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var task in latest)
        {
            data.Plans.TryGetValue(task.Id, out var current);
            if (current is null || current.TimeVersion != task.TimeVersion)
            {
                var preference = current?.Preference ?? new();
                ReminderPlan calculated;
                try { calculated = ReminderPlanner.Plan(task.Time, Choice(preference)); }
                catch (ArgumentException) { calculated = ReminderPlanner.Plan(task.Time, new DefaultReminder()) with { NeedsReview = true }; }
                data.Plans[task.Id] = MakePlan(task, (current?.PlanVersion ?? 0) + 1, preference, calculated, now);
            }
            else data.Plans[task.Id] = current with { Active = Active(task, current.Preference, now) };
        }
        foreach (var id in data.Plans.Keys.Where(id => !ids.Contains(id)).ToArray())
            data.Plans[id] = data.Plans[id] with { Active = false };
        return data.Plans.Values.OrderBy(p => p.TaskId, StringComparer.Ordinal).ToArray();
    }

    private TaskReminderPlan MakePlan(TaskItem task, long version, ReminderPreference preference, ReminderPlan calculated, DateTimeOffset now)
        => new(task.Id, task.TimeVersion, version, preference, calculated.NeedsReview, Active(task, preference, now),
            calculated.Nodes.Order().Select(at => new ReminderNode(Identity(new { accountId, task.Id, task.TimeVersion,
                PlanVersion = version, preference, ScheduledAt = at }), at, "pending")).ToArray());

    private static bool Active(TaskItem task, ReminderPreference preference, DateTimeOffset now)
        => task.State == TaskState.Active && !task.Time.IsPast(now) && preference.Mode != "off";

    private static bool MemberValid(StoreData data, ReminderMember member)
        => MemberMatchesPlan(data, member) && data.Plans[member.TaskId].Active;

    private static bool MemberMatchesPlan(StoreData data, ReminderMember member)
        => data.Plans.TryGetValue(member.TaskId, out var plan) && plan.TimeVersion == member.TimeVersion &&
            plan.PlanVersion == member.PlanVersion && member.NodeIds.All(id => plan.Nodes.Any(n => n.Id == id));

    private static void SetNodeStates(StoreData data, ReminderDelivery delivery, string state)
    {
        foreach (var member in delivery.Members)
            if (data.Plans.TryGetValue(member.TaskId, out var plan) && plan.TimeVersion == member.TimeVersion && plan.PlanVersion == member.PlanVersion)
            {
                var ids = member.NodeIds.ToHashSet(StringComparer.Ordinal);
                data.Plans[member.TaskId] = plan with { Nodes = plan.Nodes.Select(n => ids.Contains(n.Id) ? n with { State = state } : n).ToArray() };
            }
    }

    private static ReminderChoice Choice(ReminderPreference preference)
    {
        if (preference.Mode is "default" or "off" && preference.Hours is null && preference.Days is null && preference.Clock is null)
            return preference.Mode == "off" ? new DisabledReminder() : new DefaultReminder();
        if (preference.Mode == "hours" && preference.Hours is { } hours && double.IsFinite(hours) && hours > 0 &&
            preference.Days is null && preference.Clock is null) return new HoursBefore(hours);
        if (preference.Mode == "calendar" && preference.Days is >= 0 && preference.Hours is null &&
            TimeOnly.TryParseExact(preference.Clock, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
            return new CalendarBefore(preference.Days.Value, clock);
        throw new ArgumentException("提醒规则或参数无效。", nameof(preference));
    }

    private static string Identity(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private sealed record DeliveryRecord(ReminderDelivery Delivery, int Attempts = 0, bool RemovalRequired = false,
        IReadOnlyList<ReminderMember>? OriginalMembers = null);
    private sealed record OperationRecord(string Request, TaskReminderPlan Result);
    private sealed record StoreData(Dictionary<string, TaskReminderPlan> Plans,
        Dictionary<string, DeliveryRecord> Deliveries, Dictionary<string, OperationRecord> Operations)
    {
        public static StoreData Empty() => new(new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(StringComparer.Ordinal));
    }

    private void Initialize()
    {
        var exists = File.Exists(DatabasePath);
        using var connection = Open(create: !exists);
        using var transaction = connection.BeginTransaction();
        if (!exists)
        {
            Execute(connection, transaction, "CREATE TABLE store_metadata(account_id TEXT PRIMARY KEY NOT NULL); CREATE TABLE reminder_state(id INTEGER PRIMARY KEY CHECK(id=1), record_json TEXT NOT NULL); PRAGMA user_version=1;");
            Execute(connection, transaction, "INSERT INTO store_metadata(account_id) VALUES($value);", accountId);
            Execute(connection, transaction, "INSERT INTO reminder_state(id,record_json) VALUES(1,$value);", JsonSerializer.Serialize(StoreData.Empty()));
        }
        else
        {
            using var schema = Command(connection, transaction, "PRAGMA user_version;");
            if (Convert.ToInt32(schema.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("提醒数据库版本不受支持，原文件已保留。");
            using var owner = Command(connection, transaction, "SELECT account_id FROM store_metadata;");
            using var reader = owner.ExecuteReader();
            if (!reader.Read() || reader.GetString(0) != accountId || reader.Read())
                throw new InvalidDataException("提醒数据库账号不匹配，原文件已保留。");
        }
        using (var check = Command(connection, transaction, "PRAGMA quick_check;"))
            if (!string.Equals(check.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                throw new InvalidDataException("提醒数据库损坏，原文件已保留。");
        Read(connection, transaction);
        transaction.Commit();
    }

    private StoreData Read()
    {
        using var connection = Open();
        return Read(connection, null);
    }

    private static StoreData Read(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, "SELECT record_json FROM reminder_state WHERE id=1;");
        var json = command.ExecuteScalar() as string ?? throw new InvalidDataException("提醒数据库缺少状态记录。");
        try
        {
            var data = JsonSerializer.Deserialize<StoreData>(json);
            if (data?.Plans is null || data.Deliveries is null || data.Operations is null)
                throw new InvalidDataException("提醒数据库状态为空。");
            return data;
        }
        catch (JsonException error) { throw new InvalidDataException("提醒数据库状态损坏，原文件已保留。", error); }
    }

    private T Mutate<T>(Func<StoreData, T> action)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var data = Read(connection, transaction);
        var result = action(data);
        Execute(connection, transaction, "UPDATE reminder_state SET record_json=$value WHERE id=1;", JsonSerializer.Serialize(data));
        transaction.Commit();
        return result;
    }

    private SqliteConnection Open(bool create = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite, Pooling = false
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, string? value = null)
    {
        using var command = Command(connection, transaction, sql);
        if (value is not null) command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
