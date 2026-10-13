using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Windows.IntegrationProbe;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using BrushConverter = System.Windows.Media.BrushConverter;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace MessagePurify.Windows.App;

/// <summary>正式事项的桌面呈现；不调用 Windows 通知或声音。</summary>
public sealed class DesktopCardHost : IDisposable
{
    private readonly AppRuntime runtime;
    private readonly DesktopLayoutStore layouts;
    private readonly Func<string, string, Task> openItem;
    private readonly IDesktopEnvironment environment;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private CardWindow? tasks, pending;
    private nint parent;
    private double scale = 1;
    private string theme = "light";
    private bool refreshing, disposed;
    private int dirty = 1;
    private DateTimeOffset lastRefresh;
    private DesktopCardsSnapshot? current;
    private string? signature, operationError;
    private long? renderedGeneration;
    private DesktopEnvironmentState? attachedEnvironment;
    private long attachmentEpoch, recoveryVersion;
    private bool reloadLayout;
    private Window? watchedWindow;
    private HwndSource? systemSource;
    private uint taskbarCreatedMessage;
    public string State { get; private set; } = "waiting";
    public string? Error { get; private set; }
    public event Action? StatusChanged;
    public DesktopCardsSnapshot? Snapshot => current;
    internal Window TaskWindow => tasks!;
    internal Window PendingWindow => pending!;
    internal DesktopCardLayout Layout => new(tasks!.Bounds, pending!.Bounds);

    public DesktopCardHost(AppRuntime runtime, string dataRoot, Func<string, string, Task> openItem, IDesktopEnvironment? environment = null)
    {
        this.runtime = runtime;
        this.openItem = openItem;
        this.environment = environment ?? new NativeDesktopEnvironment();
        layouts = new(dataRoot);
        runtime.Changed += OnChanged;
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();
    }

    private void OnChanged() => Interlocked.Exchange(ref dirty, 1);

    public void RequestRecovery()
    {
        if (disposed) return;
        if (!timer.Dispatcher.CheckAccess())
        {
            timer.Dispatcher.BeginInvoke(new Action(RequestRecovery));
            return;
        }
        recoveryVersion++;
        reloadLayout = true;
        signature = null;
        OnChanged();
        timer.Dispatcher.BeginInvoke(new Action(async () => await RefreshAsync()));
    }

    public void WatchSystemWindow(Window window)
    {
        UnwatchSystemWindow();
        if (disposed) return;
        watchedWindow = window;
        taskbarCreatedMessage = DesktopNative.RegisterWindowMessage("TaskbarCreated");
        window.SourceInitialized += OnSystemSourceInitialized;
        AttachSystemHook();
    }

    private void OnSystemSourceInitialized(object? sender, EventArgs args) => AttachSystemHook();
    private void AttachSystemHook()
    {
        if (systemSource is not null || watchedWindow is null) return;
        var handle = new WindowInteropHelper(watchedWindow).Handle;
        if (handle == 0) return;
        systemSource = HwndSource.FromHwnd(handle);
        systemSource?.AddHook(OnSystemMessage);
    }
    private nint OnSystemMessage(nint hwnd, int message, nint parameter, nint data, ref bool handled)
    {
        if (DesktopSystemMessages.ShouldRefresh(message, parameter, taskbarCreatedMessage)) RequestRecovery();
        return 0;
    }
    private void UnwatchSystemWindow()
    {
        if (watchedWindow is not null) watchedWindow.SourceInitialized -= OnSystemSourceInitialized;
        systemSource?.RemoveHook(OnSystemMessage);
        systemSource = null;
        watchedWindow = null;
    }

    public void SetAppearance(string theme, bool reducedMotion)
    {
        // Cards do not animate; reduced motion therefore also applies without a second setting.
        this.theme = theme;
        signature = null;
        OnChanged();
    }

