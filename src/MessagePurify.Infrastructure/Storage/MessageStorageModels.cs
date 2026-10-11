using MessagePurify.Core;

namespace MessagePurify.Infrastructure.Storage;

public enum MessageOrigin { Realtime, History }

/// <summary>持久化消息及其规则候选；候选不代表已创建正式事项。</summary>
public sealed record StoredMessage(SchoolMessage Message, MessageOrigin Origin,
    string BatchId, DateTimeOffset ReceivedAt, long ProcessingVersion,
    RuleExtractionResult? Extraction, DateTimeOffset? ProcessedAt);
