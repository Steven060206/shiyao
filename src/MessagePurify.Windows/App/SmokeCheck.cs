using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using MessagePurify.Windows.IntegrationProbe;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using Microsoft.Web.WebView2.Core;

namespace MessagePurify.Windows.App;

/// <summary>仅验证模式使用合成账号，正式启动绝不载入样例。</summary>
internal static class SmokeCheck
{
    public static AccountWorkspace CreateWorkspace(string dataRoot)
    {
        var workspace = new AccountWorkspace(dataRoot, "10000");
        var now = DateTimeOffset.UtcNow;
        HashSet<string> pinned = ["10001"];
        workspace.SetAvailableGroups([new OneBotGroup("10001", "合成课程群")], pinned);
        workspace.Execute(new("10000", "confirm-group", "10001", "setup-course", 0, Course: "数学"), now);
        var due = now.ToOffset(TimeSpan.FromHours(8)).AddDays(2);
        workspace.Ingest(new(new("10000", "10001", "1"), "10002", now,
            [new(SegmentKind.Text, $"{due.Month}月{due.Day}日之前提交数学作业")]), pinned, MessageOrigin.Realtime, "synthetic", now);
        workspace.Ingest(new(new("10000", "10001", "2"), "10002", now.AddSeconds(1),
            [new(SegmentKind.Text, "请提交数学报告")]), pinned, MessageOrigin.Realtime, "synthetic", now);
        return workspace;
    }

