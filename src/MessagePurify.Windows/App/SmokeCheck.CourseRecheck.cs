using System.IO;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static readonly string[] CourseRecheckChecks = ["course-recheck-settings", "course-recheck-scope",
        "course-recheck-task-preserved", "course-recheck-conflict-card", "course-recheck-open-original",
        "course-recheck-reopen"];

    private static async Task CheckCourseRecheckAsync(string dataRoot, string reportFile)
    {
        var root = Path.Combine(dataRoot, "course-recheck");
        var workspace = new AccountWorkspace(root, "10000");
        var now = DateTimeOffset.UtcNow;
        var due = now.ToOffset(TimeSpan.FromHours(8)).AddDays(2);
        HashSet<string> pinned = ["10001"];
        workspace.SetAvailableGroups([new OneBotGroup("10001", "合成课程重查群")], pinned);
        workspace.Execute(new(workspace.AccountId, "confirm-group", "10001", "native-recheck-initial-course", 0,
            Course: "数学"), now);
        var text = $"请于{due.Month}月{due.Day}日之前提交实验报告";
        workspace.Ingest(new(new(workspace.AccountId, "10001", "recheck-original"), "10002", now,
            [new(SegmentKind.Text, text)]), pinned, MessageOrigin.Realtime, "synthetic-course-recheck", now);
        var original = workspace.Snapshot(now).Items.Single(item => item.Kind == "task");
        workspace.Execute(new(workspace.AccountId, "edit-task", original.Id, "native-recheck-manual-edit", original.Version,
            Date: original.Due, TimeKind: "deadline", Title: "人工保留的实验报告", Course: "数学", Priority: 2), now);
        var store = new AccountTaskStore(root, workspace.AccountId);
        var before = JsonSerializer.Serialize(store.FindTask(original.Id));
        var preserved = workspace.Snapshot(now).Items.Single(item => item.Id == original.Id);
        var runtime = new AppRuntime(root, workspace);
        var window = new AppWindow(runtime, root, hidden: true, registerStartup: false);
        using var desktop = new DesktopCardHost(runtime, root, window.OpenItemAsync);
        try
        {
            window.Show();
            await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitAsync(window, "snapshot !== null && initialized");
            await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
            await WaitAsync(window, "document.querySelector('.group-setting[data-group-id=\"10001\"] [name=course]') !== null");
            await EvaluateAsync(window, "(() => {const group=document.querySelector('.group-setting[data-group-id=\"10001\"]');"
                + "const input=group.querySelector('[name=course]');input.value='物理';"
                + "input.dispatchEvent(new Event('input',{bubbles:true}));group.querySelector('[data-action=confirm-group]').click();})()");
            await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).CourseRecheck is { Course: "物理" });
            await WaitAsync(window, "document.querySelector('#course-recheck-result')?.textContent.includes('1')"
                + " && ![...requests.values()].some(item => item.operation === 'confirm-group')");
            var checkedSnapshot = workspace.Snapshot(DateTimeOffset.UtcNow);
            var result = checkedSnapshot.CourseRecheck;
            if (checkedSnapshot.CourseRecheckError is not null || result is null
                || result.AccountId != workspace.AccountId || result.GroupId != "10001"
                || result.Course != "物理" || result.Excluded || result.SkippedUnpinned
                || result.CheckedMessages != 1 || result.CheckedTasks != 1 || result.AddedReviews != 1
                || result.AddedTasks != 0 || result.AddedNotices != 0 || result.MissingEvidenceMessages != 0
                || result.FirstMessageAt != now || result.LastMessageAt != now)
                throw new InvalidDataException("课程重查结果未准确展示本地实际检查范围与新增建议。");
            if (JsonSerializer.Serialize(store.FindTask(original.Id)) != before
                || checkedSnapshot.Items.Count(item => item.Kind == "task") != 1)
                throw new InvalidDataException("课程关联修改覆盖了人工修正或重复创建了已有任务。");
            var conflict = checkedSnapshot.Items.Single(item => item.Kind == "review" && item.State == "pending");
            if (!conflict.IsCourseConflict || conflict.CanAccept || conflict.CanAcceptNotice
                || conflict.TargetTaskId != original.Id || !conflict.Evidence.SequenceEqual(original.Evidence))
                throw new InvalidDataException("课程冲突必须保留原证据并要求核对，不能提供加入任务或通知。");
            await desktop.RefreshAsync();
            if (desktop.Snapshot?.Tasks.Count != 1 || desktop.Snapshot.Pending.Count != 1
                || desktop.Snapshot.Tasks[0].Id != original.Id || desktop.Snapshot.Pending[0].Id != conflict.Id)
                throw new InvalidOperationException("课程重查后桌面未同时保留原任务和显示待确认建议。");
            await window.OpenItemAsync(workspace.AccountId, conflict.Id);
            await WaitAsync(window, "document.querySelector('[data-action=open-target]') !== null"
                + " && document.querySelector('[data-action=reject]')?.textContent.includes('保留原任务')"
                + " && document.querySelector('[data-action=ignore]') !== null"
                + " && document.querySelector('#review-form') === null"
                + " && document.querySelector('#pending-confirm-button') === null");
            window.ShowMainWindow();
            await using (var image = File.Create(Path.ChangeExtension(reportFile, ".course-recheck.png")))
                await window.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            await EvaluateAsync(window, "document.querySelector('[data-action=open-target]').click()");
            await WaitAsync(window, "document.querySelector('.task-row.selected')?.dataset.id === " + JsonSerializer.Serialize(original.Id)
                + " && document.querySelector('.detail-title')?.textContent === " + JsonSerializer.Serialize(preserved.Title)
                + " && document.querySelector('#time-form [name=course]')?.value === '数学'"
                + " && document.querySelector('.source-section .message')?.textContent === " + JsonSerializer.Serialize(text));
            if (JsonSerializer.Serialize(store.FindTask(original.Id)) != before || runtime.Status.SyncPhase != "idle")
                throw new InvalidDataException("查看原任务不能修改任务或触发 QQ 同步。");
        }
        finally { window.Exiting = true; window.Close(); await runtime.StopAsync(); }

        var reopened = new AccountWorkspace(root, workspace.AccountId).Snapshot(DateTimeOffset.UtcNow);
        if (JsonSerializer.Serialize(store.FindTask(original.Id)) != before
            || reopened.Items.Count(item => item.Kind == "task") != 1
            || reopened.Items.Single(item => item.Kind == "review") is not { IsCourseConflict: true, State: "pending" })
            throw new InvalidDataException("重开工作区丢失了原任务或课程冲突建议。");
    }
}
