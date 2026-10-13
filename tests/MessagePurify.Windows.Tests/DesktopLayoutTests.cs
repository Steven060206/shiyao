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

        Assert.Equal(layout, reopened.Load(new DesktopRect(0, 0, 1920, 1080), 1));
        Assert.Null(store.Error);
        Assert.Null(reopened.Error);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void FractionalSavedPositionsLoadAndRemainUnchangedWhenTheOtherCardMoves()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(LayoutPath, """
            {"schema":1,"tasks":{"x":1389.6,"y":204,"width":340,"height":360},
            "pending":{"x":1033.6,"y":206.4,"width":340,"height":360}}
            """);
        var saved = File.ReadAllBytes(LayoutPath);
        var expected = new DesktopCardLayout(new(1737, 255, 425, 450), new(1292, 258, 425, 450));
        var bounds = new DesktopRect(0, 0, 2560, 1380);
        var store = new DesktopLayoutStore(directory);

        Assert.Equal(expected, store.Load(new[] { bounds }, 1.25));
        Assert.Null(store.Error);
        Assert.Equal(expected, store.Load(bounds, 1.25));
        Assert.Null(store.Error);
        Assert.Equal(saved, File.ReadAllBytes(LayoutPath));

        var moved = expected with { Tasks = new(1800, 300, 425, 450) };
        store.SaveCard(moved, 1.25, pending: false);
        Assert.Null(store.Error);
        Assert.Equal(moved, new DesktopLayoutStore(directory).Load(new[] { bounds }, 1.25));
        using var json = JsonDocument.Parse(File.ReadAllText(LayoutPath));
        Assert.Equal(1033.6, json.RootElement.GetProperty("pending").GetProperty("x").GetDouble());
        Assert.Equal(206.4, json.RootElement.GetProperty("pending").GetProperty("y").GetDouble());
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
        var restored = new DesktopLayoutStore(directory).Load(new DesktopRect(0, 0, 2000, 1200), 1.5);
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
        var layout = store.Load(new DesktopRect(0, 0, 1920, 1080), 1);
        Assert.NotNull(store.Error);

        store.Save(layout, 1);

        Assert.Null(store.Error);
        Assert.Equal(layout, new DesktopLayoutStore(directory).Load(new DesktopRect(0, 0, 1920, 1080), 1));
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

    [Fact]
    public void FirstMultimonitorLoadPlacesBothCardsOnTheFirstWorkArea()
    {
        DesktopRect[] areas = [new(-1280, -100, 1280, 860), new(0, 0, 1920, 1040)];
        var layout = new DesktopLayoutStore(directory).Load(areas, 1);

        AssertContained(layout.Tasks, areas[0]);
        AssertContained(layout.Pending, areas[0]);
        Assert.Equal(layout.Tasks.Y, layout.Pending.Y);
        Assert.True(layout.Tasks.X + layout.Tasks.Width <= layout.Pending.X);
        Assert.False(File.Exists(LayoutPath));
    }

    [Fact]
    public void MultimonitorLoadFitsSavedCardsIntoRealWorkAreasRatherThanTheGap()
    {
        var store = new DesktopLayoutStore(directory);
        store.Save(new(new(900, 60, 100, 200), new(1600, 740, 300, 250)), 1);
        var original = File.ReadAllText(LayoutPath);
        DesktopRect[] areas = [new(0, 0, 800, 600), new(1200, 0, 800, 760)];

        var layout = store.Load(areas, 1);

        Assert.Equal(new DesktopRect(700, 60, 100, 200), layout.Tasks);
        Assert.Equal(new DesktopRect(1600, 510, 300, 250), layout.Pending);
        Assert.Equal(original, File.ReadAllText(LayoutPath));
    }

    [Fact]
    public void DisconnectedMonitorAndItsReturnLeaveTheOriginalLayoutUntouched()
    {
        var store = new DesktopLayoutStore(directory);
        var saved = new DesktopCardLayout(new(-1100, 30, 400, 350), new(900, 40, 300, 250));
        store.Save(saved, 1);
        var original = File.ReadAllText(LayoutPath);

        var disconnected = store.Load([new(0, 0, 1280, 760)], 1);
        AssertContained(disconnected.Tasks, new(0, 0, 1280, 760));
        Assert.Equal(original, File.ReadAllText(LayoutPath));

        var returned = new DesktopLayoutStore(directory).Load(
            [new(0, 0, 1280, 760), new(-1280, 0, 1280, 760)], 1);
        Assert.Equal(saved, returned);
        Assert.Equal(original, File.ReadAllText(LayoutPath));
    }

    [Fact]
    public void MovingTaskCardWhileOtherDisplayIsMissingPreservesPendingOriginalDips()
    {
        var store = new DesktopLayoutStore(directory);
        store.Save(new(new(100, 80, 400, 360), new(2400, 120, 500, 400)), 2);
        var current = store.Load([new(0, 0, 1000, 760)], 1.5);
        var moved = new DesktopRect(20, 30, 300, 270);

        store.SaveCard(current with { Tasks = moved }, 1.5, pending: false);

        using var json = JsonDocument.Parse(File.ReadAllText(LayoutPath));
        var pending = json.RootElement.GetProperty("pending");
        Assert.Equal(1200, pending.GetProperty("x").GetDouble());
        Assert.Equal(60, pending.GetProperty("y").GetDouble());
        Assert.Equal(250, pending.GetProperty("width").GetDouble());
        Assert.Equal(200, pending.GetProperty("height").GetDouble());
        var returned = new DesktopLayoutStore(directory).Load(
            [new(0, 0, 1000, 760), new(1600, 0, 1000, 760)], 1.5);
        Assert.Equal(moved, returned.Tasks);
        Assert.Equal(new DesktopRect(1800, 90, 375, 300), returned.Pending);
        Assert.Null(store.Error);
    }

    [Fact]
    public void ResizingPendingCardPreservesTheTaskCardOnItsMissingDisplay()
    {
        var store = new DesktopLayoutStore(directory);
        store.Save(new(new(-1100, 30, 400, 350), new(300, 40, 300, 250)), 1);
        var current = store.Load([new(0, 0, 1280, 760)], 1);
        var resized = current.Pending with { Width = 420, Height = 310 };

        store.SaveCard(current with { Pending = resized }, 1, pending: true);

        var returned = new DesktopLayoutStore(directory).Load(
            [new(0, 0, 1280, 760), new(-1280, 0, 1280, 760)], 1);
        Assert.Equal(new DesktopRect(-1100, 30, 400, 350), returned.Tasks);
        Assert.Equal(resized, returned.Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstExplicitSingleCardSavePersistsBothCurrentCards(bool pending)
    {
        var store = new DesktopLayoutStore(directory);
        var current = new DesktopCardLayout(new(80, 60, 400, 320), new(700, 60, 350, 280));

        store.SaveCard(current, 2, pending);

        Assert.Equal(current, new DesktopLayoutStore(directory).Load([new(0, 0, 1280, 760)], 2));
        Assert.Null(store.Error);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void ScaleChangesRestoreSavedDipsWithoutReplacingTheFile()
    {
        var store = new DesktopLayoutStore(directory);
        var saved = new DesktopCardLayout(new(80, 60, 400, 320), new(700, 60, 350, 280));
        store.Save(saved, 2);
        var original = File.ReadAllText(LayoutPath);
        DesktopRect[] areas = [new(0, 0, 1920, 1040), new(-1280, 0, 1280, 960)];

        var scaled = store.Load(areas, 1.5);
        Assert.Equal(new DesktopRect(60, 45, 300, 240), scaled.Tasks);
        Assert.Equal(new DesktopRect(525, 45, 262.5, 210), scaled.Pending);
        Assert.Equal(saved, store.Load(areas, 2));
        Assert.Equal(original, File.ReadAllText(LayoutPath));
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("{\"schema\":99,\"tasks\":{},\"pending\":{}}")]
    public void MultimonitorFallbackPreservesCorruptFileUntilExplicitCardSave(string contents)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(LayoutPath, contents);
        var store = new DesktopLayoutStore(directory);
        DesktopRect[] areas = [new(0, 0, 1920, 1040), new(-1280, 0, 1280, 960)];
        var fallback = store.Load(areas, 1);

        Assert.NotNull(store.Error);
        Assert.Equal(contents, File.ReadAllText(LayoutPath));
        store.SaveCard(fallback, 1, pending: false);
        Assert.Null(store.Error);
        Assert.Equal(fallback, new DesktopLayoutStore(directory).Load(areas, 1));
        using var json = JsonDocument.Parse(File.ReadAllText(LayoutPath));
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
    }

    [Fact]
    public void FailedSingleCardReplacementPreservesTheExistingFileAndReportsError()
    {
        var store = new DesktopLayoutStore(directory);
        var layout = new DesktopCardLayout(new(10, 10, 300, 260), new(500, 10, 300, 260));
        store.Save(layout, 1);
        var original = File.ReadAllText(LayoutPath);
        using (var locked = new FileStream(LayoutPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            store.SaveCard(layout with { Tasks = new(50, 50, 300, 260) }, 1, pending: false);

        Assert.NotNull(store.Error);
        Assert.Equal(original, File.ReadAllText(LayoutPath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void InvalidSingleCardGeometryCannotReplaceTheExistingFile()
    {
        var store = new DesktopLayoutStore(directory);
        var layout = new DesktopCardLayout(new(10, 10, 300, 260), new(500, 10, 300, 260));
        store.Save(layout, 1);
        var original = File.ReadAllText(LayoutPath);

        Assert.ThrowsAny<ArgumentException>(() => store.SaveCard(
            layout with { Tasks = new(10, 10, 0, 260) }, 1, pending: false));
        Assert.Equal(original, File.ReadAllText(LayoutPath));
    }

    [Fact]
    public void MissingOrInvalidWorkAreasAreRejectedBeforeReadingTheFile()
    {
        var store = new DesktopLayoutStore(directory);

        Assert.ThrowsAny<ArgumentException>(() => store.Load(Array.Empty<DesktopRect>(), 1));
        Assert.ThrowsAny<ArgumentException>(() => store.Load((IReadOnlyList<DesktopRect>)null!, 1));
        Assert.ThrowsAny<ArgumentException>(() => store.Load([new(0, 0, 1000, 800), new(0, 0, 0, 800)], 1));
        Assert.False(File.Exists(LayoutPath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NewLayoutEntrypointsRejectInvalidScaleBeforeReadingOrWriting(double scale)
    {
        var store = new DesktopLayoutStore(directory);
        var layout = new DesktopCardLayout(new(10, 10, 300, 260), new(500, 10, 300, 260));

        Assert.ThrowsAny<ArgumentException>(() => store.Load([new(0, 0, 1000, 800)], scale));
        Assert.ThrowsAny<ArgumentException>(() => store.SaveCard(layout, scale, pending: false));
        Assert.False(File.Exists(LayoutPath));
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