    public async Task RefreshAsync()
    {
        if (disposed || refreshing || runtime.IsRestoring) return;
        refreshing = true;
        try
        {
            if (!EnsureAttached()) return;
            var now = DateTimeOffset.UtcNow;
            if (Interlocked.Exchange(ref dirty, 0) == 0 && now - lastRefresh < TimeSpan.FromSeconds(30)) return;
            var workspace = runtime.Workspace;
            var epoch = attachmentEpoch;
            var request = recoveryVersion;
            var snapshot = await Task.Run(() => workspace?.Snapshot(now));
            if (disposed) return;
            if (runtime.IsRestoring || !ReferenceEquals(workspace, runtime.Workspace)
                || snapshot?.Generation != workspace?.Generation || snapshot?.AccountId != workspace?.AccountId
                || request != recoveryVersion) { OnChanged(); return; }
            if (!EnsureAttached() || epoch != attachmentEpoch || request != recoveryVersion) { OnChanged(); return; }
            if (snapshot is not null && renderedGeneration is not null && renderedGeneration != snapshot.Generation)
            {
                var restoredLayout = layouts.Load(WorkAreas(), scale);
                tasks!.SetBounds(restoredLayout.Tasks);
                pending!.SetBounds(restoredLayout.Pending);
                operationError = null;
                signature = null;
            }
            renderedGeneration = snapshot?.Generation;
            current = DesktopCardProjection.Build(snapshot, runtime.Status);
            lastRefresh = now;
            var nextSignature = DesktopCardProjection.RenderSignature(current, theme, operationError, layouts.Error, now);
            if (signature == nextSignature) return;
            signature = nextSignature;
            var generation = snapshot?.Generation ?? 0;
            tasks!.Render(current, false, theme, operationError ?? layouts.Error, now,
                (account, item) => OpenAsync(account, item, generation, epoch), (account, item) => CompleteAsync(account, item, generation, epoch));
            pending!.Render(current, true, theme, operationError ?? layouts.Error, now,
                (account, item) => OpenAsync(account, item, generation, epoch), (account, item) => CompleteAsync(account, item, generation, epoch));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            const string refreshError = "读取桌面事项失败，请打开主界面查看连接与本地数据状态。";
            signature = null;
            OnChanged();
            if (tasks?.Alive == true) tasks.ShowError(refreshError);
        }
        finally { refreshing = false; }
    }

    private bool EnsureAttached()
    {
        var latest = environment.Capture();
        if (latest is null)
        {
            DisposeWindows();
            SetStatus("waiting", "正在等待 Windows 桌面就绪。");
            return false;
        }
        if (!latest.Compatible)
        {
            DisposeWindows();
            SetStatus("unavailable", "桌面显示模式暂不兼容，请在主界面查看事项。");
            return false;
        }
        try
        {
            if (tasks?.Alive == true && pending?.Alive == true && latest.Parent == parent && latest.Scale == scale
                && environment.IsAttached(tasks.Handle, parent) && environment.IsAttached(pending.Handle, parent))
            {
                if (reloadLayout || attachedEnvironment is null || attachedEnvironment.Bounds != latest.Bounds
                    || !attachedEnvironment.WorkAreas.SequenceEqual(latest.WorkAreas))
                {
                    attachedEnvironment = latest;
                    var layout = layouts.Load(latest.WorkAreas, scale);
                    tasks.SetBounds(layout.Tasks);
                    pending.SetBounds(layout.Pending);
                    signature = null;
                    OnChanged();
                }
                reloadLayout = false;
                return true;
            }
            DisposeWindows();
            parent = latest.Parent;
            scale = latest.Scale;
            attachedEnvironment = latest;
            var restored = layouts.Load(WorkAreas(), scale);
            tasks = new("拾要 · 提醒事项", parent, scale, restored.Tasks, WorkAreas, () => SaveLayout(false), RequestRecovery);
            pending = new("拾要 · 待确认", parent, scale, restored.Pending, WorkAreas, () => SaveLayout(true), RequestRecovery);
            if (!environment.IsAttached(tasks.Handle, parent) || !environment.IsAttached(pending.Handle, parent))
                throw new InvalidOperationException("Desktop attachment changed during creation.");
            reloadLayout = false;
            signature = null;
            OnChanged();
            SetStatus("attached", null);
            return true;
        }
        catch (InvalidOperationException)
        {
            DisposeWindows();
            SetStatus("unavailable", "桌面卡片挂载失败，请在主界面查看事项。");
            return false;
        }
    }

