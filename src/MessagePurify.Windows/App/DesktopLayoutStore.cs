using System.IO;
using System.Text.Json;
using MessagePurify.Windows.IntegrationProbe;

namespace MessagePurify.Windows.App;

public sealed record DesktopCardLayout(DesktopRect Tasks, DesktopRect Pending);

public sealed class DesktopLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string path;
    private sealed record StoredLayout(int Schema, DesktopRect Tasks, DesktopRect Pending);

    public DesktopLayoutStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        path = Path.Combine(Path.GetFullPath(dataRoot), "desktop-layout.json");
    }

    public string? Error { get; private set; }

    public DesktopCardLayout Load(DesktopRect bounds, double scale)
    {
        ValidateScale(scale);
        var fallback = Default(bounds, scale);
        Error = null;
        if (!File.Exists(path)) return fallback;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredLayout>(File.ReadAllText(path), JsonOptions);
            if (stored?.Schema != 1) throw new JsonException("Unsupported desktop layout schema.");
            return new(DesktopGeometry.Clamp(FromDips(stored.Tasks, scale), bounds),
                DesktopGeometry.Clamp(FromDips(stored.Pending, scale), bounds));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            Error = "桌面布局无法读取，已使用默认位置；原文件已保留。";
            return fallback;
        }
    }

    public void Save(DesktopCardLayout layout, double scale)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ValidateScale(scale);
        var stored = new StoredLayout(1, ToDips(layout.Tasks, scale), ToDips(layout.Pending, scale));
        Validate(stored.Tasks);
        Validate(stored.Pending);
        Error = null;
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(stored, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error = "桌面布局未能保存，原文件已保留。";
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static DesktopCardLayout Default(DesktopRect bounds, double scale)
    {
        Validate(bounds);
        var marginX = Math.Min(24 * scale, bounds.Width / 20);
        var marginY = Math.Min(24 * scale, bounds.Height / 20);
        var gap = Math.Min(16 * scale, bounds.Width / 20);
        var width = Math.Min(340 * scale, (bounds.Width - 2 * marginX - gap) / 2);
        var height = Math.Min(360 * scale, bounds.Height - 2 * marginY);
        var tasks = new DesktopRect(bounds.X + marginX, bounds.Y + marginY, width, height);
        var pending = tasks with { X = tasks.X + width + gap };
        return new(DesktopGeometry.Clamp(tasks, bounds), DesktopGeometry.Clamp(pending, bounds));
    }

    private static void ValidateScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be finite and positive.");
    }

    private static void Validate(DesktopRect rectangle) => DesktopGeometry.Clamp(rectangle, rectangle);
    private static DesktopRect ToDips(DesktopRect rectangle, double scale) =>
        new(rectangle.X / scale, rectangle.Y / scale, rectangle.Width / scale, rectangle.Height / scale);
    private static DesktopRect FromDips(DesktopRect rectangle, double scale) =>
        new(rectangle.X * scale, rectangle.Y * scale, rectangle.Width * scale, rectangle.Height * scale);
}
