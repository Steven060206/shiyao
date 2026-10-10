using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MessagePurify.Core;

public enum DateIssue { None, Missing, Invalid, Ambiguous }

public sealed record DateParseResult(TaskTime? Time, DateIssue Issue, string? Detail);

/// <summary>以原消息发送时刻为基准；冲突、范围及无法唯一解释的本地时刻不生成正式时间。</summary>
public static class MessageDateParser
{
    private static readonly Regex Dates = Pattern(
        @"(?<![0-9])(?:(?<year>[0-9]+)(?<separator>[-/])(?<month>[0-9]+)\k<separator>(?<day>[0-9]+)" +
        @"|(?<year>[0-9]+)年(?<month>[0-9]+)月(?<day>[0-9]+)[日号]" +
        @"|(?<month>[0-9]+)月(?<day>[0-9]+)[日号]" +
        @"|(?<month>[0-9]+)[/-](?<day>[0-9]+)(?![/-][0-9])" +
        @"|(?<nextMonth>下个?月)(?<day>[0-9]+)[日号]" +
        @"|(?<relative>(?<!大)(?:今天|明天|后天)|今晚)" +
        @"|(?<![上下个])(?:(?<week>下周|本周|这周)(?:星期|周|礼拜)?" +
        @"|(?<week>下|本|这)?(?:星期|周|礼拜))(?<weekday>[一二三四五六日天]))");

    private static readonly Regex Clocks = Pattern(
        @"(?<![0-9.:])(?<period>上午|下午|晚上|中午|凌晨|早上)?\s*" +
        @"(?<hour>[0-9]+)(?::(?<minute>[0-9]+)(?<seconds>:[0-9]+)?" +
        @"|[点时](?:(?<half>半)|(?<minute>[0-9]+)分?)?)(?![0-9])");

    private static readonly Regex RangeConnector = Pattern(@"^\s*(?:或(?:者)?|至|到|[-—~～、])\s*$");
    private static readonly Regex AbbreviatedDateRange = Pattern(
        @"(?:[日号]|[0-9]+月[0-9]+)\s*(?:或(?:者)?|至|到|[-—~～、])\s*[0-9]+[日号]");
    private static readonly Regex ClockExpressions = Pattern(
        @"(?:[0-9]+(?:\.[0-9]+)?|[零〇一二两三四五六七八九十]+)[点时](?:半|一刻|三刻|[0-9零〇一二两三四五六七八九十]+分?)?" +
        @"|[0-9]+:[0-9]+(?::[0-9]+)?");

