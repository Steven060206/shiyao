using MessagePurify.Core;
using Xunit;

namespace MessagePurify.Core.Tests;

public class TaskTimeTests
{
    [Theory]
    [InlineData(TimeKind.Deadline)]
    [InlineData(TimeKind.Start)]
    public void DateOnly_RemainsValidUntilNextLocalDay(TimeKind kind)
    {
        var time = new TaskTime(kind, new DateOnly(2026, 10, 12), null,
            "China Standard Time");

        Assert.Null(time.GetExactInstant());
        Assert.False(time.IsPast(DateTimeOffset.Parse("2026-10-12T23:59:59+08:00")));
        Assert.True(time.IsPast(DateTimeOffset.Parse("2026-10-13T00:00:00+08:00")));
        Assert.Null(time.Time);
        Assert.Equal(kind, time.Kind);
    }

    [Theory]
    [InlineData(TimeKind.Deadline)]
    [InlineData(TimeKind.Start)]
    public void ExactTime_BecomesPastAtTheSpecifiedInstant(TimeKind kind)
    {
        var time = new TaskTime(kind, new DateOnly(2026, 10, 12),
            new TimeOnly(18, 0), "China Standard Time");

        Assert.Equal(DateTimeOffset.Parse("2026-10-12T10:00:00Z"), time.GetExactInstant());
        Assert.False(time.IsPast(DateTimeOffset.Parse("2026-10-12T17:59:59+08:00")));
        Assert.True(time.IsPast(DateTimeOffset.Parse("2026-10-12T18:00:00+08:00")));
        Assert.True(time.IsPast(DateTimeOffset.Parse("2026-10-12T11:00:00Z")));
        Assert.Equal(kind, time.Kind);
    }

    [Fact]
    public void DateOnly_UsesSavedZoneInsteadOfNowOffset()
    {
        var time = new TaskTime(TimeKind.Deadline, new DateOnly(2026, 10, 12), null,
            "China Standard Time");

        Assert.False(time.IsPast(DateTimeOffset.Parse("2026-10-12T15:59:59Z")));
        Assert.True(time.IsPast(DateTimeOffset.Parse("2026-10-12T16:00:00Z")));
        Assert.True(time.IsPast(DateTimeOffset.Parse("2026-10-12T09:00:00-07:00")));
    }

    [Fact]
    public void ExactTime_UsesTheSavedZone()
    {
        var time = new TaskTime(TimeKind.Start, new DateOnly(2026, 10, 12),
            new TimeOnly(18, 0), "Eastern Standard Time");

        Assert.Equal(DateTimeOffset.Parse("2026-10-12T22:00:00Z"), time.GetExactInstant());
        Assert.False(time.IsPast(DateTimeOffset.Parse("2026-10-12T21:59:59Z")));
        Assert.True(time.IsPast(DateTimeOffset.Parse("2026-10-12T22:00:00Z")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidTimeZone_DoesNotGuessAFormalTime(bool hasClock)
    {
        var time = new TaskTime(TimeKind.Deadline, new DateOnly(2026, 10, 12),
            hasClock ? new TimeOnly(18, 0) : null, "MessagePurify/MissingZone");

        Assert.Throws<TimeZoneNotFoundException>(() => time.IsPast(DateTimeOffset.Parse("2026-10-12T10:00:00Z")));
        if (hasClock)
            Assert.Throws<TimeZoneNotFoundException>(() => time.GetExactInstant());
        else
            Assert.Null(time.GetExactInstant());
    }

    [Theory]
    [InlineData(3, 8, 2, 30)]
    [InlineData(11, 1, 1, 30)]
    public void InvalidOrAmbiguousLocalClock_RequiresConfirmation(
        int month, int day, int hour, int minute)
    {
        var time = new TaskTime(TimeKind.Deadline, new DateOnly(2026, month, day),
            new TimeOnly(hour, minute), "Eastern Standard Time");

        Assert.Throws<ArgumentException>(() => time.GetExactInstant());
        Assert.Throws<ArgumentException>(() => time.IsPast(DateTimeOffset.Parse("2026-10-12T10:00:00Z")));
    }
}
