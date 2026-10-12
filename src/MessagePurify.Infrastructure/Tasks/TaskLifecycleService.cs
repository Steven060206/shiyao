using System.Globalization;
using System.Text.Json;
using MessagePurify.Core;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tasks;

/// <summary>账号独立的正式事项、待确认建议及操作历史；每次写入原子提交。</summary>
public sealed class AccountTaskStore
{
    public string AccountId { get; }
    public string DatabasePath { get; }

    public AccountTaskStore(string dataRoot, string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        if (string.IsNullOrEmpty(accountId) || accountId.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("QQ 账号须为 ASCII 数字标识。", nameof(accountId));
        AccountId = accountId;
        DatabasePath = Path.Combine(Path.GetFullPath(dataRoot), "accounts", accountId, "tasks.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Initialize();
    }

    public TaskItem CreateTask(string id, RuleCandidate candidate, string operationId, DateTimeOffset now)
        => InTransaction((connection, transaction) => CreateTaskInTransaction(connection, transaction, id, candidate, operationId, now));

    internal TaskItem CreateTaskInTransaction(SqliteConnection connection, SqliteTransaction transaction,
        string id, RuleCandidate candidate, string operationId, DateTimeOffset now)
    {
        ValidateCandidate(candidate);
        if (candidate.Disposition != RuleDisposition.Task || candidate.TargetTaskId is not null ||
            candidate.Action is RuleAction.Cancel or RuleAction.Postpone or RuleAction.NewRequirement or RuleAction.Inform ||
            !candidate.Assessments.Action.Reliable || !candidate.Assessments.Audience.Reliable ||
            !candidate.Assessments.Course.Reliable || !candidate.Assessments.Time.Reliable)
            throw new ArgumentException("只有可靠的新任务候选可以自动建立正式事项。", nameof(candidate));
        ValidateTime(candidate.Time);
        return WriteInTransaction<TaskItem>(connection, transaction, operationId,
            new { Kind = "create", Id = id, Candidate = Canonical(candidate) }, "task", (connection, transaction) =>
        {
            ValidateId(id);
            var duplicate = FindBySource<TaskItem>(connection, transaction, "tasks", Canonical(candidate));
            if (duplicate is not null) return new(duplicate, duplicate.Id);
            if (Read<TaskItem>(connection, transaction, "tasks", id) is not null)
                throw new InvalidOperationException("事项 ID 已被其他候选使用。");
            var task = NewTask(id, candidate, candidate.Time!, false, now);
            Insert(connection, transaction, "tasks", id, task, Canonical(candidate));
            AddEvidence(connection, transaction, task);
            return new(task, task.Id);
        }, now);
    }

    public PendingReview SavePending(string id, RuleCandidate candidate, string operationId, DateTimeOffset now)
        => InTransaction((connection, transaction) => SavePendingInTransaction(connection, transaction, id, candidate, operationId, now));

    internal PendingReview SavePendingInTransaction(SqliteConnection connection, SqliteTransaction transaction,
        string id, RuleCandidate candidate, string operationId, DateTimeOffset now)
    {
        ValidateCandidate(candidate);
        if (candidate.Disposition is not (RuleDisposition.Pending or RuleDisposition.Change))
            throw new ArgumentException("此入口只保存待确认或关联变更候选。", nameof(candidate));
        return WriteInTransaction<PendingReview>(connection, transaction, operationId,
            new { Kind = "pending", Id = id, Candidate = Canonical(candidate) }, "review", (connection, transaction) =>
        {
            ValidateId(id);
            var duplicate = FindBySource<PendingReview>(connection, transaction, "reviews", Canonical(candidate));
            if (duplicate is not null) return new(duplicate, duplicate.Id);
            if (Read<PendingReview>(connection, transaction, "reviews", id) is not null)
                throw new InvalidOperationException("待确认 ID 已被其他候选使用。");
            var review = new PendingReview(id, AccountId, Normalize(candidate), ReviewState.Pending, now, null, null, 1);
            Insert(connection, transaction, "reviews", id, review, Canonical(candidate));
            return new(review, review.Id);
        }, now);
    }

    public TaskItem? FindTask(string id) => Find<TaskItem>("tasks", id);
    public PendingReview? FindReview(string id) => Find<PendingReview>("reviews", id);
    public IReadOnlyList<TaskItem> ListTasks(TaskState? state = null) =>
        List<TaskItem>("tasks").Where(item => state is null || item.State == state).ToArray();
    public IReadOnlyList<PendingReview> ListReviews(ReviewState? state = null) =>
        List<PendingReview>("reviews").Where(item => state is null || item.State == state).ToArray();

    public TaskItem Complete(string id, string operationId, long expectedVersion, DateTimeOffset now) =>
        ChangeTask(id, operationId, expectedVersion, now, "complete", null, task =>
        {
            var snapshot = TaskTransitions.Complete(Snapshot(task));
            if (snapshot.Version == task.Version) return task;
            return Apply(task, snapshot) with
            {
                CompletedAt = now, UndoExpiresAt = now.AddHours(1), UndoOperationId = operationId
            };
        });

    // 可传入界面保存的完成操作 ID，避免旧撤销入口影响新的完成记录。
    public TaskItem UndoComplete(string id, string operationId, long expectedVersion, DateTimeOffset now,
        string? completedOperationId = null) =>
        ChangeTask(id, operationId, expectedVersion, now, "undo-complete", completedOperationId, task =>
        {
            if (task.UndoExpiresAt is not { } expiry || task.UndoOperationId is null ||
                (completedOperationId is not null && task.UndoOperationId != completedOperationId)) return task;
            var result = TaskTransitions.UndoComplete(Snapshot(task), new(UndoKind.Complete, expiry, task.Version), now, false);
            return !result.Changed ? task : Apply(task, result.Current) with
            {
                CompletedAt = null, UndoExpiresAt = null, UndoOperationId = null
            };
        });

    /// <summary>用户直接取消。消息提出的取消须通过关联建议接受并追加证据。</summary>
    public TaskItem Cancel(string id, string operationId, long expectedVersion, DateTimeOffset now) =>
        ChangeTask(id, operationId, expectedVersion, now, "cancel", null, task =>
        {
            var snapshot = TaskTransitions.Cancel(Snapshot(task));
            return snapshot.Version == task.Version ? task : Apply(task, snapshot) with
            {
                CompletedAt = null, CancelledAt = now, ArchivedAt = null,
                UndoExpiresAt = null, UndoOperationId = null
            };
        });

    public TaskItem Archive(string id, string operationId, long expectedVersion, DateTimeOffset now) =>
        ChangeTask(id, operationId, expectedVersion, now, "archive", null, task =>
        {
            var snapshot = TaskTransitions.Archive(Snapshot(task));
            return snapshot.Version == task.Version ? task : Apply(task, snapshot) with
            {
                CompletedAt = null, ArchivedAt = now, UndoExpiresAt = null, UndoOperationId = null
            };
        });

    public TaskItem RestoreArchived(string id, string operationId, long expectedVersion, DateTimeOffset now) =>
        ChangeTask(id, operationId, expectedVersion, now, "restore", null, task =>
        {
            var snapshot = TaskTransitions.RestoreArchived(Snapshot(task));
            return snapshot.Version == task.Version ? task : Apply(task, snapshot) with
            {
                CompletedAt = null, ArchivedAt = null, UndoExpiresAt = null, UndoOperationId = null
            };
        });

    public TaskItem AcceptTime(string id, string operationId, long expectedVersion, TaskTime time, DateTimeOffset now)
    {
        ValidateTime(time);
        return ChangeTask(id, operationId, expectedVersion, now, "accept-time", time,
            task => Apply(task, TaskTransitions.AcceptTime(Snapshot(task), time)));
    }

    public PendingReview IgnoreReview(string id, string operationId, long expectedVersion, DateTimeOffset now) =>
        ChangeReview(id, operationId, expectedVersion, now, "ignore", null, review =>
            review.State != ReviewState.Pending ? review : review with
            {
                State = ReviewState.Ignored, IgnoredAt = now, UndoExpiresAt = now.AddHours(24),
                UndoOperationId = operationId, Version = review.Version + 1
            });

    public PendingReview UndoIgnored(string id, string operationId, long expectedVersion, DateTimeOffset now,
        string? ignoredOperationId = null) =>
        ChangeReview(id, operationId, expectedVersion, now, "undo-ignore", ignoredOperationId, review =>
            review.State != ReviewState.Ignored || review.UndoExpiresAt is not { } expiry ||
            review.UndoOperationId is null || (ignoredOperationId is not null && review.UndoOperationId != ignoredOperationId) ||
            !TaskTransitions.CanUndo(new(UndoKind.Ignore, expiry, review.Version), now, false)
                ? review : review with
                {
                    State = ReviewState.Pending, IgnoredAt = null, UndoExpiresAt = null,
                    UndoOperationId = null, Version = review.Version + 1
                });

    public PendingReview RejectReview(string id, string operationId, long expectedVersion, DateTimeOffset now) =>
        ChangeReview(id, operationId, expectedVersion, now, "reject", null, review =>
            review.State is ReviewState.Accepted or ReviewState.Rejected ? review : review with
            {
                State = ReviewState.Rejected, UndoExpiresAt = null, UndoOperationId = null, Version = review.Version + 1
            });

    public TaskItem AcceptReview(string reviewId, string taskId, string operationId, long expectedReviewVersion,
        TaskTime? confirmedTime, DateTimeOffset now, long? expectedTaskVersion = null)
    {
        ValidateId(reviewId);
        ValidateId(taskId);
        if (confirmedTime is not null) ValidateTime(confirmedTime);
        return Write<TaskItem>(operationId, new { Kind = "accept-review", ReviewId = reviewId, TaskId = taskId,
            ExpectedReviewVersion = expectedReviewVersion, ExpectedTaskVersion = expectedTaskVersion, Time = confirmedTime },
            "task", (connection, transaction) =>
        {
            var review = Required<PendingReview>(connection, transaction, "reviews", reviewId);
            CheckVersion(review.Version, expectedReviewVersion);
            if (review.State != ReviewState.Pending)
                throw new InvalidOperationException("只有待确认状态的建议可以接受。");
            var candidate = review.Candidate;
            ValidateCandidate(candidate);
            if (candidate.Action == RuleAction.Inform)
                throw new ArgumentException("普通通知不能通过事项入口接受。", nameof(reviewId));
            var previous = Read<TaskItem>(connection, transaction, "tasks", taskId);
            TaskItem task;
            if (candidate.TargetTaskId is null)
            {
                if (previous is not null || expectedTaskVersion is not null)
                    throw new InvalidOperationException("新候选不能覆盖已有事项。");
                if (candidate.Action is RuleAction.Cancel or RuleAction.Postpone or RuleAction.NewRequirement)
                    throw new ArgumentException("关联变更缺少目标事项。", nameof(reviewId));
                var time = confirmedTime ?? candidate.Time;
                ValidateTime(time);
                task = NewTask(taskId, candidate, time!, confirmedTime is not null, now);
                Insert(connection, transaction, "tasks", taskId, task, Canonical(candidate));
            }
            else
            {
                if (candidate.TargetTaskId != taskId || expectedTaskVersion is null)
                    throw new ArgumentException("关联建议须指定同一事项及其当前版本。", nameof(taskId));
                if (previous is null) throw new InvalidOperationException("关联建议的目标事项不存在。");
                CheckVersion(previous.Version, expectedTaskVersion.Value);
                task = AcceptLinked(previous, candidate, confirmedTime, now);
                Update(connection, transaction, "tasks", taskId, task);
            }
            AddEvidence(connection, transaction, task);
            var accepted = review with
            {
                State = ReviewState.Accepted, AcceptedTaskId = taskId, UndoExpiresAt = null,
                UndoOperationId = null, Version = review.Version + 1
            };
            Update(connection, transaction, "reviews", reviewId, accepted);
            return new(task, taskId, new { Review = review, Task = previous }, new { Review = accepted, Task = task });
        }, now);
    }

    private TaskItem AcceptLinked(TaskItem previous, RuleCandidate candidate, TaskTime? confirmedTime, DateTimeOffset now)
    {
        var snapshot = Snapshot(previous);
        if (candidate.Action == RuleAction.Cancel) snapshot = TaskTransitions.Cancel(snapshot);
        else
        {
            // 催交候选可能借用提取时的旧期限；只有显式确认时间才允许修改正式值。
            var time = confirmedTime ?? (candidate.Action == RuleAction.Remind ? null : candidate.Time);
            if (candidate.Action is RuleAction.Postpone or RuleAction.NewRequirement) ValidateTime(time);
            if (time is not null)
            {
                if (confirmedTime is not null) snapshot = TaskTransitions.AcceptTime(snapshot, time);
                else
                {
                    var proposal = TaskTransitions.ProposeTime(snapshot, time);
                    if (proposal.SuggestedTime is not null)
                        throw new InvalidOperationException("时间已受手动保护，须明确确认替换值。");
                    snapshot = proposal.Current;
                }
            }
            if (candidate.Action == RuleAction.NewRequirement)
            {
                ValidateTime(time);
                if (previous.State == TaskState.Archived) snapshot = TaskTransitions.RestoreArchived(snapshot);
                else snapshot = TaskTransitions.RestoreForNewAction(snapshot);
            }
        }
        // 一次接受操作只推进一次事项版本；时间版本仅在正式时间改变时推进。
        var task = Apply(previous, snapshot) with
        {
            Version = previous.Version + 1,
            Evidence = previous.Evidence.Concat(candidate.Evidence).Distinct().ToArray()
        };
        if (candidate.Action == RuleAction.NewRequirement)
            task = task with { Title = candidate.Title, Action = RuleAction.NewRequirement };
        if (task.State == TaskState.Cancelled)
            task = task with
            {
                CompletedAt = null, ArchivedAt = null, CancelledAt = previous.CancelledAt ?? now,
                UndoExpiresAt = null, UndoOperationId = null
            };
        else if (task.State == TaskState.Active && previous.State != TaskState.Active)
            task = task with { CompletedAt = null, ArchivedAt = null, UndoExpiresAt = null, UndoOperationId = null };
        return task;
    }

    internal (TaskItem? Task, string? ReviewReason) ApplyAutomaticChangeInTransaction(
        SqliteConnection connection, SqliteTransaction transaction, RuleCandidate candidate,
        string operationId, DateTimeOffset now)
    {
        ValidateCandidate(candidate);
        if (candidate.Disposition != RuleDisposition.Change || candidate.TargetTaskId is null ||
            candidate.Action is not (RuleAction.Cancel or RuleAction.Postpone or RuleAction.NewRequirement or RuleAction.Remind))
            throw new ArgumentException("自动变更须有明确关联目标及有效行动。", nameof(candidate));
        var assessments = candidate.Assessments;
        if (!assessments.Action.Reliable || !assessments.Audience.Reliable || !assessments.Course.Reliable ||
            !assessments.Time.Reliable || !assessments.Source.Reliable || !assessments.Association.Reliable ||
            candidate.ReviewReasons.Count != 0)
            return (null, "变更的行动、来源、关联或正式时间仍需确认");
        var previous = Read<TaskItem>(connection, transaction, "tasks", candidate.TargetTaskId);
        if (previous is null) return (null, "关联目标事项已不存在，需要确认");
        if (candidate.Action == RuleAction.NewRequirement && previous.State is TaskState.Archived or TaskState.Cancelled)
            return (null, "已归档或已取消事项的新要求必须由用户确认");
        if (candidate.Action is RuleAction.Postpone or RuleAction.NewRequirement)
        {
            if (candidate.Time is null) return (null, "缺少可靠正式时间，需要确认");
            if (previous.TimeProtected && candidate.Time != previous.Time)
                return (null, "新时间与手动保护的正式时间冲突，需要确认");
        }
        var result = WriteInTransaction<TaskItem>(connection, transaction, operationId,
            new { Kind = "automatic-change", TaskId = previous.Id, ExpectedTaskVersion = previous.Version,
                Candidate = Normalize(candidate) }, "task", (connection, transaction) =>
        {
            // 催交只补充证据；自动新要求保留稳定标题，正文留在操作历史及原消息。
            var updated = AcceptLinked(previous, candidate, null, now) with
            {
                Title = previous.Title,
                Priority = candidate.Time != previous.Time && candidate.Action is RuleAction.Postpone or RuleAction.NewRequirement
                    ? candidate.Priority : previous.Priority
            };
            Update(connection, transaction, "tasks", previous.Id, updated);
            AddEvidence(connection, transaction, updated);
            return new(updated, updated.Id, previous);
        }, now);
        return (result, null);
    }

    private TaskItem ChangeTask(string id, string operationId, long expectedVersion, DateTimeOffset now,
        string kind, object? value, Func<TaskItem, TaskItem> change)
    {
        ValidateId(id);
        return Write<TaskItem>(operationId, new { Kind = kind, Id = id, ExpectedVersion = expectedVersion, Value = value }, "task",
            (connection, transaction) =>
            {
                var before = Required<TaskItem>(connection, transaction, "tasks", id);
                CheckVersion(before.Version, expectedVersion);
                var after = change(before);
                if (after.Version != before.Version) Update(connection, transaction, "tasks", id, after);
                return new(after, id, before);
            }, now);
    }

    private PendingReview ChangeReview(string id, string operationId, long expectedVersion, DateTimeOffset now,
        string kind, object? value, Func<PendingReview, PendingReview> change)
    {
        ValidateId(id);
        return Write<PendingReview>(operationId, new { Kind = kind, Id = id, ExpectedVersion = expectedVersion, Value = value }, "review",
            (connection, transaction) =>
            {
                var before = Required<PendingReview>(connection, transaction, "reviews", id);
                CheckVersion(before.Version, expectedVersion);
                var after = change(before);
                if (after.Version != before.Version) Update(connection, transaction, "reviews", id, after);
                return new(after, id, before);
            }, now);
    }

    private sealed record Mutation<T>(T Current, string Id, object? Before = null, object? After = null);

    private T Write<T>(string operationId, object request, string resultKind,
        Func<SqliteConnection, SqliteTransaction, Mutation<T>> mutate, DateTimeOffset now) where T : class
        => InTransaction((connection, transaction) =>
            WriteInTransaction(connection, transaction, operationId, request, resultKind, mutate, now));

    private T InTransaction<T>(Func<SqliteConnection, SqliteTransaction, T> mutate)
    {
        using var connection = Open();
        // BeginTransaction 默认取得写锁；读取版本、更新数据和登记操作都位于同一事务。
        using var transaction = connection.BeginTransaction();
        var result = mutate(connection, transaction);
        transaction.Commit();
        return result;
    }

    private static T WriteInTransaction<T>(SqliteConnection connection, SqliteTransaction transaction,
        string operationId, object request, string resultKind,
        Func<SqliteConnection, SqliteTransaction, Mutation<T>> mutate, DateTimeOffset now) where T : class
    {
        ValidateId(operationId);
        var requestJson = JsonSerializer.Serialize(request);
        using (var replay = Command(connection, transaction,
            "SELECT request_json, result_kind, result_id FROM operations WHERE operation_id = $id;", ("$id", operationId)))
        using (var reader = replay.ExecuteReader())
        {
            if (reader.Read())
            {
                if (reader.GetString(0) != requestJson || reader.GetString(1) != resultKind)
                    throw new InvalidOperationException("操作 ID 已用于不同的请求。");
                var resultId = reader.GetString(2);
                reader.Close();
                var current = Required<T>(connection, transaction, resultKind == "task" ? "tasks" : "reviews", resultId);
                return current;
            }
        }
        var result = mutate(connection, transaction);
        using var audit = Command(connection, transaction, """
            INSERT INTO operations(operation_id, request_json, result_kind, result_id, processed_at, before_json, after_json)
            VALUES($id, $request, $kind, $result, $at, $before, $after);
            """, ("$id", operationId), ("$request", requestJson), ("$kind", resultKind), ("$result", result.Id),
            ("$at", now.ToString("O", CultureInfo.InvariantCulture)),
            ("$before", result.Before is null ? DBNull.Value : JsonSerializer.Serialize(result.Before)),
            ("$after", JsonSerializer.Serialize(result.After ?? result.Current)));
        audit.ExecuteNonQuery();
        return result.Current;
    }

    private TaskItem NewTask(string id, RuleCandidate candidate, TaskTime time, bool timeProtected, DateTimeOffset now) =>
        new(id, AccountId, candidate.Title, candidate.Course, candidate.Action, time, TaskState.Active,
            timeProtected, candidate.Priority, 1, 1, candidate.Evidence.Distinct().ToArray(), now, null, null, null);

    private static TaskSnapshot Snapshot(TaskItem task) =>
        new(task.Id, task.State, task.Time, task.TimeProtected, task.Version, task.TimeVersion);
    private static TaskItem Apply(TaskItem task, TaskSnapshot snapshot) => task with
    {
        State = snapshot.State, Time = snapshot.Time, TimeProtected = snapshot.TimeProtected,
        Version = snapshot.Version, TimeVersion = snapshot.TimeVersion
    };

    internal void ValidateCandidate(RuleCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateId(candidate.Title);
        if (!Enum.IsDefined(candidate.Action) || !Enum.IsDefined(candidate.Disposition) || candidate.Priority is < 0 or > 3)
            throw new ArgumentException("候选分类或优先级无效。", nameof(candidate));
        if (candidate.Evidence.Count == 0) throw new ArgumentException("消息候选必须有原始证据。", nameof(candidate));
        foreach (var key in candidate.Evidence)
        {
            if (key.AccountId != AccountId) throw new ArgumentException("证据账号与事项存储不匹配。", nameof(candidate));
            ValidateId(key.GroupId);
            ValidateId(key.MessageId);
        }
        if (candidate.TargetTaskId is not null) ValidateId(candidate.TargetTaskId);
        if (candidate.Time is not null) ValidateTime(candidate.Time);
    }

    private static void ValidateTime(TaskTime? time)
    {
        if (time is null || !Enum.IsDefined(time.Kind)) throw new ArgumentException("须提供有效正式时间。", nameof(time));
        ValidateId(time.TimeZoneId);
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(time.TimeZoneId);
            _ = time.GetExactInstant();
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException("正式时间的时区无效。", nameof(time), exception);
        }
    }

    private static void ValidateId(string id) => ArgumentException.ThrowIfNullOrWhiteSpace(id);
    private static void CheckVersion(long current, long expected)
    {
        if (current != expected) throw new InvalidOperationException("记录已变化，请重新读取后再操作。");
    }

    private static RuleCandidate Normalize(RuleCandidate candidate) => candidate with
    {
        Evidence = candidate.Evidence.Distinct().OrderBy(key => key.AccountId, StringComparer.Ordinal)
            .ThenBy(key => key.GroupId, StringComparer.Ordinal).ThenBy(key => key.MessageId, StringComparer.Ordinal).ToArray()
    };
    // 完整规范候选直接作为唯一键，避免哈希碰撞；一条消息可合法支持多个不同事项。
    private static string Canonical(RuleCandidate candidate) => JsonSerializer.Serialize(Normalize(candidate));

    private T? Find<T>(string table, string id) where T : class
    {
        ValidateId(id);
        using var connection = Open();
        return Read<T>(connection, null, table, id);
    }

    private IReadOnlyList<T> List<T>(string table) where T : class
    {
        using var connection = Open();
        using var command = Command(connection, null, $"SELECT record_json FROM {table} ORDER BY rowid;");
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read()) result.Add(Deserialize<T>(reader.GetString(0)));
        return result;
    }

