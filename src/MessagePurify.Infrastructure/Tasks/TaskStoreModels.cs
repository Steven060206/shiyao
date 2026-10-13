using MessagePurify.Core;

namespace MessagePurify.Infrastructure.Tasks;

public enum ReviewState { Pending, Ignored, Accepted, Rejected }

public sealed record TaskItem(string Id, string AccountId, string Title,
    string? Course, RuleAction Action, TaskTime Time, TaskState State,
    bool TimeProtected, int? Priority, long Version, long TimeVersion,
    IReadOnlyList<MessageKey> Evidence, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, DateTimeOffset? CancelledAt,
    DateTimeOffset? ArchivedAt, DateTimeOffset? UndoExpiresAt = null,
    string? UndoOperationId = null, int? PriorityOverride = null, string Source = "qq");

public sealed record PendingReview(string Id, string AccountId,
    RuleCandidate Candidate, ReviewState State, DateTimeOffset CreatedAt,
    DateTimeOffset? IgnoredAt, DateTimeOffset? UndoExpiresAt, long Version,
    string? AcceptedTaskId = null, string? UndoOperationId = null);