    private IReadOnlyList<DesktopRect> WorkAreas() => attachedEnvironment?.WorkAreas
        ?? throw new InvalidOperationException("Desktop work areas unavailable.");

    private void SaveLayout(bool isPending)
    {
        if (runtime.IsRestoring) return;
        if (tasks?.Alive != true || pending?.Alive != true
            || !environment.IsAttached(tasks.Handle, parent) || !environment.IsAttached(pending.Handle, parent))
        {
            RequestRecovery();
            return;
        }
        layouts.SaveCard(Layout, scale, isPending);
        signature = null;
        OnChanged();
    }

    internal void SetLayout(DesktopCardLayout layout)
    {
        tasks!.SetBounds(DesktopPlacement.Fit(layout.Tasks, WorkAreas()));
        pending!.SetBounds(DesktopPlacement.Fit(layout.Pending, WorkAreas()));
        layouts.Save(Layout, scale);
        signature = null;
        OnChanged();
    }

    private bool CanOperate(string accountId, long generation, long epoch) => !disposed && !runtime.IsRestoring
        && epoch == attachmentEpoch && runtime.Workspace?.Generation == generation
        && runtime.Workspace.AccountId == accountId && tasks?.Alive == true && pending?.Alive == true
        && environment.IsAttached(tasks.Handle, parent) && environment.IsAttached(pending.Handle, parent);

    private async Task OpenAsync(string accountId, ApplicationItem item, long generation, long epoch)
    {
        if (!CanOperate(accountId, generation, epoch)) return;
        try { await openItem(accountId, item.Id); }
        catch (InvalidOperationException) { operationError = "主界面尚未就绪，请稍后重试。"; OnChanged(); }
    }

    private async Task CompleteAsync(string accountId, ApplicationItem item, long generation, long epoch)
    {
        if (!CanOperate(accountId, generation, epoch)) return;
        try
        {
            if (runtime.IsRestoring) throw new InvalidOperationException();
            var workspace = runtime.Workspace ?? throw new InvalidOperationException();
            await Task.Run(() => workspace.Execute(new(accountId, "complete", item.Id,
                Guid.NewGuid().ToString("N"), item.Version, Generation: generation), DateTimeOffset.UtcNow));
            operationError = null;
            runtime.Refresh();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            operationError = "事项已有变化或保存未完成，请核对最新内容后重试。";
            OnChanged();
        }
        await RefreshAsync();
    }

    private void SetStatus(string state, string? error)
    {
        if (State == state && Error == error) return;
        State = state; Error = error;
        StatusChanged?.Invoke();
    }

    internal object Inspect() => new { tasks = tasks!.Inspect(), pending = pending!.Inspect() };

