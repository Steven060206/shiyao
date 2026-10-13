namespace MessagePurify.Windows.IntegrationProbe;

public readonly record struct DesktopRect(double X, double Y, double Width, double Height);
public readonly record struct DesktopPoint(double X, double Y);

public static class DesktopGeometry
{
    public static DesktopRect Drag(DesktopRect start, DesktopPoint initialCursor, DesktopPoint currentCursor, DesktopRect parentBounds) =>
        Clamp(start with { X = start.X + currentCursor.X - initialCursor.X, Y = start.Y + currentCursor.Y - initialCursor.Y }, parentBounds);

    public static DesktopRect Resize(DesktopRect current, DesktopPoint delta, DesktopRect parentBounds, DesktopPoint minimumSize)
    {
        var size = current with
        {
            Width = Math.Min(Math.Max(minimumSize.X, current.Width + delta.X), parentBounds.X + parentBounds.Width - current.X),
            Height = Math.Min(Math.Max(minimumSize.Y, current.Height + delta.Y), parentBounds.Y + parentBounds.Height - current.Y)
        };
        return Clamp(size, parentBounds);
    }

    public static DesktopRect Clamp(DesktopRect rectangle, DesktopRect monitor)
    {
        if (!Valid(rectangle) || !Valid(monitor)) throw new ArgumentException("Geometry must be finite and positive.");
        var width = Math.Min(rectangle.Width, monitor.Width);
        var height = Math.Min(rectangle.Height, monitor.Height);
        // Subtract sizes first so equal bounds cannot round below their origin.
        return new(Math.Clamp(rectangle.X, monitor.X, monitor.X + (monitor.Width - width)),
            Math.Clamp(rectangle.Y, monitor.Y, monitor.Y + (monitor.Height - height)), width, height);
    }

    private static bool Valid(DesktopRect rect) => double.IsFinite(rect.X) && double.IsFinite(rect.Y) &&
        double.IsFinite(rect.Width) && double.IsFinite(rect.Height) && rect.Width > 0 && rect.Height > 0;
}
