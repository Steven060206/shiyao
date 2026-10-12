using System.IO;
using System.Text.Json;
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

    public static async Task RunAsync(AppWindow window, AccountWorkspace workspace, string dataRoot, string reportPath)
    {
        var initial = workspace.Snapshot(DateTimeOffset.UtcNow);
        var task = initial.Items.Single(i => i.Kind == "task");
        var review = initial.Items.Single(i => i.Kind == "review" && i.State == "pending");
        await WaitAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]') !== null");
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
        await WaitAsync(window, "document.querySelector('#reminder-form [name=reminderMode]') !== null");
        await EvaluateAsync(window, "document.querySelector('#reminder-form [name=reminderMode]').value='calendar';document.querySelector('#reminder-form [name=reminderMode]').dispatchEvent(new Event('change',{bubbles:true}))");
        await WaitAsync(window, "document.querySelector('#reminder-form [name=reminderDays]') !== null");
        await EvaluateAsync(window, "document.querySelector('#reminder-form [name=reminderDays]').value='1';document.querySelector('#reminder-form [name=reminderDays]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#reminder-form [name=reminderClock]').value='20:00';document.querySelector('#reminder-form [name=reminderClock]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-action=set-reminder]').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Reminders.Single(i => i.TaskId == task.Id).Preference.Clock == "20:00");
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
        var reopenedReminder = reopened.Snapshot(DateTimeOffset.UtcNow).Reminders.Single(i => i.TaskId == task.Id);
        if (reopenedReminder.Preference.Mode != "calendar" || reopenedReminder.Preference.Days != 1
            || reopenedReminder.Preference.Clock != "20:00")
            throw new InvalidDataException("重开工作区没有保留自定义提醒。");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]').click()");
        await WaitAsync(window, "document.querySelectorAll('.task-row').length === 2");
        var reportFile = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);
        var capture = Path.ChangeExtension(reportFile, ".png");
        await using (var image = File.Create(capture)) await window.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        File.WriteAllText(reportFile, JsonSerializer.Serialize(new { success = true, synthetic = true, checks = new[] { "snapshot", "complete", "undo-complete", "explicit-date", "accept-review", "reminder-settings", "native-bridge", "reopen" }, taskCount = 2, capture }, AppWindow.JsonOptions));
    }

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
