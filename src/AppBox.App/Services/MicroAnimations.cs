using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AppBox.Core;

namespace AppBox.App.Services;

/// <summary>
/// Interactive macOS-style micro-animations:
/// 1. Gaussian Dock Magnification (Fisheye wave on Sidebar &amp; Overview)
/// 2. Elastic App Launch Bounce
/// 3. 3-D Parallax Card Tilt on Hover
/// 4. Classic macOS "Poof" Particle Cloud on Remove
/// </summary>
public static class MicroAnimations
{
    public static void ApplyCardTilt(FrameworkElement tile, Point mousePosInTile)
    {
        double w = tile.ActualWidth;
        double h = tile.ActualHeight;
        if (w <= 4 || h <= 4) return;

        double nx = Math.Clamp((mousePosInTile.X / w - 0.5) * 2.0, -1.0, 1.0);
        double ny = Math.Clamp((mousePosInTile.Y / h - 0.5) * 2.0, -1.0, 1.0);

        var group = EnsureTransformGroup(tile, out var scale, out var skew, out var translate);
        tile.RenderTransformOrigin = new Point(0.5, 0.5);
        Panel.SetZIndex(tile, 10);

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        skew.BeginAnimation(SkewTransform.AngleXProperty, null);
        skew.BeginAnimation(SkewTransform.AngleYProperty, null);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);

