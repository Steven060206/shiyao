using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.Infrastructure.Tests;

public sealed class OneBotMessageParserTests
{
    private const string Message = """
        {"post_type":"message","message_type":"group","self_id":9007199254740993,
         "group_id":9007199254740995,"message_id":-123,"message_seq":-123,"real_seq":"0077",
         "user_id":9007199254740997,"time":1791597600,
         "message":[{"type":"text","data":{"text":"明天18点交作业"}},
                    {"type":"image","data":{"file":"notice.png","url":"http://example.invalid/private"}},
                    {"type":"reply","data":{"id":"-122","seq":77}},
                    {"type":"at","data":{"qq":"all"}}]}
        """;

    [Fact]
    public void Parse_PreservesLargeNumericIdsSendingTimeAndProtocolFields()
    {
        var result = OneBotMessageParser.ParseGroupMessage(Json(Message), "9007199254740993");
        Assert.Equal(new MessageKey("9007199254740993", "9007199254740995", "-123"), result.Message.Key);
        Assert.Equal("9007199254740997", result.Message.SenderId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791597600), result.Message.SentAt);
        Assert.Equal("-123", result.MessageSequence);
        Assert.Equal("0077", result.RealSequence);
        Assert.Equal(4, result.ProtocolSegments.Count);
        Assert.Equal("all", result.ProtocolSegments[3].Data.GetProperty("qq").GetString());
    }

    [Fact]
    public void Parse_MapsTextImageAndReplyWithoutTreatingImageSummaryAsOcr()
    {
        var result = OneBotMessageParser.ParseGroupMessage(Json(Message), "9007199254740993");
        Assert.Equal(3, result.Message.Segments.Count);
        Assert.Equal(new MessageSegment(SegmentKind.Text, "明天18点交作业"), result.Message.Segments[0]);
        Assert.Equal(new MessageSegment(SegmentKind.Image, "", AssetId: "notice.png"), result.Message.Segments[1]);
        Assert.Equal(new MessageSegment(SegmentKind.Quote, "", new("9007199254740993", "9007199254740995", "-122")), result.Message.Segments[2]);
    }

    [Fact]
    public void Parse_HistoryWithoutSelfIdUsesVerifiedAccount()
    {
        var source = Message.Replace("\"self_id\":9007199254740993,", "");
        Assert.Equal("9007199254740993", OneBotMessageParser.ParseGroupMessage(Json(source), "9007199254740993").Message.Key.AccountId);
    }

    [Fact]
    public void Parse_ReplySequenceAloneDoesNotInventMessageEvidence()
    {
        var source = Message.Replace("\"id\":\"-122\",", "");
        var result = OneBotMessageParser.ParseGroupMessage(Json(source), "9007199254740993");
        Assert.Null(result.Message.Segments[2].Reference);
        Assert.Equal(77, result.ProtocolSegments[2].Data.GetProperty("seq").GetInt32());
    }

    [Theory]
    [InlineData("\"self_id\":9007199254740993", "\"self_id\":9007199254740994", "account-mismatch")]
    [InlineData("\"message_type\":\"group\"", "\"message_type\":\"private\"", "private-message")]
    [InlineData("\"message_id\":-123", "\"message_id\":1.5", "invalid-id")]
    [InlineData("\"time\":1791597600", "\"time\":1e30", "invalid-time")]
    [InlineData("\"user_id\":9007199254740997", "\"user_id\":null", "invalid-id")]
    public void Parse_RejectsInvalidScopeAndFields(string oldValue, string newValue, string code)
    {
        var exception = Assert.Throws<OneBotProtocolException>(() =>
            OneBotMessageParser.ParseGroupMessage(Json(Message.Replace(oldValue, newValue)), "9007199254740993"));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Parse_RejectsWrongHistoryGroup()
    {
        var exception = Assert.Throws<OneBotProtocolException>(() =>
            OneBotMessageParser.ParseGroupMessage(Json(Message), "9007199254740993", "different"));
        Assert.Equal("group-mismatch", exception.Code);
    }

    [Fact]
    public void Parse_RequiresArrayFormatRatherThanTurningCqIntoActionText()
    {
        var message = Json("""{"message_type":"group","group_id":"g","message_id":"m","user_id":"u","time":1791597600,"message":"[CQ:image,file=x]明天交作业"}""");
        var exception = Assert.Throws<OneBotProtocolException>(() => OneBotMessageParser.ParseGroupMessage(message, "a"));
        Assert.Equal("array-format-required", exception.Code);
    }

    [Fact]
    public void Parse_UnknownSegmentPreservesDataWithoutCreatingActionText()
    {
        var message = Json("""{"message_type":"group","group_id":"g","message_id":"m","user_id":"u","time":1791597600,"message":[{"type":"future","data":{"text":"明天交作业"}}]}""");
        var result = OneBotMessageParser.ParseGroupMessage(message, "a");
        Assert.Empty(result.Message.Segments);
        Assert.Equal("future", Assert.Single(result.ProtocolSegments).Type);
        Assert.Equal("明天交作业", result.ProtocolSegments[0].Data.GetProperty("text").GetString());
    }

    [Fact]
    public void Parse_StringIdsKeepLeadingZeroes()
    {
        var message = Json("""{"self_id":"0001","message_type":"group","group_id":"0002","message_id":"0003","user_id":"0004","time":1791597600,"message":[]}""");
        Assert.Equal(new MessageKey("0001", "0002", "0003"), OneBotMessageParser.ParseGroupMessage(message, "0001").Message.Key);
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
