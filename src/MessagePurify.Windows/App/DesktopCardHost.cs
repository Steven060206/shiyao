using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Windows.IntegrationProbe;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using BrushConverter = System.Windows.Media.BrushConverter;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace MessagePurify.Windows.App;

/// <summary>正式事项的桌面呈现；不调用 Windows 通知或声音。</summary>
public sealed class DesktopCardHost : IDisposable
{
    private readonly AppRuntime runtime;
    private readonly DesktopLayoutStore layouts;
    private readonly Func<string, string, Task> openItem;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private CardWindow? tasks, pending;
    private nint parent;
    private double scale = 1;
    private string theme = "light";
    private bool refreshing, disposed;
    private int dirty = 1;
    private DateTimeOffset lastRefresh;
    private DesktopCardsSnapshot? current;
    private string? signature, operationError;
    public string State { get; private set; } = "waiting";
    public string? Error { get; private set; }
    public event Action? StatusChanged;
    public DesktopCardsSnapshot? Snapshot => current;
    internal Window TaskWindow => tasks!;
    internal Window PendingWindow => pending!;
    internal DesktopCardLayout Layout => new(tasks!.Bounds, pending!.Bounds);

    public DesktopCardHost(AppRuntime runtime, string dataRoot, Func<string, string, Task> openItem)
    {
        this.runtime = runtime;
        this.openItem = openItem;
        layouts = new(dataRoot);
        runtime.Changed += OnChanged;
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();
    }

    private void OnChanged() => Interlocked.Exchange(ref dirty, 1);

    public void SetAppearance(string theme, bool reducedMotion)
    {
        // Cards do not animate; reduced motion therefore also applies without a second setting.
        this.theme = theme;
        signature = null;
        OnChanged();
    }

    public async Task RefreshAsync()
    {
        if (disposed || refreshing) return;
        refreshing = true;
        try
        {
            if (!EnsureAttached()) return;
            var now = DateTimeOffset.UtcNow;
            if (Interlocked.Exchange(ref dirty, 0) == 0 && now - lastRefresh < TimeSpan.FromSeconds(30)) return;
            var snapshot = await Task.Run(() => runtime.Workspace?.Snapshot(now));
            if (disposed) return;
            current = DesktopCardProjection.Build(snapshot, runtime.Status);
            lastRefresh = now;
            var nextSignature = DesktopCardProjection.RenderSignature(current, theme, operationError, layouts.Error, now);
            if (signature == nextSignature) return;
            signature = nextSignature;
            tasks!.Render(current, false, theme, operationError ?? layouts.Error, now, OpenAsync, CompleteAsync);
            pending!.Render(current, true, theme, operationError ?? layouts.Error, now, OpenAsync, CompleteAsync);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            operationError = "读取桌面事项失败，请打开主界面查看连接与本地数据状态。";
            signature = null;
            OnChanged();
            tasks?.ShowError(operationError);
        }
        finally { refreshing = false; }
    }

    private bool EnsureAttached()
    {
        var latest = DesktopNative.FindDesktopParent();
        if (tasks is not null && pending is not null && latest == parent &&
            DesktopNative.IsWindow(tasks.Handle) && DesktopNative.IsWindow(pending.Handle))
        {
            var dpi = DesktopNative.GetDpiForWindow(parent) / 96d;
            if (dpi != scale)
            {
                // Drag/resize already save; an automatic DPI reload must preserve a damaged layout file.
                DisposeWindows();
            }
            else
            {
                var area = ParentBounds();
                tasks.SetBounds(DesktopGeometry.Clamp(tasks.Bounds, area));
                pending.SetBounds(DesktopGeometry.Clamp(pending.Bounds, area));
                return true;
            }
        }
        DisposeWindows();
        if (latest == 0) { SetStatus("waiting", "正在等待 Windows 桌面就绪。"); return false; }
        if (!DesktopNative.AreDpiAwarenessContextsEqual(DesktopNative.GetWindowDpiAwarenessContext(latest),
            DesktopNative.GetThreadDpiAwarenessContext()))
        {
            SetStatus("unavailable", "桌面显示模式暂不兼容，请在主界面查看事项。");
            return false;
        }
        parent = latest;
        scale = DesktopNative.GetDpiForWindow(parent) / 96d;
        var layout = layouts.Load(ParentBounds(), scale);
        try
        {
            tasks = new("消息提纯 · 提醒事项", parent, scale, layout.Tasks, ParentBounds, SaveLayout);
            pending = new("消息提纯 · 待确认", parent, scale, layout.Pending, ParentBounds, SaveLayout);
            signature = null;
            OnChanged();
            SetStatus("attached", null);
            return true;
        }
        catch (InvalidOperationException)
        {
            DisposeWindows();
            SetStatus("unavailable", "桌面卡片挂载失败，请在主界面查看事项。");
            return false;
        }
    }

