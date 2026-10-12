using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class AppBridgeProtocolTests
{
    private const string Command = """{"id":"request-1","operation":"complete","parameters":{"accountId":"12345","id":"task-9007199254740993","operationId":"op-1","version":2}}""";

    [Fact]
    public void Parses_versioned_command_without_rounding_identifiers()
    {
        Assert.True(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, Command, out var request));
        Assert.Equal("request-1", request!.Id);
        Assert.Equal("complete", request.Operation);
        Assert.Equal("task-9007199254740993", request.Parameters.GetProperty("id").GetString());
        Assert.Equal(2, request.Parameters.GetProperty("version").GetInt64());
    }

    [Theory]
    [InlineData("https://example.com/index.html")]
    [InlineData("http://app.messagepurify.local/index.html")]
    [InlineData("https://app.messagepurify.local/other.html")]
    [InlineData("https://app.messagepurify.local/index.html?remote=1")]
    [InlineData("https://app.messagepurify.local/index.html#remote")]
    [InlineData("https://app.messagepurify.local:444/index.html")]
    public void Rejects_untrusted_sources(string source) => Assert.False(AppBridgeProtocol.TryParse(source, Command, out _));

    [Theory]
    [InlineData("{\"id\":\"1\",\"id\":\"2\",\"operation\":\"snapshot\",\"parameters\":{}}")]
    [InlineData("{\"id\":\"1\",\"operation\":\"snapshot\",\"parameters\":{\"accountId\":\"1\",\"accountId\":\"2\"}}")]
    [InlineData("{\"id\":\"1\",\"operation\":\"send_msg\",\"parameters\":{}}")]
    [InlineData("{\"id\":\"1\",\"operation\":\"complete\",\"parameters\":{\"accountId\":\"123\",\"id\":\"t\",\"operationId\":\"o\",\"version\":0}}")]
    [InlineData("{\"id\":\"1\",\"operation\":\"complete\",\"parameters\":{\"accountId\":123,\"id\":\"t\",\"operationId\":\"o\",\"version\":1}}")]
    [InlineData("{\"id\":\"1\",\"operation\":\"complete\",\"parameters\":{\"accountId\":\"123\",\"id\":\"t\",\"operationId\":\"o\",\"version\":1.5}}")]
    [InlineData("{\"id\":\"1\",\"operation\":\"complete\",\"parameters\":{\"accountId\":\"123\",\"id\":\"t\",\"operationId\":\"o\",\"version\":1,\"trustedSenders\":[\"123\"]}}")]
    [InlineData("{\"id\":\"1\",\"operation\":\"accept-review\",\"parameters\":{\"accountId\":\"123\",\"id\":\"r\",\"operationId\":\"o\",\"version\":1,\"date\":\"2026-02-30\"}}")]
    [InlineData("{\"id\":\"1\",\"operation\":\"accept-review\",\"parameters\":{\"accountId\":\"123\",\"id\":\"r\",\"operationId\":\"o\",\"version\":1,\"date\":\"2026-10-15\",\"time\":\"25:00\"}}")]
    [InlineData("[]")]
    [InlineData("invalid json")]
    public void Rejects_invalid_or_ambiguous_commands(string json) => Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, json, out _));

    [Fact]
    public void Accepts_empty_snapshot_and_initial_course_confirmation()
    {
        Assert.True(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, """{"id":"a","operation":"snapshot","parameters":{}}""", out _));
        Assert.True(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, """{"id":"b","operation":"confirm-group","parameters":{"accountId":"123","id":"456","operationId":"o","version":0,"course":"高等数学","excluded":false}}""", out _));
    }

    [Fact]
    public void Rejects_oversized_payload_before_parsing() => Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, new string('x', 16385), out _));
}
