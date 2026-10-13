using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using MessagePurify.Infrastructure.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Orientation = System.Windows.Controls.Orientation;

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static readonly string[] DesktopAppearanceChecks = ["desktop-dark-translucent", "desktop-light-translucent",
        "desktop-rounded-transparent-corners", "desktop-empty-compact", "desktop-content-height-restored",
        "desktop-slim-scrollbar", "desktop-theme-scroll-preserved"];

    private static bool HasDesktopTheme(Window window, bool dark) => window.AllowsTransparency
        && window.Background is SolidColorBrush backdrop && backdrop.Color.A == 0
        && window.Content is Border { Background: SolidColorBrush surface }
        && surface.Color == (Color)ColorConverter.ConvertFromString(dark ? "#CD182532" : "#DEF4F7FA");

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
        }
        finally { await runtime.StopAsync(); }
    }
}
