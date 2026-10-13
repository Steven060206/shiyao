using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Backup;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class BackupRuntimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-runtime-backup-" + Guid.NewGuid().ToString("N"));
    private static readonly BackupApplicationSettings Settings = new(false, "system", "system", null);

    [Fact]
    public async Task RestorePausesOperationsAndRejectsSecondSwitchUntilResult()
    {
        var workspace = new AccountWorkspace(root, "12345");
        var package = Path.Combine(root, "snapshot.zip");
        workspace.ExportBackup(package, Settings, DateTimeOffset.UtcNow);
        using var prepared = workspace.InspectBackup(package);
        using var second = workspace.InspectBackup(package);
        var runtime = new AppRuntime(root, workspace);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var operation = runtime.RestoreBackupAsync(prepared, Settings, _ =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            Assert.True(runtime.IsRestoring);
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RestoreBackupAsync(second, Settings));
            Assert.True(runtime.IsRestoring);
        }
        finally { release.Set(); }
        await operation;
        Assert.False(runtime.IsRestoring);
        Assert.Same(workspace, runtime.Workspace);
        Assert.Equal(1, workspace.Generation);
        await runtime.StopAsync();
    }

    [Fact]
    public async Task FailedRestoreUnpausesAndKeepsCurrentWorkspaceGeneration()
    {
        var workspace = new AccountWorkspace(root, "12345");
        var package = Path.Combine(root, "snapshot.zip");
        workspace.ExportBackup(package, Settings, DateTimeOffset.UtcNow);
        using var prepared = workspace.InspectBackup(package);
        var runtime = new AppRuntime(root, workspace);

        await Assert.ThrowsAsync<IOException>(() => runtime.RestoreBackupAsync(prepared, Settings,
            _ => throw new IOException("settings-unavailable")));

        Assert.False(runtime.IsRestoring);
        Assert.Same(workspace, runtime.Workspace);
        Assert.Equal(0, workspace.Generation);
        Assert.Empty(workspace.Snapshot(DateTimeOffset.UtcNow).Items);
        await runtime.StopAsync();
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
