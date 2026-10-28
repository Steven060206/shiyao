using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Images;
using MessagePurify.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class AccountImageStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-images-" + Guid.NewGuid().ToString("N"));
    private const string Account = "12345";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private string Database => Path.Combine(root, "accounts", Account, "messages.sqlite3");
    private static byte[] Png => [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 1, 2, 3, 4];

    [Fact]
    public void Register_IsPersistentIdempotentAndPreservesOriginalMessage()
    {
        var message = Seed();
        var before = new AccountMessageStore(root, Account).Find(message.Key);
        var store = new AccountImageStore(root, Account);
        store.Register(message, Now);
        store.Register(message, Now.AddMinutes(1));

        var image = Assert.Single(new AccountImageStore(root, Account).List());
        Assert.Equal("pending", image.Status);
        Assert.Equal(1, image.SegmentIndex);
        Assert.Equal(message.Key, image.Message);
        Assert.Equal("opaque-image", image.AssetId);
        Assert.Equal(0, image.Attempts);
        Assert.Equal(Now, image.UpdatedAt);
        Assert.Null(image.RelativePath);
        Assert.Null(store.GetOriginalPath(image));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(new AccountMessageStore(root, Account).Find(message.Key)));
        Assert.Equal(image, store.Find(image.Id));
        Assert.Equal(image, Assert.Single(AccountImageStore.ReadAll(Database, Account)));
    }

    [Fact]
    public void Register_RejectsUnknownMessageDifferentOwnerAndChangedImageIdentity()
    {
        var message = Seed();
        var store = new AccountImageStore(root, Account);
        Assert.Throws<ArgumentException>(() => store.Register(message with { Key = message.Key with { AccountId = "999" } }, Now));
        Assert.Throws<InvalidDataException>(() => store.Register(message with { Key = message.Key with { MessageId = "missing" } }, Now));
        Assert.Throws<InvalidDataException>(() => store.Register(message with { Segments = [new(SegmentKind.Image, "", AssetId: "different")] }, Now));
        Assert.Empty(store.List());
    }

    [Fact]
    public void ImageId_SeparatesAccountGroupMessageAndSegmentWithoutUsingFileNames()
    {
        var store = new AccountImageStore(root, Account);
        var first = Seed();
        var second = first with { Key = first.Key with { GroupId = "other" } };
        var third = first with { Key = first.Key with { MessageId = "other" } };
        var fourth = first with { Key = first.Key with { MessageId = "two-images" }, Segments = [new(SegmentKind.Image, "", AssetId: "../../image"), new(SegmentKind.Image, "", AssetId: "../../image")] };
        foreach (var message in new[] { second, third, fourth }) Enqueue(message);
        foreach (var message in new[] { first, second, third, fourth }) store.Register(message, Now);

        var images = store.List();
        Assert.Equal(5, images.Count);
        Assert.Equal(5, images.Select(image => image.Id).Distinct().Count());
        Assert.All(images, image => Assert.Matches("^[0-9a-f]{64}$", image.Id));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("tiff")]
    [InlineData("webp")]
    public void SaveOriginal_UsesDetectedFormatAndHashAndKeepsSource(string format)
    {
        var image = Register();
        var bytes = FormatBytes(format);
        var source = Source(bytes, "source.with-untrusted-extension");
        var before = File.ReadAllBytes(source);
        var store = new AccountImageStore(root, Account);

        var saved = store.SaveOriginal(image.Id, source, Now.AddMinutes(1));

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal("downloaded", saved.Status);
        Assert.Equal(hash, saved.Sha256);
        Assert.Equal(bytes.Length, saved.Length);
        Assert.Equal("images/" + hash + "." + format, saved.RelativePath);
        Assert.Equal(bytes, File.ReadAllBytes(store.GetOriginalPath(saved)!));
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(image.AssetId, saved.AssetId);
        Assert.Equal(saved, Assert.Single(AccountImageStore.ReadAll(Database, Account)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.GetOriginalPath(saved))!, "*.tmp"));
    }

    [Fact]
    public void SaveOriginal_SharedContentIsDeduplicatedAndRepeatDoesNotDiscardRecognition()
    {
        var message = Seed() with { Key = new(Account, "100", "two"), Segments = [new(SegmentKind.Image, "", AssetId: "one"), new(SegmentKind.Image, "", AssetId: "two")] };
        Enqueue(message);
        var store = new AccountImageStore(root, Account);
        store.Register(message, Now);
        var images = store.List();
        var source = Source(Png);
        var first = store.SaveOriginal(images[0].Id, source, Now);
        var recognized = store.SaveRecognition(first.Id, Recognition(), Now.AddMinutes(1));
        var repeated = store.SaveOriginal(first.Id, source, Now.AddMinutes(2));
        var second = store.SaveOriginal(images[1].Id, source, Now);

        Assert.Equal("recognized", repeated.Status);
        Assert.Equal(JsonSerializer.Serialize(recognized.Recognition), JsonSerializer.Serialize(repeated.Recognition));
        Assert.Equal(first.RelativePath, second.RelativePath);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "accounts", Account, "images")));
    }

    [Theory]
    [InlineData("relative.png")]
    [InlineData("https://example.invalid/private.png")]
    [InlineData("file:///C:/private.png")]
    [InlineData("\\\\server\\share\\private.png")]
    public void SaveOriginal_RejectsNonLocalAbsolutePathWithoutChangingEvidence(string source)
    {
        var image = Register();
        var store = new AccountImageStore(root, Account);
        Assert.Throws<ArgumentException>(() => store.SaveOriginal(image.Id, source, Now));
        Assert.Equal(image, store.Find(image.Id));
    }

    [Fact]
    public void SaveOriginal_RejectsUnknownFormatAndOversizeWithoutAssociatingPartialFile()
    {
        var image = Register();
        var store = new AccountImageStore(root, Account);
        Assert.Throws<InvalidDataException>(() => store.SaveOriginal(image.Id, Source(Encoding.UTF8.GetBytes("not an image")), Now));
        var large = Source(Png, "large.png");
        using (var file = new FileStream(large, FileMode.Open, FileAccess.Write)) file.SetLength(32 * 1024 * 1024 + 1L);
        Assert.Throws<InvalidDataException>(() => store.SaveOriginal(image.Id, large, Now));
        Assert.Equal(image, store.Find(image.Id));
        Assert.False(Directory.Exists(Path.Combine(root, "accounts", Account, "images")) && Directory.EnumerateFiles(Path.Combine(root, "accounts", Account, "images")).Any());
    }

    [Fact]
    public void Recognition_StoresTextPositionsConfidenceAndFailureKeepsOriginalAcrossRetry()
    {
        var image = Register();
        var store = new AccountImageStore(root, Account);
        var downloaded = store.SaveOriginal(image.Id, Source(Png), Now);
        var failed = store.SetFailure(image.Id, "ocr", "OCR_FAILED", Now.AddMinutes(1));
        Assert.Equal("ocr-failed", failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal("OCR_FAILED", failed.Error);
        Assert.Equal(downloaded.RelativePath, failed.RelativePath);
        Assert.True(File.Exists(store.GetOriginalPath(failed)));
        var retry = store.Retry(image.Id, Now.AddMinutes(2));
        Assert.Equal("downloaded", retry.Status);
        Assert.Equal(0, retry.Attempts);
        Assert.Null(retry.Error);

        var recognized = store.SaveRecognition(image.Id, Recognition(), Now.AddMinutes(3));
        Assert.Equal("recognized", recognized.Status);
        Assert.Equal(JsonSerializer.Serialize(Recognition()), JsonSerializer.Serialize(recognized.Recognition));
        Assert.Equal(recognized.RelativePath, downloaded.RelativePath);
        var reopened = Assert.Single(new AccountImageStore(root, Account).List());
        Assert.Equal(JsonSerializer.Serialize(recognized), JsonSerializer.Serialize(reopened));
    }

    [Fact]
    public void FailureAndRetry_DoNotEraseOriginalOrAlreadyRecognizedText()
    {
        var image = Register();
        var store = new AccountImageStore(root, Account);
        var failed = store.SetFailure(image.Id, "download", "download-failed", Now);
        Assert.Equal("download-failed", failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal("pending", store.Retry(image.Id, Now).Status);
        store.SaveOriginal(image.Id, Source(Png), Now);
        var recognized = store.SaveRecognition(image.Id, Recognition(), Now);
        Assert.Equal(JsonSerializer.Serialize(recognized), JsonSerializer.Serialize(store.Retry(image.Id, Now)));
        Assert.Throws<ArgumentException>(() => store.SetFailure(image.Id, "other", "failure", Now));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void SaveRecognition_RejectsInvalidConfidenceWithoutChangingOriginal(double confidence)
    {
        var image = Register();
        var store = new AccountImageStore(root, Account);
        var downloaded = store.SaveOriginal(image.Id, Source(Png), Now);
        var recognition = new ImageRecognition("text", [new("text", [[0d, 0d], [1d, 0d], [1d, 1d], [0d, 1d]], confidence)]);
        Assert.Throws<InvalidDataException>(() => store.SaveRecognition(image.Id, recognition, Now));
        Assert.Equal(downloaded, store.Find(image.Id));
    }

    [Fact]
    public void SaveRecognition_RejectsInvalidGeometryWithoutChangingOriginal()
    {
        var image = Register();
        var store = new AccountImageStore(root, Account);
        var downloaded = store.SaveOriginal(image.Id, Source(Png), Now);
        var badBoxes = new IReadOnlyList<IReadOnlyList<double>>[]
        {
            [], [[0d, 0d]], [[0d, 0d], [1d, 0d], [1d, double.NaN], [0d, 1d]],
            [[0d, 0d], [1d, 0d], [1d, 1d, 2d], [0d, 1d]]
        };
        foreach (var box in badBoxes)
            Assert.Throws<InvalidDataException>(() => store.SaveRecognition(image.Id, new("text", [new("text", box, .9)]), Now));
        Assert.Equal(downloaded, store.Find(image.Id));
    }

    [Fact]
    public void SaveRecognition_RequiresOriginalAndAllowsEmptySuccessfulText()
    {
        var image = Register();
        var store = new AccountImageStore(root, Account);
        Assert.Throws<InvalidOperationException>(() => store.SaveRecognition(image.Id, new("", []), Now));
        store.SaveOriginal(image.Id, Source(Png), Now);
        var empty = store.SaveRecognition(image.Id, new("", []), Now);
        Assert.Equal("recognized", empty.Status);
        Assert.Equal("", empty.Recognition!.Text);
        Assert.Empty(empty.Recognition.Lines);
    }

    [Fact]
    public void ReadAll_IsReadOnlyAndCompatibleWithOldDatabase()
    {
        var message = Seed();
        var original = File.ReadAllBytes(Database);
        Assert.Empty(AccountImageStore.ReadAll(Database, Account));
        Assert.Equal(original, File.ReadAllBytes(Database));
        Assert.Throws<InvalidDataException>(() => AccountImageStore.ReadAll(Database, "999"));
        var store = new AccountImageStore(root, Account);
        store.Register(message, Now);
        var populated = File.ReadAllBytes(Database);
        Assert.Single(AccountImageStore.ReadAll(Database, Account));
        Assert.Equal(populated, File.ReadAllBytes(Database));
    }

    [Fact]
    public void DeletingSourceMessageCascadesImageEvidenceWithoutDeletingOtherMessage()
    {
        var image = Register();
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, ForeignKeys = true, Pooling = false }.ToString());
        db.Open();
        using var delete = db.CreateCommand();
        delete.CommandText = "DELETE FROM messages WHERE message_id=$id;";
        delete.Parameters.AddWithValue("$id", image.Message.MessageId);
        Assert.Equal(1, delete.ExecuteNonQuery());
        Assert.Empty(new AccountImageStore(root, Account).List());
    }

    [Fact]
    public void OriginalPathRejectsForeignOwnerAndTraversalAndReturnsNullWhenMissing()
    {
        var image = Register();
        var store = new AccountImageStore(root, Account);
        var downloaded = store.SaveOriginal(image.Id, Source(Png), Now);
        Assert.Throws<InvalidDataException>(() => store.GetOriginalPath(downloaded with { RelativePath = "../outside.png" }));
        Assert.Throws<ArgumentException>(() => store.GetOriginalPath(downloaded with { Message = downloaded.Message with { AccountId = "999" } }));
        File.Delete(store.GetOriginalPath(downloaded)!);
        Assert.Null(store.GetOriginalPath(downloaded));
    }

    private ImageEvidenceRecord Register()
    {
        var message = Seed();
        var store = new AccountImageStore(root, Account);
        store.Register(message, Now);
        return Assert.Single(store.List());
    }
    private SchoolMessage Seed()
    {
        var message = new SchoolMessage(new(Account, "100", "200"), "teacher", Now,
            [new(SegmentKind.Text, "synthetic notice"), new(SegmentKind.Image, "", AssetId: "opaque-image")]);
        Enqueue(message);
        return message;
    }
    private void Enqueue(SchoolMessage message) => new AccountMessageStore(root, Account).TryEnqueue(message, MessageOrigin.History, "synthetic", Now);
    private string Source(byte[] bytes, string name = "input.png")
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, name);
        File.WriteAllBytes(file, bytes);
        return file;
    }
    private static ImageRecognition Recognition() => new("明天交作业", [new("明天交作业", [[0d, 0d], [50d, 0d], [50d, 10d], [0d, 10d]], .98)]);
    private static byte[] FormatBytes(string format) => format switch
    {
        "png" => Png,
        "jpg" => [255, 216, 255, 224, 1, 2, 3, 4],
        "gif" => Encoding.ASCII.GetBytes("GIF89a1234567890"),
        "bmp" => Encoding.ASCII.GetBytes("BM12345678901234"),
        "tiff" => [73, 73, 42, 0, 8, 0, 0, 0],
        "webp" => Encoding.ASCII.GetBytes("RIFF1234WEBP1234"),
        _ => throw new ArgumentException()
    };
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
