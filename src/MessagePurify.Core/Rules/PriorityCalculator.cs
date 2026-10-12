using System.Text.RegularExpressions;

namespace MessagePurify.Core;

public static class PriorityCalculator
{
    /// <summary>保留明确后果这一事实，与当前日期分级是否已达到 P0 无关。</summary>
    public static bool HasClearConsequence(string text) => Regex.IsMatch(text,
        "影响成绩|影响资格|取消资格|逾期不收|计入成绩|扣分") && !Regex.IsMatch(text,
        "不影响(?:成绩|资格)|不计入成绩|不扣分|不会扣分|逾期(?:也|仍)?(?:收|接收)|不是逾期不收");

    public static int Calculate(TaskTime time, DateTimeOffset now, bool clearConsequence = false)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(time.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var days = time.Date.DayNumber - today.DayNumber;
        var priority = days <= 0 ? 0 : days <= 3 ? 1 : days <= 7 ? 2 : 3;
        return clearConsequence ? Math.Max(0, priority - 1) : priority;
    }
}
