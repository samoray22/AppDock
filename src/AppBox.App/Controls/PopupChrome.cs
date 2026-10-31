using System.Windows;
using System.Windows.Media;
using AppBox.Core;
using AppBox.App.Services;

namespace AppBox.App.Controls;

/// <summary>
/// macOS Sonoma/Sequoia-style frosted popover with smooth S-curve Bezier pointer beak,
/// multi-layered vibrancy, specular inner rim, and ambient icon color glow.
/// </summary>
public sealed class PopupChrome : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(PopupChrome),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(PopupChrome),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AmbientGlowBrushProperty = DependencyProperty.Register(
        nameof(AmbientGlowBrush), typeof(Brush), typeof(PopupChrome),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? AmbientGlowBrush { get => (Brush?)GetValue(AmbientGlowBrushProperty); set => SetValue(AmbientGlowBrushProperty, value); }

    public double PointerCenter { get; set; }
    public bool ShowPointer { get; set; }
    public bool RoundCorners { get; set; } = true;

    protected override void OnRender(DrawingContext dc)
    {
        var depth = ShowPointer ? PopupPointer.Depth : 0;
        if (ActualWidth < 4 || ActualHeight <= depth + 4) return;

        var radius = RoundCorners ? 24.0 : 0.0;
        var bodyRect = new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - depth - 1);

        // Build continuous outer contour including smooth Bezier beak if pointer is active
        Geometry outerGeometry;
        if (ShowPointer && RoundCorners)
        {
            outerGeometry = CreateSeamlessBeakGeometry(bodyRect, radius, PointerCenter, ActualHeight - 0.5);
        }
        else if (ShowPointer)
        {
            // Fallback for square corners
            var rectGeom = new RectangleGeometry(bodyRect, 0, 0);
            var center = Math.Clamp(PointerCenter, 26, Math.Max(26, ActualWidth - 26));
            var tip = new StreamGeometry();
            using (var ctx = tip.Open())
            {
                ctx.BeginFigure(new Point(center - 12, bodyRect.Bottom), true, true);
                ctx.LineTo(new Point(center, ActualHeight - 0.5), true, false);
                ctx.LineTo(new Point(center + 12, bodyRect.Bottom), true, false);
            }
            outerGeometry = Geometry.Combine(rectGeom, tip, GeometryCombineMode.Union, null);
        }
        else
        {
            outerGeometry = new RectangleGeometry(bodyRect, radius, radius);
        }

        // Clip everything inside this exact outer silhouette so no pixels ever leak outside
        dc.PushClip(outerGeometry);

        // 1. Base vibrant surface fill
        dc.DrawRectangle(Fill, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // 2. Ambient top-left icon color glow (macOS Sonoma vibrancy)
        var glowBrush = AmbientGlowBrush;
        if (glowBrush is not null)
        {
            dc.DrawRectangle(glowBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        // 3. Subtle frosted glass vertical sheen
        var topSheen = ThemeService.Dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(160, 255, 255, 255);
        var bottomSheen = ThemeService.Dark ? Color.FromArgb(6, 255, 255, 255) : Color.FromArgb(24, 255, 255, 255);
        var sheenBrush = new LinearGradientBrush(topSheen, bottomSheen, new Point(0, 0), new Point(0, 1));
        dc.DrawRectangle(sheenBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Pop clip
        dc.Pop();

        // 4. Outer hairline stroke (exact silhouette)
        dc.DrawGeometry(null, new Pen(Stroke, 1), outerGeometry);

        // 5. Inner specular glass highlight rim
        var innerRimTop = ThemeService.Dark ? Color.FromArgb(44, 255, 255, 255) : Color.FromArgb(200, 255, 255, 255);
        var innerRimBottom = ThemeService.Dark ? Color.FromArgb(10, 255, 255, 255) : Color.FromArgb(32, 255, 255, 255);
        var innerRimBrush = new LinearGradientBrush(innerRimTop, innerRimBottom, new Point(0, 0), new Point(0, 1));

        if (RoundCorners && !ShowPointer)
        {
            var innerBody = new Rect(1.5, 1.5, Math.Max(0, ActualWidth - 3), Math.Max(0, ActualHeight - 3));
            dc.DrawRoundedRectangle(null, new Pen(innerRimBrush, 1), innerBody, radius - 1, radius - 1);
        }
        else if (RoundCorners && ShowPointer)
        {
            var innerBodyRect = new Rect(1.5, 1.5, Math.Max(0, ActualWidth - 3), Math.Max(0, ActualHeight - depth - 2));
            var innerGeom = CreateSeamlessBeakGeometry(innerBodyRect, Math.Max(0, radius - 1), PointerCenter, ActualHeight - 1.5);
            dc.DrawGeometry(null, new Pen(innerRimBrush, 1), innerGeom);
        }
    }

    /// <summary>
    /// Generates a single continuous macOS-style popover path with rounded corners and an elegant, organic S-curve Bezier beak.
    /// </summary>
    private static Geometry CreateSeamlessBeakGeometry(Rect body, double r, double pointerCenter, double beakTipY)
    {
        var geom = new StreamGeometry();
        var center = Math.Clamp(pointerCenter, body.Left + r + 20, body.Right - r - 20);
        var halfBase = 18.0;
        var pLeft = center - halfBase;
        var pRight = center + halfBase;

        using (var ctx = geom.Open())
        {
            // Start at top-left corner
            ctx.BeginFigure(new Point(body.Left + r, body.Top), isFilled: true, isClosed: true);

            // Top edge to top-right
            ctx.LineTo(new Point(body.Right - r, body.Top), isStroked: true, isSmoothJoin: false);
            ctx.ArcTo(new Point(body.Right, body.Top + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);

            // Right edge to bottom-right
            ctx.LineTo(new Point(body.Right, body.Bottom - r), isStroked: true, isSmoothJoin: false);
            ctx.ArcTo(new Point(body.Right - r, body.Bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);

            // Bottom edge toward beak right base
            ctx.LineTo(new Point(pRight, body.Bottom), isStroked: true, isSmoothJoin: false);

            // Pure organic macOS S-curve:
            // 1. Inflects horizontally out from the bottom edge
            // 2. Curves down gracefully into a softly rounded apex at the taskbar icon center
            ctx.BezierTo(
                new Point(center + 8, body.Bottom + 1.2),
                new Point(center + 3.5, beakTipY - 0.8),
                new Point(center, beakTipY),
                isStroked: true, isSmoothJoin: true);

            // Symmetrical S-curve back to left base
            ctx.BezierTo(
                new Point(center - 3.5, beakTipY - 0.8),
                new Point(center - 8, body.Bottom + 1.2),
                new Point(pLeft, body.Bottom),
                isStroked: true, isSmoothJoin: true);

            // Bottom edge to bottom-left corner
            ctx.LineTo(new Point(body.Left + r, body.Bottom), isStroked: true, isSmoothJoin: false);
            ctx.ArcTo(new Point(body.Left, body.Bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);

            // Left edge back to top-left
            ctx.LineTo(new Point(body.Left, body.Top + r), isStroked: true, isSmoothJoin: false);
            ctx.ArcTo(new Point(body.Left + r, body.Top), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }

        geom.Freeze();
        return geom;
    }
}
