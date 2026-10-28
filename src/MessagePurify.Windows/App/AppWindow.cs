using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Backup;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MessagePurify.Windows.App;

public sealed class AppWindow : Window
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal readonly WebView2 Browser = new();
    private readonly AppRuntime runtime;
    private readonly string dataRoot;
    private readonly bool registerStartup;
    private readonly IBackupFileDialogs backupFileDialogs;
    private PreparedAccountBackup? backupPreview;
    private long backupPreviewGeneration;
    private bool backupBusy;
    private readonly InterfacePreferences? initialPreferences;
    private bool initialPreferencesSent;
    private sealed record InterfacePreferences(string Theme, string Motion, int? DesktopTransparency = null);
    private readonly DispatcherTimer updates = new() { Interval = TimeSpan.FromSeconds(1) };
    private int dirty = 1;
    private bool posting;
    private bool disposed;
    private bool autoStartEnabled;
    public bool Exiting { get; set; }
    public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action<string, bool, int?>? AppearanceChanged;
    public object DesktopStatus { get; set; } = new { state = "waiting", error = (string?)null };

    public AppWindow(AppRuntime runtime, string dataRoot, bool hidden, bool registerStartup = true, IBackupFileDialogs? backupFileDialogs = null)
    {
        this.runtime = runtime;
        this.dataRoot = dataRoot;
        this.registerStartup = registerStartup;
        this.backupFileDialogs = backupFileDialogs ?? new BackupFileDialogs();
        initialPreferences = LoadInterfacePreferences();
        autoStartEnabled = AppStartupPreferences.Load(dataRoot);
        if (registerStartup && StartupRegistration.IsEnabled() != autoStartEnabled) StartupRegistration.SetEnabled(autoStartEnabled);
        Title = "拾要";
        SourceInitialized += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            AppIdentity.SetWindow(handle);
            WindowTheme.Apply(handle, WindowTheme.InitialDark(initialPreferences?.Theme));
        };
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/ui/assets/shiyao.ico"));
        Width = 1360; Height = 880; MinWidth = 1000; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = Browser;
        if (hidden) { Opacity = 0; ShowInTaskbar = false; ShowActivated = false; }
        runtime.Changed += OnChanged;
        Loaded += async (_, _) => await InitializeAsync();
        Closing += (_, args) =>
        {
            if (!Exiting)
            {
                args.Cancel = true;
                if (!backupBusy) { ReleaseBackupPreview(); Post(new { type = "state", backupPreview = (object?)null }); }
                Hide();
            }
        };
        Closed += (_, _) => { disposed = true; ReleaseBackupPreview(); updates.Stop(); runtime.Changed -= OnChanged; Browser.Dispose(); };
        updates.Tick += async (_, _) =>
        {
            if (posting || Interlocked.Exchange(ref dirty, 0) == 0 || Browser.CoreWebView2 is null) return;
            posting = true;
            try { await PublishAsync(); }
            finally { posting = false; }
        };
    }

    private void OnChanged() => Interlocked.Exchange(ref dirty, 1);

    public void ShowMainWindow()
    {
        Opacity = 1;
        ShowInTaskbar = true;
        ShowActivated = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataRoot, "WebView2"));
            await Browser.EnsureCoreWebView2Async(environment);
            var core = Browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping(AppBridgeProtocol.Host, Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) => { if (!AppBridgeProtocol.IsTrustedSource(args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IdnHost != AppBridgeProtocol.Host || uri.Port != 443)
                    args.Response = environment.CreateWebResourceResponse(Stream.Null, 403, "Forbidden", "");
            };
            core.WebMessageReceived += async (_, args) => await HandleAsync(args.Source, args.WebMessageAsJson);
            core.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    // WebView2 needs the initial Show; hide its transparent host once navigation is ready.
                    if (Opacity == 0) Hide();
                    Ready.TrySetResult(true);
                    updates.Start();
                }
                else Ready.TrySetException(new InvalidOperationException("本地界面加载失败。"));
            };
            core.Navigate(AppBridgeProtocol.PageUrl);
        }
        catch (Exception)
        {
            Ready.TrySetException(new InvalidOperationException("无法加载 WebView2 或本地界面。"));
            if (Opacity != 0) System.Windows.MessageBox.Show(this, "无法加载本地界面，请检查 WebView2 运行时。", "拾要");
        }
    }

    private async Task HandleAsync(string source, string json)
    {
        if (!AppBridgeProtocol.TryParse(source, json, out var request))
        {
            if (AppBridgeProtocol.IsTrustedSource(source)) Post(new { id = SafeRequestId(json), success = false, error = "请求参数无效，请检查日期和时刻。" });
            return;
        }
        var error = (string?)null;
        try
        {
            if (runtime.IsRestoring && request!.Operation is not ("snapshot" or "open-backup-location"))
                throw new InvalidOperationException("账号正在恢复，请等待结果后再操作。");
            if (request!.Operation == "sync-now")
            {
                var started = runtime.RequestSync();
                Post(new { id = request.Id, success = true, syncRequested = started, status = runtime.Status });
                return;
            }
            if (request!.Operation == "set-appearance")
            {
                var transparency = request.Parameters.TryGetProperty("desktopTransparency", out var value) && value.ValueKind != JsonValueKind.Null
                    ? value.GetInt32() : (int?)null;
                if (request.Parameters.TryGetProperty("preferenceTheme", out var preferenceTheme)
                    && (initialPreferences is null || initialPreferencesSent))
                    SaveInterfacePreferences(new(preferenceTheme.GetString()!, request.Parameters.GetProperty("preferenceMotion").GetString()!, transparency));
                WindowTheme.Apply(new System.Windows.Interop.WindowInteropHelper(this).Handle,
                    request.Parameters.GetProperty("theme").GetString() == "dark");
                AppearanceChanged?.Invoke(request.Parameters.GetProperty("theme").GetString()!,
                    request.Parameters.GetProperty("reducedMotion").GetBoolean(), transparency);
                Post(new { id = request.Id, success = true });
                return;
            }
            if (request!.Operation == "set-autostart")
            {
                var enabled = request.Parameters.GetProperty("enabled").GetBoolean();
                var previous = autoStartEnabled;
                try
                {
                    if (registerStartup) StartupRegistration.SetEnabled(enabled);
                    AppStartupPreferences.Save(dataRoot, enabled);
                    autoStartEnabled = enabled;
                    Post(new { id = request.Id, success = true, startupEnabled = autoStartEnabled });
                }
                catch (Exception exception)
                {
                    try { if (registerStartup) StartupRegistration.SetEnabled(previous); } catch { }
                    var code = exception is InvalidOperationException { Message: "startup-registration-unavailable" }
                        ? "startup-registration-unavailable" : "startup-settings-unavailable";
                    Post(new { id = request.Id, success = false, error = code, startupEnabled = autoStartEnabled });
                }
                return;
            }
            if (request!.Operation is "export-backup" or "inspect-backup" or "restore-backup" or "cancel-backup" or "open-backup-location")
            {
                await HandleBackupAsync(request);
                return;
            }
            if (request!.Operation != "snapshot")
            {
                var workspace = runtime.Workspace ?? throw new InvalidOperationException("尚未连接到 QQ 账号。");
                var p = request.Parameters;
                var command = new WorkspaceCommand(p.GetProperty("accountId").GetString()!, request.Operation,
                    p.GetProperty("id").GetString()!, p.GetProperty("operationId").GetString()!, p.GetProperty("version").GetInt64(),
                    Optional(p, "date"), Optional(p, "time"), Optional(p, "timeKind"), Optional(p, "undoOperationId"),
                    p.TryGetProperty("targetTaskVersion", out var target) && target.ValueKind != JsonValueKind.Null ? target.GetInt64() : null,
                    Optional(p, "course"), p.TryGetProperty("excluded", out var excluded) && excluded.GetBoolean(),
                    Title: Optional(p, "title"),
                    Priority: p.TryGetProperty("priority", out var priority) && priority.ValueKind != JsonValueKind.Null ? priority.GetInt32() : null,
                    MergeWithId: Optional(p, "mergeWithId"),
                    MergeWithVersion: p.TryGetProperty("mergeWithVersion", out var mergeVersion) ? mergeVersion.GetInt64() : null,
                    MergeSelection: p.TryGetProperty("mergeSelection", out var selection) ? selection.Deserialize<TaskMergeSelection>(JsonOptions) : null,
                    NewItemId: Optional(p, "newItemId"), State: Optional(p, "state"),
                    EvidenceIndexes: p.TryGetProperty("evidenceIndexes", out var indexes) ? indexes.Deserialize<int[]>() : null,
                    Generation: p.TryGetProperty("generation", out var generation) ? generation.GetInt64() : 0);
                await Task.Run(() => workspace.Execute(command, DateTimeOffset.UtcNow));
                runtime.Refresh();
            }
        }
        catch (ArgumentException exception) { error = exception.Message; }
        catch (InvalidOperationException exception)
        {
            error = request!.Operation is "edit-task" or "merge-tasks" or "keep-separate" or "split-task"
                && exception.Message == "记录已变化，请重新读取后再操作。"
                ? "version_conflict" : "事项或连接已变化，请核对最新内容后重试。";
        }
        catch (Exception) { error = "本地数据操作失败，原数据已保留。"; }
        await PublishAsync(request!.Id, error);
    }

    private async Task PublishAsync(string? requestId = null, string? error = null)
    {
        ApplicationSnapshot? snapshot = null;
        try { snapshot = await Task.Run(() => runtime.Workspace?.Snapshot(DateTimeOffset.UtcNow)); }
        catch (Exception) { error ??= "读取本地数据失败，原数据已保留。"; }
        var preferences = initialPreferencesSent ? null : initialPreferences;
        initialPreferencesSent = true;
        if (requestId is null) Post(new { type = "state", snapshot, status = runtime.Status, desktop = DesktopStatus, startupEnabled = autoStartEnabled, isRestoring = runtime.IsRestoring, backupResult = runtime.Workspace?.LastBackup, preferences, error });
        else Post(new { id = requestId, success = error is null, error, snapshot, status = runtime.Status, desktop = DesktopStatus, startupEnabled = autoStartEnabled, isRestoring = runtime.IsRestoring, backupResult = runtime.Workspace?.LastBackup, preferences });
    }

    private async Task HandleBackupAsync(AppBridgeRequest request)
    {
        var workspace = runtime.Workspace ?? throw new InvalidOperationException("尚未连接到 QQ 账号。");
        var p = request.Parameters;
        var accountId = p.GetProperty("accountId").GetString()!;
        if (workspace.AccountId != accountId) throw new InvalidOperationException("account_mismatch");
        if (p.TryGetProperty("generation", out var generation) && generation.GetInt64() != workspace.Generation)
            throw new InvalidOperationException("账号数据已恢复，请刷新后重新确认。");
        if (backupBusy) throw new InvalidOperationException("备份操作正在进行，请等待结果。");
        if (request.Operation == "cancel-backup")
        {
            if (backupPreview is null || backupPreview.Id != p.GetProperty("previewId").GetString() || backupPreview.Preview.AccountId != accountId)
                throw new InvalidOperationException("恢复预览已失效，请重新选择备份。");
            ReleaseBackupPreview(); Post(new { id = request.Id, success = true }); return;
        }
        if (request.Operation == "open-backup-location")
        {
            var result = workspace.LastBackup ?? throw new InvalidOperationException("尚无备份结果。");
            var directory = Path.GetDirectoryName(result.Path);
            if (directory is null || !Directory.Exists(directory)) throw new InvalidOperationException("备份位置已不存在，请核对本地文件。");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { Arguments = $"\"{directory}\"", UseShellExecute = true });
            Post(new { id = request.Id, success = true }); return;
        }
        backupBusy = true;
        try
        {
            if (request.Operation == "export-backup")
            {
                var path = backupFileDialogs.SelectExportPath(this, $"拾要-{accountId}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                if (path is null) { Post(new { id = request.Id, success = true, cancelled = true }); return; }
                var settings = CurrentBackupSettings(p);
                var result = await Task.Run(() => workspace.ExportBackup(path, settings, DateTimeOffset.UtcNow));
                Post(new { id = request.Id, success = true, backupResult = result });
            }
            else if (request.Operation == "inspect-backup")
            {
                ReleaseBackupPreview();
                var path = backupFileDialogs.SelectImportPath(this);
                if (path is null) { Post(new { id = request.Id, success = true, cancelled = true }); return; }
                var prepared = await Task.Run(() => workspace.InspectBackup(path));
                if (disposed || runtime.Workspace != workspace || workspace.Generation != generation.GetInt64())
                {
                    prepared.Dispose(); throw new InvalidOperationException("恢复预览已失效，请重新选择备份。");
                }
                backupPreview = prepared; backupPreviewGeneration = workspace.Generation;
                Post(new { id = request.Id, success = true, backupPreview = new { prepared.Id, generation = backupPreviewGeneration, prepared.Preview, prepared.ApplicationSettings } });
            }
            else
            {
                var prepared = backupPreview;
                if (prepared is null || prepared.Id != p.GetProperty("previewId").GetString() || prepared.Preview.AccountId != accountId
                    || backupPreviewGeneration != workspace.Generation) throw new InvalidOperationException("恢复预览已失效，请重新选择备份。");
                var settings = CurrentBackupSettings(p);
                var result = await runtime.RestoreBackupAsync(prepared, settings, restored =>
                {
                    if (registerStartup) StartupRegistration.SetEnabled(restored.AutoStartEnabled);
                    autoStartEnabled = restored.AutoStartEnabled;
                });
                var restoredPreferences = new { theme = prepared.ApplicationSettings.Theme, motion = prepared.ApplicationSettings.Motion,
                    desktopTransparency = prepared.ApplicationSettings.DesktopTransparency };
                ReleaseBackupPreview();
                var snapshot = await Task.Run(() => workspace.Snapshot(DateTimeOffset.UtcNow));
                Post(new { id = request.Id, success = true, backupResult = result, snapshot, preferences = restoredPreferences, startupEnabled = autoStartEnabled, isRestoring = false });
            }
            runtime.Refresh();
        }
        finally { backupBusy = false; }
    }

    private BackupApplicationSettings CurrentBackupSettings(JsonElement parameters)
    {
        var path = Path.Combine(dataRoot, "desktop-layout.json");
        return new(autoStartEnabled, parameters.GetProperty("theme").GetString()!, parameters.GetProperty("motion").GetString()!,
            File.Exists(path) ? File.ReadAllText(path) : null,
            parameters.TryGetProperty("desktopTransparency", out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt32() : null);
    }

    private void ReleaseBackupPreview()
    {
        var prepared = backupPreview; backupPreview = null;
        if (prepared is null) return;
        try { prepared.Dispose(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private InterfacePreferences? LoadInterfacePreferences()
    {
        var path = Path.Combine(dataRoot, "interface-preferences.json");
        try
        {
            if (!File.Exists(path)) return null;
            var value = JsonSerializer.Deserialize<InterfacePreferences>(File.ReadAllText(path), JsonOptions);
            return value is { Theme: "system" or "light" or "dark", Motion: "system" or "reduce" }
                && value.DesktopTransparency is not (< 0 or > 100) ? value : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private void SaveInterfacePreferences(InterfacePreferences value)
    {
        Directory.CreateDirectory(dataRoot);
        var path = Path.Combine(dataRoot, "interface-preferences.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task OpenItemAsync(string accountId, string id)
    {
        await Ready.Task;
        if (runtime.Workspace?.AccountId != accountId) return;
        ShowMainWindow();
        await PublishAsync();
        Post(new { type = "open-item", accountId, id });
    }

    private void Post(object message)
    {
        if (!disposed && Browser.CoreWebView2 is not null) Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
    }

    private static string? Optional(JsonElement parameters, string property) => parameters.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    private static string? SafeRequestId(string json)
    {
        if (json.Length > 16384) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString()!.Length <= 128 ? id.GetString() : null;
        }
        catch (Exception) { return null; }
    }
}
