using System.Globalization;
using System.Text.Json;

namespace MessagePurify.Windows.App;

public sealed record AppBridgeRequest(string Id, string Operation, JsonElement Parameters);

public static class AppBridgeProtocol
{
    public const string Host = "app.messagepurify.local";
    public const string PageUrl = "https://" + Host + "/index.html";
    private static readonly HashSet<string> Operations = new(StringComparer.Ordinal)
    {
        "snapshot", "sync-now", "complete", "undo-complete", "archive", "restore", "ignore", "undo-ignore",
        "reject", "accept-review", "accept-time", "edit-task", "create-task", "convert-notice", "confirm-group", "mark-read", "set-appearance", "set-autostart",
        "merge-tasks", "keep-separate", "split-task", "clean-cache", "export-backup", "inspect-backup",
        "restore-backup", "cancel-backup", "open-backup-location"
    };
    private static readonly HashSet<string> ParameterNames = new(StringComparer.Ordinal)
    {
        "accountId", "id", "operationId", "version", "date", "time", "timeKind",
        "undoOperationId", "targetTaskVersion", "course", "excluded", "theme", "reducedMotion", "enabled", "title", "priority",
        "mergeWithId", "mergeWithVersion", "mergeSelection", "newItemId", "state", "evidenceIndexes", "generation", "motion", "previewId", "preferenceTheme", "preferenceMotion", "desktopTransparency"
    };

