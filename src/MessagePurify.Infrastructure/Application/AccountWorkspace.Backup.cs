using MessagePurify.Infrastructure.Backup;
using System.Text.Json;

namespace MessagePurify.Infrastructure.Application;

public sealed partial class AccountWorkspace
{
    private readonly AccountBackupService backup;
    public long Generation { get; private set; }
    public BackupResult? LastBackup { get { lock (gate) return backup.GetLastResult(); } }

    public BackupResult ExportBackup(string destination, BackupApplicationSettings applicationSettings, DateTimeOffset now)
    {
        lock (gate) return backup.Export(destination, JsonSerializer.Serialize(settings), applicationSettings, now);
    }

    public PreparedAccountBackup InspectBackup(string source) => backup.Inspect(source);

    public BackupResult RestoreBackup(PreparedAccountBackup prepared, BackupApplicationSettings currentSettings,
        DateTimeOffset now, Action<BackupApplicationSettings>? applySettings = null)
    {
        lock (gate)
        {
            var result = backup.Restore(prepared, JsonSerializer.Serialize(settings), currentSettings, now, preferences =>
            {
                settings = ReadSettings();
                applySettings?.Invoke(preferences);
            });
            cacheCleanupError = null;
            ConsolidateLegacyMessageReviews(now);
            Generation++;
            return result;
        }
    }
}
