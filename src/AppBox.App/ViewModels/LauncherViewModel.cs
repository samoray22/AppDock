using System.Collections.ObjectModel;
using System.Windows.Media;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.ViewModels;

public sealed record AppTile(AppEntry Entry, ImageSource? Icon, bool ShowName)
{
    public string Name => Entry.Name;
}

public sealed class LauncherViewModel : ObservableObject
{
    private readonly Configuration config;
    private AppFolder? selected;
    public ObservableCollection<AppFolder> Folders { get; } = [];
    public ObservableCollection<AppTile> Applications { get; } = [];
    public bool IsEmpty => Applications.Count == 0;
    public bool HasFolders => Folders.Count > 0;
    public string Subtitle => config.Settings.LayoutMode == LayoutMode.SingleAppBox ? "Your apps, a little closer." : "Multiple AppBoxes · folder selection";
    public AppFolder? SelectedFolder
    {
        get => selected;
        set { selected = value; config.SelectedFolderId = value?.Id; RefreshApps(); Changed(); }
    }
    public RelayCommand LaunchCommand { get; }
    public LauncherViewModel(Configuration config, Action<AppEntry> launch)
    {
        this.config = config;
        LaunchCommand = new(p => { if (p is AppTile tile) launch(tile.Entry); });
        Refresh();
    }
    public void Refresh()
    {
        var selectedId = config.SelectedFolderId;
        Folders.Clear(); foreach (var folder in config.Folders) Folders.Add(folder);
        SelectedFolder = config.Folders.FirstOrDefault(f => f.Id == selectedId) ?? config.Folders.FirstOrDefault();
        Changed(nameof(HasFolders)); Changed(nameof(Subtitle));
    }
    private void RefreshApps()
    {
        Applications.Clear();
        if (selected is not null)
            foreach (var app in selected.Applications) Applications.Add(new(app, ShellIcon.Get(app), config.Settings.ShowApplicationNames));
        Changed(nameof(IsEmpty));
    }
}
