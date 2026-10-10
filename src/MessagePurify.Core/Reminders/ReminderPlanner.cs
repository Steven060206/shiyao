namespace MessagePurify.Core;

public abstract record ReminderChoice;
public sealed record DefaultReminder : ReminderChoice;
public sealed record DisabledReminder : ReminderChoice;
public sealed record HoursBefore(double Hours) : ReminderChoice;
public sealed record CalendarBefore(int Days, TimeOnly Clock) : ReminderChoice;
public sealed record ReminderPlan(IReadOnlyList<DateTimeOffset> Nodes, bool NeedsReview);

public static class ReminderPlanner
{
    public static ReminderPlan Plan(TaskTime time, ReminderChoice choice)
    {
        if (choice is DisabledReminder)
            return new(Array.Empty<DateTimeOffset>(), false);
        if (choice is DefaultReminder)
            return new(DefaultNodes(time), false);
        if (choice is HoursBefore hours)
        {
            if (!double.IsFinite(hours.Hours) || hours.Hours <= 0)
                throw new ArgumentOutOfRangeException(nameof(choice), "提前小时数必须是有限正数。");
            if (time.GetExactInstant() is not { } instant)
                return new(DefaultNodes(time), true);
            var node = instant.AddHours(-hours.Hours);
            if (node >= instant)
                throw new ArgumentOutOfRangeException(nameof(choice), "提前小时数必须产生更早的提醒时刻。");
            return new(new[] { node }, false);
        }
        if (choice is CalendarBefore calendar)
        {
            if (calendar.Days < 0)
                throw new ArgumentOutOfRangeException(nameof(choice), "提前天数不能为负数。");
            var node = time.AtLocal(time.Date.AddDays(-calendar.Days), calendar.Clock);
            if (time.GetExactInstant() is { } instant && node >= instant)
                throw new ArgumentException("提前提醒必须早于正式时间。", nameof(choice));
            return new(new[] { node }, false);
        }
        throw new ArgumentException("不支持的提醒规则。", nameof(choice));
    }

    private static IReadOnlyList<DateTimeOffset> DefaultNodes(TaskTime time) =>
        time.GetExactInstant() is { } instant
            ? new[] { instant.AddHours(-24), instant.AddHours(-2) }
            : new[] { time.AtLocal(time.Date.AddDays(-1), new TimeOnly(12, 0)) };
}
