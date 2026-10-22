using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AppBox.Core;
using AppBox.Infrastructure;
using Microsoft.Win32;
namespace AppBox.App.Views;

public sealed class IconPicker : Window
{
    private readonly ContainerIcons icons;
    private readonly Image preview = new() { Width = 96, Height = 96, Margin = new Thickness(12) };
    public ContainerIcon Choice { get; private set; }
    public IconPicker(ContainerIcons icons, ContainerIcon current)
    {
        this.icons = icons; Choice = current;
        Title = "Change Icon — AppDock"; Width = 440; Height = 470; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource("AppWindow");
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Default Icons", FontSize = 23 });
        panel.Children.Add(preview);
        var categories = new ComboBox { ItemsSource = ContainerIcons.Categories, SelectedItem = current.BuiltIn, Margin = new Thickness(0, 8, 0, 12) };
        categories.SelectionChanged += (_, _) => { if (categories.SelectedItem is string name) { Choice = new() { Kind = ContainerIconKind.BuiltIn, BuiltIn = name }; Refresh(); } };
        panel.Children.Add(categories);
        Add(panel, "Choose Custom Icon", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Icon sources|*.ico;*.png;*.exe;*.lnk", DereferenceLinks = false };
            if (dialog.ShowDialog(this) != true) return;
            try { Choice = icons.Import(dialog.FileName); Refresh(); }
            catch (Exception ex) { MessageBox.Show(this, "This icon could not be loaded. " + ex.Message, "AppDock"); }
        });
        Add(panel, "Reset to Default", () => { Choice = new(); Refresh(); });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        Add(actions, "Cancel", () => DialogResult = false); Add(actions, "Apply", () => DialogResult = true); panel.Children.Add(actions); Refresh();
    }
    private void Refresh() => preview.Source = icons.Get(Choice);
    private static void Add(Panel panel, string label, Action action) { var button = new Button { Content = label, Margin = new Thickness(3), Padding = new Thickness(12, 7, 12, 7) }; button.Click += (_, _) => action(); panel.Children.Add(button); }
}
