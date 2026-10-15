using MessagePurify.Core;

namespace MessagePurify.Infrastructure.Tasks;

public enum ReviewState { Pending, Ignored, Accepted, Rejected, Consolidated }

public sealed record TaskItem(string Id, string AccountId, string Title,
    string? Course, RuleAction Action, TaskTime Time, TaskState State,
    bool TimeProtected, int? Priority, long Version, long TimeVersion,
    IReadOnlyList<MessageKey> Evidence, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, DateTimeOffset? CancelledAt,
    DateTimeOffset? ArchivedAt, DateTimeOffset? UndoExpiresAt = null,
    string? UndoOperationId = null, int? PriorityOverride = null, string Source = "qq",
    string? MergedIntoId = null, IReadOnlyList<string>? RelatedItemIds = null,
    IReadOnlyList<string>? SeparateTaskIds = null);

public sealed record PendingReview(string Id, string AccountId,
    RuleCandidate Candidate, ReviewState State, DateTimeOffset CreatedAt,
    DateTimeOffset? IgnoredAt, DateTimeOffset? UndoExpiresAt, long Version,
    string? AcceptedTaskId = null, string? UndoOperationId = null,
    string? SplitParentTaskId = null, TaskState? SplitState = null, TimeKind? SplitTimeKind = null,
    string? ConsolidatedIntoId = null);

public sealed record TaskMergeSelection(string TitleTaskId, string CourseTaskId,
    string TimeTaskId, string PriorityTaskId, string StateTaskId);

public sealed record TaskSplitDraft(string Title, string? Course, TaskTime? Time,
    int? Priority, TaskState State, IReadOnlyList<MessageKey> Evidence, TimeKind TimeKind = TimeKind.Deadline);

public sealed record TaskSplitResult(TaskItem Parent, TaskItem? Task, PendingReview? Review);
