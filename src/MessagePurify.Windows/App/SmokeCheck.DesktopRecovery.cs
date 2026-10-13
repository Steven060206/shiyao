using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using MessagePurify.Windows.IntegrationProbe;
using Button = System.Windows.Controls.Button;

namespace MessagePurify.Windows.App;

internal static partial class SmokeCheck
{
    private static readonly string[] DesktopRecoveryChecks = [
        "desktop-detached-parent-recovery", "desktop-closed-window-recovery",
        "desktop-host-waiting", "desktop-host-return-current-data",
        "desktop-recovery-preserves-data", "desktop-recovery-appearance-layout",
        "desktop-work-area-fit", "desktop-work-area-return",
        "desktop-dpi-reload", "desktop-system-resume-message",
        "desktop-stale-window-callback", "desktop-main-window-and-empty-pending",
        "desktop-await-environment-change", "desktop-read-error-cleared-after-recovery",
        "desktop-stale-layout-callback"
    ];

    private sealed record DesktopRecoverySmokeResult(bool SyntheticHostAndDisplayChanges,
        string State, int ActiveTaskCount, int PendingCount, double Scale,
        DesktopCardLayout Layout, object NativeWindows, string Capture);

    private sealed class SyntheticDesktopEnvironment(DesktopEnvironmentState initial) : IDesktopEnvironment
    {
        private readonly NativeDesktopEnvironment native = new();
        public DesktopEnvironmentState? Current { get; set; } = initial;
        public int Captures { get; private set; }
        public int AttachmentChecks { get; private set; }
        public int? ChangeAtCapture { get; set; }
        public Action? ChangeDuringCapture { get; set; }
        public int? ChangeAtAttachmentCheck { get; set; }
        public Action? ChangeAfterAttachmentCheck { get; set; }
        public DesktopEnvironmentState? Capture()
        {
            Captures++;
            if (Captures == ChangeAtCapture)
            {
                var change = ChangeDuringCapture;
                ChangeAtCapture = null;
                ChangeDuringCapture = null;
                change?.Invoke();
            }
            return Current;
        }
        public bool IsAttached(nint child, nint parent)
        {
            var attached = native.IsAttached(child, parent);
            AttachmentChecks++;
            if (attached && AttachmentChecks == ChangeAtAttachmentCheck)
            {
                var change = ChangeAfterAttachmentCheck;
                ChangeAtAttachmentCheck = null;
                ChangeAfterAttachmentCheck = null;
                change?.Invoke();
            }
            return attached;
        }
    }

    private static async Task<DesktopRecoverySmokeResult> CheckDesktopRecoveryAsync(string dataRoot, string reportFile)
    {
        if (!Path.GetFileName(dataRoot).StartsWith("messagepurify-app-", StringComparison.Ordinal))
            throw new InvalidOperationException("桌面恢复合成检查必须使用隔离的临时工作区。");
        var recoveryRoot = Path.Combine(dataRoot, "desktop-recovery");
        var workspace = CreateWorkspace(recoveryRoot);
        var runtime = new AppRuntime(recoveryRoot, workspace);
        var window = new AppWindow(runtime, recoveryRoot, hidden: true, registerStartup: false);
        var native = new NativeDesktopEnvironment();
        var initialEnvironment = native.Capture() ?? throw new InvalidOperationException("真实 Windows 桌面暂不可用。");
        var environment = new SyntheticDesktopEnvironment(initialEnvironment);
        using var desktop = new DesktopCardHost(runtime, recoveryRoot, window.OpenItemAsync, environment);
        window.AppearanceChanged += desktop.SetAppearance;
        desktop.WatchSystemWindow(window);
        try
        {
            PrepareRecoveryHistory(recoveryRoot, workspace);
            window.Show();
            await window.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitAsync(window, "document.querySelector('.task-row') !== null");
            await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=settings]').click()");
            await WaitAsync(window, "document.querySelector('#theme-setting') !== null");
            await ChangeSettingAsync(window, "theme-setting", "dark");
            await ChangeSettingAsync(window, "motion-setting", "reduce");
            await desktop.RefreshAsync();
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);
            var baseline = workspace.Snapshot(DateTimeOffset.UtcNow);
            var active = baseline.Items.Single(item => item.Kind == "task" && item.State == "active");
            var completed = baseline.Items.Single(item => item.Kind == "task" && item.State == "completed");
            if (active.ChangeLabel != "新要求" || desktop.Snapshot?.Tasks.Single().ChangeLabel != "新要求")
                throw new InvalidOperationException("恢复合成检查没有建立当前新要求状态。");
            var layouts = new DesktopLayoutStore(recoveryRoot);
            layouts.Save(desktop.Layout, initialEnvironment.Scale);
            var baselineLayout = desktop.Layout;
            var layoutPath = Path.Combine(recoveryRoot, "desktop-layout.json");
            var originalLayoutFile = File.ReadAllText(layoutPath);
            var staleComplete = FindButton(desktop.TaskWindow, "complete:" + active.Id);
            var staleOpen = FindButton(desktop.TaskWindow, "open:" + active.Id);
            var staleResize = FindThumb(desktop.TaskWindow);

