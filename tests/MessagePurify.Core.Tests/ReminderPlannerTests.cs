using MessagePurify.Core;
using Xunit;

namespace MessagePurify.Core.Tests;

public sealed class ReminderPlannerTests
{
    [Theory]
    [InlineData(TimeKind.Deadline)]
    [InlineData(TimeKind.Start)]
    public void DefaultExactTime_Uses24And2Hours(TimeKind kind)
    {
        var time = At(kind, 12, new TimeOnly(18, 0));

        var plan = ReminderPlanner.Plan(time, new DefaultReminder());

        Assert.Equal(new[] { Instant("2026-10-11T18:00:00+08:00"),
            Instant("2026-10-12T16:00:00+08:00") }, plan.Nodes);
        Assert.False(plan.NeedsReview);
        Assert.Equal(kind, time.Kind);
        Assert.Equal(new TimeOnly(18, 0), time.Time);
    }

    [Theory]
    [InlineData(TimeKind.Deadline)]
    [InlineData(TimeKind.Start)]
    public void DefaultDate_UsesPreviousNoonWithoutInventingFormalTime(TimeKind kind)
    {
        var time = At(kind, 12, null);

        var plan = ReminderPlanner.Plan(time, new DefaultReminder());

        Assert.Equal(new[] { Instant("2026-10-11T12:00:00+08:00") }, plan.Nodes);
        Assert.False(plan.NeedsReview);
        Assert.Equal(new DateOnly(2026, 10, 12), time.Date);
        Assert.Equal(kind, time.Kind);
        Assert.Null(time.Time);
        Assert.Null(time.GetExactInstant());
    }

    [Theory]
    [InlineData(1, "2026-10-14T17:00:00+08:00")]
    [InlineData(0.5, "2026-10-14T17:30:00+08:00")]
    public void HoursBefore_RecalculatesFromNewExactTime(double hours, string expected)
    {
        var original = At(TimeKind.Deadline, 12, new TimeOnly(18, 0));
        var postponed = original with { Date = new DateOnly(2026, 10, 14) };

        var plan = ReminderPlanner.Plan(postponed, new HoursBefore(hours));

        Assert.Equal(new[] { Instant(expected) }, plan.Nodes);
        Assert.False(plan.NeedsReview);
        Assert.Equal(new DateOnly(2026, 10, 12), original.Date);
        Assert.Equal(new TimeOnly(18, 0), postponed.Time);
    }

    [Theory]
    [InlineData(TimeKind.Deadline)]
    [InlineData(TimeKind.Start)]
    public void HoursBefore_WhenPrecisionFallsBackToDateUsesDefaultAndNeedsReview(TimeKind kind)
    {
        var original = At(kind, 12, new TimeOnly(18, 0));
        var postponed = original with { Date = new DateOnly(2026, 10, 14), Time = null };
        var choice = new HoursBefore(1);
        Assert.Equal(new[] { Instant("2026-10-12T17:00:00+08:00") },
            ReminderPlanner.Plan(original, choice).Nodes);

        var plan = ReminderPlanner.Plan(postponed, choice);

        Assert.Equal(new[] { Instant("2026-10-13T12:00:00+08:00") }, plan.Nodes);
        Assert.True(plan.NeedsReview);
        Assert.Null(postponed.Time);
    }

    [Theory]
    [InlineData(TimeKind.Deadline, true)]
    [InlineData(TimeKind.Deadline, false)]
    [InlineData(TimeKind.Start, true)]
    [InlineData(TimeKind.Start, false)]
    public void CalendarBefore_PreservesDayOffsetAndClockAfterDateChange(TimeKind kind, bool exact)
    {
        var original = At(kind, 12, exact ? new TimeOnly(18, 0) : null);
        var postponed = original with { Date = new DateOnly(2026, 10, 14) };
        var choice = new CalendarBefore(1, new TimeOnly(20, 0));

        var oldPlan = ReminderPlanner.Plan(original, choice);
        var newPlan = ReminderPlanner.Plan(postponed, choice);

        Assert.Equal(new[] { Instant("2026-10-11T20:00:00+08:00") }, oldPlan.Nodes);
        Assert.Equal(new[] { Instant("2026-10-13T20:00:00+08:00") }, newPlan.Nodes);
        Assert.False(oldPlan.NeedsReview);
        Assert.False(newPlan.NeedsReview);
        Assert.Equal(original.Time, postponed.Time);
    }

    [Fact]
    public void CalendarBefore_RemainsValidWhenFormalTimeLosesClockPrecision()
    {
        var original = At(TimeKind.Deadline, 12, new TimeOnly(18, 0));
        var postponed = original with { Date = new DateOnly(2026, 10, 14), Time = null };

        var plan = ReminderPlanner.Plan(postponed, new CalendarBefore(1, new TimeOnly(20, 0)));

        Assert.Equal(new[] { Instant("2026-10-13T20:00:00+08:00") }, plan.Nodes);
        Assert.False(plan.NeedsReview);
        Assert.Null(postponed.Time);
    }

