using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AppBox.App.Services;

namespace AppBox.App.Views;

public partial class LauncherWindow : Window
{
    public bool SuppressDismiss { get; set; }
    public Action<string>? OpenSettings { get; set; }
    public LauncherWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPlacement.Round(this, ThemeService.Dark);
        Deactivated += (_, _) => { if (!SuppressDismiss) Hide(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }
    public void Present(bool animations)
    {
        BeginAnimation(OpacityProperty, null); Opacity = 1;
        WindowPlacement.Place(this);
        Show(); Activate(); WindowPlacement.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (animations && SystemParameters.ClientAreaAnimation)
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke("General");
    private void Manage_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke("Folders");
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Hide();
}
