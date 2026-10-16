using System.Text.Json;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class NoticeReviewBridgeTests
{
    private const string Command = """{"id":"notice-review","operation":"accept-notice","parameters":{"accountId":"123","id":"review-1","operationId":"confirm-notice","version":1}}""";

    [Fact]
    public void Accepts_notice_review_without_requiring_a_date()
    {
        Assert.True(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, Command, out var request));
        Assert.Equal("accept-notice", request!.Operation);
        Assert.Equal(4, request.Parameters.EnumerateObject().Count());
        Assert.False(request.Parameters.TryGetProperty("date", out _));
    }

    [Fact]
    public void Accepts_notice_review_with_workspace_generation()
    {
        Assert.True(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl,
            Command.Replace("\"version\":1", "\"version\":1,\"generation\":2"), out var request));
        Assert.Equal(2, request!.Parameters.GetProperty("generation").GetInt64());
    }

    [Theory]
    [InlineData("date", "2026-10-20")]
    [InlineData("time", "13:00")]
    [InlineData("timeKind", "start")]
    [InlineData("course", "课程")]
    [InlineData("title", "通知")]
    [InlineData("undoOperationId", "previous-operation")]
    [InlineData("targetTaskVersion", 1)]
    public void Rejects_task_edit_fields_on_notice_confirmation(string field, object value)
    {
        var fields = Fields();
        fields[field] = value;
        Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, Request(fields), out _));
    }

    [Theory]
    [InlineData("accountId", "another-account")]
    [InlineData("id", "")]
    [InlineData("operationId", "")]
    [InlineData("version", 0)]
    [InlineData("version", -1)]
    [InlineData("version", 1.5)]
    [InlineData("generation", -1)]
    public void Rejects_invalid_notice_confirmation_identity_or_version(string field, object value)
    {
        var fields = Fields();
        fields[field] = value;
        Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, Request(fields), out _));
    }

    [Theory]
    [InlineData("accountId")]
    [InlineData("id")]
    [InlineData("operationId")]
    [InlineData("version")]
    public void Rejects_incomplete_notice_confirmation(string field)
    {
        var fields = Fields();
        fields.Remove(field);
        Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, Request(fields), out _));
    }

    [Fact]
    public void Rejects_notice_confirmation_from_an_untrusted_page() =>
        Assert.False(AppBridgeProtocol.TryParse("https://example.com/index.html", Command, out _));

    private static Dictionary<string, object> Fields() => new()
    {
        ["accountId"] = "123", ["id"] = "review-1", ["operationId"] = "confirm-notice", ["version"] = 1
    };

    private static string Request(Dictionary<string, object> parameters) =>
        JsonSerializer.Serialize(new { id = "notice-review", operation = "accept-notice", parameters });
}
