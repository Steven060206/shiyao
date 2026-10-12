using System.Text.Json;
using System.IO;

namespace MessagePurify.Windows.App;

internal static class AppStartupPreferences
{
    private sealed record Settings(bool AutoStartEnabled);

    public static bool Load(string dataRoot)
    {
        var path = Path.Combine(Path.GetFullPath(dataRoot), "app-settings.json");
        if (!File.Exists(path)) return true;
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
            return settings?.AutoStartEnabled ?? true;
        }
        catch (JsonException) { return true; }
        catch (IOException) { return true; }
    }

    public static void Save(string dataRoot, bool enabled)
    {
        var directory = Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "app-settings.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(enabled)));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
