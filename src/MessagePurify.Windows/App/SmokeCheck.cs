using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using MessagePurify.Windows.IntegrationProbe;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MessagePurify.Windows.App;

/// <summary>仅验证模式使用合成账号，正式启动绝不载入样例。</summary>
internal static partial class SmokeCheck
{
    public static AccountWorkspace CreateWorkspace(string dataRoot)
    {
        var workspace = new AccountWorkspace(dataRoot, "10000");
        var now = DateTimeOffset.UtcNow;
        HashSet<string> pinned = ["10001"];
        workspace.SetAvailableGroups([new OneBotGroup("10001", "合成课程群")], pinned);
        workspace.Execute(new("10000", "confirm-group", "10001", "setup-course", 0, Course: "数学"), now);
        var due = now.ToOffset(TimeSpan.FromHours(8)).AddDays(2);
        workspace.Ingest(new(new("10000", "10001", "1"), "10002", now,
            [new(SegmentKind.Text, $"{due.Month}月{due.Day}日之前提交数学作业")]), pinned, MessageOrigin.Realtime, "synthetic", now);
        workspace.Ingest(new(new("10000", "10001", "2"), "10002", now.AddSeconds(1),
            [new(SegmentKind.Text, "请提交数学报告")]), pinned, MessageOrigin.Realtime, "synthetic", now);
        return workspace;
    }

