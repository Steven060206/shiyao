using MessagePurify.QqProbe;

namespace MessagePurify.Infrastructure.Tests;

public sealed class PinnedGroupProbeTests
{
    private const string First = "9007199254740995";
    private const string Second = "9007199254740996";

    [Fact]
    public async Task OnlyPinnedGroupsReachHistoryAndRealtimeParsingEvenWithAllOption()
    {
        await using var server = new LoopbackOneBotServer
        {
            GroupCount = 2, EmitHistoryEvents = false,
            // Deliberately no message body: an excluded event must never reach the parser.
            EventsOnFirstPinnedRead = [new { post_type = "message", message_type = "group", self_id = "9007199254740993", group_id = Second }],
            PinnedResponder = (_, _) => (0, Pins(First))
        };
        var report = await ProbeRunner.RunAsync(Options(server), "synthetic-test-token");
        Assert.True(report.Success, report.ToMarkdown());
        Assert.Equal(2, report.GroupCount);
        Assert.Equal(1, report.PinnedGroupCount);
        Assert.Equal(First, Assert.Single(server.HistoryParameters).GetProperty("group_id").GetString());
        Assert.Equal(1, report.UnpinnedGroupEventsIgnored);
        Assert.Equal(0, report.RealtimeGroupMessages);
        Assert.Equal(1, report.UniqueMessages);
        Assert.DoesNotContain(Second, report.ToMarkdown());
    }

    [Fact]
    public async Task NoPinnedGroupsReadsNoHistoryAndExcludesGroupEvents()
    {
        await using var server = new LoopbackOneBotServer
        {
            PinnedResponder = (_, _) => (0, Pins()),
            EventsOnFirstPinnedRead = [new { post_type = "message", message_type = "group", self_id = "9007199254740993", group_id = First }]
        };
        var report = await ProbeRunner.RunAsync(Options(server), null);
        Assert.True(report.Success, report.ToMarkdown());
        Assert.Empty(server.HistoryParameters);
        Assert.Equal(0, report.UniqueMessages);
        Assert.Equal(1, report.UnpinnedGroupEventsIgnored);
    }

    [Theory]
    [InlineData(1404, false)]
    [InlineData(0, true)]
    public async Task MissingOrMalformedPinnedStateNeverFallsBackToAllGroups(int retcode, bool malformed)
    {
        await using var server = new LoopbackOneBotServer { GroupCount = 2,
            PinnedResponder = (_, _) => (retcode, malformed ? new { self_id = "9007199254740993" } : null) };
        var report = await ProbeRunner.RunAsync(Options(server), null);
        Assert.False(report.Success);
        Assert.Empty(server.HistoryParameters);
        Assert.Equal(0, report.UniqueMessages);
        Assert.Null(report.PinnedGroupCount);
    }

    [Fact]
    public async Task LiveEventUsesTheCurrentScopeInsteadOfTheInitialSnapshot()
    {
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = true,
            PinnedResponder = (_, read) => (0, read <= 2 ? Pins(First) : Pins()) };
        var report = await ProbeRunner.RunAsync(Options(server), null);
        Assert.True(report.Success, report.ToMarkdown());
        Assert.Single(server.HistoryParameters);
        Assert.Equal(1, report.HistoryMessages);
        Assert.Equal(0, report.RealtimeGroupMessages);
        Assert.Equal(1, report.UnpinnedGroupEventsIgnored);
    }

    [Fact]
    public async Task LosingPinnedStateBeforeALiveEventStopsBeforeParsingItsBody()
    {
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = true,
            PinnedResponder = (_, read) => read <= 2 ? (0, Pins(First)) : (1200, null) };
        var report = await ProbeRunner.RunAsync(Options(server), null);
        Assert.False(report.Success);
        Assert.Equal(0, report.RealtimeGroupMessages);
        Assert.Contains(report.Failures, failure => failure.Action == "messagepurify_get_pinned_groups");
    }

    [Fact]
    public async Task UnpinningBetweenPagesStopsWithoutAnotherHistoryRequest()
    {
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = false,
            PinnedResponder = (_, read) => (0, read <= 2 ? Pins(First) : Pins()) };
        var report = await ProbeRunner.RunAsync(Options(server) with { HistoryPages = 3, HistoryDays = 0 }, null);
        Assert.True(report.Success, report.ToMarkdown());
        Assert.Single(server.HistoryParameters);
        Assert.Equal("unpinned", Assert.Single(report.HistoryResults).StopReason);
    }

    [Fact]
    public async Task ReconnectReplacesThePinnedScope()
    {
        await using var server = new LoopbackOneBotServer { GroupCount = 2, EmitHistoryEvents = false,
            PinnedResponder = (connection, _) => (0, Pins(connection == 1 ? First : Second)) };
        var report = await ProbeRunner.RunAsync(Options(server) with { ReconnectRounds = 1 }, null);
        Assert.True(report.Success, report.ToMarkdown());
        Assert.Equal([First, Second], server.HistoryParameters.Select(item => item.GetProperty("group_id").GetString()));
    }

    [Fact]
    public async Task UnpinningBeforeTheFirstPageStopsThatGroup()
    {
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = false,
            PinnedResponder = (_, read) => (0, read == 1 ? Pins(First) : Pins()) };
        var report = await ProbeRunner.RunAsync(Options(server), null);
        Assert.True(report.Success, report.ToMarkdown());
        Assert.Empty(server.HistoryParameters);
        Assert.Equal("unpinned", Assert.Single(report.HistoryResults).StopReason);
    }

    [Fact]
    public async Task LosingPinnedStateDuringPagingStopsAllFurtherHistory()
    {
        await using var server = new LoopbackOneBotServer { GroupCount = 2, EmitHistoryEvents = false,
            PinnedResponder = (_, read) => read == 1 ? (0, Pins(First, Second)) : (1200, null) };
        var report = await ProbeRunner.RunAsync(Options(server), null);
        Assert.False(report.Success);
        Assert.Empty(server.HistoryParameters);
        Assert.Equal(1, report.HistoryGroupsAttempted);
        Assert.Equal("pinned-state-unavailable", Assert.Single(report.HistoryResults).StopReason);
    }

    [Fact]
    public async Task PinnedListFromAnotherAccountStopsBeforeHistory()
    {
        await using var server = new LoopbackOneBotServer { PinnedResponder = (_, _) =>
            (0, new { self_id = "different-account", group_ids = new[] { new { group_id = First } } }) };
        var report = await ProbeRunner.RunAsync(Options(server), null);
        Assert.False(report.Success);
        Assert.Empty(server.HistoryParameters);
        Assert.Contains(report.Failures, failure => failure.Code == "account-mismatch");
    }

    private static object Pins(params string[] ids) => new { self_id = "9007199254740993", group_ids = ids.Select(group_id => new { group_id }).ToArray() };
    private static ProbeArguments Options(LoopbackOneBotServer server) => ProbeArguments.Parse([
        "--url", server.Url.AbsoluteUri, "--history-groups", "all", "--listen-seconds", "1"]);
}
