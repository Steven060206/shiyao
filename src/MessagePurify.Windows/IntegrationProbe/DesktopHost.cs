using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MessagePurify.Windows.IntegrationProbe;

public sealed class DesktopHost : IDisposable
{
    private Window? window;
    private nint oldParent;
    private nint originalStyle;
    private nint parent;
    private double dpiScale;

    // These public Win32 APIs attach to the current Explorer desktop hierarchy.
    // That hierarchy is private Explorer behavior; this is an explicitly requested probe.
    public DesktopProbeResult Attach()
    {
        nint desktopParent = 0;
        Native.EnumWindows((handle, _) =>
        {
            string className = Native.ClassName(handle);
            if (className is "Progman" or "WorkerW")
            {
                // The outer wallpaper host is behind the icon view and hides our cards.
                desktopParent = Native.FindWindowEx(handle, 0, "SHELLDLL_DefView", null);
                if (desktopParent != 0) return false;
            }
            return true;
        }, 0);
        if (desktopParent == 0) return new("desktopParentMissing", null, false, false, false, null);

        var parentContext = Native.GetWindowDpiAwarenessContext(desktopParent);
        if (!Native.AreDpiAwarenessContextsEqual(parentContext, Native.GetThreadDpiAwarenessContext()))
            return new("dpiContextMismatch", Native.ClassName(desktopParent), false, false, false, Native.GetDpiForWindow(desktopParent));

        parent = desktopParent;
        dpiScale = Native.GetDpiForWindow(parent) / 96d;
        var bounds = DesktopGeometry.Clamp(new(60 * dpiScale, 80 * dpiScale, 680 * dpiScale, 300 * dpiScale), ParentBounds());
        window = new Window
        {
            Title = "消息提纯桌面验证", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, Topmost = false,
            Width = bounds.Width / dpiScale, Height = bounds.Height / dpiScale,
            Background = Brushes.White, Content = CreateCards()
        };
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.EnsureHandle();
        originalStyle = Native.GetWindowLongPtr(hwnd, Native.StyleIndex);
        Native.SetWindowLongPtr(hwnd, Native.StyleIndex, (nint)(((long)originalStyle & ~Native.WsPopup) | Native.WsChild));
        var extended = (long)Native.GetWindowLongPtr(hwnd, Native.ExStyleIndex);
        Native.SetWindowLongPtr(hwnd, Native.ExStyleIndex, (nint)((extended | Native.WsExNoActivate | Native.WsExToolWindow) & ~Native.WsExTopmost));
        Marshal.SetLastPInvokeError(0);
        oldParent = Native.SetParent(hwnd, desktopParent);
        int error = Marshal.GetLastPInvokeError();
        if (oldParent == 0 && error != 0)
        {
            Dispose();
            return new("attachmentFailed:" + error, Native.ClassName(desktopParent), false, false, false, Native.GetDpiForWindow(desktopParent));
        }
        window.Show();
        SetBounds(bounds);
        long actualStyle = (long)Native.GetWindowLongPtr(hwnd, Native.StyleIndex);
        long actualExtended = (long)Native.GetWindowLongPtr(hwnd, Native.ExStyleIndex);
        bool attached = Native.GetParent(hwnd) == desktopParent;
        return new(attached ? "attachedStructureOnly" : "parentMismatch", Native.ClassName(desktopParent),
            (actualStyle & Native.WsChild) != 0, (actualExtended & Native.WsExNoActivate) != 0,
            (actualExtended & Native.WsExTopmost) != 0, Native.GetDpiForWindow(hwnd));
    }