    internal void Capture(string path)
    {
        tasks!.UpdateLayout(); pending!.UpdateLayout();
        var first = (FrameworkElement)tasks.Content;
        var second = (FrameworkElement)pending.Content;
        var width = first.ActualWidth + second.ActualWidth + 16;
        var height = Math.Max(first.ActualHeight, second.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(first), null, new Rect(0, 0, first.ActualWidth, first.ActualHeight));
            context.DrawRectangle(new VisualBrush(second), null, new Rect(first.ActualWidth + 16, 0, second.ActualWidth, second.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private void DisposeWindows()
    {
        attachmentEpoch++;
        attachedEnvironment = null;
        signature = null;
        OnChanged();
        tasks?.Dispose(); pending?.Dispose();
        tasks = null; pending = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        runtime.Changed -= OnChanged;
        UnwatchSystemWindow();
        DisposeWindows();
    }

    private sealed class CardWindow : Window, IDisposable
    {
        private readonly nint parent, oldParent, originalStyle;
        private readonly double scale;
        private readonly Func<IReadOnlyList<DesktopRect>> workAreas;
        private readonly Action save;
        private readonly Action recover;
        private readonly Grid content = new();
        private readonly StackPanel list = new();
        private readonly TextBlock heading = new(), footer = new();
        private readonly TextBlock count = new();
        private readonly Border countBadge = new();
        private readonly Border frame;
        private readonly Thumb resize;
        private bool disposed, closed, interactionFailed;
        internal nint Handle { get; }
        internal DesktopRect Bounds { get; private set; }
        internal bool Alive => !closed && !disposed && DesktopNative.IsWindow(Handle);

        public CardWindow(string title, nint parent, double scale, DesktopRect bounds,
            Func<IReadOnlyList<DesktopRect>> workAreas, Action save, Action recover)
        {
            this.parent = parent; this.scale = scale; this.workAreas = workAreas; this.save = save; this.recover = recover;
            Closed += (_, _) => closed = true;
            Title = title; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            Resources.MergedDictionaries.Add(new() { Source = new Uri("/MessagePurify;component/DesktopCardStyles.xaml", UriKind.Relative) });
            Resources[SystemParameters.VerticalScrollBarWidthKey] = 7d;
            ShowInTaskbar = false; ShowActivated = false; Topmost = false;
            Width = bounds.Width / scale; Height = bounds.Height / scale;
            FontFamily = new("Microsoft YaHei UI"); FontSize = 13;
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            content.RowDefinitions.Add(new());
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            heading.Margin = new(0); heading.FontSize = 16; heading.FontWeight = FontWeights.SemiBold;
            heading.Cursor = Cursors.SizeAll;
            var header = new DockPanel { Margin = new(18, 16, 18, 12), LastChildFill = true };
            count.FontSize = 11; count.FontWeight = FontWeights.SemiBold;
            countBadge.Child = count; countBadge.CornerRadius = new(7); countBadge.Padding = new(8, 2, 8, 2);
            countBadge.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(countBadge, Dock.Right); header.Children.Add(countBadge); header.Children.Add(heading);
            content.Children.Add(header);
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(0, 0, 9, 0) };
            list.Margin = new(10, 0, 3, 0); Grid.SetRow(scroll, 1); content.Children.Add(scroll);
            footer.Margin = new(18, 9, 34, 12); footer.FontSize = 10; footer.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(footer, 2); content.Children.Add(footer);
            resize = new Thumb { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, ToolTip = "拖动调整卡片大小",
                Style = (Style)Resources["DesktopResize"] };
            Grid.SetRowSpan(resize, 3); content.Children.Add(resize);
            frame = new Border { Child = content, BorderThickness = new(1), CornerRadius = new(16), Margin = new(5),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = .16 } };
            Content = frame;
            DesktopPoint? initial = null;
            DesktopRect start = default;
            void Interact(Action action)
            {
                if (!Alive) return;
                try { action(); }
                catch (InvalidOperationException)
                {
                    interactionFailed = true; initial = null; heading.ReleaseMouseCapture(); recover();
                }
            }
            heading.MouseLeftButtonDown += (_, args) => Interact(() =>
            {
                interactionFailed = false; initial = CursorInParent(); start = Bounds; heading.CaptureMouse(); args.Handled = true;
            });
            heading.MouseMove += (_, args) =>
            {
                if (initial is not null && args.LeftButton == MouseButtonState.Pressed)
                    Interact(() =>
                    {
                        var cursor = CursorInParent();
                        SetBounds(DesktopPlacement.Fit(start with { X = start.X + cursor.X - initial.Value.X,
                            Y = start.Y + cursor.Y - initial.Value.Y }, workAreas()));
                    });
            };
            heading.MouseLeftButtonUp += (_, args) =>
            {
                var changed = initial is not null; initial = null; heading.ReleaseMouseCapture();
                if (changed && Alive && !interactionFailed) save(); args.Handled = true;
            };
            resize.DragStarted += (_, _) => interactionFailed = false;
            resize.DragDelta += (_, args) => Interact(() => SetBounds(DesktopPlacement.Fit(Bounds with
            {
                Width = Math.Max(260 * scale, Bounds.Width + args.HorizontalChange * scale),
                Height = Math.Max(200 * scale, Bounds.Height + args.VerticalChange * scale)
            }, workAreas())));
            resize.DragCompleted += (_, _) => { if (Alive && !interactionFailed) save(); };
            Handle = new WindowInteropHelper(this).EnsureHandle();
            originalStyle = DesktopNative.GetWindowLongPtr(Handle, -16);
            DesktopNative.SetWindowLongPtr(Handle, -16, (nint)(((long)originalStyle & ~0x80000000L) | 0x40000000L));
            var extended = (long)DesktopNative.GetWindowLongPtr(Handle, -20);
            DesktopNative.SetWindowLongPtr(Handle, -20, (nint)((extended | 0x08000000L | 0x80L) & ~8L));
            Marshal.SetLastPInvokeError(0);
            oldParent = DesktopNative.SetParent(Handle, parent);
            if (oldParent == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                DesktopNative.SetWindowLongPtr(Handle, -16, originalStyle);
                Close();
                throw new InvalidOperationException("Desktop attachment failed.");
            }
            try { Show(); SetBounds(bounds); }
            catch (InvalidOperationException) { Dispose(); throw; }
        }

        internal void SetBounds(DesktopRect bounds)
        {
            if (!Alive || !DesktopNative.IsWindow(parent) || DesktopNative.GetParent(Handle) != parent
                || !DesktopNative.SetWindowPos(Handle, 0, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
                    (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 4 | 0x10 | 0x20))
                throw new InvalidOperationException("Desktop positioning failed.");
            Bounds = bounds;
        }

        private DesktopPoint CursorInParent()
        {
            if (!Alive || !DesktopNative.IsWindow(parent) || !DesktopNative.GetCursorPos(out var point)
                || !DesktopNative.ScreenToClient(parent, ref point))
                throw new InvalidOperationException("Desktop pointer unavailable.");
            return new(point.X, point.Y);
        }

        internal void ShowError(string error) => footer.Text = error;

        internal void Render(DesktopCardsSnapshot snapshot, bool isPending, string theme, string? error, DateTimeOffset now,
            Func<string, ApplicationItem, Task> open, Func<string, ApplicationItem, Task> complete)
        {
            var dark = theme == "dark";
            Brush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            var background = Brush(dark ? "#CD182532" : "#DEF4F7FA");
            var text = Brush(dark ? "#F3F6FC" : "#182A3B");
            var muted = Brush(dark ? "#CFD8E5" : "#485D70");
            var line = Brush(dark ? "#36D1DDED" : "#99FFFFFF");
            var accent = Brush(dark ? "#A8DEC8" : "#236754");
            Foreground = text; frame.Background = background; frame.BorderBrush = line;
            heading.Foreground = text; footer.Foreground = muted;
            Resources["DesktopScrollBrush"] = Brush(dark ? "#88D5DFEA" : "#88627A90");
            count.Foreground = accent; countBadge.Background = Brush(dark ? "#208FCAB5" : "#17236754");
            var items = isPending ? snapshot.Pending : snapshot.Tasks;
            heading.Text = isPending ? "待确认" : "提醒事项";
            count.Text = items.Count.ToString(); countBadge.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            frame.VerticalAlignment = items.Count == 0 && !isPending ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            frame.Height = items.Count == 0 && !isPending ? Math.Min(154, Math.Max(0, Height - 10)) : double.NaN;
            var previousScroll = ((ScrollViewer)content.Children[1]).VerticalOffset;
            list.Children.Clear();
            if (items.Count == 0)
                list.Children.Add(new TextBlock { Text = snapshot.AccountId is null ? "等待账号连接，已有事项将保留" : "暂无待办\n重要的事出现时，会留在这里。",
                    Margin = new(8, 10, 8, 10), LineHeight = 23, Foreground = muted, TextWrapping = TextWrapping.Wrap });
            foreach (var item in items)
            {
                var body = new StackPanel { Margin = new(10, 10, 10, 11) };
                var past = DesktopCardProjection.IsPast(item, now);
                var highlighted = !isPending && (past || item.Priority is 0 or 1);
                var normalRow = Brush(highlighted ? dark ? "#602D5147" : "#AADEEEE6" : dark ? "#09FFFFFF" : "#44FFFFFF");
                var row = new Border { Child = body, CornerRadius = new(10), BorderThickness = new(highlighted ? 1 : 0),
                    BorderBrush = highlighted ? Brush(dark ? "#7795C9B6" : "#6681AC9B") : Brushes.Transparent,
                    Background = normalRow, Margin = new(0, 0, 0, 4) };
                row.MouseEnter += (_, _) => row.Background = Brush(dark ? "#245A758A" : "#AAFFFFFF");
                row.MouseLeave += (_, _) => row.Background = normalRow;
                var title = item.Title + (item.Unread ? "  ●" : "");
                var details = new Button { Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold, FontSize = 14, MaxHeight = 66 }, Foreground = text,
                    Background = Brushes.Transparent, BorderThickness = new(0), HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new(0), Cursor = Cursors.Hand, Tag = "open:" + item.Id };
                details.Click += async (_, _) => await open(snapshot.AccountId!, item);
                body.Children.Add(details);
                if (!string.IsNullOrEmpty(item.ChangeLabel))
                    body.Children.Add(new TextBlock { Text = item.ChangeLabel, Foreground = accent,
                        FontSize = 11, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                        Margin = new(0, 6, 0, 0) });
                body.Children.Add(new TextBlock { Text = item.Source == "manual" ? (item.Course ?? "未分类") + " · 手动添加" : item.Course ?? item.Group, Foreground = muted,
                    FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 6, 0, 4) });
                var label = isPending ? "核对原文，补齐待确认信息" :
                    $"P{item.Priority ?? 3}  ·  {(past ? item.TimeKind == "start" ? "开始时间已过" : "已逾期" : item.TimeKind == "start" ? "开始" : "截止")}  {item.Due}{(item.Time is null ? " · 仅有日期" : " " + item.Time)}";
                body.Children.Add(new TextBlock { Text = label, Foreground = highlighted ? accent : muted,
                    FontSize = 11, TextWrapping = TextWrapping.Wrap });
                if (!isPending)
                {
                    var done = new Button { Content = "完成", Tag = "complete:" + item.Id, Padding = new(10, 4, 10, 4),
                        HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0),
                        Background = Brush(dark ? "#30477B67" : "#DCEDE5"), Foreground = accent, BorderThickness = new(0), Cursor = Cursors.Hand };
                    done.Click += async (_, _) => { done.IsEnabled = false; await complete(snapshot.AccountId!, item); };
                    body.Children.Add(done);
                }
                list.Children.Add(row);
            }
            ((ScrollViewer)content.Children[1]).ScrollToVerticalOffset(previousScroll);
            footer.Text = error ?? snapshot.ConnectionText;
            if (isPending && items.Count == 0) Hide(); else if (!IsVisible) { Show(); SetBounds(Bounds); }
        }

