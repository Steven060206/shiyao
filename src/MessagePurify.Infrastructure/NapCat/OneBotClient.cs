using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace MessagePurify.Infrastructure.NapCat;

/// <summary>本机 OneBot 正向连接。只暴露读取 API；事件保留至调用端消费。</summary>
public sealed class OneBotClient : IAsyncDisposable
{
    private sealed record Pending(string Action, TaskCompletionSource<JsonElement> Completion);
    private readonly WebSocket socket;
    private readonly TimeSpan requestTimeout;
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly ConcurrentDictionary<string, Pending> pending = new();
    private readonly Channel<JsonElement> events = Channel.CreateUnbounded<JsonElement>(new() { SingleWriter = true });
    private readonly Task receiver;
    private readonly object lifecycleGate = new();
    private readonly TaskCompletionSource<bool> callsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? terminalError;
    private Task? disposeTask;
    private int disposed;
    private int activeCalls;
    public ChannelReader<JsonElement> Events => events.Reader;

    public OneBotClient(WebSocket socket, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (socket.State != WebSocketState.Open) throw new ArgumentException("需要已经打开的 WebSocket。");
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (this.requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        this.socket = socket;
        receiver = ReceiveAsync();
    }

    public static async Task<OneBotClient> ConnectAsync(Uri uri, string? accessToken = null,
        TimeSpan? requestTimeout = null, CancellationToken cancellationToken = default)
    {
        OneBotEndpoint.Validate(uri);
        var transport = new ClientWebSocket();
        try
        {
            if (!string.IsNullOrEmpty(accessToken)) transport.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connecting.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(10));
            await transport.ConnectAsync(uri, connecting.Token);
            return new(transport, requestTimeout);
        }
        catch { transport.Dispose(); throw; }
    }

    public async Task<OneBotStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var data = await CallAsync("get_status", new { }, cancellationToken);
        return new(OneBotJson.Boolean(data, "online"), OneBotJson.Boolean(data, "good"));
    }

    public async Task<OneBotLogin> GetLoginInfoAsync(CancellationToken cancellationToken = default)
    {
        var data = await CallAsync("get_login_info", new { }, cancellationToken);
        return new(OneBotJson.Id(data, "user_id"), OneBotJson.String(data, "nickname"));
    }

    public async Task<OneBotVersion> GetVersionInfoAsync(CancellationToken cancellationToken = default)
    {
        var data = await CallAsync("get_version_info", new { }, cancellationToken);
        return new(OneBotJson.String(data, "app_name"), OneBotJson.String(data, "app_version"), OneBotJson.String(data, "protocol_version"));
    }

    public async Task<IReadOnlyList<OneBotGroup>> GetGroupListAsync(CancellationToken cancellationToken = default)
    {
        var data = await CallAsync("get_group_list", new { }, cancellationToken);
        return OneBotJson.Array(data).Select(item => new OneBotGroup(OneBotJson.Id(item, "group_id"), OneBotJson.String(item, "group_name"))).ToArray();
    }

    public async Task<IReadOnlyList<JsonElement>> GetGroupMessageHistoryAsync(string groupId, string? messageSequence = null,
        int count = 20, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        var parameters = new Dictionary<string, object>
        {
            ["group_id"] = groupId, ["count"] = count, ["reverse_order"] = false,
            ["disable_get_url"] = true, ["parse_mult_msg"] = false, ["quick_reply"] = false
        };
        if (messageSequence is not null) parameters["message_seq"] = messageSequence;
        var data = await CallAsync("get_group_msg_history", parameters, cancellationToken);
        return OneBotJson.Array(OneBotJson.Property(data, "messages"));
    }

    private async Task<JsonElement> CallAsync(string action, object parameters, CancellationToken cancellationToken)
    {
        EnterCall();
        var echo = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[echo] = new(action, completion);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        deadline.CancelAfter(requestTimeout);
        try
        {
            if (terminalError is not null) throw terminalError;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { action, @params = parameters, echo });
            await sending.WaitAsync(deadline.Token);
            try { await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, deadline.Token); }
            finally { sending.Release(); }
            return await completion.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !stopping.IsCancellationRequested)
        { throw new OneBotRequestTimeoutException(action); }
        finally
        {
            pending.TryRemove(echo, out _);
            ExitCall();
        }
    }

    private async Task ReceiveAsync()
    {
        try
        {
            var buffer = new byte[16 * 1024];
            using var payload = new MemoryStream();
            while (!stopping.IsCancellationRequested)
            {
                var frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stopping.Token);
                if (frame.MessageType == WebSocketMessageType.Close) throw new OneBotProtocolException("connection-closed");
                if (frame.MessageType != WebSocketMessageType.Text) throw new OneBotProtocolException("binary-message");
                if (payload.Length + frame.Count > 50 * 1024 * 1024) throw new OneBotProtocolException("message-too-large");
                payload.Write(buffer, 0, frame.Count);
                if (!frame.EndOfMessage) continue;
                JsonElement packet;
                try
                {
                    using var document = JsonDocument.Parse(payload.GetBuffer().AsMemory(0, (int)payload.Length));
                    packet = document.RootElement.Clone();
                }
                catch (JsonException) { throw new OneBotProtocolException("invalid-json"); }
                payload.SetLength(0);
                Dispatch(packet);
            }
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref disposed) == 0)
                terminalError = exception is OneBotApiException or OneBotProtocolException ? exception : new OneBotProtocolException("transport-failed");
        }
        finally
        {
            var error = terminalError ?? new ObjectDisposedException(nameof(OneBotClient));
            foreach (var entry in pending.Values) entry.Completion.TrySetException(error);
            events.Writer.TryComplete(terminalError);
        }
    }

    private void Dispatch(JsonElement packet)
    {
        if (packet.ValueKind != JsonValueKind.Object) throw new OneBotProtocolException("invalid-packet");
        if (packet.TryGetProperty("post_type", out var type) && type.ValueKind == JsonValueKind.String)
        {
            events.Writer.TryWrite(packet);
            return;
        }
        var code = OneBotJson.Property(packet, "retcode");
        if (!code.TryGetInt32(out var retcode)) throw new OneBotProtocolException("invalid-response");
        var echo = packet.TryGetProperty("echo", out var echoed) && echoed.ValueKind == JsonValueKind.String ? echoed.GetString() : null;
        if (echo is null && retcode == 1403) throw new OneBotApiException("authentication", retcode);
        if (echo is null) throw new OneBotProtocolException("missing-echo");
        if (!pending.TryGetValue(echo, out var request)) return; // 超时后的回复不能挪给其他请求。
        var status = OneBotJson.String(packet, "status");
        if (status != "ok" || retcode != 0) request.Completion.TrySetException(new OneBotApiException(request.Action, retcode));
        else request.Completion.TrySetResult(OneBotJson.Property(packet, "data").Clone());
    }

    private void EnterCall()
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            activeCalls++;
        }
    }

    private void ExitCall()
    {
        lock (lifecycleGate)
        {
            activeCalls--;
            if (disposed != 0 && activeCalls == 0) callsDrained.TrySetResult(true);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycleGate)
        {
            if (disposeTask is null)
            {
                disposed = 1;
                disposeTask = DisposeCoreAsync();
            }
            return new ValueTask(disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        stopping.Cancel();
        socket.Abort();
        await receiver;
        Task drain;
        lock (lifecycleGate)
        {
            drain = activeCalls == 0 ? Task.CompletedTask : callsDrained.Task;
        }
        await drain;
        socket.Dispose();
        stopping.Dispose();
        sending.Dispose();
    }
}
