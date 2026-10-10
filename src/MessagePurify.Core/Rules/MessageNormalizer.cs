using System.Text;
using System.Text.RegularExpressions;

namespace MessagePurify.Core;

public static class MessageNormalizer
{
    public static SchoolMessage Normalize(SchoolMessage message) => message with
    {
        Segments = message.Segments.Select(segment => segment with
        {
            Text = Regex.Replace(segment.Text.Normalize(NormalizationForm.FormKC)
                .Replace("\r\n", "\n").Replace('\r', '\n'), @"[^\S\n]+", " ").Trim()
        }).ToArray()
    };
}
