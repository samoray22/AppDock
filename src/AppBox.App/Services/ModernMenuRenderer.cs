using System.Drawing;
using System.Windows.Forms;

namespace AppBox.App.Services;

internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly bool dark;
    private readonly Color backColor;
    private readonly Color textColor;
    private readonly Color hoverColor;
    private readonly Color borderColor;
    private readonly Color separatorColor;

    public ModernMenuRenderer(bool dark) : base(new ModernColorTable(dark))
    {
        this.dark = dark;
        backColor = dark ? Color.FromArgb(41, 43, 48) : Color.FromArgb(255, 255, 255);
        textColor = dark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(27, 32, 40);
        hoverColor = dark ? Color.FromArgb(38, 61, 86) : Color.FromArgb(227, 240, 255);
        borderColor = dark ? Color.FromArgb(75, 80, 88) : Color.FromArgb(217, 223, 232);
        separatorColor = dark ? Color.FromArgb(60, 65, 72) : Color.FromArgb(225, 230, 238);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(backColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(borderColor, 1);
        var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, r);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Don't render the classic left grey column margin
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected && e.Item.Enabled)
        {
            using var brush = new SolidBrush(hoverColor);
            var rect = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            e.Graphics.FillRectangle(brush, rect);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? textColor : (dark ? Color.FromArgb(120, 125, 133) : Color.FromArgb(160, 165, 175));
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(separatorColor, 1);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 28, y, e.Item.Width - 8, y);
    }

    private sealed class ModernColorTable : ProfessionalColorTable
    {
        private readonly bool dark;
        public ModernColorTable(bool dark) => this.dark = dark;
        public override Color MenuBorder => dark ? Color.FromArgb(75, 80, 88) : Color.FromArgb(217, 223, 232);
        public override Color ToolStripDropDownBackground => dark ? Color.FromArgb(41, 43, 48) : Color.FromArgb(255, 255, 255);
        public override Color MenuItemSelected => dark ? Color.FromArgb(38, 61, 86) : Color.FromArgb(227, 240, 255);
        public override Color MenuItemBorder => Color.Transparent;
    }
}
