using System.Net.WebSockets;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.QqProbe;

public static class ProbeRunner
{
    public static async Task<ProbeReport> RunAsync(ProbeArguments options, string? accessToken,
        CancellationToken cancellationToken = default)
    {
        var report = new ProbeReport { HistoryDays = options.HistoryDays, HistoryPageLimit = options.HistoryPages, ReconnectsRequested = options.ReconnectRounds };
        string? expectedAccountId = null;
        for (var connectionIndex = 1; connectionIndex <= options.ReconnectRounds + 1; connectionIndex++)
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<bool>? consuming = null;
            var failuresBeforeConnection = report.Failures.Count;
            try
            {
                if (connectionIndex > 1) await Task.Delay(TimeSpan.FromSeconds(2), session.Token);
                report.ConnectionsAttempted++;
                report.TransportConnected = false;
                report.AccountVerified = false;
                report.QqOnline = null;
                report.PinnedGroupCount = null;
                await using var client = await OneBotClient.ConnectAsync(options.Url, accessToken, cancellationToken: session.Token);
                report.TransportConnected = true;
                var status = await client.GetStatusAsync(session.Token);
                report.QqOnline = status.Online;
                if (!status.Online) { report.Fail(new("qq-offline", ConnectionIndex: connectionIndex)); return report; }
                var login = await client.GetLoginInfoAsync(session.Token);
                if (expectedAccountId is not null && login.AccountId != expectedAccountId)
                    throw new OneBotProtocolException("account-mismatch");
                expectedAccountId ??= login.AccountId;
                report.AccountVerified = true;
                report.ConnectionsVerified++;
                report.Version = await client.GetVersionInfoAsync(session.Token);
                var groups = await client.GetGroupListAsync(session.Token);
                report.GroupCount = groups.Count;
                var pinnedGroups = await client.GetPinnedGroupIdsAsync(login.AccountId, session.Token);
                report.PinnedGroupCount = groups.Count(group => pinnedGroups.Contains(group.GroupId));
                consuming = ConsumeAsync(client, login.AccountId, connectionIndex, report, session);
                var groupIndex = 0;
                foreach (var group in groups.Where(group => pinnedGroups.Contains(group.GroupId)).Take(options.HistoryGroups))
                {
                    session.Token.ThrowIfCancellationRequested();
                    report.HistoryGroupsAttempted++;
                    await ReadHistoryAsync(client, login.AccountId, group.GroupId, ++groupIndex, connectionIndex, options, report, session.Token);
                }
                await Task.Delay(TimeSpan.FromSeconds(options.ListenSeconds), session.Token);
                session.Token.ThrowIfCancellationRequested();
                session.Cancel();
                if (!await consuming) return report;
                consuming = null;
                if (connectionIndex > 1) report.ReconnectsCompleted++;
            }
            catch (OperationCanceledException)
            {
                if (report.Failures.Count == failuresBeforeConnection)
                    report.Fail(new(cancellationToken.IsCancellationRequested ? "cancelled" : "connection-timeout", ConnectionIndex: connectionIndex));
                return report;
            }
            catch (Exception exception) when (exception is OneBotApiException or OneBotProtocolException or OneBotRequestTimeoutException or WebSocketException or ArgumentException)
            { report.Fail(SafeFailure(exception) with { ConnectionIndex = connectionIndex }); return report; }
            finally
            {
                session.Cancel();
                if (consuming is not null) await consuming;
            }
        }
        return report;
    }

    private static async Task ReadHistoryAsync(OneBotClient client, string accountId, string groupId, int groupIndex, int connectionIndex,
        ProbeArguments options, ProbeReport report, CancellationToken cancellationToken)
    {
        var seen = new HashSet<MessageKey>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var cutoff = options.HistoryDays > 0 ? report.StartedAt.AddDays(-options.HistoryDays) : (DateTimeOffset?)null;
        var sampling = options.HistoryDays == 0 && options.HistoryPages == 1;
        string? cursor = null;
        var pagesRead = 0;
        var uniqueInWindow = 0;
        void Finish(string reason) => report.FinishHistoryGroup(new(groupIndex, pagesRead, uniqueInWindow, reason, connectionIndex));
        try
        {
            while (pagesRead < options.HistoryPages)
            {
                // Check again before every page: an unpinned group must stop immediately.
                var pinnedGroups = await client.GetPinnedGroupIdsAsync(accountId, cancellationToken);
                if (!pinnedGroups.Contains(groupId)) { Finish("unpinned"); return; }
                IReadOnlyList<JsonElement> page;
                try { page = await client.GetGroupMessageHistoryAsync(groupId, cursor, options.HistoryCount, cancellationToken); }
                catch (Exception exception) when (exception is OneBotApiException or OneBotRequestTimeoutException)
                {
                    report.HistoryRetries++;
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                    pinnedGroups = await client.GetPinnedGroupIdsAsync(accountId, cancellationToken);
                    if (!pinnedGroups.Contains(groupId)) { Finish("unpinned"); return; }
                    page = await client.GetGroupMessageHistoryAsync(groupId, cursor, options.HistoryCount, cancellationToken);
                }
                // Parse the complete page before recording it, so an account/group mismatch cannot partially pass.
                var messages = page.Select(message => OneBotMessageParser.ParseGroupMessage(message, accountId, groupId)).ToArray();
                pagesRead++;
                if (messages.Length == 0) { Finish("empty-page"); return; }
                var priorCount = seen.Count;
                foreach (var message in messages)
                {
                    var added = seen.Add(message.Message.Key);
                    if (cutoff is null || message.Message.SentAt >= cutoff)
                    {
                        report.Record(message, history: true, connectionIndex);
                        if (added) uniqueInWindow++;
                    }
                }
                if (sampling) { Finish("sampled"); return; }
                if (cutoff is not null && messages.Any(message => message.Message.SentAt < cutoff))
                { Finish("date-boundary"); return; }
                if (seen.Count == priorCount) throw new OneBotProtocolException("history-no-progress");
                var next = OldestCursor(messages);
                if (!cursors.Add(next)) throw new OneBotProtocolException("history-no-progress");
                cursor = next;
            }
            throw new OneBotProtocolException("history-page-limit");
        }
        catch (OneBotProtocolException exception) when (exception.Code is "account-mismatch" or "pinned-state-invalid")
        { Finish(exception.Code); throw; }
        catch (Exception exception) when (exception is OneBotApiException { Action: "messagepurify_get_pinned_groups" }
            or OneBotRequestTimeoutException { Action: "messagepurify_get_pinned_groups" })
        { Finish("pinned-state-unavailable"); throw; }
        catch (Exception exception) when (exception is OneBotApiException or OneBotProtocolException or OneBotRequestTimeoutException)
        {
            var failure = SafeFailure(exception) with { GroupIndex = groupIndex, ConnectionIndex = connectionIndex };
            report.Fail(failure);
            Finish(failure.Code);
        }
        catch (OperationCanceledException) { Finish("cancelled"); throw; }
    }

    private static string OldestCursor(IReadOnlyList<ParsedOneBotMessage> messages)
    {
        var oldestTime = messages.Min(message => message.Message.SentAt);
        var oldest = messages.Where(message => message.Message.SentAt == oldestTime)
            .DistinctBy(message => message.Message.Key).ToArray();
        var selected = oldest[0];
        if (oldest.Length > 1)
        {
            var ordered = new List<(ParsedOneBotMessage Message, BigInteger Sequence)>();
            foreach (var message in oldest)
            {
                if (!BigInteger.TryParse(message.RealSequence, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
                    throw new OneBotProtocolException("history-cursor-ambiguous");
                ordered.Add((message, sequence));
            }
            var minimum = ordered.Min(item => item.Sequence);
            var first = ordered.Where(item => item.Sequence == minimum).ToArray();
            if (first.Length != 1) throw new OneBotProtocolException("history-cursor-ambiguous");
            selected = first[0].Message;
        }
        return selected.MessageSequence ?? throw new OneBotProtocolException("history-cursor-missing");
    }

    private static async Task<bool> ConsumeAsync(OneBotClient client, string accountId, int connectionIndex, ProbeReport report, CancellationTokenSource session)
    {
        try
        {
            await foreach (var packet in client.Events.ReadAllAsync(session.Token))
            {
                var hasAccount = packet.TryGetProperty("self_id", out var self);
                if (hasAccount)
                {
                    var id = self.ValueKind == JsonValueKind.String ? self.GetString() : self.GetRawText();
                    if (id != accountId) throw new OneBotProtocolException("account-mismatch");
                }
                packet.TryGetProperty("message_type", out var type);
                if (type.ValueKind == JsonValueKind.String && type.GetString() is "group" or "private" && !hasAccount)
                    throw new OneBotProtocolException("account-unknown");
                if (type.ValueKind == JsonValueKind.String && type.GetString() == "private")
                    report.PrivateEventsIgnored++;
                else if (type.ValueKind == JsonValueKind.String && type.GetString() == "group")
                {
                    var groupId = OneBotJsonId(packet, "group_id");
                    var pinnedGroups = await client.GetPinnedGroupIdsAsync(accountId, session.Token);
                    if (!pinnedGroups.Contains(groupId)) { report.UnpinnedGroupEventsIgnored++; continue; }
                    report.Record(OneBotMessageParser.ParseGroupMessage(packet, accountId), history: false, connectionIndex);
                }
                else report.OtherEvents++;
            }
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception exception)
        {
            report.Fail(SafeFailure(exception) with { ConnectionIndex = connectionIndex });
            session.Cancel();
            return false;
        }
        return true;
    }

    private static string OneBotJsonId(JsonElement packet, string name)
    {
        if (!packet.TryGetProperty(name, out var value)) throw new OneBotProtocolException("missing-field");
        if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out _)) return value.GetRawText();
        throw new OneBotProtocolException("invalid-id");
    }

    private static ProbeFailure SafeFailure(Exception exception) => exception switch
    {
        OneBotApiException api => new("api-failed", api.Action, api.Retcode),
        OneBotRequestTimeoutException timeout => new("request-timeout", timeout.Action),
        OneBotProtocolException protocol => new(protocol.Code),
        WebSocketException => new("connection-failed"),
        ArgumentException => new("invalid-configuration"),
        _ => new("probe-failed")
    };
}
