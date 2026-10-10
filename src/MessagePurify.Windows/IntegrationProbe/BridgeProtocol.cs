using System.Text.Json;

namespace MessagePurify.Windows.IntegrationProbe;

public sealed record BridgeReply(bool Success, string? Id, string? Value, string? Error);

public static class BridgeProtocol
{
    public const string Host = "probe.messagepurify.local";
    public const string PageUrl = "https://" + Host + "/index.html";

    public static bool IsTrustedSource(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IdnHost == Host && uri.Port == 443 &&
        uri.UserInfo.Length == 0 && uri.AbsolutePath == "/index.html";

    public static BridgeReply Handle(string source, string json, string expectedNonce)
    {
        BridgeReply Reject(string error, string? id = null) => new(false, id, null, error);
        if (!IsTrustedSource(source)) return Reject("source");
        if (json.Length > 4096) return Reject("size");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!UniqueObject(root) || !TryString(root, "id", out var id) || id.Length is 0 or > 64 ||
                !TryString(root, "operation", out var operation)) return Reject("parameters");
            if (operation != "echo") return Reject("operation", id);
            if (!root.TryGetProperty("parameters", out var parameters) || !UniqueObject(parameters) ||
                !TryString(parameters, "value", out var value) || value.Length > 1024 ||
                !TryString(parameters, "nonce", out var nonce)) return Reject("parameters", id);
            if (nonce != expectedNonce) return Reject("nonce", id);
            return new(true, id, value, null);
        }
        catch (JsonException) { return Reject("json"); }
    }

    private static bool UniqueObject(JsonElement element) => element.ValueKind == JsonValueKind.Object &&
        element.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() ==
        element.EnumerateObject().Count();

    private static bool TryString(JsonElement element, string property, out string value)
    {
        value = "";
        if (!element.TryGetProperty(property, out var candidate) || candidate.ValueKind != JsonValueKind.String) return false;
        value = candidate.GetString()!;
        return true;
    }
}
