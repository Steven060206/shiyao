using MessagePurify.Windows.App;
using MessagePurify.Windows.IntegrationProbe;

namespace MessagePurify.Windows.Tests;

public sealed class DesktopPlacementTests
{
    [Theory]
    [InlineData(507, 107, 500, 100)]
    [InlineData(507, 200, 500, 100)]
    [InlineData(90, 107, 100, 100)]
    [InlineData(305, 370, 300, 364)]
    [InlineData(305, 30, 300, 36)]
    public void NearbyCardsSnapToAllFourSidesWithoutResizing(double x, double y, double expectedX, double expectedY)
    {
        var moving = new DesktopRect(x, y, 200, 360);
        Assert.Equal(moving with { X = expectedX, Y = expectedY }, DesktopPlacement.Snap(moving,
            new(300, 100, 200, 400), 64, 264, [new(0, 0, 1200, 900)], 1));
    }

    [Theory]
    [InlineData(525, 100)]
    [InlineData(502, 600)]
    [InlineData(350, 370)]
    public void CardsAwayFromAnAlignedEdgeRemainFree(double x, double y)
    {
        var moving = new DesktopRect(x, y, 200, 300);
        Assert.Equal(moving, DesktopPlacement.Snap(moving, new(300, 100, 200, 400),
            300, 400, [new(0, 0, 1200, 900)], 1));
    }

    [Fact]
    public void SnapDistanceScalesWithDpiAndUsesCompactVisibleHeight()
    {
        var moving = new DesktopRect(606, 594, 400, 400);
        Assert.Equal(moving with { X = 600, Y = 580 }, DesktopPlacement.Snap(moving,
            new(600, 252, 400, 720), 400, 328, [new(0, 0, 2400, 1800)], 2));
    }

    [Fact]
    public void SnappingDoesNotMoveOrResizeCardsOutsideTheWorkArea()
    {
        var moving = new DesktopRect(100, 285, 200, 300);
        Assert.Equal(moving, DesktopPlacement.Snap(moving, new(100, 100, 200, 200),
            300, 200, [new(0, 0, 800, 590)], 1));
    }

    [Fact]
    public void CardAlreadyInsideANegativeCoordinateMonitorKeepsItsPositionAndSize()
    {
        var rectangle = new DesktopRect(-1100, -700, 400, 300);

        Assert.Equal(rectangle, DesktopPlacement.Fit(rectangle,
            [new(0, 0, 1920, 1040), new(-1280, -900, 1280, 860)]));
    }

    [Fact]
    public void GapBetweenDisplaysMovesCardOntoTheNearestWorkArea()
    {
        var result = DesktopPlacement.Fit(new(920, 60, 100, 200),
            [new(0, 0, 800, 600), new(1200, 0, 800, 600)]);

        Assert.Equal(new DesktopRect(700, 60, 100, 200), result);
    }

    [Fact]
    public void CardOutsideAllDisplaysChoosesTheNearestAreaEvenWhenItIsNotFirst()
    {
        var result = DesktopPlacement.Fit(new(-1800, -200, 300, 250),
            [new(0, 0, 1000, 800), new(-1280, -100, 1280, 800)]);

        Assert.Equal(new DesktopRect(-1280, -100, 300, 250), result);
    }

    [Fact]
    public void CardAcrossDisplaysChoosesTheLargestIntersection()
    {
        var result = DesktopPlacement.Fit(new(900, 50, 400, 300),
            [new(0, 0, 1000, 800), new(1000, 0, 1000, 800)]);

        Assert.Equal(new DesktopRect(1000, 50, 400, 300), result);
    }

    [Fact]
    public void EqualIntersectionsUseTheFirstAreaForStablePrimaryMonitorPreference()
    {
        var result = DesktopPlacement.Fit(new(900, 50, 200, 300),
            [new(1000, 0, 1000, 800), new(0, 0, 1000, 800)]);

        Assert.Equal(new DesktopRect(1000, 50, 200, 300), result);
    }

    [Fact]
    public void EqualDistancesFromAGapUseTheFirstArea()
    {
        var result = DesktopPlacement.Fit(new(900, 50, 100, 300),
            [new(1100, 0, 900, 800), new(0, 0, 800, 800)]);

        Assert.Equal(new DesktopRect(1100, 50, 100, 300), result);
    }

    [Fact]
    public void VerticalGapDoesNotCountAsVisibleDesktopSpace()
    {
        var result = DesktopPlacement.Fit(new(100, -350, 300, 200),
            [new(0, 0, 1000, 760), new(0, -1200, 1000, 800)]);

        Assert.Equal(new DesktopRect(100, -600, 300, 200), result);
    }

    [Theory]
    [InlineData(0, 0, 1000, 760, 700, 560)]
    [InlineData(0, 40, 1000, 760, 700, 40)]
    [InlineData(40, 0, 960, 800, 40, 0)]
    [InlineData(0, 0, 960, 800, 660, 600)]
    public void CardFitsInsideWorkAreaAwayFromAnyTaskbarEdge(double x, double y,
        double width, double height, double expectedX, double expectedY)
    {
        var card = new DesktopRect(x == 40 ? 0 : 850, y == 40 ? 0 : 700, 300, 200);
        if (x == 40) card = card with { Y = 0 };

        Assert.Equal(new DesktopRect(expectedX, expectedY, 300, 200),
            DesktopPlacement.Fit(card, [new(x, y, width, height)]));
    }

    [Fact]
    public void OversizedCardShrinksToOneWorkAreaInsteadOfSpanningTheVirtualDesktop()
    {
        var result = DesktopPlacement.Fit(new(-20, -20, 1600, 1200),
            [new(0, 0, 900, 700), new(1300, 0, 800, 600)]);

        Assert.Equal(new DesktopRect(0, 0, 900, 700), result);
    }

    [Fact]
    public void LargestIntersectionTakesPriorityOverTheSmallestMove()
    {
        var result = DesktopPlacement.Fit(new(90, 50, 500, 500),
            [new(0, 0, 100, 600), new(400, 0, 800, 600)]);

        Assert.Equal(new DesktopRect(400, 50, 500, 500), result);
    }

    [Fact]
    public void TinyWorkAreaStillProducesAPositiveVisibleCard()
    {
        Assert.Equal(new DesktopRect(10, 20, 30, 15),
            DesktopPlacement.Fit(new(900, 800, 340, 360), [new(10, 20, 30, 15)]));
    }

    [Fact]
    public void MissingWorkAreasAreRejectedInsteadOfInventingVisibleDesktopSpace()
    {
        Assert.ThrowsAny<ArgumentException>(() => DesktopPlacement.Fit(new(0, 0, 340, 360), []));
        Assert.ThrowsAny<ArgumentException>(() => DesktopPlacement.Fit(new(0, 0, 340, 360), null!));
    }

    [Theory]
    [InlineData(double.NaN, 0, 100, 100)]
    [InlineData(0, double.PositiveInfinity, 100, 100)]
    [InlineData(0, 0, 0, 100)]
    [InlineData(0, 0, 100, -1)]
    public void InvalidCardOrWorkAreaIsRejected(double x, double y, double width, double height)
    {
        var invalid = new DesktopRect(x, y, width, height);
        var valid = new DesktopRect(0, 0, 1000, 800);

        Assert.ThrowsAny<ArgumentException>(() => DesktopPlacement.Fit(invalid, [valid]));
        Assert.ThrowsAny<ArgumentException>(() => DesktopPlacement.Fit(new(0, 0, 100, 100), [valid, invalid]));
    }
}
