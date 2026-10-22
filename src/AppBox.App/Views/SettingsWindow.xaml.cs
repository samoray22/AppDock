using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AppBox.Core;
using AppBox.Infrastructure;
using AppBox.App.Services;
using AppBox.App.ViewModels;

namespace AppBox.App.Views;

public partial class SettingsWindow : Window
{
    public string LaunchersDirectory { get; set; } = "";
    public Action<AppFolder>? ChangeIcon { get; set; }
    public Action<AppFolder>? EditContainer { get; set; }
    private SettingsViewModel Model => (SettingsViewModel)DataContext;
    public SettingsWindow(SettingsViewModel model, string section)
    {
        InitializeComponent(); DataContext = model;
        SelectSection(section);
        SyncThemes();
        SourceInitialized += (_, _) => ApplyWindowStyle();
        Loaded += (_, _) => WindowPlacement.FitSettingsToWorkArea(this);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => WindowPlacement.FitSettingsToWorkArea(this)));
        model.PropertyChanged += ModelChanged;
        Closed += (_, _) => model.PropertyChanged -= ModelChanged;
    }
    public Guid? SelectedContainerId => Model.SelectedFolder?.Id;
    public void SelectContainer(AppFolder container) => Model.SelectedFolder = container;
    public void SelectSection(string section) => Sections.SelectedIndex = section switch { "Containers" or "Folders" => 1, "Taskbar Interaction" or "Taskbar" => 3, _ => 0 };
    private void Guard(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "AppDock", MessageBoxButton.OK, MessageBoxImage.Information); } }
    private void NewFolder_Click(object sender, RoutedEventArgs e) { var name = TextPrompt.Ask(this, "Create a Container", "New Container"); if (name is not null) Guard(() => Model.Create(name)); }
    private void Rename_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is null) return; var name = TextPrompt.Ask(this, "Rename Container", Model.SelectedFolder.Name); if (name is not null) Guard(() => Model.Rename(name)); }
    private void Delete_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is not null && MessageBox.Show(this, $"Delete “{Model.SelectedFolder.Name}” and its app references? Application files will be kept.", "Delete Container", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) Guard(Model.Delete); }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedFolder is null) { MessageBox.Show(this, "Create a Container first.", "AppDock"); return; }
        var dialog = new OpenFileDialog { Filter = "Applications and shortcuts|*.exe;*.lnk;*.url", Multiselect = true, DereferenceLinks = false };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames) Guard(() => Model.Add(new AppEntry { Name = Path.GetFileNameWithoutExtension(file), Target = file, Kind = Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? TargetKind.Shortcut : TargetKind.Executable }));
    }
    private void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedFolder is null) { MessageBox.Show(this, "Create a Container first.", "AppDock"); return; }
        var picker = new ApplicationPicker { Owner = this };
        if (picker.ShowDialog() == true && picker.Selected is not null) Guard(() => Model.Add(picker.Selected));
    }
    private void Remove_Click(object sender, RoutedEventArgs e) => Guard(Model.Remove);
    private void Up_Click(object sender, RoutedEventArgs e) => Guard(() => Model.Move(-1));
    private void Down_Click(object sender, RoutedEventArgs e) => Guard(() => Model.Move(1));
    private void Preference_Click(object sender, RoutedEventArgs e) => Guard(Model.Persist);
    public WindowStyleResult? NativeStyleState { get; private set; }
    public void ApplyWindowStyle()
    {
        if (DataContext is not SettingsViewModel || new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        NativeStyleState = NativeWindowStyle.Apply(this, Model.UseWindows11Style, ThemeService.Dark);
        StyleToggle.ToolTip = NativeStyleState.Detail;
        StyleFeedback.Text = "The system backdrop is unavailable; the standard theme is active.";
        StyleFeedback.Visibility = Model.UseWindows11Style && !NativeStyleState.SystemBackdropApplied ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(SettingsViewModel.UseWindows11Style)) ApplyWindowStyle(); }
    private void Theme_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var settings = Model.Config.Settings;
        settings.Theme = sender == FollowSystem ? (FollowSystem.IsChecked == true ? AppTheme.System : AppTheme.Light) : sender == DarkTheme ? AppTheme.Dark : AppTheme.Light;
        SyncThemes(); ThemeService.Apply(settings.Theme); Model.Persist();
    });
    private void SyncThemes() { var theme = Model.Config.Settings.Theme; FollowSystem.IsChecked = theme == AppTheme.System; LightTheme.IsChecked = theme == AppTheme.Light; DarkTheme.IsChecked = theme == AppTheme.Dark; }
    private void Icon_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is { } folder) { ChangeIcon?.Invoke(folder); Model.Persist(); } }
    private void Active_Click(object sender, RoutedEventArgs e) => Guard(Model.ToggleActive);
    private void Edit_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is { } folder) EditContainer?.Invoke(folder); }
    private void Launchers_Click(object sender, RoutedEventArgs e) => Guard(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + LaunchersDirectory + "\"") { UseShellExecute = true }));
    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
