using System.Runtime.InteropServices;
using MessagePurify.Windows.IntegrationProbe;

namespace MessagePurify.Windows.App;

public interface IDesktopEnvironment
{
    DesktopEnvironmentState? Capture();
    bool IsAttached(nint child, nint parent);
}

public sealed record DesktopEnvironmentState(nint Parent, double Scale, DesktopRect Bounds,
    IReadOnlyList<DesktopRect> WorkAreas, bool Compatible);

public sealed class NativeDesktopEnvironment : IDesktopEnvironment
{
    public DesktopEnvironmentState? Capture()
    {
        var parent = DesktopNative.FindDesktopParent();
        if (parent == 0 || !DesktopNative.IsWindow(parent)) return null;
        var dpi = DesktopNative.GetDpiForWindow(parent);
        if (dpi == 0 || !DesktopNative.GetClientRect(parent, out var rectangle)) return null;
        var bounds = new DesktopRect(0, 0, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var areas = new List<(bool Primary, DesktopRect Area)>();
        if (!EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            var topLeft = new DesktopNative.Point { X = info.Work.Left, Y = info.Work.Top };
            var bottomRight = new DesktopNative.Point { X = info.Work.Right, Y = info.Work.Bottom };
            if (!DesktopNative.ScreenToClient(parent, ref topLeft) || !DesktopNative.ScreenToClient(parent, ref bottomRight)) return true;
            var x = Math.Max(0, topLeft.X); var y = Math.Max(0, topLeft.Y);
            var width = Math.Min(bounds.Width, bottomRight.X) - x;
            var height = Math.Min(bounds.Height, bottomRight.Y) - y;
            if (width > 0 && height > 0) areas.Add(((info.Flags & 1) != 0, new(x, y, width, height)));
            return true;
        }, 0) || areas.Count == 0 || !DesktopNative.IsWindow(parent)) return null;
        return new(parent, dpi / 96d, bounds, areas.OrderByDescending(area => area.Primary)
            .Select(area => area.Area).ToArray(), DesktopNative.AreDpiAwarenessContextsEqual(
                DesktopNative.GetWindowDpiAwarenessContext(parent), DesktopNative.GetThreadDpiAwarenessContext()));
    }

    public bool IsAttached(nint child, nint parent) => DesktopNative.IsWindow(parent) && DesktopNative.IsWindow(child)
        && DesktopNative.GetParent(child) == parent
        && ((long)DesktopNative.GetWindowLongPtr(child, -16) & 0x40000000L) != 0
        && ((long)DesktopNative.GetWindowLongPtr(child, -20) & (0x08000000L | 8L)) == 0x08000000L;

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal DesktopNative.Rectangle Monitor, Work;
        internal uint Flags;
    }
    private delegate bool MonitorCallback(nint monitor, nint hdc, nint rectangle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorCallback callback, nint parameter);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
