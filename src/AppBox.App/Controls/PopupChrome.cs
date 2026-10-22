using System.Windows;
using System.Windows.Media;
using AppBox.Core;
namespace AppBox.App.Controls;

// The body and pointer form one stroked geometry, so their border and shadow have no seam.
public sealed class PopupChrome : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(PopupChrome), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(PopupChrome), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double PointerCenter { get; set; }
    public bool ShowPointer { get; set; }
    protected override void OnRender(DrawingContext dc)
    {
        var depth = ShowPointer ? PopupPointer.Depth : 0;
        if (ActualWidth < 2 || ActualHeight <= depth + 2) return;
        var body = new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - depth - 1);
        Geometry geometry = new RectangleGeometry(body, 16, 16);
        if (ShowPointer)
        {
            var center = Math.Clamp(PointerCenter, 26, Math.Max(26, ActualWidth - 26));
            var tip = new StreamGeometry();
            using (var context = tip.Open()) { context.BeginFigure(new Point(center - 10, body.Bottom - 1), true, true); context.LineTo(new Point(center, ActualHeight - 1), true, false); context.LineTo(new Point(center + 10, body.Bottom - 1), true, false); }
            geometry = Geometry.Combine(geometry, tip, GeometryCombineMode.Union, null);
        }
        dc.DrawGeometry(Fill, new Pen(Stroke, 1), geometry);
    }
}
