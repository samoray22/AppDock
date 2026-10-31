namespace AppBox.Core;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);
public readonly record struct PixelPoint(int X, int Y);
public readonly record struct PopupBounds(int X, int Y, int Width, int Height);

public static class PopupPlacement
{
    public static PopupBounds Calculate(PixelRect monitor, PixelRect work, PixelPoint cursor, double dpiScale, double widthDip = 468, double heightDip = 510)
    {
        if (work.Right - work.Left < 32 || work.Bottom - work.Top < 32 || dpiScale <= 0 || !double.IsFinite(dpiScale))
            throw new ArgumentException("Invalid monitor geometry.");
        var width = Math.Min((int)Math.Ceiling(widthDip * dpiScale), work.Right - work.Left - 16);
        var height = Math.Min((int)Math.Ceiling(heightDip * dpiScale), work.Bottom - work.Top - 16);
        var nearTaskbar = cursor.Y >= work.Bottom || cursor.Y < work.Top || cursor.X < work.Left || cursor.X >= work.Right;
        var x = nearTaskbar ? cursor.X - width / 2 : work.Left + (work.Right - work.Left - width) / 2;
        var y = work.Bottom - height - 10;
        if (work.Top > monitor.Top) y = work.Top + 10;
        if (work.Left > monitor.Left) { x = work.Left + 10; y = cursor.Y - height / 2; }
        if (work.Right < monitor.Right) { x = work.Right - width - 10; y = cursor.Y - height / 2; }
        return new(Math.Clamp(x, work.Left + 8, work.Right - width - 8), Math.Clamp(y, work.Top + 8, work.Bottom - height - 8), width, height);
    }

    public static PopupBounds CalculateEdgeHandle(PixelRect work, ScreenEdge edge, double verticalPosition, double dpiScale, double widthDip = 12, double heightDip = 136)
    {
        if (work.Right - work.Left < 32 || work.Bottom - work.Top < 32 || dpiScale <= 0 || !double.IsFinite(dpiScale))
            throw new ArgumentException("Invalid monitor geometry.");
        var ratio = double.IsFinite(verticalPosition) ? Math.Clamp(verticalPosition, 0.05, 0.95) : 0.35;
        var width = Math.Max(4, (int)Math.Ceiling(widthDip * dpiScale));
        var height = Math.Min((int)Math.Ceiling(heightDip * dpiScale), Math.Max(32, work.Bottom - work.Top - 16));
        var x = edge == ScreenEdge.Left ? work.Left : work.Right - width;
        var centerY = work.Top + (int)Math.Round((work.Bottom - work.Top) * ratio);
        var y = Math.Clamp(centerY - height / 2, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - height - 8));
        return new(x, y, width, height);
    }

    public static PopupBounds CalculateSidebarStrip(PixelRect work, ScreenEdge edge, double verticalPosition, double dpiScale, double widthDip = 78, double heightDip = 360)
    {
        if (work.Right - work.Left < 32 || work.Bottom - work.Top < 32 || dpiScale <= 0 || !double.IsFinite(dpiScale))
            throw new ArgumentException("Invalid monitor geometry.");
        var ratio = double.IsFinite(verticalPosition) ? Math.Clamp(verticalPosition, 0.05, 0.95) : 0.35;
        var width = Math.Min((int)Math.Ceiling(widthDip * dpiScale), work.Right - work.Left - 16);
        var height = Math.Min((int)Math.Ceiling(heightDip * dpiScale), work.Bottom - work.Top - 16);
        var edgeMargin = (int)Math.Ceiling(4 * dpiScale);
        var x = edge == ScreenEdge.Left ? work.Left + edgeMargin : work.Right - width - edgeMargin;
        var centerY = work.Top + (int)Math.Round((work.Bottom - work.Top) * ratio);
        var y = Math.Clamp(centerY - height / 2, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - height - 8));
        return new(x, y, width, height);
    }

    public static PopupBounds CalculateLateralFlyout(PixelRect work, ScreenEdge edge, PopupBounds sidebarBounds, int anchorCenterY, double dpiScale, double widthDip = 468, double heightDip = 510)
    {
        if (work.Right - work.Left < 32 || work.Bottom - work.Top < 32 || dpiScale <= 0 || !double.IsFinite(dpiScale))
            throw new ArgumentException("Invalid monitor geometry.");
        var width = Math.Min((int)Math.Ceiling(widthDip * dpiScale), work.Right - work.Left - 16);
        var height = Math.Min((int)Math.Ceiling(heightDip * dpiScale), work.Bottom - work.Top - 16);
        var gap = (int)Math.Ceiling(6 * dpiScale);
        var x = edge == ScreenEdge.Left
            ? sidebarBounds.X + sidebarBounds.Width + gap
            : sidebarBounds.X - width - gap;
        var y = anchorCenterY - height / 2;
        return new(
            Math.Clamp(x, work.Left + 8, Math.Max(work.Left + 8, work.Right - width - 8)),
            Math.Clamp(y, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - height - 8)),
            width,
            height);
    }
}
