using System.Globalization;
using System.IO;

namespace MessagePurify.Windows.App;

public static class EvidenceImageProtocol
{
    public static bool TryReadStatic(string requestUri, string method, out string relativePath, out string contentType)
    {
        relativePath = ""; contentType = "";
        var prefix = $"https://{AppBridgeProtocol.Host}/";
        if (method != "GET" || !requestUri.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var path = requestUri[prefix.Length..];
        if (path.Length == 0 || path.Contains('%') || path.Contains('?') || path.Contains('#') || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or "..") || path.StartsWith("evidence/", StringComparison.Ordinal)) return false;
        var type = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8", ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8", ".svg" => "image/svg+xml", ".ico" => "image/x-icon",
            ".woff" => "font/woff", ".woff2" => "font/woff2", ".ttf" => "font/ttf",
            _ => ContentType(path)
        };
        if (type is null) return false;
        relativePath = path; contentType = type; return true;
    }

    public static bool TryRead(string requestUri, string method, string accountId, long generation, out string imageId)
    {
        imageId = "";
        if (method != "GET" || generation < 0 || string.IsNullOrEmpty(accountId) || accountId.Length > 64
            || accountId.Any(c => c is < '0' or > '9')
            || !Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.IdnHost != AppBridgeProtocol.Host || uri.Port != 443 || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        var prefix = $"https://{AppBridgeProtocol.Host}/evidence/{accountId}/{generation.ToString(CultureInfo.InvariantCulture)}/";
        // Check the original URI too: Uri canonicalization must not admit encoded or traversed paths.
        if (!requestUri.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var id = requestUri[prefix.Length..];
        if (id.Length != 64 || id.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) return false;
        imageId = id;
        return true;
    }

    public static string? ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tiff" => "image/tiff",
        ".webp" => "image/webp",
        _ => null
    };
}
