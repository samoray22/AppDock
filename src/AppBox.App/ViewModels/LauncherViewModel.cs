using System.Collections.ObjectModel;
using System.Windows.Media;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.ViewModels;

public sealed class AppTile : ObservableObject
{
    public AppEntry Entry { get; }
    public ImageSource? Icon { get; }
    public bool ShowName { get; }
    public string Name => Entry.Name;
    public string Label { get; }
    public bool IsLastLaunched { get; }

    private bool isRunning;
    public bool IsRunning
    {
        get => isRunning;
        set
        {
            if (isRunning == value) return;
            isRunning = value;
            Changed();
        }
    }

    public AppTile(AppEntry entry, ImageSource? icon, bool showName, string? displayLabel = null, bool isLastLaunched = false, bool isRunning = false)
    {
        Entry = entry;
        Icon = icon;
        ShowName = showName;
        Label = displayLabel ?? entry.Name;
        IsLastLaunched = isLastLaunched;
        this.isRunning = isRunning;
    }
}

public sealed record ContainerTile(AppFolder Folder, ImageSource Icon)
{
    public int Count => Folder.Applications.Count;
    public string CountText => Folder.Applications.Count == 1 ? "1 app" : $"{Folder.Applications.Count} apps";
}

public sealed class LauncherViewModel : ObservableObject
{
    private readonly Configuration config;
    private readonly FolderManager manager;
    private readonly Action persist;
    private readonly ApplicationImportService importer;
    private readonly ContainerIcons containerIcons;
    public LauncherInteraction Interaction { get; } = new();
    private AppFolder? selected;
    private string status = "";
    private string searchQuery = "";
    private bool externalDrop;
    private int? viewportColumnLimit;
    public ObservableCollection<ContainerTile> Containers { get; } = [];
    private bool overview; private bool canReturnToOverview;
    public bool IsOverview
    {
        get => overview;
        private set
        {
            overview = value;
            viewportColumnLimit = null;
            Changed();
            Changed(nameof(ShowApplications));
            Changed(nameof(ShowSearchBox));
            Changed(nameof(CanGoBack));
            Changed(nameof(HeaderTitle));
            Changed(nameof(HeaderIcon));
            Changed(nameof(HeaderBadge));
            Changed(nameof(EmptyMessage));
            Changed(nameof(TileButtonWidth));
            Changed(nameof(TileButtonHeight));
            Changed(nameof(TileContentWidth));
            Changed(nameof(TileLabelMaxWidth));
            Changed(nameof(CellWidth));
            Changed(nameof(CellHeight));
            Changed(nameof(EffectiveColumns));
            Changed(nameof(WrapWidth));
            Changed(nameof(DesiredWidth));
            Changed(nameof(GridHeight));
            Changed(nameof(ContentHeight));
            Changed(nameof(DesiredHeight));
        }
    }
    public bool ShowApplications => !IsOverview;
    public bool ShowSearchBox => !IsOverview && !IsEditing;
    public bool CanGoBack => !IsOverview && canReturnToOverview && !IsEditing;
    public string HeaderTitle => IsOverview ? "AppDock" : (selected?.Name ?? "Container");
    public ImageSource HeaderIcon => IsOverview ? ContainerIcons.BuiltIn("AppDock Box") : containerIcons.Get(selected?.Icon);
    public string HeaderBadge => IsOverview
        ? (Containers.Count == 1 ? "1 container" : $"{Containers.Count} containers")
        : (Applications.Count == 1 ? "1 app" : $"{Applications.Count} apps");
    public string EmptyMessage => HasSearchQuery
        ? $"No matches for “{SearchQuery.Trim()}”"
        : (IsOverview ? "No active containers. Right-click to manage containers." : "Drop apps here or click ＋ to add applications.");
    public string SearchQuery
    {
        get => searchQuery;
        set
        {
            if (searchQuery == value) return;
            searchQuery = value ?? "";
            Changed();
            Changed(nameof(HasSearchQuery));
            RefreshApps();
            Changed(nameof(HeaderBadge));
            Changed(nameof(EmptyMessage));
        }
    }
    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(searchQuery);
    public bool UseWindows11Style => config.Settings.UseWindows11Style;
    public bool ShowContainerBorder => config.Settings.ShowContainerBorder;
    public bool ShowPopupPointer => config.Settings.ShowPopupPointer;
    public int IconSize => config.Settings.IconSize;

