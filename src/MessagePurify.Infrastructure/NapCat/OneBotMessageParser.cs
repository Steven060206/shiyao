using System.Text.Json;
using MessagePurify.Core;

namespace MessagePurify.Infrastructure.NapCat;

public static class OneBotMessageParser
{
    public static ParsedOneBotMessage ParseGroupMessage(JsonElement source, string expectedAccountId, string? expectedGroupId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAccountId);
        var messageType = OneBotJson.String(source, "message_type");
        if (messageType == "private") throw new OneBotProtocolException("private-message");
        if (messageType != "group") throw new OneBotProtocolException("unsupported-message-type");
        var selfId = OneBotJson.OptionalId(source, "self_id");
        if (selfId is not null && selfId != expectedAccountId) throw new OneBotProtocolException("account-mismatch");
        var groupId = OneBotJson.Id(source, "group_id");
        if (expectedGroupId is not null && groupId != expectedGroupId) throw new OneBotProtocolException("group-mismatch");
        var key = new MessageKey(expectedAccountId, groupId, OneBotJson.Id(source, "message_id"));
        var sender = OneBotJson.Id(source, "user_id");
        var time = OneBotJson.Property(source, "time");
        if (time.ValueKind != JsonValueKind.Number || !time.TryGetInt64(out var unixSeconds))
            throw new OneBotProtocolException("invalid-time");
        DateTimeOffset sentAt;
        try { sentAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch (ArgumentOutOfRangeException) { throw new OneBotProtocolException("invalid-time"); }

        var content = OneBotJson.Property(source, "message");
        if (content.ValueKind != JsonValueKind.Array) throw new OneBotProtocolException("array-format-required");
        var segments = new List<MessageSegment>();
        var protocolSegments = new List<OneBotSegment>();
        foreach (var item in content.EnumerateArray())
        {
            var type = OneBotJson.String(item, "type");
            var data = OneBotJson.Property(item, "data");
            if (data.ValueKind != JsonValueKind.Object) throw new OneBotProtocolException("invalid-segment");
            protocolSegments.Add(new(type, data.Clone()));
            switch (type)
            {
                case "text":
                    segments.Add(new(SegmentKind.Text, OneBotJson.String(data, "text")));
                    break;
                case "image":
                    segments.Add(new(SegmentKind.Image, "", AssetId: OneBotJson.String(data, "file")));
                    break;
                case "reply":
                    var id = OneBotJson.OptionalId(data, "id");
                    segments.Add(new(SegmentKind.Quote, "", id is null ? null : new(expectedAccountId, groupId, id)));
                    break;
            }
        }
        return new(new(key, sender, sentAt, segments), OneBotJson.OptionalId(source, "message_seq"),
            OneBotJson.OptionalId(source, "real_seq"), protocolSegments);
    }
}
