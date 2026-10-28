using System.Text.Json;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.Infrastructure.Tests;

public sealed class OneBotImageTests
{
    [Fact]
    public async Task Image_UsesReadOnlyGetImageAndReturnsOnlyLocalPath()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket);
        var image = client.GetImageAsync("opaque-private-file");
        var request = await socket.NextRequestAsync();
        Assert.Equal("get_image", request.GetProperty("action").GetString());
        Assert.Equal("opaque-private-file", request.GetProperty("params").GetProperty("file").GetString());
        Assert.Single(request.GetProperty("params").EnumerateObject());
        socket.Push(Response(request, new { file = "C:\\synthetic\\image.png", url = "https://example.invalid/private", file_size = "1234" }));
        Assert.Equal(new OneBotImage("C:\\synthetic\\image.png"), await image);
    }

    [Theory]
    [InlineData("relative.png")]
    [InlineData("https://example.invalid/private.png")]
    [InlineData("file:///C:/private.png")]
    [InlineData("\\\\server\\share\\private.png")]
    [InlineData("")]
    public async Task Image_RejectsNonLocalPathWithoutLeakingResponse(string path)
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket);
        var image = client.GetImageAsync("private-asset");
        var request = await socket.NextRequestAsync();
        socket.Push(Response(request, new { file = path, url = "secret-url" }));
        var error = await Assert.ThrowsAsync<OneBotProtocolException>(() => image);
        Assert.Equal("image-path-invalid", error.Code);
        Assert.DoesNotContain("private", error.Message);
        Assert.DoesNotContain("secret-url", error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"file\":null}")]
    [InlineData("{\"file\":123}")]
    public async Task Image_RejectsMissingOrWrongPathFieldAndNextRequestWorks(string data)
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket);
        var first = client.GetImageAsync("asset");
        var request = await socket.NextRequestAsync();
        socket.Push(Response(request, JsonDocument.Parse(data).RootElement));
        await Assert.ThrowsAsync<OneBotProtocolException>(() => first);
        var second = client.GetImageAsync("asset");
        request = await socket.NextRequestAsync();
        socket.Push(Response(request, new { file = "C:\\synthetic\\image.png" }));
        Assert.NotNull(await second);
    }

    [Fact]
    public async Task Image_CancellationDoesNotCloseConnectionOrConsumeNextReply()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket);
        using var cancel = new CancellationTokenSource();
        var first = client.GetImageAsync("first", cancel.Token);
        var oldRequest = await socket.NextRequestAsync();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var second = client.GetImageAsync("second");
        var request = await socket.NextRequestAsync();
        socket.Push(Response(oldRequest, new { file = "C:\\synthetic\\wrong.png" }));
        socket.Push(Response(request, new { file = "C:\\synthetic\\correct.png" }));
        Assert.Equal("C:\\synthetic\\correct.png", (await second).FilePath);
    }

    [Fact]
    public async Task Image_BlankAssetRejectedBeforeRequest()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetImageAsync(" "));
    }

    [Fact]
    public async Task Image_TimeoutUsesSafeActionAndNextRequestStillWorks()
    {
        using var socket = new FakeWebSocket();
        await using var client = new OneBotClient(socket, TimeSpan.FromMilliseconds(80));
        var first = client.GetImageAsync("private-timeout-asset");
        await socket.NextRequestAsync();
        var failure = await Assert.ThrowsAsync<OneBotRequestTimeoutException>(() => first);
        Assert.Equal("get_image", failure.Action);
        Assert.DoesNotContain("private-timeout-asset", failure.Message);
        var second = client.GetImageAsync("next");
        var request = await socket.NextRequestAsync();
        socket.Push(Response(request, new { file = "C:\\synthetic\\image.png" }));
        Assert.NotNull(await second);
    }

    private static string Response(JsonElement request, object data) => JsonSerializer.Serialize(new
    {
        status = "ok", retcode = 0, data, echo = request.GetProperty("echo").GetString()
    });
}
