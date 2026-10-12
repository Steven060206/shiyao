using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MessagePurify.Infrastructure.Application;

public sealed record InstalledGame(string Name, string InstallDirectory, IReadOnlyList<string> ExecutablePaths,
    bool IsMinecraft = false);
public sealed record RunningGameProcess(int ProcessId, string ProcessName, string? ExecutablePath, bool IsMinecraft = false);
public sealed record GameCatalogSnapshot(IReadOnlyList<InstalledGame> Games, DateTimeOffset? LastScannedAt, string? StatusMessage);
public sealed record GameInstallLocation(string Name, string Directory);
public sealed record GameScanLocations(IReadOnlyList<string> SteamRoots, IReadOnlyList<GameInstallLocation> RegisteredGames,
    IReadOnlyList<string> GameRoots, IReadOnlyList<string> MinecraftRoots, string? EpicManifestsDirectory = null);

/// <summary>仅扫描平台库和游戏安装目录；进程检测与每日目录扫描分开运行。</summary>
public sealed class InstalledGameCatalog : IGameActivityDetector
{
    private readonly string cachePath;
    private readonly Func<IReadOnlyList<InstalledGame>> discover;
    private readonly Func<IReadOnlyList<RunningGameProcess>> processSnapshot;
    private readonly SemaphoreSlim scanning = new(1, 1);
    private GameCatalogSnapshot snapshot = new([], null, null);
    private bool ready;
    private bool scannedThisRun;
    private readonly object processGate = new();
    private long lastProcessSample;
    private IReadOnlyList<RunningGameProcess> cachedProcesses = [];
    private readonly Dictionary<(int Id, long Started), bool> javaGames = new();

