using System.IO;
using System.Text.Json;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.Windows.App;

internal static class NapCatConnectionConfiguration
{
    public static string? ReadAccessToken(string? environmentToken, string configurationDirectory)
    {
        if (!string.IsNullOrEmpty(environmentToken)) return environmentToken;
        if (!Directory.Exists(configurationDirectory)) return null;
        var tokens = new List<string?>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(configurationDirectory, "onebot11_*.json"))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("network", out var network) || network.ValueKind != JsonValueKind.Object
                    || !network.TryGetProperty("websocketServers", out var servers)) continue;
                if (servers.ValueKind != JsonValueKind.Array) throw new OneBotProtocolException("napcat-configuration-invalid");
                foreach (var server in servers.EnumerateArray())
                {
                    if (server.ValueKind != JsonValueKind.Object
                        || !server.TryGetProperty("enable", out var enabled) || enabled.ValueKind != JsonValueKind.True
                        || !server.TryGetProperty("port", out var port) || port.ValueKind != JsonValueKind.Number
                        || !port.TryGetInt32(out var number) || number != 3001
                        || !server.TryGetProperty("host", out var host) || host.ValueKind != JsonValueKind.String
                        || !new[] { "127.0.0.1", "localhost", "::1" }.Contains(host.GetString(), StringComparer.OrdinalIgnoreCase)) continue;
                    if (server.TryGetProperty("token", out var token) && token.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                        throw new OneBotProtocolException("napcat-configuration-invalid");
                    tokens.Add(token.ValueKind == JsonValueKind.String ? token.GetString() : null);
                }
            }
        }
        catch (JsonException) { throw new OneBotProtocolException("napcat-configuration-invalid"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new OneBotProtocolException("napcat-configuration-unavailable"); }
        if (tokens.Count > 1) throw new OneBotProtocolException("napcat-configuration-ambiguous");
        return tokens.SingleOrDefault();
    }
}
