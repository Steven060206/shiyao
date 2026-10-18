using System.IO;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using Microsoft.Web.WebView2.Core;

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static readonly string[] NoticeReviewChecks = ["notice-review-pending", "notice-review-no-date",
        "notice-review-confirm-button", "notice-review-list-source", "notice-review-desktop-hide",
        "notice-review-reopen-replay", "notice-review-convert-task",
        "notice-time-defaults", "notice-time-convert-start"];

    private static async Task CheckNoticeReviewAsync(string dataRoot, string reportFile)
    {
        var root = Path.Combine(dataRoot, "notice-review");
        var workspace = new AccountWorkspace(root, "10000");
        var now = DateTimeOffset.UtcNow;
        var sentAt = new DateTimeOffset(2026, 10, 16, 15, 43, 30, TimeSpan.FromHours(8));
        const string text = "关于合成餐饮比赛赛前交流工作的通知：\n时间：下周二(10.20)下午1点～4点\n地点：合成楼1-101";
        const string groupName = "合成合成比赛通知群";
        HashSet<string> pinned = ["10001"];
        workspace.SetAvailableGroups([new OneBotGroup("10001", groupName)], pinned);
        var original = new SchoolMessage(new(workspace.AccountId, "10001", "pending-notice"), "10002", sentAt,
            [new(SegmentKind.Text, text)]);
        workspace.Ingest(original, pinned, MessageOrigin.Realtime, "synthetic-pending-notice", now);
        var initial = workspace.Snapshot(now);
        if (initial.Items.Count != 1 || initial.Items[0] is not { Kind: "review", State: "pending",
                Action: "inform", CanAccept: false, CanAcceptNotice: true } review
            || review.Course is not null || review.Title != text
            || review.Evidence.Count != 1 || review.Evidence[0].Text != text)
            throw new InvalidOperationException("课程群尚未确认的普通通知应保留整条待确认，并提供加入通知的能力。");

        var runtime = new AppRuntime(root, workspace);
        var window = new AppWindow(runtime, root, hidden: true, registerStartup: false);
        using (var desktop = new DesktopCardHost(runtime, root, window.OpenItemAsync))
        {
            try
            {
                window.Show();
                await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await desktop.RefreshAsync();
                if (desktop.Snapshot?.Pending.Count != 1 || desktop.Snapshot.Tasks.Count != 0)
                    throw new InvalidOperationException("普通通知审核前应只出现在待确认桌面卡片。");
                await window.OpenItemAsync(workspace.AccountId, review.Id);
                await WaitAsync(window, "document.querySelector('#pending-confirm-button')?.dataset.action === 'accept-notice'"
                    + " && document.querySelector('#pending-confirm-button').textContent === '确认并加入通知'"
                    + " && !document.querySelector('#pending-confirm-button').disabled"
                    + " && document.querySelector('.source-section .message')?.textContent === " + JsonSerializer.Serialize(text)
                    + " && document.querySelector('#review-form [name=date]') === null");
                await using (var capture = File.Create(Path.ChangeExtension(reportFile, ".notice-review.png")))
                    await window.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
                await EvaluateAsync(window, "document.querySelector('#pending-confirm-button').click()");
                await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Any(item => item.Kind == "notice"));
                var accepted = workspace.Snapshot(DateTimeOffset.UtcNow);
                var notice = accepted.Items.Single();
                if (notice.Kind != "notice" || notice.Id != "notice-review-" + review.Id
                    || notice.Title != text || notice.Course is not null || notice.TargetTaskId is not null
                    || !notice.Evidence.SequenceEqual(review.Evidence))
                    throw new InvalidDataException("确认普通通知必须移出待确认、保留完整来源，并且不创建任务。");
                await WaitAsync(window, "document.querySelector('#breadcrumb')?.textContent === '通知'"
                    + " && document.querySelector('.task-row.selected')?.dataset.id === " + JsonSerializer.Serialize(notice.Id));
                await WaitAsync(window, "document.querySelector('.source-section .message')?.textContent === "
                    + JsonSerializer.Serialize(text) + " && document.querySelector('#notice-form') !== null");
                await desktop.RefreshAsync();
                if (desktop.Snapshot?.Tasks.Count != 0 || desktop.Snapshot.Pending.Count != 0
                    || desktop.TaskWindow.IsVisible || desktop.PendingWindow.IsVisible)
                    throw new InvalidOperationException("加入通知后不得出现任务桌面卡片，待确认清空后应隐藏。");
            }
            finally { window.Exiting = true; window.Close(); await runtime.StopAsync(); }
        }

        var reopened = new AccountWorkspace(root, workspace.AccountId);
        reopened.SetAvailableGroups([new OneBotGroup("10001", groupName)], pinned);
        reopened.Ingest(original, pinned, MessageOrigin.History, "synthetic-pending-notice-replay", now.AddSeconds(1));
        var restored = reopened.Snapshot(DateTimeOffset.UtcNow);
        var restoredNotice = restored.Items.Single();
        if (restoredNotice.Kind != "notice" || !restoredNotice.Evidence.SequenceEqual(review.Evidence))
            throw new InvalidDataException("普通通知重开、重放后必须保留完整原文且不重新出现待确认或任务。");
        if (restoredNotice.Due != "2026-10-20" || restoredNotice.Time != "13:00" || restoredNotice.TimeKind != "start")
            throw new InvalidDataException("通知活动时间读取错误：" + JsonSerializer.Serialize(new
                { restoredNotice.Due, restoredNotice.Time, restoredNotice.TimeKind }));
        var reopenedRuntime = new AppRuntime(root, reopened);
        var reopenedWindow = new AppWindow(reopenedRuntime, root, hidden: true, registerStartup: false);
        try
        {
            reopenedWindow.Show();
            await reopenedWindow.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            // HTML navigation completes before the initial account snapshot; let its default-page
            // initialization finish before choosing notices, as in the appearance reopen checks.
            await WaitAsync(reopenedWindow, "snapshot !== null && initialized");
            await EvaluateAsync(reopenedWindow, "document.querySelector('[data-action=nav][data-page=notices]').click()");
            await WaitAsync(reopenedWindow, "document.querySelector('.task-row')?.dataset.id === "
                + JsonSerializer.Serialize(restoredNotice.Id));
            await EvaluateAsync(reopenedWindow, "document.querySelector('.task-row').click()");
            await WaitAsync(reopenedWindow, "document.querySelector('#notice-form') !== null"
                + " && document.querySelector('.source-section .message')?.textContent === " + JsonSerializer.Serialize(text));
            await WaitAsync(reopenedWindow, "document.querySelector('#notice-form [name=date]').value === '2026-10-20'"
                + " && document.querySelector('#notice-form [name=time]').value === '13:00'"
                + " && document.querySelector('#notice-form [name=timeKind]').value === 'start'"
                + " && !document.querySelector('.detail-scroll').textContent.includes('确认正式日期后')");
            await EvaluateAsync(reopenedWindow, "document.querySelector('#notice-form').requestSubmit()");
            await WaitAsync(() => reopened.Snapshot(DateTimeOffset.UtcNow).Items.Any(item => item.Kind == "task"));
            var converted = reopened.Snapshot(DateTimeOffset.UtcNow);
            var task = converted.Items.Single(item => item.Kind == "task");
            var preservedNotice = converted.Items.Single(item => item.Kind == "notice");
            if (converted.Items.Count != 2 || task.Due != "2026-10-20" || task.Time != "13:00"
                || task.TimeKind != "start" || task.Title != text || !task.Evidence.SequenceEqual(review.Evidence)
                || preservedNotice.Id != restoredNotice.Id || preservedNotice.TargetTaskId != task.Id
                || !preservedNotice.Evidence.SequenceEqual(review.Evidence))
                throw new InvalidDataException("活动通知应带出可靠开始时间，经确认加入任务并保留通知及原消息。");
        }
        finally { reopenedWindow.Exiting = true; reopenedWindow.Close(); await reopenedRuntime.StopAsync(); }
    }
}
