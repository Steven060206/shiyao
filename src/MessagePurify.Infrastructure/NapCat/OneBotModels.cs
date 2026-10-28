using System.Net;
using System.Text.Json;
using MessagePurify.Core;

namespace MessagePurify.Infrastructure.NapCat;

public sealed record OneBotStatus(bool Online, bool Good);
public sealed record OneBotLogin(string AccountId, string Nickname);
public sealed record OneBotVersion(string AppName, string AppVersion, string ProtocolVersion);
public sealed record OneBotGroup(string GroupId, string GroupName);
public sealed record OneBotImage(string FilePath);
public sealed record OneBotSegment(string Type, JsonElement Data);
public sealed record ParsedOneBotMessage(SchoolMessage Message, string? MessageSequence,
    string? RealSequence, IReadOnlyList<OneBotSegment> ProtocolSegments);

public sealed class OneBotProtocolException(string code) : Exception($"OneBot protocol: {code}")
{
    public string Code { get; } = code;
}

public sealed class OneBotApiException(string action, int retcode) : Exception($"OneBot API {action}: retcode {retcode}")
{
    public string Action { get; } = action;
    public int Retcode { get; } = retcode;
}

public sealed class OneBotRequestTimeoutException(string action) : TimeoutException($"OneBot API {action}: request timeout")
{
    public string Action { get; } = action;
}

public static class OneBotEndpoint
{
    public static void Validate(Uri uri)
    {
        var loopback = uri.IsAbsoluteUri && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
        if (!loopback || uri.Scheme is not ("ws" or "wss") || uri.AbsolutePath != "/"
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new ArgumentException("需要本机 loopback WebSocket 根地址，凭据只通过环境变量提供。");
    }
}

internal static class OneBotJson
{
    public static JsonElement Property(JsonElement source, string name)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(name, out var value))
            throw new OneBotProtocolException("missing-field");
        return value;
    }

    public static string String(JsonElement source, string name)
    {
        var value = Property(source, name);
        if (value.ValueKind != JsonValueKind.String) throw new OneBotProtocolException("invalid-field");
        return value.GetString()!;
    }

    public static string Id(JsonElement source, string name)
    {
        var value = Property(source, name);
        if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
        if (value.ValueKind == JsonValueKind.Number)
        {
            var raw = value.GetRawText();
            var digits = raw.AsSpan();
            if (digits.Length > 0 && digits[0] == '-') digits = digits[1..];
            if (digits.Length > 0 && digits.IndexOfAnyExceptInRange('0', '9') < 0) return raw;
        }
        throw new OneBotProtocolException("invalid-id");
    }

    public static string? OptionalId(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? Id(source, name) : null;

    public static bool Boolean(JsonElement source, string name)
    {
        var value = Property(source, name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new OneBotProtocolException("invalid-field");
        return value.GetBoolean();
    }

    public static IReadOnlyList<JsonElement> Array(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) throw new OneBotProtocolException("invalid-array");
        return value.EnumerateArray().Select(item => item.Clone()).ToArray();
    }
}