    public static DateParseResult Parse(string text, DateTimeOffset sentAt,
        string timeZoneId, TimeKind kind)
    {
        try
        {
            if (!Enum.IsDefined(kind))
                return Issue(DateIssue.Invalid, "时间类型无效。");

            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var messageDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(sentAt, zone).DateTime);
            var normalized = text.Normalize(NormalizationForm.FormKC);
            var dates = Dates.Matches(normalized).ToArray();
            var clocks = Clocks.Matches(normalized).ToArray();
            var parsedDates = dates.Select(match => ReadDate(match, messageDate)).ToArray();
            var parsedClocks = clocks.Select(ReadClock).ToArray();

            // 保留多个完整日期给上层按行动语义拆分，不在这里择早或择晚。
            if (parsedDates.Distinct().Count() > 1 || parsedClocks.Distinct().Count() > 1 ||
                HasRange(normalized, dates) || HasRange(normalized, clocks) ||
                AbbreviatedDateRange.IsMatch(normalized) ||
                clocks.Any(clock => clock.Groups["period"].Value == "晚上" && Number(clock.Groups["hour"]) == 12))
                return Issue(DateIssue.Ambiguous, "存在多个时间或日期范围，需要确认各自含义。");

            if (ClockExpressions.Matches(normalized).Any(expression =>
                !clocks.Any(clock => clock.Index <= expression.Index &&
                    clock.Index + clock.Length >= expression.Index + expression.Length)))
                return Issue(DateIssue.Missing, "存在尚未支持的钟点表达，需要确认。");

            if (dates.Length == 0 ||
                (dates.All(match => match.Groups["relative"].Value == "今晚") && clocks.Length == 0))
                return Issue(DateIssue.Missing, "缺少明确日期，需要补充或确认。");

            var time = new TaskTime(kind, parsedDates[0],
                parsedClocks.Length == 0 ? null : parsedClocks[0], timeZoneId);
            // 复用正式时间校验，不猜测夏令时跳过或重复的当地钟点。
            _ = time.GetExactInstant();
            return new DateParseResult(time, DateIssue.None, null);
        }
        catch (Exception exception) when (exception is ArgumentException or
            TimeZoneNotFoundException or InvalidTimeZoneException or RegexMatchTimeoutException)
        {
            return Issue(DateIssue.Invalid, "日期、钟点或保存的时区无法可靠解释，需要确认。");
        }
    }

    private static DateOnly ReadDate(Match match, DateOnly messageDate)
    {
        if (match.Groups["relative"].Success)
            return messageDate.AddDays(match.Groups["relative"].Value switch
            {
                "明天" => 1,
                "后天" => 2,
                _ => 0
            });

        if (match.Groups["weekday"].Success)
        {
            var target = "一二三四五六日".IndexOf(match.Groups["weekday"].Value.Replace('天', '日')[0]);
            var current = ((int)messageDate.DayOfWeek + 6) % 7;
            var week = match.Groups["week"].Value;
            var distance = week.Length == 0 ? (target - current + 7) % 7 : target - current;
            if (week.StartsWith('下'))
                distance += 7;
            return messageDate.AddDays(distance);
        }

        var year = match.Groups["year"].Success ? Number(match.Groups["year"]) : messageDate.Year;
        if (match.Groups["year"].Success && match.Groups["year"].Value.Length != 4)
            throw new ArgumentException("明确年份需为四位数。");
        var month = match.Groups["month"].Success ? Number(match.Groups["month"]) : messageDate.Month;
        if (match.Groups["nextMonth"].Success)
        {
            var nextMonth = new DateOnly(messageDate.Year, messageDate.Month, 1).AddMonths(1);
            year = nextMonth.Year;
            month = nextMonth.Month;
        }

        return new DateOnly(year, month, Number(match.Groups["day"]));
    }

    private static TimeOnly ReadClock(Match match)
    {
        if (match.Groups["seconds"].Success)
            throw new ArgumentException("尚未支持含秒钟的时间表达。");

        var hour = Number(match.Groups["hour"]);
        var minute = match.Groups["half"].Success ? 30 :
            match.Groups["minute"].Success ? Number(match.Groups["minute"]) : 0;
        var period = match.Groups["period"].Value;
        if (period.Length != 0)
        {
            if (hour is < 1 or > 12)
                throw new ArgumentException("上午或下午钟点需明确。");
            if (period is "下午" or "晚上")
                hour = hour == 12 ? period == "下午" ? 12 : 0 : hour + 12;
            else if (period is "上午" or "早上" or "凌晨")
                hour %= 12;
            else if (period == "中午" && hour < 11)
                throw new ArgumentException("中午钟点需确认。");
        }

        return new TimeOnly(hour, minute);
    }

    private static int Number(Group group) =>
        int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value : throw new ArgumentException("数值超出支持范围。");

    private static bool HasRange(string text, Match[] matches)
    {
        for (var index = 1; index < matches.Length; index++)
        {
            var previousEnd = matches[index - 1].Index + matches[index - 1].Length;
            if (RangeConnector.IsMatch(text[previousEnd..matches[index].Index]))
                return true;
        }
        return false;
    }

    private static Regex Pattern(string pattern) => new(pattern,
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static DateParseResult Issue(DateIssue issue, string detail) => new(null, issue, detail);

}
