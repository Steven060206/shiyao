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
        try
        {
            var context = await listener.GetContextAsync().WaitAsync(stopping.Token);
            ObservedAuthorization = context.Request.Headers["Authorization"];
            socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            var buffer = new byte[8192];
            while (!stopping.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stopping.Token);
                if (frame.MessageType == WebSocketMessageType.Close) break;
                using var request = JsonDocument.Parse(buffer.AsMemory(0, frame.Count));
                var action = request.RootElement.GetProperty("action").GetString()!;
                Actions.Add(action);
                object data = action switch
                {
                    "get_status" => new { online = Online, good = true },
                    "get_login_info" => new { user_id = "9007199254740993", nickname = "private-nickname-sentinel" },
                    "get_version_info" => new { app_name = "NapCat.Onebot", app_version = "4.0.0", protocol_version = "v11" },
                    "get_group_list" => new[] { new { group_id = "9007199254740995", group_name = "private-group-sentinel" } },
                    "get_group_msg_history" => new { messages = new[] { GroupMessage() } },
                    _ => throw new InvalidOperationException("Unexpected write operation")
                };
                if (action == "get_group_msg_history")
                {
                    var groupEvent = JsonSerializer.SerializeToElement(GroupMessage()).EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone());
                    if (EventAccount is null) groupEvent.Remove("self_id");
                    else groupEvent["self_id"] = JsonSerializer.SerializeToElement(EventAccount);
                    await SendAsync(groupEvent);
                    await SendAsync(new { post_type = "message", message_type = "private", self_id = "9007199254740993" });
                    await SendAsync(new { post_type = "meta_event", meta_event_type = "heartbeat", self_id = "9007199254740993" });
                }
                await SendAsync(new { status = "ok", retcode = 0, echo = request.RootElement.GetProperty("echo").GetString(), data });
            }
        }
        catch (WebSocketException) { } // 客户端正常 Dispose 会终止探针连接。
        catch (Exception exception) when (stopping.IsCancellationRequested && exception is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
    }

    private static object GroupMessage() => new
    {
        post_type = "message", message_type = "group", self_id = "9007199254740993",
        group_id = "9007199254740995", message_id = "opaque-message-id", message_seq = "opaque-message-id",
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
