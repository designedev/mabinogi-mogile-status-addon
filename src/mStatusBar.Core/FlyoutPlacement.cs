namespace mStatusBar.Core;

public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public static class FlyoutPlacement
{
    // All inputs use physical screen pixels, including monitors with negative coordinates.
    public static ScreenRect Calculate(ScreenRect work, ScreenRect anchor, double width, double height, double gap)
    {
        width = Math.Min(Math.Max(1, width), work.Width);
        height = Math.Min(Math.Max(1, height), work.Height);
        var left = Math.Clamp(anchor.Right - width, work.Left, work.Right - width);
        var top = anchor.Top - gap - height;
        if (top < work.Top) top = anchor.Bottom + gap;
        top = Math.Clamp(top, work.Top, work.Bottom - height);
        return new(left, top, width, height);
    }
}
