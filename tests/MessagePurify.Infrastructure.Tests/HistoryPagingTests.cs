using System.Text.Json;
using MessagePurify.QqProbe;

namespace MessagePurify.Infrastructure.Tests;

public sealed class HistoryPagingTests
{
    [Fact]
    public async Task Probe_PaginatesWithOpaqueOldestCursorAndCountsOverlapSeparately()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = false };
        server.HistoryResponder = (parameters, call) =>
        {
            var group = Group(parameters);
            var cursor = parameters.TryGetProperty("message_seq", out var value) ? value.GetString() : null;
            return cursor switch
            {
                null => (0, Page(Message(group, "b", now - 10, "seq-b", "20"), Message(group, "a", now, "seq-a", "30"))),
                "seq-b" => (0, Page(Message(group, "b", now - 10, "seq-b", "20"), Message(group, "c", now - 20, "seq-c", "10"))),
                "seq-c" => (0, Page()),
                _ => throw new InvalidOperationException("unexpected cursor")
            };
        };

        var options = ProbeArguments.Parse(["--url", server.Url.AbsoluteUri, "--history-days", "1", "--history-pages", "4", "--history-count", "2", "--listen-seconds", "0"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");

        Assert.True(report.Success, string.Join(";", report.Failures.Select(f => $"{f.Code}:{f.Retcode}:{f.GroupIndex}")));
        Assert.Equal(4, report.HistoryMessages); // 2 + 2, including the overlap.
        Assert.Equal(3, report.UniqueMessages);
        var result = Assert.Single(report.HistoryResults);
        Assert.Equal((1, 3, 3, "empty-page"), (result.GroupIndex, result.PagesRead, result.UniqueMessages, result.StopReason));
        var cursors = server.HistoryParameters.Select(Cursor).ToArray();
        Assert.Equal(3, cursors.Length);
        Assert.Null(cursors[0]);
        Assert.Equal("seq-b", cursors[1]);
        Assert.Equal("seq-c", cursors[2]);
        Assert.False(server.HistoryParameters[0].GetProperty("reverse_order").GetBoolean());
        Assert.True(server.HistoryParameters[1].GetProperty("reverse_order").GetBoolean());
        Assert.True(server.HistoryParameters[2].GetProperty("reverse_order").GetBoolean());
    }

    [Fact]
    public async Task Probe_UsesSmallestNumericRealSequenceForSameSecondAndStopsAtDateBoundary()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = false };
        server.HistoryResponder = (parameters, _) =>
        {
            var group = Group(parameters);
            return parameters.TryGetProperty("message_seq", out var cursor)
                ? (0, Page(Message(group, "new-low", now - 3600, "opaque-low", "99999999999999999999"), Message(group, "old", now - TimeSpan.FromDays(2).Days * 86400, "opaque-old", "1")))
                : (0, Page(Message(group, "new-high", now - 3600, "opaque-high", "100000000000000000000"), Message(group, "new-low", now - 3600, "opaque-low", "99999999999999999999")));
        };

        var options = ProbeArguments.Parse(["--url", server.Url.AbsoluteUri, "--history-days", "1", "--history-pages", "4", "--history-count", "2", "--listen-seconds", "0"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");

        Assert.True(report.Success, string.Join(";", report.Failures.Select(f => $"{f.Code}:{f.Retcode}:{f.GroupIndex}")));
        Assert.Equal(3, report.HistoryMessages); // The overlap is in-window; the old boundary row is not.
        Assert.Equal("opaque-low", server.HistoryParameters[1].GetProperty("message_seq").GetString());
        Assert.Equal("date-boundary", Assert.Single(report.HistoryResults).StopReason);
    }

    [Fact]
    public async Task Probe_RejectsAmbiguousOrMissingOldestCursor()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var ambiguous = new LoopbackOneBotServer { EmitHistoryEvents = false };
        ambiguous.HistoryResponder = (parameters, _) =>
        {
            var group = Group(parameters);
            return (0, Page(Message(group, "a", now, "cursor-a", null), Message(group, "b", now, "cursor-b", null)));
        };
        var options = ProbeArguments.Parse(["--url", ambiguous.Url.AbsoluteUri, "--history-days", "1", "--history-pages", "2", "--history-count", "2", "--listen-seconds", "0"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");
        Assert.False(report.Success);
        Assert.Contains(report.Failures, failure => failure.Code == "history-cursor-ambiguous" && failure.GroupIndex == 1);

        await using var missing = new LoopbackOneBotServer { EmitHistoryEvents = false };
        missing.HistoryResponder = (parameters, _) =>
        {
            var group = Group(parameters);
            return (0, Page(Message(group, "missing", now, null, null)));
        };
        options = ProbeArguments.Parse(["--url", missing.Url.AbsoluteUri, "--history-days", "1", "--history-pages", "2", "--history-count", "2", "--listen-seconds", "0"]);
        report = await ProbeRunner.RunAsync(options, "synthetic-test-token");
        Assert.False(report.Success);
        Assert.Contains(report.Failures, failure => failure.Code == "history-cursor-missing" && failure.GroupIndex == 1);
    }

    [Fact]
    public async Task Probe_DoesNotPartiallyCountAPageWhenOneMessageFailsParsing()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = false };
        server.HistoryResponder = (parameters, _) =>
        {
            var group = Group(parameters);
            return (0, Page(Message(group, "valid", now, "valid-cursor", "1"), Message("wrong-group", "invalid", now - 1, "invalid-cursor", "2")));
        };
        var options = ProbeArguments.Parse(["--url", server.Url.AbsoluteUri, "--history-days", "1", "--history-pages", "2", "--history-count", "2", "--listen-seconds", "0"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");

        Assert.False(report.Success);
        Assert.Equal(0, report.HistoryMessages);
        Assert.Equal(0, report.UniqueMessages);
        Assert.Contains(report.Failures, failure => failure.Code == "group-mismatch" && failure.GroupIndex == 1);
        Assert.Equal(0, Assert.Single(report.HistoryResults).PagesRead);
    }

    [Fact]
    public async Task Probe_DoesNotLoopOnRepeatedPageAndFailsWhenPageCapIsReached()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var repeated = new LoopbackOneBotServer { EmitHistoryEvents = false };
        repeated.HistoryResponder = (parameters, _) =>
        {
            var group = Group(parameters);
            return (0, Page(Message(group, "same", now, "same-cursor", "1"), Message(group, "same-2", now - 1, "same-cursor-2", "2")));
        };
        var options = ProbeArguments.Parse(["--url", repeated.Url.AbsoluteUri, "--history-days", "1", "--history-pages", "20", "--history-count", "2", "--listen-seconds", "0"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");
        Assert.Contains(report.Failures, failure => failure.Code == "history-no-progress");
        Assert.InRange(repeated.HistoryParameters.Count, 1, 2);

        await using var capped = new LoopbackOneBotServer { EmitHistoryEvents = false };
        capped.HistoryResponder = (parameters, _) =>
        {
            var group = Group(parameters);
            return (0, Page(Message(group, "one", now, "one-cursor", "1"), Message(group, "two", now - 1, "two-cursor", "2")));
        };
        options = ProbeArguments.Parse(["--url", capped.Url.AbsoluteUri, "--history-days", "1", "--history-pages", "1", "--history-count", "2", "--listen-seconds", "0"]);
        report = await ProbeRunner.RunAsync(options, "synthetic-test-token");
        Assert.Contains(report.Failures, failure => failure.Code == "history-page-limit" && failure.GroupIndex == 1);
        Assert.Single(capped.HistoryParameters);
    }

    [Fact]
    public async Task Probe_RetriesRetcode1200OnceAndContinuesWithNextGroupAfterRepeatedFailure()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var server = new LoopbackOneBotServer { GroupCount = 2, EmitHistoryEvents = false };
        server.HistoryResponder = (parameters, call) =>
        {
            var group = Group(parameters);
            if (group == "9007199254740995") return (1200, null);
            return (0, Page(Message(group, "second-group", now, "second-cursor", "2")));
        };
        var options = ProbeArguments.Parse(["--url", server.Url.AbsoluteUri, "--history-groups", "all", "--history-days", "0", "--history-pages", "1", "--history-count", "2", "--listen-seconds", "0"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");

        Assert.False(report.Success);
        Assert.Equal(1, report.HistoryRetries);
        Assert.Contains(report.Failures, failure => failure.Code == "api-failed" && failure.GroupIndex == 1 && failure.Retcode == 1200);
        Assert.Equal(2, report.HistoryResults.Count);
        Assert.Equal("api-failed", report.HistoryResults[0].StopReason);
        Assert.Equal("sampled", report.HistoryResults[1].StopReason);
        Assert.Contains(server.HistoryParameters, item => Group(item) == "9007199254740996");
    }

    [Fact]
    public async Task Probe_RetriesOneApiFailureAndKeepsReadingAfterAShortPage()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var server = new LoopbackOneBotServer { EmitHistoryEvents = false };
        server.HistoryResponder = (parameters, call) =>
        {
            var group = Group(parameters);
            if (call == 1) return (1200, null);
            if (call == 2) return (0, Page(Message(group, "first", now, "cursor-first", "1")));
            return (0, Page());
        };
        var options = ProbeArguments.Parse(["--url", server.Url.AbsoluteUri, "--history-days", "1", "--history-pages", "3", "--history-count", "2", "--listen-seconds", "0"]);
        var report = await ProbeRunner.RunAsync(options, "synthetic-test-token");

        Assert.True(report.Success);
        Assert.Equal(1, report.HistoryRetries);
        Assert.Equal(3, server.HistoryParameters.Count);
        Assert.Equal("empty-page", Assert.Single(report.HistoryResults).StopReason);
        Assert.Equal(1, report.HistoryMessages);
    }

    private static string Group(JsonElement parameters) => parameters.GetProperty("group_id").GetString()!;

    private static string? Cursor(JsonElement parameters) => parameters.TryGetProperty("message_seq", out var cursor) ? cursor.GetString() : null;

    private static object Page(params object[] messages) => new { messages };

    private static object Message(string group, string id, long time, string? sequence, string? realSequence)
    {
        var value = new Dictionary<string, object?>
        {
            ["post_type"] = "message", ["message_type"] = "group", ["self_id"] = "9007199254740993",
            ["group_id"] = group, ["message_id"] = id, ["user_id"] = "9007199254740997", ["time"] = time,
            ["message"] = new[] { new { type = "text", data = new { text = "private-body-sentinel" } } }
        };
        if (sequence is not null) value["message_seq"] = sequence;
        if (realSequence is not null) value["real_seq"] = realSequence;
        return value;
    }
}
