using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Images;

/// <summary>原图先完整落盘，再保存关联；原消息与 OCR 结果分别保存。</summary>
public sealed class AccountImageStore
{
    private const long MaximumImageBytes = 32L * 1024 * 1024;
    private readonly string accountId;
    private readonly string accountDirectory;
    private readonly string databasePath;

    public AccountImageStore(string dataRoot, string accountId)
    {
        var messages = new AccountMessageStore(dataRoot, accountId);
        this.accountId = accountId;
        databasePath = messages.DatabasePath;
        accountDirectory = Path.GetDirectoryName(databasePath)!;
        EnsureSchema();
    }

    public void EnsureSchema()
    {
        using var db = Open(databasePath, readOnly: false);
        using var create = db.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS image_evidence(
                id TEXT PRIMARY KEY NOT NULL,
                account_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                message_id TEXT NOT NULL,
                segment_index INTEGER NOT NULL CHECK(segment_index >= 0),
                record_json TEXT NOT NULL,
                UNIQUE(account_id,group_id,message_id,segment_index),
                FOREIGN KEY(account_id,group_id,message_id)
                    REFERENCES messages(account_id,group_id,message_id) ON DELETE CASCADE
            );
            """;
        create.ExecuteNonQuery();
    }

    public void Register(SchoolMessage message, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateOwner(message.Key, accountId);
        if (message.IsPrivate || message.Segments is null) throw new ArgumentException("只接受已保留的群消息。", nameof(message));
        using var db = Open(databasePath, readOnly: false);
        using var transaction = db.BeginTransaction();
        using (var find = db.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT message_json FROM messages WHERE account_id=$account AND group_id=$group AND message_id=$message;";
            BindKey(find, message.Key);
            if (find.ExecuteScalar() is not string original ||
                JsonSerializer.Serialize(JsonSerializer.Deserialize<SchoolMessage>(original)) != JsonSerializer.Serialize(message))
                throw new InvalidDataException("图片来源与保留原消息不一致。");
        }
        for (var index = 0; index < message.Segments.Count; index++)
        {
            var segment = message.Segments[index];
            if (segment.Kind != SegmentKind.Image) continue;
            var image = new ImageEvidenceRecord(IdFor(message.Key, index), message.Key, index, segment.AssetId,
                "pending", null, null, null, null, null, 0, now);
            using var insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO image_evidence(id,account_id,group_id,message_id,segment_index,record_json)
                VALUES($id,$account,$group,$message,$index,$json) ON CONFLICT DO NOTHING;
                """;
            BindKey(insert, image.Message);
            insert.Parameters.AddWithValue("$id", image.Id);
            insert.Parameters.AddWithValue("$index", image.SegmentIndex);
            insert.Parameters.AddWithValue("$json", JsonSerializer.Serialize(image));
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<ImageEvidenceRecord> List() => ReadAll(databasePath, accountId);

    public IReadOnlyList<ImageEvidenceRecord> ListFor(MessageKey key)
    {
        ValidateOwner(key, accountId);
        using var db = Open(databasePath, readOnly: true);
        using var select = db.CreateCommand();
        select.CommandText = "SELECT id,account_id,group_id,message_id,segment_index,record_json FROM image_evidence WHERE account_id=$account AND group_id=$group AND message_id=$message ORDER BY segment_index;";
        BindKey(select, key);
        using var reader = select.ExecuteReader();
        var result = new List<ImageEvidenceRecord>();
        while (reader.Read()) result.Add(Read(reader, accountId));
        return result;
    }

    public void CleanUnreferencedOriginals()
    {
        var directory = Path.Combine(accountDirectory, "images");
        if (!Directory.Exists(directory)) return;
        var retained = List().Where(image => image.RelativePath is not null)
            .Select(image => Path.GetFileName(image.RelativePath!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(path);
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[a-f0-9]{64}\.(png|jpg|gif|bmp|tiff|webp)\z") && !retained.Contains(name))
                File.Delete(path);
        }
    }

    public ImageEvidenceRecord? Find(string id)
    {
        ValidateId(id);
        using var db = Open(databasePath, readOnly: true);
        using var find = db.CreateCommand();
        find.CommandText = "SELECT id,account_id,group_id,message_id,segment_index,record_json FROM image_evidence WHERE id=$id;";
        find.Parameters.AddWithValue("$id", id);
        using var rows = find.ExecuteReader();
        return rows.Read() ? Read(rows, accountId) : null;
    }

    public ImageEvidenceRecord SaveOriginal(string id, string sourcePath, DateTimeOffset now)
    {
        var image = Required(id);
        ValidateLocalPath(sourcePath);
        string? temporary = null;
        try
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.Length is <= 0 or > MaximumImageBytes) throw new InvalidDataException("图片为空或超过32MiB。");
            Span<byte> header = stackalloc byte[16];
            var count = source.Read(header);
            var format = DetectFormat(header[..count]);
            source.Position = 0;
            var directory = Path.Combine(accountDirectory, "images");
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            long length = 0;
            string hash;
            using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[64 * 1024];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        length += read;
                        if (length > MaximumImageBytes) throw new InvalidDataException("图片超过32MiB。");
                        digest.AppendData(buffer, 0, read);
                        destination.Write(buffer, 0, read);
                    }
                    destination.Flush(flushToDisk: true);
                }
                hash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            }
            var relativePath = "images/" + hash + "." + format;
            var target = Path.Combine(accountDirectory, "images", hash + "." + format);
            if (File.Exists(target))
            {
                using var existing = File.OpenRead(target);
                if (existing.Length != length || Convert.ToHexString(SHA256.HashData(existing)).ToLowerInvariant() != hash)
                    throw new InvalidDataException("已有原图与校验值不一致。");
                File.Delete(temporary);
            }
            else File.Move(temporary, target);
            temporary = null;
            if (image.Recognition is not null && image.Sha256 == hash)
                return Save(image with { Status = "recognized", RelativePath = relativePath, Length = length, Error = null, Attempts = 0, UpdatedAt = now });
            return Save(image with { Status = "downloaded", RelativePath = relativePath, Sha256 = hash, Length = length,
                Recognition = null, Error = null, Attempts = 0, UpdatedAt = now });
        }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
    }

    public ImageEvidenceRecord SaveRecognition(string id, ImageRecognition recognition, DateTimeOffset now)
    {
        var image = Required(id);
        if (GetOriginalPath(image) is null) throw new InvalidOperationException("原图尚未保存。");
        ValidateRecognition(recognition);
        return Save(image with { Status = "recognized", Recognition = recognition, Error = null, Attempts = 0, UpdatedAt = now });
    }

    public ImageEvidenceRecord SetFailure(string id, string stage, string code, DateTimeOffset now)
    {
        if (stage is not ("download" or "ocr")) throw new ArgumentException("图片处理阶段无效。", nameof(stage));
        if (string.IsNullOrWhiteSpace(code) || code.Length > 80 || code.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("图片错误码无效。", nameof(code));
        var image = Required(id);
        return Save(image with { Status = stage + "-failed", Error = code, Attempts = checked(image.Attempts + 1), UpdatedAt = now });
    }

    public ImageEvidenceRecord Retry(string id, DateTimeOffset now)
    {
        var image = Required(id);
        if (image.Status == "recognized" && GetOriginalPath(image) is not null) return image;
        return Save(image with { Status = GetOriginalPath(image) is null ? "pending" : "downloaded", Error = null, Attempts = 0, UpdatedAt = now });
    }

    public string? GetOriginalPath(ImageEvidenceRecord image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateOwner(image.Message, accountId);
        if (image.RelativePath is null) return null;
        ValidateStoredPath(image);
        var path = Path.Combine(accountDirectory, image.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    /// <summary>仅检查快照库，不初始化旧库或修改任何数据。</summary>
    public static IReadOnlyList<ImageEvidenceRecord> ReadAll(string databasePath, string accountId)
    {
        using var db = Open(databasePath, readOnly: true);
        using var owner = db.CreateCommand();
        owner.CommandText = "SELECT account_id FROM store_metadata;";
        using (var rows = owner.ExecuteReader())
            if (!rows.Read() || rows.GetString(0) != accountId || rows.Read()) throw new InvalidDataException("图片数据库账号不匹配。");
        using var exists = db.CreateCommand();
        exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='image_evidence';";
        if (exists.ExecuteScalar() is null) return [];
        using var select = db.CreateCommand();
        select.CommandText = "SELECT id,account_id,group_id,message_id,segment_index,record_json FROM image_evidence ORDER BY rowid;";
        using var reader = select.ExecuteReader();
        var images = new List<ImageEvidenceRecord>();
        while (reader.Read()) images.Add(Read(reader, accountId));
        return images;
    }

    private ImageEvidenceRecord Required(string id) => Find(id) ?? throw new InvalidOperationException("图片记录不存在。");
    private ImageEvidenceRecord Save(ImageEvidenceRecord image)
    {
        using var db = Open(databasePath, readOnly: false);
        using var update = db.CreateCommand();
        update.CommandText = "UPDATE image_evidence SET record_json=$json WHERE id=$id;";
        update.Parameters.AddWithValue("$json", JsonSerializer.Serialize(image));
        update.Parameters.AddWithValue("$id", image.Id);
        if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("图片来源已变化。");
        return image;
    }
    private static ImageEvidenceRecord Read(SqliteDataReader reader, string accountId)
    {
        var image = JsonSerializer.Deserialize<ImageEvidenceRecord>(reader.GetString(5)) ?? throw new InvalidDataException("图片记录为空。");
        ValidateOwner(image.Message, accountId);
        if (image.Id != reader.GetString(0) || image.Message.AccountId != reader.GetString(1) || image.Message.GroupId != reader.GetString(2) ||
            image.Message.MessageId != reader.GetString(3) || image.SegmentIndex != reader.GetInt32(4) || image.SegmentIndex < 0 ||
            image.Id != IdFor(image.Message, image.SegmentIndex) || image.Attempts < 0 || image.Status is not ("pending" or "downloaded" or "recognized" or "download-failed" or "ocr-failed"))
            throw new InvalidDataException("图片记录与来源不一致。");
        if (image.RelativePath is not null) ValidateStoredPath(image);
        else if (image.Sha256 is not null || image.Length is not null || image.Recognition is not null || image.Status is "downloaded" or "recognized" or "ocr-failed")
            throw new InvalidDataException("图片原图关联缺失。");
        if (image.Status == "recognized" && image.Recognition is null) throw new InvalidDataException("图片识别结果缺失。");
        if (image.Recognition is not null) ValidateRecognition(image.Recognition);
        return image;
    }
    private static void ValidateStoredPath(ImageEvidenceRecord image)
    {
        var hash = image.Sha256;
        if (hash is null || hash.Length != 64 || hash.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            image.Length is null or <= 0 or > MaximumImageBytes || image.RelativePath is null ||
            !new[] { "png", "jpg", "gif", "bmp", "tiff", "webp" }.Any(format => image.RelativePath == "images/" + hash + "." + format))
            throw new InvalidDataException("图片原图路径或校验值无效。");
    }
    private static void ValidateRecognition(ImageRecognition recognition)
    {
        if (recognition is null || recognition.Text is null || recognition.Lines is null ||
            recognition.Lines.Any(line => line is null || line.Text is null || !double.IsFinite(line.Confidence) || line.Confidence is < 0 or > 1 ||
                line.Box is null || line.Box.Count != 4 || line.Box.Any(point => point is null || point.Count != 2 || point.Any(value => !double.IsFinite(value)))))
            throw new InvalidDataException("图片识别结果无效。");
    }
    private static string IdFor(MessageKey key, int segmentIndex) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[] { key.AccountId, key.GroupId, key.MessageId, segmentIndex })))).ToLowerInvariant();
    private static void ValidateId(string id)
    {
        if (id is null || id.Length != 64 || id.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("图片标识无效。", nameof(id));
    }
    private static void ValidateOwner(MessageKey key, string accountId)
    {
        if (key is null || key.AccountId != accountId) throw new ArgumentException("图片来源账号不匹配。", nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(key.GroupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.MessageId);
    }
    private static void ValidateLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal)) throw new ArgumentException("图片需要本机绝对路径。", nameof(path));
    }
    private static string DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "png";
        if (header.StartsWith(new byte[] { 255, 216, 255 })) return "jpg";
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8)) return "gif";
        if (header.StartsWith("BM"u8)) return "bmp";
        if (header.StartsWith(new byte[] { 73, 73, 42, 0 }) || header.StartsWith(new byte[] { 77, 77, 0, 42 })) return "tiff";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8)) return "webp";
        throw new InvalidDataException("图片格式不受支持。");
    }
    private static void BindKey(SqliteCommand command, MessageKey key)
    {
        command.Parameters.AddWithValue("$account", key.AccountId);
        command.Parameters.AddWithValue("$group", key.GroupId);
        command.Parameters.AddWithValue("$message", key.MessageId);
    }
    private static SqliteConnection Open(string path, bool readOnly)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            ForeignKeys = true, Pooling = false
        }.ToString());
        try { db.Open(); return db; }
        catch { db.Dispose(); throw; }
    }
}
