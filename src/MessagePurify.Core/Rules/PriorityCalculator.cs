namespace MessagePurify.Core;

public static class PriorityCalculator
{
    public static int Calculate(TaskTime time, DateTimeOffset now, bool clearConsequence = false)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(time.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var days = time.Date.DayNumber - today.DayNumber;
        var priority = days <= 0 ? 0 : days <= 3 ? 1 : days <= 7 ? 2 : 3;
        return clearConsequence ? Math.Max(0, priority - 1) : priority;
    }
}
