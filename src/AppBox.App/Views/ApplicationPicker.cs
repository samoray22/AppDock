using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.Views;

public sealed class ApplicationPicker : Window
{
    public AppEntry? Selected { get; private set; }
    public ApplicationPicker()
    {
        SetResourceReference(StyleProperty, "AppWindow");
        Title = "Add an application"; Width = 580; Height = 550; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = "Find your application", FontSize = 24, Margin = new Thickness(3,0,0,12) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var search = new TextBox { ToolTip = "Search Start menu and installed apps" }; System.Windows.Automation.AutomationProperties.SetName(search, "Search applications");
        DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        var status = new TextBlock { Text = "Reading the Windows application catalog…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3,8,3,8) };
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        var add = new Button { Content = "Add selected application", IsDefault = true, IsEnabled = false };
        DockPanel.SetDock(add, Dock.Bottom); panel.Children.Add(add);
        var list = new ListBox { DisplayMemberPath = "Name" }; panel.Children.Add(list); Content = panel;
        list.SelectionChanged += (_, _) => add.IsEnabled = list.SelectedItem is AppEntry;
        add.Click += (_, _) => { Selected = list.SelectedItem as AppEntry; if (Selected is not null) DialogResult = true; };
        Loaded += async (_, _) =>
        {
            try
            {
                // Shell COM automation requires an STA thread, keeping discovery off the UI thread.
                var completion = new TaskCompletionSource<IReadOnlyList<AppEntry>>();
                var thread = new Thread(() => { try { completion.SetResult(new ApplicationDiscovery().Discover()); } catch (Exception ex) { completion.SetException(ex); } }) { IsBackground = true };
                thread.SetApartmentState(ApartmentState.STA); thread.Start();
                var apps = await completion.Task;
                list.ItemsSource = apps;
                var view = CollectionViewSource.GetDefaultView(apps);
                view.Filter = item => ((AppEntry)item).Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase);
                search.TextChanged += (_, _) => view.Refresh();
                status.Text = $"{apps.Count} entries from Start menu and Windows AppsFolder. Some shortcuts may not be launchable.";
            }
            catch (Exception ex) { status.Text = "Discovery is unavailable. You can still browse for files. " + ex.Message; }
        };
    }
}
