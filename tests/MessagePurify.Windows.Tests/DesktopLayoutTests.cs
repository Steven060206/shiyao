using System.Text.Json;
using MessagePurify.Windows.App;
using MessagePurify.Windows.IntegrationProbe;

namespace MessagePurify.Windows.Tests;

public sealed class DesktopLayoutTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MessagePurify-DesktopLayout-" + Guid.NewGuid().ToString("N"));
    private string LayoutPath => Path.Combine(directory, "desktop-layout.json");

    [Fact]
    public void FirstLoadPlacesTwoCardsSideBySideWithoutWritingAFile()
    {
        var store = new DesktopLayoutStore(directory);
        var bounds = new DesktopRect(0, 0, 1920, 1080);

        var layout = store.Load(bounds, 1);

        Assert.Equal(340, layout.Tasks.Width);
        Assert.Equal(360, layout.Tasks.Height);
        Assert.Equal(layout.Tasks.Width, layout.Pending.Width);
        Assert.Equal(layout.Tasks.Height, layout.Pending.Height);
        Assert.Equal(layout.Tasks.Y, layout.Pending.Y);
        Assert.True(layout.Tasks.X + layout.Tasks.Width <= layout.Pending.X);
        AssertContained(layout.Tasks, bounds);
        AssertContained(layout.Pending, bounds);
        Assert.Null(store.Error);
        Assert.False(File.Exists(LayoutPath));
    }

    [Theory]
    [InlineData(280, 190, 1)]
    [InlineData(800, 400, 2)]
    [InlineData(30, 20, 1)]
    public void DefaultsFitSmallAndScaledMonitors(double width, double height, double scale)
    {
        var bounds = new DesktopRect(-width, -height, width, height);
        var layout = new DesktopLayoutStore(directory).Load(bounds, scale);

        AssertContained(layout.Tasks, bounds);
        AssertContained(layout.Pending, bounds);
        Assert.True(layout.Tasks.X + layout.Tasks.Width <= layout.Pending.X);
    }

    [Fact]
    public void DragAndShrinkAreRestoredByANewStore()
    {
        var layout = new DesktopCardLayout(new(126, 45, 210, 185), new(514, 83, 260, 235));
        var store = new DesktopLayoutStore(directory);

        store.Save(layout, 1);
        var reopened = new DesktopLayoutStore(directory);

        Assert.Equal(layout, reopened.Load(new(0, 0, 1920, 1080), 1));
        Assert.Null(store.Error);
        Assert.Null(reopened.Error);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void FileStoresDipsAndCurrentScaleRestoresPhysicalCoordinates()
    {
        var store = new DesktopLayoutStore(directory);
        var layout = new DesktopCardLayout(new(100, 80, 500, 400), new(660, 80, 400, 600));

        store.Save(layout, 2);

        using var json = JsonDocument.Parse(File.ReadAllText(LayoutPath));
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        var tasks = json.RootElement.GetProperty("tasks");
        Assert.Equal(50, tasks.GetProperty("x").GetDouble());
        Assert.Equal(40, tasks.GetProperty("y").GetDouble());
        Assert.Equal(250, tasks.GetProperty("width").GetDouble());
        Assert.Equal(200, tasks.GetProperty("height").GetDouble());
        var restored = new DesktopLayoutStore(directory).Load(new(0, 0, 2000, 1200), 1.5);
        Assert.Equal(new DesktopRect(75, 60, 375, 300), restored.Tasks);
        Assert.Equal(new DesktopRect(495, 60, 300, 450), restored.Pending);
    }

    [Fact]
    public void LoadClampsOffscreenCoordinatesAndLargeDimensionsWithoutChangingTheFile()
    {
        var store = new DesktopLayoutStore(directory);
        store.Save(new(new(-1800, -250, 600, 700), new(2900, 2000, 210, 190)), 1);
        var saved = File.ReadAllText(LayoutPath);
        var bounds = new DesktopRect(20, 30, 360, 220);

        var restored = new DesktopLayoutStore(directory).Load(bounds, 1);

        Assert.Equal(new DesktopRect(20, 30, 360, 220), restored.Tasks);
        Assert.Equal(new DesktopRect(170, 60, 210, 190), restored.Pending);
        Assert.Equal(saved, File.ReadAllText(LayoutPath));
    }

    [Theory]
    [InlineData("{invalid-json")]
    [InlineData("{\"schema\":99,\"tasks\":{},\"pending\":{}}")]
    [InlineData("{\"schema\":1,\"tasks\":{\"x\":1,\"y\":2,\"width\":0,\"height\":3},\"pending\":{\"x\":5,\"y\":6,\"width\":7,\"height\":8}}")]
    [InlineData("{\"schema\":1}")]
    [InlineData("null")]
    public void CorruptOrUnknownFileIsPreservedAndReported(string contents)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(LayoutPath, contents);
        var bounds = new DesktopRect(0, 0, 1920, 1080);
        var defaultLayout = new DesktopLayoutStore(Path.Combine(directory, "missing")).Load(bounds, 1);
        var store = new DesktopLayoutStore(directory);

        var restored = store.Load(bounds, 1);

        Assert.Equal(defaultLayout, restored);
        Assert.False(string.IsNullOrWhiteSpace(store.Error));
        var scaledBounds = new DesktopRect(0, 0, 1280, 720);
        var scaledDefault = new DesktopLayoutStore(Path.Combine(directory, "missing")).Load(scaledBounds, 1.5);
        Assert.Equal(scaledDefault, store.Load(scaledBounds, 1.5));
        Assert.False(string.IsNullOrWhiteSpace(store.Error));
        Assert.Equal(contents, File.ReadAllText(LayoutPath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void SuccessfulExplicitSaveCanRepairAFallbackAndClearsTheError()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(LayoutPath, "broken");
        var store = new DesktopLayoutStore(directory);
        var layout = store.Load(new(0, 0, 1920, 1080), 1);
        Assert.NotNull(store.Error);

        store.Save(layout, 1);

        Assert.Null(store.Error);
        Assert.Equal(layout, new DesktopLayoutStore(directory).Load(new(0, 0, 1920, 1080), 1));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void SavingAFailedReplacementReportsErrorAndRemovesTemporaryFiles()
    {
        Directory.CreateDirectory(LayoutPath);
        var store = new DesktopLayoutStore(directory);

        store.Save(new(new(0, 0, 200, 200), new(220, 0, 200, 200)), 1);

        Assert.NotNull(store.Error);
        Assert.True(Directory.Exists(LayoutPath));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidScaleIsRejectedBeforeReadingOrWriting(double scale)
    {
        var store = new DesktopLayoutStore(directory);
        var bounds = new DesktopRect(0, 0, 1000, 800);
        var layout = new DesktopCardLayout(new(10, 10, 200, 200), new(230, 10, 200, 200));

        Assert.ThrowsAny<ArgumentException>(() => { store.Load(bounds, scale); });
        Assert.ThrowsAny<ArgumentException>(() => { store.Save(layout, scale); });
        Assert.False(File.Exists(LayoutPath));
    }

    [Theory]
    [InlineData(double.NaN, 0, 100, 100)]
    [InlineData(0, double.PositiveInfinity, 100, 100)]
    [InlineData(0, 0, 0, 100)]
    [InlineData(0, 0, 100, -1)]
    public void InvalidSavedGeometryCannotReplaceAnExistingValidFile(double x, double y, double width, double height)
    {
        var store = new DesktopLayoutStore(directory);
        var valid = new DesktopCardLayout(new(10, 10, 200, 200), new(230, 10, 200, 200));
        store.Save(valid, 1);
        var original = File.ReadAllText(LayoutPath);

        Assert.ThrowsAny<ArgumentException>(() => { store.Save(valid with { Tasks = new(x, y, width, height) }, 1); });
        Assert.Equal(original, File.ReadAllText(LayoutPath));
    }

    private static void AssertContained(DesktopRect card, DesktopRect bounds)
    {
        Assert.True(card.Width > 0 && card.Height > 0);
        Assert.InRange(card.X, bounds.X, bounds.X + bounds.Width - card.Width);
        Assert.InRange(card.Y, bounds.Y, bounds.Y + bounds.Height - card.Height);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
