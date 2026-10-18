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

    [Fact]
    public void ActivityNotice_WithWeekdayDateAliasAndRoom_RetainsStartFromOriginalMessageDate()
    {
        const string text = "关于26餐饮设计比赛赛前打磨工作的通知：\n时间：下周二(10.20)下午1点～4点\n地点：北4-302";
        var result = MessageDateParser.Parse(text, DateTimeOffset.Parse("2026-10-16T18:00:00+08:00"),
            Zone, TimeKind.Start);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(2026, 10, 20), result.Time!.Date);
        Assert.Equal(new TimeOnly(13, 0), result.Time.Time);
        Assert.Equal(TimeKind.Start, result.Time.Kind);
    }

    [Theory]
    [InlineData("明天下午1点～4点", 13, 0)]
    [InlineData("明天1点到下午4点", 13, 0)]
    [InlineData("明天下午1点至16点", 13, 0)]
    [InlineData("明天13点至下午4点", 13, 0)]
    [InlineData("明天下午12点到1点", 12, 0)]
    [InlineData("明天上午11点至下午1点", 11, 0)]
    [InlineData("明天上午9点至中午12点", 9, 0)]
    [InlineData("明天晚上8点～10点", 20, 0)]
    [InlineData("明天凌晨1点到3点", 1, 0)]
    [InlineData("明天9点至11点", 9, 0)]
    [InlineData("明天13:00-16:00", 13, 0)]
    [InlineData("明天13时00分—16时00分", 13, 0)]
    [InlineData("明天 下午1点半 ~ 4点30分", 13, 30)]
    [InlineData("明天上午9:15到11:45", 9, 15)]
    public void ReliableSameDayActivityRange_UsesStartAndSharesPeriodOnlyWithinRange(
        string text, int hour, int minute)
    {
        var result = MessageDateParser.Parse(text, SentAt, Zone, TimeKind.Start);
        var notice = MessageDateParser.ParseNotice(text, SentAt, Zone);
        var deadline = MessageDateParser.Parse(text, SentAt, Zone, TimeKind.Deadline);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(2026, 10, 11), result.Time!.Date);
        Assert.Equal(new TimeOnly(hour, minute), result.Time.Time);
        Assert.Equal(TimeKind.Start, result.Time.Kind);
        Assert.Equal(result, notice);
        Assert.Equal(DateIssue.Ambiguous, deadline.Issue);
        Assert.Null(deadline.Time);
    }

    [Theory]
    [InlineData("通知：明天", null)]
    [InlineData("通知：明天下午1点", 13)]
    public void NoticeWithSingleDateOrClock_KeepsExistingDeadlineSemantics(string text, int? hour)
    {
        var result = MessageDateParser.ParseNotice(text, SentAt, Zone);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(2026, 10, 11), result.Time!.Date);
        Assert.Equal(hour is null ? (TimeOnly?)null : new TimeOnly(hour.Value, 0), result.Time.Time);
        Assert.Equal(TimeKind.Deadline, result.Time.Kind);
    }

    [Theory]
    [InlineData("2026-10-16T18:00:00+08:00", "下周二(10.20)", 2026, 10, 20)]
    [InlineData("2026-12-31T18:00:00+08:00", "下周二（1.5）", 2027, 1, 5)]
    [InlineData("2027-10-15T18:00:00+08:00", "下周三(10.20)", 2027, 10, 20)]
    [InlineData("2026-10-16T18:00:00+08:00", "2027年10月20日(10.20)", 2027, 10, 20)]
    public void BracketedMonthDayAlias_ValidatesAgainstDateAndInheritsItsYear(
        string sentAt, string date, int year, int month, int day)
    {
        var result = MessageDateParser.ParseNotice($"通知：{date}下午1点～4点",
            DateTimeOffset.Parse(sentAt), Zone);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(year, month, day), result.Time!.Date);
        Assert.Equal(new TimeOnly(13, 0), result.Time.Time);
    }

    [Theory]
    [InlineData("下周二(10.21)", DateIssue.Ambiguous)]
    [InlineData("下周三(10.20)", DateIssue.Ambiguous)]
    [InlineData("2027年10月20日(10.21)", DateIssue.Ambiguous)]
    [InlineData("下周二(13.20)", DateIssue.Invalid)]
    [InlineData("下周二(2.30)", DateIssue.Invalid)]
    [InlineData("下周二(2.29)", DateIssue.Invalid)]
    [InlineData("下周二(100.20)", DateIssue.Invalid)]
    [InlineData("下周二(10.20.1)", DateIssue.Invalid)]
    [InlineData("下周二(10.20或10.21)", DateIssue.Invalid)]
    [InlineData("下周二(10.20", DateIssue.Invalid)]
    public void ConflictingOrInvalidMonthDayAlias_DoesNotSilentlyIgnoreExplicitDate(
        string date, DateIssue expectedIssue)
    {
        var result = MessageDateParser.ParseNotice($"通知：{date}下午1点～4点",
            DateTimeOffset.Parse("2026-10-16T18:00:00+08:00"), Zone);

        Assert.Equal(expectedIssue, result.Issue);
        Assert.Null(result.Time);
    }

    [Theory]
    [InlineData("预算10.20万元")]
    [InlineData("评分10.20分")]
    [InlineData("版本v1.2通知")]
    [InlineData("预算(10.20)通知")]
    [InlineData("10.20下午1点～4点")]
    public void DecimalOrStandaloneDotNumbers_DoNotInventCalendarDate(string text)
    {
        var result = MessageDateParser.ParseNotice(text, SentAt, Zone);

        Assert.Equal(DateIssue.Missing, result.Issue);
        Assert.Null(result.Time);
    }

    [Fact]
    public void DecimalElsewhereInMessage_DoesNotBecomeConflictingDateAlias()
    {
        var result = MessageDateParser.ParseNotice("明天（预算10.20万元）下午1点～4点", SentAt, Zone);

        Assert.Equal(DateIssue.None, result.Issue);
        Assert.Equal(new DateOnly(2026, 10, 11), result.Time!.Date);
        Assert.Equal(new TimeOnly(13, 0), result.Time.Time);
    }

    [Theory]
    [InlineData("明天9点或11点")]
    [InlineData("明天9点或者11点")]
    [InlineData("明天9点、11点")]
    [InlineData("明天9点，11点")]
    [InlineData("明天11点到9点")]
    [InlineData("明天9点到9点")]
    [InlineData("明天23点到1点")]
    [InlineData("明天晚上11点到凌晨1点")]
    [InlineData("明天晚上10点到12点")]
    [InlineData("明天中午11点到1点")]
    [InlineData("明天9点到11点到13点")]
    [InlineData("明天9点到11点，13点到15点")]
    [InlineData("明天9点到后天11点")]
    [InlineData("10月12日至10月12日9点到11点")]
    [InlineData("明天9点到25点")]
    [InlineData("明天下午13点到16点")]
    [InlineData("明天9:00到11:00:00")]
    [InlineData("明天9点到11点一刻")]
    [InlineData("明天9点30秒到11点")]
    [InlineData("9点到11点")]
    public void UnreliableRange_ProducesNoStartForTaskOrNotice(string text)
    {
        var activity = MessageDateParser.Parse(text, SentAt, Zone, TimeKind.Start);
        var notice = MessageDateParser.ParseNotice(text, SentAt, Zone);

        Assert.NotEqual(DateIssue.None, activity.Issue);
        Assert.Null(activity.Time);
        Assert.NotEqual(DateIssue.None, notice.Issue);
        Assert.Null(notice.Time);
    }

    [Fact]
    public void ThreeRepeatedClocks_CannotBecomeActivityStart()
    {
        var result = MessageDateParser.Parse("明天9点（9点）（9点）", SentAt, Zone, TimeKind.Start);

        Assert.Equal(DateIssue.Ambiguous, result.Issue);
        Assert.Null(result.Time);
    }

    [Theory]
    [InlineData("2026-03-08 01:30-02:30")]
    [InlineData("2026-11-01 00:30-01:30")]
    public void ActivityRange_InvalidOrAmbiguousEndInSavedZone_IsNotDiscarded(string text)
    {
        var result = MessageDateParser.ParseNotice(text, SentAt, "Eastern Standard Time");

        Assert.Equal(DateIssue.Invalid, result.Issue);
        Assert.Null(result.Time);
    }
}