    public InstalledGameCatalog(string cachePath, Func<IReadOnlyList<InstalledGame>>? discover = null,
        Func<IReadOnlyList<RunningGameProcess>>? processSnapshot = null)
    {
        this.cachePath = Path.GetFullPath(cachePath);
        this.discover = discover ?? DiscoverInstalledGames;
        this.processSnapshot = processSnapshot ?? SampleProcesses;
        try
        {
            if (File.Exists(this.cachePath))
            {
                var saved = JsonSerializer.Deserialize<GameCatalogSnapshot>(File.ReadAllText(this.cachePath));
                if (saved is { Games: not null, LastScannedAt: not null })
                {
                    snapshot = saved;
                    ready = true;
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        { snapshot = new([], null, "game-catalog-unavailable"); }
    }

    public bool IsReady => Volatile.Read(ref ready);
    public GameCatalogSnapshot Snapshot => Volatile.Read(ref snapshot);

    public bool IsGameRunning()
    {
        var games = Snapshot.Games;
        if (games.Count == 0) return false;
        IReadOnlyList<RunningGameProcess> processes;
        try { processes = processSnapshot(); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { return true; }
        foreach (var process in processes)
        {
            if (process.ProcessId <= 4) continue;
            if (process.IsMinecraft && games.Any(game => game.IsMinecraft)) return true;
            foreach (var game in games)
            {
                if (process.ExecutablePath is not null)
                {
                    if (game.ExecutablePaths.Contains(process.ExecutablePath, StringComparer.OrdinalIgnoreCase)) return true;
                }
                else if (game.ExecutablePaths.Any(path => string.Equals(Path.GetFileNameWithoutExtension(path),
                             process.ProcessName, StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        return false;
    }

    public async Task<bool> RefreshIfDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await scanning.WaitAsync(cancellationToken);
        try
        {
            if (scannedThisRun && Snapshot.LastScannedAt is { } previous && now - previous < TimeSpan.FromDays(1)) return false;
            // A cached list permits game detection even before this session's first scan.
            if (IsGameRunning()) return false;
            try
            {
                var found = await Task.Run(discover, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var current = new GameCatalogSnapshot(found.DistinctBy(game => game.InstallDirectory,
                    StringComparer.OrdinalIgnoreCase).OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase).ToArray(), now, null);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var temporary = cachePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, cachePath, true);
                Volatile.Write(ref snapshot, current);
                Volatile.Write(ref ready, true);
                scannedThisRun = true;
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
                or System.Security.SecurityException or System.ComponentModel.Win32Exception)
            {
                Volatile.Write(ref snapshot, Snapshot with { StatusMessage = "game-catalog-unavailable" });
                return false; // Keep the last valid list; retry on the next background check.
            }
        }
        finally { scanning.Release(); }
    }

    public static IReadOnlyList<InstalledGame> Scan(GameScanLocations locations)
    {
        var found = new Dictionary<string, InstalledGame>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string directory, bool minecraft = false)
        {
            if (IsNonGameName(name) || !Directory.Exists(directory)) return;
            directory = Path.GetFullPath(directory);
            if (Directory.Exists(Path.Combine(directory, "steamapps"))) return;
            if (found.ContainsKey(directory)) return;
            var executables = minecraft ? [] : FindGameExecutables(directory);
            if (!minecraft && executables.Count == 0) return;
            found[directory] = new(name, directory, executables, minecraft);
        }
        var libraries = new HashSet<string>(locations.SteamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (var root in locations.SteamRoots)
        {
            var folders = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(folders)) continue;
            foreach (Match path in Regex.Matches(File.ReadAllText(folders), "\"path\"\\s*\"([^\"]+)\""))
                libraries.Add(path.Groups[1].Value.Replace("\\\\", "\\"));
        }
        foreach (var root in libraries)
        {
            var apps = Path.Combine(root, "steamapps");
            foreach (var manifest in Files(apps, "appmanifest_*.acf"))
            {
                var text = File.ReadAllText(manifest);
                var name = VdfValue(text, "name");
                var folder = VdfValue(text, "installdir");
                if (name is not null && folder is not null)
                {
                    var common = Path.GetFullPath(Path.Combine(apps, "common"));
                    var install = Path.GetFullPath(Path.Combine(common, folder));
                    if (IsWithin(install, common)) Add(name, install);
                }
            }
            // Playable directories left in a library also count, even if its manifest is missing.
            foreach (var directory in Directories(Path.Combine(apps, "common"))) Add(Path.GetFileName(directory), directory);
        }
        foreach (var install in locations.RegisteredGames) Add(install.Name, install.Directory);
        foreach (var root in locations.GameRoots)
        {
            foreach (var directory in Directories(root))
            {
                if (Path.GetFileName(directory).Equals("rail_apps", StringComparison.OrdinalIgnoreCase))
                    foreach (var game in Directories(directory)) Add(Path.GetFileName(game), game);
                else Add(Path.GetFileName(directory), directory);
            }
        }
        foreach (var root in locations.MinecraftRoots)
            if (Directories(Path.Combine(root, "versions")).Any()) Add("Minecraft", root, minecraft: true);
        if (locations.EpicManifestsDirectory is { } epic)
        {
            foreach (var manifest in Files(epic, "*.item"))
            {
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(manifest));
                    if (json.RootElement.TryGetProperty("InstallLocation", out var directory)
                        && json.RootElement.TryGetProperty("DisplayName", out var name)) Add(name.GetString()!, directory.GetString()!);
                }
                catch (JsonException) { }
            }
        }
        return found.Values.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<InstalledGame> DiscoverInstalledGames()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registered = new List<GameInstallLocation>();
        var gameRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var minecraftRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var registry = RegistryKey.OpenBaseKey(hive, view);
            using (var steam = registry.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                if ((steam?.GetValue("SteamPath") ?? steam?.GetValue("InstallPath")) is string path) steamRoots.Add(path);
            using var uninstall = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var key in uninstall.GetSubKeyNames())
            {
                using var app = uninstall.OpenSubKey(key);
                var name = app?.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(name) || IsNonGameName(name)) continue;
                var publisher = app?.GetValue("Publisher") as string ?? "";
                var path = app?.GetValue("InstallLocation") as string;
                // Steam's uninstaller points to Steam.exe rather than the game directory.
                // Missing install paths are recovered from its manifests/library directories.
                if (key.StartsWith("Steam App", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(path)) continue;
                if (string.IsNullOrWhiteSpace(path))
                {
                    var executable = ExecutableFromText(app?.GetValue("UninstallString") as string)
                        ?? ExecutableFromText(app?.GetValue("DisplayIcon") as string);
                    path = executable is null ? null : Path.GetDirectoryName(executable);
                }
                if (string.IsNullOrWhiteSpace(path)) continue;
                var known = key.StartsWith("Steam App", StringComparison.OrdinalIgnoreCase)
                    || Regex.IsMatch(publisher, "Blizzard|Rockstar|Ubisoft|CD PROJEKT|GOG|Riot", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(name, "守望先锋|英雄联盟|无畏契约|三角洲行动|魔兽|星际|炉石|暗黑破坏神|DNF|穿越火线|QQ飞车|逆战|Minecraft", RegexOptions.IgnoreCase);
                if (known) registered.Add(new(name, path));
            }
        }
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            var root = drive.RootDirectory.FullName;
            foreach (var name in new[] { "Steam", "SteamLibrary" }) steamRoots.Add(Path.Combine(root, name));
            foreach (var name in new[] { "WeGameApps", "Games", "游戏" }) gameRoots.Add(Path.Combine(root, name));
            foreach (var name in new[] { "PCL2", "Minecraft", ".minecraft" }) minecraftRoots.Add(Path.Combine(root, name));
        }
        foreach (var programFiles in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            steamRoots.Add(Path.Combine(programFiles, "Steam"));
            gameRoots.Add(Path.Combine(programFiles, "Ubisoft", "Ubisoft Game Launcher", "games"));
            gameRoots.Add(Path.Combine(programFiles, "Epic Games"));
        }
        minecraftRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"));
        var epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        return Scan(new(steamRoots.ToArray(), registered, gameRoots.ToArray(), minecraftRoots.ToArray(), epic));
    }

    private IReadOnlyList<RunningGameProcess> SampleProcesses()
    {
        lock (processGate)
        {
            var now = Stopwatch.GetTimestamp();
            if (lastProcessSample != 0 && Stopwatch.GetElapsedTime(lastProcessSample, now) < TimeSpan.FromSeconds(1)) return cachedProcesses;
            var current = new List<RunningGameProcess>();
            var javaKeys = new HashSet<(int, long)>();
            var gameNames = Snapshot.Games.SelectMany(game => game.ExecutablePaths)
                .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var inspectJava = Snapshot.Games.Any(game => game.IsMinecraft);
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        var name = process.ProcessName;
                        var java = name.Equals("java", StringComparison.OrdinalIgnoreCase) || name.Equals("javaw", StringComparison.OrdinalIgnoreCase);
                        if (!gameNames.Contains(name) && !(inspectJava && java)) continue;
                        string? path = null;
                        try { path = process.MainModule?.FileName; }
                        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                        var minecraft = false;
                        if (java)
                        {
                            try
                            {
                                var key = (process.Id, process.StartTime.ToUniversalTime().Ticks);
                                javaKeys.Add(key);
                                if (!javaGames.TryGetValue(key, out minecraft)) javaGames[key] = minecraft = IsMinecraftProcess(process.Id);
                            }
                            catch (System.ComponentModel.Win32Exception) { minecraft = true; }
                        }
                        current.Add(new(process.Id, name, path, minecraft));
                    }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                }
            }
            foreach (var key in javaGames.Keys.Where(key => !javaKeys.Contains(key)).ToArray()) javaGames.Remove(key);
            cachedProcesses = current;
            lastProcessSample = now;
            return cachedProcesses;
        }
    }

