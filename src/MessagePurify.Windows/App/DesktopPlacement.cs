using MessagePurify.Windows.IntegrationProbe;

namespace MessagePurify.Windows.App;

public static class DesktopPlacement
{
    public static DesktopRect Snap(DesktopRect moving, DesktopRect anchor, double movingVisibleHeight,
        double anchorVisibleHeight, IReadOnlyList<DesktopRect> workAreas, double scale)
    {
        var fitted = Fit(moving, workAreas);
        Validate(anchor);
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var threshold = 16 * scale;
        // Each visible panel has a 5 DIP inset; overlap the window margins to leave a 2 DIP gap.
        var marginOverlap = 8 * scale;
        var targets = new DesktopPoint[]
        {
            new(anchor.X + anchor.Width - marginOverlap, anchor.Y), new(anchor.X - fitted.Width + marginOverlap, anchor.Y),
            new(anchor.X, anchor.Y + anchorVisibleHeight - marginOverlap), new(anchor.X, anchor.Y - movingVisibleHeight + marginOverlap)
        };
        var nearest = targets.Where((point, index) => index < 2
            ? Math.Abs(point.X - fitted.X) <= threshold && fitted.Y < anchor.Y + anchorVisibleHeight && fitted.Y + movingVisibleHeight > anchor.Y
            : Math.Abs(point.Y - fitted.Y) <= threshold && fitted.X < anchor.X + anchor.Width && fitted.X + fitted.Width > anchor.X)
            .OrderBy(point => Math.Abs(point.X - fitted.X) + Math.Abs(point.Y - fitted.Y));
        foreach (var point in nearest)
        {
            var snapped = fitted with { X = point.X, Y = point.Y };
            if (Fit(snapped, workAreas) == snapped) return snapped;
        }
        return fitted;
    }

    public static DesktopRect Fit(DesktopRect rectangle, IReadOnlyList<DesktopRect> workAreas)
    {
        Validate(rectangle);
        ArgumentNullException.ThrowIfNull(workAreas);
        if (workAreas.Count == 0) throw new ArgumentException("At least one desktop work area is required.", nameof(workAreas));
        for (var index = 0; index < workAreas.Count; index++) Validate(workAreas[index]);

        var selected = 0;
        var bestIntersection = 0d;
        for (var index = 0; index < workAreas.Count; index++)
        {
            var intersection = IntersectionArea(rectangle, workAreas[index]);
            // Strict comparison deliberately preserves the caller's order for ties.
            if (intersection > bestIntersection)
            {
                selected = index;
                bestIntersection = intersection;
            }
        }

        if (bestIntersection <= 0)
        {
            var bestDistance = double.PositiveInfinity;
            for (var index = 0; index < workAreas.Count; index++)
            {
                var fitted = FitToArea(rectangle, workAreas[index]);
                var distance = Math.Abs(fitted.X - rectangle.X) + Math.Abs(fitted.Y - rectangle.Y);
                if (distance < bestDistance)
                {
                    selected = index;
                    bestDistance = distance;
                }
            }
        }

        return FitToArea(rectangle, workAreas[selected]);
    }

    private static DesktopRect FitToArea(DesktopRect rectangle, DesktopRect area)
    {
        var width = Math.Min(rectangle.Width, area.Width);
        var height = Math.Min(rectangle.Height, area.Height);
        var x = Math.Clamp(rectangle.X, area.X, area.X + area.Width - width);
        var y = Math.Clamp(rectangle.Y, area.Y, area.Y + area.Height - height);
        return new(x, y, width, height);
    }

    private static double IntersectionArea(DesktopRect first, DesktopRect second)
    {
        var width = Math.Min(first.X + first.Width, second.X + second.Width) - Math.Max(first.X, second.X);
        var height = Math.Min(first.Y + first.Height, second.Y + second.Height) - Math.Max(first.Y, second.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }

    private static void Validate(DesktopRect rectangle)
    {
        if (!double.IsFinite(rectangle.X) || !double.IsFinite(rectangle.Y) ||
            !double.IsFinite(rectangle.Width) || !double.IsFinite(rectangle.Height) ||
            rectangle.Width <= 0 || rectangle.Height <= 0)
            throw new ArgumentException("Desktop geometry must be finite and positive.");
    }
}