        internal object Inspect() => new { parentClass = DesktopNative.ClassName(DesktopNative.GetParent(Handle)),
            child = ((long)DesktopNative.GetWindowLongPtr(Handle, -16) & 0x40000000L) != 0,
            noActivate = ((long)DesktopNative.GetWindowLongPtr(Handle, -20) & 0x08000000L) != 0,
            topmost = ((long)DesktopNative.GetWindowLongPtr(Handle, -20) & 8L) != 0,
            visible = IsVisible, bounds = Bounds, transparent = AllowsTransparency,
            surfaceHeight = frame.ActualHeight, surfaceWidth = frame.ActualWidth };

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!closed && DesktopNative.IsWindow(Handle))
            {
                Hide(); DesktopNative.SetParent(Handle, oldParent); DesktopNative.SetWindowLongPtr(Handle, -16, originalStyle);
            }
            if (!closed) Close();
        }
    }
}

internal static class DesktopNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rectangle { public int Left, Top, Right, Bottom; }
    internal delegate bool EnumCallback(nint hwnd, nint parameter);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder buffer, int capacity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rectangle rectangle);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
    internal static string ClassName(nint hwnd) { var buffer = new StringBuilder(256); GetClassName(hwnd, buffer, buffer.Capacity); return buffer.ToString(); }
    internal static nint FindDesktopParent()
    {
        nint parent = 0;
        EnumWindows((handle, _) =>
        {
            if (ClassName(handle) is "Progman" or "WorkerW") parent = FindWindowEx(handle, 0, "SHELLDLL_DefView", null);
            return parent == 0;
        }, 0);
        return parent;
    }
}