    public void ShowHome()
    {
        searchQuery = "";
        Changed(nameof(SearchQuery));
        Changed(nameof(HasSearchQuery));
        canReturnToOverview = config.Settings.LayoutMode == LayoutMode.SingleAppBox; IsOverview = config.Settings.LayoutMode == LayoutMode.SingleAppBox;
        Refresh();
    }

    public void OpenOverview()
    {
        searchQuery = "";
        Changed(nameof(SearchQuery));
        Changed(nameof(HasSearchQuery));
        canReturnToOverview = true; IsOverview = true;
        Refresh();
    }

    public void OpenContainer(AppFolder folder, bool? allowBackToOverview = null)
    {
        searchQuery = "";
        Changed(nameof(SearchQuery));
        Changed(nameof(HasSearchQuery));
        canReturnToOverview = allowBackToOverview ?? (IsOverview || config.Settings.LayoutMode == LayoutMode.SingleAppBox); IsOverview = false; SelectedFolder = folder;
        IsOverview = false;
    }

    public void CheckRunningApps()
    {
        runningAppsCts?.Cancel();
        var cts = new CancellationTokenSource();
        runningAppsCts = cts;
        var tiles = Applications.Concat(SidebarRecentApps).ToArray();
        if (tiles.Length == 0) return;

        var token = cts.Token;
        Task.Run(() =>
        {
            if (token.IsCancellationRequested) return null;
            var runningNames = RunningApps.GetRunningProcessNames();
            if (token.IsCancellationRequested) return null;

            var results = new (AppTile Tile, bool IsRunning)[tiles.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                results[i] = (tiles[i], RunningApps.IsRunning(tiles[i].Entry, runningNames, shortcutResolver));
            }
            return results;
        }, token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && !token.IsCancellationRequested && t.Result is { } results)
            {
                if (uiSyncContext is not null)
                {
                    uiSyncContext.Post(_ =>
                    {
                        foreach (var (tile, isRunning) in results)
                            tile.IsRunning = isRunning;
                    }, null);
                }
                else
                {
                    foreach (var (tile, isRunning) in results)
                        tile.IsRunning = isRunning;
                }
            }
        });
    }


    public void LaunchApp(AppEntry entry, LaunchOptions? options = null)

    {

        if (!Interaction.CanLaunch) return;

        launchWithOptions(entry, options ?? new());

    }



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
    public ObservableCollection<AppTile> Applications { get; } = []; public ObservableCollection<AppTile> SidebarRecentApps { get; } = []; public bool HasSidebarRecentApps => SidebarRecentApps.Count > 0;
    public bool IsEmpty => IsOverview ? Containers.Count == 0 : Applications.Count == 0;
    public bool HasFolders => Folders.Count > 0;
    public bool IsEditing => Interaction.IsEditing;
    public bool AnimationsEnabled => config.Settings.EnableAnimations;
    public ContainerAnimationStyle AnimationStyle => config.Settings.AnimationStyle;
    public int AnimationDurationMs => config.Settings.AnimationDurationMs;
    public bool DockMagnificationEnabled => config.Settings.EnableAnimations && config.Settings.EnableDockMagnification;
    public bool LaunchBounceEnabled => config.Settings.EnableAnimations && config.Settings.EnableLaunchBounce;
    public bool CardTiltEnabled => config.Settings.EnableAnimations && config.Settings.EnableCardTiltOnHover;
    public bool PoofOnRemoveEnabled => config.Settings.EnableAnimations && config.Settings.EnablePoofOnRemove;
    public string EditButtonText => IsEditing ? "Done" : "Edit";
    public string Status { get => status; private set { if (status == value) return; status = value; Changed(); Changed(nameof(HasStatus)); } }
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public bool ExternalDropActive { get => externalDrop; set { externalDrop = value; Changed(); } }
    public SortMode EffectiveSortMode => selected?.SortModeOverride ?? config.Settings.SortMode;
    public int MaxColumns => Math.Clamp((!IsOverview && selected?.GridColumnsOverride is { } c) ? c : config.Settings.GridColumns, 2, 8);
    public int MaxRows => Math.Clamp((!IsOverview && selected?.GridRowsOverride is { } r) ? r : config.Settings.GridRows, 1, 8);
    public double TileButtonWidth => IsOverview ? 82 : (config.Settings.ShowApplicationNames ? Math.Max(72, IconSize + 22) : IconSize + 18);
    public double TileButtonHeight => IsOverview ? 92 : (config.Settings.ShowApplicationNames ? IconSize + 48 : IconSize + 18);
    public double TileContentWidth => TileButtonWidth - 8;
    public double TileLabelMaxWidth => TileButtonWidth - 10;
    public double CellWidth => TileButtonWidth + 10;
    public double CellHeight => TileButtonHeight + 10;
    private int ActiveCount => IsOverview ? Containers.Count : Applications.Count;
    public int EffectiveColumns => LauncherSizing.EffectiveColumns(ActiveCount, viewportColumnLimit.HasValue ? Math.Min(MaxColumns, viewportColumnLimit.Value) : MaxColumns);
    public double WrapWidth => EffectiveColumns * CellWidth;
    public double DesiredWidth => LauncherSizing.GridWidth(ActiveCount, MaxColumns, MaxRows, CellWidth);
    public double ContentHeight => IsOverview ? LauncherSizing.GridHeight(Containers.Count, EffectiveColumns, CellHeight) : GridHeight;
    public double GridHeight => LauncherSizing.GridHeight(Applications.Count, EffectiveColumns, CellHeight);
    public double DesiredHeight => LauncherSizing.DesiredHeight(ActiveCount, IsEditing, EffectiveColumns, MaxRows, CellHeight);

    public void SetGridWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var fit = Math.Max(1, (int)(width / CellWidth));
        int? next = fit < LauncherSizing.EffectiveColumns(ActiveCount, MaxColumns) ? fit : null;
        if (next == viewportColumnLimit) return;
        viewportColumnLimit = next;
        Changed(nameof(EffectiveColumns)); Changed(nameof(WrapWidth)); Changed(nameof(GridHeight)); Changed(nameof(ContentHeight)); Changed(nameof(DesiredHeight));
    }

    public string Subtitle => IsEditing ? (EffectiveSortMode == SortMode.RecentFirst ? "Edit mode · switch to manual order to rearrange" : "Edit mode · drag to reorder or click − to remove") : config.Settings.LayoutMode == LayoutMode.SingleAppBox ? "Your apps, a little closer." : "Multiple AppDocks · container selection";

    public AppFolder? SelectedFolder
    {
        get => selected;
        set
        {
            selected = value;
            config.SelectedFolderId = value?.Id;
            RefreshApps();
            Changed();
            Changed(nameof(HeaderTitle));
            Changed(nameof(HeaderIcon));
            Changed(nameof(HeaderBadge));
            Changed(nameof(CanGoBack));
        }
    }

    public RelayCommand LaunchCommand { get; }
    public RelayCommand ToggleEditCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand RemoveTileCommand { get; }

    private readonly Action<AppEntry, LaunchOptions> launchWithOptions;
    private readonly IShortcutResolver shortcutResolver = new WindowsShortcutResolver();
    private readonly SynchronizationContext? uiSyncContext = SynchronizationContext.Current;
    private CancellationTokenSource? runningAppsCts;

    public LauncherViewModel(Configuration config, Action<AppEntry> launch, Action? persist = null, ApplicationImportService? importer = null, string? iconDirectory = null)
        : this(config, (entry, options) => launch(entry), persist, importer, iconDirectory)
    {
    }

    public LauncherViewModel(Configuration config, Action<AppEntry, LaunchOptions> launch, Action? persist = null, ApplicationImportService? importer = null, string? iconDirectory = null)
    {
        this.config = config;
        this.launchWithOptions = launch;
        manager = new(config);
        this.persist = persist ?? (() => { });
        this.importer = importer ?? new(new WindowsShortcutResolver(), null, () => config.Settings.AllowCommandLineShells);
        containerIcons = new(iconDirectory ?? "");
        LaunchCommand = new(p =>
        {
            if (p is AppTile tile) LaunchApp(tile.Entry, new());
            else if (p is (AppTile t, LaunchOptions opt)) LaunchApp(t.Entry, opt);
        });
        ToggleEditCommand = new(_ => SetEditing(!IsEditing));
        BackCommand = new(_ => { if (CanGoBack) OpenOverview(); });
        ClearSearchCommand = new(_ => SearchQuery = "");
        RemoveTileCommand = new(p =>
        {
            if (p is AppTile tile)
            {
                if (!IsEditing) SetEditing(true);
                Remove(tile.Entry);
            }
        });
        ShowHome();
    }

    public void SetEditing(bool editing)
    {
        if (editing)
        {
            searchQuery = "";
            Changed(nameof(SearchQuery));
            Changed(nameof(HasSearchQuery));
            IsOverview = false;
            Interaction.BeginEdit();
            RefreshApps();
        }
        else Interaction.EndEdit();
        ExternalDropActive = false; Status = "";
        Changed(nameof(IsEditing)); Changed(nameof(ShowSearchBox)); Changed(nameof(EditButtonText)); Changed(nameof(Subtitle));
        Changed(nameof(CanGoBack)); Changed(nameof(HeaderTitle)); Changed(nameof(HeaderBadge)); Changed(nameof(DesiredHeight));
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

    public bool ImportIntoFolder(AppFolder? folder, IEnumerable<string> items) { return Edit(() => { var targetFolder = folder ?? selected ?? config.Folders.FirstOrDefault(f => f.IsActive) ?? manager.Create("My apps"); selected = targetFolder; var result = importer.Import(config, targetFolder, items); return $"{result.Added.Count} added to {targetFolder.Name}" + (result.Rejected.Count == 0 ? "." : $"; {result.Rejected.Count} skipped.\n" + string.Join("\n", result.Rejected.Select(r => r.Item + ": " + r.Reason))); }); }
    public bool Reorder(Guid folderId, Guid appId, int index)
    {
        if (!IsEditing || selected?.Id != folderId || config.Settings.SortMode == SortMode.RecentFirst) return false;
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

    public void RemoveFromDrag(AppEntry entry)
    {
        if (selected is not null) Edit(() => { manager.Remove(selected, entry); return "Removed from container. Application files are unchanged."; });
    }

    public bool Restore(AppFolder folder, AppEntry entry, int index)
    {
        return Edit(() => { manager.Insert(folder, entry, index); return $"Restored {entry.Name}."; });
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
        Folders.Clear(); Containers.Clear();
        foreach (var folder in config.Folders.Where(f => f.IsActive))
        {
            Folders.Add(folder);
            Containers.Add(new(folder, containerIcons.Get(folder.Icon)));
        }
        SelectedFolder = (IsEditing ? config.Folders : Folders.AsEnumerable()).FirstOrDefault(f => f.Id == selectedId) ?? Folders.FirstOrDefault(); RefreshSidebarRecentApps();
        viewportColumnLimit = null;
        Changed(nameof(IconSize)); Changed(nameof(MaxColumns)); Changed(nameof(MaxRows)); Changed(nameof(ShowPopupPointer)); Changed(nameof(UseWindows11Style)); Changed(nameof(ShowContainerBorder));
        Changed(nameof(AnimationsEnabled)); Changed(nameof(AnimationStyle)); Changed(nameof(AnimationDurationMs));
        Changed(nameof(TileButtonWidth)); Changed(nameof(TileButtonHeight)); Changed(nameof(TileContentWidth)); Changed(nameof(TileLabelMaxWidth)); Changed(nameof(CellWidth)); Changed(nameof(CellHeight));
        Changed(nameof(HasFolders)); Changed(nameof(Subtitle));
        Changed(nameof(HeaderTitle)); Changed(nameof(HeaderIcon)); Changed(nameof(HeaderBadge)); Changed(nameof(CanGoBack)); Changed(nameof(IsEmpty));
        Changed(nameof(MaxColumns)); Changed(nameof(MaxRows)); Changed(nameof(EffectiveSortMode)); Changed(nameof(Subtitle));
        Changed(nameof(EffectiveColumns)); Changed(nameof(WrapWidth)); Changed(nameof(DesiredWidth)); Changed(nameof(GridHeight)); Changed(nameof(ContentHeight)); Changed(nameof(DesiredHeight));
    }

    private void RefreshApps()
    {
        Applications.Clear();
        if (selected is not null)
        {
            var filter = searchQuery.Trim();
            var apps = selected.Applications.AsEnumerable();
            if (EffectiveSortMode == SortMode.RecentFirst)
            {
                apps = apps
                    .OrderByDescending(a => a.LastLaunchedUtc.HasValue)
                    .ThenByDescending(a => a.LastLaunchedUtc)
                    .ThenBy(a => selected.Applications.IndexOf(a));
            }

            var lastLaunched = selected.Applications
                .Where(a => a.LastLaunchedUtc.HasValue)
                .OrderByDescending(a => a.LastLaunchedUtc)
                .FirstOrDefault();

            foreach (var app in apps)
            {
                if (filter.Length > 0 && !app.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                    continue;
                var label = config.Settings.ShortenAppNames ? AppLabel.Shorten(app.Name) : app.Name;
                bool isLast = lastLaunched is not null && app.Id == lastLaunched.Id;
                Applications.Add(new(app, ShellIcon.Get(app), config.Settings.ShowApplicationNames, label, isLast));
            }
        }
        viewportColumnLimit = null;
        Changed(nameof(IsEmpty));
        Changed(nameof(EmptyMessage));
        Changed(nameof(HeaderBadge));
        Changed(nameof(MaxColumns)); Changed(nameof(MaxRows)); Changed(nameof(EffectiveSortMode)); Changed(nameof(Subtitle));
        Changed(nameof(EffectiveColumns)); Changed(nameof(WrapWidth)); Changed(nameof(DesiredWidth));
        Changed(nameof(GridHeight)); Changed(nameof(ContentHeight));
        Changed(nameof(DesiredHeight));
        CheckRunningApps(); } public void RefreshSidebarRecentApps() { SidebarRecentApps.Clear(); if (config.Settings.SidebarShowRecentApps) { int maxCount = Math.Clamp(config.Settings.SidebarRecentAppsCount, 1, 5); var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var candidates = config.Folders.Where(f => f.IsActive).SelectMany(f => f.Applications).Where(a => seenTargets.Add(a.Target)).OrderByDescending(a => a.LastLaunchedUtc.HasValue).ThenByDescending(a => a.LastLaunchedUtc).Take(maxCount).ToList(); var mostRecent = candidates.FirstOrDefault(a => a.LastLaunchedUtc.HasValue); foreach (var app in candidates) { var label = config.Settings.ShortenAppNames ? AppLabel.Shorten(app.Name) : app.Name; bool isLast = mostRecent is not null && app.Id == mostRecent.Id; SidebarRecentApps.Add(new(app, ShellIcon.Get(app), false, label, isLast)); } } Changed(nameof(HasSidebarRecentApps)); CheckRunningApps();
    }
}
