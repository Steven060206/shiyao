using System.Globalization;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Application;

/// <summary>同一账号的收件、规则路由、界面操作及快照使用同一串行入口。</summary>
public sealed class AccountWorkspace
{
    private readonly object gate = new();
    private readonly AccountMessageStore messages;
    private readonly AccountTaskStore tasks;
    private readonly AccountCandidateRouter router;
    private readonly LocalMessageProcessor processor;
    private readonly string settingsPath;
    private WorkspaceSettings settings;
    private Dictionary<string, string> availableGroups = new(StringComparer.Ordinal);
    private HashSet<string> pinnedGroups = new(StringComparer.Ordinal);
    public string AccountId { get; }

    public AccountWorkspace(string dataRoot, string accountId)
    {
        messages = new(dataRoot, accountId);
        tasks = new(dataRoot, accountId);
        router = new(messages, tasks);
        processor = new(messages);
        AccountId = accountId;
        settingsPath = Path.Combine(Path.GetDirectoryName(tasks.DatabasePath)!, "workspace-settings.json");
        settings = ReadSettings();
    }

    public void SetAvailableGroups(IReadOnlyList<OneBotGroup> groups, IReadOnlySet<string> pinnedGroupIds)
    {
        lock (gate)
        {
            ArgumentNullException.ThrowIfNull(groups);
            ArgumentNullException.ThrowIfNull(pinnedGroupIds);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(group.GroupId);
                names.Add(group.GroupId, string.IsNullOrWhiteSpace(group.GroupName) ? group.GroupId : group.GroupName);
            }
            availableGroups = names;
            pinnedGroups = new(pinnedGroupIds, StringComparer.Ordinal);
        }
    }

    public bool Enqueue(SchoolMessage message, IReadOnlySet<string> pinnedGroupIds,
        MessageOrigin origin, string batchId, DateTimeOffset now)
    {
        lock (gate) return EnqueueCore(message, pinnedGroupIds, origin, batchId, now);
    }

    public bool Ingest(SchoolMessage message, IReadOnlySet<string> pinnedGroupIds,
        MessageOrigin origin, string batchId, DateTimeOffset now)
    {
        lock (gate)
        {
            var added = EnqueueCore(message, pinnedGroupIds, origin, batchId, now);
            RouteGroup(message.Key.GroupId, pinnedGroupIds, now);
            return added;
        }
    }

    public int ProcessPending(IReadOnlySet<string> pinnedGroupIds, DateTimeOffset now)
    {
        lock (gate)
        {
            ArgumentNullException.ThrowIfNull(pinnedGroupIds);
            return pinnedGroupIds.Sum(groupId => RouteGroup(groupId, pinnedGroupIds, now));
        }
    }

    private bool EnqueueCore(SchoolMessage message, IReadOnlySet<string> pinnedGroupIds,
        MessageOrigin origin, string batchId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(pinnedGroupIds);
        if (message.Key.AccountId != AccountId)
            throw new ArgumentException("消息账号不属于当前工作区。", nameof(message));
        var context = Context(message.Key.GroupId, now);
        if (context.CourseExcluded) return false;
        return processor.Ingest(message, pinnedGroupIds, context, origin, batchId, now);
    }

    private int RouteGroup(string groupId, IReadOnlySet<string> pinnedGroupIds, DateTimeOffset now)
    {
        var count = 0;
        int processed;
        do
        {
            processed = router.ProcessAndRoutePending(Context(groupId, now), pinnedGroupIds, 1000);
            count += processed;
        } while (processed == 1000);
        return count;
    }

    private RuleContext Context(string groupId, DateTimeOffset now)
    {
        settings.Relationships.TryGetValue(groupId, out var relationship);
        return new(AccountId, groupId, "Asia/Shanghai", now, relationship?.Course,
            CourseExcluded: relationship?.Excluded == true);
    }

    public ApplicationSnapshot Snapshot(DateTimeOffset now)
    {
        lock (gate)
        {
            var taskList = tasks.ListTasks();
            var items = new List<ApplicationItem>();
            foreach (var task in taskList)
                items.Add(Item(task.Id, "task", task.Title, task.Course, task.State.ToString().ToLowerInvariant(),
                    task.Time, task.Version, task.Evidence, task.Action, now,
                    undoId: task.UndoOperationId, undoExpires: task.UndoExpiresAt));
            foreach (var review in tasks.ListReviews().Where(review => review.State is ReviewState.Pending or ReviewState.Ignored))
            {
                var candidate = review.Candidate;
                var target = taskList.FirstOrDefault(task => task.Id == candidate.TargetTaskId);
                var reason = AcceptanceProblem(candidate, target is not null);
                items.Add(Item(review.Id, "review", candidate.Title, candidate.Course,
                    review.State.ToString().ToLowerInvariant(), candidate.Time, review.Version, candidate.Evidence,
                    candidate.Action, now, candidate.TargetTaskId, target?.Version, review.UndoOperationId,
                    review.UndoExpiresAt, reason is null ? candidate.ReviewReasons :
                        candidate.ReviewReasons.Append(reason).Distinct().ToArray(), candidate.SuggestedTime,
                    review.State == ReviewState.Pending && reason is null));
            }
            foreach (var notice in router.ListNotices())
                items.Add(Item(notice.Id, "notice", notice.Candidate.Title, notice.Candidate.Course,
                    "active", notice.Candidate.Time, notice.ProcessingVersion, notice.Candidate.Evidence,
                    notice.Candidate.Action, now, reasons: notice.Candidate.ReviewReasons,
                    suggested: notice.Candidate.SuggestedTime));
            var groups = availableGroups.Keys.Union(settings.Relationships.Keys, StringComparer.Ordinal)
                .Select(id =>
                {
                    settings.Relationships.TryGetValue(id, out var relationship);
                    var name = GroupName(id);
                    return new ApplicationGroup(id, name, name, relationship?.Course,
                        relationship?.Excluded == true, pinnedGroups.Contains(id));
                }).OrderBy(group => group.Name, StringComparer.Ordinal).ToArray();
            return new(AccountId, items, groups, now);
        }
    }

    private ApplicationItem Item(string id, string kind, string title, string? course, string state,
        TaskTime? time, long version, IReadOnlyList<MessageKey> evidenceKeys, RuleAction action,
        DateTimeOffset now, string? target = null, long? targetVersion = null, string? undoId = null,
        DateTimeOffset? undoExpires = null, IReadOnlyList<string>? reasons = null,
        TaskTime? suggested = null, bool canAccept = false)
    {
        var sources = evidenceKeys.Distinct().Select(key => (Key: key, Stored: messages.Find(key))).ToArray();
        var evidence = sources.Select(source =>
        {
            var message = source.Stored?.Message;
            return new ApplicationEvidence(source.Key.GroupId, GroupName(source.Key.GroupId), message?.SenderId ?? "",
                message?.SentAt, message is null ? "原消息暂不可用" : string.Join("\n",
                    message.Segments.Where(segment => segment.Kind != SegmentKind.Image).Select(segment => segment.Text)),
                message?.Segments.Count(segment => segment.Kind == SegmentKind.Image) ?? 0);
        }).ToArray();
        // 每个候选的完整行动正文保留后果及否定；P0 数值饱和也不丢失明确后果。
        var clearConsequence = sources.Any(source =>
            source.Stored?.Extraction?.Candidates.Any(candidate => (candidate.Title == title || candidate.TargetTaskId == id) &&
                PriorityCalculator.HasClearConsequence(candidate.Title)) == true);
        return new(id, kind, title, course, string.Join("、", evidence.Select(item => item.GroupName).Distinct()),
            state, FormatDate(time), FormatTime(time), (time?.Kind ?? suggested?.Kind ?? Core.TimeKind.Deadline)
                .ToString().ToLowerInvariant(), time is null ? null : PriorityCalculator.Calculate(time, now, clearConsequence),
            version, target, targetVersion, undoId, undoExpires, reasons ?? [], FormatDate(suggested),
            FormatTime(suggested), evidence, !settings.ReadVersions.TryGetValue(ReadKey(kind, id), out var readVersion)
                || readVersion < version, action.ToString().ToLowerInvariant(), canAccept);
    }

    public void Execute(WorkspaceCommand command, DateTimeOffset now)
    {
        lock (gate)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (command.AccountId != AccountId) throw new ArgumentException("操作账号不属于当前工作区。", nameof(command));
            ArgumentException.ThrowIfNullOrWhiteSpace(command.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.OperationId);
            switch (command.Operation)
            {
                case "complete": tasks.Complete(command.Id, command.OperationId, command.Version, now); break;
                case "undo-complete":
                    ArgumentException.ThrowIfNullOrWhiteSpace(command.UndoOperationId);
                    tasks.UndoComplete(command.Id, command.OperationId, command.Version, now, command.UndoOperationId); break;
                case "archive": tasks.Archive(command.Id, command.OperationId, command.Version, now); break;
                case "restore": tasks.RestoreArchived(command.Id, command.OperationId, command.Version, now); break;
                case "ignore": tasks.IgnoreReview(command.Id, command.OperationId, command.Version, now); break;
                case "undo-ignore":
                    ArgumentException.ThrowIfNullOrWhiteSpace(command.UndoOperationId);
                    tasks.UndoIgnored(command.Id, command.OperationId, command.Version, now, command.UndoOperationId); break;
                case "reject": tasks.RejectReview(command.Id, command.OperationId, command.Version, now); break;
                case "accept-time":
                    tasks.AcceptTime(command.Id, command.OperationId, command.Version,
                        ReadTime(command, tasks.FindTask(command.Id)?.Time.Kind) ?? throw new ArgumentException("须确认正式日期。"), now); break;
                case "accept-review":
                    var review = tasks.FindReview(command.Id) ?? throw new KeyNotFoundException("待确认事项不存在。");
                    var candidate = review.Candidate;
                    var problem = AcceptanceProblem(candidate, candidate.TargetTaskId is not null && tasks.FindTask(candidate.TargetTaskId) is not null);
                    if (problem is not null) throw new ArgumentException(problem);
                    tasks.AcceptReview(command.Id, candidate.TargetTaskId ?? "accepted-" + command.Id,
                        command.OperationId, command.Version, ReadTime(command, candidate.Time?.Kind ?? candidate.SuggestedTime?.Kind),
                        now, command.TargetTaskVersion); break;
                case "confirm-group": ConfirmGroup(command); break;
                case "mark-read":
                    var item = Snapshot(now).Items.FirstOrDefault(item => item.Id == command.Id)
                        ?? throw new KeyNotFoundException("事项不存在。");
                    if (item.Version != command.Version) throw new InvalidOperationException("事项已变化，请重新查看。 ");
                    SaveSettings(settings with
                    {
                        ReadVersions = new(settings.ReadVersions, StringComparer.Ordinal)
                            { [ReadKey(item.Kind, item.Id)] = item.Version }
                    }); break;
                default: throw new ArgumentException("不支持的工作区操作。", nameof(command));
            }
        }
    }

    private void ConfirmGroup(WorkspaceCommand command)
    {
        if (!availableGroups.ContainsKey(command.Id) && !settings.Relationships.ContainsKey(command.Id))
            throw new ArgumentException("群不属于当前可用群列表。");
        SaveSettings(settings with
        {
            Relationships = new(settings.Relationships, StringComparer.Ordinal)
            {
                [command.Id] = new(GroupName(command.Id), string.IsNullOrWhiteSpace(command.Course) ? null : command.Course.Trim(), command.Excluded)
            }
        });
    }

    private static string? AcceptanceProblem(RuleCandidate candidate, bool targetExists)
    {
        if (candidate.Action == RuleAction.Inform) return "此项需要确认为通知，当前事项入口尚不支持。";
        if (candidate.Action is RuleAction.Cancel or RuleAction.Postpone or RuleAction.NewRequirement && candidate.TargetTaskId is null)
            return "关联变更缺少目标事项，需先核对关联。";
        if (candidate.TargetTaskId is not null && !targetExists) return "关联目标事项不存在，需先核对关联。";
        return null;
    }

    private static TaskTime? ReadTime(WorkspaceCommand command, Core.TimeKind? fallback)
    {
        if (command.Date is null)
        {
            if (command.Time is not null || command.TimeKind is not null) throw new ArgumentException("填写时间类型或钟点时须同时提供日期。");
            return null;
        }
        if (!DateOnly.TryParseExact(command.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("日期须为有效的 yyyy-MM-dd。");
        TimeOnly? time = null;
        if (!string.IsNullOrEmpty(command.Time))
        {
            if (!TimeOnly.TryParseExact(command.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
                throw new ArgumentException("钟点须为有效的 HH:mm。");
            time = clock;
        }
        var kind = command.TimeKind switch
        {
            null => fallback ?? Core.TimeKind.Deadline,
            "deadline" => Core.TimeKind.Deadline,
            "start" => Core.TimeKind.Start,
            _ => throw new ArgumentException("时间类型须为 deadline 或 start。")
        };
        return new(kind, date, time, "Asia/Shanghai");
    }

    private string GroupName(string id) => availableGroups.GetValueOrDefault(id)
        ?? settings.Relationships.GetValueOrDefault(id)?.Name ?? id;
    private static string ReadKey(string kind, string id) => kind + ":" + id;
    private static string? FormatDate(TaskTime? time) => time?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? FormatTime(TaskTime? time) => time?.Time?.ToString("HH:mm", CultureInfo.InvariantCulture);
    private sealed record GroupRelationship(string Name, string? Course, bool Excluded);
    private sealed record WorkspaceSettings(string AccountId, Dictionary<string, GroupRelationship> Relationships,
        Dictionary<string, long> ReadVersions);

    private WorkspaceSettings ReadSettings()
    {
        if (!File.Exists(settingsPath)) return new(AccountId, new(StringComparer.Ordinal), new(StringComparer.Ordinal));
        try
        {
            var loaded = JsonSerializer.Deserialize<WorkspaceSettings>(File.ReadAllText(settingsPath));
            if (loaded is null || loaded.AccountId != AccountId || loaded.Relationships is null || loaded.ReadVersions is null)
                throw new InvalidDataException("工作区设置账号归属或内容无效，保留原文件。 ");
            return loaded;
        }
        catch (JsonException error) { throw new InvalidDataException("工作区设置格式无效，保留原文件。", error); }
    }

    private void SaveSettings(WorkspaceSettings next)
    {
        var temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, next);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, settingsPath, overwrite: true);
            settings = next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
