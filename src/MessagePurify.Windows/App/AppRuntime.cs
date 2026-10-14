using System.IO;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Backup;

namespace MessagePurify.Windows.App;

/// <summary>按次切换 QQ；本地事项在同步之外仍可用，账号不自动切换。</summary>
public sealed class AppRuntime
{
    private readonly string dataRoot;
    private readonly CancellationTokenSource stopping = new();
    private readonly InstalledGameCatalog gameCatalog;
    private readonly ResidentSyncController syncController;
    private readonly Func<IQqSyncSession> sessionFactory;
    private readonly Func<CancellationToken, Task<MessageHostStatus>> syncBatch;
    private readonly TimeSpan syncTimeout;
    private readonly bool injectedBatch;
    private Task? running;
    private int restoring;
    private int syncRequested;
    private string? accessToken;
    private QqSyncConfiguration? qqSyncConfiguration;
    public bool IsRestoring => Volatile.Read(ref restoring) != 0;
    public AccountWorkspace? Workspace { get; private set; }
    public MessageHostStatus Status { get; private set; } = new("disconnected", "unknown", "paused", "pending", null, DateTimeOffset.UtcNow, [])
    { SyncPhase = "idle" };
    public event Action? Changed;

    public AppRuntime(string dataRoot, AccountWorkspace? workspace = null)
        : this(dataRoot, workspace, null, null, null, null) { }

