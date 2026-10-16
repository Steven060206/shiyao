using System.IO;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static async Task CheckMessageReviewAsync(string dataRoot)
    {
        await CheckStandaloneChangeAsync(dataRoot);
        var root = Path.Combine(dataRoot, "message-review");
        var workspace = new AccountWorkspace(root, "10000");
        var now = DateTimeOffset.UtcNow;
        var firstDate = now.ToOffset(TimeSpan.FromHours(8)).AddDays(2);
        var secondDate = firstDate.AddDays(3);
        var text = $"请于{firstDate.Month}月{firstDate.Day}日之前提交实验报告，格式为PDF；\n"
            + $"请于{secondDate.Month}月{secondDate.Day}日之前提交课程论文，附实验数据。";
        HashSet<string> pinned = ["10001"];
        workspace.SetAvailableGroups([new OneBotGroup("10001", "合成待核对课程群")], pinned);
        var original = new SchoolMessage(new("10000", "10001", "whole-message"), "10002", now,
            [new(SegmentKind.Text, text)]);
        workspace.Ingest(original, pinned, MessageOrigin.Realtime, "synthetic-whole-message", now);
        var before = workspace.Snapshot(now);
        if (before.Items.Count != 1 || before.Items[0] is not { Kind: "review", State: "pending", Due: null,
                SuggestedDue: null, Course: null, CanAccept: true } review
            || review.Title != text || review.Evidence.Count != 1 || review.Evidence[0].Text != text)
            throw new InvalidOperationException("同一条多事项原消息必须仅生成一项完整待确认，不得代选日期或先建部分任务。");

        var runtime = new AppRuntime(root, workspace);
        var window = new AppWindow(runtime, root, hidden: true, registerStartup: false);
        using var desktop = new DesktopCardHost(runtime, root, window.OpenItemAsync);
        try
        {
            window.Show();
            await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitAsync(window, "document.querySelectorAll('[data-batch-id]').length === 1 && document.querySelectorAll('.task-row').length === 1");
            await desktop.RefreshAsync();
            if (desktop.Snapshot?.Pending.Count != 1 || desktop.Snapshot.Pending[0].Id != review.Id
                || desktop.Snapshot.Tasks.Count != 0)
                throw new InvalidOperationException("桌面待确认重复展示了同一原消息，或提前显示部分任务。");
            await window.OpenItemAsync(workspace.AccountId, review.Id);
            var originalJson = JsonSerializer.Serialize(text);
            await WaitAsync(window, "document.querySelector('.detail-title')?.textContent === " + originalJson
                + " && document.querySelector('.source-section .message')?.textContent === " + originalJson
                + " && document.querySelector('#review-form [name=date]')?.value === ''");

            var acceptedDate = firstDate.ToString("yyyy-MM-dd");
            await SetFormFieldsAsync(window, "review-form", new { date = acceptedDate });
            await EvaluateAsync(window, "document.querySelector('#pending-confirm-button').click()");
            await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count(item => item.Kind == "task") == 1);
            var accepted = workspace.Snapshot(DateTimeOffset.UtcNow);
            var task = accepted.Items.Single(item => item.Kind == "task");
            if (accepted.Items.Count != 1 || task.State != "active" || task.Title != text
                || task.Due != acceptedDate || task.Time is not null || !task.Evidence.SequenceEqual(review.Evidence))
                throw new InvalidDataException("一次确认必须仅保存一项完整任务、显式日期和完整原消息证据。");
            await window.OpenItemAsync(workspace.AccountId, task.Id);
            await WaitAsync(window, "document.querySelector('.detail-title')?.textContent === " + originalJson
                + " && document.querySelector('[data-action=open-split]') !== null");

            var childTitle = "手动拆出的课程论文";
            var childDate = secondDate.ToString("yyyy-MM-dd");
            await OpenSplitFormAsync(window);
            if (workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count(item => item.Kind == "task") != 1)
                throw new InvalidOperationException("打开手动拆分表单不应提前生成任务。");
            await SetFormFieldsAsync(window, "split-task-form", new { title = childTitle, course = "手动核对课程",
                date = childDate, time = "", timeKind = "deadline", priority = "auto", state = "active" });
            await SelectSplitEvidenceAsync(window, 0);
            await EvaluateAsync(window, "document.querySelector('#split-task-form').requestSubmit()");
            await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Any(item => item.Kind == "task" && item.Title == childTitle));
            var split = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Title == childTitle);
            if (split.State != "active" || split.Due != childDate || !split.Evidence.SequenceEqual(review.Evidence)
                || !(split.RelatedItemIds ?? []).Contains(task.Id))
                throw new InvalidDataException("显式手动拆分没有保留所选日期、原消息证据或父事项关联。");

            var reopened = new AccountWorkspace(root, workspace.AccountId);
            reopened.SetAvailableGroups([new OneBotGroup("10001", "合成待核对课程群")], pinned);
            reopened.Ingest(original, pinned, MessageOrigin.History, "synthetic-whole-message-replay", now.AddSeconds(1));
            var after = reopened.Snapshot(DateTimeOffset.UtcNow);
            var parent = after.Items.Single(item => item.Id == task.Id);
            if (after.Items.Count != 2 || after.Items.Any(item => item.Kind != "task")
                || parent.Title != text || parent.Due != acceptedDate || !parent.Evidence.SequenceEqual(review.Evidence)
                || !(parent.RelatedItemIds ?? []).Contains(split.Id)
                || after.Items.Single(item => item.Id == split.Id).Due != childDate)
                throw new InvalidDataException("重开并重读原消息后丢失完整父事项、手动子事项，或重新产生自动拆分条目。");
        }
        finally { window.Exiting = true; window.Close(); await runtime.StopAsync(); }
    }
}
