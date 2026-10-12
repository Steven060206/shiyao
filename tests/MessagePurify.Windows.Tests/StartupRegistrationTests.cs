using Microsoft.Win32;
using MessagePurify.Windows.App;
using System.Runtime.Versioning;

namespace MessagePurify.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string subKey = $"Software\\MessagePurify.Tests\\{Guid.NewGuid():N}\\Run";

    [Fact]
    public void EnableWritesQuotedExecutablePathAndDisableRemovesOnlyTheAppValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey)!;
        key.SetValue("OtherApp", "keep-me");
        var executable = @"D:\消息 提纯\MessagePurify.exe";

        StartupRegistration.SetEnabled(subKey, "MessagePurify", executable, true);

        using (var enabled = Registry.CurrentUser.OpenSubKey(subKey))
        {
            Assert.Equal($"\"{executable}\" --background", enabled!.GetValue("MessagePurify"));
            Assert.True(StartupRegistration.IsEnabled(subKey, "MessagePurify", executable));
        }

        StartupRegistration.SetEnabled(subKey, "MessagePurify", executable, false);

        using var disabled = Registry.CurrentUser.OpenSubKey(subKey);
        Assert.Null(disabled!.GetValue("MessagePurify"));
        Assert.Equal("keep-me", disabled.GetValue("OtherApp"));
        Assert.False(StartupRegistration.IsEnabled(subKey, "MessagePurify", executable));
    }

    [Fact]
    public void RepeatedEnableIsIdempotentAndDifferentPathIsNotReportedEnabled()
    {
        var first = @"C:\Program Files\消息提纯\MessagePurify.exe";
        var second = @"C:\Other\MessagePurify.exe";
        StartupRegistration.SetEnabled(subKey, "MessagePurify", first, true);
        StartupRegistration.SetEnabled(subKey, "MessagePurify", first, true);

        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        Assert.Equal($"\"{first}\" --background", key!.GetValue("MessagePurify"));
        Assert.True(StartupRegistration.IsEnabled(subKey, "MessagePurify", first));
        Assert.False(StartupRegistration.IsEnabled(subKey, "MessagePurify", second));
    }

    [Fact]
    public void StartupPreferenceDefaultsToEnabledAndPersistsDisabledChoice()
    {
        var root = Path.Combine(Path.GetTempPath(), "MessagePurify-startup-preference-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(AppStartupPreferences.Load(root));
            AppStartupPreferences.Save(root, false);
            Assert.False(AppStartupPreferences.Load(root));
            AppStartupPreferences.Save(root, true);
            Assert.True(AppStartupPreferences.Load(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void StartupPreferenceFallsBackToEnabledWhenTheSettingsFileIsMalformed()
    {
        var root = Path.Combine(Path.GetTempPath(), "MessagePurify-startup-preference-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "app-settings.json"), "{not-json");
            Assert.True(AppStartupPreferences.Load(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree($"Software\\MessagePurify.Tests\\{subKey.Split('\\')[2]}", false);
}
