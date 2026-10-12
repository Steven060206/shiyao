using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class AppBridgeProtocolTests
{
    [Theory]
    [InlineData("light", false)]
    [InlineData("dark", true)]
    public void Parses_desktop_appearance_without_account(string theme, bool reducedMotion)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { id = "appearance", operation = "set-appearance", parameters = new { theme, reducedMotion } });
        Assert.True(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, json, out var request));
        Assert.Equal("set-appearance", request!.Operation);
        Assert.Equal(theme, request.Parameters.GetProperty("theme").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Parses_startup_preference_without_account(bool enabled)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { id = "startup", operation = "set-autostart", parameters = new { enabled } });
        Assert.True(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, json, out var request));
        Assert.Equal("set-autostart", request!.Operation);
        Assert.Equal(enabled, request.Parameters.GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData("{\"theme\":\"system\",\"reducedMotion\":false}")]
    [InlineData("{\"theme\":\"dark\"}")]
    [InlineData("{\"theme\":\"dark\",\"reducedMotion\":\"true\"}")]
    [InlineData("{\"theme\":\"dark\",\"reducedMotion\":false,\"accountId\":\"1\"}")]
    [InlineData("{\"theme\":\"dark\",\"theme\":\"light\",\"reducedMotion\":false}")]
    public void Rejects_invalid_appearance(string parameters) => Assert.False(AppBridgeProtocol.TryParse(
        AppBridgeProtocol.PageUrl, "{\"id\":\"appearance\",\"operation\":\"set-appearance\",\"parameters\":" + parameters + "}", out _));

    [Fact]
    public void Rejects_untrusted_appearance_and_appearance_fields_on_task_commands()
    {
        const string json = """{"id":"appearance","operation":"set-appearance","parameters":{"theme":"dark","reducedMotion":false}}""";
        Assert.False(AppBridgeProtocol.TryParse("https://example.com/index.html", json, out _));
        Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, Command.Replace("\"version\":2", "\"version\":2,\"theme\":\"dark\""), out _));
    }
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
    public void Accepts_notice_conversion_with_date_and_optional_time()
    {
        const string json = """{"id":"n","operation":"convert-notice","parameters":{"accountId":"123","id":"notice-1","operationId":"o","version":1,"date":"2026-10-14","time":"18:00","timeKind":"deadline"}}""";
        Assert.True(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, json, out var request));
        Assert.Equal("convert-notice", request!.Operation);
        Assert.Equal("2026-10-14", request.Parameters.GetProperty("date").GetString());
    }

    [Fact]
    public void Rejects_oversized_payload_before_parsing() => Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, new string('x', 16385), out _));

    [Theory]
    [InlineData("\"reminderMode\":\"default\",\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"off\",\"reminderVersion\":2")]
    [InlineData("\"reminderMode\":\"hours\",\"reminderHours\":1.5,\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"calendar\",\"reminderDays\":0,\"reminderClock\":\"20:30\",\"reminderVersion\":1")]
    public void Rejects_retired_system_reminder_preferences(string fields) =>
        Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, ReminderRequest(fields), out _));

    [Theory]
    [InlineData("\"reminderMode\":\"default\"")]
    [InlineData("\"reminderMode\":\"default\",\"reminderVersion\":0")]
    [InlineData("\"reminderMode\":\"default\",\"reminderVersion\":1.5")]
    [InlineData("\"reminderMode\":\"daily\",\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"hours\",\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"hours\",\"reminderHours\":0,\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"hours\",\"reminderHours\":-1,\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"hours\",\"reminderHours\":\"2\",\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"hours\",\"reminderHours\":1e999,\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"calendar\",\"reminderDays\":-1,\"reminderClock\":\"12:00\",\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"calendar\",\"reminderDays\":1.5,\"reminderClock\":\"12:00\",\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"calendar\",\"reminderDays\":1,\"reminderClock\":\"25:00\",\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"calendar\",\"reminderDays\":1,\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"off\",\"reminderHours\":2,\"reminderVersion\":1")]
    [InlineData("\"reminderMode\":\"hours\",\"reminderHours\":2,\"reminderDays\":1,\"reminderVersion\":1")]
    public void Rejects_incomplete_or_ambiguous_reminder_preferences(string fields) =>
        Assert.False(AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl, ReminderRequest(fields), out _));

    private static string ReminderRequest(string fields) =>
        "{\"id\":\"r\",\"operation\":\"set-reminder\",\"parameters\":{\"accountId\":\"123\",\"id\":\"t\",\"operationId\":\"o\",\"version\":1," + fields + "}}";
}
