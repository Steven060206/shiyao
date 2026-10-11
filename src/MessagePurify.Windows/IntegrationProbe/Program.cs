using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MessagePurify.Windows.IntegrationProbe;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [STAThread]
    public static int Main(string[] arguments)
    {
        var report = new ProbeReport { Elevated = NotificationProbe.IsElevated() };
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MessagePurify", "IntegrationProbe", "results", "windows-probe.json");
        ProbeOptions options;
        try { options = ProbeOptions.Parse(arguments); }
        catch (ArgumentException)
        {
            report.Error = "invalidArguments";
            report.Save(defaultPath);
            return 2;
        }
        if (options.ColdActivation) defaultPath = Path.Combine(Path.GetDirectoryName(defaultPath)!, "cold-activation.json");
        if (options.ProtocolActivation is not null) defaultPath = Path.Combine(Path.GetDirectoryName(defaultPath)!,
            "protocol-" + Guid.NewGuid().ToString("N") + ".json");
        var reportPath = Path.GetFullPath(options.ReportPath ?? defaultPath);
        if (options.Help)
        {
            report.Error = "Usage: --bridge --report path | --interactive-desktop --wait-seconds 120 | --notification silent|normal [--exit-after-send]";
            report.Save(reportPath);
            return 0;
        }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Dispatcher.InvokeAsync(async () =>
        {
            int exitCode = 0;
            using var desktop = new DesktopHost();
            using var notifications = new NotificationProbe();
            var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Stop() => stop.TrySetResult(true);
            Window? mainWindow = null;
            try
            {
                report.Save(reportPath);
                if (options.ProtocolActivation is not null)
                {
                    NotificationProtocol.TryParse(options.ProtocolActivation, out var route);
                    report.NotificationActivationTransport = "protocol";
                    report.NotificationProbeId = route!.ProbeId;
                    report.ActivationObserved = true;
                    report.ActivationKind = "protocol";
                    report.ActivationAccountMatched = route.BelongsTo(NotificationProbe.SyntheticAccount);
                    string detail = report.ActivationAccountMatched == true && route.ItemId == "demo-item"
                        ? "已打开测试事项。\n这是合成内容，仅在本机显示。"
                        : "这条提醒属于其他账号或事项。\n未读取当前账号数据。";
                    mainWindow = CreateMainWindow(detail, Stop);
                    mainWindow.Show();
                    mainWindow.Activate();
                    report.Save(reportPath);
                    await Task.WhenAny(stop.Task, Task.Delay(TimeSpan.FromSeconds(options.WaitSeconds)));
                }
                if (options.Bridge) await RunBridgeAsync(options, report, reportPath).WaitAsync(TimeSpan.FromSeconds(options.WaitSeconds));
                if (options.InteractiveDesktop)
                {
                    mainWindow = CreateMainWindow("桌面验证即将附着。Win+D、遮挡、鼠标和 DPI 均需分别实测。", Stop);
                    report.Desktop = desktop.Attach();
                    ((TextBlock)((StackPanel)mainWindow.Content).Children[0]).Text =
                        "桌面状态：" + report.Desktop.Status + "\n窗口父子关系不代表可视验收通过。\n此窗口作为附着失败时的处理入口。";
                    if (report.Desktop.Status != "attachedStructureOnly") mainWindow.Show();
                    report.Save(reportPath);
                }
                if (options.Notification is not null || options.ColdActivation)
                {
                    report.NotificationMode = options.Notification;
                    var activation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    bool registered = notifications.Register(report, argument =>
                    {
                        application.Dispatcher.InvokeAsync(() =>
                        {
                            if (!NotificationRoute.TryParse(argument, out var route))
                            {
                                report.Error = "invalidActivationRoute";
                                report.Save(reportPath);
                                return;
                            }
                            report.ActivationObserved = true;
                            report.ActivationKind = options.ColdActivation ? "cold" : "running";
                            report.ActivationAccountMatched = route!.BelongsTo(NotificationProbe.SyntheticAccount);
                            string detail = report.ActivationAccountMatched == true
                                ? "已定位合成账号 demo-account 的合成事项 demo-item。\n这不是正式任务数据。"
                                : "通知属于其他账号。需要显式切换后查看，未读取当前账号数据。";
                            mainWindow ??= CreateMainWindow(detail, Stop);
                            ((TextBlock)((StackPanel)mainWindow.Content).Children[0]).Text = detail;
                            mainWindow.Show();
                            mainWindow.Activate();
                            report.Save(reportPath);
                            activation.TrySetResult(true);
                        });
                    }, options.ColdActivation);
                    if (!registered) exitCode = 3;
                    else if (options.Notification is not null)
                    {
                        notifications.Send(options.Notification, report);
                        if (report.NotificationStatus != "submitted") exitCode = 3;
                    }
                    report.Save(reportPath);
                    if (registered && !options.ExitAfterSend)
                    {
                        await Task.WhenAny(activation.Task, stop.Task, Task.Delay(TimeSpan.FromSeconds(options.WaitSeconds)));
                        if (!report.ActivationObserved) report.ActivationKind = "notObservedWithinWait";
                        if (report.ActivationObserved) await Task.WhenAny(stop.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                    }
                }
                else if (options.InteractiveDesktop) await Task.WhenAny(stop.Task, Task.Delay(TimeSpan.FromSeconds(options.WaitSeconds)));
            }
            catch (Exception exception)
            {
                report.Error = exception.GetType().Name + ":0x" + exception.HResult.ToString("X8");
                if (options.Bridge && !report.BridgeRoundTrip) report.BridgeStatus = "failed";
                exitCode = 1;
            }
            finally
            {
                desktop.Dispose();
                notifications.Dispose();
                report.Save(reportPath);
                mainWindow?.Close();
                application.Shutdown(exitCode);
            }
        });
        return application.Run();
    }

    private static async Task RunBridgeAsync(ProbeOptions options, ProbeReport report, string reportPath)
    {
        report.BridgeStatus = "running";
        var nonce = Guid.NewGuid().ToString("N");
        const string literal = "<script>中国😀</script>";
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var browser = new WebView2();
        var window = new Window
        {
            Width = 720, Height = 400, Content = browser, Opacity = 0,
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
            Title = "消息提纯隐藏桥验证", IsHitTestVisible = false
        };
        var hiddenHandle = new WindowInteropHelper(window).EnsureHandle();
        var hiddenStyle = (long)Native.GetWindowLongPtr(hiddenHandle, Native.ExStyleIndex);
        Native.SetWindowLongPtr(hiddenHandle, Native.ExStyleIndex, (nint)(hiddenStyle | Native.WsExNoActivate | Native.WsExToolWindow | 0x20));
        window.Show();
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MessagePurify", "IntegrationProbe", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await browser.EnsureCoreWebView2Async(environment);
            var core = browser.CoreWebView2;
            report.WebView2Version = environment.BrowserVersionString;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.SetVirtualHostNameToFolderMapping(BridgeProtocol.Host, Path.Combine(AppContext.BaseDirectory, "ui"),
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) => { if (!BridgeProtocol.IsTrustedSource(args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess) completed.TrySetException(new InvalidOperationException("Local page failed."));
            };
            core.WebMessageReceived += (_, args) =>
            {
                var reply = BridgeProtocol.Handle(args.Source, args.WebMessageAsJson, nonce);
                core.PostWebMessageAsJson(JsonSerializer.Serialize(reply, JsonOptions));
                if (reply.Success && reply.Id == "bridge-roundtrip") report.LiteralTextPreserved = reply.Value == literal;
                if (reply.Success && reply.Id == "bridge-ack" && reply.Value == "received:" + literal)
                    completed.TrySetResult(true);
            };
            core.Navigate(BridgeProtocol.PageUrl + "?nonce=" + nonce);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(options.WaitSeconds));
            report.PageSecureContext = await core.ExecuteScriptAsync("window.isSecureContext") == "true";
            report.BridgeRoundTrip = report.LiteralTextPreserved && report.PageSecureContext;
            report.BridgeStatus = report.BridgeRoundTrip ? "passed" : "failed";
            var capturePath = Path.ChangeExtension(reportPath, ".png");
            await using (var image = File.Create(capturePath)) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            report.CaptureFileName = Path.GetFileName(capturePath);
            report.Save(reportPath);
            if (!report.BridgeRoundTrip) throw new InvalidOperationException("Bridge verification failed.");
        }
        finally { window.Close(); }
    }

    private static Window CreateMainWindow(string text, Action stop)
    {
        var close = new Button { Content = "结束验证", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => stop();
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = text, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(close);
        var window = new Window { Title = "消息提纯：Windows 集成验证", Width = 600, Height = 280, Content = panel };
        window.Closing += (_, _) => stop();
        return window;
    }
}
