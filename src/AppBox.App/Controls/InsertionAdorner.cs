using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AppBox.App.Controls;

internal sealed class InsertionAdorner(UIElement element, bool after, Brush brush) : Adorner(element)
{
    protected override void OnRender(DrawingContext drawing)
    {
        var x = after ? AdornedElement.RenderSize.Width + 1 : -1;
        var pen = new Pen(brush, 3);
        drawing.DrawLine(pen, new Point(x, 3), new Point(x, AdornedElement.RenderSize.Height - 3));
        drawing.DrawEllipse(brush, null, new Point(x, 3), 3, 3);
    }
}
