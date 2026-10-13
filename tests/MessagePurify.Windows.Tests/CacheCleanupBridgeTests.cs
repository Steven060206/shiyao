using System.Text.Json;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class CacheCleanupBridgeTests
{
    [Fact]
    public void Accepts_account_bound_cache_cleanup_with_version_zero()
    {
        Assert.True(Parse(Fields(), out var request));
        Assert.Equal("clean-cache", request!.Operation);
        Assert.Equal("cache", request.Parameters.GetProperty("id").GetString());
        Assert.Equal(0, request.Parameters.GetProperty("version").GetInt64());
        Assert.Equal(4, request.Parameters.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("accountId")]
    [InlineData("id")]
    [InlineData("operationId")]
    [InlineData("version")]
    public void Rejects_missing_cleanup_context(string field)
    {
        var fields = Fields(); fields.Remove(field);
        Assert.False(Parse(fields, out _));
    }

    [Theory]
    [InlineData("accountId", "\"\"")]
    [InlineData("accountId", "\"abc\"")]
    [InlineData("accountId", "123")]
    [InlineData("id", "\"task-1\"")]
    [InlineData("id", "\"Cache\"")]
    [InlineData("id", "null")]
    [InlineData("operationId", "\" \"")]
    [InlineData("operationId", "null")]
    [InlineData("version", "1")]
    [InlineData("version", "-1")]
    [InlineData("version", "0.5")]
    [InlineData("version", "\"0\"")]
    [InlineData("version", "null")]
    public void Rejects_invalid_cleanup_context(string field, string raw)
    {
        var fields = Fields(); fields[field] = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.False(Parse(fields, out _));
    }

    [Theory]
    [InlineData("date")]
    [InlineData("time")]
    [InlineData("timeKind")]
    [InlineData("course")]
    [InlineData("excluded")]
    [InlineData("enabled")]
    [InlineData("title")]
    [InlineData("priority")]
    [InlineData("undoOperationId")]
    [InlineData("targetTaskVersion")]
    [InlineData("mergeWithId")]
    [InlineData("mergeWithVersion")]
    [InlineData("mergeSelection")]
    [InlineData("newItemId")]
    [InlineData("state")]
    [InlineData("evidenceIndexes")]
    [InlineData("theme")]
    [InlineData("reducedMotion")]
    [InlineData("cutoff")]
    public void Rejects_extra_business_fields_even_when_null(string field)
    {
        var fields = Fields(); fields[field] = null;
        Assert.False(Parse(fields, out _));
    }

    [Fact]
    public void Rejects_duplicate_cleanup_fields()
    {
        const string json = "{\"id\":\"request\",\"operation\":\"clean-cache\",\"parameters\":{\"accountId\":\"123\",\"id\":\"cache\",\"operationId\":\"op\",\"version\":0,\"version\":0}}";
        Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, json, out _));
    }

    private static Dictionary<string, object?> Fields() => new()
    { ["accountId"] = "123", ["id"] = "cache", ["operationId"] = "cleanup-1", ["version"] = 0 };

    private static bool Parse(Dictionary<string, object?> fields, out AppBridgeRequest? request) =>
        AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl,
            JsonSerializer.Serialize(new { id = "request-1", operation = "clean-cache", parameters = fields }), out request);
}
