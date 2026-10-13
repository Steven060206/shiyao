using MessagePurify.Infrastructure.Reminders;

namespace MessagePurify.Infrastructure.Application;

public sealed record WorkspaceCommand(string AccountId, string Operation, string Id,
    string OperationId, long Version, string? Date = null, string? Time = null,
    string? TimeKind = null, string? UndoOperationId = null, long? TargetTaskVersion = null,
    string? Course = null, bool Excluded = false, string? ReminderMode = null,
    double? ReminderHours = null, int? ReminderDays = null, string? ReminderClock = null,
    long? ReminderVersion = null, string? Title = null, int? Priority = null);

public sealed record ApplicationSnapshot(string AccountId, IReadOnlyList<ApplicationItem> Items,
    IReadOnlyList<ApplicationGroup> Groups, DateTimeOffset GeneratedAt,
    IReadOnlyList<TaskReminderPlan> Reminders);

public sealed record ApplicationItem(string Id, string Kind, string Title, string? Course,
    string Group, string State, string? Due, string? Time, string TimeKind, int? Priority,
    long Version, string? TargetTaskId, long? TargetTaskVersion, string? UndoOperationId,
    DateTimeOffset? UndoExpiresAt, IReadOnlyList<string> ReviewReasons, string? SuggestedDue,
    string? SuggestedTime, IReadOnlyList<ApplicationEvidence> Evidence, bool Unread,
    string Action, bool CanAccept);

public sealed record ApplicationEvidence(string GroupId, string GroupName, string SenderId,
    DateTimeOffset? SentAt, string Text, int ImageCount);

public sealed record ApplicationGroup(string GroupId, string Name, string SuggestedCourse,
    string? Course, bool Excluded, bool Pinned);
