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
using MessagePurify.Infrastructure.Tasks;
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
        await WaitAsync(window, "document.querySelector('#time-form [name=title]') !== null");
        var editedDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddDays(4).ToString("yyyy-MM-dd");
        var editFields = JsonSerializer.Serialize(new { title = "修订后的数学报告", course = "数学实验",
            date = editedDate, time = "18:30", timeKind = "start", priority = "3" });
        await EvaluateAsync(window, "Object.entries(" + editFields + ").forEach(([name,value])=>{const control=document.querySelector('#time-form [name='+name+']');control.value=value;control.dispatchEvent(new Event('input',{bubbles:true}));control.dispatchEvent(new Event('change',{bubbles:true}));});document.querySelector('#time-form').requestSubmit()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).Title == "修订后的数学报告");
        var edited = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id);
        if (edited.Course != "数学实验" || edited.Due != editedDate || edited.Time != "18:30" || edited.TimeKind != "start"
            || edited.Priority != 3 || edited.Version != task.Version + 3 || edited.Evidence.Count != task.Evidence.Count)
            throw new InvalidOperationException("完整编辑未保存全部字段或丢失了原始证据。");
        var editedReopened = new AccountWorkspace(dataRoot, workspace.AccountId).Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id);
        if (editedReopened.Title != edited.Title || editedReopened.Course != edited.Course || editedReopened.Due != edited.Due
            || editedReopened.Time != edited.Time || editedReopened.TimeKind != edited.TimeKind || editedReopened.Priority != 3)
            throw new InvalidDataException("重开工作区没有保留完整编辑。");
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Any(i => i.Id == task.Id && i.Title == edited.Title
            && i.Course == edited.Course && i.Due == edited.Due && i.Time == edited.Time && i.TimeKind == "start" && i.Priority == 3) == true);
        await WaitAsync(window, "document.querySelector('.change-history')?.textContent.includes('修改任务') && document.querySelector('.change-history').textContent.includes('修订后的数学报告')");
        await WaitAsync(() => !workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).Unread);
        if (workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).ChangeLabel is not null)
            throw new InvalidOperationException("已展示的编辑历史没有清除变化标记。");
        // An incoming task change must not silently upgrade the version of the form draft.
        workspace.Execute(new(workspace.AccountId, "edit-task", task.Id, "native-concurrent-edit", edited.Version,
            Date: editedDate, Time: "18:30", TimeKind: "start", Title: "同步更新的数学报告", Course: "数学实验", Priority: 3), DateTimeOffset.UtcNow);
        runtime.Refresh();
        await WaitAsync(window, "document.querySelector('.detail-title').textContent === '同步更新的数学报告'");
        await EvaluateAsync(window, "document.querySelector('#time-form').requestSubmit()");
        await WaitAsync(window, "document.querySelector('#detail-error').textContent.includes('草稿') && document.querySelector('[data-action=refresh-edit]').hidden === false");
        if (workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).Title != "同步更新的数学报告"
            || await EvaluateAsync(window, "document.querySelector('#time-form [name=title]').value === '修订后的数学报告'") != "true")
            throw new InvalidOperationException("版本冲突覆盖了正式任务或丢失了编辑草稿。");
        await EvaluateAsync(window, "document.querySelector('[data-action=refresh-edit]').click()");
        await WaitAsync(window, "document.querySelector('#detail-error').hidden === true");
        await EvaluateAsync(window, "document.querySelector('#time-form').requestSubmit()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).Title == edited.Title);
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=pending]').click()");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(review.Id) + "]').click()");
        await WaitAsync(window, "document.querySelector('#review-form [name=date]') !== null");
        var emptyDate = await EvaluateAsync(window, "document.querySelector('#review-form [name=date]').value === ''");
        if (emptyDate != "true") throw new InvalidOperationException("未确认建议不应成为正式日期。");
        var due = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddDays(3).ToString("yyyy-MM-dd");
        await EvaluateAsync(window, "document.querySelector('#review-form [name=date]').value=" + JsonSerializer.Serialize(due) + ";document.querySelector('#review-form [name=date]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#pending-confirm-button').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count(i => i.Kind == "task") == 2);
        // Save a synthetic quoted requirement while no task detail is visible, then inspect its desktop label.
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
        var acceptedTask = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Kind == "task" && i.Id != task.Id);
        var newRequirementDate = DateOnly.Parse(acceptedTask.Due!, System.Globalization.CultureInfo.InvariantCulture);
        var requirementAt = DateTimeOffset.UtcNow.AddSeconds(5);
        var requirementKey = new MessageKey(workspace.AccountId, "10001", "3");
        workspace.Enqueue(new(requirementKey, "10002", requirementAt,
            [new(SegmentKind.Text, $"数学报告新增要求：{newRequirementDate.Month}月{newRequirementDate.Day}日之前补交实验数据"),
                new(SegmentKind.Quote, "原通知", new(workspace.AccountId, "10001", "2"))]),
            new HashSet<string> { "10001" }, MessageOrigin.Realtime, "synthetic-new-requirement", requirementAt);
        var taskRecord = new AccountTaskStore(dataRoot, workspace.AccountId).FindTask(acceptedTask.Id)
            ?? throw new InvalidDataException("合成任务未保存。");
        var reliable = new RuleAssessments(new("明确新要求", true), new("适用于本人", true),
            new("课程已确认", true), new("正式时间明确", true), new("原消息可核对", true), new("引用明确", true));
        new AccountTaskStore(dataRoot, workspace.AccountId).SavePending("synthetic-new-requirement-review",
            new RuleCandidate(taskRecord.Title, RuleAction.NewRequirement, RuleDisposition.Change, taskRecord.Course,
                taskRecord.Time, null, taskRecord.Id, taskRecord.Priority, reliable, [], [requirementKey]),
            "synthetic-new-requirement-review-operation", requirementAt);
        var newRequirement = workspace.Snapshot(requirementAt).Items.Single(i => i.Id == acceptedTask.Id);
        if (newRequirement.ChangeLabel != "待确认变更" || !newRequirement.Unread || newRequirement.Title != acceptedTask.Title)
            throw new InvalidOperationException("合成新要求没有保留稳定标题或产生未查看标记。");
        runtime.Refresh();
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Any(i => i.Id == acceptedTask.Id && i.ChangeLabel == "待确认变更") == true);
        await WaitAsync(() => Descendants(desktop.TaskWindow).OfType<System.Windows.Controls.TextBlock>().Any(text => text.Text == "待确认变更"));
        FindButton(desktop.TaskWindow, "open:" + acceptedTask.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('.pending-changes')?.textContent.includes('待确认新要求') && document.querySelector('.pending-changes').textContent.includes('查看建议')");
        var pendingRequirement = workspace.Snapshot(requirementAt).Items.Single(i => i.Kind == "review" && i.Id == "synthetic-new-requirement-review");
        workspace.Execute(new(workspace.AccountId, "accept-review", pendingRequirement.Id, "synthetic-accept-new-requirement",
            pendingRequirement.Version, Date: acceptedTask.Due, Time: acceptedTask.Time, TimeKind: acceptedTask.TimeKind,
            TargetTaskVersion: newRequirement.Version), requirementAt.AddSeconds(1));
        runtime.Refresh();
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Any(i => i.Id == acceptedTask.Id && i.ChangeLabel == "新要求") == true);
        FindButton(desktop.TaskWindow, "open:" + acceptedTask.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('.change-history')?.textContent.includes('确认新要求') && document.querySelector('.change-history').textContent.includes('补交实验数据')");
        await WaitAsync(() => !workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == acceptedTask.Id).Unread);
        await WaitAsync(() => desktop.Snapshot?.Tasks.Any(i => i.Id == acceptedTask.Id && i.ChangeLabel is null) == true);
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
        var resizeThumb = FindThumb(desktop.TaskWindow);
        resizeThumb.RaiseEvent(new DragDeltaEventArgs(20, 12));
        resizeThumb.RaiseEvent(new DragCompletedEventArgs(20, 12, false));
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
                "desktop-data", "desktop-theme", "desktop-open-item", "desktop-complete", "desktop-layout", "pending-hide",
                "edit-task", "edit-task-reopen", "edit-task-desktop", "edit-task-conflict",
                "change-history", "change-read", "desktop-new-requirement", "desktop-change-read" },
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
    private static Thumb FindThumb(Window window) => Descendants(window).OfType<Thumb>()
        .Single(thumb => Equals(thumb.ToolTip, "拖动调整卡片大小"));

    public static async Task RunReadOnlyAsync(AppRuntime runtime, string reportPath, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var snapshot = runtime.Workspace?.Snapshot(DateTimeOffset.UtcNow);
        var status = runtime.Status;
        var success = snapshot is not null && status.Transport == "connected" && status.Qq == "online";
        var safeStatus = new { status.Transport, status.Qq, status.Receiving, status.History, status.Error,
            status.Scheduled, status.LastSuccessfulSyncAt, status.NextScheduledSyncAt,
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
