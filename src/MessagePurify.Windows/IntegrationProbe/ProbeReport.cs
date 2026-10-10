using System.IO;
using System.Text.Json;

namespace MessagePurify.Windows.IntegrationProbe;

public sealed class ProbeReport
{
    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
    public string OsVersion { get; } = Environment.OSVersion.VersionString;
    public bool Elevated { get; set; }
    public string? Error { get; set; }
    public string BridgeStatus { get; set; } = "notRequested";
    public string? WebView2Version { get; set; }
    public bool BridgeRoundTrip { get; set; }
    public bool LiteralTextPreserved { get; set; }
    public bool PageSecureContext { get; set; }
    public string? CaptureFileName { get; set; }
    public DesktopProbeResult Desktop { get; set; } = new("notRequested", null, false, false, false, null);
    public string NotificationStatus { get; set; } = "notRequested";
    public string? NotificationSetting { get; set; }
    public string? NotificationMode { get; set; }
    public uint? NotificationId { get; set; }
    public bool ActivationObserved { get; set; }
    public string? ActivationKind { get; set; }
    public bool? ActivationAccountMatched { get; set; }
    public Dictionary<string, string> ManualChecks { get; } = new()
    {
        ["winD"] = "notTested", ["ordinaryWindowOcclusion"] = "notTested",
        ["dragAndResize"] = "notTested", ["multiMonitorDpi"] = "notTested",
        ["explorerRestart"] = "notTested", ["sleepResume"] = "notTested",
        ["notificationPresentation"] = "notTested", ["notificationSound"] = "notTested"
    };

    public void Save(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}

public sealed record DesktopProbeResult(string Status, string? ParentClass, bool ChildStyle,
    bool NoActivateStyle, bool TopmostStyle, uint? Dpi);
