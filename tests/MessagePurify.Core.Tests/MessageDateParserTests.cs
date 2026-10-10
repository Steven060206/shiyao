using MessagePurify.Core;

namespace MessagePurify.Core.Tests;

public class MessageDateParserTests
{
    private const string Zone = "China Standard Time";
    private static readonly DateTimeOffset SentAt = DateTimeOffset.Parse("2026-10-10T23:30:00+08:00");

    [Theory]
    [InlineData("2026-10-12交作业", 2026, 10, 12)]
    [InlineData("2027/1/3交作业", 2027, 1, 3)]
    [InlineData("2026年10月12日交作业", 2026, 10, 12)]
    [InlineData("10月12日交作业", 2026, 10, 12)]
    [InlineData("10月12号交作业", 2026, 10, 12)]
    [InlineData("10/12交作业", 2026, 10, 12)]
    [InlineData("10-12交作业", 2026, 10, 12)]
    [InlineData("今天交作业", 2026, 10, 10)]
    [InlineData("明天交作业", 2026, 10, 11)]
    [InlineData("后天交作业", 2026, 10, 12)]
    [InlineData("本周一交作业", 2026, 10, 5)]
    [InlineData("这周星期日交作业", 2026, 10, 11)]
    [InlineData("下周一交作业", 2026, 10, 12)]
    [InlineData("下周星期五考试", 2026, 10, 16)]
    [InlineData("周六交作业", 2026, 10, 10)]
    [InlineData("星期一考试", 2026, 10, 12)]
    [InlineData("礼拜天交作业", 2026, 10, 11)]
    [InlineData("下月3日交作业", 2026, 11, 3)]
    [InlineData("下个月3号交作业", 2026, 11, 3)]
    [InlineData("１０月１２日交作业", 2026, 10, 12)]
    [InlineData("明天晚上交作业", 2026, 10, 11)]
    [InlineData("10月12日，即2026-10-12交作业", 2026, 10, 12)]
    public void ExplicitDate_RetainsDateOnlyPrecision(string text, int year, int month, int day)
    {
        var result = MessageDateParser.Parse(text, SentAt, Zone, TimeKind.Deadline);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(year, month, day), result.Time!.Date);
        Assert.Null(result.Time.Time);
        Assert.Equal(TimeKind.Deadline, result.Time.Kind);
        Assert.Equal(Zone, result.Time.TimeZoneId);
    }

    [Theory]
    [InlineData("明天18:30交报告", 18, 30)]
    [InlineData("明天１８：３０交报告", 18, 30)]
    [InlineData("明天18点交报告", 18, 0)]
    [InlineData("明天18点30分交报告", 18, 30)]
    [InlineData("明天下午3点交报告", 15, 0)]
    [InlineData("明天晚上8点半交报告", 20, 30)]
    [InlineData("明天上午9:15交报告", 9, 15)]
    [InlineData("明天上午12点交报告", 0, 0)]
    [InlineData("明天下午12点交报告", 12, 0)]
    [InlineData("明天中午12点交报告", 12, 0)]
    [InlineData("明天18:00（18点）交报告", 18, 0)]
    public void ExplicitClock_IsPreservedWithRequestedKind(string text, int hour, int minute)
    {
        var result = MessageDateParser.Parse(text, SentAt, Zone, TimeKind.Start);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(2026, 10, 11), result.Time!.Date);
        Assert.Equal(new TimeOnly(hour, minute), result.Time.Time);
        Assert.Equal(TimeKind.Start, result.Time.Kind);
    }

    [Theory]
    [InlineData("明天18点交报告", 2027, 1, 1)]
    [InlineData("后天18点交报告", 2027, 1, 2)]
    [InlineData("下月3日18点交报告", 2027, 1, 3)]
    [InlineData("1月3日18点交报告", 2026, 1, 3)]
    [InlineData("下周一18点交报告", 2027, 1, 4)]
    public void YearBoundary_UsesOriginalMessageDate(string text, int year, int month, int day)
    {
        var result = MessageDateParser.Parse(text, DateTimeOffset.Parse("2026-12-31T23:30:00+08:00"),
            Zone, TimeKind.Deadline);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(year, month, day), result.Time!.Date);
        Assert.Equal(new TimeOnly(18, 0), result.Time.Time);
    }

    [Fact]
    public void RelativeDate_UsesSavedZoneRatherThanTimestampOffset()
    {
        var result = MessageDateParser.Parse("明天18点交报告",
            DateTimeOffset.Parse("2026-12-31T18:30:00Z"), Zone, TimeKind.Deadline);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(2027, 1, 2), result.Time!.Date);
    }

    [Fact]
    public void Tonight_WithExplicitClockUsesMessageDate()
    {
        var result = MessageDateParser.Parse("今晚20:30交报告", SentAt, Zone, TimeKind.Deadline);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(2026, 10, 10), result.Time!.Date);
        Assert.Equal(new TimeOnly(20, 30), result.Time.Time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("交作业")]
    [InlineData("下周交作业")]
    [InlineData("稍后交作业")]
    [InlineData("今晚交作业")]
    [InlineData("晚上交作业")]
    [InlineData("晚上8点交作业")]
    [InlineData("18:30交作业")]
    [InlineData("下周18点交作业")]
    [InlineData("大后天交作业")]
    [InlineData("上周五交作业")]
    [InlineData("上周星期一交作业")]
    [InlineData("下下周星期一交作业")]
    [InlineData("下下周礼拜一交作业")]
    [InlineData("下下周一交作业")]
    [InlineData("明天三点交作业")]
    [InlineData("明天18.5点交作业")]
    [InlineData("明天18点一刻交作业")]
    [InlineData("明天18点30秒交作业")]
    [InlineData("明天18点30分20秒交作业")]
    public void MissingExplicitDate_DoesNotInventOne(string text)
    {
        var result = MessageDateParser.Parse(text, SentAt, Zone, TimeKind.Deadline);

        Assert.Equal(DateIssue.Missing, result.Issue);
        Assert.Null(result.Time);
        Assert.NotEmpty(result.Detail!);
    }

    [Theory]
    [InlineData("2026-02-30交作业")]
    [InlineData("2026年2月29日交作业")]
    [InlineData("13月12日交作业")]
    [InlineData("10/32交作业")]
    [InlineData("2026-13-12交作业")]
    [InlineData("10000年10月12日交作业")]
    [InlineData("99999999999999999999999年10月12日交作业")]
    [InlineData("99999999999999999999999/12交作业")]
    [InlineData("下月32日交作业")]
    [InlineData("明天25:00交作业")]
    [InlineData("明天18:60交作业")]
    [InlineData("明天24点交作业")]
    [InlineData("明天99999999999999999999999点交作业")]
    [InlineData("明天99999999999999999999999:30交作业")]
    [InlineData("明天下午15点交作业")]
    [InlineData("明天上午0点交作业")]
    [InlineData("明天18点60分交作业")]
    [InlineData("10-12-13交作业")]
    [InlineData("2026-10-12-13交作业")]
    [InlineData("2026/10/12/13交作业")]
    [InlineData("2026-10/12交作业")]
    [InlineData("明天18:30:00交作业")]
    public void InvalidNumericValue_ReturnsIssueWithoutThrowing(string text)
    {
        var result = MessageDateParser.Parse(text, SentAt, Zone, TimeKind.Deadline);

        Assert.Equal(DateIssue.Invalid, result.Issue);
        Assert.Null(result.Time);
        Assert.NotEmpty(result.Detail!);
    }

    [Theory]
    [InlineData("10月12日或13日交作业")]
    [InlineData("10月12日至13日考试")]
    [InlineData("10月12日到10月14日考试")]
    [InlineData("10月12日或者10月13日交作业")]
    [InlineData("今天布置，明天交作业")]
    [InlineData("明天或后天交作业")]
    [InlineData("下周一至周三考试")]
    [InlineData("明天18点或20点交作业")]
    [InlineData("明天18:00至20:00考试")]
    [InlineData("明天18点至18点考试")]
    [InlineData("10月12日至10月12日考试")]
    [InlineData("10月12-13日考试")]
    [InlineData("10月12到13日考试")]
    [InlineData("明天晚上12点交报告")]
    public void ConflictingDatesOrRanges_RequireConfirmation(string text)
    {
        var result = MessageDateParser.Parse(text, SentAt, Zone, TimeKind.Deadline);

        Assert.Equal(DateIssue.Ambiguous, result.Issue);
        Assert.Null(result.Time);
        Assert.NotEmpty(result.Detail!);
    }

    [Theory]
    [InlineData("2026-03-08 02:30考试")]
    [InlineData("2026-11-01 01:30考试")]
    public void DstGapOrRepeatedClock_IsInvalid(string text)
    {
        var result = MessageDateParser.Parse(text, SentAt, "Eastern Standard Time", TimeKind.Start);

        Assert.Equal(DateIssue.Invalid, result.Issue);
        Assert.Null(result.Time);
    }

    [Theory]
    [InlineData("明天交作业")]
    [InlineData("明天18点交作业")]
    public void UnknownTimeZone_IsInvalidForEitherPrecision(string text)
    {
        var result = MessageDateParser.Parse(text, SentAt, "MessagePurify/MissingZone", TimeKind.Deadline);

        Assert.Equal(DateIssue.Invalid, result.Issue);
        Assert.Null(result.Time);
    }

    [Fact]
    public void NextMonth_DoesNotClampAnInvalidDay()
    {
        var result = MessageDateParser.Parse("下月31日交作业",
            DateTimeOffset.Parse("2026-01-31T12:00:00+08:00"), Zone, TimeKind.Deadline);

        Assert.Equal(DateIssue.Invalid, result.Issue);
        Assert.Null(result.Time);
    }

    [Fact]
    public void RelativeDateBeyondRepresentableCalendar_IsInvalid()
    {
        var result = MessageDateParser.Parse("后天交作业",
            DateTimeOffset.Parse("9999-12-30T00:00:00Z"), "UTC", TimeKind.Deadline);

        Assert.Equal(DateIssue.Invalid, result.Issue);
        Assert.Null(result.Time);
    }
}
