namespace MessagePurify.Infrastructure.Reminders;

public sealed record ReminderPreference(string Mode = "default", double? Hours = null,
    int? Days = null, string? Clock = null);

public sealed record TaskReminderPlan(string TaskId, long TimeVersion, long PlanVersion,
    ReminderPreference Preference, bool NeedsReview, bool Active, IReadOnlyList<ReminderNode> Nodes);

public sealed record ReminderNode(string Id, DateTimeOffset ScheduledAt, string State);

public sealed record ReminderMember(string TaskId, string Title, long TimeVersion,
    long PlanVersion, IReadOnlyList<string> NodeIds);

public sealed record ReminderDelivery(string Id, string AccountId, string Kind,
    IReadOnlyList<ReminderMember> Members, string State, string? NotificationId = null, string? Error = null);

public sealed record ReminderSubmission(string State, string? NotificationId = null, string? Error = null);

public interface IReminderSink
{
    ReminderSubmission Submit(ReminderDelivery delivery);
    void Remove(string notificationId);
}
