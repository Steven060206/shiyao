using MessagePurify.Core;
using Xunit;

namespace MessagePurify.Core.Tests;

public class MessageNormalizerTests
{
    [Fact]
    public void Normalize_PreservesEvidenceAndOriginalSendingContext()
    {
        var key = new MessageKey("account", "group", "9007199254740993");
        var quoted = new MessageKey("account", "group", "original");
        var sent = DateTimeOffset.Parse("2026-12-31T23:30:00+08:00");
        var message = new SchoolMessage(key, "sender", sent, new[]
        {
            new MessageSegment(SegmentKind.Text, "明天　１８：００  交作业\r\n请附封面"),
            new MessageSegment(SegmentKind.Image, "１０月１２日", null, "image-1"),
            new MessageSegment(SegmentKind.Quote, "旧通知", quoted)
        });

        var result = MessageNormalizer.Normalize(message);

        Assert.Equal(key, result.Key);
        Assert.Equal(sent, result.SentAt);
        Assert.Equal("sender", result.SenderId);
        Assert.Equal("明天 18:00 交作业\n请附封面", result.Segments[0].Text);
        Assert.Equal(SegmentKind.Image, result.Segments[1].Kind);
        Assert.Equal("image-1", result.Segments[1].AssetId);
        Assert.Equal("10月12日", result.Segments[1].Text);
        Assert.Equal(quoted, result.Segments[2].Reference);
        Assert.Equal("明天　１８：００  交作业\r\n请附封面", message.Segments[0].Text);
    }

    [Fact]
    public void Normalize_PreservesPrivateScopeAndEmptyImageEvidence()
    {
        var message = new SchoolMessage(new("a", "g", "m"), "s",
            DateTimeOffset.Parse("2026-10-10T10:00:00+08:00"),
            new[] { new MessageSegment(SegmentKind.Image, "", AssetId: "failed-ocr") }, true);

        var result = MessageNormalizer.Normalize(message);

        Assert.True(result.IsPrivate);
        Assert.Single(result.Segments);
        Assert.Equal("", result.Segments[0].Text);
        Assert.Equal("failed-ocr", result.Segments[0].AssetId);
    }
}
