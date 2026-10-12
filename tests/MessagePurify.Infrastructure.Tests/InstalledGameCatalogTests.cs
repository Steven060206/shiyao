using System.Text.Json;
using MessagePurify.Infrastructure.Application;

namespace MessagePurify.Infrastructure.Tests;

public sealed class InstalledGameCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-game-catalog-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-10-12T10:00:00+08:00");
    private string Cache => Path.Combine(root, "catalog.json");

    [Fact]
    public void SteamLibrariesAndPlayableOrphansExcludeLaunchersToolsAndUninstalledResidue()
    {
        var steam = Path.Combine(root, "Steam");
        var library = Path.Combine(root, "OtherLibrary");
        var apps = Path.Combine(steam, "steamapps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "libraryfolders.vdf"), $"\"path\" \"{library.Replace("\\", "\\\\")}\"");
        MakeExecutable(Path.Combine(apps, "common", "PrimaryGame", "Binaries", "Win64", "Game-Win64-Shipping.exe"));
        MakeExecutable(Path.Combine(apps, "common", "PrimaryGame", "Launcher.exe"));
        MakeExecutable(Path.Combine(apps, "common", "PrimaryGame", "UnityCrashHandler64.exe"));
        MakeExecutable(Path.Combine(apps, "common", "PrimaryGame", "TCLS", "Idle.exe"));
        MakeExecutable(Path.Combine(steam, "steam.exe"));
        Manifest(apps, "1", "游戏标题", "PrimaryGame");
        MakeExecutable(Path.Combine(library, "steamapps", "common", "PortableGame", "Play.exe"));
        MakeExecutable(Path.Combine(apps, "common", "wallpaper_engine", "wallpaper64.exe"));
        Manifest(apps, "431960", "Wallpaper Engine", "wallpaper_engine");
        Directory.CreateDirectory(Path.Combine(apps, "common", "RemovedGame"));
        Manifest(apps, "2", "卸载残留", "RemovedGame");

        var games = InstalledGameCatalog.Scan(new([steam], [new("Steam registry uninstall location", steam)], [], []));
        Assert.Equal(2, games.Count);
        Assert.Equal("游戏标题", Assert.Single(games, game => game.InstallDirectory.EndsWith("PrimaryGame")).Name);
        Assert.EndsWith("Game-Win64-Shipping.exe", Assert.Single(games.Single(game => game.Name == "游戏标题").ExecutablePaths));
        Assert.Contains(games, game => game.Name == "PortableGame");
    }

    [Fact]
    public void RegisteredWeGameEpicAndMinecraftAreFoundWithoutCountingPlatformHelpers()
    {
        var wegame = Path.Combine(root, "WeGameApps");
        var registered = Path.Combine(wegame, "rail_apps", "无畏契约(1)");
        MakeExecutable(Path.Combine(registered, "live", "ShooterGame", "Binaries", "Win64", "VALORANT-Win64-Shipping.exe"));
        MakeExecutable(Path.Combine(wegame, "common_apps", "Assistant.exe"));
        var overwatch = Path.Combine(root, "Overwatch");
        MakeExecutable(Path.Combine(overwatch, "_retail_", "Overwatch.exe"));
        MakeExecutable(Path.Combine(overwatch, "_retail_", "NeacLoader.exe"));
        var minecraft = Path.Combine(root, "PCL2");
        Directory.CreateDirectory(Path.Combine(minecraft, "versions", "Fabric"));
        var epic = Path.Combine(root, "Epic", "Manifests");
        var epicGame = Path.Combine(root, "EpicGame");
        MakeExecutable(Path.Combine(epicGame, "ActualGame.exe"));
        Directory.CreateDirectory(epic);
        File.WriteAllText(Path.Combine(epic, "game.item"), JsonSerializer.Serialize(new { DisplayName = "Epic游戏", InstallLocation = epicGame }));
        var games = InstalledGameCatalog.Scan(new([], [new("无畏契约", registered), new("守望先锋", overwatch)], [wegame], [minecraft], epic));
        Assert.Equal(4, games.Count);
        Assert.Single(games, game => game.Name == "Minecraft" && game.IsMinecraft && game.ExecutablePaths.Count == 0);
        Assert.Single(games, game => game.Name == "无畏契约");
        Assert.Single(games.Single(game => game.Name == "守望先锋").ExecutablePaths);
        Assert.DoesNotContain(games, game => game.Name == "common_apps");
    }

    [Fact]
    public async Task StartupAndDailyScanReplaceAddedAndRemovedGamesAndPersistTheList()
    {
        IReadOnlyList<InstalledGame> installed = [Game("Old")];
        var scans = 0;
        var catalog = new InstalledGameCatalog(Cache, () => { scans++; return installed; }, () => []);
        Assert.False(catalog.IsReady);
        Assert.True(await catalog.RefreshIfDueAsync(now));
        Assert.True(catalog.IsReady);
        installed = [Game("New")];
        Assert.False(await catalog.RefreshIfDueAsync(now.AddHours(23)));
        Assert.True(await catalog.RefreshIfDueAsync(now.AddDays(1)));
        Assert.Equal(2, scans);
        Assert.Equal("New", Assert.Single(catalog.Snapshot.Games).Name);
        var reloaded = new InstalledGameCatalog(Cache, () => installed, () => []);
        Assert.True(reloaded.IsReady);
        Assert.Equal("New", Assert.Single(reloaded.Snapshot.Games).Name);
        Assert.True(await reloaded.RefreshIfDueAsync(now.AddDays(1).AddMinutes(1)), "每次应用启动重新扫描，不沿用昨天的启动标记");
    }

    [Fact]
    public async Task RunningWindowedGameDefersDailyDirectoryScanUntilTheProcessExits()
    {
        var game = Game("Play");
        IReadOnlyList<RunningGameProcess> processes = [];
        var scans = 0;
        var catalog = new InstalledGameCatalog(Cache, () => { scans++; return [game]; }, () => processes);
        await catalog.RefreshIfDueAsync(now);
        processes = [new(123, "Play", game.ExecutablePaths[0])];
        Assert.True(catalog.IsGameRunning());
        Assert.False(await catalog.RefreshIfDueAsync(now.AddDays(1)));
        Assert.Equal(1, scans);
        processes = [];
        Assert.True(await catalog.RefreshIfDueAsync(now.AddDays(1).AddMinutes(1)));
        Assert.Equal(2, scans);
    }

    [Fact]
    public async Task CachedListBlocksStartupScanningDuringAnAlreadyRunningGame()
    {
        var game = Game("Play");
        var first = new InstalledGameCatalog(Cache, () => [game], () => []);
        await first.RefreshIfDueAsync(now);
        var scans = 0;
        var second = new InstalledGameCatalog(Cache, () => { scans++; return [game]; },
            () => [new(123, "Play", game.ExecutablePaths[0])]);
        Assert.True(second.IsReady);
        Assert.False(await second.RefreshIfDueAsync(now.AddMinutes(1)));
        Assert.Equal(0, scans);
    }

    [Fact]
    public async Task MatchingUsesExecutablePathAndOnlyFallsBackToNameIfPathCannotBeRead()
    {
        var game = Game("Play");
        IReadOnlyList<RunningGameProcess> processes = [new(12, "Steam", Path.Combine(root, "steam.exe")),
            new(13, "Play", Path.Combine(root, "OtherApp", "Play.exe")), new(0, "Play", null)];
        var catalog = new InstalledGameCatalog(Cache, () => [game], () => processes);
        await catalog.RefreshIfDueAsync(now);
        Assert.False(catalog.IsGameRunning());
        processes = [new(15, "PLAY", null)];
        Assert.True(catalog.IsGameRunning());
        processes = [new(15, "Play", game.ExecutablePaths[0].ToUpperInvariant())];
        Assert.True(catalog.IsGameRunning());
    }

    [Fact]
    public async Task OrdinaryJavaIsAllowedAndMinecraftJavaPausesSync()
    {
        IReadOnlyList<RunningGameProcess> processes = [new(15, "javaw", null, false)];
        var minecraft = new InstalledGame("Minecraft", Path.Combine(root, "PCL2"), [], true);
        var catalog = new InstalledGameCatalog(Cache, () => [minecraft], () => processes);
        await catalog.RefreshIfDueAsync(now);
        Assert.False(catalog.IsGameRunning());
        processes = [new(15, "javaw", null, true)];
        Assert.True(catalog.IsGameRunning());
        var controller = new ResidentSyncController(catalog);
        Assert.False(controller.CanSync);
    }

    [Theory]
    [InlineData("java net.minecraft.client.main.Main --accessToken synthetic-private-token", true)]
    [InlineData("java net.fabricmc.loader.impl.launch.knot.KnotClient", true)]
    [InlineData("java cpw.mods.bootstraplauncher.BootstrapLauncher --launchTarget forgeclient", true)]
    [InlineData("java cpw.mods.modlauncher.Launcher --gameDir E:/PCL2/versions/test", true)]
    [InlineData("java net.minecraft.server.Main", false)]
    [InlineData("java -jar normal-application.jar", false)]
    public void MinecraftCommandLineIsClassifiedWithoutReturningItsContents(string command, bool expected)
        => Assert.Equal(expected, InstalledGameCatalog.IsMinecraftCommandLine(command));

    [Fact]
    public async Task FailedRefreshPreservesPriorListAndRetriesInsteadOfErasingIt()
    {
        var fail = false;
        var catalog = new InstalledGameCatalog(Cache, () => fail ? throw new IOException("synthetic") : [Game("Play")], () => []);
        Assert.True(await catalog.RefreshIfDueAsync(now));
        var saved = File.ReadAllText(Cache);
        fail = true;
        Assert.False(await catalog.RefreshIfDueAsync(now.AddDays(1)));
        Assert.Equal(saved, File.ReadAllText(Cache));
        Assert.True(catalog.IsReady);
        Assert.Single(catalog.Snapshot.Games);
        fail = false;
        Assert.True(await catalog.RefreshIfDueAsync(now.AddDays(1).AddMinutes(1)));
    }

    [Fact]
    public async Task CorruptInitialCacheStaysUnreadyUntilAValidScan()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Cache, "invalid json");
        var catalog = new InstalledGameCatalog(Cache, () => [Game("Play")], () => []);
        Assert.False(catalog.IsReady);
        Assert.True(await catalog.RefreshIfDueAsync(now));
        Assert.True(catalog.IsReady);
    }

    private InstalledGame Game(string name) => new(name, Path.Combine(root, name), [Path.Combine(root, name, name + ".exe")]);
    private static void MakeExecutable(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "synthetic executable, never launched");
    }
    private static void Manifest(string apps, string id, string name, string install)
        => File.WriteAllText(Path.Combine(apps, $"appmanifest_{id}.acf"), $"\"name\" \"{name}\"\n\"installdir\" \"{install}\"");
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
