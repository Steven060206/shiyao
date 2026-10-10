using MessagePurify.Infrastructure.NapCat;
using MessagePurify.QqProbe;

namespace MessagePurify.Infrastructure.Tests;

public sealed class OneBotTransportTests
{
    [Fact]
    public async Task Connect_UsesBearerHeaderAndReceivesThroughRealLoopbackWebSocket()
    {
        await using var server = new LoopbackOneBotServer();
        await using var client = await OneBotClient.ConnectAsync(server.Url, "synthetic-test-token");
        Assert.True((await client.GetStatusAsync()).Online);
        Assert.Equal("Bearer synthetic-test-token", server.ObservedAuthorization);
        Assert.Equal("9007199254740993", (await client.GetLoginInfoAsync()).AccountId);
        Assert.Equal("4.0.0", (await client.GetVersionInfoAsync()).AppVersion);
        Assert.Equal("9007199254740995", Assert.Single(await client.GetGroupListAsync()).GroupId);
    }

    [Fact]
    public async Task Probe_ReportsOnlyCountsAndVersionsWhileDeduplicatingOverlap()
    {
        await using var server = new LoopbackOneBotServer();
        var options = ProbeArguments.Parse(["--url", server.Url.AbsoluteUri, "--listen-seconds", "1"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");
        Assert.True(report.Success);
        Assert.True(report.AccountVerified);
        Assert.Equal(1, report.GroupCount);
        Assert.Equal(1, report.HistoryMessages);
        Assert.Equal(1, report.RealtimeGroupMessages);
        Assert.Equal(1, report.UniqueMessages);
        Assert.Equal(1, report.PrivateEventsIgnored);
        Assert.Equal(["get_status", "get_login_info", "get_version_info", "get_group_list", "get_group_msg_history"], server.Actions);
        var markdown = report.ToMarkdown();
        foreach (var sentinel in new[] { "9007199254740993", "9007199254740995", "9007199254740997", "opaque-message-id", "private-body-sentinel", "private-group-sentinel", "private-nickname-sentinel", "synthetic-test-token" })
            Assert.DoesNotContain(sentinel, markdown);
        Assert.Contains("4.0.0", markdown);
    }

    [Fact]
    public async Task Probe_OfflineDoesNotReadGroupHistory()
    {
        await using var server = new LoopbackOneBotServer { Online = false };
        var options = ProbeArguments.Parse(["--url", server.Url.AbsoluteUri, "--listen-seconds", "0"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");
        Assert.False(report.Success);
        Assert.Contains(report.Failures, failure => failure.Code == "qq-offline");
        Assert.Equal(["get_status"], server.Actions);
    }

    [Theory]
    [InlineData("different-account", "account-mismatch")]
    [InlineData(null, "account-unknown")]
    public async Task Probe_RejectsUnverifiedRealtimeAccount(string? eventAccount, string errorCode)
    {
        await using var server = new LoopbackOneBotServer { EventAccount = eventAccount };
        var options = ProbeArguments.Parse(["--url", server.Url.AbsoluteUri, "--listen-seconds", "1"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");
        Assert.False(report.Success);
        Assert.Equal(0, report.RealtimeGroupMessages);
        Assert.Contains(report.Failures, failure => failure.Code == errorCode);
    }
}
