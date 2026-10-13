using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MessagePurify.Windows.App;
using Microsoft.Win32;

namespace MessagePurify.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowThemeTests
{
    [Theory]
    [InlineData("dark", true)]
    [InlineData("light", false)]
    public void ExplicitPreferenceControlsInitialFrame(string theme, bool dark) =>
        Assert.Equal(dark, WindowTheme.InitialDark(theme));

    [Fact]
    public void MissingOrSystemPreferenceFollowsWindowsApplicationTheme()
    {
        var dark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int value && value == 0;
        Assert.Equal(dark, WindowTheme.InitialDark(null));
        Assert.Equal(dark, WindowTheme.InitialDark("system"));
    }

    [Fact]
    public void NativeCaptionTextAndBorderChangeTogetherAndCanSwitchBack()
    {
        var window = CreateWindowExW(0, "STATIC", "Titlebar test", 0x00CF0000, 0, 0, 200, 200,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, window);
        try
        {
            foreach (var dark in new[] { true, false, true })
            {
                Assert.True(WindowTheme.Apply(window, dark), "Windows rejected a caption, text, border or dark-mode attribute.");
            }
        }
        finally { DestroyWindow(window); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyWindow(IntPtr window);
}
