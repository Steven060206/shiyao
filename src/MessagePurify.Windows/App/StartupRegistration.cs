using Microsoft.Win32;
using System.IO;
using System.Runtime.Versioning;

namespace MessagePurify.Windows.App;

[SupportedOSPlatform("windows")]
internal static class StartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "MessagePurify";

    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "MessagePurify.exe");

    public static bool IsEnabled() => IsEnabled(RunKeyPath, ValueName, ExecutablePath);

    public static void SetEnabled(bool enabled) => SetEnabled(RunKeyPath, ValueName, ExecutablePath, enabled);

    internal static bool IsEnabled(string subKeyPath, string valueName, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
        return string.Equals(key?.GetValue(valueName) as string, Command(executablePath), StringComparison.Ordinal);
    }

    internal static void SetEnabled(string subKeyPath, string valueName, string executablePath, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true)
            ?? throw new InvalidOperationException("startup-registration-unavailable");
        if (enabled) key.SetValue(valueName, Command(executablePath), RegistryValueKind.String);
        else key.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static string Command(string executablePath) => $"\"{executablePath}\" --background";
}
