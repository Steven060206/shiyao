using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace MessagePurify.Infrastructure.Tests;

internal sealed class FakeWebSocket : WebSocket
{
    private readonly Channel<(byte[] Data, WebSocketMessageType Type, bool End)> incoming = Channel.CreateUnbounded<(byte[], WebSocketMessageType, bool)>();
    private readonly Channel<JsonElement> sent = Channel.CreateUnbounded<JsonElement>();
    private WebSocketState state = WebSocketState.Open;
    public TaskCompletionSource<bool>? SendGate { get; set; }
    public TaskCompletionSource<bool> SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;
    public override WebSocketState State => state;

    public void Push(string json, bool end = true, WebSocketMessageType type = WebSocketMessageType.Text) =>
        incoming.Writer.TryWrite((Encoding.UTF8.GetBytes(json), type, end));

    public void CloseFromPeer() => incoming.Writer.TryWrite(([], WebSocketMessageType.Close, true));

    public async Task<JsonElement> NextRequestAsync() => await sent.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        if (SendGate is not null)
        {
            SendStarted.TrySetResult(true);
            await SendGate.Task;
        }
        using var document = JsonDocument.Parse(buffer.AsMemory());
        sent.Writer.TryWrite(document.RootElement.Clone());
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        var frame = await incoming.Reader.ReadAsync(cancellationToken);
        frame.Data.AsSpan().CopyTo(buffer.AsSpan());
        if (frame.Type == WebSocketMessageType.Close) state = WebSocketState.CloseReceived;
        return new(frame.Data.Length, frame.Type, frame.End);
    }

    public override void Abort() { state = WebSocketState.Aborted; incoming.Writer.TryComplete(); }
    public override void Dispose() => Abort();
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { state = WebSocketState.Closed; return Task.CompletedTask; }
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => CloseAsync(closeStatus, statusDescription, cancellationToken);
}