        scale.ScaleX = 1.075;
        scale.ScaleY = 1.075;
        skew.AngleX = -ny * 3.8;
        skew.AngleY = nx * 3.8;
        translate.X = nx * 2.8;
        translate.Y = ny * 2.8 - 1.5;
    }

    public static void ResetCardTilt(FrameworkElement tile)
    {
        EnsureTransformGroup(tile, out var scale, out var skew, out var translate);
        Panel.SetZIndex(tile, 0);

        var duration = TimeSpan.FromMilliseconds(150);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, duration) { EasingFunction = ease });
        skew.BeginAnimation(SkewTransform.AngleXProperty, new DoubleAnimation(0.0, duration) { EasingFunction = ease });
        skew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(0.0, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0.0, duration) { EasingFunction = ease });
    }

    public static void PlayLaunchBounce(FrameworkElement element)
    {
        EnsureTransformGroup(element, out var scale, out _, out var translate);
        element.RenderTransformOrigin = new Point(0.5, 0.85);
        Panel.SetZIndex(element, 20);

        var jumpAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
        jumpAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        jumpAnim.KeyFrames.Add(new EasingDoubleKeyFrame(-14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(95)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        jumpAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(185)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        jumpAnim.KeyFrames.Add(new EasingDoubleKeyFrame(-6.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        jumpAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));

        var scaleXAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
        scaleXAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scaleXAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.94, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85))));
        scaleXAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(185))));
        scaleXAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));

        var scaleYAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
        scaleYAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scaleYAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85))));
        scaleYAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.92, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(185))));
        scaleYAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));

        translate.BeginAnimation(TranslateTransform.YProperty, jumpAnim);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
    }

    public static void ApplyDockMagnification(
        ItemsControl itemsControl,
        Point cursorInItemsControl,
        bool verticalStrip,
        double influenceRadiusDip = 104.0,
        double maxExtraScale = 0.30)
    {
        int count = itemsControl.Items.Count;
        for (int i = 0; i < count; i++)
        {
            if (itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                continue;

            var target = FindDescendantButton(container) ?? container;
            if (target.ActualWidth <= 0 || target.ActualHeight <= 0) continue;

            Point center = target.TranslatePoint(new Point(target.ActualWidth / 2.0, target.ActualHeight / 2.0), itemsControl);
            double dist = verticalStrip
                ? Math.Abs(cursorInItemsControl.Y - center.Y)
                : Math.Sqrt(Math.Pow(cursorInItemsControl.X - center.X, 2) + Math.Pow(cursorInItemsControl.Y - center.Y, 2));

            double magScale = ContainerWarpMesh.ComputeDockMagnificationScale(dist, influenceRadiusDip, maxExtraScale);
            EnsureTransformGroup(target, out var scale, out _, out _);
            target.RenderTransformOrigin = new Point(0.5, 0.5);
            Panel.SetZIndex(container, magScale > 1.12 ? 15 : (magScale > 1.02 ? 5 : 0));

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = magScale;
            scale.ScaleY = magScale;
        }
    }

    public static void ResetDockMagnification(ItemsControl itemsControl)
    {
        var duration = TimeSpan.FromMilliseconds(150);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        int count = itemsControl.Items.Count;
        for (int i = 0; i < count; i++)
        {
            if (itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                continue;

            Panel.SetZIndex(container, 0);
            var target = FindDescendantButton(container) ?? container;
            EnsureTransformGroup(target, out var scale, out _, out _);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, duration) { EasingFunction = ease });
        }
    }

    public static void ShowPoofCloud(Point screenDipPosition)
    {
        try
        {
            const double size = 104.0;
            var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };

            // 7 classic macOS puff clouds arranged radially
            (double dx, double dy, double r0, double r1)[] puffs =
            [
                (0, 0, 14, 24),
                (-16, -10, 11, 20),
                (16, -10, 11, 20),
                (-18, 10, 10, 18),
                (18, 10, 10, 18),
                (0, -18, 10, 19),
                (0, 15, 9, 17)
            ];

            var cloudFill = new RadialGradientBrush(
                Color.FromArgb(235, 248, 250, 252),
                Color.FromArgb(140, 203, 213, 225));
            cloudFill.Freeze();
            var cloudStroke = new SolidColorBrush(Color.FromArgb(110, 148, 163, 184));
            cloudStroke.Freeze();

            var duration = TimeSpan.FromMilliseconds(320);
            var outEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            foreach (var (dx, dy, r0, r1) in puffs)
            {
                var ellipse = new Ellipse
                {
                    Width = r0 * 2,
                    Height = r0 * 2,
                    Fill = cloudFill,
                    Stroke = cloudStroke,
                    StrokeThickness = 1.0,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                var scale = new ScaleTransform(0.45, 0.45);
                var translate = new TranslateTransform(0, 0);
                var tg = new TransformGroup();
                tg.Children.Add(scale);
                tg.Children.Add(translate);
                ellipse.RenderTransform = tg;

                Canvas.SetLeft(ellipse, size / 2.0 - r0);
                Canvas.SetTop(ellipse, size / 2.0 - r0);
                canvas.Children.Add(ellipse);

                double targetScale = r1 / r0;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.45, targetScale, duration) { EasingFunction = outEase });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.45, targetScale, duration) { EasingFunction = outEase });
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx * 0.25, dx * 1.45, duration) { EasingFunction = outEase });
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy * 0.25, dy * 1.45 - 6.0, duration) { EasingFunction = outEase });
            }

            var poofWin = new Window
            {
                Width = size,
                Height = size,
                Left = screenDipPosition.X - size / 2.0,
                Top = screenDipPosition.Y - size / 2.0,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                IsHitTestVisible = false,
                Content = canvas
            };

            poofWin.SourceInitialized += (_, _) => WindowPlacement.ApplyToolWindowStyle(poofWin, ThemeService.Dark);
            poofWin.Show();

            var fadeAnim = new DoubleAnimationUsingKeyFrames { Duration = duration };
            fadeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            fadeAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(duration), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            fadeAnim.Completed += (_, _) =>
            {
                try { poofWin.Close(); } catch { }
            };
            canvas.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }
        catch
        {
        }
    }

    private static Button? FindDescendantButton(DependencyObject root)
    {
        if (root is Button b) return b;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            if (FindDescendantButton(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        }
        return null;
    }

    private static TransformGroup EnsureTransformGroup(
        UIElement element,
        out ScaleTransform scale,
        out SkewTransform skew,
        out TranslateTransform translate)
    {
        if (element.RenderTransform is TransformGroup existing &&
            existing.Children.Count == 3 &&
            existing.Children[0] is ScaleTransform s &&
            existing.Children[1] is SkewTransform k &&
            existing.Children[2] is TranslateTransform t)
        {
            scale = s;
            skew = k;
            translate = t;
            return existing;
        }

        scale = new ScaleTransform(1.0, 1.0);
        skew = new SkewTransform(0.0, 0.0);
        translate = new TranslateTransform(0.0, 0.0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(skew);
        group.Children.Add(translate);
        element.RenderTransform = group;
        return group;
    }
}
