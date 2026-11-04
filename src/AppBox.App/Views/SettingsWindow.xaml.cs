using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        Closed += (_, _) => { StopAnimationPreview(); model.PropertyChanged -= ModelChanged; };
    }
    public Guid? SelectedContainerId => Model.SelectedFolder?.Id;
    public void SelectContainer(AppFolder container) => Model.SelectedFolder = container;
    public void SelectSection(string section) => Sections.SelectedIndex = section switch { "Containers" or "Folders" => 1, "Appearance" => 2, "Taskbar Interaction" or "Taskbar" => 3, "Sidebar" or "Edge Sidebar" => 4, "Portable" or "Portable Behavior" => 5, "Help" or "Guide" => 6, "About" => 7, _ => 0 };
    private void Guard(Action action) { try { action(); } catch (Exception ex) { AppDockDialog.Show(this, ex.Message, "AppDock", MessageBoxImage.Information); } }
    private void NewFolder_Click(object sender, RoutedEventArgs e) { var name = TextPrompt.Ask(this, LocalizationService.Get("Dialog.CreateContainerTitle"), LocalizationService.Get("Dialog.NewContainerDefault")); if (name is not null) Guard(() => Model.Create(name)); }
    private void Rename_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is null) return; var name = TextPrompt.Ask(this, LocalizationService.Get("Dialog.RenameContainerTitle"), Model.SelectedFolder.Name); if (name is not null) Guard(() => Model.Rename(name)); }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedFolder is { } folder && AppDockDialog.Confirm(this, LocalizationService.Format("Dialog.DeleteContainerPrompt", folder.Name), LocalizationService.Get("Dialog.DeleteContainerTitle"), LocalizationService.Get("Common.Delete"), isDanger: true))
        {
            Guard(() =>
            {
                var rootDir = !string.IsNullOrEmpty(LaunchersDirectory) ? Path.GetDirectoryName(LaunchersDirectory)! : AppPaths.ActiveDataDirectory;
                new ContainerLaunchers(rootDir, AppPaths.Executable).UnpinFromTaskbar(folder);
                Model.Delete();
            });
        }
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedFolder is null) { AppDockDialog.Show(this, LocalizationService.Get("Dialog.CreateContainerFirst"), "AppDock"); return; }
        var dialog = new OpenFileDialog { Filter = LocalizationService.Get("Dialog.BrowseFilter"), Multiselect = true, DereferenceLinks = false };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames) Guard(() => Model.Add(new AppEntry { Name = Path.GetFileNameWithoutExtension(file), Target = file, Kind = Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? TargetKind.Shortcut : TargetKind.Executable }));
    }
    private void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedFolder is null) { AppDockDialog.Show(this, LocalizationService.Get("Dialog.CreateContainerFirst"), "AppDock"); return; }
        var picker = new ApplicationPicker { Owner = this };
        if (picker.ShowDialog() == true && picker.Selected is not null) Guard(() => Model.Add(picker.Selected));
    }
    private void Remove_Click(object sender, RoutedEventArgs e) => Guard(Model.Remove);
    private void Top_Click(object sender, RoutedEventArgs e) => Guard(() => { Model.MoveToTop(); ScrollSelectedAppIntoView(); });
    private void Up_Click(object sender, RoutedEventArgs e) => Guard(() => { Model.Move(-1); ScrollSelectedAppIntoView(); });
    private void Down_Click(object sender, RoutedEventArgs e) => Guard(() => { Model.Move(1); ScrollSelectedAppIntoView(); });
    private void Bottom_Click(object sender, RoutedEventArgs e) => Guard(() => { Model.MoveToBottom(); ScrollSelectedAppIntoView(); });
    private void ScrollSelectedAppIntoView() { if (ApplicationsList.SelectedItem is not null) ApplicationsList.ScrollIntoView(ApplicationsList.SelectedItem); }
    private void Preference_Click(object sender, RoutedEventArgs e) => Guard(Model.Persist);
    public WindowStyleResult? NativeStyleState { get; private set; }
    public void ApplyWindowStyle()
    {
        if (DataContext is not SettingsViewModel || new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        NativeStyleState = NativeWindowStyle.Apply(this, Model.UseWindows11Style, ThemeService.Dark);
        StyleToggle.ToolTip = NativeStyleState.Detail;
        StyleFeedback.Text = LocalizationService.Get("General.BackdropUnavailable");
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
    private void Active_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var folder = Model.SelectedFolder;
        Model.ToggleActive();
        if (folder is { IsActive: false })
        {
            var rootDir = !string.IsNullOrEmpty(LaunchersDirectory) ? Path.GetDirectoryName(LaunchersDirectory)! : AppPaths.ActiveDataDirectory;
            new ContainerLaunchers(rootDir, AppPaths.Executable).UnpinFromTaskbar(folder);
        }
    });
    private void PinTaskbar_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (Model.SelectedFolder is not { } folder) { AppDockDialog.Show(this, LocalizationService.Get("Dialog.CreateOrSelectContainerFirst"), "AppDock"); return; }
        if (!folder.IsActive) folder.IsActive = true; Model.Config.Settings.EnableTaskbar = true;
        Model.Config.Settings.LayoutMode = LayoutMode.MultipleAppBoxes;
        Model.Persist();
        var rootDir = !string.IsNullOrEmpty(LaunchersDirectory) ? Path.GetDirectoryName(LaunchersDirectory)! : AppPaths.ActiveDataDirectory;
        var launchers = new ContainerLaunchers(rootDir, AppPaths.Executable);
        if (launchers.PinToTaskbar(folder))
            AppDockDialog.Show(this, LocalizationService.Format("Dialog.PinnedSuccess", folder.Name), LocalizationService.Get("Dialog.PinnedTitle"), MessageBoxImage.Information);
        else
            AppDockDialog.Show(this, LocalizationService.Get("Dialog.PinnedManual"), LocalizationService.Get("Dialog.PinnedTitle"), MessageBoxImage.Warning);
    });
    private void Edit_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is { } folder) EditContainer?.Invoke(folder); }
    private void Launchers_Click(object sender, RoutedEventArgs e) => Guard(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + LaunchersDirectory + "\"") { UseShellExecute = true }));
    private void RefreshIcons_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Model.Persist();
        ContainerLaunchers.NotifyShellUpdate();
        AppDockDialog.Show(this, LocalizationService.Get("Dialog.IconsRefreshed"), "AppDock", MessageBoxImage.Information);
    });
    public Action<bool>? SwitchStorageLocation { get; set; }
    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Directory.CreateDirectory(AppPaths.ActiveDataDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + AppPaths.ActiveDataDirectory + "\"") { UseShellExecute = true });
    });
    private void PortableModeToggle_Click(object sender, RoutedEventArgs e)
    {
        var targetPortable = PortableModeToggle.IsChecked == true;
        PortableModeToggle.IsChecked = Model.IsPortableMode;

        var targetDir = targetPortable ? AppPaths.PortableDataDirectory : AppPaths.LocalAppDataDirectory;
        var prompt = targetPortable
            ? LocalizationService.Format("Dialog.PortableSwitchToPortablePrompt", targetDir)
            : LocalizationService.Format("Dialog.PortableSwitchToAppDataPrompt", targetDir);

        if (!AppDockDialog.Confirm(this, prompt, LocalizationService.Get("Dialog.PortableSwitchConfirmTitle"), LocalizationService.Get("Common.Confirm"), isDanger: false))
        {
            return;
        }

        Guard(() =>
        {
            if (SwitchStorageLocation is not null)
            {
                SwitchStorageLocation(targetPortable);
            }
            else
            {
                var currentStore = new ConfigurationStore(AppPaths.ActiveDataDirectory);
                currentStore.MigrateTo(targetDir, Model.Config);
            }
            Model.NotifyStorageLocationChanged();
            PortableModeToggle.IsChecked = Model.IsPortableMode;
            AppDockDialog.Show(this, LocalizationService.Format("Dialog.PortableSwitchSuccess", AppPaths.ActiveDataDirectory), "AppDock", MessageBoxImage.Information);
        });
        PortableModeToggle.IsChecked = Model.IsPortableMode;
    }
    private void HandleColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            Model.IsHandleColorCustom = true;
            Model.SidebarHandleCustomColor = hex;
        }
    }
    private Point folderDragStart;
    private AppFolder? draggedFolder;
    private void FolderUp_Click(object sender, RoutedEventArgs e) => Guard(() => { Model.MoveFolder(-1); if (FoldersList.SelectedItem is not null) FoldersList.ScrollIntoView(FoldersList.SelectedItem); });
    private void FolderDown_Click(object sender, RoutedEventArgs e) => Guard(() => { Model.MoveFolder(1); if (FoldersList.SelectedItem is not null) FoldersList.ScrollIntoView(FoldersList.SelectedItem); });
    private void FoldersList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        folderDragStart = e.GetPosition(FoldersList);
        draggedFolder = FindFolderFromElement(e.OriginalSource as DependencyObject);
    }
    private void FoldersList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || draggedFolder is null) return;
        var pos = e.GetPosition(FoldersList);
        if (Math.Abs(pos.X - folderDragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - folderDragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var folderToDrag = draggedFolder;
            draggedFolder = null;
            DragDrop.DoDragDrop(FoldersList, new DataObject(typeof(AppFolder), folderToDrag), DragDropEffects.Move);
        }
    }
    private void FoldersList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(AppFolder)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }
    private void FoldersList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(AppFolder)) || e.Data.GetData(typeof(AppFolder)) is not AppFolder sourceFolder) return;
        var targetFolder = FindFolderFromElement(e.OriginalSource as DependencyObject);
        if (targetFolder is not null && !ReferenceEquals(sourceFolder, targetFolder))
        {
            Guard(() => Model.ReorderFolder(sourceFolder, targetFolder));
        }
        e.Handled = true;
    }
    private static AppFolder? FindFolderFromElement(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ListBoxItem { DataContext: AppFolder folder }) return folder;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Get("Dialog.ExportTitle"),
            Filter = LocalizationService.Get("Dialog.ExportFilter"),
            DefaultExt = ".zip",
            FileName = $"AppDock_Backup_{DateTime.Now:yyyyMMdd}"
        };
        if (dialog.ShowDialog(this) != true) return;
        Guard(() =>
        {
            var rootDir = !string.IsNullOrEmpty(LaunchersDirectory) ? Path.GetDirectoryName(LaunchersDirectory) : null;
            Model.ExportBackup(dialog.FileName, rootDir);
            AppDockDialog.Show(this, LocalizationService.Format("Dialog.ExportSuccess", dialog.FileName), LocalizationService.Get("Dialog.ExportComplete"), MessageBoxImage.Information);
        });
    }
    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("Dialog.ImportTitle"),
            Filter = LocalizationService.Get("Dialog.ImportFilter"),
            DereferenceLinks = false
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!AppDockDialog.Confirm(this, LocalizationService.Get("Dialog.ImportConfirmPrompt"), LocalizationService.Get("Dialog.ImportConfirmTitle"), LocalizationService.Get("Dialog.ImportButton")))
            return;
        Guard(() =>
        {
            var rootDir = !string.IsNullOrEmpty(LaunchersDirectory) ? Path.GetDirectoryName(LaunchersDirectory) : null;
            Model.ImportBackup(dialog.FileName, rootDir);
            SyncThemes();
            ThemeService.Apply(Model.Config.Settings.Theme);
            ApplyWindowStyle();
            AppDockDialog.Show(this, LocalizationService.Format("Dialog.ImportSuccess", Model.Config.Folders.Count), LocalizationService.Get("Dialog.ImportComplete"), MessageBoxImage.Information);
        });
    }
    private WarpMeshRenderer? previewWarpRenderer;
    private EventHandler? previewRenderHandler;
    private ContainerAnimationStyle lastPreviewRandomStyle = ContainerAnimationStyle.Genie;
    private void AnimationStyle_Click(object sender, RoutedEventArgs e) => PlayAnimationPreview();
    private void ReplayAnimationPreview_Click(object sender, RoutedEventArgs e) => PlayAnimationPreview();
    private void AnimationDurationSlider_MouseUp(object sender, MouseButtonEventArgs e) => PlayAnimationPreview();

    private void StopAnimationPreview()
    {
        if (previewRenderHandler is not null)
        {
            System.Windows.Media.CompositionTarget.Rendering -= previewRenderHandler;
            previewRenderHandler = null;
        }
        if (PreviewWarpViewport is not null) PreviewWarpViewport.Visibility = Visibility.Collapsed;
        if (PreviewCaptureRoot is not null) PreviewCaptureRoot.Opacity = 1.0;
    }

    private void PlayAnimationPreview()
    {
        StopAnimationPreview();
        if (PreviewCaptureRoot is null || PreviewWarpViewport is null) return;
        PreviewCaptureRoot.UpdateLayout();

        double w = PreviewCaptureRoot.ActualWidth > 0 ? PreviewCaptureRoot.ActualWidth : 398;
        double h = PreviewCaptureRoot.ActualHeight > 0 ? PreviewCaptureRoot.ActualHeight : 172;

        previewWarpRenderer ??= new WarpMeshRenderer(PreviewWarpViewport, PreviewWarpCamera, PreviewWarpGeometryModel, PreviewWarpMesh);
        if (!previewWarpRenderer.CaptureAndBindMaterial(PreviewCaptureRoot, w, h)) return;

        var configuredStyle = Model.AnimationStyle;
        var style = configuredStyle == ContainerAnimationStyle.Random
            ? ContainerWarpMesh.GetRandomConcreteStyle(lastPreviewRandomStyle)
            : configuredStyle;
        if (configuredStyle == ContainerAnimationStyle.Random)
        {
            lastPreviewRandomStyle = style;
        }

        int closeMs = ContainerWarpMesh.GetDurationMs(style, isOpening: false, Model.AnimationDurationMs) + 90;
        int holdMs = 180;
        int openMs = ContainerWarpMesh.GetDurationMs(style, isOpening: true, Model.AnimationDurationMs) + 90;
        int totalMs = closeMs + holdMs + openMs;

        previewWarpRenderer.UpdatePositions(style, w, h, 0.5, 0.98, null, 0.0);
        PreviewWarpViewport.Visibility = Visibility.Visible;
        PreviewCaptureRoot.Opacity = 0.0;

        long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMs < closeMs)
            {
                double t = Math.Clamp(elapsedMs / closeMs, 0.0, 1.0);
                double collapse = ContainerWarpMesh.ComputeCollapseProgress(style, t, isOpening: false);
                previewWarpRenderer.UpdatePositions(style, w, h, 0.5, 0.98, null, collapse);
            }
            else if (elapsedMs < closeMs + holdMs)
            {
                previewWarpRenderer.UpdatePositions(style, w, h, 0.5, 0.98, null, 1.0);
            }
            else if (elapsedMs < totalMs)
            {
                double t = Math.Clamp((elapsedMs - closeMs - holdMs) / openMs, 0.0, 1.0);
                double collapse = ContainerWarpMesh.ComputeCollapseProgress(style, t, isOpening: true);
                previewWarpRenderer.UpdatePositions(style, w, h, 0.5, 0.98, null, collapse);
            }
            else
            {
                StopAnimationPreview();
            }
        };

        previewRenderHandler = handler;
        System.Windows.Media.CompositionTarget.Rendering += handler;
    }
    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
