using System.Text.Json;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class ManualTaskBridgeTests
{
    [Theory]
    [InlineData(null, null, "deadline")]
    [InlineData("", 0, "deadline")]
    [InlineData("18:30", 3, "start")]
    public void Accepts_manual_task_with_date_and_optional_time_and_priority(string? time, int? priority, string kind)
    {
        var fields = Fields();
        fields["time"] = time;
        fields["priority"] = priority;
        fields["timeKind"] = kind;
        Assert.True(Parse(fields, out var request));
        Assert.Equal("create-task", request!.Operation);
        Assert.Equal(0, request.Parameters.GetProperty("version").GetInt64());
    }

    [Theory]
    [InlineData("title")]
    [InlineData("course")]
    [InlineData("date")]
    [InlineData("timeKind")]
    public void Rejects_missing_required_creation_fields(string field)
    {
        var fields = Fields();
        fields.Remove(field);
        Assert.False(Parse(fields, out _));
    }

    [Theory]
    [InlineData("title", "\"   \"")]
    [InlineData("title", "null")]
    [InlineData("course", "\"\"")]
    [InlineData("course", "null")]
    [InlineData("course", "\"bad\\u0000value\"")]
    [InlineData("date", "\"\"")]
    [InlineData("date", "\"2026-02-30\"")]
    [InlineData("date", "null")]
    [InlineData("time", "\"25:00\"")]
    [InlineData("time", "1")]
    [InlineData("timeKind", "\"\"")]
    [InlineData("timeKind", "\"unknown\"")]
    [InlineData("priority", "4")]
    [InlineData("priority", "-1")]
    [InlineData("priority", "1.5")]
    [InlineData("priority", "\"1\"")]
    [InlineData("version", "1")]
    [InlineData("version", "-1")]
    public void Rejects_invalid_creation_fields(string field, string value)
    {
        var fields = Fields();
        using var json = JsonDocument.Parse(value);
        fields[field] = json.RootElement.Clone();
        Assert.False(Parse(fields, out _));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("course")]
    public void Rejects_oversized_creation_text(string field)
    {
        var fields = Fields();
        fields[field] = new string('字', 257);
        Assert.False(Parse(fields, out _));
    }

    [Fact]
    public void Accepts_omitted_optional_time_and_priority()
    {
        var fields = Fields();
        fields.Remove("time");
        fields.Remove("priority");
        Assert.True(Parse(fields, out _));
    }

    [Fact]
    public void Creation_cannot_override_source_or_supply_message_evidence()
    {
        var fields = Fields();
        fields["source"] = "qq";
        Assert.False(Parse(fields, out _));
        fields.Remove("source");
        fields["evidence"] = new[] { "message-1" };
        Assert.False(Parse(fields, out _));
    }

    private static bool Parse(Dictionary<string, object?> fields, out AppBridgeRequest? request) =>
        AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl,
            JsonSerializer.Serialize(new { id = "request-1", operation = "create-task", parameters = fields }), out request);

    private static Dictionary<string, object?> Fields() => new()
    {
        ["accountId"] = "123", ["id"] = "manual-1", ["operationId"] = "op-1", ["version"] = 0,
        ["title"] = "补录实验报告", ["course"] = "数学实验", ["date"] = "2026-10-15",
        ["time"] = null, ["timeKind"] = "deadline", ["priority"] = null
    };
}
