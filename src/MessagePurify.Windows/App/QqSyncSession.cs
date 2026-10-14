using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MessagePurify.Windows.App;

public interface IQqSyncSession : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);
    Task RestoreAsync();
}

/// <summary>一次同步拥有一次 Framework 会话，退出或失败后切回普通 QQ。</summary>
public sealed class QqSyncSession : IQqSyncSession
{
    private readonly IQqSyncProcessAdapter processes;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool restoreNeeded;
    private int started;

    public QqSyncSession() : this(QqSyncConfiguration.Load(AppContext.BaseDirectory)) { }

    internal QqSyncSession(QqSyncConfiguration configuration) : this(new WindowsQqSyncProcessAdapter(
        configuration.QqExecutable, configuration.PackageDirectory, configuration.PrepareScript)) { }

    internal QqSyncSession(IQqSyncProcessAdapter processes) => this.processes = processes;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("QQ 同步会话已启动。");
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Prepare the bridge before closing QQ, so missing files cannot interrupt chatting.
            await processes.ValidateAndPrepareAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            processes.CaptureOrdinaryQq();
            restoreNeeded = true;
            await processes.StopOrdinaryQqAsync(cancellationToken);
            await processes.StartFrameworkAsync(cancellationToken);
        }
        catch
        {
            await RestoreCoreAsync();
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task RestoreAsync()
    {
        await gate.WaitAsync();
        try { await RestoreCoreAsync(); }
        finally { gate.Release(); }
    }

    private async Task RestoreCoreAsync()
    {
        if (!restoreNeeded) return;
        await processes.StopFrameworkAsync();
        await processes.StartOrdinaryQqAsync();
        restoreNeeded = false;
    }

    public ValueTask DisposeAsync() => new(RestoreAsync());
}

internal interface IQqSyncProcessAdapter
{
    Task ValidateAndPrepareAsync(CancellationToken cancellationToken);
    void CaptureOrdinaryQq();
    Task StopOrdinaryQqAsync(CancellationToken cancellationToken);
    Task StartFrameworkAsync(CancellationToken cancellationToken);
    Task StopFrameworkAsync();
    Task StartOrdinaryQqAsync();
}

