namespace MessagePurify.Core;

public sealed record MessageKey(string AccountId, string GroupId, string MessageId);
public enum SegmentKind { Text, Image, Quote }
public sealed record MessageSegment(SegmentKind Kind, string Text,
    MessageKey? Reference = null, string? AssetId = null);
public sealed record SchoolMessage(MessageKey Key, string SenderId,
    DateTimeOffset SentAt, IReadOnlyList<MessageSegment> Segments, bool IsPrivate = false);

/// <summary>由调用者提供已确认关系；关系只在对应账号和群内有效。</summary>
public sealed record RuleContext(string AccountId, string GroupId, string TimeZoneId,
    DateTimeOffset Now, string? ConfirmedCourse = null,
    IReadOnlySet<string>? TrustedSenders = null, IReadOnlySet<string>? ExcludedSenders = null,
    bool CourseExcluded = false, IReadOnlyList<KnownTask>? KnownTasks = null);

public sealed record KnownTask(string Id, string Title, string Course, TaskTime Time,
    TaskState State, IReadOnlyList<MessageKey> Evidence, string? ConfirmedPublisherId = null);

/// <summary>描述学校通知中的行动要求，仅用于事项分类，不是软件要执行的命令。</summary>
public enum RuleAction { Submit, Complete, Attend, Register, Remind, Postpone, Cancel, NewRequirement, Inform }
public enum RuleDisposition { Task, Notice, Pending, Ignored, Change }
public sealed record RuleAssessment(string Description, bool Reliable);
public sealed record RuleAssessments(RuleAssessment Action, RuleAssessment Audience,
    RuleAssessment Course, RuleAssessment Time, RuleAssessment Source, RuleAssessment Association);
public sealed record RuleCandidate(string Title, RuleAction Action, RuleDisposition Disposition,
    string? Course, TaskTime? Time, TaskTime? SuggestedTime, string? TargetTaskId,
    int? Priority, RuleAssessments Assessments, IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<MessageKey> Evidence);
public sealed record RuleExtractionResult(string RuleVersion, IReadOnlyList<RuleCandidate> Candidates);