    public static bool IsMinecraftCommandLine(string commandLine) =>
        Regex.IsMatch(commandLine, @"net\.minecraft\.client|net\.fabricmc\.loader.*KnotClient|--launchTarget\s+\S*client|--gameDir\s+", RegexOptions.IgnoreCase);

    private static bool IsMinecraftProcess(int processId)
    {
        if (!OperatingSystem.IsWindows()) return false;
        object? locator = null, service = null, results = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            service = ((dynamic)locator!).ConnectServer(".", @"root\cimv2");
            results = ((dynamic)service!).ExecQuery($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            foreach (var entry in (dynamic)results!)
            {
                try
                {
                    // Inspect only in memory; authentication arguments are never returned or saved.
                    if (entry.CommandLine is string command && IsMinecraftCommandLine(command)) return true;
                }
                finally { if (Marshal.IsComObject(entry)) Marshal.ReleaseComObject(entry); }
            }
            return false;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return true; } // An inaccessible Java process remains paused until it can be identified.
        finally
        {
            foreach (var value in new[] { results, service, locator })
                if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }

    private static IReadOnlyList<string> FindGameExecutables(string root)
    {
        var found = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var next))
        {
            foreach (var file in Files(next.Path, "*.exe"))
                if (!Regex.IsMatch(Path.GetFileNameWithoutExtension(file),
                    "unins|uninstall|卸载|launcher|crash|helper|updat|cef|webview|redist|vcredist|dxsetup|unitycrash|easyanticheat|beservice|neacloader|leagueclient|riotclient|rail|wegame|wallpaper|dspcop|service|^steam$|^gameoverlay|^streaming_client$", RegexOptions.IgnoreCase))
                    found.Add(Path.GetFullPath(file));
            if (next.Depth >= 8) continue;
            foreach (var directory in Directories(next.Path))
            {
                var name = Path.GetFileName(directory);
                if (Regex.IsMatch(name, @"^(_CommonRedist|Engine|ThirdParty|TCLS|launcher|SDK|Support|Redist|cache|logs|temp|node_modules|\.git|Content|.*_Data|shadercache|wallpaper_engine)$", RegexOptions.IgnoreCase)) continue;
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push((directory, next.Depth + 1));
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsNonGameName(string name) => Regex.IsMatch(name,
        @"Wallpaper Engine|wallpaper_engine|Steamworks|Steam Controller|Common Redistributable|Runtime|SDK|Launcher|^Steam$|^WeGame$|Battle\.net|暴雪战网|Ubisoft Connect|Rockstar Games Launcher|助手|精彩时刻|^common_apps$|^downloading$|^rail_user_data$", RegexOptions.IgnoreCase);
    private static string? VdfValue(string text, string key)
    {
        var match = Regex.Match(text, $"\"{Regex.Escape(key)}\"\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }
    private static string? ExecutableFromText(string? text)
    {
        if (text is null) return null;
        var match = Regex.Match(text, "^\\s*\"?(.+?\\.exe)(?:\"|,|\\s|$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
    private static bool IsWithin(string path, string root) => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyList<string> Directories(string directory)
    {
        try { return Directory.Exists(directory) ? Directory.GetDirectories(directory) : []; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }
    private static IReadOnlyList<string> Files(string directory, string pattern)
    {
        try { return Directory.Exists(directory) ? Directory.GetFiles(directory, pattern) : []; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }
}
