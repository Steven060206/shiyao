using MessagePurify.Core;
using Xunit;

namespace MessagePurify.Core.Tests;

public class PriorityCalculatorTests
{
    [Theory]
    [InlineData("今日提交报告，逾期不收", true)]
    [InlineData("下周参加考试，影响资格", true)]
    [InlineData("提交报告，逾期仍接收", false)]
    [InlineData("不计入成绩，不扣分", false)]
    [InlineData("通知讨论成绩、考试资格与报名", false)]
    public void ConsequenceFact_IsIndependentOfPrioritySaturation(string text, bool expected)
        => Assert.Equal(expected, PriorityCalculator.HasClearConsequence(text));

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-10T10:00:00+08:00");

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(13, 1)]
    [InlineData(14, 2)]
    [InlineData(17, 2)]
    [InlineData(18, 3)]
    public void Priority_UsesNonOverlappingLocalCalendarBoundaries(int day, int expected)
    {
        var time = new TaskTime(TimeKind.Deadline, new(2026, 10, day), null,
            "China Standard Time");
        Assert.Equal(expected, PriorityCalculator.Calculate(time, Now));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(13, 0)]
    [InlineData(17, 1)]
    [InlineData(18, 2)]
    public void ClearConsequence_RaisesOneLevelWithoutGoingAboveP0(int day, int expected)
    {
        var time = new TaskTime(TimeKind.Deadline, new(2026, 10, day), null,
            "China Standard Time");
        Assert.Equal(expected, PriorityCalculator.Calculate(time, Now, true));
    }

    [Fact]
    public void Priority_UsesSavedTimeZoneAndDoesNotCompletePastStart()
    {
        var time = new TaskTime(TimeKind.Start, new(2026, 10, 10), new(9, 0),
            "China Standard Time");
        Assert.Equal(0, PriorityCalculator.Calculate(time, Now.ToOffset(TimeSpan.FromHours(-7))));
        Assert.Equal(TimeKind.Start, time.Kind);
    }

    [Fact]
    public void ThreeDayBoundary_UsesSavedLocalDateAcrossUtcMidnight()
    {
        var time = new TaskTime(TimeKind.Deadline, new(2026, 10, 13), new(23, 59),
            "China Standard Time");
        var now = DateTimeOffset.Parse("2026-10-09T16:30:00Z");
        Assert.Equal(1, PriorityCalculator.Calculate(time, now));
    }
}
