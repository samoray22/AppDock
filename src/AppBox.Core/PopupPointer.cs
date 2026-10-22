namespace AppBox.Core;

public readonly record struct PointerPlacement(bool Visible, double CenterDip, bool UsesCursorFallback);
public static class PopupPointer
{
    public const double Depth = 10;
    public const double ShadowMargin = 8;
    public static PointerPlacement Calculate(PixelRect monitor, PixelRect work, PixelPoint cursor, PopupBounds popup, double scale, bool enabled)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentException("Invalid DPI scale.");
        var contentWidth = popup.Width / scale - 2 * ShadowMargin;
        if (!enabled || work.Bottom >= monitor.Bottom || work.Left > monitor.Left || work.Right < monitor.Right || contentWidth < 56) return new(false, contentWidth / 2, true);
        var onBottomTaskbar = cursor.Y >= work.Bottom && cursor.Y < monitor.Bottom && cursor.X >= monitor.Left && cursor.X < monitor.Right;
        var requested = onBottomTaskbar ? (cursor.X - popup.X) / scale - ShadowMargin : contentWidth / 2;
        return new(true, Math.Clamp(requested, 26, contentWidth - 26), true);
    }
}
