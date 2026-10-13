using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class AppIdentityTests
{
    [Fact]
    public void StartupReplacesLauncherIdentityAndRemainsStableOnReinitialization()
    {
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("MessagePurify.Tests.Launcher"));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            AppIdentity.Initialize();
            Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out var id));
            try { Assert.Equal(AppIdentity.Id, Marshal.PtrToStringUni(id)); }
            finally { Marshal.FreeCoTaskMem(id); }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr id);

    [Fact]
    public void MainWindowHasExplicitIdentityRegardlessOfLaunchContext()
    {
        var window = CreateWindowExW(0, "STATIC", "Identity test", 0, 0, 0, 100, 100,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, window);
        try
        {
            AppIdentity.SetWindow(window);
            Assert.Equal(AppIdentity.Id, AppIdentity.GetWindowId(window));
        }
        finally { DestroyWindow(window); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyWindow(IntPtr window);
}