    private DesktopRect ParentBounds()
    {
        if (!DesktopNative.GetClientRect(parent, out var bounds)) throw new InvalidOperationException("Desktop bounds unavailable.");
        return new(0, 0, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    private void SaveLayout()
    {
        if (tasks is not null && pending is not null) layouts.Save(Layout, scale);
        signature = null;
        OnChanged();
    }

    internal void SetLayout(DesktopCardLayout layout)
    {
        tasks!.SetBounds(DesktopGeometry.Clamp(layout.Tasks, ParentBounds()));
        pending!.SetBounds(DesktopGeometry.Clamp(layout.Pending, ParentBounds()));
        SaveLayout();
    }

    private async Task OpenAsync(string accountId, ApplicationItem item)
    {
        try { await openItem(accountId, item.Id); }
        catch (InvalidOperationException) { operationError = "主界面尚未就绪，请稍后重试。"; OnChanged(); }
    }

    private async Task CompleteAsync(string accountId, ApplicationItem item)
    {
        try
        {
            var workspace = runtime.Workspace ?? throw new InvalidOperationException();
            await Task.Run(() => workspace.Execute(new(accountId, "complete", item.Id,
                Guid.NewGuid().ToString("N"), item.Version), DateTimeOffset.UtcNow));
            operationError = null;
            runtime.Refresh();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            operationError = "事项已有变化或保存未完成，请核对最新内容后重试。";
            OnChanged();
        }
        await RefreshAsync();
    }

    private void SetStatus(string state, string? error)
    {
        if (State == state && Error == error) return;
        State = state; Error = error;
        StatusChanged?.Invoke();
    }

    internal object Inspect() => new { tasks = tasks!.Inspect(), pending = pending!.Inspect() };

    internal void Capture(string path)
    {
        tasks!.UpdateLayout(); pending!.UpdateLayout();
        var first = (FrameworkElement)tasks.Content;
        var second = (FrameworkElement)pending.Content;
        var width = first.ActualWidth + second.ActualWidth + 16;
        var height = Math.Max(first.ActualHeight, second.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(first), null, new Rect(0, 0, first.ActualWidth, first.ActualHeight));
            context.DrawRectangle(new VisualBrush(second), null, new Rect(first.ActualWidth + 16, 0, second.ActualWidth, second.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private void DisposeWindows()
    {
        tasks?.Dispose(); pending?.Dispose();
        tasks = null; pending = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        runtime.Changed -= OnChanged;
        DisposeWindows();
    }

    private sealed class CardWindow : Window, IDisposable
    {
        private readonly nint parent, oldParent, originalStyle;
        private readonly double scale;
        private readonly Func<DesktopRect> parentBounds;
        private readonly Action save;
        private readonly Grid content = new();
        private readonly StackPanel list = new();
        private readonly TextBlock heading = new(), footer = new();
        private readonly Border frame;
        private readonly Thumb resize;
        private bool disposed;
        internal nint Handle { get; }
        internal DesktopRect Bounds { get; private set; }

        public CardWindow(string title, nint parent, double scale, DesktopRect bounds,
            Func<DesktopRect> parentBounds, Action save)
        {
            this.parent = parent; this.scale = scale; this.parentBounds = parentBounds; this.save = save;
            Title = title; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; ShowActivated = false; Topmost = false;
            Width = bounds.Width / scale; Height = bounds.Height / scale;
            FontFamily = new("Microsoft YaHei UI"); FontSize = 13;
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            content.RowDefinitions.Add(new());
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            heading.Margin = new(18, 16, 18, 12); heading.FontSize = 18; heading.FontWeight = FontWeights.SemiBold;
            heading.Cursor = Cursors.SizeAll;
            content.Children.Add(heading);
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            list.Margin = new(12, 0, 12, 0); Grid.SetRow(scroll, 1); content.Children.Add(scroll);
            footer.Margin = new(18, 10, 34, 12); footer.FontSize = 11; footer.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(footer, 2); content.Children.Add(footer);
            resize = new Thumb { Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, ToolTip = "拖动调整卡片大小" };
            Grid.SetRowSpan(resize, 3); content.Children.Add(resize);
            var arrow = new TextBlock { Text = "↘", FontSize = 22, Width = 30, Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
            Grid.SetRowSpan(arrow, 3); content.Children.Add(arrow);
            frame = new Border { Child = content, BorderThickness = new(1), CornerRadius = new(12) };
            Content = frame;
            DesktopPoint? initial = null;
            DesktopRect start = default;
            heading.MouseLeftButtonDown += (_, args) => { initial = CursorInParent(); start = Bounds; heading.CaptureMouse(); args.Handled = true; };
            heading.MouseMove += (_, args) =>
            {
                if (initial is not null && args.LeftButton == MouseButtonState.Pressed)
                    SetBounds(DesktopGeometry.Drag(start, initial.Value, CursorInParent(), parentBounds()));
            };
            heading.MouseLeftButtonUp += (_, args) => { initial = null; heading.ReleaseMouseCapture(); save(); args.Handled = true; };
            resize.DragDelta += (_, args) => SetBounds(DesktopGeometry.Resize(Bounds,
                new(args.HorizontalChange * scale, args.VerticalChange * scale), parentBounds(), new(260 * scale, 200 * scale)));
            resize.DragCompleted += (_, _) => save();
            Handle = new WindowInteropHelper(this).EnsureHandle();
            originalStyle = DesktopNative.GetWindowLongPtr(Handle, -16);
            DesktopNative.SetWindowLongPtr(Handle, -16, (nint)(((long)originalStyle & ~0x80000000L) | 0x40000000L));
            var extended = (long)DesktopNative.GetWindowLongPtr(Handle, -20);
            DesktopNative.SetWindowLongPtr(Handle, -20, (nint)((extended | 0x08000000L | 0x80L) & ~8L));
            Marshal.SetLastPInvokeError(0);
            oldParent = DesktopNative.SetParent(Handle, parent);
            if (oldParent == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                DesktopNative.SetWindowLongPtr(Handle, -16, originalStyle);
                Close();
                throw new InvalidOperationException("Desktop attachment failed.");
            }
            Show(); SetBounds(bounds);
        }

        internal void SetBounds(DesktopRect bounds)
        {
            Bounds = bounds;
            DesktopNative.SetWindowPos(Handle, 0, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
                (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 4 | 0x10 | 0x20);
        }

        private DesktopPoint CursorInParent()
        {
            if (!DesktopNative.GetCursorPos(out var point) || !DesktopNative.ScreenToClient(parent, ref point))
                throw new InvalidOperationException("Desktop pointer unavailable.");
            return new(point.X, point.Y);
        }

        internal void ShowError(string error) => footer.Text = error;

        internal void Render(DesktopCardsSnapshot snapshot, bool isPending, string theme, string? error, DateTimeOffset now,
            Func<string, ApplicationItem, Task> open, Func<string, ApplicationItem, Task> complete)
        {
            var dark = theme == "dark";
            Brush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            var background = Brush(dark ? "#000000" : "#F7FAF8");
            var text = Brush(dark ? "#F3F5F4" : "#17231D");
            var muted = Brush(dark ? "#ABB9B0" : "#586B60");
            var line = Brush(dark ? "#303B34" : "#CCD8D0");
            var accent = Brush(dark ? "#8DD4A6" : "#22663D");
            Background = background; Foreground = text; frame.Background = background; frame.BorderBrush = line;
            heading.Foreground = text; footer.Foreground = muted; resize.Background = line;
            var items = isPending ? snapshot.Pending : snapshot.Tasks;
            heading.Text = (isPending ? "待确认" : "提醒事项") + (items.Count > 0 ? "  ·  " + items.Count : "");
            var previousScroll = ((ScrollViewer)content.Children[1]).VerticalOffset;
            list.Children.Clear();
            if (items.Count == 0)
                list.Children.Add(new TextBlock { Text = snapshot.AccountId is null ? "等待账号连接，已有事项将保留" : "暂无待办",
                    Margin = new(8, 18, 8, 18), Foreground = muted, TextWrapping = TextWrapping.Wrap });
            foreach (var item in items)
            {
                var body = new StackPanel { Margin = new(12) };
                var past = DesktopCardProjection.IsPast(item, now);
                var highlighted = !isPending && (past || item.Priority is 0 or 1);
                var row = new Border { Child = body, CornerRadius = new(8), BorderThickness = new(1),
                    BorderBrush = highlighted ? accent : line, Background = highlighted ? Brush(dark ? "#14281B" : "#E5F3E9") : background,
                    Margin = new(0, 0, 0, 8) };
                var title = item.Title + (item.Unread ? "  ●" : "");
                var details = new Button { Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold, FontSize = 14, MaxHeight = 66 }, Foreground = text,
                    Background = Brushes.Transparent, BorderThickness = new(0), HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new(0), Cursor = Cursors.Hand, Tag = "open:" + item.Id };
                details.Click += async (_, _) => await open(snapshot.AccountId!, item);
                body.Children.Add(details);
                if (!string.IsNullOrEmpty(item.ChangeLabel))
                    body.Children.Add(new TextBlock { Text = item.ChangeLabel, Foreground = accent,
                        FontSize = 11, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                        Margin = new(0, 6, 0, 0) });
                body.Children.Add(new TextBlock { Text = item.Source == "manual" ? (item.Course ?? "未分类") + " · 手动添加" : item.Course ?? item.Group, Foreground = muted,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 6, 0, 4) });
                var label = isPending ? "核对原文，补齐待确认信息" :
                    $"P{item.Priority ?? 3}  ·  {(past ? item.TimeKind == "start" ? "开始时间已过" : "已逾期" : item.TimeKind == "start" ? "开始" : "截止")}  {item.Due}{(item.Time is null ? " · 仅有日期" : " " + item.Time)}";
                body.Children.Add(new TextBlock { Text = label, Foreground = highlighted ? accent : muted,
                    FontSize = 11, TextWrapping = TextWrapping.Wrap });
                if (!isPending)
                {
                    var done = new Button { Content = "完成", Tag = "complete:" + item.Id, Padding = new(10, 4, 10, 4),
                        HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0),
                        Background = accent, Foreground = dark ? Brushes.Black : Brushes.White, BorderThickness = new(0), Cursor = Cursors.Hand };
                    done.Click += async (_, _) => { done.IsEnabled = false; await complete(snapshot.AccountId!, item); };
                    body.Children.Add(done);
                }
                list.Children.Add(row);
            }
            ((ScrollViewer)content.Children[1]).ScrollToVerticalOffset(previousScroll);
            footer.Text = error ?? snapshot.ConnectionText;
            if (isPending && items.Count == 0) Hide(); else if (!IsVisible) { Show(); SetBounds(Bounds); }
        }

        internal object Inspect() => new { parentClass = DesktopNative.ClassName(DesktopNative.GetParent(Handle)),
            child = ((long)DesktopNative.GetWindowLongPtr(Handle, -16) & 0x40000000L) != 0,
            noActivate = ((long)DesktopNative.GetWindowLongPtr(Handle, -20) & 0x08000000L) != 0,
            topmost = ((long)DesktopNative.GetWindowLongPtr(Handle, -20) & 8L) != 0,
            visible = IsVisible, bounds = Bounds };

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (DesktopNative.IsWindow(Handle))
            {
                Hide(); DesktopNative.SetParent(Handle, oldParent); DesktopNative.SetWindowLongPtr(Handle, -16, originalStyle);
            }
            Close();
        }
    }
}

internal static class DesktopNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rectangle { public int Left, Top, Right, Bottom; }
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
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    internal static string ClassName(nint hwnd) { var buffer = new StringBuilder(256); GetClassName(hwnd, buffer, buffer.Capacity); return buffer.ToString(); }
    internal static nint FindDesktopParent()
    {
        nint parent = 0;
        EnumWindows((handle, _) =>
        {
            if (ClassName(handle) is "Progman" or "WorkerW") parent = FindWindowEx(handle, 0, "SHELLDLL_DefView", null);
            return parent == 0;
        }, 0);
        return parent;
    }
}
