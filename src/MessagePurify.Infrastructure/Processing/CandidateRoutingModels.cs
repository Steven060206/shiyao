using MessagePurify.Core;
using MessagePurify.Infrastructure.Storage;

namespace MessagePurify.Infrastructure.Processing;

/// <summary>普通通知只保存在本地列表；此记录不代表系统通知已经投递。</summary>
public sealed record StoredNotice(string Id, string AccountId, RuleCandidate Candidate,
    MessageKey Source, long ProcessingVersion, MessageOrigin Origin, string BatchId,
    DateTimeOffset SentAt, DateTimeOffset CreatedAt);