    private static T Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("生命周期记录为空。");
    private static T? Read<T>(SqliteConnection connection, SqliteTransaction? transaction, string table, string id) where T : class
    {
        using var command = Command(connection, transaction, $"SELECT record_json FROM {table} WHERE id = $id;", ("$id", id));
        return command.ExecuteScalar() is string json ? Deserialize<T>(json) : null;
    }
    private static T Required<T>(SqliteConnection connection, SqliteTransaction transaction, string table, string id) where T : class =>
        Read<T>(connection, transaction, table, id) ?? throw new KeyNotFoundException("生命周期记录不存在。");
    private static T? FindBySource<T>(SqliteConnection connection, SqliteTransaction transaction, string table, string source) where T : class
    {
        using var command = Command(connection, transaction, $"SELECT record_json FROM {table} WHERE source_key = $source;", ("$source", source));
        return command.ExecuteScalar() is string json ? Deserialize<T>(json) : null;
    }
    private static void Insert<T>(SqliteConnection connection, SqliteTransaction transaction, string table, string id, T value, string source)
    {
        using var command = Command(connection, transaction,
            $"INSERT INTO {table}(id, record_json, source_key) VALUES($id, $json, $source);",
            ("$id", id), ("$json", JsonSerializer.Serialize(value)), ("$source", source));
        command.ExecuteNonQuery();
    }
    private static void Update<T>(SqliteConnection connection, SqliteTransaction transaction, string table, string id, T value)
    {
        using var command = Command(connection, transaction, $"UPDATE {table} SET record_json = $json WHERE id = $id;",
            ("$id", id), ("$json", JsonSerializer.Serialize(value)));
        command.ExecuteNonQuery();
    }
    private void AddEvidence(SqliteConnection connection, SqliteTransaction transaction, TaskItem task)
    {
        foreach (var key in task.Evidence)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO task_evidence(task_id, account_id, group_id, message_id) VALUES($id, $account, $group, $message)
                ON CONFLICT(task_id, account_id, group_id, message_id) DO NOTHING;
                """, ("$id", task.Id), ("$account", AccountId), ("$group", key.GroupId), ("$message", key.MessageId));
            command.ExecuteNonQuery();
        }
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

    private void Initialize()
    {
        using var connection = Open(create: true);
        using var transaction = connection.BeginTransaction();
        using var versionCommand = Command(connection, transaction, "PRAGMA user_version;");
        var version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version == 0)
        {
            using var tables = Command(connection, transaction,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';");
            if (Convert.ToInt64(tables.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("已有数据库缺少可识别的结构版本，保留原数据并停止打开。");
            using var schema = Command(connection, transaction, """
                CREATE TABLE store_metadata(account_id TEXT PRIMARY KEY NOT NULL);
                CREATE TABLE tasks(id TEXT PRIMARY KEY NOT NULL, record_json TEXT NOT NULL, source_key TEXT NOT NULL UNIQUE);
                CREATE TABLE reviews(id TEXT PRIMARY KEY NOT NULL, record_json TEXT NOT NULL, source_key TEXT NOT NULL UNIQUE);
                CREATE TABLE task_evidence(
                    task_id TEXT NOT NULL REFERENCES tasks(id),
                    account_id TEXT NOT NULL REFERENCES store_metadata(account_id),
                    group_id TEXT NOT NULL, message_id TEXT NOT NULL,
                    PRIMARY KEY(task_id, account_id, group_id, message_id));
                CREATE TABLE operations(
                    sequence INTEGER PRIMARY KEY AUTOINCREMENT, operation_id TEXT NOT NULL UNIQUE,
                    request_json TEXT NOT NULL, result_kind TEXT NOT NULL CHECK(result_kind IN ('task', 'review')),
                    result_id TEXT NOT NULL, processed_at TEXT NOT NULL, before_json TEXT, after_json TEXT NOT NULL);
                INSERT INTO store_metadata(account_id) VALUES($account);
                PRAGMA user_version = 1;
                """, ("$account", AccountId));
            schema.ExecuteNonQuery();
        }
        else if (version != 1)
            throw new InvalidDataException("数据库结构版本尚不支持，保留原数据并停止打开。");
        using (var owner = Command(connection, transaction, "SELECT account_id FROM store_metadata;"))
        using (var reader = owner.ExecuteReader())
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
            DataSource = DatabasePath, Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            ForeignKeys = true, Pooling = false
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }
}