    internal AppRuntime(string dataRoot, AccountWorkspace? workspace, Func<IQqSyncSession>? sessionFactory,
        IGameActivityDetector? gameDetector, Func<CancellationToken, Task<MessageHostStatus>>? syncBatch,
        TimeSpan? syncTimeout)
    {
        this.dataRoot = Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(this.dataRoot);
        AccountBackupService.RecoverInterrupted(this.dataRoot);
        gameCatalog = new(Path.Combine(this.dataRoot, "game-catalog.json"));
        syncController = new(gameDetector ?? gameCatalog);
        this.sessionFactory = sessionFactory ?? (() => new QqSyncSession(
            qqSyncConfiguration ??= QqSyncConfiguration.Load(AppContext.BaseDirectory)));
        this.syncBatch = syncBatch ?? RunBatchAsync;
        this.syncTimeout = syncTimeout ?? TimeSpan.FromMinutes(5);
        injectedBatch = syncBatch is not null;
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

    public void Start(bool syncOnStartup = true)
    {
        if (running is not null) return;
        if (syncOnStartup) RequestSync();
        running = Task.Run(RunAsync);
    }

    // The diagnostic mode reads the existing interface without starting or stopping QQ.
    public void StartReadOnlyCheck()
    {
        running ??= Task.Run(async () =>
        {
            try
            {
                if (!injectedBatch)
                {
                    await gameCatalog.RefreshIfDueAsync(DateTimeOffset.UtcNow, stopping.Token);
                    ReadAccessToken();
                }
                var result = await syncBatch(stopping.Token);
                SetStatus(result with { SyncPhase = null });
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (Exception exception)
            { SetStatus(Status with { SyncPhase = null, Transport = "disconnected", Error = SafeFailure(exception) }); }
        });
    }

    /// <summary>重复点击合并到当前请求；不叠加 QQ 重启。</summary>
    public bool RequestSync()
    {
        if (stopping.IsCancellationRequested || IsRestoring || Interlocked.CompareExchange(ref syncRequested, 1, 0) != 0) return false;
        var now = DateTimeOffset.UtcNow;
        syncController.Request(new(SyncRequestKind.Startup, now, now));
        SetStatus(Status with { SyncPhase = "queued", Error = null });
        return true;
    }

    public void Refresh() => Changed?.Invoke();

    public async Task<BackupResult> RestoreBackupAsync(PreparedAccountBackup prepared,
        BackupApplicationSettings currentSettings, Action<BackupApplicationSettings>? applySettings = null)
    {
        if (Interlocked.CompareExchange(ref restoring, 1, 0) != 0)
            throw new InvalidOperationException("正在恢复备份，请等待结果。");
        Changed?.Invoke();
        try
        {
            var workspace = Workspace ?? throw new InvalidOperationException("尚未连接到 QQ 账号。");
            return await Task.Run(() => workspace.RestoreBackup(prepared, currentSettings,
                DateTimeOffset.UtcNow, applySettings));
        }
        finally
        {
            Volatile.Write(ref restoring, 0);
            Changed?.Invoke();
        }
    }

    private async Task RunAsync()
    {
        var catalogTask = Task.CompletedTask;
        try
        {
            if (!injectedBatch)
            {
                await gameCatalog.RefreshIfDueAsync(DateTimeOffset.UtcNow, stopping.Token);
                catalogTask = MaintainGameCatalogAsync();
            }
            while (!stopping.IsCancellationRequested)
            {
                var request = await syncController.WaitAsync(stopping.Token);
                Interlocked.Exchange(ref syncRequested, 1);
                var resumedAfterGame = false;
                while (!stopping.IsCancellationRequested)
                {
                    while (!syncController.CanSync || IsRestoring)
                    {
                        resumedAfterGame = true;
                        SetStatus(Status with { SyncPhase = "deferred", Error = IsRestoring ? "backup-restoring" : syncController.PauseReason });
                        await Task.Delay(200, stopping.Token);
                    }
                    request = syncController.CoalescePending(request);
                    if (!await RunSyncSessionAsync()) break;
                    resumedAfterGame = true;
                }
                syncController.Acknowledge(request, resumedAfterGame);
                Interlocked.Exchange(ref syncRequested, 0);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            stopping.Cancel();
            syncController.Complete();
            try { await catalogTask; }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        }
    }

    // Returns true only when a game interrupted the batch, keeping its single deferred request.
    private async Task<bool> RunSyncSessionAsync()
    {
        using var batch = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        batch.CancelAfter(syncTimeout);
        var monitor = MonitorGameAsync(batch);
        IQqSyncSession? session = null;
        var defer = false;
        string? failure = null;
        try
        {
            if (!injectedBatch)
                ReadAccessToken();
            SetStatus(Status with { SyncPhase = "starting", Transport = "connecting", Qq = "unknown", Receiving = "paused", Error = null, HistoryFailures = [] });
            session = sessionFactory();
            await session.StartAsync(batch.Token);
            batch.Token.ThrowIfCancellationRequested();
            SetStatus(Status with { SyncPhase = "syncing" });
            var result = await syncBatch(batch.Token);
            SetStatus(result with { SyncPhase = "syncing", LastSuccessfulSyncAt = result.LastSuccessfulSyncAt ?? Status.LastSuccessfulSyncAt });
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            var pauseReason = await monitor;
            defer = pauseReason is not null;
            failure = pauseReason ?? "sync-timeout";
        }
        catch (Exception exception)
        {
            failure = exception is IOException && Status.SyncPhase == "starting"
                ? "qq-sync-start-failed" : SafeFailure(exception);
            defer = failure is "game-active" or "game-catalog-unavailable";
        }
        finally
        {
            batch.Cancel();
            await monitor;
            if (session is not null)
            {
                SetStatus(Status with { SyncPhase = "restoring", Transport = "disconnected", Qq = "unknown", Receiving = "paused" });
                try { await session.DisposeAsync(); }
                catch { failure = "qq-restore-failed"; defer = false; }
            }
            var partial = Status.History == "reading" ? "partial" : Status.History;
            SetStatus(Status with { SyncPhase = defer ? "deferred" : failure is null ? "idle" : "failed",
                Transport = "disconnected", Qq = "unknown", Receiving = "paused", History = partial, Error = failure });
        }
        return defer;
    }

    private async Task<string?> MonitorGameAsync(CancellationTokenSource batch)
    {
        try
        {
            while (!batch.IsCancellationRequested)
            {
                if (!syncController.CanSync)
                {
                    var reason = syncController.PauseReason;
                    batch.Cancel();
                    return reason;
                }
                await Task.Delay(200, batch.Token);
            }
        }
        catch (OperationCanceledException) when (batch.IsCancellationRequested) { }
        return null;
    }

    private async Task<MessageHostStatus> RunBatchAsync(CancellationToken cancellationToken)
    {
        var endpoint = new Uri("ws://127.0.0.1:3001/");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Workspace is null)
                {
                    await using var discovery = await OneBotClient.ConnectAsync(endpoint, accessToken, cancellationToken: cancellationToken);
                    var state = await discovery.GetStatusAsync(cancellationToken);
                    if (!state.Online || !state.Good) throw new OneBotProtocolException("qq-offline");
                    var login = await discovery.GetLoginInfoAsync(cancellationToken);
                    var workspace = new AccountWorkspace(dataRoot, login.AccountId);
                    var temporary = Path.Combine(dataRoot, "active-account.txt.tmp");
                    File.WriteAllText(temporary, login.AccountId);
                    File.Move(temporary, Path.Combine(dataRoot, "active-account.txt"), true);
                    Workspace = workspace;
                }
                var host = new NapCatMessageHost(Workspace, endpoint, accessToken, syncController);
                host.StatusChanged += state => SetStatus(state with { SyncPhase = "syncing", LastSuccessfulSyncAt = state.LastSuccessfulSyncAt ?? Status.LastSuccessfulSyncAt });
                host.DataChanged += () => Changed?.Invoke();
                await host.RunOnceAsync(cancellationToken);
                return host.Status;
            }
            catch (Exception exception) when (exception is System.Net.WebSockets.WebSocketException or OneBotRequestTimeoutException
                or OneBotProtocolException { Code: "qq-offline" or "qq-unhealthy" or "connection-closed" })
            {
                SetStatus(Status with { SyncPhase = "syncing", Transport = "connecting", Error = null });
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private void SetStatus(MessageHostStatus state)
    {
        Status = state with { Scheduled = false, NextScheduledSyncAt = null,
            UpdatedAt = DateTimeOffset.UtcNow };
        Changed?.Invoke();
    }

    private void ReadAccessToken()
    {
        qqSyncConfiguration = QqSyncConfiguration.Load(AppContext.BaseDirectory);
        accessToken = ReadAccessToken(qqSyncConfiguration, Environment.GetEnvironmentVariable("MESSAGEPURIFY_NAPCAT_TOKEN"));
    }

    internal static string? ReadAccessToken(QqSyncConfiguration configuration, string? environmentToken) =>
        NapCatConnectionConfiguration.ReadAccessToken(environmentToken, configuration.TokenConfigurationDirectory);

    private static string SafeFailure(Exception exception) => exception switch
    {
        OneBotProtocolException protocol => protocol.Code,
        OneBotApiException api => "api-" + api.Retcode,
        OneBotRequestTimeoutException => "request-timeout",
        QqSyncConfigurationException configuration => configuration.Message,
        InvalidOperationException { Message: "connection-configuration-unavailable" } => "connection-configuration-unavailable",
        InvalidOperationException invalid when invalid.Message.StartsWith("qq-", StringComparison.Ordinal) => invalid.Message,
        IOException or InvalidDataException => "local-data-unavailable",
        _ => "connection-unavailable"
    };

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
