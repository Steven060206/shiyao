using System.Net.WebSockets;
using System.Text.Json;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.Infrastructure.Tests;

public sealed class OneBotClientTests
{
    [Fact]
    public async Task Client_CorrelatesOutOfOrderResponsesAndKeepsInterleavedEvents()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        var status = client.GetStatusAsync();
        var statusRequest = await socket.NextRequestAsync();
        var login = client.GetLoginInfoAsync();
        var loginRequest = await socket.NextRequestAsync();
        socket.Push(Response(loginRequest, "{\"user_id\":9007199254740993,\"nickname\":\"do not log\"}"));
        socket.Push("{\"post_type\":\"message\",\"message_type\":\"group\",\"message_id\":1}");
        socket.Push(Response(statusRequest, "{\"online\":true,\"good\":true}"));
        Assert.Equal("9007199254740993", (await login).AccountId);
        Assert.True((await status).Online);
        var message = await client.Events.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("message", message.GetProperty("post_type").GetString());
    }

    [Fact]
    public async Task Client_UnknownEchoDoesNotStealResponseOrEvent()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        var status = client.GetStatusAsync();
        var request = await socket.NextRequestAsync();
        socket.Push("{\"status\":\"ok\",\"retcode\":0,\"data\":{},\"echo\":\"late\"}");
        socket.Push("{\"post_type\":\"meta_event\",\"echo\":\"late\",\"meta_event_type\":\"heartbeat\"}");
        socket.Push(Response(request, "{\"online\":true,\"good\":true}"));
        Assert.True((await status).Good);
        Assert.Equal("heartbeat", (await client.Events.ReadAsync()).GetProperty("meta_event_type").GetString());
    }

    [Fact]
    public async Task Client_ReassemblesFragmentedJson()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        var status = client.GetStatusAsync();
        var request = await socket.NextRequestAsync();
        var response = Response(request, "{\"online\":true,\"good\":true}");
        socket.Push(response[..25], false);
        socket.Push(response[25..]);
        Assert.True((await status).Online);
    }

    [Fact]
    public async Task Client_RetcodeErrorIncludesSafeActionAndCodeOnly()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        var status = client.GetStatusAsync();
        var request = await socket.NextRequestAsync();
        socket.Push(Response(request, "null", "failed", 1404, "private-secret-body"));
        var exception = await Assert.ThrowsAsync<OneBotApiException>(() => status);
        Assert.Equal("get_status", exception.Action);
        Assert.Equal(1404, exception.Retcode);
        Assert.DoesNotContain("private-secret-body", exception.Message);
    }

    [Fact]
    public async Task Client_RequestTimeoutNamesActionAndAllowsNextRequest()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromMilliseconds(50));
        var first = client.GetStatusAsync();
        await socket.NextRequestAsync();
        var exception = await Assert.ThrowsAsync<OneBotRequestTimeoutException>(() => first);
        Assert.Equal("get_status", exception.Action);
        var second = client.GetStatusAsync();
        var request = await socket.NextRequestAsync();
        socket.Push(Response(request, "{\"online\":false,\"good\":true}"));
        Assert.False((await second).Online);
    }

    [Fact]
    public async Task Client_CallerCancellationDoesNotCloseConnection()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();
        var first = client.GetStatusAsync(cancellation.Token);
        await socket.NextRequestAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var second = client.GetStatusAsync();
        var request = await socket.NextRequestAsync();
        socket.Push(Response(request, "{\"online\":true,\"good\":true}"));
        Assert.True((await second).Online);
    }

    [Fact]
    public async Task Client_DisposeWaitsForAnInFlightSendBeforeReleasingResources()
    {
        using var socket = new FakeWebSocket { SendGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        var request = client.GetStatusAsync();
        await socket.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var dispose = client.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(dispose.IsCompleted);

        socket.SendGate.TrySetResult(true);
        await dispose;
        await Assert.ThrowsAnyAsync<Exception>(() => request);
    }

    [Fact]
    public async Task Client_PeerCloseFailsPendingRequestAndEventReader()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        var status = client.GetStatusAsync();
        await socket.NextRequestAsync();
        socket.CloseFromPeer();
        var exception = await Assert.ThrowsAsync<OneBotProtocolException>(() => status);
        Assert.Equal("connection-closed", exception.Code);
        await Assert.ThrowsAsync<OneBotProtocolException>(() => client.Events.Completion);
    }

    [Fact]
    public async Task Client_AuthenticationFailureWithoutEchoFailsPendingRequest()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        var status = client.GetStatusAsync();
        await socket.NextRequestAsync();
        socket.Push("{\"status\":\"failed\",\"retcode\":1403,\"message\":\"never log\",\"echo\":null}");
        var exception = await Assert.ThrowsAsync<OneBotApiException>(() => status);
        Assert.Equal(1403, exception.Retcode);
    }

    [Theory]
    [InlineData("{invalid", WebSocketMessageType.Text, "invalid-json")]
    [InlineData("{\"post_type\":\"message\"}", WebSocketMessageType.Binary, "binary-message")]
    public async Task Client_InvalidTransportPayloadFailsClearly(string payload, WebSocketMessageType type, string code)
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        var status = client.GetStatusAsync();
        await socket.NextRequestAsync();
        socket.Push(payload, type: type);
        var exception = await Assert.ThrowsAsync<OneBotProtocolException>(() => status);
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task Client_HistoryUsesOpaqueCursorAndStringGroupId()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromSeconds(2));
        var history = client.GetGroupMessageHistoryAsync("9007199254740993", "-987", 20);
        var request = await socket.NextRequestAsync();
        Assert.Equal("get_group_msg_history", request.GetProperty("action").GetString());
        var parameters = request.GetProperty("params");
        Assert.Equal("9007199254740993", parameters.GetProperty("group_id").GetString());
        Assert.Equal("-987", parameters.GetProperty("message_seq").GetString());
        Assert.False(parameters.GetProperty("reverse_order").GetBoolean());
        socket.Push(Response(request, "{\"messages\":[{\"message_id\":1}]}"));
        Assert.Single(await history);
    }

    [Theory]
    [InlineData("ws://example.com:3001/")]
    [InlineData("http://127.0.0.1:3001/")]
    [InlineData("ws://127.0.0.1:3001/api")]
    [InlineData("ws://127.0.0.1:3001/?access_token=secret")]
    [InlineData("ws://user:secret@127.0.0.1:3001/")]
    public async Task Client_RejectsNonLocalOrCredentialBearingEndpoints(string uri)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => OneBotClient.ConnectAsync(new(uri), "synthetic-token"));
        Assert.DoesNotContain("secret", exception.Message);
    }

    private static string Response(JsonElement request, string data, string status = "ok", int retcode = 0, string message = "") =>
        JsonSerializer.Serialize(new { status, retcode, data = JsonDocument.Parse(data).RootElement.Clone(), echo = request.GetProperty("echo").GetString(), message });
}
