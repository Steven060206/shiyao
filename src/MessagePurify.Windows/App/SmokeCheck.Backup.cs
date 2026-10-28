using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Backup;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static readonly string[] BackupChecks = [
        "backup-page-export", "backup-archive-local-data", "backup-incomplete-image",
        "backup-preview-no-overwrite", "backup-preview-cancel", "backup-confirm-restore",
        "backup-task-states-overrides", "backup-history-evidence-courses", "backup-undo-deadline",
        "backup-pre-restore-snapshot", "backup-generation-stale", "backup-damaged-reject",
        "backup-reopen", "backup-desktop-refresh", "backup-layout-preferences", "backup-message-dedup"
    ];

    private sealed record BackupSmokeResult(string ExportPath, string PreRestoreBackupPath,
        int MissingImageCount, long Generation, string Capture, string PreviewCapture);

    private sealed class SyntheticBackupDialogs(string exportPath) : IBackupFileDialogs
    {
        public string? ImportPath { get; set; }
        public int ExportSelections { get; private set; }
        public int ImportSelections { get; private set; }
        public string? SelectExportPath(Window owner, string suggestedName)
        {
            ExportSelections++;
            return exportPath;
        }
        public string? SelectImportPath(Window owner)
        {
            ImportSelections++;
            return ImportPath;
        }
    }

    private static async Task<BackupSmokeResult> CheckBackupRestoreAsync(AppWindow originalWindow,
        AccountWorkspace workspace, AppRuntime runtime, DesktopCardHost desktop, string dataRoot, string reportFile)
    {
        if (workspace.AccountId != "10000" || !Path.GetFileName(dataRoot).StartsWith("messagepurify-app-", StringComparison.Ordinal))
            throw new InvalidOperationException("备份合成检查必须使用隔离的临时工作区。");
        var now = DateTimeOffset.UtcNow;
        HashSet<string> pinned = ["10001", "10101", "10102"];
        var imageKey = new MessageKey(workspace.AccountId, "10001", "native-backup-image");
        var date = now.ToOffset(TimeSpan.FromHours(8)).AddDays(6);
        var imageMessage = new SchoolMessage(imageKey, "10002", now,
            [new(SegmentKind.Text, $"{date.Year}年{date.Month}月{date.Day}日之前提交数学备份图片资料"),
                new(SegmentKind.Image, "", AssetId: "synthetic-not-downloaded-image")]);
        if (!workspace.Ingest(imageMessage, pinned, MessageOrigin.Realtime, "native-backup-image", now))
            throw new InvalidOperationException("备份图片引用样例未入库。");
        var beforeSetup = workspace.Snapshot(now);
        var ignored = beforeSetup.Items.First(item => item.Kind == "review" && item.State == "pending");
        workspace.Execute(new(workspace.AccountId, "ignore", ignored.Id, "native-backup-ignore", ignored.Version,
            Generation: workspace.Generation), now);
        AppStartupPreferences.Save(dataRoot, false);
        runtime.Refresh();

        var exportPath = Path.Combine(dataRoot, "native-current-account.zip");
        var dialogs = new SyntheticBackupDialogs(exportPath) { ImportPath = exportPath };
        var window = new AppWindow(runtime, dataRoot, hidden: true, registerStartup: false, backupFileDialogs: dialogs);
        window.AppearanceChanged += desktop.SetAppearance;
        window.DesktopStatus = originalWindow.DesktopStatus;
        try
        {
            window.Show();
            await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitAsync(window, "document.querySelector('.task-row') !== null");
            await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
            await WaitAsync(window, "document.querySelector('[data-action=export-backup]')?.disabled === false");
            await ChangeSettingAsync(window, "theme-setting", "dark");
            await ChangeSettingAsync(window, "motion-setting", "reduce");
            await ChangeTransparencyAsync(window, "35");
            await desktop.RefreshAsync();
            await WaitAsync(() => HasDesktopTheme(desktop.TaskWindow, true, 35));
            var baseline = workspace.Snapshot(DateTimeOffset.UtcNow);
            var baselineFingerprint = BackupFingerprint(dataRoot, workspace, baseline);
            var baselineSettings = File.ReadAllText(Path.Combine(dataRoot, "accounts", workspace.AccountId, "workspace-settings.json"));
            var baselineLayout = desktop.Layout;
            var baselineGeneration = workspace.Generation;
            var task = baseline.Items.First(item => item.Kind == "task" && item.State == "active");
            var completed = baseline.Items.First(item => item.Kind == "task" && item.State == "completed" && item.UndoExpiresAt is not null);
            var ignoredBaseline = baseline.Items.Single(item => item.Id == ignored.Id);
            await EvaluateAsync(window, "document.querySelector('[data-action=export-backup]').click()");
            await WaitAsync(() => File.Exists(exportPath) && workspace.LastBackup?.Path == exportPath);
            var exported = workspace.LastBackup ?? throw new InvalidDataException("设置页导出没有记录结果。");
            if (dialogs.ExportSelections != 1 || exported.AccountId != workspace.AccountId || exported.IsComplete
                || exported.MissingImageCount != 1 || exported.CreatedAt < now)
                throw new InvalidOperationException("设置页导出没有报告当前账号和未下载原图。");
            await WaitAsync(window, "document.querySelector('#backup-result')?.textContent.includes('未包含原图') === true");
            using (var archive = ZipFile.OpenRead(exportPath))
            {
                string[] required = ["manifest.json", "account/messages.sqlite3", "account/tasks.sqlite3",
                    "account/routing.sqlite3", "account/reminders.sqlite3", "account/workspace-settings.json",
                    "settings/app-settings.json", "settings/interface-preferences.json", "settings/desktop-layout.json"];
                if (required.Any(path => archive.GetEntry(path) is null)
                    || archive.Entries.Any(entry => entry.FullName.Contains("active-account", StringComparison.Ordinal)
                        || entry.FullName.Contains("game-catalog", StringComparison.Ordinal)))
                    throw new InvalidDataException("导出包没有包含四库及共用设置，或带入了本机账号绑定。");
            }

            workspace.Execute(new(workspace.AccountId, "edit-task", task.Id, "native-backup-change-task", task.Version,
                Date: task.Due, Time: task.Time, TimeKind: task.TimeKind, Title: "恢复前临时修改", Course: "恢复前临时课程",
                Priority: 1, Generation: workspace.Generation), DateTimeOffset.UtcNow);
            const string extraId = "native-added-after-backup";
            workspace.Execute(new(workspace.AccountId, "create-task", extraId, "native-backup-add-task", 0,
                Date: date.ToString("yyyy-MM-dd"), TimeKind: "deadline", Title: "导出后新增事项", Course: "新增事项",
                Priority: 2, Generation: workspace.Generation), DateTimeOffset.UtcNow);
            workspace.Execute(new(workspace.AccountId, "confirm-group", "10001", "native-backup-change-course", 0,
                Course: "恢复前临时群课程", Generation: workspace.Generation), DateTimeOffset.UtcNow);
            runtime.Refresh();
            await ChangeSettingAsync(window, "theme-setting", "light");
            await ChangeSettingAsync(window, "motion-setting", "system");
            await ChangeTransparencyAsync(window, "70");
            await ChangeSettingAsync(window, "autostart-setting", "on");
            await WaitAsync(() => AppStartupPreferences.Load(dataRoot));
            var resize = FindThumb(desktop.TaskWindow);
            resize.RaiseEvent(new DragDeltaEventArgs(25, 18));
            resize.RaiseEvent(new DragCompletedEventArgs(25, 18, false));
            if (desktop.Layout == baselineLayout) throw new InvalidOperationException("备份恢复检查没有建立不同的新布局。");
            var changedFingerprint = BackupFingerprint(dataRoot, workspace, workspace.Snapshot(DateTimeOffset.UtcNow));
            if (changedFingerprint == baselineFingerprint) throw new InvalidOperationException("备份恢复检查未产生可核对的新数据。");

            await EvaluateAsync(window, "document.querySelector('[data-action=inspect-backup]').click()");
            await WaitForBackupPreviewAsync(window, exported);
            if (BackupFingerprint(dataRoot, workspace, workspace.Snapshot(DateTimeOffset.UtcNow)) != changedFingerprint
                || workspace.Generation != baselineGeneration)
                throw new InvalidOperationException("仅预览备份已经覆盖了当前数据。");
            if (await EvaluateAsync(window, "document.querySelector('#backup-preview').textContent.includes('共用设置') && document.querySelector('#backup-preview').textContent.includes('未包含原图') && document.querySelector('[data-action=restore-backup]').textContent.includes('确认覆盖并恢复')") != "true")
                throw new InvalidOperationException("恢复预览缺少共用设置、图片范围或显式覆盖确认。");
            await EvaluateAsync(window, "document.querySelector('[data-action=cancel-backup]').click()");
            await WaitAsync(window, "document.querySelector('#backup-preview') === null || document.querySelector('#backup-preview').hidden");
            if (BackupFingerprint(dataRoot, workspace, workspace.Snapshot(DateTimeOffset.UtcNow)) != changedFingerprint)
                throw new InvalidOperationException("取消备份预览改变了当前数据。");
            await EvaluateAsync(window, "document.querySelector('[data-action=inspect-backup]').click()");
            await WaitForBackupPreviewAsync(window, exported);
            var previewCapture = Path.Combine(Path.GetDirectoryName(reportFile)!, Path.GetFileNameWithoutExtension(reportFile) + "-backup-preview.png");
            await CaptureBackupSectionAsync(window, previewCapture, preview: true);
            await EvaluateAsync(window, "document.querySelector('[data-action=restore-backup]').click()");
            await WaitAsync(() => workspace.Generation == baselineGeneration + 1 && !runtime.IsRestoring);
            await WaitAsync(window, "document.querySelector('#backup-result')?.textContent.includes('恢复') === true && document.querySelector('#theme-setting')?.value === 'dark' && document.querySelector('#motion-setting')?.value === 'reduce'");
            var restored = workspace.Snapshot(DateTimeOffset.UtcNow);
            if (BackupFingerprint(dataRoot, workspace, restored) != baselineFingerprint
                || File.ReadAllText(Path.Combine(dataRoot, "accounts", workspace.AccountId, "workspace-settings.json")) != baselineSettings
                || restored.Items.Any(item => item.Id == extraId))
                throw new InvalidDataException("确认恢复没有还原任务状态、人工值、证据、历史、已读和课程关系。");
            if (restored.Items.Single(item => item.Id == completed.Id).UndoExpiresAt != completed.UndoExpiresAt
                || restored.Items.Single(item => item.Id == ignored.Id).UndoExpiresAt != ignoredBaseline.UndoExpiresAt)
                throw new InvalidDataException("恢复延长或丢失了原撤销期限。");
            var result = workspace.LastBackup ?? throw new InvalidDataException("恢复结果未持久化。");
            var preRestorePath = result.PreRestoreBackupPath ?? throw new InvalidDataException("恢复没有先生成自动备份。");
            using (var prepared = workspace.InspectBackup(preRestorePath))
                if (prepared.Preview.TaskCount != exported.TaskCount + 1 || !prepared.ApplicationSettings.AutoStartEnabled
                    || prepared.ApplicationSettings.Theme != "light" || prepared.ApplicationSettings.Motion != "system"
                    || prepared.ApplicationSettings.DesktopTransparency != 70)
                    throw new InvalidDataException("恢复前自动备份没有保留新增事项及后改设置。");
            var preserved = ReadSyntheticBackup(preRestorePath, dataRoot, workspace.AccountId);
            if (preserved.Items.Single(item => item.Id == task.Id).Title != "恢复前临时修改"
                || preserved.Items.All(item => item.Id != extraId)
                || preserved.Groups.Single(group => group.GroupId == "10001").Course != "恢复前临时群课程")
                throw new InvalidDataException("恢复前自动备份缺少被覆盖的新数据。");
            if (AppStartupPreferences.Load(dataRoot)) throw new InvalidDataException("恢复没有还原共用自启动设置。");
            await desktop.RefreshAsync();
            await WaitAsync(() => desktop.Snapshot?.Tasks.Count == restored.Items.Count(item => item.Kind == "task" && item.State == "active")
                && desktop.Snapshot.Pending.Count == restored.Items.Count(item => item.Kind is "review" or "merge-review" && item.State == "pending")
                && desktop.Layout == baselineLayout
                && HasDesktopTheme(desktop.TaskWindow, true, 35));
            await CheckStaleBackupCommandAsync(window, workspace, task, baselineGeneration);
            if (workspace.Ingest(imageMessage, pinned, MessageOrigin.History, "native-backup-replay", DateTimeOffset.UtcNow)
                || new AccountMessageStore(dataRoot, workspace.AccountId).Find(imageKey)?.Message.Segments.Single(segment => segment.Kind == SegmentKind.Image).AssetId != "synthetic-not-downloaded-image")
                throw new InvalidDataException("恢复丢失图片引用或重读原消息生成重复记录。");

            dialogs.ImportPath = Path.Combine(dataRoot, "native-damaged-backup.zip");
            File.WriteAllBytes(dialogs.ImportPath, [0x50, 0x4b, 0, 0]);
            await EvaluateAsync(window, "document.querySelector('[data-action=inspect-backup]').click()");
            await WaitAsync(window, "document.querySelector('#backup-error')?.hidden === false && document.querySelector('#backup-error').textContent.length > 0");
            if (dialogs.ImportSelections != 3 || BackupFingerprint(dataRoot, workspace, workspace.Snapshot(DateTimeOffset.UtcNow)) != baselineFingerprint
                || workspace.Generation != baselineGeneration + 1 || workspace.LastBackup != result)
                throw new InvalidDataException("损坏备份被接受或改变了恢复后的数据及结果。");
            await CheckBackupReopenAsync(dataRoot, workspace.AccountId, baseline, baselineFingerprint, result);
            var capture = Path.Combine(Path.GetDirectoryName(reportFile)!, Path.GetFileNameWithoutExtension(reportFile) + "-backup.png");
            await CaptureBackupSectionAsync(window, capture, preview: false);
            return new(exportPath, preRestorePath, result.MissingImageCount, workspace.Generation, capture, previewCapture);
        }
        finally
        {
            window.AppearanceChanged -= desktop.SetAppearance;
            window.Exiting = true;
            window.Close();
        }
    }

    private static Task ChangeSettingAsync(AppWindow window, string id, string value) => EvaluateAsync(window,
        "document.getElementById(" + JsonSerializer.Serialize(id) + ").value=" + JsonSerializer.Serialize(value)
        + ";document.getElementById(" + JsonSerializer.Serialize(id) + ").dispatchEvent(new Event('change',{bubbles:true}))");

    private static async Task CaptureBackupSectionAsync(AppWindow window, string path, bool preview)
    {
        window.ShowMainWindow();
        await EvaluateAsync(window, "document.querySelector('.backup-section').scrollIntoView({block:'start',behavior:'instant'});"
            + (preview ? "if(document.querySelector('[data-action=restore-backup]').getBoundingClientRect().bottom > innerHeight-20) document.querySelector('#backup-preview').scrollIntoView({block:'center',behavior:'instant'});" : "")
            + "window.__nativeBackupCaptureReady=false;requestAnimationFrame(()=>requestAnimationFrame(()=>{window.__nativeBackupCaptureReady=true;}));");
        await WaitAsync(window, "window.__nativeBackupCaptureReady === true");
        await using var file = File.Create(path);
        await window.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
    }

    private static Task WaitForBackupPreviewAsync(AppWindow window, BackupResult result) => WaitAsync(window,
        "(() => {const p=document.querySelector('#backup-preview');return p && !p.hidden && p.dataset.accountId==="
        + JsonSerializer.Serialize(result.AccountId) + " && Number(p.dataset.taskCount)===" + result.TaskCount
        + " && Number(p.dataset.reviewCount)===" + result.ReviewCount + " && Number(p.dataset.noticeCount)===" + result.NoticeCount
        + " && Date.parse(p.dataset.createdAt)===" + result.CreatedAt.ToUnixTimeMilliseconds() + ";})()");

    private static string BackupFingerprint(string dataRoot, AccountWorkspace workspace, ApplicationSnapshot snapshot)
    {
        var store = new AccountTaskStore(dataRoot, workspace.AccountId);
        return JsonSerializer.Serialize(new { tasks = store.ListTasks(includeMerged: true).OrderBy(item => item.Id),
            reviews = store.ListReviews().OrderBy(item => item.Id), history = store.ListTaskHistory(),
            items = snapshot.Items.OrderBy(item => item.Id), groups = snapshot.Groups.OrderBy(group => group.GroupId) }, AppWindow.JsonOptions);
    }

    private static ApplicationSnapshot ReadSyntheticBackup(string path, string dataRoot, string accountId)
    {
        var isolatedRoot = Path.Combine(dataRoot, "native-pre-restore-inspection");
        var accountPath = Path.Combine(isolatedRoot, "accounts", accountId);
        Directory.CreateDirectory(accountPath);
        using var archive = ZipFile.OpenRead(path);
        foreach (var name in new[] { "messages.sqlite3", "tasks.sqlite3", "routing.sqlite3", "reminders.sqlite3", "workspace-settings.json" })
        {
            var entry = archive.GetEntry("account/" + name) ?? throw new InvalidDataException("自动备份缺少账号文件。");
            entry.ExtractToFile(Path.Combine(accountPath, name));
        }
        return new AccountWorkspace(isolatedRoot, accountId).Snapshot(DateTimeOffset.UtcNow);
    }

    private static async Task CheckStaleBackupCommandAsync(AppWindow window, AccountWorkspace workspace,
        ApplicationItem task, long staleGeneration)
    {
        var request = JsonSerializer.Serialize(new { id = "native-stale-backup-command", operation = "complete",
            parameters = new { accountId = workspace.AccountId, id = task.Id, operationId = "native-stale-after-restore",
                version = task.Version, generation = staleGeneration } }, AppWindow.JsonOptions);
        await EvaluateAsync(window, "window.__nativeBackupResponse=null;window.chrome.webview.addEventListener('message',event=>{if(event.data.id==='native-stale-backup-command')window.__nativeBackupResponse=event.data;});window.chrome.webview.postMessage(" + request + ")");
        await WaitAsync(window, "window.__nativeBackupResponse?.success === false");
        if (workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == task.Id).State != "active")
            throw new InvalidOperationException("恢复前迟到的页面操作改变了恢复后的任务。");
    }

    private static async Task CheckBackupReopenAsync(string dataRoot, string accountId, ApplicationSnapshot baseline,
        string fingerprint, BackupResult expectedResult)
    {
        var workspace = new AccountWorkspace(dataRoot, accountId);
        workspace.SetAvailableGroups(baseline.Groups.Select(group => new OneBotGroup(group.GroupId, group.Name)).ToArray(),
            baseline.Groups.Where(group => group.Pinned).Select(group => group.GroupId).ToHashSet());
        if (BackupFingerprint(dataRoot, workspace, workspace.Snapshot(DateTimeOffset.UtcNow)) != fingerprint
            || workspace.LastBackup != expectedResult)
            throw new InvalidDataException("重开工作区丢失整份恢复结果。");
        var runtime = new AppRuntime(dataRoot, workspace);
        var window = new AppWindow(runtime, dataRoot, hidden: true, registerStartup: false);
        try
        {
            window.Show();
            await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitAsync(window, "document.querySelector('.task-row') !== null");
            await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
            await WaitAsync(window, "document.querySelector('#backup-result')?.textContent.includes('恢复') === true && document.querySelector('#theme-setting')?.value === 'dark' && document.querySelector('#motion-setting')?.value === 'reduce' && document.querySelector('#autostart-setting')?.value === 'off'");
        }
        finally
        {
            window.Exiting = true;
            window.Close();
            await runtime.StopAsync();
        }
    }
}
