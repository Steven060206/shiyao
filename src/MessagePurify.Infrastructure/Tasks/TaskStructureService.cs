using System.Text.Json;
using MessagePurify.Core;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tasks;

public sealed partial class AccountTaskStore
{
    public string ResolveTaskId(string id)
    {
        ValidateId(id);
        using var connection = Open();
        return ResolveTaskId(connection, null, id);
    }

    private static string ResolveTaskId(SqliteConnection connection, SqliteTransaction? transaction, string id)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(id))
        {
            var task = Read<TaskItem>(connection, transaction, "tasks", id)
                ?? throw new KeyNotFoundException("事项不存在。");
            if (task.MergedIntoId is null) return id;
            id = task.MergedIntoId;
        }
        throw new InvalidDataException("事项合并跳转关系形成循环。");
    }

    public TaskItem MergeTasks(string keepId, string otherId, string operationId,
        long keepVersion, long otherVersion, TaskMergeSelection selection, DateTimeOffset now,
        int? confirmedPriority = null)
    {
        ValidatePair(keepId, otherId);
        if (confirmedPriority is < 0 or > 3)
            throw new ArgumentException("合并确认的优先级须为 0 到 3。", nameof(confirmedPriority));
        ArgumentNullException.ThrowIfNull(selection);
        foreach (var source in new[] { selection.TitleTaskId, selection.CourseTaskId, selection.TimeTaskId,
            selection.PriorityTaskId, selection.StateTaskId })
            if (source != keepId && source != otherId)
                throw new ArgumentException("合并字段只能选择这两项事项作为来源。", nameof(selection));
        return Write<TaskItem>(operationId,
            new { Kind = "merge-tasks", KeepId = keepId, OtherId = otherId, KeepVersion = keepVersion,
                OtherVersion = otherVersion, Selection = selection, ConfirmedPriority = confirmedPriority }, "task", (connection, transaction) =>
        {
            var keep = CurrentTask(connection, transaction, keepId, keepVersion);
            var other = CurrentTask(connection, transaction, otherId, otherVersion);
            var reviews = InvalidateRelatedReviewUndo(connection, transaction, keepId, otherId);
            TaskItem From(string source) => source == keepId ? keep : other;
            var title = From(selection.TitleTaskId);
            var time = From(selection.TimeTaskId);
            var priority = From(selection.PriorityTaskId);
            var state = From(selection.StateTaskId);
            var merged = keep with
            {
                Title = title.Title, Action = title.Action,
                Source = keep.Evidence.Count + other.Evidence.Count > 0 ? "qq" : "manual",
                Course = From(selection.CourseTaskId).Course,
                Time = time.Time, TimeProtected = true,
                TimeVersion = keep.TimeVersion + (keep.Time == time.Time ? 0 : 1),
                Priority = confirmedPriority ?? priority.Priority,
                PriorityOverride = confirmedPriority ?? priority.PriorityOverride,
                State = state.State, CompletedAt = state.CompletedAt, CancelledAt = state.CancelledAt,
                ArchivedAt = state.ArchivedAt, UndoExpiresAt = null, UndoOperationId = null,
                Version = keep.Version + 1,
                Evidence = keep.Evidence.Concat(other.Evidence).Distinct().ToArray(),
                RelatedItemIds = Relations(keep.RelatedItemIds, other.RelatedItemIds).Append(otherId)
                    .Where(id => id != keepId).Distinct().ToArray(),
                SeparateTaskIds = Relations(keep.SeparateTaskIds, other.SeparateTaskIds)
                    .Where(id => id != keepId && id != otherId).ToArray()
            };
            var source = other with
            {
                MergedIntoId = keepId, Version = other.Version + 1, UndoExpiresAt = null, UndoOperationId = null,
                SeparateTaskIds = (other.SeparateTaskIds ?? []).Where(id => id != keepId).ToArray()
            };
            Update(connection, transaction, "tasks", keepId, merged);
            Update(connection, transaction, "tasks", otherId, source);
            AddEvidence(connection, transaction, merged);
            return new(merged, keepId,
                new { Task = keep, MergedTask = other, ChangedReviews = reviews.Select(change => change.Before).ToArray() },
                new { Task = merged, MergedTask = source, ChangedReviews = reviews.Select(change => change.After).ToArray() });
        }, now);
    }

    public TaskItem KeepSeparate(string id, string otherId, string operationId,
        long version, long otherVersion, DateTimeOffset now)
    {
        ValidatePair(id, otherId);
        return Write<TaskItem>(operationId,
            new { Kind = "keep-separate", Id = id, OtherId = otherId, Version = version, OtherVersion = otherVersion },
            "task", (connection, transaction) =>
        {
            var task = CurrentTask(connection, transaction, id, version);
            var other = CurrentTask(connection, transaction, otherId, otherVersion);
            var reviews = InvalidateRelatedReviewUndo(connection, transaction, id, otherId);
            var after = task with
            {
                SeparateTaskIds = Relations(task.SeparateTaskIds, [otherId]), Version = task.Version + 1,
                UndoExpiresAt = null, UndoOperationId = null
            };
            var otherAfter = other with
            {
                SeparateTaskIds = Relations(other.SeparateTaskIds, [id]), Version = other.Version + 1,
                UndoExpiresAt = null, UndoOperationId = null
            };
            Update(connection, transaction, "tasks", id, after);
            Update(connection, transaction, "tasks", otherId, otherAfter);
            return new(after, id,
                new { Task = task, SeparateTask = other, ChangedReviews = reviews.Select(change => change.Before).ToArray() },
                new { Task = after, SeparateTask = otherAfter, ChangedReviews = reviews.Select(change => change.After).ToArray() });
        }, now);
    }

    public TaskSplitResult SplitTask(string id, string newId, string operationId,
        long version, TaskSplitDraft draft, DateTimeOffset now)
    {
        ValidatePair(id, newId);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Title);
        if (draft.Title.Length > 256 || draft.Title.Any(char.IsControl) ||
            draft.Course is not null && (draft.Course.Length > 256 || draft.Course.Any(char.IsControl)) ||
            draft.Priority is < 0 or > 3 || !Enum.IsDefined(draft.State) || !Enum.IsDefined(draft.TimeKind))
            throw new ArgumentException("拆分标题、课程、优先级或状态无效。", nameof(draft));
        ArgumentNullException.ThrowIfNull(draft.Evidence);
        if (draft.Time is not null) ValidateTime(draft.Time);
        draft = draft with
        {
            Title = draft.Title.Trim(), Course = string.IsNullOrWhiteSpace(draft.Course) ? null : draft.Course.Trim(),
            Evidence = draft.Evidence.Distinct().OrderBy(key => key.AccountId, StringComparer.Ordinal)
                .ThenBy(key => key.GroupId, StringComparer.Ordinal).ThenBy(key => key.MessageId, StringComparer.Ordinal).ToArray()
        };
        return Write<TaskSplitResult>(operationId,
            new { Kind = "split-task", Id = id, NewId = newId, Version = version, Draft = draft }, "task",
            (connection, transaction) =>
        {
            var parent = CurrentTask(connection, transaction, id, version);
            if (Read<TaskItem>(connection, transaction, "tasks", newId) is not null ||
                Read<PendingReview>(connection, transaction, "reviews", newId) is not null)
                throw new InvalidOperationException("拆分去向 ID 已被其他事项使用。");
            if (draft.Evidence.Any(key => key.AccountId != AccountId || !parent.Evidence.Contains(key)) ||
                draft.Evidence.Count == 0 && (parent.Source != "manual" || draft.Time is null))
                throw new ArgumentException("拆出项必须使用原事项证据；无证据手动任务必须填写正式日期。", nameof(draft));
            var reviews = InvalidateRelatedReviewUndo(connection, transaction, id);
            var action = parent.Action == RuleAction.Inform ? RuleAction.Complete : parent.Action;
            var updatedParent = parent with
            {
                RelatedItemIds = Relations(parent.RelatedItemIds, [newId]), Version = parent.Version + 1,
                UndoExpiresAt = null, UndoOperationId = null
            };
            TaskItem? child = null;
            PendingReview? review = null;
            if (draft.Time is { } time)
            {
                child = WithChosenState(new(newId, AccountId, draft.Title, draft.Course, action, time,
                    TaskState.Active, true, draft.Priority ?? PriorityCalculator.Calculate(time, now,
                        PriorityCalculator.HasClearConsequence(draft.Title)), 1, 1, draft.Evidence, now, null, null, null,
                    PriorityOverride: draft.Priority, Source: draft.Evidence.Count > 0 ? "qq" : "manual", RelatedItemIds: [id]), draft.State, now);
                Insert(connection, transaction, "tasks", newId, child, "split-task:" + AccountId + ":" + newId);
                AddEvidence(connection, transaction, child);
            }
            else
            {
                var reliable = new RuleAssessment("用户拆分并确认", true);
                var candidate = new RuleCandidate(draft.Title, action, RuleDisposition.Pending, draft.Course,
                    null, null, null, draft.Priority,
                    new(reliable, reliable, reliable, new("拆出项缺少正式日期", false), reliable, reliable),
                    ["拆出项缺少正式日期，请确认后加入任务"], draft.Evidence);
                review = new(newId, AccountId, candidate, ReviewState.Pending, now, null, null, 1,
                    SplitParentTaskId: id, SplitState: draft.State, SplitTimeKind: draft.TimeKind);
                Insert(connection, transaction, "reviews", newId, review, "split-review:" + AccountId + ":" + newId);
            }
            Update(connection, transaction, "tasks", id, updatedParent);
            return new(new(updatedParent, child, review), id,
                new { Task = parent, ChangedReviews = reviews.Select(change => change.Before).ToArray() },
                new { Task = updatedParent, SplitTask = child, Review = review,
                    ChangedReviews = reviews.Select(change => change.After).ToArray() });
        }, now, (connection, transaction, resultId, audit) =>
        {
            var parent = Required<TaskItem>(connection, transaction, "tasks", resultId);
            var taskId = audit.GetProperty("SplitTask").ValueKind == JsonValueKind.Null ? null :
                audit.GetProperty("SplitTask").GetProperty("Id").GetString();
            var reviewId = audit.GetProperty("Review").ValueKind == JsonValueKind.Null ? null :
                audit.GetProperty("Review").GetProperty("Id").GetString();
            return new(parent, taskId is null ? null : Required<TaskItem>(connection, transaction, "tasks",
                ResolveTaskId(connection, transaction, taskId)),
                reviewId is null ? null : Required<PendingReview>(connection, transaction, "reviews", reviewId));
        });
    }

    private TaskItem CurrentTask(SqliteConnection connection, SqliteTransaction transaction, string id, long version)
    {
        var task = Required<TaskItem>(connection, transaction, "tasks", id);
        if (task.AccountId != AccountId) throw new InvalidDataException("事项账号归属不匹配。");
        EnsureCurrent(task);
        CheckVersion(task.Version, version);
        return task;
    }

    private static void EnsureCurrent(TaskItem task)
    {
        if (task.MergedIntoId is not null)
            throw new InvalidOperationException("此事项已合并，请打开当前保留事项后再操作。");
    }

    private static void ValidatePair(string id, string otherId)
    {
        ValidateId(id); ValidateId(otherId);
        if (id == otherId) throw new ArgumentException("须选择两项不同事项。");
    }

    private static string[] Relations(IReadOnlyList<string>? first, IReadOnlyList<string>? second) =>
        (first ?? []).Concat(second ?? []).Distinct().ToArray();

    private static TaskItem WithChosenState(TaskItem task, TaskState state, DateTimeOffset now) => task with
    {
        State = state, CompletedAt = state == TaskState.Completed ? now : null,
        CancelledAt = state == TaskState.Cancelled ? now : null, ArchivedAt = state == TaskState.Archived ? now : null,
        UndoExpiresAt = null, UndoOperationId = null
    };

    private static bool HasSplitRelations(SqliteConnection connection, SqliteTransaction transaction, TaskItem task)
    {
        foreach (var id in task.RelatedItemIds ?? [])
        {
            var related = Read<TaskItem>(connection, transaction, "tasks", id);
            if (related is not null && ResolveTaskId(connection, transaction, related.Id) != task.Id) return true;
            if (Read<PendingReview>(connection, transaction, "reviews", id)?.SplitParentTaskId is not null) return true;
        }
        return false;
    }

    private sealed record ReviewUndoChange(PendingReview Before, PendingReview After);

    private static IReadOnlyList<ReviewUndoChange> InvalidateRelatedReviewUndo(SqliteConnection connection,
        SqliteTransaction transaction, params string[] taskIds)
    {
        var affected = new HashSet<string>(taskIds, StringComparer.Ordinal);
        var candidates = new List<PendingReview>();
        using (var command = Command(connection, transaction, "SELECT record_json FROM reviews ORDER BY rowid;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var review = Deserialize<PendingReview>(reader.GetString(0));
                if (review.State == ReviewState.Ignored && review.UndoOperationId is not null) candidates.Add(review);
            }
        var changes = new List<ReviewUndoChange>();
        foreach (var before in candidates)
        {
            var target = before.Candidate.TargetTaskId ?? before.SplitParentTaskId;
            if (target is null || !affected.Contains(target) &&
                (Read<TaskItem>(connection, transaction, "tasks", target) is null ||
                    !affected.Contains(ResolveTaskId(connection, transaction, target)))) continue;
            var after = before with { UndoOperationId = null, UndoExpiresAt = null, Version = before.Version + 1 };
            Update(connection, transaction, "reviews", before.Id, after);
            changes.Add(new(before, after));
        }
        return changes;
    }
}
