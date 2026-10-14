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
    private static async Task CheckBatchReviewAsync(string dataRoot, string reportFile)
    {
        var root = Path.Combine(dataRoot, "batch-review");
        var workspace = CreateWorkspace(root);
        var now = DateTimeOffset.UtcNow;
        workspace.Ingest(new(new(workspace.AccountId, "10001", "3"), "10002", now,
            [new(SegmentKind.Text, "请提交数学批量汇报资料")]), new HashSet<string> { "10001" }, MessageOrigin.Realtime, "synthetic-batch", now);
        var before = workspace.Snapshot(now).Items.Where(item => item.Kind == "review" && item.State == "pending").ToArray();
        if (before.Length != 2) throw new InvalidOperationException("批量审核未准备两项独立合成待确认。");
        var runtime = new AppRuntime(root, workspace);
        var window = new AppWindow(runtime, root, hidden: true, registerStartup: false);
        try
        {
            window.Show();
            await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitAsync(window, "document.querySelectorAll('[data-batch-id]').length === 2");
            await EvaluateAsync(window, "document.querySelector('#batch-select-all').click()");
            await EvaluateAsync(window, "document.querySelector('[data-action=batch-ignore]').click()");
            await WaitAsync(window, "document.querySelector('#batch-result').textContent.includes('已忽略 2 项')");
            var ignored = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Where(item => before.Any(original => original.Id == item.Id)).ToArray();
            if (ignored.Length != 2 || ignored.Any(item => item.State != "ignored" || item.UndoOperationId is null)
                || ignored.Select(item => item.UndoOperationId).Distinct().Count() != 2)
                throw new InvalidDataException("批量忽略未分别保存状态及撤销凭据。");
            foreach (var item in ignored)
                workspace.Execute(new(workspace.AccountId, "undo-ignore", item.Id, Guid.NewGuid().ToString("N"), item.Version,
                    UndoOperationId: item.UndoOperationId), DateTimeOffset.UtcNow);
            runtime.Refresh();
            await WaitAsync(window, "document.querySelectorAll('[data-batch-id]').length === 2");
            var date = now.ToOffset(TimeSpan.FromHours(8)).AddDays(6).ToString("yyyy-MM-dd");
            await EvaluateAsync(window, "document.querySelector('#batch-select-all').click();document.querySelector('#batch-date').value=" + JsonSerializer.Serialize(date));
            await using (var image = File.Create(Path.ChangeExtension(reportFile, ".batch-review.png")))
                await window.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            await EvaluateAsync(window, "document.querySelector('[data-action=batch-accept]').click()");
            await WaitAsync(window, "document.querySelector('#batch-result').textContent.includes('已加入 2 项')");
            var reopened = new AccountWorkspace(root, workspace.AccountId).Snapshot(DateTimeOffset.UtcNow);
            foreach (var original in before)
            {
                var task = reopened.Items.Single(item => item.Id == "accepted-" + original.Id);
                if (task.Kind != "task" || task.State != "active" || task.Due != date || task.Time is not null
                    || !task.Evidence.SequenceEqual(original.Evidence) || reopened.Items.Any(item => item.Id == original.Id))
                    throw new InvalidDataException("批量加入或重开丢失任务、日期精度、来源证据。");
            }
        }
        finally { window.Exiting = true; window.Close(); await runtime.StopAsync(); }
    }
}
