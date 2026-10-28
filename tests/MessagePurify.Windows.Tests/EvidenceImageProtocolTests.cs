using System.Text.Json;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class EvidenceImageProtocolTests
{
    private const string ImageId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ImageUrl = "https://" + AppBridgeProtocol.Host + "/evidence/12345/4/" + ImageId;

    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("app.js", "application/javascript; charset=utf-8")]
    [InlineData("image-evidence.css", "text/css; charset=utf-8")]
    [InlineData("assets/shiyao.ico", "image/x-icon")]
    public void Static_ui_uses_the_same_controlled_origin(string relative, string type)
    {
        Assert.True(EvidenceImageProtocol.TryReadStatic("https://" + AppBridgeProtocol.Host + "/" + relative, "GET", out var path, out var contentType));
        Assert.Equal(relative, path); Assert.Equal(type, contentType);
    }

    [Theory]
    [InlineData("../index.html")]
    [InlineData("assets/../../index.html")]
    [InlineData("%2e%2e/index.html")]
    [InlineData("app.js?path=other")]
    [InlineData("app.js#fragment")]
    [InlineData("C:/app.js")]
    [InlineData("assets\\app.js")]
    [InlineData("evidence/12345/4/image.png")]
    [InlineData("accounts/settings.json")]
    public void Static_ui_cannot_bypass_evidence_or_resolve_arbitrary_files(string path) =>
        Assert.False(EvidenceImageProtocol.TryReadStatic("https://" + AppBridgeProtocol.Host + "/" + path, "GET", out _, out _));

    [Fact]
    public void Original_is_addressed_only_by_current_account_generation_and_image_id()
    {
        Assert.True(EvidenceImageProtocol.TryRead(ImageUrl, "GET", "12345", 4, out var id));
        Assert.Equal(ImageId, id);
        Assert.False(EvidenceImageProtocol.TryRead(ImageUrl, "GET", "67890", 4, out _));
        Assert.False(EvidenceImageProtocol.TryRead(ImageUrl, "GET", "12345", 5, out _));
        Assert.False(EvidenceImageProtocol.TryRead(ImageUrl, "POST", "12345", 4, out _));
    }

    [Theory]
    [InlineData("http://app.messagepurify.local/evidence/12345/4/")]
    [InlineData("https://external.test/evidence/12345/4/")]
    [InlineData("https://app.messagepurify.local:444/evidence/12345/4/")]
    [InlineData("https://user@app.messagepurify.local/evidence/12345/4/")]
    [InlineData("file:///C:/accounts/12345/images/")]
    [InlineData("https://app.messagepurify.local/evidence/12345/04/")]
    [InlineData("https://app.messagepurify.local/evidence/12345/4/../4/")]
    [InlineData("https://app.messagepurify.local/evidence/%31%32%33%34%35/4/")]
    public void Rejects_other_authorities_and_noncanonical_paths(string prefix) =>
        Assert.False(EvidenceImageProtocol.TryRead(prefix + ImageId, "GET", "12345", 4, out _));

    [Theory]
    [InlineData("?path=C:/other.png")]
    [InlineData("#fragment")]
    [InlineData("/../../messages.sqlite3")]
    [InlineData(".png")]
    [InlineData("%2f")]
    public void Rejects_paths_queries_and_suffixes(string suffix) =>
        Assert.False(EvidenceImageProtocol.TryRead(ImageUrl + suffix, "GET", "12345", 4, out _));

    [Fact]
    public void Rejects_nonimage_content_types()
    {
        Assert.Equal("image/png", EvidenceImageProtocol.ContentType("C:/controlled/image.PNG"));
        Assert.Null(EvidenceImageProtocol.ContentType("C:/controlled/image.svg"));
        Assert.Null(EvidenceImageProtocol.ContentType("C:/controlled/messages.sqlite3"));
    }

    [Fact]
    public void Retry_requires_generation_fixed_version_and_only_image_context()
    {
        var fields = new Dictionary<string, object?> { ["accountId"] = "12345", ["id"] = ImageId,
            ["operationId"] = "retry-1", ["version"] = 0, ["generation"] = 4 };
        Assert.True(Parse(fields));
        foreach (var field in fields.Keys.ToArray())
        {
            var copy = new Dictionary<string, object?>(fields); copy.Remove(field); Assert.False(Parse(copy));
        }
        foreach (var (name, value) in new (string, object?)[] { ("version", 1), ("generation", -1), ("generation", "4"),
            ("id", ImageId.ToUpperInvariant()), ("id", "../image"), ("accountId", "other"), ("date", null), ("path", "C:/other.png") })
        {
            var copy = new Dictionary<string, object?>(fields) { [name] = value }; Assert.False(Parse(copy));
        }
    }

    private static bool Parse(Dictionary<string, object?> fields) => AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl,
        JsonSerializer.Serialize(new { id = "request", operation = "retry-image", parameters = fields }), out _);
}
