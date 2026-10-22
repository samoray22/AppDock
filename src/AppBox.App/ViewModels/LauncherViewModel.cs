using System.Collections.ObjectModel;
using System.Windows.Media;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.ViewModels;

public sealed record AppTile(AppEntry Entry, ImageSource? Icon, bool ShowName)
{
    public string Name => Entry.Name;
}

public sealed record ContainerTile(AppFolder Folder, ImageSource Icon);

public sealed class LauncherViewModel : ObservableObject
{
    private readonly Configuration config;
    private readonly FolderManager manager;
    private readonly Action persist;
    private readonly ApplicationImportService importer;
    public LauncherInteraction Interaction { get; } = new();
    private AppFolder? selected;
    private string status = "";
    private bool externalDrop;
    private int columns = 4;
    public ObservableCollection<ContainerTile> Containers { get; } = [];
    private bool overview;
    public bool IsOverview { get => overview; private set { overview = value; Changed(); Changed(nameof(ShowApplications)); Changed(nameof(ContentHeight)); Changed(nameof(DesiredHeight)); } }
    public bool ShowApplications => !IsOverview;
    public bool ShowPopupPointer => config.Settings.ShowPopupPointer;
    public int IconSize => config.Settings.IconSize;
    public void ShowHome() { IsOverview = config.Settings.LayoutMode == LayoutMode.SingleAppBox; Refresh(); }
    public void OpenContainer(AppFolder folder) { SelectedFolder = folder; IsOverview = false; }
    public void ToggleActive() => ChangeContainer(f => f.IsActive = !f.IsActive);
    public void RenameContainer(string name) => ChangeContainer(f => manager.Rename(f, name));
    private void ChangeContainer(Action<AppFolder> change)
    {
        if (selected is not { } folder) return;
        var name = folder.Name; var active = folder.IsActive;
        try { change(folder); persist(); Refresh(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { folder.Name = name; folder.IsActive = active; Report("Changes were not saved: " + ex.Message); }
    }
    public ObservableCollection<AppFolder> Folders { get; } = [];
    public ObservableCollection<AppTile> Applications { get; } = [];
    public bool IsEmpty => Applications.Count == 0;
    public bool HasFolders => Folders.Count > 0;
    public bool IsEditing => Interaction.IsEditing;
    public bool AnimationsEnabled => config.Settings.EnableAnimations;
    public string EditButtonText => IsEditing ? "Done" : "Edit";
    public string Status { get => status; private set { if (status == value) return; status = value; Changed(); Changed(nameof(HasStatus)); } }
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public bool ExternalDropActive { get => externalDrop; set { externalDrop = value; Changed(); } }
    public double ContentHeight => IsOverview ? LauncherSizing.GridHeight(Containers.Count, columns) : GridHeight;
    public double GridHeight => LauncherSizing.GridHeight(Applications.Count, columns);
    public double DesiredHeight => LauncherSizing.DesiredHeight(IsOverview ? Containers.Count : Applications.Count, IsEditing, columns);
    public void SetGridWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var next = Math.Max(1, (int)(width / LauncherSizing.TileWidth));
        if (next == columns) return;
        columns = next; Changed(nameof(GridHeight)); Changed(nameof(ContentHeight)); Changed(nameof(DesiredHeight));
    }
    public string Subtitle => IsEditing ? "Edit mode · changes save automatically" : config.Settings.LayoutMode == LayoutMode.SingleAppBox ? "Your apps, a little closer." : "Multiple AppDocks · container selection";
    public AppFolder? SelectedFolder
    {
        get => selected;
        set { selected = value; config.SelectedFolderId = value?.Id; RefreshApps(); Changed(); }
    }
    public RelayCommand LaunchCommand { get; }
    public RelayCommand ToggleEditCommand { get; }
    public LauncherViewModel(Configuration config, Action<AppEntry> launch, Action? persist = null, ApplicationImportService? importer = null)
    {
        this.config = config;
        manager = new(config);
        this.persist = persist ?? (() => { });
        this.importer = importer ?? new(new WindowsShortcutResolver());
        LaunchCommand = new(p => { if (Interaction.CanLaunch && p is AppTile tile) launch(tile.Entry); });
        ToggleEditCommand = new(_ => SetEditing(!IsEditing));
        ShowHome();
    }
    public void SetEditing(bool editing)
    {
        if (editing) { IsOverview = false; Interaction.BeginEdit(); } else Interaction.EndEdit();
        ExternalDropActive = false; Status = "";
        Changed(nameof(IsEditing)); Changed(nameof(EditButtonText)); Changed(nameof(Subtitle)); Changed(nameof(DesiredHeight));
    }
    public bool Import(IEnumerable<string> items)
    {
        if (!IsEditing) { Report("Click Edit before adding applications."); return false; }
        return Edit(() =>
        {
            selected ??= manager.Create("My apps");
            var result = importer.Import(config, selected, items);
            return $"{result.Added.Count} added" + (result.Rejected.Count == 0 ? "." : $"; {result.Rejected.Count} skipped.\n" + string.Join("\n", result.Rejected.Select(r => r.Item + ": " + r.Reason)));
        });
    }
    public bool Reorder(Guid folderId, Guid appId, int index)
    {
        if (!IsEditing || selected?.Id != folderId) return false;
        return Edit(() => { var app = selected.Applications.FirstOrDefault(a => a.Id == appId) ?? throw new ArgumentException("This dragged application is no longer in the container."); manager.Reorder(selected, app, index); return "Application order saved."; });
    }
    public void Rename(AppEntry entry, string name)
    {
        if (IsEditing && selected is not null) Edit(() => { manager.RenameApplication(selected, entry, name); return "Display name saved."; });
    }
    public void Remove(AppEntry entry)
    {
        if (IsEditing && selected is not null) Edit(() => { manager.Remove(selected, entry); return "Removed from container. Application files are unchanged."; });
    }
    public void Report(string text) => Status = text;
    private bool Edit(Func<string> change)
    {
        var folders = config.Folders.ToArray(); var previous = selected; var id = config.SelectedFolderId;
        var apps = previous?.Applications.ToArray() ?? [];
        var names = apps.ToDictionary(a => a.Id, a => a.Name);
        try
        {
            var message = change();
            config.SelectedFolderId = selected?.Id;
            persist(); Refresh(); Status = message;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            config.Folders.Clear(); config.Folders.AddRange(folders);
            if (previous is not null) { previous.Applications.Clear(); previous.Applications.AddRange(apps); foreach (var app in apps) app.Name = names[app.Id]; }
            config.SelectedFolderId = id; Refresh(); Status = "Changes were not saved: " + ex.Message;
            return false;
        }
    }
    public void Refresh()
    {
        var selectedId = config.SelectedFolderId;
        Folders.Clear(); Containers.Clear(); foreach (var folder in config.Folders.Where(f => f.IsActive)) { Folders.Add(folder); Containers.Add(new(folder, new ContainerIcons("").Get(folder.Icon))); }
        SelectedFolder = (IsEditing ? config.Folders : Folders.AsEnumerable()).FirstOrDefault(f => f.Id == selectedId) ?? Folders.FirstOrDefault();
        Changed(nameof(IconSize)); Changed(nameof(ShowPopupPointer));
        Changed(nameof(HasFolders)); Changed(nameof(Subtitle));
    }
    private void RefreshApps()
    {
        Applications.Clear();
        if (selected is not null)
            foreach (var app in selected.Applications) Applications.Add(new(app, ShellIcon.Get(app), config.Settings.ShowApplicationNames));
        Changed(nameof(IsEmpty));
        Changed(nameof(GridHeight)); Changed(nameof(ContentHeight));
        Changed(nameof(DesiredHeight));
    }
}
