using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Images;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Backup;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class AccountWorkspaceImageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Shiyao-images-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly HashSet<string> pins = ["100"];
    private AccountWorkspace Workspace()
    {
        var workspace = new AccountWorkspace(root, "12345");
        workspace.SetAvailableGroups([new("100", "数学群")], pins);
        workspace.Execute(new("12345", "confirm-group", "100", Guid.NewGuid().ToString(), 0, Course: "数学"), Now);
        return workspace;
    }
    private SchoolMessage Image(string id = "1", string text = "") => new(new("12345", "100", id), "67890", Now,
        [new(SegmentKind.Text, text), new(SegmentKind.Image, "", AssetId: "image-" + id)]);
    private string Original()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.png");
        File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXs8AAAAASUVORK5CYII="));
        return path;
    }
    private static readonly ImageRecognition Recognition = new("明天18点前提交数学作业", []);

    [Fact]
    public async Task ImageOnlyBecomesOneReviewWithSuggestedTimeAndUnchangedOriginal()
    {
        var workspace = Workspace(); var message = Image();
        workspace.Ingest(message, pins, MessageOrigin.History, "test", Now);
        var source = Original(); var downloads = 0;
        Assert.Equal(1, await workspace.DownloadImagesAsync((asset, token) => { downloads++; Assert.Equal("image-1", asset); return Task.FromResult(new OneBotImage(source)); }, Now));
        Assert.Equal(1, await workspace.RecognizeImagesAsync(new Recognizer(Recognition), Now));
        var item = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.Equal("review", item.Kind); Assert.Null(item.Due); Assert.Equal("2026-10-29", item.SuggestedDue);
        Assert.Equal("18:00", item.SuggestedTime);
        Assert.Equal("recognized", Assert.Single(Assert.Single(item.Evidence).Images!).Status);
        Assert.Equal(0, await workspace.DownloadImagesAsync((_, _) => throw new Exception(), Now));
        Assert.Equal(0, await workspace.RecognizeImagesAsync(new Recognizer(Recognition), Now));
        Assert.Equal(1, downloads);
        var raw = new AccountMessageStore(root, "12345").Find(message.Key)!.Message;
        Assert.Equal("", raw.Segments[1].Text);
        Assert.Single(Workspace().Snapshot(Now).Items);
    }

    [Theory]
    [InlineData("ignore")]
    [InlineData("reject")]
    [InlineData("accept-review")]
    public async Task RecognitionPreservesUserDecisionsAndOriginalReview(string operation)
    {
        var workspace = Workspace(); var message = Image(text: "请提交数学作业");
        workspace.Ingest(message, pins, MessageOrigin.History, "test", Now);
        var review = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(new("12345", operation, review.Id, "user-decision", review.Version,
            Date: operation == "accept-review" ? "2026-11-03" : null), Now);
        using var db = new SqliteConnection("Data Source=" + Path.Combine(root, "accounts/12345/tasks.sqlite3")); db.Open();
        using var command = db.CreateCommand(); command.CommandText = "SELECT record_json FROM reviews;";
        var before = (string)command.ExecuteScalar()!;
        await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(Original())), Now);
        await workspace.RecognizeImagesAsync(new Recognizer(Recognition), Now);
        Assert.Equal(before, command.ExecuteScalar());
        if (operation == "accept-review") Assert.Equal("2026-11-03", Assert.Single(workspace.Snapshot(Now).Items, i => i.Kind == "task").Due);
        else Assert.DoesNotContain(workspace.Snapshot(Now).Items, i => i.State == "pending");
    }

    [Fact]
    public async Task FailedRecognitionKeepsOriginalAndExplicitRetrySucceeds()
    {
        var workspace = Workspace(); workspace.Ingest(Image(), pins, MessageOrigin.History, "test", Now);
        await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(Original())), Now);
        await workspace.RecognizeImagesAsync(new Recognizer(null), Now);
        var image = Assert.Single(new AccountImageStore(root, "12345").List());
        Assert.Equal("ocr-failed", image.Status); Assert.NotNull(image.RelativePath);
        Assert.Equal(image.Id, Assert.Single(Assert.Single(workspace.Snapshot(Now).ImageQueue!).Images!).Id);
        workspace.RetryImage(image.Id, workspace.Generation, Now);
        Assert.Equal(1, await workspace.RecognizeImagesAsync(new Recognizer(Recognition), Now));
    }

    [Fact]
    public async Task UnpinnedOrPausedImagesAreNotDownloadedAndLateResultsAreDiscarded()
    {
        var workspace = Workspace(); workspace.Ingest(Image(), pins, MessageOrigin.History, "test", Now);
        var calls = 0;
        Assert.Equal(0, await workspace.DownloadImagesAsync((_, _) => { calls++; throw new Exception(); }, Now, () => false));
        Assert.Equal(0, calls);
        await workspace.DownloadImagesAsync((_, _) => { workspace.SetAvailableGroups([new("100", "数学群")], new HashSet<string>()); return Task.FromResult(new OneBotImage(Original())); }, Now);
        Assert.Equal("pending", Assert.Single(new AccountImageStore(root, "12345").List()).Status);
    }

    [Fact]
    public async Task DownloadedQueueResumesAfterReopenWithoutAnotherQqSession()
    {
        var workspace = Workspace(); workspace.Ingest(Image(), pins, MessageOrigin.History, "test", Now);
        await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(Original())), Now);
        var reopened = new AccountWorkspace(root, "12345");
        Assert.Equal(1, await reopened.RecognizeImagesAsync(new Recognizer(Recognition), Now));
        Assert.Equal("review", Assert.Single(reopened.Snapshot(Now).Items).Kind);
    }

    [Fact]
    public async Task MissingOriginalAfterIncompleteRestoreCanDownloadAgainWithoutLosingOcr()
    {
        var workspace = Workspace(); workspace.Ingest(Image(), pins, MessageOrigin.History, "test", Now);
        await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(Original())), Now);
        await workspace.RecognizeImagesAsync(new Recognizer(Recognition), Now);
        var store = new AccountImageStore(root, "12345"); var image = Assert.Single(store.List());
        File.Delete(store.GetOriginalPath(image)!);
        var shown = Assert.Single(Assert.Single(Assert.Single(workspace.Snapshot(Now).Items).Evidence).Images!);
        Assert.True(shown.CanRetry); Assert.Null(shown.Url); Assert.Equal(Recognition.Text, shown.OcrText);
        workspace.RetryImage(image.Id, workspace.Generation, Now);
        Assert.Equal(1, await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(Original())), Now));
        Assert.Equal("recognized", store.Find(image.Id)!.Status);
        Assert.Equal(Recognition.Text, store.Find(image.Id)!.Recognition!.Text);
        Assert.Single(workspace.Snapshot(Now).Items);
    }

    [Fact]
    public async Task RestoredOldBackupRecreatesImageQueueAndDiscardsInFlightResult()
    {
        var workspace = Workspace(); var message = Image(); workspace.Ingest(message, pins, MessageOrigin.History, "test", Now);
        var preferences = new BackupApplicationSettings(false, "dark", "reduce", null);
        var archive = Path.Combine(root, "old.zip");
        using (var db = new SqliteConnection("Pooling=False;Data Source=" + Path.Combine(root, "accounts/12345/messages.sqlite3")))
        { db.Open(); using var command = db.CreateCommand(); command.CommandText = "DROP TABLE image_evidence;"; command.ExecuteNonQuery(); }
        workspace.ExportBackup(archive, preferences, Now);
        var store = new AccountImageStore(root, "12345"); store.Register(message, Now);
        await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(Original())), Now);
        using var prepared = workspace.InspectBackup(archive);
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<ImageRecognition>();
        var operation = workspace.RecognizeImagesAsync(new DeferredRecognizer(entered, release), Now);
        await entered.Task;
        workspace.RestoreBackup(prepared, preferences, Now);
        release.SetResult(Recognition);
        Assert.Equal(0, await operation);
        Assert.Equal("pending", Assert.Single(store.List()).Status);
        Assert.Empty(workspace.Snapshot(Now).Items);
        Assert.Null(workspace.OriginalImagePath(Assert.Single(store.List()).Id, 0));
    }

    [Fact]
    public async Task CacheRetainsPendingImagesAndDeletesOnlyUnreferencedCompletedOriginals()
    {
        var workspace = Workspace(); workspace.Ingest(Image(), pins, MessageOrigin.History, "test", Now);
        Assert.Equal(0, workspace.CleanMessageCache(Now.AddDays(8)).DeletedMessages);
        await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(Original())), Now);
        await workspace.RecognizeImagesAsync(new Recognizer(new("", [])), Now);
        var store = new AccountImageStore(root, "12345"); var path = store.GetOriginalPath(Assert.Single(store.List()))!;
        var unrelated = Path.Combine(Path.GetDirectoryName(path)!, "leave-me.txt"); File.WriteAllText(unrelated, "keep");
        Assert.Equal(1, workspace.CleanMessageCache(Now.AddDays(8)).DeletedMessages);
        Assert.Empty(store.List()); Assert.False(File.Exists(path)); Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task MultipleImageMessageWaitsForAllImagesAndCreatesOneCompleteReview()
    {
        var workspace = Workspace(); var message = Image() with { Segments = [new(SegmentKind.Image, "", AssetId: "first"), new(SegmentKind.Image, "", AssetId: "second")] };
        workspace.Ingest(message, pins, MessageOrigin.History, "test", Now);
        await workspace.DownloadImagesAsync((_, _) => Task.FromResult(new OneBotImage(Original())), Now);
        var calls = 0;
        Assert.Equal(2, await workspace.RecognizeImagesAsync(new CallbackRecognizer(() =>
        {
            Assert.Empty(workspace.Snapshot(Now).Items);
            return ++calls == 1 ? Recognition : new ImageRecognition("请同时完成第二部分", []);
        }), Now));
        Assert.Contains("第二部分", Assert.Single(workspace.Snapshot(Now).Items).Title);
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Recognizer(ImageRecognition? result) : IImageTextRecognizer
    {
        public Task<ImageRecognition> RecognizeAsync(string path, CancellationToken cancellationToken = default) =>
            result is null ? Task.FromException<ImageRecognition>(new IOException("synthetic-failure")) : Task.FromResult(result);
    }
    private sealed class DeferredRecognizer(TaskCompletionSource entered, TaskCompletionSource<ImageRecognition> release) : IImageTextRecognizer
    {
        public Task<ImageRecognition> RecognizeAsync(string path, CancellationToken cancellationToken = default)
        { entered.SetResult(); return release.Task; }
    }
    private sealed class CallbackRecognizer(Func<ImageRecognition> callback) : IImageTextRecognizer
    {
        public Task<ImageRecognition> RecognizeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(callback());
    }
}
