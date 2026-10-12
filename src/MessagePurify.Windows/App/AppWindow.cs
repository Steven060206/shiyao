using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using MessagePurify.Infrastructure.Application;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MessagePurify.Windows.App;

public sealed class AppWindow : Window
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal readonly WebView2 Browser = new();
    private readonly AppRuntime runtime;
    private readonly string dataRoot;
    private readonly DispatcherTimer updates = new() { Interval = TimeSpan.FromSeconds(1) };
    private int dirty = 1;
    private bool posting;
    private bool disposed;
    public bool Exiting { get; set; }
    public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AppWindow(AppRuntime runtime, string dataRoot, bool hidden)
    {
        this.runtime = runtime;
        this.dataRoot = dataRoot;
        Title = "消息提纯";
        Width = 1360; Height = 880; MinWidth = 1000; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = Browser;
        if (hidden) { Opacity = 0; ShowInTaskbar = false; }
        runtime.Changed += OnChanged;
        Loaded += async (_, _) => await InitializeAsync();
        Closing += (_, args) => { if (!Exiting) { args.Cancel = true; Hide(); } };
        Closed += (_, _) => { disposed = true; updates.Stop(); runtime.Changed -= OnChanged; Browser.Dispose(); };
        updates.Tick += async (_, _) =>
        {
            if (posting || Interlocked.Exchange(ref dirty, 0) == 0 || Browser.CoreWebView2 is null) return;
            posting = true;
            try { await PublishAsync(); }
            finally { posting = false; }
        };
    }

    private void OnChanged() => Interlocked.Exchange(ref dirty, 1);

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
                if (args.IsSuccess) { Ready.TrySetResult(true); updates.Start(); }
                else Ready.TrySetException(new InvalidOperationException("本地界面加载失败。"));
            };
            core.Navigate(AppBridgeProtocol.PageUrl);
        }
        catch (Exception)
        {
            Ready.TrySetException(new InvalidOperationException("无法加载 WebView2 或本地界面。"));
            if (Opacity != 0) System.Windows.MessageBox.Show(this, "无法加载本地界面，请检查 WebView2 运行时。", "消息提纯");
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
            if (request!.Operation != "snapshot")
            {
                var workspace = runtime.Workspace ?? throw new InvalidOperationException("尚未连接到 QQ 账号。");
                var p = request.Parameters;
                var command = new WorkspaceCommand(p.GetProperty("accountId").GetString()!, request.Operation,
                    p.GetProperty("id").GetString()!, p.GetProperty("operationId").GetString()!, p.GetProperty("version").GetInt64(),
                    Optional(p, "date"), Optional(p, "time"), Optional(p, "timeKind"), Optional(p, "undoOperationId"),
                    p.TryGetProperty("targetTaskVersion", out var target) && target.ValueKind != JsonValueKind.Null ? target.GetInt64() : null,
                    Optional(p, "course"), p.TryGetProperty("excluded", out var excluded) && excluded.GetBoolean());
                await Task.Run(() => workspace.Execute(command, DateTimeOffset.UtcNow));
            }
        }
        catch (ArgumentException exception) { error = exception.Message; }
        catch (InvalidOperationException) { error = "事项或连接已变化，请核对最新内容后重试。"; }
        catch (Exception) { error = "本地数据操作失败，原数据已保留。"; }
        await PublishAsync(request!.Id, error);
    }

    private async Task PublishAsync(string? requestId = null, string? error = null)
    {
        ApplicationSnapshot? snapshot = null;
        try { snapshot = await Task.Run(() => runtime.Workspace?.Snapshot(DateTimeOffset.UtcNow)); }
        catch (Exception) { error ??= "读取本地数据失败，原数据已保留。"; }
        if (requestId is null) Post(new { type = "state", snapshot, status = runtime.Status, error });
        else Post(new { id = requestId, success = error is null, error, snapshot, status = runtime.Status });
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
