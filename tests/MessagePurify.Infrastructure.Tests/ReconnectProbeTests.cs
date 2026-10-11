using System.Text.Json;
using MessagePurify.QqProbe;

namespace MessagePurify.Infrastructure.Tests;

public sealed class ReconnectProbeTests
{
    [Fact]
    public async Task Reconnect_RereadsFromLatestPageAndDeduplicatesHistoryAndLiveEvents()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = true };
        server.HistoryResponder = (parameters, _) =>
        {
            var cursor = parameters.TryGetProperty("message_seq", out var value) ? value.GetString() : null;
            var boundary = server.ConnectionCount == 1 ? "old-session-cursor" : "new-session-cursor";
            if (cursor is not null)
            {
                Assert.Equal(boundary, cursor); // 第二连接不能复用第一连接的游标。
                return (0, Page(Message("common", now - 60, boundary), Message("outside", now - 172800, "outside-cursor")));
            }
            return server.ConnectionCount == 1
                ? (0, Page(Message("common", now - 60, boundary)))
                : (0, Page(Message("missed", now, "missed-cursor"), Message("common", now - 60, boundary)));
        };
        var report = await ProbeRunner.RunAsync(Options(server, days: 1), "synthetic-test-token");

        Assert.True(report.Success, report.ToMarkdown());
        Assert.Equal(2, server.ConnectionCount);
        Assert.Equal(2, report.ConnectionsVerified);
        Assert.Equal(1, report.ReconnectsCompleted);
        Assert.Equal(2, report.UniqueMessages);
        Assert.Equal(5, report.HistoryMessages);
        Assert.Equal(4, report.RealtimeGroupMessages); // 每页都先推送一条合成事件。
        Assert.Equal(7, report.DuplicateMessages);
        Assert.Equal(1, report.NewMessagesAfterReconnect);
        Assert.Equal([1, 2], report.HistoryResults.Select(result => result.ConnectionIndex));
        Assert.Equal(["date-boundary", "date-boundary"], report.HistoryResults.Select(result => result.StopReason));
        Assert.False(server.HistoryParameters[0].TryGetProperty("message_seq", out _));
        Assert.False(server.HistoryParameters[2].TryGetProperty("message_seq", out _));
        Assert.All(server.Actions, action => Assert.Contains(action, new[] { "get_status", "get_login_info", "get_version_info", "get_group_list", "get_group_msg_history", "messagepurify_get_pinned_groups" }));
        var markdown = report.ToMarkdown();
        foreach (var secret in new[] { "9007199254740993", "9007199254740995", "9007199254740997", "old-session-cursor", "new-session-cursor", "private-body-sentinel", "synthetic-test-token" })
            Assert.DoesNotContain(secret, markdown);
        Assert.Contains("不复用旧游标", markdown);
        Assert.Contains("未验证非预期断网的自动恢复", markdown);
    }

    [Fact]
    public async Task Reconnect_StopsBeforeReadingAChangedAccount()
    {
        await using var server = new LoopbackOneBotServer
        {
            EmitHistoryEvents = false,
            AccountForConnection = index => index == 1 ? "9007199254740993" : "changed-account-sentinel"
        };
        var report = await ProbeRunner.RunAsync(Options(server), "synthetic-test-token");

        Assert.False(report.Success);
        Assert.False(report.AccountVerified);
        Assert.Equal(2, report.ConnectionsAttempted);
        Assert.Equal(1, report.ConnectionsVerified);
        Assert.Equal(0, report.ReconnectsCompleted);
        Assert.Single(server.HistoryParameters);
        Assert.Contains(report.Failures, failure => failure.Code == "account-mismatch" && failure.ConnectionIndex == 2);
        Assert.DoesNotContain("changed-account-sentinel", report.ToMarkdown());
    }

    [Fact]
    public async Task Reconnect_RetainsEarlierHistoryFailureEvenWhenSecondReadSucceeds()
    {
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = false };
        server.HistoryResponder = (_, _) => server.ConnectionCount == 1
            ? (1200, null) : (0, Page());
        var report = await ProbeRunner.RunAsync(Options(server), "synthetic-test-token");

        Assert.False(report.Success);
        Assert.Equal(1, report.ReconnectsCompleted);
        Assert.Equal(1, report.HistoryRetries);
        Assert.Equal(3, server.HistoryParameters.Count);
        Assert.Equal(["api-failed", "empty-page"], report.HistoryResults.Select(result => result.StopReason));
        Assert.Contains(report.Failures, failure => failure.Retcode == 1200 && failure.GroupIndex == 1 && failure.ConnectionIndex == 1);
    }

    [Fact]
    public async Task Reconnect_CancellationPreventsAnyFurtherConnection()
    {
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = false };
        using var cancellation = new CancellationTokenSource();
        server.HistoryResponder = (_, _) =>
        {
            cancellation.Cancel();
            return (0, Page());
        };
        var report = await ProbeRunner.RunAsync(Options(server), "synthetic-test-token", cancellation.Token);

        Assert.False(report.Success);
        Assert.Equal(1, server.ConnectionCount);
        Assert.Equal(0, report.ReconnectsCompleted);
        Assert.Contains(report.Failures, failure => failure.Code == "cancelled");
    }

    private static ProbeArguments Options(LoopbackOneBotServer server, int days = 0) =>
        ProbeArguments.Parse(["--url", server.Url.AbsoluteUri, "--reconnect-rounds", "1", "--history-days", days.ToString(), "--history-pages", days == 0 ? "1" : "3", "--history-count", "2", "--listen-seconds", "1"]);

    private static object Page(params object[] messages) => new { messages };

    private static object Message(string id, long time, string cursor) => new
    {
        post_type = "message", message_type = "group", self_id = "9007199254740993", group_id = "9007199254740995",
        message_id = id, message_seq = cursor, user_id = "9007199254740997", time,
        message = new[] { new { type = "text", data = new { text = "private-body-sentinel" } } }
    };
}
