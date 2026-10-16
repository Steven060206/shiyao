using System.IO;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static async Task CheckStandaloneChangeAsync(string dataRoot)
    {
        var root = Path.Combine(dataRoot, "standalone-change");
        var workspace = new AccountWorkspace(root, "10000");
        var sentAt = new DateTimeOffset(2026, 10, 16, 20, 2, 32, TimeSpan.FromHours(8));
        const string text = "修改材料，补交材料，思想汇报统一改为下周一18：00在合成大厅收取";
        HashSet<string> pinned = ["10001"];
        workspace.SetAvailableGroups([new OneBotGroup("10001", "合成材料通知群")], pinned);
        var original = new SchoolMessage(new("10000", "10001", "standalone-change"), "10002", sentAt,
            [new(SegmentKind.Text, text)]);
        workspace.Ingest(original, pinned, MessageOrigin.Realtime, "synthetic-standalone", sentAt);
        workspace = new AccountWorkspace(root, "10000");
        var review = workspace.Snapshot(sentAt).Items.Single();
        if (review is not { Kind: "review", Action: "postpone", Due: "2026-10-19", Time: "18:00", CanAccept: true, TargetTaskId: null })
            throw new InvalidOperationException("完整时间的无目标变更必须保留一个可确认的待确认项。");
        var runtime = new AppRuntime(root, workspace);
        var window = new AppWindow(runtime, root, hidden: true, registerStartup: false);
        using var desktop = new DesktopCardHost(runtime, root, window.OpenItemAsync);
        try
        {
            window.Show();
            await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitAsync(window, "snapshot !== null && initialized");
            await window.OpenItemAsync(workspace.AccountId, review.Id);
            await WaitAsync(window, "document.querySelector('#review-form [name=date]')?.value === '2026-10-19'"
                + " && document.querySelector('#review-form [name=time]')?.value === '18:00'"
                + " && document.querySelector('#pending-confirm-button')?.disabled === false"
                + " && document.querySelector('.detail-scroll')?.textContent.includes('按完整原消息新建任务')"
                + " && document.querySelector('.source-section .message')?.textContent === " + JsonSerializer.Serialize(text));
            await EvaluateAsync(window, "document.querySelector('#pending-confirm-button').click()");
            await WaitAsync(() => workspace.Snapshot(sentAt).Items.Any(item => item.Kind == "task"));
            var task = workspace.Snapshot(sentAt).Items.Single();
            if (task is not { Kind: "task", State: "active", Due: "2026-10-19", Time: "18:00" }
                || task.Title != text || !task.Evidence.SequenceEqual(review.Evidence))
                throw new InvalidDataException("确认必须保存整条任务、正式时间和原消息证据。");
            await desktop.RefreshAsync();
            if (desktop.Snapshot?.Tasks.Count != 1 || desktop.Snapshot.Pending.Count != 0)
                throw new InvalidDataException("无目标变更确认后应移入提醒事项卡片。");
        }
        finally { window.Exiting = true; window.Close(); await runtime.StopAsync(); }
        var reopened = new AccountWorkspace(root, "10000");
        reopened.Ingest(original, pinned, MessageOrigin.History, "synthetic-replay", sentAt.AddMinutes(1));
        if (reopened.Snapshot(sentAt).Items.Single() is not { Kind: "task", Due: "2026-10-19", Time: "18:00" })
            throw new InvalidDataException("重开与重放必须保留同一任务，不重复待确认。");
    }
}