    public static async Task RunAsync(AppWindow window, AccountWorkspace workspace, AppRuntime runtime,
        DesktopCardHost desktop, string dataRoot, string reportPath)
    {
        var initial = workspace.Snapshot(DateTimeOffset.UtcNow);
        var task = initial.Items.Single(i => i.Kind == "task");
        var review = initial.Items.Single(i => i.Kind == "review" && i.State == "pending");
        await WaitAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]') !== null");
        await WaitAsync(window, "document.title === '拾要' && document.querySelector('img.brand-mark')?.complete && document.querySelector('img.brand-mark').naturalWidth > 0");
        if (window.Title != "拾要" || window.Icon is null)
            throw new InvalidOperationException("拾要窗口名称或图标未加载。");
        await desktop.RefreshAsync();
        if (desktop.State != "attached" || desktop.Snapshot?.Tasks.Count != 1 || desktop.Snapshot.Pending.Count != 1)
            throw new InvalidOperationException("正式桌面未挂载或没有显示合成事项。");
        if (desktop.TaskWindow.Title != "拾要 · 提醒事项" || desktop.PendingWindow.Title != "拾要 · 待确认")
            throw new InvalidOperationException("拾要桌面卡片名称未更新。");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
        await WaitAsync(window, "document.querySelector('#theme-setting') !== null");
        await EvaluateAsync(window, "document.querySelector('#theme-setting').value='dark';document.querySelector('#theme-setting').dispatchEvent(new Event('change',{bubbles:true}))");
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.TaskWindow.Background is SolidColorBrush brush && brush.Color == Colors.Black);
        var reportFile = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);
        var cardsCapture = Path.Combine(Path.GetDirectoryName(reportFile)!, Path.GetFileNameWithoutExtension(reportFile) + "-cards.png");
        desktop.Capture(cardsCapture);
        FindButton(desktop.PendingWindow, "open:" + review.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('#review-form') !== null");
        FindButton(desktop.TaskWindow, "open:" + task.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('[data-action=complete]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]').click()");
        await WaitAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]') !== null");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]').click()");
        await WaitAsync(window, "document.querySelector('[data-action=complete]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=complete]').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).State == "completed");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=completed]').click()");
        await WaitAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]') !== null");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]').click()");
        await WaitAsync(window, "document.querySelector('[data-action=undo-complete]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=undo-complete]').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).State == "active");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]').click()");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]').click()");
        if (await EvaluateAsync(window, "document.querySelector('#reminder-form') === null") != "true")
            throw new InvalidOperationException("已取消的系统提醒设置仍在页面中。");
        await WaitAsync(window, "document.querySelector('#time-form [name=title]') !== null");
        var editedDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddDays(4).ToString("yyyy-MM-dd");
        var editFields = JsonSerializer.Serialize(new { title = "修订后的数学报告", course = "数学实验",
            date = editedDate, time = "18:30", timeKind = "start", priority = "3" });
        await EvaluateAsync(window, "Object.entries(" + editFields + ").forEach(([name,value])=>{const control=document.querySelector('#time-form [name='+name+']');control.value=value;control.dispatchEvent(new Event('input',{bubbles:true}));control.dispatchEvent(new Event('change',{bubbles:true}));});document.querySelector('#time-form').requestSubmit()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).Title == "修订后的数学报告");
        var edited = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id);
        if (edited.Course != "数学实验" || edited.Due != editedDate || edited.Time != "18:30" || edited.TimeKind != "start"
            || edited.Priority != 3 || edited.Version != task.Version + 3 || edited.Evidence.Count != task.Evidence.Count)
            throw new InvalidOperationException("完整编辑未保存全部字段或丢失了原始证据。");
        var editedReopened = new AccountWorkspace(dataRoot, workspace.AccountId).Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id);
        if (editedReopened.Title != edited.Title || editedReopened.Course != edited.Course || editedReopened.Due != edited.Due
            || editedReopened.Time != edited.Time || editedReopened.TimeKind != edited.TimeKind || editedReopened.Priority != 3)
            throw new InvalidDataException("重开工作区没有保留完整编辑。");
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Any(i => i.Id == task.Id && i.Title == edited.Title
            && i.Course == edited.Course && i.Due == edited.Due && i.Time == edited.Time && i.TimeKind == "start" && i.Priority == 3) == true);
        await WaitAsync(window, "document.querySelector('.change-history')?.textContent.includes('修改任务') && document.querySelector('.change-history').textContent.includes('修订后的数学报告')");
        await WaitAsync(() => !workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).Unread);
        if (workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).ChangeLabel is not null)
            throw new InvalidOperationException("已展示的编辑历史没有清除变化标记。");
        // An incoming task change must not silently upgrade the version of the form draft.
        workspace.Execute(new(workspace.AccountId, "edit-task", task.Id, "native-concurrent-edit", edited.Version,
            Date: editedDate, Time: "18:30", TimeKind: "start", Title: "同步更新的数学报告", Course: "数学实验", Priority: 3), DateTimeOffset.UtcNow);
        runtime.Refresh();
        await WaitAsync(window, "document.querySelector('.detail-title').textContent === '同步更新的数学报告'");
        await EvaluateAsync(window, "document.querySelector('#time-form').requestSubmit()");
        await WaitAsync(window, "document.querySelector('#detail-error').textContent.includes('草稿') && document.querySelector('[data-action=refresh-edit]').hidden === false");
        if (workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).Title != "同步更新的数学报告"
            || await EvaluateAsync(window, "document.querySelector('#time-form [name=title]').value === '修订后的数学报告'") != "true")
            throw new InvalidOperationException("版本冲突覆盖了正式任务或丢失了编辑草稿。");
        await EvaluateAsync(window, "document.querySelector('[data-action=refresh-edit]').click()");
        await WaitAsync(window, "document.querySelector('#detail-error').hidden === true");
        await EvaluateAsync(window, "document.querySelector('#time-form').requestSubmit()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).Title == edited.Title);
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=pending]').click()");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(review.Id) + "]').click()");
        await WaitAsync(window, "document.querySelector('#review-form [name=date]') !== null");
        var emptyDate = await EvaluateAsync(window, "document.querySelector('#review-form [name=date]').value === ''");
        if (emptyDate != "true") throw new InvalidOperationException("未确认建议不应成为正式日期。");
        var due = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddDays(3).ToString("yyyy-MM-dd");
        await EvaluateAsync(window, "document.querySelector('#review-form [name=date]').value=" + JsonSerializer.Serialize(due) + ";document.querySelector('#review-form [name=date]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#pending-confirm-button').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Count(i => i.Kind == "task") == 2);
        // Save a synthetic quoted requirement while no task detail is visible, then inspect its desktop label.
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
        var acceptedTask = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Kind == "task" && i.Id != task.Id);
        var newRequirementDate = DateOnly.Parse(acceptedTask.Due!, System.Globalization.CultureInfo.InvariantCulture);
        var requirementAt = DateTimeOffset.UtcNow.AddSeconds(5);
        var requirementKey = new MessageKey(workspace.AccountId, "10001", "3");
        workspace.Enqueue(new(requirementKey, "10002", requirementAt,
            [new(SegmentKind.Text, $"数学报告新增要求：{newRequirementDate.Month}月{newRequirementDate.Day}日之前补交实验数据"),
                new(SegmentKind.Quote, "原通知", new(workspace.AccountId, "10001", "2"))]),
            new HashSet<string> { "10001" }, MessageOrigin.Realtime, "synthetic-new-requirement", requirementAt);
        var taskRecord = new AccountTaskStore(dataRoot, workspace.AccountId).FindTask(acceptedTask.Id)
            ?? throw new InvalidDataException("合成任务未保存。");
        var reliable = new RuleAssessments(new("明确新要求", true), new("适用于本人", true),
            new("课程已确认", true), new("正式时间明确", true), new("原消息可核对", true), new("引用明确", true));
        new AccountTaskStore(dataRoot, workspace.AccountId).SavePending("synthetic-new-requirement-review",
            new RuleCandidate(taskRecord.Title, RuleAction.NewRequirement, RuleDisposition.Change, taskRecord.Course,
                taskRecord.Time, null, taskRecord.Id, taskRecord.Priority, reliable, [], [requirementKey]),
            "synthetic-new-requirement-review-operation", requirementAt);
        var newRequirement = workspace.Snapshot(requirementAt).Items.Single(i => i.Id == acceptedTask.Id);
        if (newRequirement.ChangeLabel != "待确认变更" || !newRequirement.Unread || newRequirement.Title != acceptedTask.Title)
            throw new InvalidOperationException("合成新要求没有保留稳定标题或产生未查看标记。");
        runtime.Refresh();
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Any(i => i.Id == acceptedTask.Id && i.ChangeLabel == "待确认变更") == true);
        await WaitAsync(() => Descendants(desktop.TaskWindow).OfType<System.Windows.Controls.TextBlock>().Any(text => text.Text == "待确认变更"));
        FindButton(desktop.TaskWindow, "open:" + acceptedTask.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('.pending-changes')?.textContent.includes('待确认新要求') && document.querySelector('.pending-changes').textContent.includes('查看建议')");
        var pendingRequirement = workspace.Snapshot(requirementAt).Items.Single(i => i.Kind == "review" && i.Id == "synthetic-new-requirement-review");
        workspace.Execute(new(workspace.AccountId, "accept-review", pendingRequirement.Id, "synthetic-accept-new-requirement",
            pendingRequirement.Version, Date: acceptedTask.Due, Time: acceptedTask.Time, TimeKind: acceptedTask.TimeKind,
            TargetTaskVersion: newRequirement.Version), requirementAt.AddSeconds(1));
        runtime.Refresh();
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Any(i => i.Id == acceptedTask.Id && i.ChangeLabel == "新要求") == true);
        FindButton(desktop.TaskWindow, "open:" + acceptedTask.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('.change-history')?.textContent.includes('确认新要求') && document.querySelector('.change-history').textContent.includes('补交实验数据')");
        await WaitAsync(() => !workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == acceptedTask.Id).Unread);
        await WaitAsync(() => desktop.Snapshot?.Tasks.Any(i => i.Id == acceptedTask.Id && i.ChangeLabel is null) == true);
        var reopened = new AccountWorkspace(dataRoot, workspace.AccountId);
        if (reopened.Snapshot(DateTimeOffset.UtcNow).Items.Count(i => i.Kind == "task") != 2)
            throw new InvalidDataException("重开工作区没有保留事项。");
        runtime.Refresh();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == 2 && !desktop.PendingWindow.IsVisible);
        FindButton(desktop.TaskWindow, "complete:" + task.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(i => i.Id == task.Id).State == "completed");
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == 1);
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=completed]').click()");
        await WaitAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]') !== null");
        await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(task.Id) + "]').click()");
        await WaitAsync(window, "document.querySelector('[data-action=undo-complete]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=undo-complete]').click()");
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == 2);
        // Exercise the actual resize event and persisted layout, without moving the user's pointer.
        var saved = desktop.Layout;
        var resizeThumb = FindThumb(desktop.TaskWindow);
        resizeThumb.RaiseEvent(new DragDeltaEventArgs(20, 12));
        resizeThumb.RaiseEvent(new DragCompletedEventArgs(20, 12, false));
        var resized = desktop.Layout;
        if (resized.Tasks.Width <= saved.Tasks.Width || resized.Tasks.Height <= saved.Tasks.Height)
            throw new InvalidOperationException("正式卡片缩放事件未更新窗口。");
        var savedLayout = new DesktopLayoutStore(dataRoot).Load(new DesktopRect(0, 0, 100000, 100000),
            DesktopNative.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(desktop.TaskWindow).Handle) / 96d);
        if (savedLayout != resized) throw new InvalidOperationException("正式卡片布局没有持久化。");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=tasks]').click()");
        await WaitAsync(window, "document.querySelectorAll('.task-row').length === 2");
        // Create from the actual page and bridge, retaining date-only precision and an honest manual source.
        await EvaluateAsync(window, "document.querySelector('[data-action=new-task]').click()");
        await WaitAsync(window, "document.querySelector('#manual-task-form') !== null");
        var manualDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddDays(4).ToString("yyyy-MM-dd");
        await EvaluateAsync(window, "(() => {const form=document.querySelector('#manual-task-form');"
            + "for(const [name,value] of Object.entries({title:'口头布置的补充报告',course:'口头事项',date:"
            + JsonSerializer.Serialize(manualDate) + "})) {const input=form.elements.namedItem(name);input.value=value;"
            + "input.dispatchEvent(new Event('input',{bubbles:true}));}form.requestSubmit();})()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Any(item => item.Source == "manual"));
        var manual = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Source == "manual");
        if (manual.Title != "口头布置的补充报告" || manual.Course != "口头事项" || manual.Due != manualDate
            || manual.Time is not null || manual.TimeKind != "deadline" || manual.Group != "手动添加"
            || manual.Evidence.Count != 0 || !manual.Changes!.Any(change => change.Label == "手动添加任务"))
            throw new InvalidOperationException("手动新增丢失填写值、日期精度、来源或操作记录。");
        await WaitAsync(window, "document.querySelector('.detail-title')?.textContent === '口头布置的补充报告' && document.querySelector('#detail-pane').textContent.includes('手动添加')");
        var manualReopened = new AccountWorkspace(dataRoot, workspace.AccountId).Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == manual.Id);
        if (manualReopened.Source != "manual" || manualReopened.Due != manual.Due || manualReopened.Time is not null
            || manualReopened.Evidence.Count != 0 || manualReopened.State != "active")
            throw new InvalidDataException("手动任务重开未保留正式值或来源。");
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Any(item => item.Id == manual.Id && item.Source == "manual") == true);
        await WaitAsync(() => Descendants(desktop.TaskWindow).OfType<System.Windows.Controls.TextBlock>()
            .Any(text => text.Text.Contains("口头事项 · 手动添加", StringComparison.Ordinal)));
        FindButton(desktop.TaskWindow, "complete:" + manual.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == manual.Id).State == "completed");
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == 2);
        await CheckTaskStructureAsync(window, workspace, runtime, desktop, dataRoot, task.Id, acceptedTask.Id);
        await CheckCrossGroupMergeSuggestionsAsync(window, workspace, runtime, desktop, dataRoot);
        await CheckCacheCleanupAsync(window, workspace, runtime, dataRoot);
        var backupSmoke = await CheckBackupRestoreAsync(window, workspace, runtime, desktop, dataRoot, reportFile);
        var desktopRecoverySmoke = await CheckDesktopRecoveryAsync(dataRoot, reportFile);
        var expectedCards = workspace.Snapshot(DateTimeOffset.UtcNow);
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == expectedCards.Items.Count(item => item.Kind == "task" && item.State == "active")
            && desktop.Snapshot.Pending.Count == expectedCards.Items.Count(item => item.Kind is "review" or "merge-review" && item.State == "pending"));
        desktop.Capture(cardsCapture);
        var final = workspace.Snapshot(DateTimeOffset.UtcNow);
        var capture = Path.ChangeExtension(reportFile, ".png");
        await using (var image = File.Create(capture)) await window.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        File.WriteAllText(reportFile, JsonSerializer.Serialize(new { success = true, synthetic = true,
            checks = new[] { "branding", "snapshot", "complete", "undo-complete", "explicit-date", "accept-review", "native-bridge", "reopen",
                "desktop-data", "desktop-theme", "desktop-open-item", "desktop-complete", "desktop-layout", "pending-hide",
                "edit-task", "edit-task-reopen", "edit-task-desktop", "edit-task-conflict",
                "change-history", "change-read", "desktop-new-requirement", "desktop-change-read",
                "manual-task-create", "manual-task-reopen", "manual-task-desktop", "manual-task-complete",
                "merge-task-conflicts", "merge-task-selection", "merge-task-evidence-history", "merge-task-old-id",
                "merge-task-desktop", "split-task-create", "split-task-evidence", "split-task-related-navigation",
                "split-task-pending", "task-structure-reopen", "task-structure-two-cards",
                "cross-group-merge-suggestion", "cross-group-merge-card", "cross-group-merge-preselected",
                "cross-group-merge-current-conflict", "cross-group-keep-separate", "cross-group-keep-separate-reopen",
                "cross-group-merge-confirm", "cross-group-merge-evidence-old-id", "cross-group-merge-suggestion-hide",
                "cache-cleanup-page", "cache-cleanup-old-chat", "cache-cleanup-task-evidence",
                "cache-cleanup-review-evidence", "cache-cleanup-notice-evidence", "cache-cleanup-reopen",
                "cache-cleanup-replay-dedup" }.Concat(BackupChecks).Concat(DesktopRecoveryChecks).ToArray(),
            taskCount = final.Items.Count(item => item.Kind == "task" && item.State != "merged"),
            activeTaskCount = final.Items.Count(item => item.Kind == "task" && item.State == "active"),
            mergedTaskCount = final.Items.Count(item => item.State == "merged"),
            pendingCount = final.Items.Count(item => item.Kind == "review" && item.State == "pending"),
            mergeSuggestionCount = final.Items.Count(item => item.Kind == "merge-review" && item.State == "pending"),
            cacheCleanup = final.CacheCleanup,
            backup = backupSmoke,
            desktopRecovery = desktopRecoverySmoke,
            capture, cardsCapture, desktop = desktop.Inspect() }, AppWindow.JsonOptions));
    }

    private static async Task CheckCacheCleanupAsync(AppWindow window, AccountWorkspace workspace,
        AppRuntime runtime, string dataRoot)
    {
        // Program supplies a unique temporary root and never starts the NapCat runtime in smoke mode.
        if (workspace.AccountId != "10000" || !Path.GetFileName(dataRoot).StartsWith("messagepurify-app-", StringComparison.Ordinal))
            throw new InvalidOperationException("缓存合成检查必须使用隔离的临时工作区。");
        var now = DateTimeOffset.UtcNow;
        var old = now.AddDays(-10);
        var due = now.ToOffset(TimeSpan.FromHours(8)).AddDays(5);
        HashSet<string> pinned = ["10001", "10101", "10102"];
        string[] texts = [$"{due.Year}年{due.Month}月{due.Day}日之前提交数学缓存专项资料",
            "请提交数学缓存待核对附件", $"自愿报名数学缓存讲座，{due.Month}月{due.Day}日18点截止",
            "合成闲聊：今天阳光很好。"];
        var messages = texts.Select((text, index) => new SchoolMessage(
            new(workspace.AccountId, "10001", (801 + index).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "10002", old.AddSeconds(index), [new(SegmentKind.Text, text)])).ToArray();
        foreach (var message in messages)
            if (!workspace.Ingest(message, pinned, MessageOrigin.History, "synthetic-cache-cleanup", now))
                throw new InvalidOperationException("缓存检查的合成消息未入库。");
        var store = new AccountMessageStore(dataRoot, workspace.AccountId);
        var seeded = workspace.Snapshot(now);
        string[] kinds = ["task", "review", "notice"];
        var kept = texts.Take(3).Select((text, index) => seeded.Items.Single(item => item.Kind == kinds[index]
            && item.Evidence.Any(evidence => evidence.Text == text))).ToArray();
        if (store.Find(messages[3].Key)?.Extraction is not { } chatExtraction
            || chatExtraction.Candidates.Any(candidate => candidate.Disposition != RuleDisposition.Ignored)
            || kept.Any(item => item.Evidence.Count != 1 || item.Evidence[0].SentAt != old.AddSeconds(Array.IndexOf(kept, item))))
            throw new InvalidOperationException("缓存检查未建立已处理的旧闲聊及三类旧证据。");

        runtime.Refresh();
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
        await WaitAsync(window, "document.querySelector('[data-action=clean-cache]')?.disabled === false");
        await EvaluateAsync(window, "document.querySelector('[data-action=clean-cache]').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).CacheCleanup is not null);
        var cleaned = workspace.Snapshot(DateTimeOffset.UtcNow);
        var result = cleaned.CacheCleanup ?? throw new InvalidDataException("手动清理没有保存成功结果。");
        if (result.AccountId != workspace.AccountId || result.DeletedMessages != 1 || result.RetainedOldMessages != 3
            || result.RemainingMessages < 3 || result.Cutoff != result.CompletedAt.AddDays(-7)
            || cleaned.CacheCleanupError is not null || store.Find(messages[3].Key) is not null)
            throw new InvalidOperationException("设置页清理未按原发送日期删除一条闲聊并保留旧证据。");
        await WaitAsync(window, "document.querySelector('#cache-cleanup-result')?.textContent.includes('实际删除 1 条') === true");
        var displayedResult = await EvaluateAsync(window, "document.querySelector('#cache-cleanup-result').textContent");
        for (var index = 0; index < kept.Length; index++)
        {
            var item = cleaned.Items.Single(candidate => candidate.Id == kept[index].Id);
            if (item.Kind != kinds[index] || item.State != kept[index].State || item.Title != kept[index].Title
                || !item.Evidence.SequenceEqual(kept[index].Evidence) || store.Find(messages[index].Key) is null)
                throw new InvalidOperationException("缓存清理丢失任务、待确认或通知的原始证据。");
            if (item.Kind == "notice")
            {
                await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=notices]').click()");
                await EvaluateAsync(window, "Array.from(document.querySelectorAll('[data-action=select]')).find(row => row.dataset.id === "
                    + JsonSerializer.Serialize(item.Id) + ")?.click()");
            }
            else await window.OpenItemAsync(workspace.AccountId, item.Id);
            await WaitAsync(window, "document.querySelector('.source-section .message')?.textContent === " + JsonSerializer.Serialize(texts[index]));
        }

        var reopenedWorkspace = new AccountWorkspace(dataRoot, workspace.AccountId);
        reopenedWorkspace.SetAvailableGroups(seeded.Groups.Select(group => new OneBotGroup(group.GroupId, group.Name)).ToArray(), pinned);
        var reopened = reopenedWorkspace.Snapshot(DateTimeOffset.UtcNow);
        if (reopened.CacheCleanup != result || reopened.CacheCleanupError is not null
            || kept.Any(item => !reopened.Items.Single(candidate => candidate.Id == item.Id).Evidence.SequenceEqual(item.Evidence)))
            throw new InvalidDataException("重开工作区丢失缓存清理结果或旧证据。");
        var reopenedRuntime = new AppRuntime(dataRoot, reopenedWorkspace);
        var reopenedWindow = new AppWindow(reopenedRuntime, dataRoot, hidden: true, registerStartup: false);
        try
        {
            reopenedWindow.Show();
            await reopenedWindow.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            // NavigationReady only confirms the HTML loaded; wait for the first account snapshot
            // so its default-page initialization cannot replace the settings page after our click.
            await WaitAsync(reopenedWindow, "document.querySelector('.task-row') !== null");
            await EvaluateAsync(reopenedWindow, "document.querySelector('[data-action=nav][data-page=settings]').click()");
            await WaitAsync(reopenedWindow, "document.querySelector('#cache-cleanup-result')?.textContent.includes('实际删除 1 条') === true");
            if (await EvaluateAsync(reopenedWindow, "document.querySelector('#cache-cleanup-result').textContent") != displayedResult)
                throw new InvalidDataException("重开正式页面没有保留相同的成功清理时间及条数。");
        }
        finally
        {
            reopenedWindow.Exiting = true;
            reopenedWindow.Close();
            await reopenedRuntime.StopAsync();
        }
        if (reopenedWorkspace.Ingest(messages[3], pinned, MessageOrigin.History, "synthetic-cache-replay", DateTimeOffset.UtcNow)
            || store.Find(messages[3].Key) is not null || reopenedWorkspace.ProcessPending(pinned, DateTimeOffset.UtcNow) != 0
            || reopenedWorkspace.Snapshot(DateTimeOffset.UtcNow).Items.Count != reopened.Items.Count
            || reopenedWorkspace.Snapshot(DateTimeOffset.UtcNow).CacheCleanup != result)
            throw new InvalidOperationException("重读已清理的旧消息重新入库、生成事项或改写了清理结果。");
        runtime.Refresh();
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
        await WaitAsync(window, "document.querySelector('#cache-cleanup-result')?.textContent.includes('实际删除 1 条') === true");
    }

    private static async Task CheckCrossGroupMergeSuggestionsAsync(AppWindow window, AccountWorkspace workspace,
        AppRuntime runtime, DesktopCardHost desktop, string dataRoot)
    {
        var baseline = workspace.Snapshot(DateTimeOffset.UtcNow);
        HashSet<string> pinned = ["10001", "10101", "10102"];
        OneBotGroup[] groups = [new("10001", "合成课程群"),
            new("10101", "数学课程通知群"), new("10102", "数学课程转发群")];
        workspace.SetAvailableGroups(groups, pinned);
        var now = DateTimeOffset.UtcNow;
        foreach (var group in new[] { "10101", "10102" })
            workspace.Execute(new(workspace.AccountId, "confirm-group", group, "native-cross-group-course-" + group,
                0, Course: "数学"), now);
        var originalDate = now.ToOffset(TimeSpan.FromHours(8)).AddDays(8);
        var separateText = $"{originalDate.Year}年{originalDate.Month}月{originalDate.Day}日之前提交第一轮数学实验报告";
        var separatePair = IngestCrossGroupPair(workspace, pinned, "101", separateText, now);
        var separateSuggestion = workspace.Snapshot(now).Items.Single(item => item.Kind == "merge-review");
        var pair = separateSuggestion.MergeSuggestion ?? throw new InvalidDataException("跨群建议没有记录双方事项和版本。");
        var first = separatePair.Single(item => item.Id == pair.FirstTaskId);
        var second = separatePair.Single(item => item.Id == pair.SecondTaskId);
        if (separateSuggestion.State != "pending" || !separateSuggestion.Title.StartsWith("可能重复：", StringComparison.Ordinal)
            || separateSuggestion.Evidence.Count != 2 || separateSuggestion.ReviewReasons.Count == 0
            || first.State != "active" || second.State != "active" || first.Due != second.Due)
            throw new InvalidOperationException("跨群同原文未形成保留双方正式任务、证据和理由的待确认建议。");

        // Original equality remains a suggestion after a user changes a current formal date.
        runtime.Refresh();
        await window.OpenItemAsync(workspace.AccountId, first.Id);
        await WaitAsync(window, "document.querySelector('#time-form [name=date]') !== null");
        var changedDate = originalDate.AddDays(2).ToString("yyyy-MM-dd");
        await SetFormFieldsAsync(window, "time-form", new { date = changedDate });
        await EvaluateAsync(window, "document.querySelector('#time-form').requestSubmit()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == first.Id).Due == changedDate);
        separateSuggestion = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == separateSuggestion.Id);
        pair = separateSuggestion.MergeSuggestion ?? throw new InvalidDataException("手动改日期后建议丢失双方版本。");
        if (pair.FirstTaskVersion != workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == first.Id).Version)
            throw new InvalidOperationException("建议没有更新手动修改后的当前任务版本。");
        runtime.Refresh();
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == baseline.Items.Count(item => item.Kind == "task" && item.State == "active") + 2
            && desktop.Snapshot.Pending.Count(item => item.Kind == "merge-review") == 1
            && desktop.Snapshot.Pending.Any(item => item.Id == separateSuggestion.Id));
        FindButton(desktop.PendingWindow, "open:" + separateSuggestion.Id)
            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('.task-row.selected')?.dataset.id === " + JsonSerializer.Serialize(separateSuggestion.Id));
        await WaitAsync(window, "document.querySelector('.detail-title')?.textContent === " + JsonSerializer.Serialize(separateSuggestion.Title));
        await OpenMergeSuggestionAsync(window, pair);
        await WaitAsync(window, "document.querySelector('#merge-task-form [name=timeTaskId]')?.closest('.merge-field').classList.contains('conflict') === true"
            + " && document.querySelector('#merge-task-form').textContent.includes(" + JsonSerializer.Serialize(changedDate) + ")");
        await EvaluateAsync(window, "document.querySelector('[data-action=keep-separate]').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.All(item => item.Id != separateSuggestion.Id));
        var separated = workspace.Snapshot(DateTimeOffset.UtcNow);
        if (separated.Items.Single(item => item.Id == first.Id).Due != changedDate
            || separated.Items.Single(item => item.Id == second.Id).Due != second.Due
            || separatePair.Any(original => separated.Items.Single(item => item.Id == original.Id).State != "active"))
            throw new InvalidOperationException("保持独立改变了双方正式时间或任务状态。");
        var reopenedWorkspace = new AccountWorkspace(dataRoot, workspace.AccountId);
        reopenedWorkspace.SetAvailableGroups(groups, pinned);
        var reopened = reopenedWorkspace.Snapshot(DateTimeOffset.UtcNow);
        if (reopened.Items.Any(item => item.Id == separateSuggestion.Id)
            || reopened.Items.Single(item => item.Id == first.Id).Due != changedDate
            || reopened.Items.Single(item => item.Id == second.Id).Due != second.Due)
            throw new InvalidDataException("重开工作区重新出现已保持独立的建议或丢失当前日期。");
        runtime.Refresh();
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Pending.All(item => item.Id != separateSuggestion.Id) == true);

        var mergeText = $"{originalDate.Year}年{originalDate.Month}月{originalDate.Day}日之前提交第二轮数学实验报告";
        var mergePair = IngestCrossGroupPair(workspace, pinned, "102", mergeText, DateTimeOffset.UtcNow);
        var mergeSuggestion = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Kind == "merge-review");
        var merge = mergeSuggestion.MergeSuggestion ?? throw new InvalidDataException("第二对跨群建议缺少双方。");
        runtime.Refresh();
        await window.OpenItemAsync(workspace.AccountId, mergeSuggestion.Id);
        await OpenMergeSuggestionAsync(window, merge);
        await SetFormFieldsAsync(window, "merge-task-form", new { titleTaskId = merge.FirstTaskId,
            courseTaskId = merge.SecondTaskId, timeTaskId = merge.FirstTaskId,
            priorityTaskId = merge.SecondTaskId, stateTaskId = merge.FirstTaskId });
        await EvaluateAsync(window, "document.querySelector('#merge-task-form').requestSubmit()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == merge.SecondTaskId).State == "merged");
        var merged = workspace.Snapshot(DateTimeOffset.UtcNow);
        var kept = merged.Items.Single(item => item.Id == merge.FirstTaskId);
        var old = merged.Items.Single(item => item.Id == merge.SecondTaskId);
        var evidence = mergePair.SelectMany(item => item.Evidence).Distinct().ToArray();
        if (old.MergedIntoId != kept.Id || kept.State != "active" || kept.Evidence.Count != 2
            || evidence.Any(item => !kept.Evidence.Contains(item))
            || kept.Changes?.Any(change => change.Label.Contains("合并", StringComparison.Ordinal)) != true
            || merged.Items.Any(item => item.Id == mergeSuggestion.Id))
            throw new InvalidOperationException("显式跨群合并未保留双方原文、旧 ID、历史或没有移除建议。");
        await window.OpenItemAsync(workspace.AccountId, old.Id);
        await WaitAsync(window, "document.querySelector('.task-row.selected')?.dataset.id === " + JsonSerializer.Serialize(kept.Id));
        var mergedReopenedWorkspace = new AccountWorkspace(dataRoot, workspace.AccountId);
        mergedReopenedWorkspace.SetAvailableGroups(groups, pinned);
        var mergedReopened = mergedReopenedWorkspace.Snapshot(DateTimeOffset.UtcNow);
        if (mergedReopened.Items.Single(item => item.Id == old.Id).MergedIntoId != kept.Id
            || mergedReopened.Items.Single(item => item.Id == kept.Id).Evidence.Count != 2
            || mergedReopened.Items.Any(item => item.Id == mergeSuggestion.Id || item.Id == separateSuggestion.Id))
            throw new InvalidDataException("重开工作区丢失跨群合并或重新显示已处理建议。");
        runtime.Refresh();
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Pending.All(item => item.Kind != "merge-review") == true
            && desktop.Snapshot.Tasks.All(item => item.Id != old.Id)
            && desktop.Snapshot.Tasks.Count == baseline.Items.Count(item => item.Kind == "task" && item.State == "active") + 3);
    }

    private static ApplicationItem[] IngestCrossGroupPair(AccountWorkspace workspace, IReadOnlySet<string> pinned,
        string messageId, string text, DateTimeOffset now)
    {
        foreach (var group in new[] { "10101", "10102" })
            workspace.Ingest(new(new(workspace.AccountId, group, messageId), "10002", now,
                [new(SegmentKind.Text, text)]), pinned, MessageOrigin.Realtime, "synthetic-cross-group-" + messageId, now);
        var pair = workspace.Snapshot(now).Items.Where(item => item.Kind == "task"
            && item.Evidence.Any(evidence => evidence.Text == text)).ToArray();
        if (pair.Length != 2) throw new InvalidOperationException("跨群合成消息没有保留两项独立正式任务。");
        return pair;
    }

    private static async Task OpenMergeSuggestionAsync(AppWindow window, ApplicationMergeSuggestion suggestion)
    {
        await WaitAsync(window, "document.querySelector('[data-action=open-merge-suggestion]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=open-merge-suggestion]').click()");
        await WaitAsync(window, "document.querySelector('#merge-task-form')?.dataset.id === " + JsonSerializer.Serialize(suggestion.FirstTaskId)
            + " && document.querySelector('#merge-task-form [name=mergeWithId]')?.value === " + JsonSerializer.Serialize(suggestion.SecondTaskId));
    }

    private static async Task CheckTaskStructureAsync(AppWindow window, AccountWorkspace workspace,
        AppRuntime runtime, DesktopCardHost desktop, string dataRoot, string keptId, string sourceId)
    {
        // Use the real page, including state differences, while the manual smoke task stays completed.
        await window.OpenItemAsync(workspace.AccountId, sourceId);
        await WaitAsync(window, "document.querySelector('[data-action=complete]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=complete]').click()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == sourceId).State == "completed");
        var before = workspace.Snapshot(DateTimeOffset.UtcNow);
        var kept = before.Items.Single(item => item.Id == keptId);
        var source = before.Items.Single(item => item.Id == sourceId);
        await window.OpenItemAsync(workspace.AccountId, keptId);
        await WaitAsync(window, "document.querySelector('[data-action=open-merge]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=open-merge]').click()");
        await WaitAsync(window, "document.querySelector('#merge-task-form [name=mergeWithId]') !== null");
        await SetFormFieldsAsync(window, "merge-task-form", new { mergeWithId = sourceId });
        await WaitAsync(window, "document.querySelectorAll('#merge-task-form .merge-field.conflict').length >= 4 && document.querySelector('#merge-task-form').textContent.includes('存在差异')");
        await SetFormFieldsAsync(window, "merge-task-form", new { titleTaskId = keptId, courseTaskId = sourceId,
            timeTaskId = sourceId, priorityTaskId = sourceId, stateTaskId = keptId });
        await EvaluateAsync(window, "document.querySelector('#merge-task-form').requestSubmit()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == sourceId).State == "merged");
        var mergedSnapshot = workspace.Snapshot(DateTimeOffset.UtcNow);
        var merged = mergedSnapshot.Items.Single(item => item.Id == keptId);
        var old = mergedSnapshot.Items.Single(item => item.Id == sourceId);
        if (merged.Title != kept.Title || merged.Course != source.Course || merged.Due != source.Due
            || merged.Time != source.Time || merged.TimeKind != source.TimeKind || merged.Priority != source.Priority
            || merged.State != "active" || old.MergedIntoId != keptId || !(merged.RelatedItemIds ?? []).Contains(sourceId))
            throw new InvalidOperationException("合并未按显式选择保留字段、状态或原 ID 关联。");
        var evidence = kept.Evidence.Concat(source.Evidence).Distinct().ToArray();
        if (merged.Evidence.Count != evidence.Length || evidence.Any(item => !merged.Evidence.Contains(item))
            || merged.Changes?.Any(change => change.Label.Contains("合并", StringComparison.Ordinal)) != true
            || merged.Changes.Any(change => change.Label == "确认新要求") != true
            || merged.Changes.Any(change => change.Label == "修改任务") != true)
            throw new InvalidOperationException("合并丢失原始证据、来源历史或本次合并记录。");
        await WaitAsync(window, "document.querySelector('.change-history')?.textContent.includes('合并') && document.querySelector('.change-history').textContent.includes('确认新要求')");
        await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=completed]').click()");
        await WaitAsync(window, "document.querySelectorAll('.task-row').length === 1");
        if (await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(sourceId) + "]') === null") != "true")
            throw new InvalidOperationException("被合并的旧任务仍重复出现在已完成列表中。");
        await window.OpenItemAsync(workspace.AccountId, sourceId);
        await WaitAsync(window, "document.querySelector('.task-row.selected')?.dataset.id === " + JsonSerializer.Serialize(keptId));
        if (await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + JsonSerializer.Serialize(sourceId) + "]') === null") != "true")
            throw new InvalidOperationException("被合并的旧任务仍重复出现在列表中。");
        runtime.Refresh();
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == 1 && desktop.Snapshot.Tasks[0].Id == keptId);

        var splitTitle = "拆出的实验数据任务";
        var splitDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddDays(6).ToString("yyyy-MM-dd");
        await OpenSplitFormAsync(window);
        await SetFormFieldsAsync(window, "split-task-form", new { title = splitTitle, course = "数学拆分",
            date = splitDate, time = "20:15", timeKind = "start", priority = "1", state = "active" });
        await SelectSplitEvidenceAsync(window, 0);
        await EvaluateAsync(window, "document.querySelector('#split-task-form').requestSubmit()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Any(item => item.Kind == "task" && item.Title == splitTitle));
        var split = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Title == splitTitle);
        var parent = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == keptId);
        if (split.State != "active" || split.Course != "数学拆分" || split.Due != splitDate || split.Time != "20:15"
            || split.TimeKind != "start" || split.Priority != 1 || split.Evidence.Count != 1
            || split.Evidence[0] != merged.Evidence[0] || parent.Evidence.Count != merged.Evidence.Count
            || !(split.RelatedItemIds ?? []).Contains(keptId) || !(parent.RelatedItemIds ?? []).Contains(split.Id))
            throw new InvalidOperationException("正式拆分没有保留填写值、选中证据和父任务关联。");
        await window.OpenItemAsync(workspace.AccountId, keptId);
        await OpenRelatedAsync(window, split.Id);
        await WaitAsync(window, "document.querySelector('.detail-title')?.textContent === " + JsonSerializer.Serialize(splitTitle));
        await OpenRelatedAsync(window, keptId);

        var pendingTitle = "日期待核对的报告分项";
        await OpenSplitFormAsync(window);
        await SetFormFieldsAsync(window, "split-task-form", new { title = pendingTitle, course = "数学",
            date = "", time = "", timeKind = "deadline", priority = "auto", state = "active" });
        await SelectSplitEvidenceAsync(window, merged.Evidence.Count - 1);
        await EvaluateAsync(window, "document.querySelector('#split-task-form').requestSubmit()");
        await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Any(item => item.Kind == "review" && item.Title == pendingTitle));
        var pending = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Title == pendingTitle);
        if (pending.State != "pending" || pending.Due is not null || pending.Time is not null
            || pending.Evidence.Count != 1 || pending.Evidence[0] != merged.Evidence[^1]
            || !(pending.RelatedItemIds ?? []).Contains(keptId))
            throw new InvalidOperationException("无正式日期的拆分没有进入待确认或丢失选中证据。");
        await window.OpenItemAsync(workspace.AccountId, keptId);
        await OpenRelatedAsync(window, pending.Id);
        await WaitAsync(window, "document.querySelector('#review-form [name=date]')?.value === '' && document.querySelector('.detail-title')?.textContent === " + JsonSerializer.Serialize(pendingTitle));
        await OpenRelatedAsync(window, keptId);
        var reopened = new AccountWorkspace(dataRoot, workspace.AccountId).Snapshot(DateTimeOffset.UtcNow);
        var reopenedParent = reopened.Items.Single(item => item.Id == keptId);
        if (reopened.Items.Single(item => item.Id == sourceId).MergedIntoId != keptId
            || reopened.Items.Single(item => item.Id == sourceId).State != "merged"
            || reopenedParent.Evidence.Count != merged.Evidence.Count
            || !(reopenedParent.RelatedItemIds ?? []).Contains(split.Id)
            || !(reopenedParent.RelatedItemIds ?? []).Contains(pending.Id)
            || reopened.Items.Single(item => item.Id == split.Id).Due != splitDate
            || reopened.Items.Single(item => item.Id == pending.Id).State != "pending")
            throw new InvalidDataException("重开工作区丢失合并、拆分任务、待确认或关联。");
        runtime.Refresh();
        await desktop.RefreshAsync();
        await WaitAsync(() => desktop.Snapshot?.Tasks.Count == 2 && desktop.Snapshot.Tasks.Any(item => item.Id == split.Id)
            && desktop.Snapshot.Pending.Count == 1 && desktop.Snapshot.Pending[0].Id == pending.Id
            && desktop.TaskWindow.IsVisible && desktop.PendingWindow.IsVisible);
        FindButton(desktop.PendingWindow, "open:" + pending.Id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await WaitAsync(window, "document.querySelector('.task-row.selected')?.dataset.id === " + JsonSerializer.Serialize(pending.Id));
        await OpenRelatedAsync(window, keptId);
    }

    private static Task SetFormFieldsAsync(AppWindow window, string formId, object fields) => EvaluateAsync(window,
        "Object.entries(" + JsonSerializer.Serialize(fields) + ").forEach(([name,value])=>{const control=document.querySelector('#"
        + formId + " [name='+name+']');control.value=value;control.dispatchEvent(new Event('input',{bubbles:true}));"
        + "control.dispatchEvent(new Event('change',{bubbles:true}));})");

    private static Task SelectSplitEvidenceAsync(AppWindow window, int index) => EvaluateAsync(window,
        "document.querySelectorAll('#split-task-form [name=evidenceIndex]').forEach(control=>{control.checked=control.value==='"
        + index + "';control.dispatchEvent(new Event('change',{bubbles:true}));})");

    private static async Task OpenSplitFormAsync(AppWindow window)
    {
        await WaitAsync(window, "document.querySelector('[data-action=open-split]') !== null");
        await EvaluateAsync(window, "document.querySelector('[data-action=open-split]').click()");
        await WaitAsync(window, "document.querySelector('#split-task-form') !== null");
    }

    private static async Task OpenRelatedAsync(AppWindow window, string id)
    {
        var selector = "[data-action=open-related][data-id=" + JsonSerializer.Serialize(id) + "]";
        await WaitAsync(window, "document.querySelector(" + JsonSerializer.Serialize(selector) + ") !== null");
        await EvaluateAsync(window, "document.querySelector(" + JsonSerializer.Serialize(selector) + ").click()");
        await WaitAsync(window, "document.querySelector('.task-row.selected')?.dataset.id === " + JsonSerializer.Serialize(id));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static System.Windows.Controls.Button FindButton(Window window, string tag) => Descendants(window)
        .OfType<System.Windows.Controls.Button>().Single(button => Equals(button.Tag, tag));
    private static Thumb FindThumb(Window window) => Descendants(window).OfType<Thumb>()
        .Single(thumb => Equals(thumb.ToolTip, "拖动调整卡片大小"));

    public static async Task RunReadOnlyAsync(AppRuntime runtime, string reportPath, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var snapshot = runtime.Workspace?.Snapshot(DateTimeOffset.UtcNow);
        var status = runtime.Status;
        var success = snapshot is not null && status.Transport == "connected" && status.Qq == "online";
        var safeStatus = new { status.Transport, status.Qq, status.Receiving, status.History, status.Error,
            status.Scheduled, status.LastSuccessfulSyncAt, status.NextScheduledSyncAt,
            failures = status.HistoryFailures.Select(f => f.Reason).ToArray() };
        var reportFile = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);
        File.WriteAllText(reportFile, JsonSerializer.Serialize(new { success, synthetic = false, status = safeStatus,
            groups = snapshot?.Groups.Count, pinnedGroups = snapshot?.Groups.Count(g => g.Pinned), items = snapshot?.Items.Count,
            tasks = snapshot?.Items.Count(i => i.Kind == "task"), pending = snapshot?.Items.Count(i => i.Kind == "review"),
            notices = snapshot?.Items.Count(i => i.Kind == "notice") }, AppWindow.JsonOptions));
        if (!success) throw new InvalidOperationException("真实只读连接未通过。");
    }

    private static Task<string> EvaluateAsync(AppWindow window, string script) => window.Browser.ExecuteScriptAsync(script);
    private static async Task WaitAsync(AppWindow window, string expression)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await EvaluateAsync(window, expression) == "true") return;
            await Task.Delay(100);
        }
        throw new TimeoutException("本地页面检查超时：" + expression);
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("正式存储检查超时。");
    }
}
