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
    private Point dragStart;
    private AppTile? dragTile;
    private InsertionAdorner? insertion;
    private AdornerLayer? insertionLayer;
    public bool SuppressDismiss { get; set; }
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
        Deactivated += (_, _) => { if (Model.Interaction.DismissOnFocusLost(SuppressDismiss)) Hide(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { if (Model.IsEditing) Model.SetEditing(false); else Hide(); } };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { Model.SetEditing(false); ClearDragFeedback(); } };
        DpiChanged += (_, _) => QueueResize();
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }
    public void Present(bool animations)
    {
        BeginAnimation(OpacityProperty, null); Opacity = 1;
        anchor = WindowPlacement.CaptureAnchor();
        ApplySizing();
        Show(); Activate(); WindowPlacement.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (animations && SystemParameters.ClientAreaAnimation)
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
    }
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
        Width = LauncherSizing.Width;
        // Only measure the auto-sized chrome. Keep the scrolling grid constrained at all times.
        var available = new Size(Width - 52, double.PositiveInfinity);
        HeaderPane.Measure(available); FolderPane.Measure(available); FooterPane.Measure(available);
        var gridHeight = Model.Applications.Count == 0 ? 76 : Model.GridHeight;
        Height = Math.Min(770, Math.Ceiling(46 + HeaderPane.DesiredSize.Height + FolderPane.DesiredSize.Height + FooterPane.DesiredSize.Height + gridHeight) + 2);
        WindowPlacement.Place(this, anchor);
    }
    private void AppScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0 && DataContext is LauncherViewModel model) model.SetGridWidth(e.ViewportWidth);
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.IsEditing) return;
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
        if (!Model.IsEditing) { e.Handled = true; return; }
        if (sender is Button { DataContext: AppTile tile, ContextMenu: { } menu })
            ((MenuItem)menu.Items[0]).IsEnabled = tile.Entry.Kind != TargetKind.Packaged && File.Exists(tile.Entry.Target);
    }
    private static AppEntry? MenuEntry(object sender)
    {
        if (sender is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu { PlacementTarget: Button { DataContext: AppTile tile } }) return tile.Entry;
        return null;
    }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (MenuEntry(sender) is { } entry) Model.Remove(entry); }
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.IsEditing || MenuEntry(sender) is not { } entry) return;
        var name = TextPrompt.Ask(this, "Rename display name", entry.Name); if (name is not null) Model.Rename(entry, name);
    }
    private void Location_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.IsEditing || MenuEntry(sender) is not { } entry || entry.Kind == TargetKind.Packaged) return;
        try
        {
            WindowsFileLocation.Open(entry);
        }
        catch (Exception ex) { Model.Report("File location could not be opened: " + ex.Message); }
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke("General");
    private void Manage_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke("Folders");
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Hide();
}
