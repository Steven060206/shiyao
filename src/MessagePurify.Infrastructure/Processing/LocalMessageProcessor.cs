using MessagePurify.Core;
using MessagePurify.Infrastructure.Storage;

namespace MessagePurify.Infrastructure.Processing;

/// <summary>先保存允许接收的消息，再将现有本地规则产生的候选持久化；不创建正式事项或通知。</summary>
public sealed class LocalMessageProcessor(AccountMessageStore store)
{
    private readonly AccountMessageStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public bool Ingest(SchoolMessage message, IReadOnlySet<string> pinnedGroupIds,
        RuleContext context, MessageOrigin origin, string batchId, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(pinnedGroupIds);
        ValidateContext(context);
        if (message.Key.AccountId != context.AccountId || message.Key.GroupId != context.GroupId)
            throw new ArgumentException("消息与规则上下文的账号或群不一致。", nameof(context));

        if (message.IsPrivate || !pinnedGroupIds.Contains(message.Key.GroupId) ||
            context.ExcludedSenders?.Contains(message.SenderId) == true)
            return false;

        return _store.TryEnqueue(message, origin, batchId, receivedAt);
    }

    public int ProcessPending(RuleContext context, int limit = 100)
    {
        ValidateContext(context);
        _ = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZoneId);
        var processed = 0;
        foreach (var pending in _store.GetPending(limit, context.GroupId))
        {
            var extraction = MessageRuleExtractor.Extract(pending.Message, context);
            if (_store.TrySaveExtraction(pending.Message.Key, pending.ProcessingVersion, extraction, context.Now))
                processed++;
        }
        return processed;
    }

    private void ValidateContext(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.AccountId != _store.AccountId)
            throw new ArgumentException("规则上下文不属于当前账号的消息存储。", nameof(context));
        if (string.IsNullOrWhiteSpace(context.GroupId))
            throw new ArgumentException("规则上下文必须指定当前群。", nameof(context));
    }
}
