using System.IO;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Backup;
using MessagePurify.Infrastructure.Images;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using Microsoft.Web.WebView2.Core;

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static readonly string[] ImageChecks = ["image-original-local", "image-preview", "image-ocr-collapsed",
        "image-suggested-date-only", "image-retry", "image-manual-confirm", "image-original-unchanged", "image-backup-roundtrip"];

    private static async Task CheckImagesAsync(string dataRoot, string reportFile)
    {
        var root = Path.Combine(dataRoot, "image-evidence");
        var workspace = new AccountWorkspace(root, "10000");
        var now = DateTimeOffset.UtcNow;
        HashSet<string> pins = ["10001"];
        workspace.SetAvailableGroups([new("10001", "合成图片群")], pins);
        workspace.Execute(new("10000", "confirm-group", "10001", "image-course", 0, Course: "数学"), now);
        var raw = new SchoolMessage(new("10000", "10001", "image-original"), "10002", now,
            [new(SegmentKind.Image, "", AssetId: "synthetic-image")]);
        workspace.Ingest(raw, pins, MessageOrigin.History, "synthetic-image", now);
        var source = Path.Combine(root, "source.png");
        File.WriteAllBytes(source, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXs8AAAAASUVORK5CYII="));
        await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(source)), now);
        await workspace.RecognizeImagesAsync(new SmokeImageRecognizer(new("明天18点前提交数学作业", [])), now);
        var review = workspace.Snapshot(now).Items.Single();
        if (review.Kind != "review" || review.Due is not null || review.SuggestedDue is null || review.SuggestedTime != "18:00")
            throw new InvalidDataException("图片识别必须只产生待确认和日期建议。");
        var image = review.Evidence.Single().Images!.Single();
        var runtime = new AppRuntime(root, workspace);
        var window = new AppWindow(runtime, root, hidden: true, registerStartup: false);
        try
        {
            window.Show();
            await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var imageResponses = new List<int>();
            window.Browser.CoreWebView2.WebResourceResponseReceived += (_, response) =>
            { if (response.Request.Uri.Contains("/evidence/", StringComparison.Ordinal)) imageResponses.Add(response.Response.StatusCode); };
            await WaitAsync(window, "snapshot !== null && initialized");
            await window.OpenItemAsync(workspace.AccountId, review.Id);
            await WaitAsync(window, "document.querySelector('img.evidence-thumbnail') !== null");
            await EvaluateAsync(window, "(() => {const image=document.querySelector('img.evidence-thumbnail');image.loading='eager';image.scrollIntoView();const url=image.src;image.removeAttribute('src');image.src=url;})()");
            try { await WaitAsync(window, "document.querySelector('img.evidence-thumbnail')?.complete && document.querySelector('img.evidence-thumbnail').naturalWidth === 1"); }
            catch (TimeoutException)
            {
                var diagnostic = await EvaluateAsync(window, "JSON.stringify([...document.querySelectorAll('img.evidence-thumbnail')].map(image=>({src:image.src,complete:image.complete,width:image.naturalWidth})))");
                throw new InvalidDataException("合成原图加载失败：" + diagnostic + "; HTTP=" + string.Join(',', imageResponses));
            }
            await WaitAsync(window, "document.querySelector('details.image-ocr')?.open === false && document.querySelector('#review-form [name=date]')?.value === ''");
            await EvaluateAsync(window, "document.querySelector('[data-action=preview-image]').click()");
            await WaitAsync(window, "document.querySelector('#image-preview-dialog')?.open && document.querySelector('#image-preview-original')?.naturalWidth === 1");
            window.ShowMainWindow();
            await using (var capture = File.Create(Path.ChangeExtension(reportFile, ".images.png")))
                await window.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
            await EvaluateAsync(window, "document.querySelector('[data-action=close-image-preview]').click();document.querySelector('details.image-ocr summary').click()");
            await WaitAsync(window, "document.querySelector('details.image-ocr')?.open && document.querySelector('details.image-ocr')?.textContent.includes('明天18点前')");
            new AccountImageStore(root, "10000").SetFailure(image.Id, "ocr", "image-ocr-failed", now.AddSeconds(1));
            runtime.Refresh();
            await WaitAsync(window, "document.querySelector('[data-action=retry-image]') !== null");
            await EvaluateAsync(window, "document.querySelector('[data-action=retry-image]').click()");
            await WaitAsync(() => new AccountImageStore(root, "10000").Find(image.Id)?.Status == "downloaded");
            await workspace.RecognizeImagesAsync(new SmokeImageRecognizer(new("明天18点前提交数学作业", [])), now.AddSeconds(2));
            runtime.Refresh();
            var confirmed = now.ToOffset(TimeSpan.FromHours(8)).AddDays(3).ToString("yyyy-MM-dd");
            await SetFormFieldsAsync(window, "review-form", new { date = confirmed, time = "19:00" });
            await EvaluateAsync(window, "document.querySelector('#pending-confirm-button').click()");
            await WaitAsync(() => workspace.Snapshot(now).Items.Any(item => item.Kind == "task" && item.Due == confirmed && item.Time == "19:00"));
            var original = new AccountMessageStore(root, "10000").Find(raw.Key)!.Message;
            if (JsonSerializer.Serialize(original) != JsonSerializer.Serialize(raw))
                throw new InvalidDataException("OCR或人工确认覆盖了原消息。");
            var archive = Path.Combine(root, "images.zip");
            var preferences = new BackupApplicationSettings(false, "dark", "reduce", null);
            var exported = workspace.ExportBackup(archive, preferences, now);
            if (!exported.IsComplete || exported.MissingImageCount != 0)
                throw new InvalidDataException("已下载原图未完整进入备份。");
            var originalPath = workspace.OriginalImagePath(image.Id, workspace.Generation)!;
            File.Delete(originalPath);
            using var prepared = workspace.InspectBackup(archive);
            workspace.RestoreBackup(prepared, preferences, now);
            if (workspace.OriginalImagePath(image.Id, 0) is not null || !File.Exists(workspace.OriginalImagePath(image.Id, workspace.Generation)))
                throw new InvalidDataException("原图恢复或代次隔离失败。");
            if (runtime.Status.SyncPhase != "idle") throw new InvalidOperationException("图片合成验收意外触发QQ同步。");
        }
        finally { window.Exiting = true; window.Close(); await runtime.StopAsync(); }
    }

    private sealed class SmokeImageRecognizer(ImageRecognition result) : IImageTextRecognizer
    {
        public Task<ImageRecognition> RecognizeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
