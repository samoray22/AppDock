using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AppBox.App.Services;

internal static class TrayIconGlyphs
{
    private static readonly string[] PreferredFonts = ["Segoe Fluent Icons", "Segoe MDL2 Assets"];

    public static Bitmap Create(string glyph, bool dark, int size = 16)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Pick font
        Font? font = null;
        foreach (var familyName in PreferredFonts)
        {
            try
            {
                var f = new Font(familyName, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
                if (f.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase))
                {
                    font = f;
                    break;
                }
                f.Dispose();
            }
            catch { }
        }
        font ??= new Font(FontFamily.GenericSansSerif, 9.5f, FontStyle.Regular, GraphicsUnit.Point);

        var color = dark ? Color.FromArgb(240, 245, 245, 245) : Color.FromArgb(240, 45, 50, 58);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
        font.Dispose();

        return bmp;
    }
}
