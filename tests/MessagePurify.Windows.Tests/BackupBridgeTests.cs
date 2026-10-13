using System.Text.Json;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class BackupBridgeTests
{
    [Fact]
    public void Appearance_can_persist_original_preference_separately_from_resolved_theme()
    {
        var fields = new Dictionary<string, object?> { ["theme"] = "dark", ["reducedMotion"] = false, ["preferenceTheme"] = "system", ["preferenceMotion"] = "system" };
        Assert.True(Parse("set-appearance", fields));
        fields.Remove("preferenceMotion"); Assert.False(Parse("set-appearance", fields));
        fields["preferenceMotion"] = "fast"; Assert.False(Parse("set-appearance", fields));
    }
    [Theory]
    [InlineData("export-backup")]
    [InlineData("inspect-backup")]
    [InlineData("restore-backup")]
    [InlineData("cancel-backup")]
    [InlineData("open-backup-location")]
    public void Accepts_host_selected_backup_operation(string operation)
    {
        Assert.True(Parse(operation, Fields(operation)));
    }

    [Theory]
    [InlineData("path", "C:/other.zip")]
    [InlineData("accountId", "other")]
    [InlineData("accountId", "")]
    [InlineData("theme", "blue")]
    [InlineData("motion", "fast")]
    [InlineData("generation", -1)]
    [InlineData("generation", "0")]
    public void Rejects_invalid_export_context(string field, object value)
    {
        var fields = Fields("export-backup"); fields[field] = value;
        Assert.False(Parse("export-backup", fields));
    }

    [Theory]
    [InlineData("accountId")]
    [InlineData("generation")]
    [InlineData("theme")]
    [InlineData("motion")]
    [InlineData("previewId")]
    public void Restore_requires_complete_confirmation_context(string field)
    {
        var fields = Fields("restore-backup"); fields.Remove(field);
        Assert.False(Parse("restore-backup", fields));
    }

    [Theory]
    [InlineData("export-backup", "id")]
    [InlineData("inspect-backup", "theme")]
    [InlineData("cancel-backup", "generation")]
    [InlineData("open-backup-location", "previewId")]
    [InlineData("complete", "previewId")]
    public void Rejects_irrelevant_even_null_parameters(string operation, string field)
    {
        var fields = operation == "complete" ? new Dictionary<string, object?> { ["accountId"] = "123", ["id"] = "task", ["operationId"] = "op", ["version"] = 1 } : Fields(operation);
        fields[field] = null;
        Assert.False(Parse(operation, fields));
    }

    [Fact]
    public void Ordinary_command_carries_nonnegative_generation()
    {
        var fields = new Dictionary<string, object?> { ["accountId"] = "123", ["id"] = "task", ["operationId"] = "op", ["version"] = 1, ["generation"] = 4 };
        Assert.True(Parse("complete", fields));
        fields["generation"] = -1;
        Assert.False(Parse("complete", fields));
    }

    private static Dictionary<string, object?> Fields(string operation)
    {
        var fields = new Dictionary<string, object?> { ["accountId"] = "123" };
        if (operation is "export-backup" or "inspect-backup" or "restore-backup") fields["generation"] = 0;
        if (operation is "export-backup" or "restore-backup") { fields["theme"] = "system"; fields["motion"] = "reduce"; }
        if (operation is "restore-backup" or "cancel-backup") fields["previewId"] = "preview-1";
        return fields;
    }

    private static bool Parse(string operation, Dictionary<string, object?> fields) => AppBridgeProtocol.TryParse(AppBridgeProtocol.PageUrl,
        JsonSerializer.Serialize(new { id = "request", operation, parameters = fields }), out _);
}
