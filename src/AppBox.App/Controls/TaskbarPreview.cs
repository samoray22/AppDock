using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppBox.Infrastructure;
namespace AppBox.App.Controls;
public sealed class TaskbarPreview : UserControl
{
    public static readonly DependencyProperty MultipleProperty = DependencyProperty.Register(nameof(Multiple), typeof(bool), typeof(TaskbarPreview), new PropertyMetadata(false, (o, _) => ((TaskbarPreview)o).Build()));
    public bool Multiple { get => (bool)GetValue(MultipleProperty); set => SetValue(MultipleProperty, value); }
    public TaskbarPreview() { IsHitTestVisible = false; Focusable = false; Build(); }
    private void Build()
    {
        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (Multiple)
            foreach (var category in new[] { "Development", "Office", "Graphics", "Utilities" }) Add(icons, ContainerIcons.BuiltIn(category), 30);
        else
        {
            Add(icons, ContainerIcons.BuiltIn("General Applications"), 23, 0.35);
            Add(icons, ContainerIcons.BuiltIn("General Applications"), 23, 0.35);
            Add(icons, new BitmapImage(new Uri("pack://application:,,,/AppDock;component/Resources/Branding/AppDock64.png")), 34);
        }
        var frame = new Border { CornerRadius = new CornerRadius(7), Height = 54, Child = icons };
        frame.SetResourceReference(Border.BackgroundProperty, "PreviewSurface"); Content = frame;
    }
    private static void Add(Panel panel, ImageSource source, double size, double opacity = 1)
    {
        var image = new Image { Source = source, Width = size, Height = size, Margin = new Thickness(7, 0, 7, 0), Opacity = opacity };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality); panel.Children.Add(image);
    }
}
