using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Application;

public sealed record CrossGroupMergeGroup(string Id, string Name, string? Course, bool Excluded);
public sealed record CrossGroupMergeMatch(string Id, string FirstTaskId, long FirstTaskVersion,
    string SecondTaskId, long SecondTaskVersion, IReadOnlyList<string> Reasons);

public static class CrossGroupMergeFinder
{
    // 原消息和候选一致仅构成待核对建议，不证明跨群对象或原通知身份相同。
    public static IReadOnlyList<CrossGroupMergeMatch> Find(IReadOnlyList<TaskItem> tasks,
        IReadOnlyList<PendingReview> reviews, IReadOnlyList<CrossGroupMergeGroup> groups,
        Func<MessageKey, StoredMessage?> findMessage)
    {
        var byId = tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var byGroup = groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        var current = tasks.Where(task => task.MergedIntoId is null)
            .OrderBy(task => task.Id, StringComparer.Ordinal).ToArray();
        var separated = new HashSet<(string First, string Second)>();
        var splitFamily = new HashSet<string>(StringComparer.Ordinal);

        string? Resolve(string id)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (byId.TryGetValue(id, out var task) && visited.Add(id))
            {
                if (task.MergedIntoId is null) return id;
                if (!byId.TryGetValue(task.MergedIntoId, out var target) || target.AccountId != task.AccountId) return null;
                id = target.Id;
            }
            return null;
        }

        foreach (var task in tasks)
        {
            if (Resolve(task.Id) is not { } id) continue;
            foreach (var other in task.SeparateTaskIds ?? [])
                if (Resolve(other) is { } otherId && id != otherId)
                    separated.Add(Pair(id, otherId));
            foreach (var related in task.RelatedItemIds ?? [])
                if (Resolve(related) is { } relatedId && id != relatedId)
                {
                    splitFamily.Add(id);
                    splitFamily.Add(relatedId);
                }
        }
        foreach (var review in reviews)
            if (review.SplitParentTaskId is { } parent && Resolve(parent) is { } parentId)
            {
                splitFamily.Add(parentId);
                if (review.AcceptedTaskId is { } child && Resolve(child) is { } childId)
                    splitFamily.Add(childId);
            }

        var shared = current.SelectMany(task => task.Evidence.Distinct())
            .GroupBy(key => key).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var sources = new Dictionary<string, IReadOnlyList<OriginalSource>>(StringComparer.Ordinal);
        foreach (var task in current)
        {
            if (task.Source == "manual" || splitFamily.Contains(task.Id)) continue;
            var originals = new List<OriginalSource>();
            foreach (var key in task.Evidence.Distinct())
            {
                if (key.AccountId != task.AccountId || shared.Contains(key) ||
                    !byGroup.TryGetValue(key.GroupId, out var group) || group.Excluded || string.IsNullOrWhiteSpace(group.Course)) continue;
                var stored = findMessage(key);
                if (stored is null || stored.Message.Key != key || stored.Message.IsPrivate ||
                    stored.Message.Segments.Any(segment => segment.Kind == SegmentKind.Quote) ||
                    stored.Extraction?.Candidates.Count != 1) continue;
                var candidate = stored.Extraction.Candidates[0];
                var assessment = candidate.Assessments;
                if (candidate.Disposition != RuleDisposition.Task || candidate.Time is null ||
                    candidate.Action is not (RuleAction.Submit or RuleAction.Complete or RuleAction.Attend or RuleAction.Register) ||
                    !assessment.Action.Reliable || !assessment.Audience.Reliable || !assessment.Course.Reliable ||
                    !assessment.Time.Reliable || !assessment.Association.Reliable ||
                    string.IsNullOrWhiteSpace(candidate.Title) || string.IsNullOrWhiteSpace(candidate.Course) ||
                    Normalize(candidate.Course) != Normalize(group.Course) || !candidate.Evidence.Contains(key)) continue;
                var text = Normalize(string.Join("\n", stored.Message.Segments
                    .Where(segment => segment.Kind == SegmentKind.Text).Select(segment => segment.Text)));
                if (text.Length > 0)
                    originals.Add(new(key.GroupId, group.Name, text, Normalize(candidate.Title),
                        candidate.Action, Normalize(candidate.Course), candidate.Time));
            }
            if (originals.Count > 0) sources[task.Id] = originals;
        }

        var matches = new List<CrossGroupMergeMatch>();
        for (var firstIndex = 0; firstIndex < current.Length; firstIndex++)
        {
            var first = current[firstIndex];
            if (!sources.TryGetValue(first.Id, out var firstSources)) continue;
            for (var secondIndex = firstIndex + 1; secondIndex < current.Length; secondIndex++)
            {
                var second = current[secondIndex];
                if (first.AccountId != second.AccountId || separated.Contains((first.Id, second.Id)) ||
                    first.Evidence.Intersect(second.Evidence).Any() || !sources.TryGetValue(second.Id, out var secondSources)) continue;
                var same = firstSources.Any(a => secondSources.Any(b => a.GroupId != b.GroupId &&
                    a.Text == b.Text && a.Title == b.Title && a.Action == b.Action && a.Course == b.Course && a.Time == b.Time &&
                    !ConflictingClasses(a.GroupName, b.GroupName)));
                if (!same) continue;
                matches.Add(new(StableId(first.AccountId, first.Id, second.Id), first.Id, first.Version, second.Id, second.Version,
                    ["跨群原消息完整原文相同", "双方来源群当前已确认同一课程", "原始行动和原始正式时间一致", "可能重复，请核对适用对象并确认是否合并"]));
            }
        }
        return matches;
    }

    private sealed record OriginalSource(string GroupId, string GroupName, string Text, string Title,
        RuleAction Action, string Course, TaskTime Time);

    private static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim();

    private static (string First, string Second) Pair(string first, string second) =>
        string.CompareOrdinal(first, second) < 0 ? (first, second) : (second, first);

    private static string StableId(string account, string first, string second)
    {
        var value = $"{account.Length}:{account}{first.Length}:{first}{second.Length}:{second}";
        return "cross-group-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
    }

    private static bool ConflictingClasses(string first, string second)
    {
        var a = ClassNumbers(first);
        var b = ClassNumbers(second);
        return a.Count > 0 && b.Count > 0 && !a.SetEquals(b);
    }

    private static HashSet<string> ClassNumbers(string name) => Regex.Matches(Normalize(name), @"(?<number>[0-9零〇一二三四五六七八九十百]+)\s*\)?\s*班")
        .Select(match => Number(match.Groups["number"].Value)).ToHashSet(StringComparer.Ordinal);

    private static string Number(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var arabic)) return arabic.ToString(CultureInfo.InvariantCulture);
        var result = 0;
        var digit = 0;
        foreach (var character in value)
        {
            if (character is '十' or '百')
            {
                result += (digit == 0 ? 1 : digit) * (character == '十' ? 10 : 100);
                digit = 0;
            }
            else
            {
                var number = "零一二三四五六七八九".IndexOf(character);
                digit = digit * 10 + (number < 0 ? 0 : number);
            }
        }
        return (result + digit).ToString(CultureInfo.InvariantCulture);
    }
}
