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
        "snapshot", "complete", "undo-complete", "archive", "restore", "ignore", "undo-ignore",
        "reject", "accept-review", "accept-time", "confirm-group", "mark-read", "set-reminder"
    };
    private static readonly HashSet<string> ParameterNames = new(StringComparer.Ordinal)
    {
        "accountId", "id", "operationId", "version", "date", "time", "timeKind",
        "undoOperationId", "targetTaskVersion", "course", "excluded",
        "reminderMode", "reminderHours", "reminderDays", "reminderClock", "reminderVersion"
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
            if (operation != "snapshot")
            {
                if (!Text(parameters, "accountId", 64, out var account) || account.Any(c => c is < '0' or > '9')
                    || !Text(parameters, "id", 1024, out _) || !Text(parameters, "operationId", 128, out _)
                    || !parameters.TryGetProperty("version", out var version) || !version.TryGetInt64(out var number)
                    || number < (operation == "confirm-group" ? 0 : 1)) return false;
            }
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
            if (operation == "set-reminder" && !ValidReminder(parameters)) return false;
            request = new(id, operation, parameters.Clone());
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        { return false; }
    }

    private static bool ValidReminder(JsonElement parameters)
    {
        if (!Text(parameters, "reminderMode", 16, out var mode)
            || !parameters.TryGetProperty("reminderVersion", out var version)
            || !version.TryGetInt64(out var number) || number < 1) return false;
        var hasHours = parameters.TryGetProperty("reminderHours", out var hours) && hours.ValueKind != JsonValueKind.Null;
        var hasDays = parameters.TryGetProperty("reminderDays", out var days) && days.ValueKind != JsonValueKind.Null;
        var hasClock = parameters.TryGetProperty("reminderClock", out var clock) && clock.ValueKind != JsonValueKind.Null;
        return mode switch
        {
            "default" or "off" => !hasHours && !hasDays && !hasClock,
            "hours" => hasHours && !hasDays && !hasClock && hours.ValueKind == JsonValueKind.Number
                && hours.TryGetDouble(out var value) && double.IsFinite(value) && value > 0,
            "calendar" => !hasHours && hasDays && days.ValueKind == JsonValueKind.Number
                && days.TryGetInt32(out var count) && count >= 0 && hasClock && clock.ValueKind == JsonValueKind.String
                && TimeOnly.TryParseExact(clock.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => false
        };
    }

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
