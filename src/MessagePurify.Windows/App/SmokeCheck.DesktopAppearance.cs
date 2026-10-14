using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Windows.IntegrationProbe;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static readonly string[] DesktopAppearanceChecks = ["desktop-dark-translucent", "desktop-light-translucent",
        "desktop-rounded-transparent-corners", "desktop-empty-hidden", "desktop-empty-recovery-hidden", "desktop-content-visible-restored",
        "desktop-slim-scrollbar", "desktop-theme-scroll-preserved", "desktop-snap-horizontal", "desktop-snap-vertical", "desktop-hidden-task-no-snap",
        "desktop-snap-detach", "desktop-snap-layout-reopen", "desktop-hidden-card-no-snap",
        "desktop-transparency-page", "desktop-transparency-text", "desktop-transparency-reopen", "desktop-transparency-backup",
        "main-titlebar-dark", "main-titlebar-light", "main-titlebar-reopen", "main-native-window-controls"];

    private static bool HasDesktopTheme(Window window, bool dark, int? transparency = null)
    {
        var expected = (Color)ColorConverter.ConvertFromString(dark ? "#CD182532" : "#DEF4F7FA");
        if (transparency is { } value) expected.A = (byte)Math.Round(255 * (100 - value) / 100d);
        return window.AllowsTransparency && window.Background is SolidColorBrush backdrop && backdrop.Color.A == 0
            && window.Content is Border { Background: SolidColorBrush surface } && surface.Color == expected;
    }

    private static async Task CheckDesktopAppearanceAsync(string dataRoot, string reportFile)
    {
        var appearanceRoot = Path.Combine(dataRoot, "desktop-appearance");
        var workspace = CreateWorkspace(appearanceRoot);
        var runtime = new AppRuntime(appearanceRoot, workspace);
        using var desktop = new DesktopCardHost(runtime, appearanceRoot, (_, _) => Task.CompletedTask);
        try
        {
            desktop.SetAppearance("dark", true);
            await desktop.RefreshAsync();
            var card = desktop.TaskWindow;
            card.UpdateLayout();
            if (!HasDesktopTheme(card, true)
                || ((long)DesktopNative.GetWindowLongPtr(new WindowInteropHelper(card).Handle, -20) & 0x80000L) == 0)
                throw new InvalidOperationException("深色卡片未使用透明分层窗口。");
            var surface = (Border)card.Content;
            if (surface.CornerRadius.TopLeft != 16) throw new InvalidOperationException("卡片圆角未呈现。");
            var pixels = new RenderTargetBitmap((int)card.ActualWidth, (int)card.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            pixels.Render(card);
            var corner = new byte[4]; pixels.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
            if (corner[3] >= 32) throw new InvalidOperationException("圆角外侧仍被不透明背景填满。");
            var originalLayout = desktop.Layout;
            var task = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Kind == "task");
            workspace.Execute(new(workspace.AccountId, "complete", task.Id, "appearance-complete", task.Version), DateTimeOffset.UtcNow);
            runtime.Refresh(); await desktop.RefreshAsync(); card.UpdateLayout();
            if (card.IsVisible || IsAppearanceWindowVisible(new WindowInteropHelper(card).Handle)
                || !desktop.PendingWindow.IsVisible || desktop.Layout != originalLayout)
                throw new InvalidOperationException("完成最后一项后提醒卡片未隐藏，或改变了待确认与布局。");
            card.Close(); await desktop.RefreshAsync();
            card = desktop.TaskWindow; surface = (Border)card.Content;
            if (card.IsVisible || IsAppearanceWindowVisible(new WindowInteropHelper(card).Handle) || desktop.Layout != originalLayout)
                throw new InvalidOperationException("无待办时重建桌面卡片后仍显示空卡片。");
            var captureRoot = Path.GetDirectoryName(Path.GetFullPath(reportFile))!;
            workspace.Execute(new(workspace.AccountId, "create-task", "appearance-manual", "appearance-create", 0,
                Date: DateTimeOffset.UtcNow.AddDays(3).ToString("yyyy-MM-dd"), TimeKind: "deadline", Course: "数学", Title: "完成课程报告"), DateTimeOffset.UtcNow);
            for (var index = 0; index < 8; index++)
                workspace.Execute(new(workspace.AccountId, "create-task", "appearance-extra-" + index, "appearance-add-" + index, 0,
                    Date: DateTimeOffset.UtcNow.AddDays(4).ToString("yyyy-MM-dd"), TimeKind: "deadline", Course: "数学",
                    Title: "第" + (index + 1) + "项课程任务"), DateTimeOffset.UtcNow);
            runtime.Refresh(); await desktop.RefreshAsync(); card.UpdateLayout();
            if (!card.IsVisible || !IsAppearanceWindowVisible(new WindowInteropHelper(card).Handle)
                || surface.ActualHeight < 200 || !double.IsNaN(surface.Height) || desktop.Layout != originalLayout)
                throw new InvalidOperationException("新事项出现后未恢复显示或原列表尺寸。");
            var scroll = Descendants(card).OfType<ScrollViewer>().Single();
            scroll.ScrollToVerticalOffset(90);
            await WaitAsync(() => scroll.VerticalOffset > 0);
            var offset = scroll.VerticalOffset;
            var bar = Descendants(card).OfType<System.Windows.Controls.Primitives.ScrollBar>().Single(item => item.Orientation == Orientation.Vertical);
            desktop.Capture(Path.Combine(captureRoot, "appearance-dark.png"));
            if (bar.ActualWidth > 8 || Descendants(bar).OfType<System.Windows.Controls.Primitives.Thumb>().Count() != 1)
                throw new InvalidOperationException($"细滚动条未加载：width={bar.ActualWidth}、style={bar.Style is not null}、thumbs={Descendants(bar).OfType<System.Windows.Controls.Primitives.Thumb>().Count()}。");
            var scrollThumb = Descendants(bar).OfType<Thumb>().Single();
            scrollThumb.RaiseEvent(new DragDeltaEventArgs(0, 10) { RoutedEvent = Thumb.DragDeltaEvent });
            await WaitAsync(() => scroll.VerticalOffset > offset);
            offset = scroll.VerticalOffset;
            desktop.SetAppearance("light", true); await desktop.RefreshAsync(); card.UpdateLayout();
            await WaitAsync(() => Math.Abs(scroll.VerticalOffset - offset) < 1);
            if (!HasDesktopTheme(card, false)) throw new InvalidOperationException("浅色透明材质未随主题更新。");
            desktop.Capture(Path.Combine(captureRoot, "appearance-light.png"));
            if (File.Exists(Path.Combine(appearanceRoot, "desktop-layout.json")))
                throw new InvalidDataException("自动空状态或主题切换写入了布局偏好。");
            await CheckDesktopControlsAsync(desktop, runtime, workspace, appearanceRoot, captureRoot);
        }
        finally { await runtime.StopAsync(); }
    }

    private static async Task CheckDesktopControlsAsync(DesktopCardHost desktop, AppRuntime runtime,
        AccountWorkspace workspace, string root, string captureRoot)
    {
        var initial = desktop.Layout;
        var scale = DesktopNative.GetDpiForWindow(DesktopNative.GetParent(new WindowInteropHelper(desktop.TaskWindow).Handle)) / 96d;
        var anchor = initial.Tasks with { X = initial.Tasks.Width + 5 * scale, Y = initial.Pending.Height + 5 * scale };
        desktop.SetLayout(initial with { Tasks = anchor });
        foreach (var target in new DesktopPoint[]
        {
            new(anchor.X + anchor.Width - 8 * scale, anchor.Y), new(anchor.X - initial.Pending.Width + 8 * scale, anchor.Y),
            new(anchor.X, anchor.Y - initial.Pending.Height + 8 * scale), new(anchor.X, anchor.Y + anchor.Height - 8 * scale)
        })
        {
            desktop.MoveCard(initial.Pending with { X = target.X + 4 * scale, Y = target.Y + 4 * scale }, true);
            if (desktop.Layout.Pending != initial.Pending with { X = target.X, Y = target.Y } || desktop.Layout.Tasks != anchor)
                throw new InvalidOperationException("实际卡片未吸附到相邻边缘，或改变了另一张卡片。");
            CheckPanelGap(desktop, scale, Path.Combine(captureRoot, target.Y < anchor.Y ? "snap-above.png" : target.X > anchor.X ? "snap-right.png" : "snap-left.png"));
        }
        foreach (var task in workspace.Snapshot(DateTimeOffset.UtcNow).Items.Where(item => item.Kind == "task" && item.State == "active"))
            workspace.Execute(new(workspace.AccountId, "complete", task.Id, "snap-complete-" + task.Id, task.Version), DateTimeOffset.UtcNow);
        runtime.Refresh(); await desktop.RefreshAsync(); desktop.TaskWindow.UpdateLayout();
        var bottom = anchor.Y + anchor.Height - 8 * scale;
        var unsnapped = initial.Pending with { X = anchor.X + 4 * scale, Y = bottom + 4 * scale };
        desktop.MoveCard(unsnapped, true);
        if (desktop.TaskWindow.IsVisible || !desktop.PendingWindow.IsVisible
            || desktop.Layout.Pending != unsnapped || desktop.Layout.Tasks != anchor)
            throw new InvalidOperationException("隐藏的提醒事项卡片仍参与吸附，或改变了待确认。");
        desktop.SaveLayout(true);
        if (new DesktopLayoutStore(root).Load(new DesktopRect(0, 0, 100000, 100000), scale) != desktop.Layout)
            throw new InvalidOperationException("吸附位置重读后改变。");
        var detached = unsnapped with { X = unsnapped.X + 64 * scale };
        desktop.MoveCard(detached, true);
        if (desktop.Layout.Pending != detached) throw new InvalidOperationException($"拖离后位置不同：实际={desktop.Layout.Pending}，请求={detached}。");
        foreach (var review in workspace.Snapshot(DateTimeOffset.UtcNow).Items.Where(item => item.Kind == "review" && item.State == "pending"))
            workspace.Execute(new(workspace.AccountId, "ignore", review.Id, "snap-ignore-" + review.Id, review.Version), DateTimeOffset.UtcNow);
        runtime.Refresh(); await desktop.RefreshAsync();
        var free = anchor with { X = detached.X - anchor.Width + 4 * scale, Y = detached.Y + 4 * scale };
        desktop.MoveCard(free, false);
        if (desktop.PendingWindow.IsVisible || desktop.Layout.Tasks != free)
            throw new InvalidOperationException("隐藏卡片仍参与吸附。");

        var window = new AppWindow(runtime, root, true, registerStartup: false);
        window.AppearanceChanged += desktop.SetAppearance;
        try
        {
            window.Show(); await window.Ready.Task;
            await WaitAsync(window, "snapshot !== null && initialized");
            await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
            await WaitAsync(window, "document.querySelector('#desktop-transparency-setting') !== null");
            window.ShowMainWindow();
            await ChangeSettingAsync(window, "theme-setting", "light");
            await WaitAsync(window, "document.documentElement.dataset.theme === 'light' && ![...requests.values()].some(item => item.operation === 'set-appearance')");
            CaptureTitlebar(window, Path.Combine(captureRoot, "main-titlebar-light.png"), false);
            await ChangeSettingAsync(window, "theme-setting", "dark");
            await WaitAsync(window, "document.documentElement.dataset.theme === 'dark' && ![...requests.values()].some(item => item.operation === 'set-appearance')");
            CaptureTitlebar(window, Path.Combine(captureRoot, "main-titlebar-dark.png"), true);
            await ChangeTransparencyAsync(window, "60");
            await desktop.RefreshAsync();
            try { await WaitAsync(() => HasDesktopTheme(desktop.TaskWindow, true, 60) && HasDesktopTheme(desktop.PendingWindow, true, 60)); }
            catch (TimeoutException exception)
            {
                var diagnostics = await EvaluateAsync(window, "JSON.stringify({theme:document.documentElement.dataset.theme,value:document.querySelector('#desktop-transparency-setting')?.value,preferences,hostError})");
                throw new InvalidOperationException($"透明度同步失败：tasks={((SolidColorBrush)((Border)desktop.TaskWindow.Content).Background).Color}、pending={((SolidColorBrush)((Border)desktop.PendingWindow.Content).Background).Color}、page={diagnostics}", exception);
            }
            if (((SolidColorBrush)desktop.TaskWindow.Foreground).Color.A != 255 || desktop.TaskWindow.Opacity != 1)
                throw new InvalidOperationException("调节底色导致文字一起透明。");
            using var preferences = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "interface-preferences.json")));
            if (preferences.RootElement.GetProperty("desktopTransparency").GetInt32() != 60)
                throw new InvalidOperationException("桌面透明度未写入偏好。");
            await EvaluateAsync(window, "document.querySelector('#desktop-transparency-setting').scrollIntoView({block:'center'})");
            await using var imageFile = File.Create(Path.Combine(captureRoot, "transparency-settings.png"));
            await window.Browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, imageFile);
        }
        finally { window.AppearanceChanged -= desktop.SetAppearance; window.Exiting = true; window.Close(); }
        var reopened = new AppWindow(runtime, root, true, registerStartup: false);
        reopened.AppearanceChanged += desktop.SetAppearance;
        try
        {
            reopened.Show(); await reopened.Ready.Task;
            await WaitAsync(reopened, "snapshot !== null && initialized");
            await EvaluateAsync(reopened, "document.querySelector('[data-action=nav][data-page=settings]').click()");
            await WaitAsync(reopened, "document.querySelector('#desktop-transparency-setting')?.value === '60'");
            await desktop.RefreshAsync();
            await WaitAsync(() => HasDesktopTheme(desktop.TaskWindow, true, 60));
            reopened.ShowMainWindow();
            CaptureTitlebar(reopened, Path.Combine(captureRoot, "main-titlebar-reopen.png"), true);
        }
        finally { reopened.AppearanceChanged -= desktop.SetAppearance; reopened.Exiting = true; reopened.Close(); }
    }

    private static void CaptureTitlebar(AppWindow window, string capture, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (((long)DesktopNative.GetWindowLongPtr(handle, -16) & 0x00CF0000) != 0x00CF0000)
            throw new InvalidOperationException("标题栏更新移除了原生窗口控制或缩放。");
        if (!GetRecoveryWindowRect(handle, out var rect)) throw new InvalidOperationException("无法读取主窗口边界。");
        DwmFlush();
        using var pixels = new System.Drawing.Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using var graphics = System.Drawing.Graphics.FromImage(pixels);
        var dc = graphics.GetHdc();
        try { if (!PrintTitlebarWindow(handle, dc, 2)) throw new InvalidOperationException("无法截取原生标题栏。"); }
        finally { graphics.ReleaseHdc(dc); }
        pixels.Save(capture, System.Drawing.Imaging.ImageFormat.Png);
        var color = pixels.GetPixel(90, 16);
        var expected = dark ? System.Drawing.Color.Black : System.Drawing.Color.FromArgb(237, 241, 238);
        if (Math.Abs(color.R - expected.R) > 5 || Math.Abs(color.G - expected.G) > 5 || Math.Abs(color.B - expected.B) > 5)
            throw new InvalidOperationException($"主窗口标题栏未跟随主题：dark={dark}、color={color}。");
    }

    [DllImport("user32.dll", EntryPoint = "PrintWindow")]
    private static extern bool PrintTitlebarWindow(IntPtr window, IntPtr dc, uint flags);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool IsAppearanceWindowVisible(IntPtr window);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmFlush();

    private static void CheckPanelGap(DesktopCardHost desktop, double scale, string capture)
    {
        Rect Panel(Window window)
        {
            window.UpdateLayout();
            var panel = (Border)window.Content;
            return new Rect(panel.PointToScreen(new Point()), new Size(panel.ActualWidth * scale, panel.ActualHeight * scale));
        }
        var tasks = Panel(desktop.TaskWindow);
        var pending = Panel(desktop.PendingWindow);
        var gap = pending.Left > tasks.Left ? pending.Left - tasks.Right
            : pending.Left < tasks.Left ? tasks.Left - pending.Right
            : pending.Top > tasks.Top ? pending.Top - tasks.Bottom : tasks.Top - pending.Bottom;
        if (Math.Abs(gap - 2 * scale) > 1)
            throw new InvalidOperationException($"吸附后的实际面板间距不是2 DIP：{gap / scale}。");
        Rect WindowBounds(Window window) => new(window.PointToScreen(new Point()),
            new Size(window.ActualWidth * scale, window.ActualHeight * scale));
        var taskWindow = WindowBounds(desktop.TaskWindow);
        var pendingWindow = WindowBounds(desktop.PendingWindow);
        var bounds = Rect.Union(taskWindow, pendingWindow);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            foreach (var (window, rect) in new[] { (desktop.TaskWindow, taskWindow), (desktop.PendingWindow, pendingWindow) })
            {
                // Render the whole window: a VisualBrush includes effects in its bounds and stretches compact panels.
                var windowPixels = new RenderTargetBitmap((int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                windowPixels.Render(window);
                context.DrawImage(windowPixels,
                    new Rect((rect.X - bounds.X) / scale, (rect.Y - bounds.Y) / scale, rect.Width / scale, rect.Height / scale));
            }
        }
        var pixels = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        pixels.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(pixels));
        using var stream = File.Create(capture); encoder.Save(stream);
    }

    private static Task ChangeTransparencyAsync(AppWindow window, string value) => EvaluateAsync(window,
        $"document.querySelector('#desktop-transparency-setting').value='{value}';document.querySelector('#desktop-transparency-setting').dispatchEvent(new Event('input',{{bubbles:true}}))");
}
