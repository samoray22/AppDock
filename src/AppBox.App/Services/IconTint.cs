using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AppBox.App.Services;

public static class IconTint
{
    private static readonly Dictionary<string, (Color Tint, Color Soft, Color Text)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static (Color Tint, Color Soft, Color Text)? Extract(ImageSource? image, bool dark, string? cacheKey = null)
    {
        if (image is null) return null;
        var key = cacheKey is not null ? cacheKey + (dark ? "-dark" : "-light") : null;
        if (key is not null && Cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            var bitmap = RenderToSmallBitmap(image, 16, 16);
            if (bitmap is null) return null;

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            double sumR = 0, sumG = 0, sumB = 0;
            double maxSaturation = 0;
            int weightedCount = 0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a < 64) continue; // Skip transparent pixels

                ColorToHsv(r, g, b, out _, out double s, out double v);
                if (s < 0.22 || v < 0.15) continue; // Skip low saturation (grey/white) and low brightness (black)

                if (s > maxSaturation) maxSaturation = s;

                int weight = (int)(s * 100);
                sumR += r * weight;
                sumG += g * weight;
                sumB += b * weight;
                weightedCount += weight;
            }

            if (weightedCount == 0 || maxSaturation < 0.25)
                return null; // Mostly grey or low saturation -> fallback to standard Accent

            byte avgR = (byte)Math.Clamp(sumR / weightedCount, 0, 255);
            byte avgG = (byte)Math.Clamp(sumG / weightedCount, 0, 255);
            byte avgB = (byte)Math.Clamp(sumB / weightedCount, 0, 255);
            var tint = Color.FromRgb(avgR, avgG, avgB);

            // ContainerTintSoft: subtle hover tint blended toward theme surface
            byte softAlpha = (byte)(dark ? 55 : 32);
            var soft = Color.FromArgb(softAlpha, tint.R, tint.G, tint.B);

            // ContainerTintText: adjusted until reaching at least 4.5:1 contrast against Card
            var cardColor = dark ? Color.FromRgb(0x29, 0x2B, 0x30) : Color.FromRgb(0xFF, 0xFF, 0xFF);
            var text = EnsureContrast(tint, cardColor, 4.5, dark);

            var result = (tint, soft, text);
            if (key is not null) Cache[key] = result;
            return result;
        }
        catch
        {
            return null;
        }
    }

    public static Color EnsureContrast(Color color, Color background, double minRatio, bool darkTheme)
    {
        double bgLum = RelativeLuminance(background);
        Color candidate = color;

        for (int step = 0; step < 50; step++)
        {
            double cLum = RelativeLuminance(candidate);
            double ratio = (Math.Max(bgLum, cLum) + 0.05) / (Math.Min(bgLum, cLum) + 0.05);
            if (ratio >= minRatio) return candidate;

            if (darkTheme)
            {
                // Lighten for dark background
                candidate = Color.FromRgb(
                    (byte)Math.Min(255, candidate.R + 6),
                    (byte)Math.Min(255, candidate.G + 6),
                    (byte)Math.Min(255, candidate.B + 6));
            }
            else
            {
                // Darken for light background
                candidate = Color.FromRgb(
                    (byte)Math.Max(0, candidate.R - 6),
                    (byte)Math.Max(0, candidate.G - 6),
                    (byte)Math.Max(0, candidate.B - 6));
            }
        }

        return candidate;
    }

    public static double RelativeLuminance(Color c)
    {
        double r = Linearize(c.R / 255.0);
        double g = Linearize(c.G / 255.0);
        double b = Linearize(c.B / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double Linearize(double val) =>
        val <= 0.03928 ? val / 12.92 : Math.Pow((val + 0.055) / 1.055, 2.4);

    private static void ColorToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0) h = 0;
        else if (max == rd) h = 60 * (((gd - bd) / delta) % 6);
        else if (max == gd) h = 60 * (((bd - rd) / delta) + 2);
        else h = 60 * (((rd - gd) / delta) + 4);

        if (h < 0) h += 360;
    }

    private static BitmapSource? RenderToSmallBitmap(ImageSource source, int width, int height)
    {
        if (source is BitmapSource bSrc && bSrc.PixelWidth == width && bSrc.PixelHeight == height)
            return bSrc;

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));
        }
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
