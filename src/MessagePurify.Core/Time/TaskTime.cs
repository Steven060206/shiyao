namespace MessagePurify.Core;

public enum TimeKind { Deadline, Start }

/// <summary>已确认的正式时间；仅日期时不生成虚构的截止钟点。</summary>
public sealed record TaskTime(
    TimeKind Kind, DateOnly Date, TimeOnly? Time, string TimeZoneId)
{
    public DateTimeOffset? GetExactInstant() =>
        Time is { } clock ? AtLocal(Date, clock) : null;

    public bool IsPast(DateTimeOffset now)
    {
        if (GetExactInstant() is { } instant)
            return now >= instant;

        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return localDate > Date;
    }

    internal DateTimeOffset AtLocal(DateOnly date, TimeOnly clock)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var local = DateTime.SpecifyKind(date.ToDateTime(clock), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ArgumentException("时间无法唯一解释，需要确认。");

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}
