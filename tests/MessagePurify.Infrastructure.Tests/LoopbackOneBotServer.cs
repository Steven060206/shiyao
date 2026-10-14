using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MessagePurify.Infrastructure.Tests;

internal sealed class LoopbackOneBotServer : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Task serving;
    private WebSocket? socket;
    public Uri Url { get; }
    public string? ObservedAuthorization { get; private set; }
    public List<string> Actions { get; } = [];
    public bool Online { get; init; } = true;
    public string? EventAccount { get; init; } = "9007199254740993";
    public int GroupCount { get; init; } = 1;
    public bool EmitHistoryEvents { get; init; } = true;
    public int ConnectionCount { get; private set; }
    public bool HasActiveConnection => socket?.State == WebSocketState.Open;
    public Func<int, string>? AccountForConnection { get; init; }
    public Func<int, int, (int Retcode, object? Data)>? PinnedResponder { get; set; }
    public int PinnedReads { get; private set; }
    public IReadOnlyList<object> EventsOnFirstPinnedRead { get; init; } = [];
    public Func<JsonElement, int, (int Retcode, object? Data)>? HistoryResponder { get; set; }
    public Task? HistoryResponseGate { get; init; }
    public List<JsonElement> HistoryParameters { get; } = [];
    public void Disconnect() => socket?.Abort();

    public LoopbackOneBotServer()
    {
        var reserved = new TcpListener(IPAddress.Loopback, 0);
        reserved.Start();
        var port = ((IPEndPoint)reserved.LocalEndpoint).Port;
        reserved.Stop();
        Url = new($"ws://127.0.0.1:{port}/");
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        serving = ServeAsync();
    }

    private async Task ServeAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            try { await ServeConnectionAsync(); }
            catch (WebSocketException) { } // 客户端正常 Dispose 后，继续接受下一连接。
            catch (Exception exception) when (stopping.IsCancellationRequested && exception is OperationCanceledException or HttpListenerException or ObjectDisposedException) { break; }
            finally { socket?.Dispose(); socket = null; }
        }
    }

    private async Task ServeConnectionAsync()
    {
        var context = await listener.GetContextAsync().WaitAsync(stopping.Token);
        ObservedAuthorization = context.Request.Headers["Authorization"];
        socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
        ConnectionCount++;
        var buffer = new byte[8192];
        while (!stopping.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stopping.Token);
            if (frame.MessageType == WebSocketMessageType.Close) break;
            using var request = JsonDocument.Parse(buffer.AsMemory(0, frame.Count));
            var action = request.RootElement.GetProperty("action").GetString()!;
            Actions.Add(action);
            var historyRetcode = 0;
            object data = action switch
            {
                "get_status" => new { online = Online, good = true },
                "get_login_info" => new { user_id = AccountForConnection?.Invoke(ConnectionCount) ?? "9007199254740993", nickname = "private-nickname-sentinel" },
                "get_version_info" => new { app_name = "NapCat.Onebot", app_version = "4.0.0", protocol_version = "v11" },
                "get_group_list" => Groups(),
                "messagepurify_get_pinned_groups" => PinnedData(out historyRetcode),
                "get_group_msg_history" => HistoryData(request.RootElement.GetProperty("params"), out historyRetcode),
                _ => throw new InvalidOperationException("Unexpected write operation")
            };
            if (action == "messagepurify_get_pinned_groups" && PinnedReads == 1)
                foreach (var groupEvent in EventsOnFirstPinnedRead) await SendAsync(groupEvent);
            if (action == "get_group_msg_history")
            {
                if (HistoryResponseGate is not null) await HistoryResponseGate.WaitAsync(stopping.Token);
                if (EmitHistoryEvents && historyRetcode == 0)
                {
                    var message = FirstHistoryMessage(data, request.RootElement.GetProperty("params"));
                    var groupEvent = message.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone());
                    if (EventAccount is null) groupEvent.Remove("self_id");
                    else groupEvent["self_id"] = JsonSerializer.SerializeToElement(EventAccount);
                    await SendAsync(groupEvent);
                    await SendAsync(new { post_type = "message", message_type = "private", self_id = "9007199254740993" });
                    await SendAsync(new { post_type = "meta_event", meta_event_type = "heartbeat", self_id = "9007199254740993" });
                }
            }
            await SendAsync(new { status = historyRetcode == 0 ? "ok" : "failed", retcode = historyRetcode, echo = request.RootElement.GetProperty("echo").GetString(), data });
        }
    }

    private object Groups() => Enumerable.Range(0, Math.Max(0, GroupCount)).Select(index => new
    {
        group_id = GroupId(index), group_name = $"private-group-sentinel-{index + 1}"
    }).ToArray();

    private object PinnedData(out int retcode)
    {
        PinnedReads++;
        if (PinnedResponder is not null)
        {
            var response = PinnedResponder(ConnectionCount, PinnedReads);
            retcode = response.Retcode;
            return response.Data ?? JsonSerializer.SerializeToElement<object?>(null);
        }
        retcode = 0;
        return new { self_id = AccountForConnection?.Invoke(ConnectionCount) ?? "9007199254740993",
            group_ids = Enumerable.Range(0, Math.Max(0, GroupCount)).Select(index => new { group_id = GroupId(index) }).ToArray() };
    }

    private object HistoryData(JsonElement parameters, out int retcode)
    {
        var clone = parameters.Clone();
        HistoryParameters.Add(clone);
        var requestNumber = HistoryParameters.Count;
        if (HistoryResponder is not null)
        {
            var response = HistoryResponder(clone, requestNumber);
            retcode = response.Retcode;
            return response.Data ?? JsonDocument.Parse("null").RootElement.Clone();
        }
        retcode = 0;
        return new { messages = new[] { GroupMessage(parameters.TryGetProperty("group_id", out var id) ? id.GetString()! : GroupId(0)) } };
    }

    private static JsonElement FirstHistoryMessage(object data, JsonElement parameters)
    {
        var element = JsonSerializer.SerializeToElement(data);
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("messages", out var messages)
            && messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0)
            return messages[0].Clone();
        return JsonSerializer.SerializeToElement(new
        {
            post_type = "message", message_type = "group",
            self_id = "9007199254740993",
            group_id = parameters.TryGetProperty("group_id", out var id) ? id.GetString() : "9007199254740995",
            message_id = "opaque-message-id", message_seq = "opaque-message-id",
            user_id = "9007199254740997", time = 1791597600,
            message = new[] { new { type = "text", data = new { text = "private-body-sentinel" } } }
        });
    }

    private static string GroupId(int index) => (9007199254740995UL + (ulong)index).ToString();

    private static object GroupMessage(string groupId) => new
    {
        post_type = "message", message_type = "group", self_id = "9007199254740993",
        group_id = groupId, message_id = "opaque-message-id", message_seq = "opaque-message-id",
        user_id = "9007199254740997", time = 1791597600,
        message = new[] { new { type = "text", data = new { text = "private-body-sentinel" } } }
    };

    private async Task SendAsync(object value) => await socket!.SendAsync(
        new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))),
        WebSocketMessageType.Text, true, stopping.Token);

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        socket?.Abort();
        listener.Close();
        await serving;
        socket?.Dispose();
        stopping.Dispose();
    }
}
