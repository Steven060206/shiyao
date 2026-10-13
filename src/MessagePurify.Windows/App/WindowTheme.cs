using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MessagePurify.Windows.App;

[SupportedOSPlatform("windows")]
internal static class WindowTheme
{
    internal static bool InitialDark(string? preference) => preference switch
    {
        "dark" => true,
        "light" => false,
        _ => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int value && value == 0
    };

    internal static bool Apply(IntPtr window, bool dark)
    {
        var applied = true;
        // Unsupported DWM colors on older Windows keep the native frame without blocking startup.
        Set(20, dark ? 1 : 0);            // DWMWA_USE_IMMERSIVE_DARK_MODE
        Set(35, dark ? 0 : 0xEEF1ED);     // DWMWA_CAPTION_COLOR, matching the page background
        Set(36, dark ? 0xF1F1F1 : 0x2D3625); // DWMWA_TEXT_COLOR
        Set(34, dark ? 0x2A2A2A : 0xE3E8E1); // DWMWA_BORDER_COLOR
        return applied;
        void Set(int attribute, int value) => applied &= DwmSetWindowAttribute(window, attribute, ref value, sizeof(int)) >= 0;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

}
