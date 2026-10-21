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
    private readonly HashSet<string> approved = new(StringComparer.Ordinal);
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
                if (e.Args.Contains("--smoke-test")) throw new InvalidOperationException("Exit the running AppBox instance before starting a diagnostic run.");
                if (!e.Args.Contains("--background")) await instance.SignalAsync(folder);
                Shutdown(); return;
            }
            store = new(e.Args.Contains("--smoke-test") ? Path.Combine(Path.GetTempPath(), "AppBox-Smoke-" + Guid.NewGuid().ToString("N")) : null);
            config = store.Load();
            if (!e.Args.Contains("--smoke-test")) config.Settings.StartWithWindows = StartupRegistration.IsEnabled();
            ThemeService.Apply(config.Settings.Theme);
            model = new(config, Launch, () => store.Save(config));
            launcher = new() { DataContext = model, OpenSettings = OpenSettings };
            MainWindow = launcher;
            var selected = Guid.TryParse(folder, out var id) ? config.Folders.FirstOrDefault(f => f.Id == id) : null;
            if (selected is not null && config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes) model.SelectedFolder = selected;
            instance.Listen(value => Dispatcher.BeginInvoke(() => ActivateLauncher(value)));
            CreateTray();
            SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
            if (!e.Args.Contains("--background")) launcher.Present(config.Settings.EnableAnimations);
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
            MessageBox.Show("AppBox could not start. " + ex.Message, "AppBox", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string GetFolderArgument(string[] args)
    {
        var index = Array.IndexOf(args, "--folder");
        return index >= 0 && index + 1 < args.Length && Guid.TryParse(args[index + 1], out var id) ? id.ToString() : "";
    }
    private void ActivateLauncher(string folderId = "")
    {
        if (settings is not null) { settings.Activate(); return; }
        if (config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes && Guid.TryParse(folderId, out var id))
            model.SelectedFolder = config.Folders.FirstOrDefault(f => f.Id == id) ?? model.SelectedFolder;
        launcher.Present(config.Settings.EnableAnimations);
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open AppBox", null, (_, _) => Dispatcher.Invoke(() => ActivateLauncher()));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => { ActivateLauncher(); OpenSettings("General"); }));
        menu.Items.Add("Exit AppBox", null, (_, _) => Dispatcher.Invoke(() => { Save(); Shutdown(); }));
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        tray = new() { Text = "AppBox", Icon = icon, Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(() => ActivateLauncher()); };
    }
    internal void LaunchForDiagnostics(AppEntry entry) => ExecuteLaunch(entry, _ => true);
    private void Launch(AppEntry entry) => ExecuteLaunch(entry, target =>
    {
        var key = entry.Id + "\n" + target.FileName + "\n" + target.Arguments + "\n" + target.WorkingDirectory;
        if (approved.Contains(key)) return true;
        var detail = $"Open “{entry.Name}”?\n\nTarget: {target.FileName}\nArguments: {(target.Arguments.Length == 0 ? "(none)" : target.Arguments)}\nWorking directory: {(target.WorkingDirectory.Length == 0 ? "(default)" : target.WorkingDirectory)}\n\nOnly open applications you recognize. Approval lasts for this AppBox session.";
        if (MessageBox.Show(launcher, detail, "Open application", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        approved.Add(key); return true;
    });
    private void ExecuteLaunch(AppEntry entry, Func<ResolvedTarget, bool> approve)
    {
        if (!model.Interaction.CanLaunch) return;
        launcher.SuppressDismiss = true;
        try
        {
            var result = launchService.Launch(entry, approve);
            if (model.Interaction.DismissAfterLaunch(result.Success, config.Settings.CloseAfterLaunch)) launcher.Hide();
            else if (result.Error is not null) ShowMessage(result.Error);
        }
        finally { launcher.SuppressDismiss = false; }
    }
    private void OpenSettings(string section)
    {
        if (settings is not null) { settings.Activate(); return; }
        launcher.SuppressDismiss = true;
        settings = new(new SettingsViewModel(config, Save), section) { Owner = launcher };
        settings.Closed += (_, _) =>
        {
            settings = null; launcher.SuppressDismiss = false;
            model.Refresh(); launcher.Present(config.Settings.EnableAnimations);
        };
        settings.Show();
    }
    private void Save()
    {
        try { store.Save(config); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { ShowMessage("Changes are in memory but could not be saved. " + ex.Message); }
    }
    private void ShowMessage(string text)
    {
        launcher.SuppressDismiss = true;
        try { MessageBox.Show(settings ?? (Window)launcher, text, "AppBox", MessageBoxButton.OK, MessageBoxImage.Information); }
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