    public static bool IsTrustedSource(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.AbsoluteUri == PageUrl;

    public static bool TryParse(string source, string json, out AppBridgeRequest? request)
    {
        request = null;
        if (!IsTrustedSource(source) || json.Length > 16384) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!UniqueObject(root) || root.EnumerateObject().Any(p => p.Name is not ("id" or "operation" or "parameters"))
                || !Text(root, "id", 128, out var id) || !Text(root, "operation", 32, out var operation)
                || !Operations.Contains(operation) || !root.TryGetProperty("parameters", out var parameters)
                || !UniqueObject(parameters) || parameters.EnumerateObject().Any(p => !ParameterNames.Contains(p.Name))) return false;
            if (operation == "sync-now")
            {
                if (parameters.EnumerateObject().Any()) return false;
                request = new(id, operation, parameters.Clone());
                return true;
            }
            if (operation == "set-appearance")
            {
                var hasPreferences = parameters.TryGetProperty("preferenceTheme", out _);
                var hasTransparency = parameters.TryGetProperty("desktopTransparency", out var transparency);
                if (hasTransparency && !ValidTransparency(transparency)) return false;
                if (parameters.EnumerateObject().Count() != (hasPreferences ? 4 : 2) + (hasTransparency ? 1 : 0) || !Text(parameters, "theme", 8, out var theme)
                    || theme is not ("light" or "dark") || !parameters.TryGetProperty("reducedMotion", out var motion)
                    || motion.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                if (hasPreferences && (!Text(parameters, "preferenceTheme", 8, out var preferenceTheme) || preferenceTheme is not ("system" or "light" or "dark")
                    || !Text(parameters, "preferenceMotion", 8, out var preferenceMotion) || preferenceMotion is not ("system" or "reduce"))) return false;
                if (parameters.EnumerateObject().Any(p => p.Name is not ("theme" or "reducedMotion" or "preferenceTheme" or "preferenceMotion" or "desktopTransparency"))) return false;
                request = new(id, operation, parameters.Clone());
                return true;
            }
            if (operation == "set-autostart")
            {
                if (parameters.EnumerateObject().Count() != 1 || !parameters.TryGetProperty("enabled", out var enabled)
                    || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                request = new(id, operation, parameters.Clone());
                return true;
            }
            if (operation is "export-backup" or "inspect-backup" or "restore-backup" or "cancel-backup" or "open-backup-location")
            {
                var names = operation switch
                {
                    "export-backup" => new[] { "accountId", "generation", "theme", "motion" },
                    "inspect-backup" => new[] { "accountId", "generation" },
                    "restore-backup" => new[] { "accountId", "generation", "previewId", "theme", "motion" },
                    "cancel-backup" => new[] { "accountId", "previewId" },
                    _ => new[] { "accountId" }
                };
                var hasTransparency = parameters.TryGetProperty("desktopTransparency", out var transparency);
                if (hasTransparency && (operation is not ("export-backup" or "restore-backup") || !ValidTransparency(transparency))) return false;
                if (parameters.EnumerateObject().Count() != names.Length + (hasTransparency ? 1 : 0)
                    || parameters.EnumerateObject().Any(p => !names.Contains(p.Name) && p.Name != "desktopTransparency")
                    || !Text(parameters, "accountId", 64, out var backupAccount) || backupAccount.Any(c => c is < '0' or > '9')) return false;
                if (names.Contains("generation") && (!parameters.TryGetProperty("generation", out var generation)
                    || !generation.TryGetInt64(out var generationNumber) || generationNumber < 0)) return false;
                if (names.Contains("previewId") && !Text(parameters, "previewId", 128, out _)) return false;
                if (names.Contains("theme") && (!Text(parameters, "theme", 8, out var backupTheme) || backupTheme is not ("system" or "light" or "dark")
                    || !Text(parameters, "motion", 8, out var backupMotion) || backupMotion is not ("system" or "reduce"))) return false;
                request = new(id, operation, parameters.Clone());
                return true;
            }
            if (parameters.EnumerateObject().Any(p => p.Name is "motion" or "previewId" or "preferenceTheme" or "preferenceMotion" or "desktopTransparency")) return false;
            if (parameters.TryGetProperty("generation", out var commandGeneration)
                && (!commandGeneration.TryGetInt64(out var generationValue) || generationValue < 0)) return false;
            if (parameters.EnumerateObject().Any(p => p.Name is "theme" or "reducedMotion")) return false;
            if (operation != "snapshot")
            {
                if (!Text(parameters, "accountId", 64, out var account) || account.Any(c => c is < '0' or > '9')
                    || !Text(parameters, "id", 1024, out _) || !Text(parameters, "operationId", 128, out _)
                    || !parameters.TryGetProperty("version", out var version) || !version.TryGetInt64(out var number)
                    || number < (operation is "confirm-group" or "create-task" or "clean-cache" ? 0 : 1)
                    || operation is "create-task" or "clean-cache" && number != 0) return false;
            }
            if (operation == "clean-cache" && (parameters.EnumerateObject().Count() != (parameters.TryGetProperty("generation", out _) ? 5 : 4)
                || parameters.GetProperty("id").GetString() != "cache")) return false;
            if (operation is "merge-tasks" or "keep-separate")
            {
                if (!Text(parameters, "mergeWithId", 1024, out var otherId)
                    || otherId == parameters.GetProperty("id").GetString()
                    || !parameters.TryGetProperty("mergeWithVersion", out var otherVersion)
                    || !otherVersion.TryGetInt64(out var otherNumber) || otherNumber < 1) return false;
                if (operation == "merge-tasks")
                {
                    if (!parameters.TryGetProperty("priority", out var chosenPriority)
                        || !chosenPriority.TryGetInt32(out var chosenNumber) || chosenNumber is < 0 or > 3) return false;
                    if (!parameters.TryGetProperty("mergeSelection", out var selection) || !UniqueObject(selection)
                        || selection.EnumerateObject().Count() != 5) return false;
                    foreach (var name in new[] { "titleTaskId", "courseTaskId", "timeTaskId", "priorityTaskId", "stateTaskId" })
                        if (!Text(selection, name, 1024, out var selectedId)
                            || selectedId != otherId && selectedId != parameters.GetProperty("id").GetString()) return false;
                }
                else if (parameters.TryGetProperty("mergeSelection", out _)) return false;
                if (parameters.EnumerateObject().Any(p => p.Name is "newItemId" or "state" or "evidenceIndexes")) return false;
            }
            else if (parameters.EnumerateObject().Any(p => p.Name is "mergeWithId" or "mergeWithVersion" or "mergeSelection")) return false;
            if (operation == "split-task")
            {
                if (!Text(parameters, "newItemId", 1024, out var childId) || childId == parameters.GetProperty("id").GetString()
                    || !Text(parameters, "state", 16, out var state) || state is not ("active" or "completed" or "archived" or "cancelled")
                    || !parameters.TryGetProperty("evidenceIndexes", out var indexes) || indexes.ValueKind != JsonValueKind.Array
                    || indexes.GetArrayLength() > 1024) return false;
                var seen = new HashSet<int>();
                foreach (var index in indexes.EnumerateArray())
                    if (!index.TryGetInt32(out var indexNumber) || indexNumber < 0 || !seen.Add(indexNumber)) return false;
            }
            else if (parameters.EnumerateObject().Any(p => p.Name is "newItemId" or "state" or "evidenceIndexes")) return false;
            if (operation is "edit-task" or "create-task" or "split-task")
            {
                if (!Text(parameters, "title", 256, out var title) || title.Any(char.IsControl)
                    || operation != "split-task" && !Text(parameters, "date", 10, out _)
                    || !Text(parameters, "timeKind", 8, out _)
                    || !parameters.TryGetProperty("course", out var course)
                    || course.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                    || course.ValueKind == JsonValueKind.String && course.GetString()!.Any(char.IsControl)) return false;
                if (operation == "create-task" && (!Text(parameters, "course", 256, out _)
                    || parameters.GetProperty("timeKind").GetString() is not ("deadline" or "start"))) return false;
                var hasPriority = parameters.TryGetProperty("priority", out var priority);
                if (operation == "edit-task" && !hasPriority) return false;
                if (hasPriority && (operation == "edit-task" || priority.ValueKind != JsonValueKind.Null)
                    && (!priority.TryGetInt32(out var priorityNumber) || priorityNumber is < 0 or > 3)) return false;
            }
            else if (parameters.EnumerateObject().Any(p => p.Name == "title" || p.Name == "priority" && operation != "merge-tasks")) return false;
            foreach (var property in parameters.EnumerateObject())
            {
                if (property.Name is "date" or "time" or "timeKind" or "undoOperationId" or "course")
                {
                    if (property.Value.ValueKind == JsonValueKind.Null) continue;
                    if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString()!.Length > 256) return false;
                    var value = property.Value.GetString();
                    if (!string.IsNullOrEmpty(value) && property.Name == "date" && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
                    if (!string.IsNullOrEmpty(value) && property.Name == "time" && !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
                    if (!string.IsNullOrEmpty(value) && property.Name == "timeKind" && value is not ("deadline" or "start")) return false;
                }
                if (property.Name == "targetTaskVersion" && property.Value.ValueKind != JsonValueKind.Null
                    && (!property.Value.TryGetInt64(out var targetVersion) || targetVersion < 1)) return false;
                if (property.Name == "excluded" && property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            }
            request = new(id, operation, parameters.Clone());
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        { return false; }
    }

    private static bool ValidTransparency(JsonElement element) => element.ValueKind == JsonValueKind.Null
        || element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && value is >= 0 and <= 100;

    private static bool UniqueObject(JsonElement element) => element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == element.EnumerateObject().Count();

    private static bool Text(JsonElement root, string name, int limit, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var candidate) || candidate.ValueKind != JsonValueKind.String) return false;
        value = candidate.GetString()!;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
    }
}
