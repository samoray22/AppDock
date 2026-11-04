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
    private EdgeSidebarWindow? edgeSidebar;
    private TaskbarHoverWatcher? taskbarHover;
    private LauncherViewModel model = null!;
    private SettingsWindow? settings;
    private ContainerLaunchers launchers = null!;
    private bool diagnosticRun;
    private ApplicationLaunchService launchService = null!;
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A secondary process has no window yet: install the UI context before its first await.
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(Dispatcher));
        SetCurrentProcessExplicitAppUserModelID("AppBox.Desktop");
        try
        {
            var request = ActivationRequest.ParseArguments(e.Args);
            diagnosticRun = e.Args.Contains("--smoke-test");
            if (diagnosticRun) Environment.SetEnvironmentVariable("APPDOCK_DIAGNOSTIC_SESSION", Guid.NewGuid().ToString());
            instance = new(diagnosticRun || e.Args.Contains("--smoke-secondary") ? Environment.GetEnvironmentVariable("APPDOCK_DIAGNOSTIC_SESSION") : null);
            if (!instance.IsPrimary)
            {
                if (e.Args.Contains("--smoke-test")) throw new InvalidOperationException("Exit the running AppDock instance before starting a diagnostic run.");
                if (!e.Args.Contains("--background")) await instance.SignalAsync(request);
                Shutdown(); return;
            }
            store = new(e.Args.Contains("--smoke-test") ? Path.Combine(Path.GetTempPath(), "AppBox-Smoke-" + Guid.NewGuid().ToString("N")) : null);
            config = store.Load();
            launchService = new(new WindowsShortcutResolver(), new WindowsProcessLauncher(), () => config.Settings.AllowCommandLineShells);
            if (!e.Args.Contains("--smoke-test"))
            {
                // The exe may have been moved or renamed since "Start with Windows" was enabled.
                try { StartupRegistration.RepairPath(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
                config.Settings.StartWithWindows = StartupRegistration.IsEnabled();
            }
            ThemeService.ConfigureWindows11Style(config.Settings.UseWindows11Style);
            ThemeService.Apply(config.Settings.Theme);
            LanguageResourceService.Apply(config.Settings.Language);
            launchers = new(store.DirectoryPath, AppPaths.Executable);
            launchers.Sync(config);
            if (!diagnosticRun)
            {
                if (!config.Settings.EnableTaskbar)
                {
                    launchers.UnpinMainFromTaskbar();
                    foreach (var folder in config.Folders)
                        launchers.UnpinFromTaskbar(folder);
                }
                else if (config.Settings.LayoutMode == LayoutMode.SingleAppBox)
                {
                    launchers.PinMainToTaskbar();
                }
                else
                {
                    foreach (var inactive in config.Folders.Where(f => !f.IsActive && File.Exists(launchers.ShortcutPath(f.Id))))
                        launchers.UnpinFromTaskbar(inactive);
                }
            }
            model = new(config, (Action<AppEntry, LaunchOptions>)Launch, Persist, iconDirectory: store.DirectoryPath);
            launcher = new() { DataContext = model, OpenSettings = OpenSettings, ChangeIcon = ChangeIcon, IsTaskbarEnabled = () => config.Settings.EnableTaskbar };
            launcher.SourceInitialized += (_, _) => UpdateIdentity();
            model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LauncherViewModel.SelectedFolder) && launcher.IsLoaded) UpdateIdentity(); };
            MainWindow = launcher;
            if (!diagnosticRun)
            {
                edgeSidebar = new(config, model, launcher, Persist, OpenSettings);
                taskbarHover = new(config, model, launcher, launchers, () => settings is not null, ActivateLauncherFromTaskbarHover);
                launcher.IsPointerOverSidebar = () => edgeSidebar.IsCursorOverSidebar();
                launcher.IsPointerOverTaskbarButton = () => taskbarHover.IsCursorOverHoveredTaskbarButton();
                launcher.ClosedFromLaunchOrDismiss = () =>
                {
                    edgeSidebar.CollapseAfterLaunch();
                    taskbarHover.NotifyLauncherClosed();
                };
                edgeSidebar.SyncWithConfig();
                taskbarHover.SyncWithConfig();
            }
            instance.Listen(value => Dispatcher.BeginInvoke(() => HandleActivation(value)));
            CreateTray();
            try { launchers.UpdateMain(new ContainerIcons(store.DirectoryPath).ProductIconPath); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { tray?.ShowBalloonTip(5000, "AppDock", LocalizationService.Format("Tray.MainShortcutError", ex.Message), Forms.ToolTipIcon.Info); }
            if (!diagnosticRun)
            {
                PublishJumpLists();
                ShellIcon.PreloadInBackground(config.Folders.SelectMany(f => f.Applications));
                ApplicationDiscovery.WarmUpInBackground();
            }
            SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
            if (!e.Args.Contains("--background")) HandleActivation(request);
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
            AppDockDialog.Show(null, LocalizationService.Format("Dialog.StartFailed", ex.Message), "AppDock", MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal void HandleActivation(ActivationRequest request)
    {
        if (request.Command == ActivationCommand.Open) { ActivateLauncher(request.ContainerId?.ToString() ?? ""); return; }
        var container = request.Resolve(config, requireActive: request.Command == ActivationCommand.EditContainer);
        if (request.ContainerId is not null && container is null)
        { tray?.ShowBalloonTip(5000, "AppDock", LocalizationService.Get("Tray.ContainerUnavailable"), Forms.ToolTipIcon.Info); return; }
        if (container is not null) { model.SelectedFolder = container; settings?.SelectContainer(container); }
        if (request.Command == ActivationCommand.EditContainer)
        {
            if (container is null) { OpenSettings("Containers"); return; }
            settings?.Close(); launcher.SetLateralMode(!config.Settings.EnableTaskbar); model.OpenContainer(container); launcher.Present(config.Settings.EnableAnimations); model.SetEditing(true);
        }
        else OpenSettings(request.Command == ActivationCommand.Settings ? "General" : "Containers");
    }
    private void PublishJumpLists()
    {
        try
        {
            var executable = AppPaths.Executable;
            var productIcon = new ContainerIcons(store.DirectoryPath).ProductIconPath;
            WindowsJumpLists.Publish(WindowsJumpLists.MainIdentity, executable, productIcon, null);
            if (config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes)
            {
                foreach (var container in config.Folders.Where(f => f.IsActive || File.Exists(launchers.ShortcutPath(f.Id))))
                {
                    var identity = ContainerLaunchers.Identity(container.Id);
                    WindowsJumpLists.Publish(identity, executable, productIcon, container.Id);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { tray?.ShowBalloonTip(5000, "AppDock", LocalizationService.Format("Tray.JumpListError", ex.Message), Forms.ToolTipIcon.Info); }
    }
    private void ActivateLauncher(string folderId = "")
    {
        if (settings is not null) { settings.Activate(); return; }
        taskbarHover?.NotifyOpenedByClick();
        bool alreadyVisible = launcher.IsVisible && !launcher.IsLateralFlyout;
        bool sameView = false;
        launcher.SetLateralMode(!config.Settings.EnableTaskbar);
        if (config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes && Guid.TryParse(folderId, out var id))
        {
            var container = config.Folders.FirstOrDefault(f => f.Id == id && f.IsActive);
            if (container is null) { tray?.ShowBalloonTip(5000, "AppDock", LocalizationService.Get("Tray.ContainerInactive"), Forms.ToolTipIcon.Info); return; }
            sameView = alreadyVisible && !model.IsOverview && model.SelectedFolder?.Id == container.Id;
            model.OpenContainer(container);
        }
        if (string.IsNullOrEmpty(folderId) || config.Settings.LayoutMode == LayoutMode.SingleAppBox)
        {
            sameView = alreadyVisible && model.IsOverview;
            model.ShowHome();
        }
        UpdateIdentity();
        launcher.Present(!sameView && config.Settings.EnableAnimations);
    }

    private void ActivateLauncherFromTaskbarHover(TaskbarHoverTarget target, WindowPlacement.Anchor anchor)
    {
        if (settings is not null) return;
        launcher.SetLateralMode(false);
        bool alreadyVisible = launcher.IsVisible && !launcher.IsLateralFlyout;
        bool sameView = false;
        if (!target.IsMainLauncher && config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes && target.ContainerId is { } id)
        {
            var container = config.Folders.FirstOrDefault(f => f.Id == id && f.IsActive);
            if (container is null) return;
            sameView = alreadyVisible && !model.IsOverview && model.SelectedFolder?.Id == container.Id;
            model.OpenContainer(container);
        }
        else
        {
            sameView = alreadyVisible && model.IsOverview;
            model.ShowHome();
        }
        UpdateIdentity();
        launcher.Present(!sameView && config.Settings.EnableAnimations, anchor);
    }
    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var dark = ThemeService.Dark;
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new ModernMenuRenderer(dark),
            ShowImageMargin = true
        };

        menu.Items.Add(LocalizationService.Get("Tray.Settings"), TrayIconGlyphs.Create("\uE713", dark), (_, _) => Dispatcher.Invoke(() => OpenSettings("General")));
        menu.Items.Add(LocalizationService.Get("Tray.SidebarSettings"), TrayIconGlyphs.Create("\uE8A0", dark), (_, _) => Dispatcher.Invoke(() => OpenSettings("Sidebar")));
        menu.Items.Add(LocalizationService.Get("Tray.ManageContainers"), TrayIconGlyphs.Create("\uE8B7", dark), (_, _) => Dispatcher.Invoke(() => OpenSettings("Containers")));
        menu.Items.Add(LocalizationService.Get("Tray.OpenAllContainers"), TrayIconGlyphs.Create("\uE71D", dark), (_, _) => Dispatcher.Invoke(() => { launcher.SetLateralMode(!config.Settings.EnableTaskbar); model.OpenOverview(); launcher.Present(config.Settings.EnableAnimations); }));
        menu.Items.Add(LocalizationService.Get("Tray.HelpAndGuide"), TrayIconGlyphs.Create("\uE897", dark), (_, _) => Dispatcher.Invoke(() => OpenSettings("Help")));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Get("Tray.Exit"), TrayIconGlyphs.Create("\uE7E8", dark), (_, _) => Dispatcher.Invoke(() => { Save(); Shutdown(); }));
        return menu;
    }
    private void UpdateTrayMenu()
    {
        if (tray is null) return;
        var old = tray.ContextMenuStrip;
        tray.ContextMenuStrip = BuildTrayMenu();
        old?.Dispose();
    }
    private void CreateTray()
    {
        var menu = BuildTrayMenu();
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        tray = new() { Text = "AppDock", Icon = icon, Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(() => OpenSettings("General")); };
    }
    internal void LaunchForDiagnostics(AppEntry entry) => Launch(entry, new());
    private void Launch(AppEntry entry) => Launch(entry, new());
    private void Launch(AppEntry entry, LaunchOptions options)
    {
        if (!model.Interaction.CanLaunch) return;
        var result = launchService.Launch(entry, options);
        if (result.Success)
        {
            entry.LastLaunchedUtc = DateTime.UtcNow;
            DebouncedSave();
            model.Refresh();
        }
        if (!options.KeepOpen && model.Interaction.DismissAfterLaunch(result.Success, config.Settings.CloseAfterLaunch)) launcher.Hide();
        else if (result.Error is not null)
        {
            model.Report(result.Error);
            tray?.ShowBalloonTip(5000, "AppDock", result.Error, Forms.ToolTipIcon.Warning);
        }
    }

    private System.Windows.Threading.DispatcherTimer? saveDebounceTimer;
    private void DebouncedSave()
    {
        if (saveDebounceTimer is null)
        {
            saveDebounceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            saveDebounceTimer.Tick += (_, _) =>
            {
                saveDebounceTimer.Stop();
                try { store.Save(config); } catch { }
            };
        }
        saveDebounceTimer.Stop();
        saveDebounceTimer.Start();
    }
    private void UpdateIdentity()
    {
        try
        {
            bool showOnTaskbar = config.Settings.EnableTaskbar && !launcher.IsLateralFlyout;
            WindowPlacement.SetTaskbarVisibility(launcher, showOnTaskbar);
            if (!showOnTaskbar) return;
            launchers.ApplyWindow(launcher,
                config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes && model.SelectedFolder is { IsActive: true } folder ? folder : null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }
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
        catch (Exception ex) { ShowMessage(LocalizationService.Format("Dialog.IconNotSaved", ex.Message)); }
        finally { launcher.SuppressDismiss = settings is not null; }
    }
    private void Persist()
    {
        store.Save(config);
        ThemeService.ConfigureWindows11Style(config.Settings.UseWindows11Style);
        LanguageResourceService.Apply(config.Settings.Language);
        UpdateTrayMenu();
        try
        {
            launchers.Sync(config);
            if (!diagnosticRun)
            {
                if (!config.Settings.EnableTaskbar)
                {
                    launchers.UnpinMainFromTaskbar();
                    foreach (var folder in config.Folders)
                        launchers.UnpinFromTaskbar(folder);
                }
                else if (config.Settings.LayoutMode == LayoutMode.SingleAppBox)
                {
                    launchers.PinMainToTaskbar();
                    foreach (var folder in config.Folders)
                        launchers.UnpinFromTaskbar(folder);
                }
                else
                {
                    launchers.UnpinMainFromTaskbar();
                    foreach (var inactive in config.Folders.Where(f => !f.IsActive && File.Exists(launchers.ShortcutPath(f.Id))))
                        launchers.UnpinFromTaskbar(inactive);
                }
                PublishJumpLists();
            }
            if (model is not null && launcher is not null)
            {
                model.Refresh();
                if (launcher.IsLoaded) UpdateIdentity();
            }
            edgeSidebar?.SyncWithConfig();
            taskbarHover?.SyncWithConfig();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { tray?.ShowBalloonTip(5000, "AppDock", LocalizationService.Format("Tray.ShortcutSaveError", ex.Message), Forms.ToolTipIcon.Warning); }
    }
    private void OpenSettings(string section)
    {
        if (settings is not null)
        {
            settings.SelectSection(section);
            if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
            settings.Activate();
            WindowPlacement.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(settings).Handle);
            return;
        }
        var wasLauncherVisible = launcher.IsVisible;
        launcher.SuppressDismiss = true;
        var reopeningForEdit = false;
        settings = new(new SettingsViewModel(config, Persist), section)
        {
            Owner = wasLauncherVisible ? launcher : null,
            ShowInTaskbar = !wasLauncherVisible,
            WindowStartupLocation = wasLauncherVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            LaunchersDirectory = launchers.DirectoryPath,
            ChangeIcon = ChangeIcon,
            EditContainer = folder =>
            {
                reopeningForEdit = true;
                settings?.Close();
                model.OpenContainer(folder);
                launcher.Present(false);
                model.SetEditing(true);
            }
        };
        settings.Closed += (_, _) =>
        {
            settings = null;
            launcher.SuppressDismiss = false;
            if (!reopeningForEdit)
            {
                model.ShowHome();
                UpdateIdentity();
                if (wasLauncherVisible) launcher.Present(config.Settings.EnableAnimations);
            }
        };
        settings.Show();
        settings.Activate();
        WindowPlacement.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(settings).Handle);
    }
    private void Save()
    {
        try { Persist(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { ShowMessage(LocalizationService.Format("Common.SettingsSavedError", ex.Message)); }
    }
    private void ShowMessage(string text)
    {
        launcher.SuppressDismiss = true;
        try { AppDockDialog.Show(settings ?? (Window)launcher, text, "AppDock", MessageBoxImage.Information); }
        finally { launcher.SuppressDismiss = settings is not null; }
    }
    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (config.Settings.Theme == AppTheme.System) ThemeService.Apply(AppTheme.System);
            edgeSidebar?.ApplyWindowStyle();
        });
    }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        if (tray is not null) { tray.Visible = false; tray.Icon?.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        edgeSidebar?.Cleanup();
        edgeSidebar?.Close();
        taskbarHover?.Cleanup();
        launcher?.Cleanup();
        instance?.Dispose(); base.OnExit(e);
    }
}