    public static async Task RunAsync(AppWindow window, AccountWorkspace workspace, AppRuntime runtime,
        DesktopCardHost desktop, string dataRoot, string reportPath)
    {
        var initial = workspace.Snapshot(DateTimeOffset.UtcNow);
        var task = initial.Items.Single(i => i.Kind == "task");
        var review = initial.Items.Single(i => i.Kind == "review" && i.State == "pending");
        await WaitAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]') !== null");
        await desktop.RefreshAsync();
        if (desktop.State != "attached" || desktop.Snapshot?.Tasks.Count != 1 || desktop.Snapshot.Pending.Count != 1)
            throw new InvalidOperationException("正式桌面未挂载或没有显示合成事项。");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
        await WaitAsync(window, "document.querySelector('#theme-setting') !== null");
        await EvaluateAsync(window, "document.querySelector('#theme-setting').value='dark';document.querySelector('#theme-setting').dispatchEvent(new Event('change',{bubbles:true}))");
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.TaskWindow.Background is SolidColorBrush brush && brush.Color == Colors.Black);
        var reportFile = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);
        var cardsCapture = Path.Combine(Path.GetDirectoryName(reportFile)!, Path.GetFileNameWithoutExtension(reportFile) + "-cards.png");
        desktop.Capture(cardsCapture);
        FindButton(desktop.PendingWindow, "open:" + review.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('#review-form') !== null");
        FindButton(desktop.TaskWindow, "open:" + task.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('[data-action=complete]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]').click()");
        await WaitAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]') !== null");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]').click()");
        await WaitAsync(window, "document.querySelector('[data-action=complete]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=complete]').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).State == "completed");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=completed]').click()");
        await WaitAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]') !== null");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]').click()");
        await WaitAsync(window, "document.querySelector('[data-action=undo-complete]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=undo-complete]').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).State == "active");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]').click()");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]').click()");
        if (await EvaluateAsync(window, "document.querySelector('#reminder-form') === null") != "true")
            throw new InvalidOperationException("已取消的系统提醒设置仍在页面中。");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=pending]').click()");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(review.Id) + "]').click()");
        await WaitAsync(window, "document.querySelector('#review-form [name=date]') !== null");
        var emptyDate = await EvaluateAsync(window, "document.querySelector('#review-form [name=date]').value === ''");
        if (emptyDate != "true") throw new InvalidOperationException("未确认建议不应成为正式日期。");
        var due = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddDays(3).ToString("yyyy-MM-dd");
        await EvaluateAsync(window, "document.querySelector('#review-form [name=date]').value=" + JsonSerializer.Serialize(due) + ";document.querySelector('#review-form [name=date]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#pending-confirm-button').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count(i => i.Kind == "task") == 2);
        var reopened = new AccountWorkspace(dataRoot, workspace.AccountId);
        if (reopened.Snapshot(DateTimeOffset.UtcNow).Items.Count(i => i.Kind == "task") != 2)
            throw new InvalidDataException("重开工作区没有保留事项。");
        runtime.Refresh();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == 2 && !desktop.PendingWindow.IsVisible);
        FindButton(desktop.TaskWindow, "complete:" + task.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).State == "completed");
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == 1);
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=completed]').click()");
        await WaitAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]') !== null");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]').click()");
        await WaitAsync(window, "document.querySelector('[data-action=undo-complete]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=undo-complete]').click()");
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == 2);
        // Exercise the actual resize event and persisted layout, without moving the user's pointer.
        var saved = desktop.Layout;
        FindThumb(desktop.TaskWindow).RaiseEvent(new DragDeltaEventArgs(20, 12));
        FindThumb(desktop.TaskWindow).RaiseEvent(new DragCompletedEventArgs(20, 12, false));
        var resized = desktop.Layout;
        if (resized.Tasks.Width <= saved.Tasks.Width || resized.Tasks.Height <= saved.Tasks.Height)
            throw new InvalidOperationException("正式卡片缩放事件未更新窗口。");
        var savedLayout = new DesktopLayoutStore(dataRoot).Load(new(0, 0, 100000, 100000),
            DesktopNative.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(desktop.TaskWindow).Handle) / 96d);
        if (savedLayout != resized) throw new InvalidOperationException("正式卡片布局没有持久化。");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]').click()");
        await WaitAsync(window, "document.querySelectorAll('.task-row').length === 2");
        var capture = Path.ChangeExtension(reportFile, ".png");
        await using (var image = File.Create(capture)) await window.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        File.WriteAllText(reportFile, JsonSerializer.Serialize(new { success = true, synthetic = true,
            checks = new[] { "snapshot", "complete", "undo-complete", "explicit-date", "accept-review", "native-bridge", "reopen",
                "desktop-data", "desktop-theme", "desktop-open-item", "desktop-complete", "desktop-layout", "pending-hide" },
            taskCount = 2, capture, cardsCapture, desktop = desktop.Inspect() }, AppWindow.JsonOptions));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static System.Windows.Controls.Button FindButton(Window window, string tag) => Descendants(window)
        .OfType<System.Windows.Controls.Button>().Single(button => Equals(button.Tag, tag));
    private static Thumb FindThumb(Window window) => Descendants(window).OfType<Thumb>().First();

    public static async Task RunReadOnlyAsync(AppRuntime runtime, string reportPath, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var snapshot = runtime.Workspace?.Snapshot(DateTimeOffset.UtcNow);
        var status = runtime.Status;
        var success = snapshot is not null && status.Transport == "connected" && status.Qq == "online";
        var safeStatus = new { status.Transport, status.Qq, status.Receiving, status.History, status.Error,
            failures = status.HistoryFailures.Select(f => f.Reason).ToArray() };
        var reportFile = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);
        File.WriteAllText(reportFile, JsonSerializer.Serialize(new { success, synthetic = false, status = safeStatus,
            groups = snapshot?.Groups.Count, pinnedGroups = snapshot?.Groups.Count(g => g.Pinned), items = snapshot?.Items.Count,
            tasks = snapshot?.Items.Count(i => i.Kind == "task"), pending = snapshot?.Items.Count(i => i.Kind == "review"),
            notices = snapshot?.Items.Count(i => i.Kind == "notice") }, AppWindow.JsonOptions));
        if (!success) throw new InvalidOperationException("真实只读连接未通过。");
    }

    private static Task<string> EvaluateAsync(AppWindow window, string script) => window.Browser.ExecuteScriptAsync(script);
    private static async Task WaitAsync(AppWindow window, string expression)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await EvaluateAsync(window, expression) == "true") return;
            await Task.Delay(100);
        }
        throw new TimeoutException("本地页面检查超时。");
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("正式存储检查超时。");
    }
}
