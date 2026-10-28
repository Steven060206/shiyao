using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Images;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.Infrastructure.Tests;

public sealed class ImageMessageHostTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Shiyao-image-host-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SingleSyncDownloadsOriginalBeforeDisconnectAndLeavesOcrForBackground()
    {
        var workspace = Workspace(); var source = Original();
        await using var server = new ImageServer(["asset"], _ => (0, source));
        var host = new NapCatMessageHost(workspace, server.Url);
        var imageRefreshed = false;
        host.DataChanged += () => imageRefreshed |= Images().Any(image => image.Status == "downloaded");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunOnceAsync(timeout.Token);

        Assert.Equal("complete", host.Status.History);
        Assert.Null(host.Status.Error);
        Assert.True(imageRefreshed);
        Assert.Equal("asset", Assert.Single(server.RequestedImages));
        var image = Assert.Single(Images());
        Assert.Equal("downloaded", image.Status);
        Assert.Null(image.Recognition);
        var saved = new AccountImageStore(root, "12345").GetOriginalPath(image)!;
        Assert.NotEqual(source, saved);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(saved));
        Assert.True(server.ImageWasRequestedWhileConnected);
        Assert.Equal("disconnected", host.Status.Transport);
        Assert.Empty(workspace.Snapshot(DateTimeOffset.UtcNow).Items);
    }

    [Fact]
    public async Task OneImageFailureDoesNotAbortOtherDownloadsOrMessageSync()
    {
        var workspace = Workspace(); var source = Original();
        await using var server = new ImageServer(["bad", "good"], asset => asset == "bad" ? (1200, null) : (0, source));
        var host = new NapCatMessageHost(workspace, server.Url);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.RunOnceAsync(timeout.Token);

        Assert.Equal("complete", host.Status.History);
        Assert.NotNull(host.Status.LastSuccessfulSyncAt);
        Assert.Null(host.Status.Error);
        Assert.Equal(new[] { "bad", "good" }, server.RequestedImages);
        var images = Images();
        Assert.Equal("download-failed", Assert.Single(images, image => image.AssetId == "bad").Status);
        Assert.Equal("downloaded", Assert.Single(images, image => image.AssetId == "good").Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopOrGameCancelsDownloadAndRetainsPendingQueue(bool gameInterrupt)
    {
        var workspace = Workspace(); var game = new Game(); var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new ImageServer(["asset"], _ => (0, Original()), gate.Task);
        var host = new NapCatMessageHost(workspace, server.Url, syncController: new ResidentSyncController(game));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var running = host.RunOnceAsync(cancellation.Token);
        try
        {
            await server.ImageRequested.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (gameInterrupt)
            {
                game.Active = true;
                var failure = await Assert.ThrowsAsync<OneBotProtocolException>(() => running);
                Assert.Equal("game-active", failure.Code);
            }
            else
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            }
            Assert.Equal("pending", Assert.Single(Images()).Status);
            Assert.Null(Assert.Single(Images()).RelativePath);
        }
        finally
        {
            cancellation.Cancel(); gate.TrySetResult();
            try { await running; } catch (Exception error) when (error is OperationCanceledException or OneBotProtocolException) { }
        }
    }

    private AccountWorkspace Workspace()
    {
        var workspace = new AccountWorkspace(root, "12345");
        workspace.SetAvailableGroups([new("100", "数学群")], new HashSet<string> { "100" });
        workspace.Execute(new("12345", "confirm-group", "100", "course", 0, Course: "数学"), DateTimeOffset.UtcNow);
        return workspace;
    }
    private ImageEvidenceRecord[] Images() => new AccountImageStore(root, "12345").List().ToArray();
    private string Original()
    {
        var source = Path.Combine(root, "napcat-original.png");
        File.WriteAllBytes(source, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXs8AAAAASUVORK5CYII="));
        return source;
    }
    private sealed class Game : IGameActivityDetector
    {
        public volatile bool Active;
        public bool IsGameRunning() => Active;
    }
    private sealed class ImageServer : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly Task serving;
        private WebSocket? socket;
        public Uri Url { get; }
        public List<string> RequestedImages { get; } = [];
        public TaskCompletionSource ImageRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ImageWasRequestedWhileConnected { get; private set; }

        public ImageServer(string[] assets, Func<string, (int Retcode, string? File)> image, Task? imageGate = null)
        {
            var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start(); var port = ((IPEndPoint)portReservation.LocalEndpoint).Port; portReservation.Stop();
            Url = new($"ws://127.0.0.1:{port}/"); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
            serving = ServeAsync(assets, image, imageGate);
        }
        private async Task ServeAsync(string[] assets, Func<string, (int Retcode, string? File)> image, Task? imageGate)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(stopping.Token);
                socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                var buffer = new byte[8192];
                while (!stopping.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stopping.Token);
                    if (received.MessageType == WebSocketMessageType.Close) break;
                    using var request = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
                    var action = request.RootElement.GetProperty("action").GetString(); var retcode = 0;
                    object? data = action switch
                    {
                        "get_status" => new { online = true, good = true },
                        "get_login_info" => new { user_id = "12345", nickname = "synthetic" },
                        "get_group_list" => new[] { new { group_id = "100", group_name = "数学群" } },
                        "messagepurify_get_pinned_groups" => new { self_id = "12345", group_ids = new[] { new { group_id = "100" } } },
                        "get_group_msg_history" => new { messages = new[] { Message("new", DateTimeOffset.UtcNow, assets), Message("boundary", DateTimeOffset.UtcNow.AddDays(-8), []) } },
                        "get_image" => null,
                        _ => throw new InvalidOperationException("Unexpected operation")
                    };
                    if (action == "get_image")
                    {
                        var asset = request.RootElement.GetProperty("params").GetProperty("file").GetString()!;
                        RequestedImages.Add(asset); ImageWasRequestedWhileConnected = socket.State == WebSocketState.Open;
                        ImageRequested.TrySetResult();
                        if (imageGate is not null) await imageGate.WaitAsync(stopping.Token);
                        var result = image(asset); retcode = result.Retcode; data = new { file = result.File };
                    }
                    var response = JsonSerializer.SerializeToUtf8Bytes(new { status = retcode == 0 ? "ok" : "failed", retcode,
                        echo = request.RootElement.GetProperty("echo").GetString(), data });
                    await socket.SendAsync(new ArraySegment<byte>(response), WebSocketMessageType.Text, true, stopping.Token);
                }
            }
            catch (Exception error) when (error is OperationCanceledException or WebSocketException ||
                stopping.IsCancellationRequested && error is HttpListenerException or ObjectDisposedException) { }
        }
        private static object Message(string id, DateTimeOffset time, string[] assets) => new
        {
            post_type = "message", message_type = "group", self_id = "12345", group_id = "100",
            message_id = id, message_seq = id, user_id = "67890", time = time.ToUnixTimeSeconds(),
            message = assets.Select(asset => new { type = "image", data = new { file = asset } }).ToArray()
        };
        public async ValueTask DisposeAsync()
        {
            stopping.Cancel(); socket?.Abort(); listener.Close(); await serving; socket?.Dispose(); stopping.Dispose();
        }
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
