using System.IO;
using System.IO.Pipes;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MessagePurify.Windows.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] arguments)
    {
        AppIdentity.Initialize();
        var check = arguments.Contains("--smoke-test") || arguments.Contains("--read-only-check");
        var background = arguments.Contains("--background");
        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MessagePurify", "App");
        var report = Argument(arguments, "--report") ?? Path.Combine(dataRoot, "validation.json");
        if (arguments.Contains("--smoke-test")) dataRoot = Path.Combine(Path.GetTempPath(), "messagepurify-app-" + Guid.NewGuid().ToString("N"));
        using var mutex = new Mutex(true, "Local\\MessagePurify.Application", out var first);
        if (!first && !check)
        {
            if (background) return 0;
            try { using var client = new NamedPipeClientStream(".", "MessagePurify.Application", PipeDirection.Out); client.Connect(1000); client.WriteByte(1); }
            catch (Exception exception) when (exception is IOException or TimeoutException) { }
            return 0;
        }
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 0;
        application.Dispatcher.InvokeAsync(async () =>
        {
            AppRuntime? runtime = null;
            AppWindow? window = null;
            DesktopCardHost? desktop = null;
            using var stopping = new CancellationTokenSource();
            using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/ui/assets/shiyao.ico")).Stream;
            using var trayIcon = new System.Drawing.Icon(iconStream);
            using var tray = new Forms.NotifyIcon { Text = "拾要", Icon = trayIcon, Visible = !check };
            var exiting = false;
            async Task Exit()
            {
                if (exiting) return;
                exiting = true;
                try
                {
                    desktop?.Dispose();
                    if (window is not null) { window.Exiting = true; window.Close(); }
                    tray.Visible = false; stopping.Cancel();
                    if (runtime is not null) await runtime.StopAsync();
                }
                finally { application.Shutdown(exitCode); }
            }
            try
            {
                var synthetic = arguments.Contains("--smoke-test") ? SmokeCheck.CreateWorkspace(dataRoot) : null;
                runtime = new(dataRoot, synthetic);
                window = new(runtime, dataRoot, check || background, registerStartup: !check);
                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("打开拾要", null, (_, _) => application.Dispatcher.Invoke(window.ShowMainWindow));
                menu.Items.Add("退出", null, async (_, _) => await application.Dispatcher.InvokeAsync(Exit).Task.Unwrap());
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += (_, _) => application.Dispatcher.Invoke(window.ShowMainWindow);
                if (!check || synthetic is not null)
                {
                    desktop = new(runtime, dataRoot, window.OpenItemAsync);
                    desktop.WatchSystemWindow(window);
                    window.AppearanceChanged += desktop.SetAppearance;
                    desktop.StatusChanged += () =>
                    {
                        window.DesktopStatus = new { state = desktop.State, error = desktop.Error };
                        runtime.Refresh();
                    };
                    await desktop.RefreshAsync();
                }
                window.Show();
                if (arguments.Contains("--read-only-check")) runtime.StartReadOnlyCheck();
                else if (!arguments.Contains("--smoke-test")) runtime.Start(syncOnStartup: !background);
                if (check)
                {
                    await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    if (synthetic is not null)
                    {
                        await SmokeCheck.RunAsync(window, synthetic, runtime, desktop!, dataRoot, report, tray);
                        if (int.TryParse(Argument(arguments, "--hold-seconds"), out var hold))
                            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(hold, 0, 10)));
                    }
                    else await SmokeCheck.RunReadOnlyAsync(runtime, report, int.TryParse(Argument(arguments, "--seconds"), out var seconds) ? Math.Clamp(seconds, 1, 120) : 20);
                    await Exit();
                }
                // Keep the tray and shutdown token alive until the application exits.
                else await ListenAsync(window, runtime, stopping.Token);
            }
            catch (Exception exception)
            {
                exitCode = 1;
                if (check)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
                    if (arguments.Contains("--smoke-test") || !File.Exists(report))
                        File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { success = false, error = "application-check-failed",
                            detail = arguments.Contains("--smoke-test") ? exception.ToString() : null }));
                }
                else System.Windows.MessageBox.Show("程序启动失败，已有数据已保留。请查看应用说明中的依赖检查。", "拾要");
                await Exit();
            }
        });
        application.Run();
        return exitCode;
    }

    private static async Task ListenAsync(AppWindow window, AppRuntime runtime, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream("MessagePurify.Application", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(cancellationToken);
                var signal = new byte[1];
                if (await server.ReadAsync(signal, cancellationToken) == 1)
                {
                    runtime.RequestSync();
                    await window.Dispatcher.InvokeAsync(window.ShowMainWindow);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) { }
        }
    }

    private static string? Argument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
}
