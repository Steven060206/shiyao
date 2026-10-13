using System.Globalization;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Reminders;
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
    private readonly AccountReminderService reminders;
    private IReminderSink? reminderSink;
    private readonly string settingsPath;
    private WorkspaceSettings settings;
    private string? cacheCleanupError;
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
        reminders = new(dataRoot, accountId, tasks);
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

    public int ProcessPending(IReadOnlySet<string> pinnedGroupIds, DateTimeOffset now, Func<bool>? canContinue = null)
    {
        lock (gate)
        {
            ArgumentNullException.ThrowIfNull(pinnedGroupIds);
            var count = 0;
            foreach (var groupId in pinnedGroupIds)
            {
                if (canContinue?.Invoke() == false) break;
                count += RouteGroup(groupId, pinnedGroupIds, now, canContinue);
            }
            return count;
        }
    }

    public CacheCleanupResult CleanMessageCache(DateTimeOffset now, Func<bool>? canContinue = null)
    {
        lock (gate)
        {
            try
            {
                var result = router.CleanMessageCache(now, canContinue);
                cacheCleanupError = null;
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                cacheCleanupError = "缓存清理失败，临时消息与上次成功结果已保留。";
                throw;
            }
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
        if (router.HasRouted(message.Key)) return false;
        return processor.Ingest(message, pinnedGroupIds, context, origin, batchId, now);
    }

    private int RouteGroup(string groupId, IReadOnlySet<string> pinnedGroupIds, DateTimeOffset now, Func<bool>? canContinue = null)
    {
        var count = 0;
        while (canContinue?.Invoke() != false)
        {
            var processed = router.ProcessAndRoutePending(Context(groupId, now), pinnedGroupIds, 1000, canContinue);
            count += processed;
            if (processed < 1000) break;
        }
        if (canContinue?.Invoke() != false) RefreshReminders(now);
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
            var taskList = tasks.ListTasks(includeMerged: true);
            var taskById = taskList.ToDictionary(task => task.Id, StringComparer.Ordinal);
            string CurrentTaskId(string id)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (taskById.TryGetValue(id, out var current) && current.MergedIntoId is { } next && visited.Add(id)) id = next;
                return id;
            }
            var reviews = tasks.ListReviews();
            var history = tasks.ListTaskHistory().GroupBy(entry => entry.After.Id)
                .ToDictionary(group => group.Key, group => group.Select(TaskChangeProjection.Build)
                    .OfType<TaskChangeProjection.Change>().ToArray(), StringComparer.Ordinal);
            var items = new List<ApplicationItem>();
            foreach (var task in taskList)
            {
                var ownChanges = history.GetValueOrDefault(task.Id) ?? [];
                var changes = ownChanges.Concat(taskList.Where(source => source.MergedIntoId is not null && CurrentTaskId(source.Id) == task.Id)
                    .SelectMany(source => history.GetValueOrDefault(source.Id) ?? [])).OrderBy(change => change.Entry.At).ToArray();
                var important = ownChanges.LastOrDefault(change => change.Important);
                var pending = reviews.Where(review => review.State == ReviewState.Pending && review.Candidate.TargetTaskId is { } targetId && CurrentTaskId(targetId) == task.Id)
                    .Select(review => new ApplicationPendingChange(review.Id, TaskChangeProjection.SuggestionLabel(review.Candidate.Action),
                        FormatDate(review.Candidate.Time ?? review.Candidate.SuggestedTime),
                        FormatTime(review.Candidate.Time ?? review.Candidate.SuggestedTime),
                        (review.Candidate.Time?.Kind ?? review.Candidate.SuggestedTime?.Kind ?? task.Time.Kind).ToString().ToLowerInvariant(),
                        review.Candidate.ReviewReasons)).ToArray();
                items.Add(Item(task.Id, "task", task.Title, task.Course, task.MergedIntoId is null ? task.State.ToString().ToLowerInvariant() : "merged",
                    task.Time, task.Version, task.Evidence, task.Action, now,
                    undoId: task.UndoOperationId, undoExpires: task.UndoExpiresAt, priorityOverride: task.PriorityOverride,
                    unreadVersion: important?.Entry.After.Version ?? task.Version,
                    changeLabel: important?.Badge, changes: changes.Reverse().Select(change => new ApplicationChange(
                        change.Label, change.Entry.At, change.Entry.After.Version, change.Fields, Evidence(change.Entry.Evidence))).ToArray(),
                    pendingChanges: pending, source: task.Source) with
                    { MergedIntoId = task.MergedIntoId is null ? null : CurrentTaskId(task.Id), RelatedItemIds = task.RelatedItemIds });
            }
            foreach (var review in reviews.Where(review => review.State is ReviewState.Pending or ReviewState.Ignored || review.SplitParentTaskId is not null))
            {
                var candidate = review.Candidate;
                var targetId = candidate.TargetTaskId ?? (review.State == ReviewState.Accepted ? review.AcceptedTaskId : null);
                var target = targetId is null ? null : taskList.FirstOrDefault(task => task.Id == CurrentTaskId(targetId));
                var reason = AcceptanceProblem(candidate, target is not null, review.SplitParentTaskId is not null);
                items.Add(Item(review.Id, "review", candidate.Title, candidate.Course,
                    review.State.ToString().ToLowerInvariant(), candidate.Time, review.Version, candidate.Evidence,
                    candidate.Action, now, target?.Id ?? targetId, target?.Version, review.UndoOperationId,
                    review.UndoExpiresAt, reason is null ? candidate.ReviewReasons :
                        candidate.ReviewReasons.Append(reason).Distinct().ToArray(), candidate.SuggestedTime,
                    review.State == ReviewState.Pending && reason is null) with
                    { RelatedItemIds = review.SplitParentTaskId is { } parentId ? [CurrentTaskId(parentId)] : null,
                      TimeKind = (candidate.Time?.Kind ?? candidate.SuggestedTime?.Kind ?? review.SplitTimeKind ?? Core.TimeKind.Deadline).ToString().ToLowerInvariant() });
            }
            foreach (var notice in router.ListNotices())
            {
                var convertedId = CurrentTaskId("notice-task-" + notice.Id);
                var converted = taskList.FirstOrDefault(task => task.Id == convertedId);
                items.Add(Item(notice.Id, "notice", notice.Candidate.Title, notice.Candidate.Course,
                    converted is null ? "active" : "converted", notice.Candidate.Time, notice.ProcessingVersion,
                    notice.Candidate.Evidence, notice.Candidate.Action, now, converted?.Id, converted?.Version,
                    reasons: notice.Candidate.ReviewReasons, suggested: notice.Candidate.SuggestedTime));
            }
            var groups = availableGroups.Keys.Union(settings.Relationships.Keys, StringComparer.Ordinal)
                .Select(id =>
                {
                    settings.Relationships.TryGetValue(id, out var relationship);
                    var name = GroupName(id);
                    return new ApplicationGroup(id, name, name, relationship?.Course,
                        relationship?.Excluded == true, pinnedGroups.Contains(id));
                }).OrderBy(group => group.Name, StringComparer.Ordinal).ToArray();
            var mergeGroups = groups.Select(group => new CrossGroupMergeGroup(group.GroupId, group.Name,
                group.Course, group.Excluded)).ToArray();
            foreach (var match in CrossGroupMergeFinder.Find(taskList, reviews, mergeGroups, messages.Find))
            {
                var first = taskById[match.FirstTaskId];
                var second = taskById[match.SecondTaskId];
                // 任一方版本递增均使建议重新未读，不用较大版本掩盖另一方修改。
                var version = checked(match.FirstTaskVersion + match.SecondTaskVersion);
                items.Add(Item(match.Id, "merge-review", "可能重复：" + first.Title, first.Course, "pending",
                    null, version, first.Evidence.Concat(second.Evidence).Distinct().ToArray(),
                    first.Action, now, reasons: match.Reasons) with
                {
                    MergeSuggestion = new(match.FirstTaskId, match.FirstTaskVersion,
                        match.SecondTaskId, match.SecondTaskVersion)
                });
            }
            return new(AccountId, items, groups, now, reminders.Synchronize(now),
                router.ReadCacheCleanupResult(), cacheCleanupError);
        }
    }

    private ApplicationItem Item(string id, string kind, string title, string? course, string state,
        TaskTime? time, long version, IReadOnlyList<MessageKey> evidenceKeys, RuleAction action,
        DateTimeOffset now, string? target = null, long? targetVersion = null, string? undoId = null,
        DateTimeOffset? undoExpires = null, IReadOnlyList<string>? reasons = null,
        TaskTime? suggested = null, bool canAccept = false, int? priorityOverride = null,
        long? unreadVersion = null, string? changeLabel = null, IReadOnlyList<ApplicationChange>? changes = null,
        IReadOnlyList<ApplicationPendingChange>? pendingChanges = null, string source = "qq")
    {
        var sources = evidenceKeys.Distinct().Select(key => (Key: key, Stored: messages.Find(key))).ToArray();
        var evidence = Evidence(evidenceKeys);
        var unread = !settings.ReadVersions.TryGetValue(ReadKey(kind, id), out var readVersion)
            || readVersion < (unreadVersion ?? version);
        // 每个候选的完整行动正文保留后果及否定；P0 数值饱和也不丢失明确后果。
        var clearConsequence = source == "manual" && PriorityCalculator.HasClearConsequence(title) || sources.Any(messageSource =>
            messageSource.Stored?.Extraction?.Candidates.Any(candidate => (candidate.Title == title || candidate.TargetTaskId == id) &&
                PriorityCalculator.HasClearConsequence(candidate.Title)) == true);
        return new(id, kind, title, course, source == "manual" ? "手动添加" : string.Join("、", evidence.Select(item => item.GroupName).Distinct()),
            state, FormatDate(time), FormatTime(time), (time?.Kind ?? suggested?.Kind ?? Core.TimeKind.Deadline)
                .ToString().ToLowerInvariant(), priorityOverride ?? (time is null ? null : PriorityCalculator.Calculate(time, now, clearConsequence)),
            version, target, targetVersion, undoId, undoExpires, reasons ?? [], FormatDate(suggested),
            FormatTime(suggested), evidence, unread, action.ToString().ToLowerInvariant(), canAccept,
            pendingChanges?.Count > 0 ? "待确认变更" : unread ? changeLabel : null, changes, pendingChanges, source);
    }

    private ApplicationEvidence[] Evidence(IReadOnlyList<MessageKey> keys) => keys.Distinct().Select(key =>
        {
            var message = messages.Find(key)?.Message;
            return new ApplicationEvidence(key.GroupId, GroupName(key.GroupId), message?.SenderId ?? "",
                message?.SentAt, message is null ? "原消息暂不可用" : string.Join("\n",
                    message.Segments.Where(segment => segment.Kind != SegmentKind.Image).Select(segment => segment.Text)),
                message?.Segments.Count(segment => segment.Kind == SegmentKind.Image) ?? 0);
        }).ToArray();

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
                case "clean-cache":
                    if (command.Id != "cache" || command.Version != 0)
                        throw new ArgumentException("缓存清理须使用 cache 标识及版本 0。", nameof(command));
                    CleanMessageCache(now);
                    return;
                case "create-task":
                    if (command.Version != 0) throw new ArgumentException("新增任务版本须为 0。", nameof(command));
                    ArgumentException.ThrowIfNullOrWhiteSpace(command.Title);
                    ArgumentException.ThrowIfNullOrWhiteSpace(command.Course);
                    ArgumentException.ThrowIfNullOrWhiteSpace(command.TimeKind);
                    tasks.CreateManualTask(command.Id, command.Title, command.Course,
                        ReadTime(command, null) ?? throw new ArgumentException("新增任务前须填写正式日期。"),
                        command.Priority, command.OperationId, now); break;
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
                case "edit-task":
                    ArgumentException.ThrowIfNullOrWhiteSpace(command.Title);
                    ArgumentException.ThrowIfNullOrWhiteSpace(command.TimeKind);
                    tasks.EditTask(command.Id, command.OperationId, command.Version, command.Title, command.Course,
                        ReadTime(command, null) ?? throw new ArgumentException("保存任务前须填写正式日期。"),
                        command.Priority, now); break;
                case "convert-notice": ConvertNotice(command, now); break;
                case "merge-tasks":
                    ArgumentException.ThrowIfNullOrWhiteSpace(command.MergeWithId);
                    tasks.MergeTasks(command.Id, command.MergeWithId, command.OperationId, command.Version,
                        command.MergeWithVersion ?? throw new ArgumentException("须核对另一事项版本。"),
                        command.MergeSelection ?? throw new ArgumentException("须选择合并字段和状态。"), now,
                        command.Priority ?? throw new ArgumentException("须确认要保留的优先级。")); break;
                case "keep-separate":
                    ArgumentException.ThrowIfNullOrWhiteSpace(command.MergeWithId);
                    tasks.KeepSeparate(command.Id, command.MergeWithId, command.OperationId, command.Version,
                        command.MergeWithVersion ?? throw new ArgumentException("须核对另一事项版本。"), now); break;
                case "split-task": SplitTask(command, now); break;
                case "accept-review":
                    var review = tasks.FindReview(command.Id) ?? throw new KeyNotFoundException("待确认事项不存在。");
                    var candidate = review.Candidate;
                    var problem = AcceptanceProblem(candidate, candidate.TargetTaskId is not null && tasks.FindTask(candidate.TargetTaskId) is not null,
                        review.SplitParentTaskId is not null);
                    if (problem is not null) throw new ArgumentException(problem);
                    tasks.AcceptReview(command.Id, candidate.TargetTaskId ?? "accepted-" + command.Id,
                        command.OperationId, command.Version, ReadTime(command, candidate.Time?.Kind ?? candidate.SuggestedTime?.Kind ?? review.SplitTimeKind),
                        now, command.TargetTaskVersion); break;
                case "confirm-group": ConfirmGroup(command); break;
                case "set-reminder":
                    reminders.Configure(command.Id, command.Version,
                        command.ReminderVersion ?? throw new ArgumentException("须提供提醒计划版本。"),
                        new(command.ReminderMode ?? throw new ArgumentException("须选择提醒方式。"),
                            command.ReminderHours, command.ReminderDays, command.ReminderClock),
                        command.OperationId, now); break;
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
            RefreshReminders(now);
        }
    }

    /// <summary>投递端与正式状态变更共用工作区锁；提交先发生时，后续操作清理旧条目。</summary>
    public IReadOnlyList<ReminderDelivery> DispatchReminders(IReminderSink sink, DateTimeOffset now,
        bool recovering = false)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (gate)
        {
            reminderSink = sink;
            return reminders.DispatchDue(now, sink, recovering);
        }
    }

    public IReadOnlyList<ReminderDelivery> ReminderDeliveries()
    {
        lock (gate) return reminders.Deliveries();
    }

    private void RefreshReminders(DateTimeOffset now)
    {
        reminders.Synchronize(now);
        if (reminderSink is not null) reminders.ReconcileNotifications(reminderSink, now);
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

    private void ConvertNotice(WorkspaceCommand command, DateTimeOffset now)
    {
        var notice = router.FindNotice(command.Id) ?? throw new KeyNotFoundException("通知不存在。");
        if (notice.AccountId != AccountId) throw new ArgumentException("通知账号不属于当前工作区。", nameof(command));
        if (notice.ProcessingVersion != command.Version)
            throw new InvalidOperationException("通知已有变化，请重新读取后再转换。");
        var time = ReadTime(command, notice.Candidate.Time?.Kind ?? notice.Candidate.SuggestedTime?.Kind)
            ?? throw new ArgumentException("加入任务前须填写正式日期。", nameof(command));
        tasks.CreateTaskFromNotice("notice-task-" + notice.Id, notice.Id, notice.Candidate,
            time, PriorityCalculator.Calculate(time, now, false),
            command.OperationId, now);
    }

    private void SplitTask(WorkspaceCommand command, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.NewItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Title);
        var parent = tasks.FindTask(command.Id) ?? throw new KeyNotFoundException("原事项不存在。");
        var indexes = command.EvidenceIndexes ?? throw new ArgumentException("须核对拆分证据。");
        if (indexes.Any(index => index < 0 || index >= parent.Evidence.Count) || indexes.Distinct().Count() != indexes.Count)
            throw new ArgumentException("证据选择已变化，请核对原事项。");
        var state = command.State switch
        {
            "active" => TaskState.Active, "completed" => TaskState.Completed,
            "cancelled" => TaskState.Cancelled, "archived" => TaskState.Archived,
            _ => throw new ArgumentException("须选择拆出事项的状态。")
        };
        var kind = command.TimeKind switch
        {
            "deadline" => Core.TimeKind.Deadline, "start" => Core.TimeKind.Start,
            _ => throw new ArgumentException("须选择截止或开始时间。")
        };
        var hasDate = !string.IsNullOrEmpty(command.Date);
        if (!hasDate && !string.IsNullOrEmpty(command.Time)) throw new ArgumentException("填写钟点时须同时提供日期。");
        var time = hasDate ? ReadTime(command, kind) : null;
        tasks.SplitTask(command.Id, command.NewItemId, command.OperationId, command.Version,
            new(command.Title, command.Course, time, command.Priority, state,
                indexes.Select(index => parent.Evidence[index]).ToArray(), kind), now);
    }

    private static string? AcceptanceProblem(RuleCandidate candidate, bool targetExists, bool userSplit = false)
    {
        if (candidate.Action == RuleAction.Inform) return "此项需要确认为通知，当前事项入口尚不支持。";
        if (!userSplit && candidate.Action is RuleAction.Cancel or RuleAction.Postpone or RuleAction.NewRequirement && candidate.TargetTaskId is null)
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
