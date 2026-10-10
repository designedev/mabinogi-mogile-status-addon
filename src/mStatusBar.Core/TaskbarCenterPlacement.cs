namespace mStatusBar.Core;

public static class TaskbarCenterPlacement
{
    // Coordinates are physical pixels relative to the taskbar, including negative monitor origins only in the caller.
    public static double Left(double taskbarWidth, double freeLeft, double freeRight, double contentWidth)
    {
        if (!double.IsFinite(taskbarWidth) || !double.IsFinite(freeLeft) || !double.IsFinite(freeRight) ||
            !double.IsFinite(contentWidth) || taskbarWidth <= 0 || freeRight < freeLeft || contentWidth < 0 || contentWidth > freeRight - freeLeft)
            throw new ArgumentException("가운데 배치 영역이 올바르지 않습니다.");
        return Math.Clamp((taskbarWidth - contentWidth) / 2, freeLeft, freeRight - contentWidth);
    }
}