    private Grid CreateCards()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new());
        for (int index = 0; index < 2; index++)
        {
            var text = new TextBlock { Text = index == 0 ? "提醒事项\n\n桌面附着验证：此处为合成内容" : "待确认\n\n此处没有真实 QQ 数据",
                Margin = new Thickness(20), FontSize = 16, TextWrapping = TextWrapping.Wrap };
            var border = new Border { Margin = new Thickness(6), BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = text };
            Grid.SetColumn(border, index);
            grid.Children.Add(border);
        }
        DesktopPoint? dragStart = null;
        DesktopRect windowStart = default;
        grid.MouseLeftButtonDown += (_, args) =>
        {
            if (window is null) return;
            dragStart = CursorInParent();
            windowStart = CurrentBounds();
            grid.CaptureMouse();
        };
        grid.MouseMove += (_, args) =>
        {
            if (window is null || dragStart is null || args.LeftButton != MouseButtonState.Pressed) return;
            SetBounds(DesktopGeometry.Drag(windowStart, dragStart.Value, CursorInParent(), ParentBounds()));
        };
        grid.MouseLeftButtonUp += (_, _) => { dragStart = null; grid.ReleaseMouseCapture(); };
        var resize = new Thumb { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE,
            Background = Brushes.LightGray, ToolTip = "拖动箭头调整两张卡片的大小" };
        Grid.SetColumn(resize, 1);
        resize.DragDelta += (_, args) =>
        {
            if (window is null) return;
            SetBounds(DesktopGeometry.Resize(CurrentBounds(), new(args.HorizontalChange * dpiScale, args.VerticalChange * dpiScale),
                ParentBounds(), new(400 * dpiScale, 180 * dpiScale)));
        };
        grid.Children.Add(resize);
        var resizeArrow = new TextBlock { Text = "↘", FontSize = 22, Width = 32, Height = 32,
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        Grid.SetColumn(resizeArrow, 1);
        grid.Children.Add(resizeArrow);
        return grid;
    }

    private DesktopRect ParentBounds()
    {
        if (!Native.GetClientRect(parent, out var bounds)) throw new InvalidOperationException("Cannot read desktop bounds.");
        return new(0, 0, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    private DesktopPoint CursorInParent()
    {
        if (!Native.GetCursorPos(out var point) || !Native.ScreenToClient(parent, ref point))
            throw new InvalidOperationException("Cannot read desktop pointer.");
        return new(point.X, point.Y);
    }

    private DesktopRect CurrentBounds()
    {
        var hwnd = new WindowInteropHelper(window!).Handle;
        if (!Native.GetWindowRect(hwnd, out var rectangle)) throw new InvalidOperationException("Cannot read card bounds.");
        var origin = new Native.Point { X = rectangle.Left, Y = rectangle.Top };
        if (!Native.ScreenToClient(parent, ref origin)) throw new InvalidOperationException("Cannot map card coordinates.");
        return new(origin.X, origin.Y, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
    }

    private void SetBounds(DesktopRect bounds)
    {
        var hwnd = new WindowInteropHelper(window!).Handle;
        if (!Native.SetWindowPos(hwnd, 0, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
            (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), Native.NoActivate | Native.NoZOrder | Native.FrameChanged))
            throw new InvalidOperationException("Cannot position desktop card.");
    }

    public void Dispose()
    {
        if (window is null) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != 0)
        {
            window.Hide();
            Native.SetParent(hwnd, oldParent);
            Native.SetWindowLongPtr(hwnd, Native.StyleIndex, originalStyle);
        }
        window.Close();
        window = null;
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rectangle { public int Left, Top, Right, Bottom; }
    internal const int StyleIndex = -16, ExStyleIndex = -20;
    internal const long WsPopup = 0x80000000L, WsChild = 0x40000000L,
        WsExNoActivate = 0x08000000L, WsExToolWindow = 0x80L, WsExTopmost = 8L;
    internal const uint NoSize = 1, NoZOrder = 4, NoActivate = 0x10, FrameChanged = 0x20;
    internal delegate bool EnumCallback(nint hwnd, nint parameter);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder buffer, int capacity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rectangle rectangle);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rectangle rectangle);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    internal static string ClassName(nint hwnd) { var buffer = new StringBuilder(256); GetClassName(hwnd, buffer, buffer.Capacity); return buffer.ToString(); }
}
