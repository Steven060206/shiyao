using System.IO;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.Windows.App;

/// <summary>初次从只读登录接口确定账号；以后始终使用保留的账号，不自动切换。</summary>
public sealed class AppRuntime
{
    private readonly string dataRoot;
    private readonly CancellationTokenSource stopping = new();
    private readonly InstalledGameCatalog gameCatalog;
    private readonly ResidentSyncController syncController;
    private Task? running;
    public AccountWorkspace? Workspace { get; private set; }
    public MessageHostStatus Status { get; private set; } = new("disconnected", "unknown", "paused", "pending", null, DateTimeOffset.UtcNow, [])
    { Scheduled = true };
    public event Action? Changed;

    public AppRuntime(string dataRoot, AccountWorkspace? workspace = null)
    {
        this.dataRoot = Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(this.dataRoot);
        gameCatalog = new(Path.Combine(this.dataRoot, "game-catalog.json"));
        syncController = new(gameCatalog);
        Workspace = workspace;
        var accountPath = Path.Combine(this.dataRoot, "active-account.txt");
        if (Workspace is null && File.Exists(accountPath))
        {
            var account = File.ReadAllText(accountPath).Trim();
            if (account.Length == 0 || account.Any(c => c is < '0' or > '9'))
                throw new InvalidDataException("账号记录无法识别，已保留原数据。");
            Workspace = new(this.dataRoot, account);
        }
    }

    public void Start() => running ??= Task.Run(RunAsync);

    public void Refresh() => Changed?.Invoke();

    private async Task RunAsync()
    {
        var endpoint = new Uri("ws://127.0.0.1:3001/");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var scheduler = new ScheduledSyncRunner(syncController,
            new ScheduledSyncPlanner(timeZone), gameCatalog);
        var schedulerTask = Task.CompletedTask;
        var catalogTask = Task.CompletedTask;
        try
        {
            await gameCatalog.RefreshIfDueAsync(DateTimeOffset.UtcNow, stopping.Token);
            catalogTask = MaintainGameCatalogAsync();
            schedulerTask = scheduler.RunAsync(stopping.Token);
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    var accessToken = NapCatConnectionConfiguration.ReadAccessToken(
                        Environment.GetEnvironmentVariable("MESSAGEPURIFY_NAPCAT_TOKEN"),
                        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../napcat/framework/v4.18.37/package/config")));
                    if (Workspace is null)
                    {
                        Status = Status with { Transport = "connecting", Error = null, UpdatedAt = DateTimeOffset.UtcNow };
                        Changed?.Invoke();
                        await using var discovery = await OneBotClient.ConnectAsync(endpoint, accessToken, cancellationToken: stopping.Token);
                        var state = await discovery.GetStatusAsync(stopping.Token);
                        if (!state.Online || !state.Good) throw new OneBotProtocolException("qq-offline");
                        var login = await discovery.GetLoginInfoAsync(stopping.Token);
                        var workspace = new AccountWorkspace(dataRoot, login.AccountId);
                        var temporary = Path.Combine(dataRoot, "active-account.txt.tmp");
                        File.WriteAllText(temporary, login.AccountId);
                        File.Move(temporary, Path.Combine(dataRoot, "active-account.txt"), true);
                        Workspace = workspace;
                    }
                    var host = new NapCatMessageHost(Workspace, endpoint, accessToken, syncController);
                    host.StatusChanged += state =>
                    {
                        Status = !gameCatalog.IsReady && state.Error is null
                            ? state with { Receiving = "paused", Error = "game-catalog-unavailable" } : state;
                        Changed?.Invoke();
                    };
                    host.DataChanged += () => Changed?.Invoke();
                    await host.RunAsync(stopping.Token);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    var error = exception switch
                    {
                        OneBotProtocolException protocol => protocol.Code,
                        OneBotApiException api => "api-" + api.Retcode,
                        OneBotRequestTimeoutException => "request-timeout",
                        IOException or InvalidDataException => "local-data-unavailable",
                        _ => "connection-unavailable"
                    };
                    Status = Status with { Transport = "disconnected", Qq = "unknown", Receiving = "paused",
                        History = Status.History == "reading" ? "partial" : Status.History,
                        Error = error, UpdatedAt = DateTimeOffset.UtcNow };
                    Changed?.Invoke();
                }
                try { await Task.Delay(TimeSpan.FromSeconds(5), stopping.Token); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            stopping.Cancel();
            syncController.Complete();
            try { await Task.WhenAll(schedulerTask, catalogTask); }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        }
    }

    private async Task MaintainGameCatalogAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stopping.Token);
            if (await gameCatalog.RefreshIfDueAsync(DateTimeOffset.UtcNow, stopping.Token)) Changed?.Invoke();
        }
    }

    public async Task StopAsync()
    {
        stopping.Cancel();
        if (running is not null) await running;
        stopping.Dispose();
    }
}
