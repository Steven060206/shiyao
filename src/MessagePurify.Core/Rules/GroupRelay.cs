using System.Text;
using System.Text.RegularExpressions;

namespace MessagePurify.Core;

public static class GroupRelay
{
    private static readonly Regex Heading = new(@"^(?:# *(?:群)?接龙(?=$|[\s:])|【(?:群)?接龙】|\[(?:群)?接龙\]|(?:群)?接龙(?=$|[\s:]))");
    private const string Separator = @"(?:\.(?![0-9])|[、)])";
    private static readonly Regex FirstEntry = new(@"(?<!\S)(?:1" + Separator + @"|\(1\))[ \t]*");
    private static readonly Regex NumberedEntry = new(@"^(?:[1-9][0-9]*" + Separator + @"|\([1-9][0-9]*\))[ \t]*");
    private static readonly Regex Instruction = new(@"^(?:请|务必|必须|所有同学|全体同学|明天|后天|本周|下周)");

    public static string AnalysisText(SchoolMessage message, SegmentKind kind) =>
        WithoutResponses(string.Join("\n", message.Segments.Where(segment => segment.Kind == kind).Select(segment => segment.Text)));

    // Only retire an existing review when the original is entirely a text roster.
    public static bool IsTextResponseList(SchoolMessage message) =>
        !message.Segments.Any(segment => segment.Kind == SegmentKind.Image) &&
        message.Segments.Any(segment => segment.Kind == SegmentKind.Text && segment.Text.Contains("接龙")) &&
        string.IsNullOrWhiteSpace(AnalysisText(message, SegmentKind.Text));

    internal static string WithoutResponses(string text)
    {
        if (!text.Contains("接龙")) return text;
        var kept = new List<string>();
        var headingIndex = -1;
        var inRoster = false;
        foreach (var line in text.Normalize(NormalizationForm.FormKC).ReplaceLineEndings("\n").Split('\n'))
        {
            var value = line.Trim();
            var heading = Heading.Match(value);
            if (heading.Success)
            {
                headingIndex = kept.Count;
                inRoster = false;
                value = value[heading.Length..].TrimStart(' ', ':');
            }

            // Wait for participant 1 before removing the copied prompt and its deadline.
            // A standalone request to start a relay is still an ordinary school message.
            if (headingIndex >= 0 && FirstEntry.Match(value) is { Success: true } first)
            {
                if (IsResponse(value, first))
                {
                    kept.RemoveRange(headingIndex, kept.Count - headingIndex);
                    headingIndex = -1;
                    inRoster = true;
                    continue;
                }
                headingIndex = -1;
            }
            if (inRoster && (value.Length == 0 || IsResponse(value, NumberedEntry.Match(value)))) continue;
            inRoster = false;
            kept.Add(line);
        }
        return string.Join("\n", kept);
    }

    private static bool IsResponse(string line, Match number) => number.Success &&
        number.Index + number.Length < line.Length && !Instruction.IsMatch(line[(number.Index + number.Length)..]);
}