            // Detach an actual child HWND without destroying it. IsWindow alone cannot detect this.
            var detachedWindow = desktop.TaskWindow;
            var detachedHandle = new WindowInteropHelper(detachedWindow).Handle;
            Marshal.SetLastPInvokeError(0);
            DesktopNative.SetParent(detachedHandle, 0);
            var detachError = Marshal.GetLastPInvokeError();
            var actualParent = DesktopNative.GetParent(detachedHandle);
            if (!DesktopNative.IsWindow(detachedHandle) || actualParent == initialEnvironment.Parent)
                throw new InvalidOperationException($"未能建立仍有有效 HWND 的脱挂卡片：originalParent={initialEnvironment.Parent}, actualParent={actualParent}, error={detachError}。");
            await desktop.RefreshAsync();
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);
            if (ReferenceEquals(detachedWindow, desktop.TaskWindow))
                throw new InvalidOperationException("仍然使用已脱挂的旧卡片。");
            staleComplete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            staleOpen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == active.Id).State != "active"
                || await EvaluateAsync(window, "document.querySelector('#theme-setting') !== null") != "true")
                throw new InvalidOperationException("已释放卡片的旧回调仍然操作了数据或主窗口。");

            var closedWindow = desktop.TaskWindow;
            closedWindow.Close();
            await desktop.RefreshAsync();
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);
            if (ReferenceEquals(closedWindow, desktop.TaskWindow))
                throw new InvalidOperationException("已关闭的卡片未重建。");

            var fingerprint = BackupFingerprint(recoveryRoot, workspace, workspace.Snapshot(DateTimeOffset.UtcNow));
            var taskHandle = new WindowInteropHelper(desktop.TaskWindow).Handle;
            var pendingHandle = new WindowInteropHelper(desktop.PendingWindow).Handle;
            environment.Current = null;
            await desktop.RefreshAsync();
            if (desktop.State != "waiting" || DesktopNative.IsWindow(taskHandle) || DesktopNative.IsWindow(pendingHandle))
                throw new InvalidOperationException("桌面宿主丢失后未等待，或旧卡片仍存活。");
            if (BackupFingerprint(recoveryRoot, workspace, workspace.Snapshot(DateTimeOffset.UtcNow)) != fingerprint)
                throw new InvalidDataException("宿主丢失改变了事项、历史、证据或课程关系。");
            environment.Current = initialEnvironment;
            await desktop.RefreshAsync();
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);
            if (BackupFingerprint(recoveryRoot, workspace, workspace.Snapshot(DateTimeOffset.UtcNow)) != fingerprint
                || desktop.Snapshot?.Tasks.Single().ChangeLabel != "新要求"
                || !desktop.PendingWindow.IsVisible
                || !HasDesktopTheme(desktop.TaskWindow, true)
                || desktop.Layout != baselineLayout || File.ReadAllText(layoutPath) != originalLayoutFile
                || workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == completed.Id).UndoExpiresAt != completed.UndoExpiresAt)
                throw new InvalidDataException("恢复未保留当前任务、新要求、撤销期限、主题或布局。");

            var primary = initialEnvironment.WorkAreas[0];
            var small = new DesktopRect(primary.X + primary.Width * .35, primary.Y + primary.Height * .25,
                Math.Min(primary.Width * .5, 420 * initialEnvironment.Scale),
                Math.Min(primary.Height * .5, 320 * initialEnvironment.Scale));
            environment.Current = initialEnvironment with { WorkAreas = [small] };
            await desktop.RefreshAsync();
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);
            staleResize.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            if (!ContainsRecoveryBounds(small, desktop.Layout.Tasks) || !ContainsRecoveryBounds(small, desktop.Layout.Pending)
                || desktop.Layout == baselineLayout || File.ReadAllText(layoutPath) != originalLayoutFile)
                throw new InvalidDataException("合成工作区域变化未夹入可见区域，或覆盖了原布局文件。");
            environment.Current = initialEnvironment;
            await desktop.RefreshAsync();
            if (desktop.Layout != baselineLayout || File.ReadAllText(layoutPath) != originalLayoutFile)
                throw new InvalidDataException("原工作区域返回后未还原保存位置。");

            var scale = initialEnvironment.Scale * 1.25;
            environment.Current = initialEnvironment with { Scale = scale };
            await desktop.RefreshAsync();
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);
            await WaitAsync(() => Descendants(desktop.TaskWindow).OfType<TextBlock>()
                .Any(text => text.Text.StartsWith(active.Title, StringComparison.Ordinal)));
            var scaledWidth = desktop.Layout.Tasks.Width;
            var expectedScaledWidth = baselineLayout.Tasks.Width * 1.25;
            var layoutFileUnchanged = File.ReadAllText(layoutPath) == originalLayoutFile;
            var taskTexts = Descendants(desktop.TaskWindow).OfType<TextBlock>().Select(text => text.Text).ToArray();
            var titlePresent = taskTexts.Any(text => text.StartsWith(active.Title, StringComparison.Ordinal));
            var hasNativeBounds = GetRecoveryWindowRect(new WindowInteropHelper(desktop.TaskWindow).Handle, out var nativeBounds);
            var nativeWidth = nativeBounds.Right - nativeBounds.Left;
            if (Math.Abs(scaledWidth - expectedScaledWidth) > .01
                || !layoutFileUnchanged || !titlePresent || !hasNativeBounds
                || Math.Abs(nativeWidth - scaledWidth) > 1)
                throw new InvalidDataException($"合成 DPI 变化未重新缩放真实卡片，或丢失文字和原布局："
                    + $"initialScale={initialEnvironment.Scale}, requestedScale={scale}, baselineWidth={baselineLayout.Tasks.Width}, "
                    + $"scaledWidth={scaledWidth}, expectedScaledWidth={expectedScaledWidth}, layoutFileUnchanged={layoutFileUnchanged}, "
                    + $"titlePresent={titlePresent}, hasNativeBounds={hasNativeBounds}, nativeWidth={nativeWidth}, "
                    + $"nativeRect=({nativeBounds.Left},{nativeBounds.Top},{nativeBounds.Right},{nativeBounds.Bottom}), "
                    + $"title={System.Text.Json.JsonSerializer.Serialize(active.Title)}, texts={System.Text.Json.JsonSerializer.Serialize(taskTexts)}。");
            environment.Current = initialEnvironment;
            await desktop.RefreshAsync();

            // The second capture is after the background snapshot await. Lose the environment there.
            environment.ChangeAtCapture = environment.Captures + 2;
            environment.ChangeDuringCapture = () => environment.Current = null;
            runtime.Refresh();
            await desktop.RefreshAsync();
            if (environment.ChangeAtCapture is not null || desktop.State != "waiting")
                throw new InvalidOperationException("跨 await 的环境变化未被核对，或旧快照仍被绘制。");
            environment.Current = initialEnvironment;
            await desktop.RefreshAsync();
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);

            // Close tasks after the final pending attachment check returned true, forcing a render failure.
            environment.ChangeAtAttachmentCheck = environment.AttachmentChecks + 4;
            environment.ChangeAfterAttachmentCheck = () => desktop.TaskWindow.Close();
            desktop.SetAppearance("dark", true);
            runtime.Refresh();
            await desktop.RefreshAsync();
            if (environment.ChangeAtAttachmentCheck is not null)
                throw new InvalidOperationException("未能在快照后的最终附着检查中建立关闭窗口的竞态。");
            await desktop.RefreshAsync();
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);
            await WaitAsync(() => Descendants(desktop.TaskWindow).OfType<TextBlock>()
                .Any(text => text.Text.StartsWith(active.Title, StringComparison.Ordinal)));
            if (Descendants(desktop.TaskWindow).OfType<TextBlock>()
                    .Any(text => text.Text.Contains("读取桌面事项失败", StringComparison.Ordinal))
                || !Descendants(desktop.TaskWindow).OfType<TextBlock>()
                    .Any(text => text.Text.StartsWith(active.Title, StringComparison.Ordinal)))
                throw new InvalidOperationException("成功重建并刷新后仍显示旧读取错误，或未显示当前事项。");

            var current = workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == active.Id);
            const string resumedTitle = "恢复运行后当前数学事项";
            workspace.Execute(new(workspace.AccountId, "edit-task", current.Id, "native-resume-current-task", current.Version,
                Date: current.Due, Time: current.Time, TimeKind: current.TimeKind, Title: resumedTitle, Course: current.Course,
                Priority: current.Priority, Generation: workspace.Generation), DateTimeOffset.UtcNow);
            var capturesBeforeResume = environment.Captures;
            // Deliver the normal Windows resume notification only to this process; the computer does not sleep.
            SendRecoveryMessage(new WindowInteropHelper(window).Handle, 0x0218, 0x0012, 0);
            await WaitAsync(() => environment.Captures > capturesBeforeResume
                && desktop.Snapshot?.Tasks.Single().Title == resumedTitle);
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);

            FindButton(desktop.TaskWindow, "open:" + current.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(window, "document.querySelector('.detail-title')?.textContent === '恢复运行后当前数学事项' && document.querySelector('[data-action=complete]') !== null");
            await EvaluateAsync(window, "document.querySelector('[data-action=complete]').click()");
            await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == current.Id).State == "completed");
            await EvaluateAsync(window, "document.querySelector('[data-action=nav][data-page=completed]').click()");
            await WaitAsync(window, "document.querySelector('.task-row[data-id=" + System.Text.Json.JsonSerializer.Serialize(current.Id) + "]') !== null");
            await EvaluateAsync(window, "document.querySelector('.task-row[data-id=" + System.Text.Json.JsonSerializer.Serialize(current.Id) + "]').click()");
            await WaitAsync(window, "document.querySelector('[data-action=undo-complete]') !== null");
            await EvaluateAsync(window, "document.querySelector('[data-action=undo-complete]').click()");
            await WaitAsync(() => workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == current.Id).State == "active");
            foreach (var review in workspace.Snapshot(DateTimeOffset.UtcNow).Items.Where(item => item.Kind == "review" && item.State == "pending"))
                workspace.Execute(new(workspace.AccountId, "ignore", review.Id, "native-recovery-ignore-" + review.Id,
                    review.Version, Generation: workspace.Generation), DateTimeOffset.UtcNow);
            environment.Current = null;
            await desktop.RefreshAsync();
            environment.Current = initialEnvironment;
            await desktop.RefreshAsync();
            var expected = workspace.Snapshot(DateTimeOffset.UtcNow);
            await WaitAsync(() => desktop.Snapshot?.Tasks.Count == expected.Items.Count(item => item.Kind == "task" && item.State == "active")
                && desktop.Snapshot.Pending.Count == 0 && !desktop.PendingWindow.IsVisible);
            AssertRecoveryAttached(desktop, native, initialEnvironment.Parent);
            if (workspace.Snapshot(DateTimeOffset.UtcNow).Items.Single(item => item.Id == completed.Id).UndoExpiresAt != completed.UndoExpiresAt
                || desktop.Layout != baselineLayout || File.ReadAllText(layoutPath) != originalLayoutFile)
                throw new InvalidDataException("后续恢复延长原撤销期限，或没有还原布局。");
            var capture = Path.Combine(Path.GetDirectoryName(reportFile)!, Path.GetFileNameWithoutExtension(reportFile) + "-desktop-recovery.png");
            await desktop.RefreshAsync();
            desktop.Capture(capture);
            return new(true, desktop.State, desktop.Snapshot!.Tasks.Count, desktop.Snapshot.Pending.Count,
                initialEnvironment.Scale, desktop.Layout, desktop.Inspect(), capture);
        }
        finally
        {
            window.AppearanceChanged -= desktop.SetAppearance;
            window.Exiting = true;
            window.Close();
            await runtime.StopAsync();
        }
    }

    private static void PrepareRecoveryHistory(string dataRoot, AccountWorkspace workspace)
    {
        var now = DateTimeOffset.UtcNow;
        var original = workspace.Snapshot(now).Items.Single(item => item.Kind == "task");
        var store = new AccountTaskStore(dataRoot, workspace.AccountId);
        var task = store.FindTask(original.Id) ?? throw new InvalidDataException("合成恢复事项未保存。");
        var evidence = new MessageKey(workspace.AccountId, "10001", "native-recovery-requirement");
        workspace.Enqueue(new(evidence, "10002", now,
            [new(SegmentKind.Text, "数学作业新增要求：补充实验数据"),
                new(SegmentKind.Quote, "原通知", new(workspace.AccountId, "10001", "1"))]),
            new HashSet<string> { "10001" }, MessageOrigin.Realtime, "native-recovery-requirement", now);
        var assessments = new RuleAssessments(new("明确新要求", true), new("适用于本人", true),
            new("课程已确认", true), new("正式时间明确", true), new("原消息可核对", true), new("引用明确", true));
        const string reviewId = "native-recovery-requirement-review";
        store.SavePending(reviewId, new RuleCandidate(task.Title, RuleAction.NewRequirement, RuleDisposition.Change,
            task.Course, task.Time, null, task.Id, task.Priority, assessments, [], [evidence]),
            "native-recovery-requirement-review-operation", now);
        var requirement = workspace.Snapshot(now).Items.Single(item => item.Id == reviewId);
        workspace.Execute(new(workspace.AccountId, "accept-review", reviewId, "native-recovery-accept-requirement",
            requirement.Version, Date: original.Due, Time: original.Time, TimeKind: original.TimeKind,
            TargetTaskVersion: original.Version, Generation: workspace.Generation), now);
        const string completedId = "native-recovery-completed";
        workspace.Execute(new(workspace.AccountId, "create-task", completedId, "native-recovery-add-completed", 0,
            Date: DateOnly.FromDateTime(now.DateTime).AddDays(5).ToString("yyyy-MM-dd"), TimeKind: "deadline",
            Title: "恢复检查已完成事项", Course: "数学", Priority: 2, Generation: workspace.Generation), now);
        var created = workspace.Snapshot(now).Items.Single(item => item.Id == completedId);
        workspace.Execute(new(workspace.AccountId, "complete", completedId, "native-recovery-complete", created.Version,
            Generation: workspace.Generation), now);
    }

    private static void AssertRecoveryAttached(DesktopCardHost desktop, NativeDesktopEnvironment native, nint parent)
    {
        if (desktop.State != "attached" || desktop.Error is not null
            || !native.IsAttached(new WindowInteropHelper(desktop.TaskWindow).Handle, parent)
            || !native.IsAttached(new WindowInteropHelper(desktop.PendingWindow).Handle, parent))
            throw new InvalidOperationException("重建卡片未正确附着到真实桌面父窗或窗口 styles 不符合要求。");
    }

    private static bool ContainsRecoveryBounds(DesktopRect area, DesktopRect card) =>
        card.X >= area.X - .01 && card.Y >= area.Y - .01
        && card.X + card.Width <= area.X + area.Width + .01
        && card.Y + card.Height <= area.Y + area.Height + .01;

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendRecoveryMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    private static extern bool GetRecoveryWindowRect(nint hwnd, out DesktopNative.Rectangle rectangle);
}
