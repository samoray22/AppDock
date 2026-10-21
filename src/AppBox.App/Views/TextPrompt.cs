using System.Windows;
using System.Windows.Controls;

namespace AppBox.App.Views;

public static class TextPrompt
{
    public static string? Ask(Window owner, string title, string initial)
    {
        var input = new TextBox { Text = initial, MaxLength = 60, MinWidth = 300 };
        var button = new Button { Content = "Save", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, Margin = new Thickness(3,0,0,14) }); panel.Children.Add(input); panel.Children.Add(button);
        var window = new Window { Title = title, Owner = owner, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        window.SetResourceReference(FrameworkElement.StyleProperty, "AppWindow");
        button.Click += (_, _) => { if (string.IsNullOrWhiteSpace(input.Text)) return; window.DialogResult = true; };
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text : null;
    }
}
