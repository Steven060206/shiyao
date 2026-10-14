using System.Globalization;
using System.Net.WebSockets;
using System.Numerics;
using System.Text.Json;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;

namespace MessagePurify.Infrastructure.Application;

public sealed record HistoryFailure(string GroupId, string Reason);
public sealed record MessageHostStatus(string Transport, string Qq, string Receiving,
    string History, string? Error, DateTimeOffset UpdatedAt, IReadOnlyList<HistoryFailure> HistoryFailures)
{
    public bool Scheduled { get; init; }
    public string? SyncPhase { get; init; }
    public DateTimeOffset? LastSuccessfulSyncAt { get; init; }
    public DateTimeOffset? NextScheduledSyncAt { get; init; }
}

/// <summary>只读本机 QQ 宿主；历史和实时先保存原消息，补漏结束后按原发送时间处理。</summary>
public sealed class NapCatMessageHost
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleRefreshInterval = TimeSpan.FromSeconds(30);
    private readonly AccountWorkspace workspace;
    private readonly Uri endpoint;
    private readonly string? accessToken;
    private readonly ResidentSyncController? syncController;
    private readonly ScheduledSyncPlanner? schedule;
    private MessageHostStatus status = new("disconnected", "unknown", "paused", "pending", null, DateTimeOffset.UtcNow, []);
    private int running;
    private sealed class SyncPausedException : Exception;

    private sealed class ConnectionState : IDisposable
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public IReadOnlyList<OneBotGroup> Groups = [];
        public IReadOnlyList<OneBotGroup> SyncedGroups = [];
        public IReadOnlySet<string> Pins = new HashSet<string>(StringComparer.Ordinal);
        public readonly Queue<string> HistoryQueue = new();
        public readonly HashSet<string> Queued = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Failures = new(StringComparer.Ordinal);
        public bool HistoryRunning;
        public bool SingleBatch;
        public string CompletedHistory = "pending";
        public DateTimeOffset RefreshedAt;
        public string BatchId = "history-" + Guid.NewGuid().ToString("N");
        public DateTimeOffset Cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        public void Dispose() => Gate.Dispose();
    }

    public NapCatMessageHost(AccountWorkspace workspace, Uri endpoint, string? accessToken = null,
        ResidentSyncController? syncController = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(endpoint);
        OneBotEndpoint.Validate(endpoint);
        this.workspace = workspace;
        this.endpoint = endpoint;
        this.accessToken = accessToken;
        this.syncController = syncController;
        if (syncController is not null)
        {
            schedule = new(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
            status = status with { Scheduled = true, NextScheduledSyncAt = schedule.NextSlotAfter(status.UpdatedAt) };
        }
    }

    public event Action<MessageHostStatus>? StatusChanged;
    public event Action? DataChanged;
    public MessageHostStatus Status => Volatile.Read(ref status);

    /// <summary>只执行一轮七天历史同步；请求由调用端调度，完成后释放接口连接。</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("消息宿主已运行。");
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string? pauseReason = null;
        async Task WatchPermissionAsync()
        {
            if (syncController is null) return;
            while (!session.IsCancellationRequested)
            {
                if (!syncController.CanSync)
                {
                    pauseReason = syncController.PauseReason;
                    session.Cancel();
                    return;
                }
                try { await Task.Delay(TimeSpan.FromMilliseconds(100), session.Token); }
                catch (OperationCanceledException) when (session.IsCancellationRequested) { return; }
            }
        }
        var permissionWatch = WatchPermissionAsync();
        Publish("connecting", "unknown", "paused", "pending", null, []);
        try
        {
            EnsureSyncAllowed();
            await using var client = await OneBotClient.ConnectAsync(endpoint, accessToken, cancellationToken: session.Token);
            await RunConnectionAsync(client, session.Token, singleBatch: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            var reason = pauseReason ?? (exception is SyncPausedException ? syncController!.PauseReason : SafeFailure(exception));
            var previous = Status;
            Publish("disconnected", reason == "qq-offline" ? "offline" : "unknown", "paused",
                previous.History == "reading" ? "partial" : previous.History, reason, previous.HistoryFailures);
            if (pauseReason is not null || exception is SyncPausedException)
                throw new OneBotProtocolException(reason);
            throw;
        }
        finally
        {
            session.Cancel();
            await permissionWatch;
            var previous = Status;
            Publish("disconnected", previous.Qq, "paused", previous.History == "reading" ? "partial" : previous.History,
                previous.Error, previous.HistoryFailures);
            Volatile.Write(ref running, 0);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("消息宿主已运行。");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var prior = Status;
                Publish("connecting", "unknown", "paused", prior.History == "reading" ? "partial" : prior.History,
                    null, prior.HistoryFailures);
                try
                {
                    await using var client = await OneBotClient.ConnectAsync(endpoint, accessToken, cancellationToken: cancellationToken);
                    await RunConnectionAsync(client, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    var previous = Status;
                    var reason = SafeFailure(exception);
                    Publish("disconnected", reason == "qq-offline" ? "offline" : "unknown", "paused",
                        previous.History == "reading" ? "partial" : previous.History, reason, previous.HistoryFailures);
                }
                try { await Task.Delay(ReconnectDelay, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            }
        }
        finally
        {
            var previous = Status;
            Publish("disconnected", "unknown", "paused", previous.History == "reading" ? "partial" : previous.History,
                previous.Error, previous.HistoryFailures);
            Volatile.Write(ref running, 0);
        }
    }

    private async Task RunConnectionAsync(OneBotClient client, CancellationToken cancellationToken, bool singleBatch = false)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var state = new ConnectionState();
        state.SingleBatch = singleBatch;
        state.CompletedHistory = Status.History;
        state.HistoryRunning = singleBatch || syncController is null;
        foreach (var failure in Status.HistoryFailures) state.Failures[failure.GroupId] = failure.Reason;
        Task? consuming = null;
        try
        {
            await RefreshAsync(client, state, session.Token, queueHistory: singleBatch || syncController is null);
            if (singleBatch) EnsureSyncAllowed();
            consuming = ConsumeAsync(client, state, session);
            if (syncController is not null && !singleBatch)
            {
                await RunResidentBatchesAsync(client, state, session.Token);
                return;
            }
            while (true)
            {
                session.Token.ThrowIfCancellationRequested();
                if (!await AdvanceHistoryAsync(client, state, session.Token))
                {
                    if (singleBatch) return;
                    await Task.Delay(RefreshInterval, session.Token);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && consuming is { IsFaulted: true })
        { await consuming; throw; }
        finally
        {
            session.Cancel();
            if (consuming is not null)
            {
                try { await consuming; }
                catch (OperationCanceledException) when (session.IsCancellationRequested) { }
                catch { /* The coordinator or reader failure is reported by RunAsync. */ }
            }
        }
    }

    private async Task RunResidentBatchesAsync(OneBotClient client, ConnectionState state, CancellationToken cancellationToken)
    {
        while (true)
        {
            var request = await WaitForSyncRequestAsync(client, state, cancellationToken);
            var resumedAfterGame = false;
            while (true)
            {
                resumedAfterGame |= syncController!.GameActive;
                await WaitUntilSyncAllowedAsync(state, cancellationToken);
                request = syncController!.CoalescePending(request);
                try
                {
                    await BeginHistoryAsync(client, state, request, cancellationToken);
                    while (await AdvanceHistoryAsync(client, state, cancellationToken)) { }
                    syncController.Acknowledge(request, resumedAfterGame);
                    break;
                }
                catch (SyncPausedException)
                {
                    resumedAfterGame = true;
                    // Retain the request and raw messages. After the game, start at the newest
                    // page again so events discarded during the pause are recovered once.
                }
            }
        }
    }

    private async Task<bool> AdvanceHistoryAsync(OneBotClient client, ConnectionState state, CancellationToken cancellationToken)
    {
        EnsureSyncAllowed();
        string? nextGroup;
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            EnsureSyncAllowed();
            if (syncController is null && DateTimeOffset.UtcNow - state.RefreshedAt >= RefreshInterval)
                await RefreshAsync(client, state, cancellationToken);
            nextGroup = state.HistoryQueue.Count == 0 ? null : state.HistoryQueue.Dequeue();
            if (nextGroup is null && state.HistoryRunning)
            {
                await RefreshPinsAsync(client, state, cancellationToken, queueHistory: true);
                EnsureSyncAllowed();
                if (state.HistoryQueue.Count > 0) nextGroup = state.HistoryQueue.Dequeue();
                else
                {
                    if (workspace.ProcessPending(state.Pins, DateTimeOffset.UtcNow,
                            syncController is null ? null : () => syncController.CanSync) > 0) DataChanged?.Invoke();
                    EnsureSyncAllowed();
                    try
                    {
                        workspace.CleanMessageCache(DateTimeOffset.UtcNow,
                            () => !cancellationToken.IsCancellationRequested && syncController?.CanSync != false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && syncController?.CanSync == false)
                    { throw new SyncPausedException(); }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        // 清理错误由独立工作区状态呈现；已完成的消息同步仍按实际结果记录。
                    }
                    DataChanged?.Invoke();
                    EnsureSyncAllowed();
                    state.HistoryRunning = false;
                    state.CompletedHistory = state.Failures.Count == 0 ? "complete" : "partial";
                    PublishConnected(state, completedBatch: true);
                }
            }
        }
        finally { state.Gate.Release(); }
        if (nextGroup is null) return false;
        await ReadHistoryAsync(client, state, nextGroup, cancellationToken);
        return true;
    }

    private void EnsureSyncAllowed()
    {
        if (syncController is not null && !syncController.CanSync) throw new SyncPausedException();
    }

    private async Task WaitUntilSyncAllowedAsync(ConnectionState state, CancellationToken cancellationToken)
    {
        while (syncController is not null && !syncController.CanSync)
        {
            Publish("connected", "online", "paused", state.HistoryRunning ? "partial" : Status.History,
                syncController.PauseReason, Status.HistoryFailures);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    // Called under the connection gate (initial verification happens before the reader starts).
    private async Task RefreshAsync(OneBotClient client, ConnectionState state, CancellationToken cancellationToken,
        bool queueHistory = true)
    {
        var qq = await client.GetStatusAsync(cancellationToken);
        if (!qq.Online) throw new OneBotProtocolException("qq-offline");
        if (!qq.Good) throw new OneBotProtocolException("qq-unhealthy");
        var login = await client.GetLoginInfoAsync(cancellationToken);
        if (login.AccountId != workspace.AccountId) throw new OneBotProtocolException("account-mismatch");
        state.Groups = await client.GetGroupListAsync(cancellationToken);
        await RefreshPinsAsync(client, state, cancellationToken, queueHistory);
        state.RefreshedAt = DateTimeOffset.UtcNow;
        PublishConnected(state);
    }

    private async Task RefreshPinsAsync(OneBotClient client, ConnectionState state, CancellationToken cancellationToken,
        bool queueHistory = true)
    {
        IReadOnlySet<string> pins;
        try { pins = await client.GetPinnedGroupIdsAsync(workspace.AccountId, cancellationToken); }
        catch (Exception exception) when (exception is OneBotApiException or OneBotRequestTimeoutException)
        { throw new OneBotProtocolException("pinned-state-unavailable"); }
        var groupsChanged = queueHistory
            && state.Groups.Any(group => pins.Contains(group.GroupId) && !state.Queued.Contains(group.GroupId));
        foreach (var group in state.Groups.Where(group => queueHistory && pins.Contains(group.GroupId) && !state.Queued.Contains(group.GroupId)))
        {
            if (state.Queued.Add(group.GroupId)) state.HistoryQueue.Enqueue(group.GroupId);
            state.Failures.Remove(group.GroupId);
            state.HistoryRunning = true;
        }
        foreach (var unpinned in state.Pins.Where(id => !pins.Contains(id))) state.Queued.Remove(unpinned);
        var changed = !state.Pins.SetEquals(pins) || !state.SyncedGroups.SequenceEqual(state.Groups);
        state.Pins = pins;
        if (changed)
        {
            workspace.SetAvailableGroups(state.Groups, pins);
            state.SyncedGroups = state.Groups;
            // Names and pin changes are reflected through the same snapshot even without new messages.
            DataChanged?.Invoke();
        }
        if (groupsChanged) PublishConnected(state);
    }

    private async Task<SyncRequest> WaitForSyncRequestAsync(OneBotClient client, ConnectionState state,
        CancellationToken cancellationToken)
    {
        if (syncController is null) throw new InvalidOperationException("未配置常驻同步控制器。");
        var request = syncController.WaitAsync(cancellationToken).AsTask();
        while (true)
        {
            var refresh = Task.Delay(RefreshInterval, cancellationToken);
            var completed = await Task.WhenAny(request, refresh);
            if (completed == request) return await request;
            if (!syncController.CanSync)
            {
                Publish("connected", "online", "paused", Status.History, syncController.PauseReason, Status.HistoryFailures);
                continue;
            }
            if (Status.Error is "game-active" or "game-catalog-unavailable") PublishConnected(state);
            if (DateTimeOffset.UtcNow - state.RefreshedAt < IdleRefreshInterval) continue;
            await state.Gate.WaitAsync(cancellationToken);
            try { await RefreshAsync(client, state, cancellationToken, queueHistory: false); }
            finally { state.Gate.Release(); }
        }
    }

    private async Task BeginHistoryAsync(OneBotClient client, ConnectionState state,
        SyncRequest request, CancellationToken cancellationToken)
    {
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            EnsureSyncAllowed();
            state.HistoryQueue.Clear();
            state.Queued.Clear();
            state.Failures.Clear();
            state.HistoryRunning = true;
            state.BatchId = $"history-{request.SlotAt:yyyyMMdd-HHmm}-{Guid.NewGuid():N}";
            state.Cutoff = DateTimeOffset.UtcNow.AddDays(-7);
            await RefreshAsync(client, state, cancellationToken, queueHistory: true);
            EnsureSyncAllowed();
        }
        finally { state.Gate.Release(); }
    }

    private async Task ConsumeAsync(OneBotClient client, ConnectionState state, CancellationTokenSource session)
    {
        try
        {
            await foreach (var packet in client.Events.ReadAllAsync(session.Token))
            {
                if (packet.TryGetProperty("self_id", out _)
                    && OneBotJson.Id(packet, "self_id") != workspace.AccountId)
                    throw new OneBotProtocolException("account-mismatch");
                if (!packet.TryGetProperty("message_type", out var type)
                    || type.ValueKind != JsonValueKind.String || type.GetString() != "group") continue;
                if (!packet.TryGetProperty("self_id", out _)) throw new OneBotProtocolException("account-unknown");
                var groupId = OneBotJson.Id(packet, "group_id");
                await state.Gate.WaitAsync(session.Token);
                try
                {
                    if (syncController is not null && (!state.HistoryRunning || !syncController.CanSync)) continue;
                    await RefreshPinsAsync(client, state, session.Token);
                    if (syncController is not null && !syncController.CanSync) continue;
                    if (!state.Pins.Contains(groupId) || !state.Groups.Any(group => group.GroupId == groupId)) continue;
                    var message = OneBotMessageParser.ParseGroupMessage(packet, workspace.AccountId, groupId).Message;
                    var changed = state.HistoryRunning
                        ? workspace.Enqueue(message, state.Pins, MessageOrigin.Realtime, state.BatchId, DateTimeOffset.UtcNow)
                        : workspace.Ingest(message, state.Pins, MessageOrigin.Realtime, "live-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
                    if (changed) DataChanged?.Invoke();
                }
                finally { state.Gate.Release(); }
            }
            throw new OneBotProtocolException("connection-closed");
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch { session.Cancel(); throw; }
    }

    private async Task ReadHistoryAsync(OneBotClient client, ConnectionState state, string groupId, CancellationToken cancellationToken)
    {
        var seen = new HashSet<MessagePurify.Core.MessageKey>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        try
        {
            for (var pageIndex = 0; pageIndex < 100; pageIndex++)
            {
                EnsureSyncAllowed();
                await state.Gate.WaitAsync(cancellationToken);
                try
                {
                    if (DateTimeOffset.UtcNow - state.RefreshedAt >= RefreshInterval)
                        await RefreshAsync(client, state, cancellationToken);
                    else await RefreshPinsAsync(client, state, cancellationToken);
                    if (!state.Pins.Contains(groupId)) throw new OneBotProtocolException("unpinned");
                }
                finally { state.Gate.Release(); }
                EnsureSyncAllowed();
                var page = await client.GetGroupMessageHistoryAsync(groupId, cursor, 100, cancellationToken);
                EnsureSyncAllowed();
                await state.Gate.WaitAsync(cancellationToken);
                try
                {
                    // Recheck after the request too: in-flight unpinning must exclude the page before body parsing.
                    await RefreshPinsAsync(client, state, cancellationToken);
                    EnsureSyncAllowed();
                    if (!state.Pins.Contains(groupId)) throw new OneBotProtocolException("unpinned");
                    var messages = page.Where(item => item.ValueKind == JsonValueKind.Object
                            && item.TryGetProperty("message_type", out var type)
                            && type.ValueKind == JsonValueKind.String && type.GetString() == "group")
                        .Select(item => OneBotMessageParser.ParseGroupMessage(item, workspace.AccountId, groupId)).ToArray();
                    if (messages.Length == 0) throw new OneBotProtocolException("history-empty-page");
                    var previousCount = seen.Count;
                    var changed = false;
                    foreach (var message in messages)
                    {
                        EnsureSyncAllowed();
                        seen.Add(message.Message.Key);
                        if (message.Message.SentAt >= state.Cutoff)
                            changed |= workspace.Enqueue(message.Message, state.Pins, MessageOrigin.History, state.BatchId, DateTimeOffset.UtcNow);
                    }
                    if (changed) DataChanged?.Invoke();
                    if (messages.Any(message => message.Message.SentAt < state.Cutoff)) return;
                    if (seen.Count == previousCount) throw new OneBotProtocolException("history-no-progress");
                    var next = OldestCursor(messages);
                    if (!cursors.Add(next)) throw new OneBotProtocolException("history-no-progress");
                    cursor = next;
                }
                finally { state.Gate.Release(); }
            }
            throw new OneBotProtocolException("history-page-limit");
        }
        catch (OneBotProtocolException exception) when (exception.Code is "account-mismatch" or "pinned-state-invalid"
            or "pinned-state-unavailable" or "qq-offline" or "qq-unhealthy"
            || state.SingleBatch && !exception.Code.StartsWith("history-", StringComparison.Ordinal) && exception.Code != "unpinned")
        {
            await state.Gate.WaitAsync(cancellationToken);
            try
            {
                state.Failures[groupId] = exception.Code;
                PublishConnected(state);
            }
            finally { state.Gate.Release(); }
            throw;
        }
        catch (Exception exception) when (exception is OneBotApiException or OneBotProtocolException or OneBotRequestTimeoutException)
        {
            await state.Gate.WaitAsync(cancellationToken);
            try { state.Failures[groupId] = SafeFailure(exception); }
            finally { state.Gate.Release(); }
        }
    }

    private static string OldestCursor(IReadOnlyList<ParsedOneBotMessage> messages)
    {
        var oldestTime = messages.Min(message => message.Message.SentAt);
        var oldest = messages.Where(message => message.Message.SentAt == oldestTime).DistinctBy(message => message.Message.Key).ToArray();
        var selected = oldest[0];
        if (oldest.Length > 1)
        {
            var ordered = new List<(ParsedOneBotMessage Message, BigInteger Sequence)>();
            foreach (var message in oldest)
            {
                if (!BigInteger.TryParse(message.RealSequence, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
                    throw new OneBotProtocolException("history-cursor-ambiguous");
                ordered.Add((message, sequence));
            }
            var minimum = ordered.Min(item => item.Sequence);
            var first = ordered.Where(item => item.Sequence == minimum).ToArray();
            if (first.Length != 1) throw new OneBotProtocolException("history-cursor-ambiguous");
            selected = first[0].Message;
        }
        return selected.MessageSequence ?? throw new OneBotProtocolException("history-cursor-missing");
    }

    private void PublishConnected(ConnectionState state, bool completedBatch = false)
    {
        var blocked = syncController is not null && !syncController.CanSync;
        Publish("connected", "online",
            blocked || syncController is not null && !state.HistoryRunning ? "paused" : "active",
            state.HistoryRunning ? blocked ? "partial" : "reading" : state.CompletedHistory,
            blocked ? syncController!.PauseReason : null,
            state.Failures.Select(entry => new HistoryFailure(entry.Key, entry.Value))
                .OrderBy(item => item.GroupId, StringComparer.Ordinal).ToArray(), completedBatch);
    }

    private void Publish(string transport, string qq, string receiving, string history, string? error,
        IReadOnlyList<HistoryFailure> failures, bool completedBatch = false)
    {
        var now = DateTimeOffset.UtcNow;
        var current = new MessageHostStatus(transport, qq, receiving, history, error, now, failures)
        {
            Scheduled = syncController is not null,
            LastSuccessfulSyncAt = completedBatch && failures.Count == 0 ? now : Status.LastSuccessfulSyncAt,
            NextScheduledSyncAt = schedule?.NextSlotAfter(now)
        };
        Volatile.Write(ref status, current);
        StatusChanged?.Invoke(current);
    }

    private static string SafeFailure(Exception exception) => exception switch
    {
        OneBotApiException api => "api-failed-" + api.Retcode.ToString(CultureInfo.InvariantCulture),
        OneBotRequestTimeoutException => "request-timeout",
        OneBotProtocolException protocol => protocol.Code,
        WebSocketException => "connection-failed",
        ArgumentException => "invalid-configuration",
        _ => "host-failed"
    };
}
