using System.Collections.ObjectModel;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    public Configuration Config { get; }
    private readonly Action<bool> writeStartup;
    private readonly Func<bool> readStartup;
    private string preferenceError = "";
    public string PreferenceError { get => preferenceError; private set { preferenceError = value; Changed(); } }
    public bool Windows11StyleAvailable { get; }
    public bool IsSingleLayout { get => Config.Settings.LayoutMode == LayoutMode.SingleAppBox; set { if (value) SetPreference(Config.Settings.LayoutMode, LayoutMode.SingleAppBox, v => Config.Settings.LayoutMode = v); } }
    public bool IsMultipleLayout { get => Config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes; set { if (value) SetPreference(Config.Settings.LayoutMode, LayoutMode.MultipleAppBoxes, v => Config.Settings.LayoutMode = v); } }
    public bool ShowAppNames { get => Config.Settings.ShowApplicationNames; set => SetPreference(ShowAppNames, value, v => Config.Settings.ShowApplicationNames = v); }
    public bool AutoCloseAfterLaunch { get => Config.Settings.CloseAfterLaunch; set => SetPreference(AutoCloseAfterLaunch, value, v => Config.Settings.CloseAfterLaunch = v); }
    public bool UseWindows11Style { get => Config.Settings.UseWindows11Style; set { if (Windows11StyleAvailable) SetPreference(UseWindows11Style, value, v => Config.Settings.UseWindows11Style = v); } }
    public bool StartWithWindows
    {
        get => Config.Settings.StartWithWindows;
        set
        {
            if (value == StartWithWindows) return;
            var previous = StartWithWindows;
            try { writeStartup(value); Config.Settings.StartWithWindows = readStartup(); save(); PreferenceError = ""; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                try { writeStartup(previous); Config.Settings.StartWithWindows = readStartup(); }
                catch { Config.Settings.StartWithWindows = previous; }
                PreferenceError = "Startup preference could not be saved: " + ex.Message;
            }
            NotifyPreferences();
        }
    }
    private void SetPreference<T>(T previous, T value, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(previous, value)) return;
        assign(value);
        try { save(); PreferenceError = ""; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { assign(previous); PreferenceError = "This setting could not be saved: " + ex.Message; }
        NotifyPreferences();
    }
    private void NotifyPreferences()
    {
        foreach (var property in new[] { nameof(IsSingleLayout), nameof(IsMultipleLayout), nameof(ShowAppNames), nameof(AutoCloseAfterLaunch), nameof(StartWithWindows), nameof(UseWindows11Style) }) Changed(property);
    }
    private readonly FolderManager manager;
    private readonly Action save;
    private AppFolder? selectedFolder;
    public ObservableCollection<AppFolder> Folders { get; } = [];
    public ObservableCollection<AppEntry> Applications { get; } = [];
    public AppEntry? SelectedApplication { get; set; }
    public AppFolder? SelectedFolder
    {
        get => selectedFolder;
        set { selectedFolder = value; Config.SelectedFolderId = value?.Id; Changed(); Changed(nameof(FolderName)); RefreshApps(); }
    }
    public string FolderName { get => selectedFolder?.Name ?? ""; set { if (selectedFolder is not null) { manager.Rename(selectedFolder, value); Changed(); } } }
    public SettingsViewModel(Configuration config, Action save, Action<bool>? writeStartup = null, Func<bool>? readStartup = null, bool? windows11StyleAvailable = null)
    {
        Config = config; manager = new(config); this.save = save;
        this.writeStartup = writeStartup ?? StartupRegistration.SetEnabled;
        this.readStartup = readStartup ?? StartupRegistration.IsEnabled;
        Windows11StyleAvailable = windows11StyleAvailable ?? OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
        Refresh();
    }
    public void ToggleActive() { if (SelectedFolder is not null) { SelectedFolder.IsActive = !SelectedFolder.IsActive; Persist(); } }
    public void Create(string name) { manager.Create(name); Persist(); }
    public void Rename(string name) { if (SelectedFolder is not null) { manager.Rename(SelectedFolder, name); Persist(); } }
    public void Delete() { if (SelectedFolder is not null) { manager.Delete(SelectedFolder); Persist(); } }
    public void Add(AppEntry entry)
    {
        if (SelectedFolder is null) throw new ArgumentException("Create or select a container first.");
        var result = new ApplicationImportService(new WindowsShortcutResolver()).Import(Config, SelectedFolder, [entry.Kind == TargetKind.Packaged ? "shell:AppsFolder\\" + entry.Target : entry.Target]);
        if (result.Rejected.Count > 0) throw new ArgumentException(result.Rejected[0].Reason);
        Persist();
    }
    public void Remove() { if (SelectedFolder is not null && SelectedApplication is not null) { manager.Remove(SelectedFolder, SelectedApplication); Persist(); } }
    public void Move(int offset) { if (SelectedFolder is not null && SelectedApplication is not null) { var app = SelectedApplication; manager.Move(SelectedFolder, app, offset); Persist(); SelectedApplication = app; Changed(nameof(SelectedApplication)); } }
    public void Persist() { save(); Refresh(); }
    private void Refresh()
    {
        var id = Config.SelectedFolderId;
        Folders.Clear(); foreach (var folder in Config.Folders) Folders.Add(folder);
        SelectedFolder = Config.Folders.FirstOrDefault(f => f.Id == id) ?? Config.Folders.FirstOrDefault();
    }
    private void RefreshApps()
    {
        Applications.Clear(); if (SelectedFolder is not null) foreach (var app in SelectedFolder.Applications) Applications.Add(app);
        SelectedApplication = null; Changed(nameof(SelectedApplication));
    }
}
