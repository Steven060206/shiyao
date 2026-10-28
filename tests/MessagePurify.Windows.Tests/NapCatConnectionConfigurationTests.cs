using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class NapCatConnectionConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-connection-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnvironmentCredentialHasPriorityAndDoesNotReadCorruptLocalFiles()
    {
        Write("onebot11_1.json", "corrupt");
        Assert.Equal("environment-only", NapCatConnectionConfiguration.ReadAccessToken("environment-only", root));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("::1")]
    [InlineData("LOCALHOST")]
    public void DirectApplicationLaunchReadsOnlyTheSingleEnabledLocalEndpoint(string host)
    {
        Write("onebot11_1.json", Config(new { enable = false, port = 3001, host, token = "disabled" },
            new { enable = false, port = 6099, host, token = "disabled-other-port" },
            new { enable = true, port = 3001, host = "0.0.0.0", token = "remote-bind" },
            new { enable = true, port = 3001, host, token = "synthetic-local" }));
        Write("unrelated.json", "corrupt");
        Assert.Equal("synthetic-local", NapCatConnectionConfiguration.ReadAccessToken(null, root));
        Assert.Equal(3001, NapCatConnectionConfiguration.Read(null, root).Endpoint.Port);
    }

    [Theory]
    [InlineData("127.0.0.1", 13001)]
    [InlineData("localhost", 3001)]
    [InlineData("::1", 23456)]
    public void EndpointAndCredentialComeFromTheSameConfiguration(string host, int port)
    {
        Write("onebot11_1.json", Config(new { enable = true, port, host, token = "configured-token" }));
        var connection = NapCatConnectionConfiguration.Read(null, root);
        Assert.Equal(new UriBuilder("ws", host, port).Uri, connection.Endpoint);
        Assert.Equal("configured-token", connection.AccessToken);
        var overridden = NapCatConnectionConfiguration.Read("environment-token", root);
        Assert.Equal(connection.Endpoint, overridden.Endpoint);
        Assert.Equal("environment-token", overridden.AccessToken);
    }

    [Fact]
    public async Task ConfiguredNonDefaultPortConnectsToTheActualLoopbackWebSocket()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var server = new HttpListener();
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        Write("onebot11_1.json", Config(new { enable = true, port, host = "127.0.0.1", token = "loopback-token" }));
        var configuration = NapCatConnectionConfiguration.Read(null, root);
        var connecting = OneBotClient.ConnectAsync(configuration.Endpoint, configuration.AccessToken);
        var request = await server.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        using var socket = (await request.AcceptWebSocketAsync(null)).WebSocket;
        await using var client = await connecting;
        Assert.Equal("Bearer loopback-token", request.Request.Headers["Authorization"]);
    }

    [Fact]
    public void MissingConfigurationUsesTheNewDefault()
    {
        Assert.Equal(new Uri("ws://127.0.0.1:13001/"), NapCatConnectionConfiguration.Read(null, root).Endpoint);
    }

    [Fact]
    public void MultipleEnabledPortsAreAmbiguousEvenWithAnEnvironmentCredential()
    {
        Write("onebot11_1.json", Config(
            new { enable = true, port = 3001, host = "127.0.0.1", token = "old-token" },
            new { enable = true, port = 13001, host = "127.0.0.1", token = "new-token" }));
        var error = Assert.Throws<OneBotProtocolException>(() => NapCatConnectionConfiguration.Read("override", root));
        Assert.Equal("napcat-configuration-ambiguous", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void InvalidLocalPortIsRejected(int port)
    {
        Write("onebot11_1.json", Config(new { enable = true, port, host = "127.0.0.1" }));
        Assert.Equal("napcat-configuration-invalid",
            Assert.Throws<OneBotProtocolException>(() => NapCatConnectionConfiguration.Read(null, root)).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousEndpointsNeverGuessAndExceptionsDoNotExposeCredentials(bool acrossFiles)
    {
        var server = new { enable = true, port = 3001, host = "127.0.0.1", token = "private-sentinel" };
        Write("onebot11_1.json", acrossFiles ? Config(server) : Config(server, server));
        if (acrossFiles) Write("onebot11_2.json", Config(server));
        var error = Assert.Throws<OneBotProtocolException>(() => NapCatConnectionConfiguration.ReadAccessToken(null, root));
        Assert.Equal("napcat-configuration-ambiguous", error.Code);
        Assert.DoesNotContain("private-sentinel", error.ToString());
    }

    [Theory]
    [InlineData("{\"private-sentinel\":")]
    [InlineData("{\"network\":{\"websocketServers\":{}}}")]
    [InlineData("{\"network\":{\"websocketServers\":[{\"enable\":true,\"host\":\"127.0.0.1\",\"port\":3001,\"token\":42}]}}")]
    public void MalformedConfigurationStopsWithoutExposingItsContents(string content)
    {
        Write("onebot11_1.json", content);
        var error = Assert.Throws<OneBotProtocolException>(() => NapCatConnectionConfiguration.ReadAccessToken(null, root));
        Assert.Equal("napcat-configuration-invalid", error.Code);
        Assert.DoesNotContain("private-sentinel", error.ToString());
    }

    [Fact]
    public void MissingConfigurationAndEndpointWithoutCredentialRemainCompatible()
    {
        Assert.Null(NapCatConnectionConfiguration.ReadAccessToken(null, root));
        Write("onebot11_1.json", Config(new { enable = true, port = 3001, host = "localhost" }));
        Assert.Null(NapCatConnectionConfiguration.ReadAccessToken(null, root));
    }

    private static string Config(params object[] servers) => JsonSerializer.Serialize(new { network = new { websocketServers = servers } });
    private void Write(string name, string content)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name), content);
    }
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