internal sealed class WindowsQqSyncProcessAdapter(string qqExecutable, string packageDirectory, string prepareScript)
    : IQqSyncProcessAdapter
{
    private sealed record ProcessIdentity(int Id, DateTime StartedUtc, string Path, int ParentId);
    private readonly string qqPath = Path.GetFullPath(qqExecutable);
    private readonly string packagePath = Path.GetFullPath(packageDirectory);
    private readonly string preparePath = Path.GetFullPath(prepareScript);
    private readonly ConcurrentDictionary<int, ProcessIdentity> ownedQq = new();
    private IReadOnlyList<ProcessIdentity> ordinaryQq = [];
    private Process? launcher;
    private ProcessIdentity? launcherIdentity;

    public async Task ValidateAndPrepareAsync(CancellationToken cancellationToken)
    {
        var prepareDirectory = Path.GetDirectoryName(preparePath)!;
        foreach (var dependency in new[] { qqPath, preparePath,
            Path.Combine(packagePath, "napimain.exe"), Path.Combine(packagePath, "napiloader.dll"),
            Path.Combine(packagePath, "napcat.mjs"), Path.Combine(prepareDirectory, "nativeLoaderPinned.cjs"),
            Path.Combine(prepareDirectory, "pinned-groups.mjs") })
            if (!File.Exists(dependency)) throw new FileNotFoundException("QQ 同步启动文件缺失，原 QQ 保持运行。", dependency);
        var info = HiddenProcess("powershell.exe", prepareDirectory);
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", preparePath, "-PackageDirectory", packagePath })
            info.ArgumentList.Add(argument);
        using var preparation = Process.Start(info) ?? throw new IOException("无法准备 QQ 只读接口。");
        preparation.BeginOutputReadLine();
        preparation.BeginErrorReadLine();
        try { await preparation.WaitForExitAsync(cancellationToken); }
        catch
        {
            if (!preparation.HasExited) preparation.Kill();
            await preparation.WaitForExitAsync();
            throw;
        }
        if (preparation.ExitCode != 0) throw new IOException("QQ 只读接口准备失败，原 QQ 保持运行。");
        foreach (var file in new[] { "nativeLoaderPinned.cjs", "pinned-groups.mjs", "messagepurify-napcat.mjs" })
            if (!File.Exists(Path.Combine(packagePath, file))) throw new FileNotFoundException("QQ 置顶群接口未就绪。", file);
    }

    public void CaptureOrdinaryQq() => ordinaryQq = Snapshot().Where(IsSpecifiedQq).ToArray();

    public Task StopOrdinaryQqAsync(CancellationToken cancellationToken) => StopAsync(ordinaryQq, cancellationToken);

    public async Task StartFrameworkAsync(CancellationToken cancellationToken)
    {
        // A QQ opened during the switch must not be injected or terminated as our new session.
        if (Snapshot().Any(IsSpecifiedQq)) throw new IOException("QQ 在同步启动期间重新打开，已停止本次切换。");
        var info = HiddenProcess(Path.Combine(packagePath, "napimain.exe"), packagePath);
        info.RedirectStandardInput = true;
        foreach (var argument in new[] { qqPath, Path.Combine(packagePath, "napiloader.dll"), Path.Combine(packagePath, "nativeLoaderPinned.cjs") })
            info.ArgumentList.Add(argument);
        launcher = Process.Start(info) ?? throw new IOException("无法启动 QQ 同步环境。");
        var launched = new ProcessIdentity(launcher.Id, launcher.StartTime.ToUniversalTime(), Path.GetFullPath(info.FileName), 0);
        launcherIdentity = launched;
        launcher.OutputDataReceived += (_, args) =>
        {
            // The launcher reports its created child PID; never retain raw QQ logs or credentials.
            var line = args.Data?.Trim();
            if (line?.StartsWith("进程 PID:", StringComparison.Ordinal) == true
                && int.TryParse(line["进程 PID:".Length..].Trim(), out var id))
            {
                try
                {
                    using var child = Process.GetProcessById(id);
                    if (ReadIdentity(child, launched.Id) is { } identity && IsSpecifiedQq(identity)
                        && identity.StartedUtc >= launched.StartedUtc) ownedQq[id] = identity;
                }
                catch (ArgumentException) { }
            }
        };
        launcher.BeginOutputReadLine();
        launcher.BeginErrorReadLine();
        await launcher.StandardInput.WriteLineAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureFrameworkQq();
            if (ownedQq.Values.Any(IsAlive)) return;
            if (launcher.HasExited || DateTime.UtcNow >= deadline) throw new IOException("QQ 同步环境未能启动。");
            await Task.Delay(100, cancellationToken);
        }
    }

    public async Task StopFrameworkAsync()
    {
        CaptureFrameworkQq();
        await StopAsync(ownedQq.Values.ToArray(), CancellationToken.None);
        if (launcherIdentity is not null) await StopAsync([launcherIdentity], CancellationToken.None);
        launcher?.Dispose();
        launcher = null;
        launcherIdentity = null;
        ownedQq.Clear();
    }

    public Task StartOrdinaryQqAsync()
    {
        // Preserve a normal QQ the user opened during synchronization.
        if (!Snapshot().Any(IsSpecifiedQq))
        {
            using var normal = Process.Start(new ProcessStartInfo(qqPath)
            {
                UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(qqPath)!
            }) ?? throw new IOException("无法恢复普通 QQ。");
        }
        return Task.CompletedTask;
    }

    private bool IsSpecifiedQq(ProcessIdentity identity) => string.Equals(identity.Path, qqPath, StringComparison.OrdinalIgnoreCase);

    private void CaptureFrameworkQq()
    {
        if (launcherIdentity is null) return;
        var snapshot = Snapshot();
        var parents = snapshot.ToDictionary(item => item.Id, item => item.ParentId);
        var anchors = ownedQq.Values.Where(CanAnchor).Select(identity => identity.Id).ToHashSet();
        if (CanAnchor(launcherIdentity)) anchors.Add(launcherIdentity.Id);
        foreach (var process in snapshot.Where(IsSpecifiedQq))
        {
            if (process.StartedUtc < launcherIdentity.StartedUtc) continue;
            var current = process.Id;
            var visited = new HashSet<int>();
            while (visited.Add(current))
            {
                if (anchors.Contains(current)) { ownedQq[process.Id] = process; break; }
                if (!parents.TryGetValue(current, out current)) break;
            }
        }
    }

    private static ProcessStartInfo HiddenProcess(string executable, string directory) => new(executable)
    {
        WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
    };

    private static async Task StopAsync(IReadOnlyList<ProcessIdentity> identities, CancellationToken cancellationToken)
    {
        foreach (var identity in identities)
        {
            using var process = FindMatching(identity);
            if (process is null) continue;
            try { process.CloseMainWindow(); }
            catch (InvalidOperationException) when (process.HasExited) { }
        }
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (identities.Any(IsAlive) && DateTime.UtcNow < deadline)
            await Task.Delay(100, cancellationToken);
        foreach (var identity in identities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = FindMatching(identity);
            if (process is null) continue;
            // QQ's WM_CLOSE may hide to its tray. Authorized fallback targets only captured identities.
            try { process.Kill(); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception && process.HasExited) { }
            await process.WaitForExitAsync(cancellationToken);
        }
        if (identities.Any(IsAlive)) throw new IOException("QQ 未完全退出，已停止环境切换。");
    }

    private static bool IsAlive(ProcessIdentity identity)
    {
        using var process = FindMatching(identity);
        return process is not null;
    }

    private static bool CanAnchor(ProcessIdentity identity)
    {
        // Children retain the original parent PID after it exits. A reused PID must not own a new QQ.
        try
        {
            using var process = Process.GetProcessById(identity.Id);
            return !process.HasExited && process.StartTime.ToUniversalTime() == identity.StartedUtc
                && string.Equals(process.MainModule?.FileName, identity.Path, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return true; }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception) { return false; }
    }

    private static Process? FindMatching(ProcessIdentity identity)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(identity.Id);
            if (!process.HasExited && process.StartTime.ToUniversalTime() == identity.StartedUtc
                && string.Equals(process.MainModule?.FileName, identity.Path, StringComparison.OrdinalIgnoreCase)) return process;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { }
        process?.Dispose();
        return null;
    }

    private static ProcessIdentity? ReadIdentity(Process process, int parentId)
    {
        try
        {
            return process.HasExited || process.MainModule?.FileName is not { } path ? null
                : new(process.Id, process.StartTime.ToUniversalTime(), path, parentId);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception) { return null; }
    }

    private static IReadOnlyList<ProcessIdentity> Snapshot()
    {
        var parents = ParentIds();
        var result = new List<ProcessIdentity>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
                if (ReadIdentity(process, parents.GetValueOrDefault(process.Id)) is { } identity) result.Add(identity);
        }
        return result;
    }

    private static Dictionary<int, int> ParentIds()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new nint(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snapshot, ref entry))
                do { result[(int)entry.ProcessId] = (int)entry.ParentProcessId; } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriorityClass;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
