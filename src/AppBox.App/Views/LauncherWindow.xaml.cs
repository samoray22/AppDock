using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32;
using AppBox.App.Controls;
using AppBox.App.ViewModels;
using AppBox.Infrastructure;
using AppBox.Core;
using AppBox.App.Services;

namespace AppBox.App.Views;

public partial class LauncherWindow : Window
{
    internal const string InternalDragFormat = "AppBox.ApplicationMove.v1";
    internal sealed record ApplicationMove(Guid FolderId, Guid ApplicationId);
    private LauncherViewModel Model => (LauncherViewModel)DataContext;
    private WindowPlacement.Anchor? anchor;
    private bool resizeQueued;
    private bool contextMenuOpen;
    private Point dragStart;
    private AppTile? dragTile;
    private InsertionAdorner? insertion;
    private AdornerLayer? insertionLayer;
    public bool SuppressDismiss { get; set; }
    public Action<AppFolder>? ChangeIcon { get; set; }
    public Action<string>? OpenSettings { get; set; }
    public LauncherWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPlacement.Round(this, ThemeService.Dark);
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LauncherViewModel oldModel) oldModel.PropertyChanged -= ModelChanged;
            if (e.NewValue is LauncherViewModel newModel) newModel.PropertyChanged += ModelChanged;
            QueueResize();
        };
        Deactivated += (_, _) => { if (Model.Interaction.DismissOnFocusLost(SuppressDismiss || contextMenuOpen || OwnedWindows.Cast<Window>().Any(w => w.IsVisible))) Hide(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { if (Model.IsEditing) Model.SetEditing(false); else Hide(); } };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { Model.SetEditing(false); Model.Interaction.CompleteClose(); ClearDragFeedback(); } };
        DpiChanged += (_, _) => QueueResize();
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }
    public void Present(bool animations, WindowPlacement.Anchor? requestedAnchor = null)
    {
        var opening = Model.Interaction.TryBeginOpen();
        if (opening) { BeginAnimation(OpacityProperty, null); Opacity = 1; }
        anchor = requestedAnchor ?? WindowPlacement.CaptureAnchor();
        ApplySizing();
        Show(); Activate(); WindowPlacement.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var token = Model.Interaction.Generation;
        if (opening && animations && SystemParameters.ClientAreaAnimation)
        {
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130));
            animation.Completed += (_, _) => Model.Interaction.CompleteOpen(token);
            BeginAnimation(OpacityProperty, animation);
        }
        else if (opening) Model.Interaction.CompleteOpen(token);
    }
    public new void Hide()
    {
        Model.Interaction.BeginClose(); BeginAnimation(OpacityProperty, null); base.Hide(); Model.Interaction.CompleteClose();
    }
    public void Cleanup()
    {
        BeginAnimation(OpacityProperty, null); ClearDragFeedback();
        if (DataContext is LauncherViewModel model) { model.PropertyChanged -= ModelChanged; model.Interaction.CompleteClose(); }
    }
    public PointerPlacement Pointer { get; private set; }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LauncherViewModel.DesiredHeight) or nameof(LauncherViewModel.HasStatus)) QueueResize();
        if (e.PropertyName == nameof(LauncherViewModel.IsEditing) && !Model.IsEditing) ClearDragFeedback();
    }
    private void QueueResize()
    {
        if (resizeQueued) return;
        resizeQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { resizeQueued = false; if (IsVisible) ApplySizing(); }));
    }
    private void ApplySizing()
    {
        var count = Model.IsOverview ? Model.Containers.Count : Model.Applications.Count;
        Width = Math.Min(LauncherSizing.Width, Math.Max(122, Math.Min(4, Math.Max(1, count)) * LauncherSizing.TileWidth + 26)) + 2 * PopupPointer.ShadowMargin;
        Height = Model.DesiredHeight + 2 * PopupPointer.ShadowMargin + (Model.ShowPopupPointer ? PopupPointer.Depth : 0);
        Pointer = WindowPlacement.Place(this, anchor, Model.ShowPopupPointer);
        if (!Pointer.Visible && Model.ShowPopupPointer)
        {
            Height -= PopupPointer.Depth; Pointer = WindowPlacement.Place(this, anchor, Model.ShowPopupPointer);
        }
        Frame.Margin = new Thickness(0, 0, 0, Pointer.Visible ? PopupPointer.Depth : 0);
        Chrome.ShowPointer = Pointer.Visible; Chrome.PointerCenter = Pointer.CenterDip; Chrome.InvalidateVisual();
    }
    private void AppScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0 && DataContext is LauncherViewModel model) model.SetGridWidth(e.ViewportWidth);
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.IsEditing) Model.SetEditing(true);
        var dialog = new OpenFileDialog { Filter = "Applications and shortcuts|*.exe;*.lnk;*.url", Multiselect = true, DereferenceLinks = false };
        if (dialog.ShowDialog(this) == true) ImportFiles(dialog.FileNames);
    }
    internal void ImportFiles(IEnumerable<string> files)
    {
        var before = Model.Applications.Count;
        Model.Import(files);
        if (Model.Applications.Count > before && Model.AnimationsEnabled && SystemParameters.ClientAreaAnimation)
            AppGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(180)));
    }
    private void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this); dragTile = (sender as Button)?.DataContext as AppTile;
    }
    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (!Model.IsEditing || Model.SelectedFolder is null || e.LeftButton != MouseButtonState.Pressed || dragTile is null) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var data = new DataObject(); data.SetData(InternalDragFormat, new ApplicationMove(Model.SelectedFolder.Id, dragTile.Entry.Id), false);
        dragTile = null;
        try { DragDrop.DoDragDrop((Button)sender, data, DragDropEffects.Move); }
        finally { ClearDragFeedback(); }
        e.Handled = true;
    }
    private void Launcher_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = UpdateDragFeedback(e.Data, e.GetPosition(this), e.AllowedEffects);
    }
    internal DragDropEffects UpdateDragFeedback(IDataObject data, Point point, DragDropEffects allowed)
    {
        ClearDragFeedback();
        if (!Model.IsEditing) return DragDropEffects.None;
        try
        {
            // Never fall through to external import when an internal format is present, even if invalid.
            if (data.GetDataPresent(InternalDragFormat, false))
            {
                if (InGrid(point) && data.GetData(InternalDragFormat, false) is ApplicationMove move && move.FolderId == Model.SelectedFolder?.Id && Model.Applications.Any(a => a.Entry.Id == move.ApplicationId) && (allowed & DragDropEffects.Move) != 0)
                {
                    ShowInsertion(point);
                    var y = TranslatePoint(point, AppScroller).Y;
                    if (y < 24) AppScroller.ScrollToVerticalOffset(AppScroller.VerticalOffset - 12);
                    else if (y > AppScroller.ActualHeight - 24) AppScroller.ScrollToVerticalOffset(AppScroller.VerticalOffset + 12);
                    return DragDropEffects.Move;
                }
            }
            else if ((allowed & DragDropEffects.Copy) != 0 && ShellDropReader.HasCandidates(data))
            { Model.ExternalDropActive = true; return DragDropEffects.Copy; }
            else Model.Report("Unsupported drop. Add .exe, .lnk or HTTP/HTTPS .url shortcuts.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Model.Report("This drop could not be read: " + ex.Message); }
        return DragDropEffects.None;
    }
    private void Launcher_DragLeave(object sender, DragEventArgs e)
    {
        ClearDragFeedback();
        e.Handled = true;
    }
    private void Launcher_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = ImportDrop(e.Data, e.GetPosition(this), e.AllowedEffects);
    }
    internal DragDropEffects ImportDrop(IDataObject data, Point point, DragDropEffects allowed)
    {
        try
        {
            if (!Model.IsEditing) { Model.Report("Click Edit before adding applications."); return DragDropEffects.None; }
            if (data.GetDataPresent(InternalDragFormat, false))
            {
                if (InGrid(point) && data.GetData(InternalDragFormat, false) is ApplicationMove move && (allowed & DragDropEffects.Move) != 0 && Model.Reorder(move.FolderId, move.ApplicationId, InsertionIndex(point, out _, out _)))
                    return DragDropEffects.Move;
            }
            else if ((allowed & DragDropEffects.Copy) != 0)
            { var before = Model.Applications.Count; ImportFiles(ShellDropReader.Read(data)); if (Model.Applications.Count > before) return DragDropEffects.Copy; }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Model.Report("Applications could not be added: " + ex.Message); }
        finally { ClearDragFeedback(); }
        return DragDropEffects.None;
    }
    private int InsertionIndex(Point point, out Button? tile, out bool after)
    {
        tile = TileAt(InputHitTest(point) as DependencyObject);
        after = tile is not null && point.X > tile.TranslatePoint(new Point(tile.ActualWidth / 2, 0), this).X;
        if (tile?.DataContext is AppTile app) return Model.Applications.IndexOf(app) + (after ? 1 : 0);
        if (Model.Applications.Count == 0) return 0;
        // Blank margins between tiles still refer to the nearest visible cell, not the end of the folder.
        var gridPoint = TranslatePoint(point, AppGrid);
        var columns = Math.Max(1, (int)(AppScroller.ViewportWidth / LauncherSizing.TileWidth));
        var cell = Math.Clamp((int)(Math.Max(0, gridPoint.Y) / LauncherSizing.RowHeight) * columns + (int)(Math.Max(0, gridPoint.X) / LauncherSizing.TileWidth), 0, Model.Applications.Count);
        after = cell == Model.Applications.Count || Math.Max(0, gridPoint.X) % LauncherSizing.TileWidth > LauncherSizing.TileWidth / 2;
        if (AppGrid.ItemContainerGenerator.ContainerFromIndex(Math.Min(cell, Model.Applications.Count - 1)) is DependencyObject container) tile = DescendantButton(container);
        return Math.Min(Model.Applications.Count, cell + (after ? 1 : 0));
    }
    private bool InGrid(Point point)
    {
        var local = TranslatePoint(point, AppScroller);
        return local.X >= 0 && local.Y >= 0 && local.X <= AppScroller.ActualWidth && local.Y <= AppScroller.ActualHeight;
    }
    private void ShowInsertion(Point point)
    {
        InsertionIndex(point, out var tile, out var after);
        if (tile is null) return;
        insertionLayer = AdornerLayer.GetAdornerLayer(tile);
        insertion = new(tile, after, (Brush)FindResource("Accent")) { IsHitTestVisible = false };
        insertionLayer?.Add(insertion);
    }
    private void ClearDragFeedback()
    {
        if (DataContext is LauncherViewModel model) model.ExternalDropActive = false;
        if (insertion is not null) insertionLayer?.Remove(insertion);
        insertion = null; insertionLayer = null;
    }
    private static Button? TileAt(DependencyObject? element)
    {
        while (element is not null) { if (element is Button { DataContext: AppTile } button) return button; element = VisualTreeHelper.GetParent(element); }
        return null;
    }
    private static Button? DescendantButton(DependencyObject element)
    {
        if (element is Button button) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) if (DescendantButton(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        return null;
    }
    private void Tile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is Button { DataContext: AppTile tile, ContextMenu: { } menu })
            ((MenuItem)menu.Items[0]).IsEnabled = tile.Entry.Kind != TargetKind.Packaged && File.Exists(tile.Entry.Target);
    }
    private static AppEntry? MenuEntry(object sender)
    {
        if (sender is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu { PlacementTarget: Button { DataContext: AppTile tile } }) return tile.Entry;
        return null;
    }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (MenuEntry(sender) is { } entry) { Model.SetEditing(true); Model.Remove(entry); } }
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is not { } entry) return;
        var name = TextPrompt.Ask(this, "Rename", entry.Name); if (name is not null) { Model.SetEditing(true); Model.Rename(entry, name); }
    }
    private void Location_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is not { } entry || entry.Kind == TargetKind.Packaged) return;
        try
        {
            WindowsFileLocation.Open(entry);
        }
        catch (Exception ex) { Model.Report("File location could not be opened: " + ex.Message); }
    }
    private void Container_RightClick(object sender, MouseButtonEventArgs e) { if (sender is Button { DataContext: ContainerTile tile }) Model.SelectedFolder = tile.Folder; }
    private void Container_Click(object sender, RoutedEventArgs e) { if (sender is Button { DataContext: ContainerTile tile }) Model.OpenContainer(tile.Folder); }
    private void Container_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && TileAt(source) is not null) return;
        if (Frame.ContextMenu is { } menu) for (int i = 0; i < 6; i++) ((MenuItem)menu.Items[i]).IsEnabled = Model.SelectedFolder is not null;
    }
    private void Menu_Opened(object sender, RoutedEventArgs e) => contextMenuOpen = true;
    private void Menu_Closed(object sender, RoutedEventArgs e) => contextMenuOpen = false;
    private void Launch_Click(object sender, RoutedEventArgs e) { if (MenuEntry(sender) is { } entry) { Model.SetEditing(false); Model.LaunchCommand.Execute(Model.Applications.FirstOrDefault(a => a.Entry == entry)); } }
    private void EditContainer_Click(object sender, RoutedEventArgs e) => Model.SetEditing(true);
    private void RenameContainer_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is { } folder) { var name = TextPrompt.Ask(this, "Rename Container", folder.Name); if (name is not null) Model.RenameContainer(name); } }
    private void ChangeIcon_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is { } folder) ChangeIcon?.Invoke(folder); }
    private void Activate_Click(object sender, RoutedEventArgs e) => Model.ToggleActive();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke("General");
    private void Manage_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke("Containers");
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Hide();
}
