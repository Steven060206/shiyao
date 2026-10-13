using System.Text.Json;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class TaskStructureBridgeTests
{
    private static Dictionary<string, object?> Merge() => new()
    {
        ["accountId"] = "123", ["id"] = "a", ["version"] = 1,
        ["operationId"] = "operation", ["mergeWithId"] = "b", ["mergeWithVersion"] = 2, ["priority"] = 2,
        ["mergeSelection"] = new Dictionary<string, object?>
        {
            ["titleTaskId"] = "a", ["courseTaskId"] = "b", ["timeTaskId"] = "b",
            ["priorityTaskId"] = "a", ["stateTaskId"] = "a"
        }
    };

    private static Dictionary<string, object?> Split() => new()
    {
        ["accountId"] = "123", ["id"] = "a", ["version"] = 1, ["operationId"] = "split",
        ["newItemId"] = "child", ["title"] = "独立作业", ["course"] = null,
        ["date"] = "2026-10-20", ["time"] = null, ["timeKind"] = "deadline",
        ["priority"] = null, ["state"] = "active", ["evidenceIndexes"] = new[] { 0, 2 }
    };

    private static bool Parse(string operation, Dictionary<string, object?> fields) =>
        AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl,
            JsonSerializer.Serialize(new { id = "request", operation, parameters = fields }), out _);

    [Fact]
    public void Accepts_merge_and_explicit_independent_decision()
    {
        Assert.True(Parse("merge-tasks", Merge()));
        var separate = Merge(); separate.Remove("mergeSelection"); separate.Remove("priority");
        Assert.True(Parse("keep-separate", separate));
    }

    [Theory]
    [InlineData("mergeWithId", "\"a\"")]
    [InlineData("mergeWithId", "null")]
    [InlineData("mergeWithVersion", "0")]
    [InlineData("mergeWithVersion", "\"1\"")]
    [InlineData("mergeSelection", "null")]
    [InlineData("version", "0")]
    [InlineData("priority", "null")]
    [InlineData("priority", "4")]
    public void Rejects_invalid_merge_participants(string key, string raw)
    {
        var fields = Merge(); fields[key] = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.False(Parse("merge-tasks", fields));
    }

    [Theory]
    [InlineData("titleTaskId")]
    [InlineData("courseTaskId")]
    [InlineData("timeTaskId")]
    [InlineData("priorityTaskId")]
    [InlineData("stateTaskId")]
    public void Every_merge_field_must_choose_one_of_the_two_tasks(string field)
    {
        var fields = Merge();
        var selection = (Dictionary<string, object?>)fields["mergeSelection"]!;
        selection[field] = "unrelated";
        Assert.False(Parse("merge-tasks", fields));
        selection.Remove(field);
        Assert.False(Parse("merge-tasks", fields));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("completed")]
    [InlineData("archived")]
    [InlineData("cancelled")]
    public void Accepts_split_with_explicit_state_and_optional_formal_date(string state)
    {
        var fields = Split(); fields["state"] = state;
        Assert.True(Parse("split-task", fields));
        fields["date"] = null;
        Assert.True(Parse("split-task", fields));
    }

    [Theory]
    [InlineData("newItemId", "\"a\"")]
    [InlineData("title", "\" \"")]
    [InlineData("course", "5")]
    [InlineData("date", "\"2026-02-30\"")]
    [InlineData("timeKind", "\"bad\"")]
    [InlineData("state", "\"pending\"")]
    [InlineData("priority", "4")]
    [InlineData("evidenceIndexes", "[-1]")]
    [InlineData("evidenceIndexes", "[0,0]")]
    [InlineData("evidenceIndexes", "[0.5]")]
    [InlineData("evidenceIndexes", "[\"0\"]")]
    [InlineData("evidenceIndexes", "null")]
    public void Rejects_invalid_split_fields(string key, string raw)
    {
        var fields = Split(); fields[key] = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.False(Parse("split-task", fields));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("create-task")]
    [InlineData("snapshot")]
    public void Structural_parameters_cannot_leak_into_other_operations(string operation)
    {
        var fields = new Dictionary<string, object?>
        { ["accountId"] = "123", ["id"] = "a", ["version"] = 1, ["operationId"] = "operation", ["mergeWithId"] = "b" };
        Assert.False(Parse(operation, fields));
    }
}
