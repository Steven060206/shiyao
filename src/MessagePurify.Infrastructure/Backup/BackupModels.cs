namespace MessagePurify.Infrastructure.Backup;

public sealed record BackupApplicationSettings(bool AutoStartEnabled, string Theme, string Motion,
    string? DesktopLayoutJson);

public sealed record BackupResult(string AccountId, string Path, DateTimeOffset CreatedAt,
    bool IsComplete, int MissingImageCount, int TaskCount, int ReviewCount, int NoticeCount,
    string? PreRestoreBackupPath = null);

public sealed class PreparedAccountBackup : IDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public BackupResult Preview { get; }
    public BackupApplicationSettings ApplicationSettings { get; }
    internal string DirectoryPath { get; }
    internal bool IsDisposed { get; private set; }

    internal PreparedAccountBackup(BackupResult preview, BackupApplicationSettings settings, string directory)
        => (Preview, ApplicationSettings, DirectoryPath) = (preview, settings, directory);

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}
