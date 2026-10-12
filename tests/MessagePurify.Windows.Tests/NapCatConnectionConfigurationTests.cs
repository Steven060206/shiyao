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
            new { enable = true, port = 6099, host, token = "wrong-port" },
            new { enable = true, port = 3001, host = "0.0.0.0", token = "remote-bind" },
            new { enable = true, port = 3001, host, token = "synthetic-local" }));
        Write("unrelated.json", "corrupt");
        Assert.Equal("synthetic-local", NapCatConnectionConfiguration.ReadAccessToken(null, root));
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
