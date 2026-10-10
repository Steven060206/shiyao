using System.Net.WebSockets;
using System.Text.Json;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.QqProbe;

public static class ProbeRunner
{
    public static async Task<ProbeReport> RunAsync(ProbeArguments options, string? accessToken,
        CancellationToken cancellationToken = default)
    {
        var report = new ProbeReport();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? consuming = null;
        try
        {
            await using var client = await OneBotClient.ConnectAsync(options.Url, accessToken, cancellationToken: session.Token);
            report.TransportConnected = true;
            var status = await client.GetStatusAsync(session.Token);
            report.QqOnline = status.Online;
            if (!status.Online) { report.Fail(new("qq-offline")); return report; }
            var login = await client.GetLoginInfoAsync(session.Token);
            report.AccountVerified = true;
            report.Version = await client.GetVersionInfoAsync(session.Token);
            var groups = await client.GetGroupListAsync(session.Token);
            report.GroupCount = groups.Count;
            consuming = ConsumeAsync(client, login.AccountId, report, session);
            foreach (var group in groups.Take(options.HistoryGroups))
            {
                session.Token.ThrowIfCancellationRequested();
                report.HistoryGroupsAttempted++;
                try
                {
                    var messages = await client.GetGroupMessageHistoryAsync(group.GroupId, count: options.HistoryCount, cancellationToken: session.Token);
                    foreach (var message in messages)
                        report.Record(OneBotMessageParser.ParseGroupMessage(message, login.AccountId, group.GroupId), history: true);
                    report.HistoryGroupsSucceeded++;
                }
                catch (OneBotProtocolException exception) when (exception.Code == "account-mismatch") { throw; }
                catch (Exception exception) when (exception is OneBotApiException or OneBotProtocolException or OneBotRequestTimeoutException)
                { report.Fail(SafeFailure(exception)); }
            }
            await Task.Delay(TimeSpan.FromSeconds(options.ListenSeconds), session.Token);
            session.Cancel();
            await consuming;
            consuming = null;
        }
        catch (OperationCanceledException)
        {
            if (report.Failures.Count == 0) report.Fail(new(cancellationToken.IsCancellationRequested ? "cancelled" : "connection-timeout"));
        }
        catch (Exception exception) when (exception is OneBotApiException or OneBotProtocolException or OneBotRequestTimeoutException or WebSocketException or ArgumentException)
        { report.Fail(SafeFailure(exception)); }
        finally
        {
            session.Cancel();
            if (consuming is not null) await consuming;
        }
        return report;
    }

    private static async Task ConsumeAsync(OneBotClient client, string accountId, ProbeReport report, CancellationTokenSource session)
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
                    report.Record(OneBotMessageParser.ParseGroupMessage(packet, accountId), history: false);
                else report.OtherEvents++;
            }
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception exception)
        {
            report.Fail(SafeFailure(exception));
            session.Cancel();
        }
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