    [Theory]
    [InlineData(TimeKind.Deadline)]
    [InlineData(TimeKind.Start)]
    public void DisabledReminder_RemainsDisabledAfterDateAndPrecisionChanges(TimeKind kind)
    {
        var original = At(kind, 12, new TimeOnly(18, 0));
        var postponed = original with { Date = new DateOnly(2026, 10, 14) };
        var dateOnly = postponed with { Time = null };

        foreach (var time in new[] { original, postponed, dateOnly })
        {
            var plan = ReminderPlanner.Plan(time, new DisabledReminder());
            Assert.Empty(plan.Nodes);
            Assert.False(plan.NeedsReview);
        }
    }

    [Fact]
    public void CalendarBefore_WithDateOnlyAllowsSameDayClockWithoutChangingPrecision()
    {
        var time = At(TimeKind.Deadline, 12, null);

        var plan = ReminderPlanner.Plan(time, new CalendarBefore(0, new TimeOnly(20, 0)));

        Assert.Equal(new[] { Instant("2026-10-12T20:00:00+08:00") }, plan.Nodes);
        Assert.False(plan.NeedsReview);
        Assert.Null(time.Time);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.Epsilon)]
    public void HoursBefore_RejectsInvalidHours(double hours)
    {
        var time = At(TimeKind.Deadline, 12, new TimeOnly(18, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReminderPlanner.Plan(time, new HoursBefore(hours)));
    }

    [Fact]
    public void CalendarBefore_RejectsNegativeDays()
    {
        var time = At(TimeKind.Deadline, 12, null);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReminderPlanner.Plan(time, new CalendarBefore(-1, new TimeOnly(12, 0))));
    }

    [Theory]
    [InlineData(TimeKind.Deadline, 18)]
    [InlineData(TimeKind.Deadline, 19)]
    [InlineData(TimeKind.Start, 18)]
    [InlineData(TimeKind.Start, 19)]
    public void CalendarBefore_RejectsNodeAtOrAfterExactTime(TimeKind kind, int hour)
    {
        var time = At(kind, 12, new TimeOnly(18, 0));
        Assert.Throws<ArgumentException>(() =>
            ReminderPlanner.Plan(time, new CalendarBefore(0, new TimeOnly(hour, 0))));
    }

    [Fact]
    public void DefaultReminder_RejectsUnknownFormalTimeZone()
    {
        var time = At(TimeKind.Deadline, 12, new TimeOnly(18, 0)) with
        { TimeZoneId = "MessagePurify/UnknownTimeZone" };
        Assert.Throws<TimeZoneNotFoundException>(() =>
            ReminderPlanner.Plan(time, new DefaultReminder()));
    }

    [Fact]
    public void DefaultExactReminder_UsesElapsedHoursAcrossDaylightSavingChange()
    {
        var time = new TaskTime(TimeKind.Deadline, new DateOnly(2026, 3, 8),
            new TimeOnly(18, 0), "Eastern Standard Time");

        var plan = ReminderPlanner.Plan(time, new DefaultReminder());

        Assert.Equal(new[] { Instant("2026-03-07T17:00:00-05:00"),
            Instant("2026-03-08T16:00:00-04:00") }, plan.Nodes);
        Assert.False(plan.NeedsReview);
    }

    [Theory]
    [InlineData(3, 8, 2, 30)]
    [InlineData(11, 1, 1, 30)]
    public void DefaultReminder_RejectsInvalidOrAmbiguousFormalTime(int month, int day,
        int hour, int minute)
    {
        var time = new TaskTime(TimeKind.Deadline, new DateOnly(2026, month, day),
            new TimeOnly(hour, minute), "Eastern Standard Time");
        Assert.Throws<ArgumentException>(() =>
            ReminderPlanner.Plan(time, new DefaultReminder()));
    }

    [Theory]
    [InlineData(3, 9, 2, 30)]
    [InlineData(11, 2, 1, 30)]
    public void CalendarBefore_RejectsInvalidOrAmbiguousReminderTime(int month, int day,
        int hour, int minute)
    {
        var time = new TaskTime(TimeKind.Deadline, new DateOnly(2026, month, day),
            null, "Eastern Standard Time");
        Assert.Throws<ArgumentException>(() =>
            ReminderPlanner.Plan(time, new CalendarBefore(1, new TimeOnly(hour, minute))));
    }

    private static TaskTime At(TimeKind kind, int day, TimeOnly? time) =>
        new(kind, new DateOnly(2026, 10, day), time, "China Standard Time");

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value);
}
