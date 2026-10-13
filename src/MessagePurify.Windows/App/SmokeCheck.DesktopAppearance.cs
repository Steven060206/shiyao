using System.IO;
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

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static readonly string[] DesktopAppearanceChecks = ["desktop-dark-translucent", "desktop-light-translucent",
        "desktop-rounded-transparent-corners", "desktop-empty-compact", "desktop-content-height-restored",
        "desktop-slim-scrollbar", "desktop-theme-scroll-preserved", "desktop-snap-horizontal", "desktop-snap-vertical-compact",
        "desktop-snap-detach", "desktop-snap-layout-reopen", "desktop-hidden-card-no-snap",
        "desktop-transparency-page", "desktop-transparency-text", "desktop-transparency-reopen", "desktop-transparency-backup"];

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
            if (surface.ActualHeight > 155 || surface.ActualHeight < 100 || desktop.Layout != originalLayout)
                throw new InvalidOperationException("空卡片未收紧，或改变了原布局偏好。");
            var emptyPixels = new RenderTargetBitmap((int)card.ActualWidth, (int)card.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            emptyPixels.Render(card);
            emptyPixels.CopyPixels(new Int32Rect((int)card.ActualWidth / 2, (int)card.ActualHeight - 10, 1, 1), corner, 4, 0);
            if (corner[3] != 0) throw new InvalidOperationException("空卡片下方仍留下矩形背景。");
            var captureRoot = Path.GetDirectoryName(Path.GetFullPath(reportFile))!;
            desktop.Capture(Path.Combine(captureRoot, "appearance-empty-dark.png"));
            workspace.Execute(new(workspace.AccountId, "create-task", "appearance-manual", "appearance-create", 0,
                Date: DateTimeOffset.UtcNow.AddDays(3).ToString("yyyy-MM-dd"), TimeKind: "deadline", Course: "数学", Title: "完成课程报告"), DateTimeOffset.UtcNow);
            for (var index = 0; index < 8; index++)
                workspace.Execute(new(workspace.AccountId, "create-task", "appearance-extra-" + index, "appearance-add-" + index, 0,
                    Date: DateTimeOffset.UtcNow.AddDays(4).ToString("yyyy-MM-dd"), TimeKind: "deadline", Course: "数学",
                    Title: "第" + (index + 1) + "项课程任务"), DateTimeOffset.UtcNow);
            runtime.Refresh(); await desktop.RefreshAsync(); card.UpdateLayout();
            if (surface.ActualHeight < 200 || !double.IsNaN(surface.Height) || desktop.Layout != originalLayout)
                throw new InvalidOperationException("新事项出现后未恢复原列表高度。");
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
            new(anchor.X + anchor.Width, anchor.Y), new(anchor.X - initial.Pending.Width, anchor.Y),
            new(anchor.X, anchor.Y - initial.Pending.Height)
        })
        {
            desktop.MoveCard(initial.Pending with { X = target.X + 4 * scale, Y = target.Y + 4 * scale }, true);
            if (desktop.Layout.Pending != initial.Pending with { X = target.X, Y = target.Y } || desktop.Layout.Tasks != anchor)
                throw new InvalidOperationException("实际卡片未吸附到相邻边缘，或改变了另一张卡片。");
        }
        desktop.Capture(Path.Combine(captureRoot, "snap-horizontal-and-above.png"));
        foreach (var task in workspace.Snapshot(DateTimeOffset.UtcNow).Items.Where(item => item.Kind == "task" && item.State == "active"))
            workspace.Execute(new(workspace.AccountId, "complete", task.Id, "snap-complete-" + task.Id, task.Version), DateTimeOffset.UtcNow);
        runtime.Refresh(); await desktop.RefreshAsync(); desktop.TaskWindow.UpdateLayout();
        var surface = (Border)desktop.TaskWindow.Content;
        var bottom = anchor.Y + (surface.ActualHeight + 10) * scale;
        desktop.MoveCard(initial.Pending with { X = anchor.X + 4 * scale, Y = bottom + 4 * scale }, true);
        var snapped = initial.Pending with { X = anchor.X, Y = bottom };
        if (desktop.Layout.Pending != snapped || surface.ActualHeight > 155 || desktop.Layout.Tasks != anchor)
            throw new InvalidOperationException("上下吸附没有使用紧凑卡片的可见高度。");
        desktop.SaveLayout(true);
        if (new DesktopLayoutStore(root).Load(new DesktopRect(0, 0, 100000, 100000), scale) != desktop.Layout)
            throw new InvalidOperationException("吸附位置重读后改变。");
        desktop.Capture(Path.Combine(captureRoot, "snap-vertical-compact.png"));
        var detached = snapped with { Y = snapped.Y + 64 * scale };
        desktop.MoveCard(detached, true);
        if (desktop.Layout.Pending != detached) throw new InvalidOperationException("拖离后仍被强制吸附。");
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
            await ChangeSettingAsync(window, "theme-setting", "dark");
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
        }
        finally { reopened.AppearanceChanged -= desktop.SetAppearance; reopened.Exiting = true; reopened.Close(); }
    }

    private static Task ChangeTransparencyAsync(AppWindow window, string value) => EvaluateAsync(window,
        $"document.querySelector('#desktop-transparency-setting').value='{value}';document.querySelector('#desktop-transparency-setting').dispatchEvent(new Event('input',{{bubbles:true}}))");
}
