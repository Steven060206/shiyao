using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Backup;
using MessagePurify.Infrastructure.Storage;

namespace MessagePurify.Infrastructure.Tests;

public sealed class BackupWorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-backup-workspace-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.FromHours(8));
    private static readonly BackupApplicationSettings Preferences = new(false, "dark", "reduce", null);
    private AccountWorkspace Workspace() => new(root, "12345");
    private string Archive => Path.Combine(root, "snapshot.zip");
    private static WorkspaceCommand Create(string id, long? generation = null) => new("12345", "create-task", id,
        "create-" + id, 0, Date: "2026-10-20", TimeKind: "deadline", Course: "数学", Title: id, Generation: generation);

    [Fact]
    public void RestoreReplacesAllDataAndReloadsCourseSettingsOnOriginalWorkspace()
    {
        var workspace = Workspace();
        workspace.SetAvailableGroups([new("100", "数学群")], new HashSet<string> { "100" });
        workspace.Execute(new("12345", "confirm-group", "100", "confirm", 0, Course: "数学"), Now);
        workspace.Execute(Create("original"), Now);
        workspace.ExportBackup(Archive, Preferences, Now);
        workspace.Execute(new("12345", "confirm-group", "100", "change", 0, Course: "旧课程", Excluded: true), Now);
        workspace.Execute(Create("later"), Now);
        using var prepared = workspace.InspectBackup(Archive);

        var restored = workspace.RestoreBackup(prepared, Preferences, Now.AddMinutes(1));

        var snapshot = workspace.Snapshot(Now.AddMinutes(1));
        Assert.Equal("original", Assert.Single(snapshot.Items).Id);
        Assert.Equal("数学", Assert.Single(snapshot.Groups).Course);
        Assert.False(Assert.Single(snapshot.Groups).Excluded);
        Assert.Equal(1, snapshot.Generation);
        Assert.NotNull(restored.PreRestoreBackupPath);
        Assert.True(File.Exists(restored.PreRestoreBackupPath));
        workspace.Execute(Create("after", workspace.Generation), Now.AddMinutes(2));
        Assert.Equal(2, Workspace().Snapshot(Now.AddMinutes(2)).Items.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void RequestsCapturedBeforeRestoreCannotWriteRestoredAccount(long? oldGeneration)
    {
        var workspace = Workspace();
        workspace.Execute(Create("original"), Now);
        workspace.ExportBackup(Archive, Preferences, Now);
        using var prepared = workspace.InspectBackup(Archive);
        workspace.RestoreBackup(prepared, Preferences, Now);

        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Create("stale", oldGeneration), Now));
        Assert.Equal("original", Assert.Single(workspace.Snapshot(Now).Items).Id);
        workspace.Execute(Create("current", workspace.Generation), Now);
        Assert.Equal(2, workspace.Snapshot(Now).Items.Count);
    }

    [Fact]
    public async Task ReceivingWaitsForSwitchAndResumesOnSameAccount()
    {
        var workspace = Workspace();
        workspace.ExportBackup(Archive, Preferences, Now);
        using var prepared = workspace.InspectBackup(Archive);
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var restore = Task.Run(() => workspace.RestoreBackup(prepared, Preferences, Now, _ =>
        {
            entered.Set();
            Assert.True(resume.Wait(TimeSpan.FromSeconds(10)));
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var pins = new HashSet<string> { "100" };
        var receive = Task.Run(() => workspace.Ingest(new(new("12345", "100", "received"), "teacher", Now,
            [new(SegmentKind.Text, "请提交数学作业")]), pins, MessageOrigin.Realtime, "during-restore", Now));
        try { Assert.NotSame(receive, await Task.WhenAny(receive, Task.Delay(150))); }
        finally { resume.Set(); }
        await restore;
        Assert.True(await receive);
        Assert.Single(workspace.Snapshot(Now).Items);
    }

    [Fact]
    public void FailedSwitchKeepsCurrentSettingsDataAndGeneration()
    {
        var workspace = Workspace();
        workspace.Execute(Create("original"), Now);
        workspace.ExportBackup(Archive, Preferences, Now);
        workspace.Execute(Create("later"), Now);
        using var prepared = workspace.InspectBackup(Archive);

        Assert.Throws<IOException>(() => workspace.RestoreBackup(prepared, Preferences, Now,
            _ => throw new IOException("settings-failure")));

        Assert.Equal(0, workspace.Generation);
        Assert.Equal(2, workspace.Snapshot(Now).Items.Count);
        workspace.Execute(Create("still-valid", 0), Now);
        Assert.Equal(3, workspace.Snapshot(Now).Items.Count);
    }

    [Fact]
    public void ResultWriteFailureReloadsRolledBackSettingsEvenWhenResultFileStaysLocked()
    {
        var workspace = Workspace();
        workspace.SetAvailableGroups([new("100", "数学群")], new HashSet<string> { "100" });
        workspace.Execute(new("12345", "confirm-group", "100", "first-course", 0, Course: "备份课程"), Now);
        workspace.ExportBackup(Archive, Preferences, Now);
        workspace.Execute(new("12345", "confirm-group", "100", "current-course", 0, Course: "当前课程"), Now);
        using var prepared = workspace.InspectBackup(Archive);
        FileStream? resultLock = null;
        try
        {
            Assert.ThrowsAny<IOException>(() => workspace.RestoreBackup(prepared, Preferences, Now, _ =>
            {
                resultLock ??= new FileStream(Path.Combine(root, "backup-history.json"), FileMode.Open, FileAccess.Read, FileShare.None);
            }));
        }
        finally { resultLock?.Dispose(); }

        Assert.Equal("当前课程", Assert.Single(workspace.Snapshot(Now).Groups).Course);
        Assert.Equal(0, workspace.Generation);
        AccountBackupService.RecoverInterrupted(root);
        Assert.Equal("当前课程", Assert.Single(workspace.Snapshot(Now).Groups).Course);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
