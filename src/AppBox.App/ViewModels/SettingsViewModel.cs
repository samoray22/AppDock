using System.Collections.ObjectModel;
using AppBox.Core;

namespace AppBox.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    public Configuration Config { get; }
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
    public SettingsViewModel(Configuration config, Action save)
    {
        Config = config; manager = new(config); this.save = save; Refresh();
    }
    public void Create(string name) { manager.Create(name); Persist(); }
    public void Rename(string name) { if (SelectedFolder is not null) { manager.Rename(SelectedFolder, name); Persist(); } }
    public void Delete() { if (SelectedFolder is not null) { manager.Delete(SelectedFolder); Persist(); } }
    public void Add(AppEntry entry) { if (SelectedFolder is null) throw new ArgumentException("Create or select a folder first."); manager.Add(SelectedFolder, entry); Persist(); }
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
