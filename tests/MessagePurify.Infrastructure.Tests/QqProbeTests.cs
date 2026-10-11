using MessagePurify.QqProbe;

namespace MessagePurify.Infrastructure.Tests;

public sealed class QqProbeTests
{
    [Fact]
    public void Options_DefaultToOneGroupOnePageAndLocalRoot()
    {
        var options = ProbeArguments.Parse([]);
        Assert.Equal(new Uri("ws://127.0.0.1:3001/"), options.Url);
        Assert.Equal(30, options.ListenSeconds);
        Assert.Equal(1, options.HistoryGroups);
        Assert.Equal(20, options.HistoryCount);
        Assert.Equal(0, options.ReconnectRounds);
    }

    [Fact]
    public void Options_AllowExplicitReadLimitsAndReport()
    {
        var options = ProbeArguments.Parse(["--url", "ws://localhost:3011/", "--listen-seconds", "0", "--history-groups", "0", "--history-count", "10", "--output", "report.md"]);
        Assert.Equal(0, options.ListenSeconds);
        Assert.Equal(0, options.HistoryGroups);
        Assert.Equal(10, options.HistoryCount);
        Assert.Equal("report.md", options.Output);
    }

    [Fact]
    public void Options_DefaultHistoryWindowUsesOnePage()
    {
        var options = ProbeArguments.Parse([]);
        Assert.Equal(0, options.HistoryDays);
        Assert.Equal(1, options.HistoryPages);
    }

    [Fact]
    public void Options_DefaultsToTwentyPagesWhenHistoryDaysAreRequested()
    {
        var options = ProbeArguments.Parse(["--history-days", "7"]);
        Assert.Equal(7, options.HistoryDays);
        Assert.Equal(20, options.HistoryPages);
    }

    [Fact]
    public void Options_AllGroupsUsesUnboundedSentinel()
    {
        var options = ProbeArguments.Parse(["--history-groups", "all"]);
        Assert.Equal(int.MaxValue, options.HistoryGroups);
    }

    [Theory]
    [InlineData("--token", "private-secret")]
    [InlineData("--send-message", "hello")]
    [InlineData("--listen-seconds", "-1")]
    [InlineData("--history-groups", "-1")]
    [InlineData("--history-count", "0")]
    [InlineData("--history-count", "101")]
    [InlineData("--history-days", "8")]
    [InlineData("--history-pages", "0")]
    [InlineData("--history-pages", "51")]
    [InlineData("--reconnect-rounds", "-1")]
    [InlineData("--reconnect-rounds", "3")]
    [InlineData("--unknown", "private-secret")]
    [InlineData("--url", "ws://127.0.0.1:3001/?access_token=private-secret")]
    public void Options_RejectInvalidArgumentsWithoutEchoingPrivateValues(string option, string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => ProbeArguments.Parse([option, value]));
        Assert.DoesNotContain("private-secret", exception.Message);
    }

    [Fact]
    public void Options_RejectDuplicateAndMissingValues()
    {
        Assert.Throws<ArgumentException>(() => ProbeArguments.Parse(["--listen-seconds", "0", "--listen-seconds", "1"]));
        Assert.Throws<ArgumentException>(() => ProbeArguments.Parse(["--output"]));
    }

    [Fact]
    public void Options_RejectsSingleItemPagesWhenPagingIsEnabled()
    {
        Assert.Throws<ArgumentException>(() => ProbeArguments.Parse(["--history-pages", "2", "--history-count", "1"]));
        Assert.Throws<ArgumentException>(() => ProbeArguments.Parse(["--history-days", "1", "--history-count", "1"]));
    }

    [Fact]
    public void Options_AllowTwoControlledReconnects()
    {
        Assert.Equal(2, ProbeArguments.Parse(["--reconnect-rounds", "2"]).ReconnectRounds);
    }
}
