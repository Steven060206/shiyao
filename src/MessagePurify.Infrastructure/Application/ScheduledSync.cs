using System.Diagnostics;
using System.Threading.Channels;

namespace MessagePurify.Infrastructure.Application;

public enum SyncRequestKind
{
    Startup,
    Scheduled,
    CatchUp
}

public sealed record SyncRequest(SyncRequestKind Kind, DateTimeOffset SlotAt, DateTimeOffset RequestedAt);

public interface IGameActivityDetector
{
    bool IsReady => true;
    bool IsGameRunning();
}

/// <summary>按用户配置的进程名判断游戏是否仍在运行；不把全屏状态误当成完整游戏检测。</summary>
public sealed class ProcessGameActivityDetector : IGameActivityDetector
{
    private readonly string[] processNames;

    public ProcessGameActivityDetector(IEnumerable<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        this.processNames = processNames
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> ProcessNames => processNames;

    public bool IsGameRunning()
    {
        foreach (var name in processNames)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0) return true;
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return false;
    }

    private static string Normalize(string value)
    {
        value = value.Trim();
        value = value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value[..^4].Trim()
            : value;
        return value.ToLowerInvariant();
    }
}

/// <summary>把三个北京时间同步点和游戏期间的延迟合并成一次请求。</summary>
public sealed class ScheduledSyncPlanner
{
    private readonly TimeZoneInfo timeZone;
    private DateTimeOffset? lastRequestedSlot;
    private DateTimeOffset? deferredSlot;
    private bool started;

    public ScheduledSyncPlanner(TimeZoneInfo timeZone)
    {
        this.timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
    }

    public SyncRequest? Evaluate(DateTimeOffset now, bool gameActive)
    {
        var due = LatestSlotAtOrBefore(now);
        if (!started)
        {
            started = true;
            if (gameActive)
            {
                deferredSlot = due;
                return null;
            }

            // A startup batch covers past slots without satisfying a future fixed point.
            lastRequestedSlot = due;
            return new(SyncRequestKind.Startup, due, now);
        }

        if (gameActive)
        {
            if (lastRequestedSlot is null || due > lastRequestedSlot)
                deferredSlot = due;
            return null;
        }

        if (deferredSlot is not null)
        {
            var slot = due > deferredSlot.Value ? due : deferredSlot.Value;
            deferredSlot = null;
            lastRequestedSlot = slot;
            return new(SyncRequestKind.CatchUp, slot, now);
        }

        if (lastRequestedSlot is null || due > lastRequestedSlot)
        {
            lastRequestedSlot = due;
            return new(SyncRequestKind.Scheduled, due, now);
        }

        return null;
    }

    public DateTimeOffset NextSlotAfter(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        var date = DateOnly.FromDateTime(local.DateTime);
        foreach (var clock in FixedTimes)
        {
            var candidate = At(date, clock);
            if (candidate > instant) return candidate;
        }
        return At(date.AddDays(1), FixedTimes[0]);
    }

    private DateTimeOffset LatestSlotAtOrBefore(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        var date = DateOnly.FromDateTime(local.DateTime);
        foreach (var clock in FixedTimes.Reverse())
        {
            var candidate = At(date, clock);
            if (candidate <= instant) return candidate;
        }
        return At(date.AddDays(-1), FixedTimes[^1]);
    }

    private DateTimeOffset At(DateOnly date, TimeOnly clock)
    {
        var local = date.ToDateTime(clock, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private static readonly TimeOnly[] FixedTimes =
    [
        new(8, 0),
        new(12, 0),
        new(20, 0)
    ];
}

/// <summary>常驻 QQ/NapCat 连接与消息宿主之间的异步同步请求队列。</summary>
public sealed class ResidentSyncController
{
    private readonly object gate = new();
    private readonly IGameActivityDetector? gameDetector;
    private SyncRequest? active;
    private DateTimeOffset? catchUpCoveredThrough;
    private readonly Channel<SyncRequest> requests = Channel.CreateUnbounded<SyncRequest>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public ResidentSyncController(IGameActivityDetector? gameDetector = null) => this.gameDetector = gameDetector;

    public bool GameActive => gameDetector?.IsGameRunning() == true;
    public bool CanSync => gameDetector?.IsReady != false && !GameActive;
    public string PauseReason => gameDetector?.IsReady == false ? "game-catalog-unavailable" : "game-active";

    public void Request(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (!IsCoveredCatchUp(request)) requests.Writer.TryWrite(request);
        }
    }

    public async ValueTask<SyncRequest> WaitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (active is not null) return MergePendingCore(active);
        }
        var request = await requests.Reader.ReadAsync(cancellationToken);
        lock (gate)
        {
            active = request;
            return MergePendingCore(request);
        }
    }

    /// <summary>游戏结束后，当前批次覆盖等待期间的多个同步点。</summary>
    public SyncRequest CoalescePending(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (active != request) throw new InvalidOperationException("同步请求已变化。");
            return MergePendingCore(request);
        }
    }

    public void Acknowledge(SyncRequest request, bool includeCatchUpRequests = false,
        DateTimeOffset? completedAt = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (active != request) return;
            if (includeCatchUpRequests)
            {
                var completed = completedAt ?? DateTimeOffset.UtcNow;
                if (catchUpCoveredThrough is null || completed > catchUpCoveredThrough)
                    catchUpCoveredThrough = completed;
            }
            active = null;
            SyncRequest? next = null;
            while (requests.Reader.TryRead(out var queued))
                if (queued.SlotAt > request.SlotAt && !IsCoveredCatchUp(queued))
                    next = next is null ? queued : Latest(next, queued);
            if (next is not null) requests.Writer.TryWrite(next);
        }
    }

    private SyncRequest MergePendingCore(SyncRequest request)
    {
        while (requests.Reader.TryRead(out var queued)) request = Latest(request, queued);
        active = request;
        return request;
    }

    private static SyncRequest Latest(SyncRequest first, SyncRequest second) =>
        second.SlotAt > first.SlotAt || second.SlotAt == first.SlotAt && second.RequestedAt > first.RequestedAt
            ? second : first;

    // The scheduler can observe game exit after the host completes its resumed batch.
    // Its late catch-up request is covered too; ordinary scheduled requests remain valid.
    private bool IsCoveredCatchUp(SyncRequest request) => request.Kind == SyncRequestKind.CatchUp
        && catchUpCoveredThrough is not null && request.SlotAt <= catchUpCoveredThrough.Value;

    public void Complete() { lock (gate) requests.Writer.TryComplete(); }
}

public sealed class ScheduledSyncRunner
{
    private readonly ResidentSyncController controller;
    private readonly ScheduledSyncPlanner planner;
    private readonly IGameActivityDetector gameDetector;
    private readonly Func<DateTimeOffset> now;
    private readonly TimeSpan pollInterval;

    public ScheduledSyncRunner(ResidentSyncController controller, ScheduledSyncPlanner planner,
        IGameActivityDetector gameDetector, Func<DateTimeOffset>? now = null,
        TimeSpan? pollInterval = null)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.gameDetector = gameDetector ?? throw new ArgumentNullException(nameof(gameDetector));
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        if (this.pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var current = now();
            var request = planner.Evaluate(current, !gameDetector.IsReady || gameDetector.IsGameRunning());
            if (request is not null) controller.Request(request);
            try { await Task.Delay(pollInterval, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }
}
