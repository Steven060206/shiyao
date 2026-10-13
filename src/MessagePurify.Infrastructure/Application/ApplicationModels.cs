using MessagePurify.Infrastructure.Reminders;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Application;

public sealed record WorkspaceCommand(string AccountId, string Operation, string Id,
    string OperationId, long Version, string? Date = null, string? Time = null,
    string? TimeKind = null, string? UndoOperationId = null, long? TargetTaskVersion = null,
    string? Course = null, bool Excluded = false, string? ReminderMode = null,
    double? ReminderHours = null, int? ReminderDays = null, string? ReminderClock = null,
    long? ReminderVersion = null, string? Title = null, int? Priority = null,
    string? MergeWithId = null, long? MergeWithVersion = null, TaskMergeSelection? MergeSelection = null,
    string? NewItemId = null, string? State = null, IReadOnlyList<int>? EvidenceIndexes = null,
    long? Generation = null);

public sealed record ApplicationSnapshot(string AccountId, IReadOnlyList<ApplicationItem> Items,
    IReadOnlyList<ApplicationGroup> Groups, DateTimeOffset GeneratedAt,
    IReadOnlyList<TaskReminderPlan> Reminders, CacheCleanupResult? CacheCleanup = null,
    string? CacheCleanupError = null, long Generation = 0);

public sealed record ApplicationItem(string Id, string Kind, string Title, string? Course,
    string Group, string State, string? Due, string? Time, string TimeKind, int? Priority,
    long Version, string? TargetTaskId, long? TargetTaskVersion, string? UndoOperationId,
    DateTimeOffset? UndoExpiresAt, IReadOnlyList<string> ReviewReasons, string? SuggestedDue,
    string? SuggestedTime, IReadOnlyList<ApplicationEvidence> Evidence, bool Unread,
    string Action, bool CanAccept, string? ChangeLabel = null,
    IReadOnlyList<ApplicationChange>? Changes = null,
    IReadOnlyList<ApplicationPendingChange>? PendingChanges = null, string Source = "qq",
    string? MergedIntoId = null, IReadOnlyList<string>? RelatedItemIds = null,
    ApplicationMergeSuggestion? MergeSuggestion = null);

public sealed record ApplicationMergeSuggestion(string FirstTaskId, long FirstTaskVersion,
    string SecondTaskId, long SecondTaskVersion);

public sealed record ApplicationChange(string Label, DateTimeOffset At, long Version,
    IReadOnlyList<ApplicationFieldChange> Fields, IReadOnlyList<ApplicationEvidence> Evidence);

public sealed record ApplicationFieldChange(string Field, string Before, string After);

public sealed record ApplicationPendingChange(string Id, string Label, string? Due, string? Time,
    string TimeKind, IReadOnlyList<string> Reasons);

public sealed record ApplicationEvidence(string GroupId, string GroupName, string SenderId,
    DateTimeOffset? SentAt, string Text, int ImageCount);

public sealed record ApplicationGroup(string GroupId, string Name, string SuggestedCourse,
    string? Course, bool Excluded, bool Pinned);
