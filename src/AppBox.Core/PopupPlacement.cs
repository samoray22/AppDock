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
}
