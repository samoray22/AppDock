using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using AppBox.Core;
using AppBox.Infrastructure;
using AppBox.App.Services;
using AppBox.App.ViewModels;
using AppBox.App.Views;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AppBox.App;

public partial class App : System.Windows.Application
{
    private SingleInstance? instance;
    private Forms.NotifyIcon? tray;
    private ConfigurationStore store = null!;
    private Configuration config = null!;
    private LauncherWindow launcher = null!;
    private LauncherViewModel model = null!;
    private SettingsWindow? settings;
    private ContainerLaunchers launchers = null!;
    private readonly ApplicationLaunchService launchService = new(new WindowsShortcutResolver(), new WindowsProcessLauncher());
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A secondary process has no window yet: install the UI context before its first await.
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(Dispatcher));
        SetCurrentProcessExplicitAppUserModelID("AppBox.Desktop");
        try
        {
            var folder = GetFolderArgument(e.Args);
            instance = new();
            if (!instance.IsPrimary)
            {
                if (e.Args.Contains("--smoke-test")) throw new InvalidOperationException("Exit the running AppDock instance before starting a diagnostic run.");
                if (!e.Args.Contains("--background")) await instance.SignalAsync(folder);
                Shutdown(); return;
            }
            store = new(e.Args.Contains("--smoke-test") ? Path.Combine(Path.GetTempPath(), "AppBox-Smoke-" + Guid.NewGuid().ToString("N")) : null);
            config = store.Load();
            if (!e.Args.Contains("--smoke-test")) config.Settings.StartWithWindows = StartupRegistration.IsEnabled();
            ThemeService.Apply(config.Settings.Theme);
            launchers = new(store.DirectoryPath, Path.Combine(AppContext.BaseDirectory, "AppDock.exe"));
            launchers.Sync(config);
            model = new(config, Launch, Persist);
            launcher = new() { DataContext = model, OpenSettings = OpenSettings, ChangeIcon = ChangeIcon };
            launcher.SourceInitialized += (_, _) => UpdateIdentity();
            model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LauncherViewModel.SelectedFolder) && launcher.IsLoaded) UpdateIdentity(); };
            MainWindow = launcher;
            var selected = Guid.TryParse(folder, out var id) ? config.Folders.FirstOrDefault(f => f.Id == id && f.IsActive) : null;
            if (selected is not null && config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes) model.SelectedFolder = selected;
            instance.Listen(value => Dispatcher.BeginInvoke(() => ActivateLauncher(value)));
            CreateTray();
            SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
            if (!e.Args.Contains("--background")) ActivateLauncher(folder);
            if (store.RecoveryMessage is not null) ShowMessage(store.RecoveryMessage);
            if (e.Args.Contains("--smoke-test")) await SmokeTest.RunAsync(this, config, store, launcher, model, OpenSettings);
        }
        catch (Exception ex)
        {
            if (e.Args.Contains("--smoke-test") || e.Args.Contains("--smoke-secondary"))
            {
                Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "artifacts"));
                File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "artifacts", e.Args.Contains("--smoke-secondary") ? "secondary-error.txt" : "smoke-error.txt"), ex.ToString());
                Shutdown(1); return;
            }
            MessageBox.Show("AppDock could not start. " + ex.Message, "AppDock", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string GetFolderArgument(string[] args)
    {
        var index = Array.IndexOf(args, "--container");
        if (index < 0) index = Array.IndexOf(args, "--folder");
        return index >= 0 && index + 1 < args.Length && Guid.TryParse(args[index + 1], out var id) ? id.ToString() : "";
    }
    private void ActivateLauncher(string folderId = "")
    {
        if (settings is not null) { settings.Activate(); return; }
        if (config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes && Guid.TryParse(folderId, out var id))
        {
            var container = config.Folders.FirstOrDefault(f => f.Id == id && f.IsActive);
            if (container is null) { tray?.ShowBalloonTip(5000, "AppDock", "This container is inactive or was deleted. Open Settings to manage containers.", Forms.ToolTipIcon.Info); return; }
            model.OpenContainer(container);
        }
        if (string.IsNullOrEmpty(folderId) || config.Settings.LayoutMode == LayoutMode.SingleAppBox) model.ShowHome();
        launcher.Present(config.Settings.EnableAnimations);
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open AppDock", null, (_, _) => Dispatcher.Invoke(() => ActivateLauncher()));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => { ActivateLauncher(); OpenSettings("General"); }));
        menu.Items.Add("Exit AppDock", null, (_, _) => Dispatcher.Invoke(() => { Save(); Shutdown(); }));
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        tray = new() { Text = "AppDock", Icon = icon, Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(() => ActivateLauncher()); };
    }
    internal void LaunchForDiagnostics(AppEntry entry) => Launch(entry);
    private void Launch(AppEntry entry)
    {
        if (!model.Interaction.CanLaunch) return;
        var result = launchService.Launch(entry);
        if (result.Success) launcher.Hide();
        else if (result.Error is not null)
        {
            model.Report(result.Error);
            tray?.ShowBalloonTip(5000, "AppDock", result.Error, Forms.ToolTipIcon.Warning);
        }
    }
    private void UpdateIdentity() => launchers.ApplyWindow(launcher,
        config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes ? model.SelectedFolder : null);
    private void ChangeIcon(AppFolder folder)
    {
        launcher.SuppressDismiss = true;
        try
        {
            var dialog = new IconPicker(new(store.DirectoryPath), folder.Icon) { Owner = settings ?? (Window)launcher };
            if (dialog.ShowDialog() == true)
            {
                var previous = folder.Icon; folder.Icon = dialog.Choice;
                try { Persist(); } catch { folder.Icon = previous; throw; }
                UpdateIdentity(); model.Refresh();
            }
        }
        catch (Exception ex) { ShowMessage("The icon could not be saved. " + ex.Message); }
        finally { launcher.SuppressDismiss = settings is not null; }
    }
    private void Persist()
    {
        store.Save(config);
        try { launchers.Sync(config); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { tray?.ShowBalloonTip(5000, "AppDock", "Settings were saved, but launcher shortcuts could not be updated. " + ex.Message, Forms.ToolTipIcon.Warning); }
    }
    private void OpenSettings(string section)
    {
        if (settings is not null) { settings.Activate(); return; }
        launcher.SuppressDismiss = true;
        settings = new(new SettingsViewModel(config, Persist), section) { Owner = launcher, LaunchersDirectory = launchers.DirectoryPath, ChangeIcon = ChangeIcon, EditContainer = folder => { settings?.Close(); model.SelectedFolder = folder; launcher.Present(false); model.SetEditing(true); } };
        settings.Closed += (_, _) =>
        {
            settings = null; launcher.SuppressDismiss = false;
            model.ShowHome(); UpdateIdentity(); launcher.Present(config.Settings.EnableAnimations);
        };
        settings.Show();
    }
    private void Save()
    {
        try { Persist(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { ShowMessage("Changes are in memory but could not be saved. " + ex.Message); }
    }
    private void ShowMessage(string text)
    {
        launcher.SuppressDismiss = true;
        try { MessageBox.Show(settings ?? (Window)launcher, text, "AppDock", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { launcher.SuppressDismiss = settings is not null; }
    }
    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => { if (config.Settings.Theme == AppTheme.System) ThemeService.Apply(AppTheme.System); });
    }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        if (tray is not null) { tray.Visible = false; tray.Icon?.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        instance?.Dispose(); base.OnExit(e);
    }
}
