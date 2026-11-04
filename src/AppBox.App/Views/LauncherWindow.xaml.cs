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
    private WarpMeshRenderer? warpRenderer;
    private EventHandler? warpRenderHandler;
    private bool lastPresentedWithAnimations;
    private bool suppressTileStagger;
    private ContainerAnimationStyle activeWarpStyle = ContainerAnimationStyle.Genie;
    private ContainerAnimationStyle lastRandomStyle = ContainerAnimationStyle.Genie;
    public bool SuppressDismiss { get; set; }
    public Action<AppFolder>? ChangeIcon { get; set; }
    public Action<string>? OpenSettings { get; set; }
    public Func<bool>? IsPointerOverSidebar { get; set; }
    public Func<bool>? IsPointerOverTaskbarButton { get; set; }
    public Func<bool>? IsTaskbarEnabled { get; set; }
    public Action? ClosedFromLaunchOrDismiss { get; set; }
    public bool IsLateralFlyout { get; private set; }
    public bool IsContextMenuOpen => contextMenuOpen;

    public void SetLateralMode(bool lateral)
    {
        IsLateralFlyout = lateral;
        bool showInTaskbar = !lateral && (IsTaskbarEnabled?.Invoke() ?? true);
        WindowPlacement.SetTaskbarVisibility(this, showInTaskbar);
    }

    public void ApplyWindowStyle()
    {
        WindowPlacement.ApplyLauncherStyle(this, Model?.ShowContainerBorder ?? false, ThemeService.Dark);
        bool showInTaskbar = !IsLateralFlyout && (IsTaskbarEnabled?.Invoke() ?? true);
        WindowPlacement.SetTaskbarVisibility(this, showInTaskbar);
    }

    public LauncherWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyWindowStyle();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LauncherViewModel oldModel) oldModel.PropertyChanged -= ModelChanged;
            if (e.NewValue is LauncherViewModel newModel) newModel.PropertyChanged += ModelChanged;
            QueueResize();
        };
        Deactivated += (_, _) => { if (Model.Interaction.DismissOnFocusLost(SuppressDismiss || contextMenuOpen || (IsPointerOverSidebar?.Invoke() ?? false) || (IsPointerOverTaskbarButton?.Invoke() ?? false) || OwnedWindows.Cast<Window>().Any(w => w.IsVisible))) Hide(); };
        PreviewKeyDown += OnPreviewKeyDown;

        PreviewTextInput += OnPreviewTextInput;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                Model.SetEditing(false);
                Model.Interaction.CompleteClose();
                ClearDragFeedback();
                if (UndoToast is not null) UndoToast.Visibility = Visibility.Collapsed;
                undoTimer?.Stop();
                undoFolder = null;
                undoEntry = null;
                UpdateHeaderActionsVisibility(immediate: true);
                if (!IsVisible) ClosedFromLaunchOrDismiss?.Invoke();
            }
        };
        DpiChanged += (_, _) => QueueResize();
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            OpenSettings?.Invoke("Help");
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (Model.HasSearchQuery) { Model.SearchQuery = ""; e.Handled = true; }
            else if (Model.IsEditing) { Model.SetEditing(false); e.Handled = true; }
            else { Hide(); e.Handled = true; }
        }
        else if (e.Key == Key.Enter && SearchBox.IsKeyboardFocused && !Model.IsEditing && Model.Applications.Count > 0)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            Model.LaunchApp(Model.Applications[0].Entry, new LaunchOptions(KeepOpen: ctrl, Elevated: shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && FocusManager.GetFocusedElement(this) is Button { DataContext: AppTile focusedTile } focusedBtn)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (ctrl || shift)
            {
                Model.LaunchApp(focusedTile.Entry, new LaunchOptions(KeepOpen: ctrl, Elevated: shift));
                if (ctrl) FlashTile(focusedBtn);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocused)
        {
            FocusFirstTile();
            e.Handled = true;
        }
        else if ((e.Key == Key.Down || e.Key == Key.Tab) && FocusManager.GetFocusedElement(this) is not (Button or TextBox))
        {
            FocusFirstTile();
            e.Handled = true;
        }
        else if (!SearchBox.IsKeyboardFocused && string.IsNullOrEmpty(Model.SearchQuery) && !Model.IsEditing && Model.ShowApplications)
        {
            int index = -1;
            if (e.Key >= Key.D1 && e.Key <= Key.D9) index = e.Key - Key.D1;
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9) index = e.Key - Key.NumPad1;

            if (index >= 0)
            {
                if (index < Model.Applications.Count)
                {
                    bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                    bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                    Model.LaunchApp(Model.Applications[index].Entry, new LaunchOptions(KeepOpen: ctrl, Elevated: shift));
                }
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Back && !SearchBox.IsKeyboardFocused && Model.CanGoBack)
        {
            Model.OpenOverview();
            e.Handled = true;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Model.IsEditing || !Model.ShowSearchBox) return;
        if (FocusManager.GetFocusedElement(this) is TextBox) return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;

        SearchBox.Focus();
        SearchBox.AppendText(e.Text);
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    public void Present(bool animations, WindowPlacement.Anchor? requestedAnchor = null)
    {
        var opening = Model.Interaction.TryBeginOpen();
        StopWarpAnimation(restoreContent: true);
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        lastPresentedWithAnimations = animations;
        anchor = requestedAnchor ?? WindowPlacement.CaptureAnchor(); SetLateralMode(anchor.LateralEdge is not null);
        ApplySizing();
        ApplyIconTint();
        ApplyAcrylic();
        UpdateSearchBoxVisuals(immediate: true);
        UpdateHeaderActionsVisibility(immediate: true);
        ResetTileTransforms();
        Model.CheckRunningApps();
        bool shouldAnimate = animations && Model.AnimationsEnabled && SystemParameters.ClientAreaAnimation;
        if (!shouldAnimate) AnimateTilesIn();
        Show(); SetLateralMode(anchor.LateralEdge is not null); Activate(); WindowPlacement.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        UpdateLayout();
        FocusFirstTile();
        var token = Model.Interaction.Generation;
        if (shouldAnimate)
        {
            StartWarpAnimation(isOpening: true, completionToken: opening ? token : null, customOrigin: null);
        }
        else
        {
            if (opening) Model.Interaction.CompleteOpen(token);
        }
    }

    public void Hide(bool immediate)
    {
        bool diagnosticFastClose = Environment.GetEnvironmentVariable("APPDOCK_DIAGNOSTIC_SESSION") is not null;
        if (immediate || !lastPresentedWithAnimations || diagnosticFastClose || !Model.AnimationsEnabled || !SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            StopWarpAnimation(restoreContent: true);
            Model.Interaction.BeginClose();
            BeginAnimation(OpacityProperty, null);
            base.Hide();
            Model.Interaction.CompleteClose();
            return;
        }

        Model.Interaction.BeginClose();
        StartWarpAnimation(isOpening: false, completionToken: null, customOrigin: null);
    }

    public new void Hide() => Hide(immediate: false);

    private (double AnchorU, double AnchorV, ScreenEdge? LateralEdge) ResolveAnchorUV(Point? customOrigin)
    {
        if (customOrigin is { } custom)
        {
            return (Math.Clamp(custom.X, 0.08, 0.92), Math.Clamp(custom.Y, 0.08, 0.92), null);
        }

        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;

        if (anchor?.LateralEdge is { } lateralEdge)
        {
            double anchorU = lateralEdge == ScreenEdge.Right ? 0.98 : 0.02;
            double anchorV = 0.5;
            if (h > 0)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                if (dpi.DpiScaleY > 0)
                {
                    double cursorDipY = anchor.Cursor.Y / dpi.DpiScaleY - Top;
                    anchorV = Math.Clamp(cursorDipY / h, 0.12, 0.88);
                }
            }
            return (anchorU, anchorV, lateralEdge);
        }

        double bottomOriginU = Pointer.Visible && w > 0
            ? Math.Clamp((Pointer.CenterDip + PopupPointer.ShadowMargin) / w, 0.12, 0.88)
            : 0.5;
        return (bottomOriginU, 0.98, null);
    }

    private void StopWarpAnimation(bool restoreContent)
    {
        if (warpRenderHandler is not null)
        {
            CompositionTarget.Rendering -= warpRenderHandler;
            warpRenderHandler = null;
        }
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        RootTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        RootTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        RootScale.ScaleX = 1.0;
        RootScale.ScaleY = 1.0;
        RootTranslate.X = 0;
        RootTranslate.Y = 0;
        if (restoreContent)
        {
            WarpViewport.Visibility = Visibility.Collapsed;
            RootGrid.Opacity = 1.0;
        }
    }

    private void ResetTileTransforms()
    {
        int count = Model.Applications.Count;
        for (int i = 0; i < count; i++)
        {
            if (AppGrid.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
            {
                container.BeginAnimation(OpacityProperty, null);
                container.Opacity = 1.0;
                container.RenderTransform = null;
            }
        }
    }

    private void StartWarpAnimation(bool isOpening, long? completionToken, Point? customOrigin)
    {
        StopWarpAnimation(restoreContent: true);

        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (w < 8 || h < 8)
        {
            if (isOpening && completionToken is { } tok) Model.Interaction.CompleteOpen(tok);
            else if (!isOpening) { base.Hide(); Model.Interaction.CompleteClose(); }
            return;
        }

        ResetTileTransforms();
        WindowRoot.UpdateLayout();

        warpRenderer ??= new WarpMeshRenderer(WarpViewport, WarpCamera, WarpGeometryModel, WarpMesh);
        if (!warpRenderer.CaptureAndBindMaterial(WindowRoot, w, h))
        {
            if (isOpening && completionToken is { } tok) Model.Interaction.CompleteOpen(tok);
            else if (!isOpening) { base.Hide(); Model.Interaction.CompleteClose(); }
            return;
        }

        var configuredStyle = Model.AnimationStyle;
        ContainerAnimationStyle style;
        if (configuredStyle == ContainerAnimationStyle.Random)
        {
            if (isOpening)
            {
                style = ContainerWarpMesh.GetRandomConcreteStyle(lastRandomStyle);
                lastRandomStyle = style;
                activeWarpStyle = style;
            }
            else
            {
                style = activeWarpStyle;
            }
        }
        else
        {
            style = configuredStyle;
            activeWarpStyle = configuredStyle;
        }
        var (anchorU, anchorV, lateralEdge) = ResolveAnchorUV(customOrigin);
        int durationMs = ContainerWarpMesh.GetDurationMs(style, isOpening, Model.AnimationDurationMs);

        double initialCollapse = ContainerWarpMesh.ComputeCollapseProgress(style, 0.0, isOpening);
        warpRenderer.UpdatePositions(style, w, h, anchorU, anchorV, lateralEdge, initialCollapse);
        WarpViewport.Visibility = Visibility.Visible;
        RootGrid.Opacity = 0.0;

        long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            double t = Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);
            double collapse = ContainerWarpMesh.ComputeCollapseProgress(style, t, isOpening);
            warpRenderer.UpdatePositions(style, w, h, anchorU, anchorV, lateralEdge, collapse);

            if (t >= 1.0)
            {
                if (warpRenderHandler == handler)
                {
                    CompositionTarget.Rendering -= handler;
                    warpRenderHandler = null;
                }

                if (isOpening)
                {
                    RootGrid.Opacity = 1.0;
                    WarpViewport.Visibility = Visibility.Collapsed;
                    if (completionToken is { } tok) Model.Interaction.CompleteOpen(tok);
                }
                else
                {
                    WarpViewport.Visibility = Visibility.Collapsed;
                    RootGrid.Opacity = 1.0;
                    base.Hide();
                    Model.Interaction.CompleteClose();
                }
            }
        };

        warpRenderHandler = handler;
        CompositionTarget.Rendering += handler;
    }

    public void Cleanup()
    {
        StopWarpAnimation(restoreContent: true);
        BeginAnimation(OpacityProperty, null); ClearDragFeedback();
        if (DataContext is LauncherViewModel model) { model.PropertyChanged -= ModelChanged; model.Interaction.CompleteClose(); }
    }

    public PointerPlacement Pointer { get; private set; }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LauncherViewModel.DesiredHeight) or nameof(LauncherViewModel.DesiredWidth) or nameof(LauncherViewModel.HasStatus)) QueueResize();
        if (e.PropertyName == nameof(LauncherViewModel.IsEditing) && !Model.IsEditing) ClearDragFeedback();

        if (e.PropertyName is nameof(LauncherViewModel.SearchQuery) or nameof(LauncherViewModel.HasSearchQuery))
        {
            UpdateSearchBoxVisuals();
            UpdateHeaderActionsVisibility();
        }
        if (e.PropertyName == nameof(LauncherViewModel.SelectedFolder))
        {
            ApplyIconTint();
            if (IsVisible && !suppressTileStagger && warpRenderHandler is null) AnimateTilesIn();
        }
        if (e.PropertyName == nameof(LauncherViewModel.ShowContainerBorder))
        {
            ApplyWindowStyle();
        }
        if (e.PropertyName == nameof(LauncherViewModel.IsEditing))
        {
            UpdateHeaderActionsVisibility();
        }
    }

    private void QueueResize()
    {
        if (resizeQueued) return;
        resizeQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { resizeQueued = false; if (IsVisible) ApplySizing(); }));
    }

    private void ApplySizing()
    {
        Width = Model.DesiredWidth + 2 * PopupPointer.ShadowMargin;
        Height = Model.DesiredHeight + 2 * PopupPointer.ShadowMargin + (Model.ShowPopupPointer ? PopupPointer.Depth : 0);
        Pointer = WindowPlacement.Place(this, anchor, Model.ShowPopupPointer);
        if (!Pointer.Visible && Model.ShowPopupPointer)
        {
            Height -= PopupPointer.Depth; Pointer = WindowPlacement.Place(this, anchor, Model.ShowPopupPointer);
        }
        Frame.Margin = new Thickness(0, 0, 0, Pointer.Visible ? PopupPointer.Depth : 0);
        Chrome.RoundCorners = Model.UseWindows11Style; Chrome.ShowPointer = Pointer.Visible; Chrome.PointerCenter = Pointer.CenterDip; Chrome.InvalidateVisual();
    }

    private void AppScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0 && DataContext is LauncherViewModel model) model.SetGridWidth(e.ViewportWidth);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.IsEditing) Model.SetEditing(true);
        var dialog = new OpenFileDialog { Filter = LocalizationService.Get("Dialog.BrowseFilter"), Multiselect = true, DereferenceLinks = false };
        if (dialog.ShowDialog(this) == true) ImportFiles(dialog.FileNames);
    }

    internal void ImportFiles(IEnumerable<string> files)
    {
        var before = Model.Applications.Count;
        Model.Import(files);
        if (Model.Applications.Count > before && Model.AnimationsEnabled && SystemParameters.ClientAreaAnimation)
            AppGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void DeleteBadge_MouseDown(object sender, MouseButtonEventArgs e)
    {
        dragTile = null;
        if (sender is Button { CommandParameter: AppTile tile } badgeBtn)
        {
            if (Model.PoofOnRemoveEnabled && SystemParameters.ClientAreaAnimation) TriggerPoofFromElement(badgeBtn);
            if (!Model.IsEditing) Model.SetEditing(true);
            Model.Remove(tile.Entry);
            e.Handled = true;
        }
    }

    private void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this); dragTile = (sender as Button)?.DataContext as AppTile;
    }

    private void TriggerPoofFromElement(FrameworkElement element)
    {
        try
        {
            var screenPx = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            MicroAnimations.ShowPoofCloud(transform.Transform(screenPx));
        }
        catch { }
    }

    private void Tile_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe) MicroAnimations.ResetCardTilt(fe);
    }

    private void OverviewGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (!Model.DockMagnificationEnabled || !SystemParameters.ClientAreaAnimation) return;
        if (sender is ItemsControl ic) MicroAnimations.ApplyDockMagnification(ic, e.GetPosition(ic), verticalStrip: false, influenceRadiusDip: 120, maxExtraScale: 0.22);
    }

    private void OverviewGrid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is ItemsControl ic) MicroAnimations.ResetDockMagnification(ic);
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (Model.CardTiltEnabled && SystemParameters.ClientAreaAnimation && sender is FrameworkElement tiltEl)
                MicroAnimations.ApplyCardTilt(tiltEl, e.GetPosition(tiltEl));
            return;
        }
        if (sender is FrameworkElement dragEl) MicroAnimations.ResetCardTilt(dragEl);
        if (Model.SelectedFolder is null || dragTile is null) return;
        var point = e.GetPosition(this);
        double minX = SystemParameters.MinimumHorizontalDragDistance * 2;
        double minY = SystemParameters.MinimumVerticalDragDistance * 2;
        if (Math.Abs(point.X - dragStart.X) < minX && Math.Abs(point.Y - dragStart.Y) < minY) return;

        var currentTile = dragTile;
        var folder = Model.SelectedFolder;
        var data = new DataObject();
        data.SetData(InternalDragFormat, new ApplicationMove(folder.Id, currentTile.Entry.Id), false);
        dragTile = null;

        bool dragCancelled = false;
        QueryContinueDragEventHandler queryHandler = (s, qe) =>
        {
            if (qe.EscapePressed) dragCancelled = true;
        };

        SuppressDismiss = true;
        var button = (Button)sender;
        button.QueryContinueDrag += queryHandler;
        try
        {
            DragDrop.DoDragDrop(button, data, DragDropEffects.Move | DragDropEffects.None);
        }
        finally
        {
            button.QueryContinueDrag -= queryHandler;
            SuppressDismiss = false;
            ClearDragFeedback();
        }

        if (!dragCancelled)
        {
            var cursorPos = PointToScreen(e.GetPosition(this));
            var windowRect = new Rect(Left, Top, ActualWidth, ActualHeight);
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var cursorDip = transform.Transform(cursorPos);

            if (!windowRect.Contains(cursorDip))
            {
                var originalIndex = folder.Applications.IndexOf(currentTile.Entry);
                if (originalIndex >= 0)
                {
                    if (Model.PoofOnRemoveEnabled && SystemParameters.ClientAreaAnimation) MicroAnimations.ShowPoofCloud(cursorDip);
                    Model.RemoveFromDrag(currentTile.Entry);
                    ShowUndoToast(folder, currentTile.Entry, originalIndex);
                }
            }
        }
        e.Handled = true;
    }

    private void Launcher_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = UpdateDragFeedback(e.Data, e.GetPosition(this), e.AllowedEffects);
    }

    internal DragDropEffects UpdateDragFeedback(IDataObject data, Point point, DragDropEffects allowed)
    {
        ClearDragFeedback();
        try
        {
            // Never fall through to external import when an internal format is present, even if invalid.
            if (data.GetDataPresent(InternalDragFormat, false))
            {
                if (!Model.IsEditing) return DragDropEffects.None;
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
            {
                Model.ExternalDropActive = true;
                return DragDropEffects.Copy;
            }
            else if (Model.IsEditing)
            {
                Model.Report("Unsupported drop. Add .exe, .lnk or HTTP/HTTPS .url shortcuts.");
            }
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
            if (data.GetDataPresent(InternalDragFormat, false))
            {
                if (!Model.IsEditing) return DragDropEffects.None;
                if (InGrid(point) && data.GetData(InternalDragFormat, false) is ApplicationMove move && (allowed & DragDropEffects.Move) != 0 && Model.Reorder(move.FolderId, move.ApplicationId, InsertionIndex(point, out _, out _)))
                    return DragDropEffects.Move;
            }
            else if ((allowed & DragDropEffects.Copy) != 0 && ShellDropReader.HasCandidates(data))
            {
                if (!Model.IsEditing) Model.SetEditing(true);
                var before = Model.Applications.Count;
                ImportFiles(ShellDropReader.Read(data));
                if (Model.Applications.Count > before) return DragDropEffects.Copy;
            }
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
        var cellWidth = Model.CellWidth;
        var cellHeight = Model.CellHeight;
        var columns = Math.Max(1, (int)((AppGrid.ActualWidth > 0 ? AppGrid.ActualWidth : AppScroller.ViewportWidth) / cellWidth));
        var cell = Math.Clamp((int)(Math.Max(0, gridPoint.Y) / cellHeight) * columns + (int)(Math.Max(0, gridPoint.X) / cellWidth), 0, Model.Applications.Count);
        after = cell == Model.Applications.Count || Math.Max(0, gridPoint.X) % cellWidth > cellWidth / 2;
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
        {
            var locItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header as string) == "Open File Location");
            if (locItem is not null) locItem.IsEnabled = tile.Entry.Kind != TargetKind.Packaged && File.Exists(tile.Entry.Target);
        }
    }

    private static AppEntry? MenuEntry(object sender)
    {
        if (sender is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu { PlacementTarget: Button { DataContext: AppTile tile } }) return tile.Entry;
        return null;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is { } entry)
        {
            if (Model.PoofOnRemoveEnabled && SystemParameters.ClientAreaAnimation &&
                sender is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu { PlacementTarget: FrameworkElement target })
            {
                TriggerPoofFromElement(target);
            }
            Model.SetEditing(true);
            Model.Remove(entry);
        }
    }
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (MenuEntry(sender) is not { } entry) return;
        var name = TextPrompt.Ask(this, LocalizationService.Get("Launcher.Menu.RenameApp"), entry.Name); if (name is not null) { Model.SetEditing(true); Model.Rename(entry, name); }
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
    private void Container_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ContainerTile tile } btn)
        {
            Point? tileOrigin = null;
            try
            {
                double w = ActualWidth > 0 ? ActualWidth : Width;
                double h = ActualHeight > 0 ? ActualHeight : Height;
                if (w > 0 && h > 0)
                {
                    var pt = btn.TranslatePoint(new Point(btn.ActualWidth / 2, btn.ActualHeight / 2), WindowRoot);
                    tileOrigin = new Point(Math.Clamp(pt.X / w, 0.12, 0.88), Math.Clamp(pt.Y / h, 0.12, 0.88));
                }
            }
            catch { }

            suppressTileStagger = true;
            try
            {
                Model.OpenContainer(tile.Folder);
                ApplySizing();
                ApplyIconTint();
                FocusFirstTile();
                if (Model.AnimationsEnabled && SystemParameters.ClientAreaAnimation)
                {
                    StartWarpAnimation(isOpening: true, completionToken: null, customOrigin: tileOrigin);
                }
            }
            finally
            {
                suppressTileStagger = false;
            }
        }
    }
    private void Container_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && TileAt(source) is not null) return;
        if (Frame.ContextMenu is { } menu) for (int i = 0; i < 6; i++) ((MenuItem)menu.Items[i]).IsEnabled = Model.SelectedFolder is not null;
    }
    private void Menu_Opened(object sender, RoutedEventArgs e) => contextMenuOpen = true;
    private void Menu_Closed(object sender, RoutedEventArgs e) => contextMenuOpen = false;
    private void Launch_Click(object sender, RoutedEventArgs e) { if (MenuEntry(sender) is { } entry) { Model.SetEditing(false); Model.LaunchCommand.Execute(Model.Applications.FirstOrDefault(a => a.Entry == entry)); } }
    private void EditContainer_Click(object sender, RoutedEventArgs e) => Model.SetEditing(true);
    private void RenameContainer_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is { } folder) { var name = TextPrompt.Ask(this, LocalizationService.Get("Dialog.RenameContainerTitle"), folder.Name); if (name is not null) Model.RenameContainer(name); } }
    private void ChangeIcon_Click(object sender, RoutedEventArgs e) { if (Model.SelectedFolder is { } folder) ChangeIcon?.Invoke(folder); }
    private void Activate_Click(object sender, RoutedEventArgs e) => Model.ToggleActive();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke("General");
    private void Manage_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke("Containers");
    private void Help_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke("Help");
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Hide();
    private void SearchBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!SearchBox.IsKeyboardFocused)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void SearchBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateSearchBoxVisuals();
    private void SearchBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateSearchBoxVisuals();

    private void UpdateSearchBoxVisuals(bool immediate = false)
    {
        if (SearchBorder is null || SearchBox is null) return;
        bool expanded = SearchBox.IsKeyboardFocused || Model.HasSearchQuery;
        double targetWidth = expanded ? 104 : 26;

        SearchIconCollapsed.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        SearchFilterPlaceholder.Visibility = (expanded && !Model.HasSearchQuery) ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Opacity = expanded ? 1.0 : 0.0;

        if (immediate || !Model.AnimationsEnabled || !SystemParameters.ClientAreaAnimation)
        {
            SearchBorder.BeginAnimation(WidthProperty, null);
            SearchBorder.Width = targetWidth;
        }
        else
        {
            var anim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            SearchBorder.BeginAnimation(WidthProperty, anim);
        }
    }

    private void FocusFirstTile()
    {
        if (Model.Applications.Count == 0) return;
        if (AppGrid.ItemContainerGenerator.ContainerFromIndex(0) is DependencyObject container)
        {
            var button = FindVisualChild<Button>(container);
            button?.Focus();
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }


    private void Tile_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not AppTile tile) return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            Model.LaunchApp(tile.Entry, new LaunchOptions(KeepOpen: true));
            FlashTile(button);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (ctrl || shift)
            {
                Model.LaunchApp(tile.Entry, new LaunchOptions(KeepOpen: ctrl, Elevated: shift));
                if (ctrl) FlashTile(button);
                e.Handled = true;
            }
        }
    }

    private void FlashTile(UIElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        if (Model.LaunchBounceEnabled && element is FrameworkElement fe) { MicroAnimations.PlayLaunchBounce(fe); return; }
        if (!Model.AnimationsEnabled) return;
        var anim = new DoubleAnimation(1.0, 1.12, TimeSpan.FromMilliseconds(40))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var transform = new ScaleTransform(1.0, 1.0);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = transform;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private AppFolder? undoFolder;
    private AppEntry? undoEntry;
    private int undoIndex = -1;
    private System.Windows.Threading.DispatcherTimer? undoTimer;

    private void ShowUndoToast(AppFolder folder, AppEntry entry, int index)
    {
        undoFolder = folder;
        undoEntry = entry;
        undoIndex = index;
        UndoToastText.Text = $"{LocalizationService.Get("Launcher.UndoToastText")} ({entry.Name})";
        UndoToast.Visibility = Visibility.Visible;

        if (undoTimer is null)
        {
            undoTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            undoTimer.Tick += (_, _) =>
            {
                undoTimer.Stop();
                UndoToast.Visibility = Visibility.Collapsed;
                undoFolder = null;
                undoEntry = null;
                UpdateHeaderActionsVisibility(immediate: true);
                if (!IsVisible) ClosedFromLaunchOrDismiss?.Invoke();
            };
        }
        undoTimer.Stop();
        undoTimer.Start();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (undoFolder is not null && undoEntry is not null && undoIndex >= 0)
        {
            Model.Restore(undoFolder, undoEntry, undoIndex);
            undoTimer?.Stop();
            UndoToast.Visibility = Visibility.Collapsed;
            undoFolder = null;
            undoEntry = null;
            undoIndex = -1;
        }
    }


    private void HeaderPanel_MouseEnter(object sender, MouseEventArgs e) => UpdateHeaderActionsVisibility();
    private void HeaderPanel_MouseLeave(object sender, MouseEventArgs e) => UpdateHeaderActionsVisibility();



    private void UpdateHeaderActionsVisibility(bool immediate = false)
    {
        if (HeaderActions is null || HeaderPanel is null) return;
        bool visible = HeaderPanel.IsMouseOver || Model.IsEditing || Model.HasSearchQuery || SearchBox?.IsKeyboardFocused == true;
        HeaderActions.IsHitTestVisible = visible;
        double target = visible ? 1.0 : 0.0;

        if (immediate || !Model.AnimationsEnabled || !SystemParameters.ClientAreaAnimation)
        {
            HeaderActions.BeginAnimation(OpacityProperty, null);
            HeaderActions.Opacity = target;
        }
        else
        {
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            HeaderActions.BeginAnimation(OpacityProperty, anim);
        }
    }

    private void AnimateTilesIn()
    {
        if (!Model.AnimationsEnabled || !SystemParameters.ClientAreaAnimation) return;
        int count = Model.Applications.Count;
        if (count == 0 || count > 40) return;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            for (int i = 0; i < count; i++)
            {
                if (AppGrid.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
                {
                    var delay = Math.Min(i * 15, 150);
                    var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(delay),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    var translateAnim = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(140))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(delay),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var translate = new TranslateTransform(0, 6);
                    container.RenderTransform = translate;
                    container.Opacity = 0;

                    container.BeginAnimation(OpacityProperty, opacityAnim);
                    translate.BeginAnimation(TranslateTransform.YProperty, translateAnim);
                }
            }
        });
    }

    private void ApplyIconTint()
    {
        var dark = ThemeService.Dark;
        var icon = Model.HeaderIcon;
        var folder = Model.SelectedFolder;
        string? cacheKey = folder is not null ? (folder.Icon.Kind + ":" + (folder.Icon.ResourcePath ?? folder.Icon.BuiltIn)) : "overview";

        Color tintColor;
        var tintResult = IconTint.Extract(icon, dark, cacheKey);
        if (tintResult is not null)
        {
            var (tint, soft, text) = tintResult.Value;
            tintColor = tint;
            Resources["ContainerTint"] = new SolidColorBrush(tint);
            Resources["ContainerTintSoft"] = new SolidColorBrush(soft);
            Resources["ContainerTintText"] = new SolidColorBrush(text);
        }
        else
        {
            tintColor = (Application.Current.Resources["Accent"] as SolidColorBrush)?.Color ?? Color.FromRgb(0, 120, 212);
            if (Application.Current.Resources["Accent"] is SolidColorBrush globalAccent)
            {
                Resources["ContainerTint"] = globalAccent;
                Resources["ContainerTintSoft"] = (SolidColorBrush)Application.Current.Resources["Hover"];
                Resources["ContainerTintText"] = (SolidColorBrush)Application.Current.Resources["AccentText"];
            }
        }

        // Apply macOS Sonoma ambient radial glow in top-left behind the container header
        byte glowAlpha = dark ? (byte)36 : (byte)28;
        var centerColor = Color.FromArgb(glowAlpha, tintColor.R, tintColor.G, tintColor.B);
        var edgeColor = Color.FromArgb(0, tintColor.R, tintColor.G, tintColor.B);
        var radialGlow = new RadialGradientBrush(centerColor, edgeColor)
        {
            Center = new Point(0.18, 0.10),
            GradientOrigin = new Point(0.18, 0.10),
            RadiusX = 0.65,
            RadiusY = 0.55
        };
        radialGlow.Freeze();
        Chrome.AmbientGlowBrush = radialGlow;
    }

    private void ApplyAcrylic()
    {
        // Disable rectangular Win32 DWM composition blur on layered transparent window
        // to prevent rectangular blur bleed outside the rounded popover corners.
        NativeWindowStyle.DisableAcrylic(this);

        if (Application.Current.Resources["SurfaceGlass"] is Brush glassBrush)
            Chrome.Fill = glassBrush;
        else if (Application.Current.Resources["Surface"] is Brush surfaceBrush)
            Chrome.Fill = surfaceBrush;
    }
}
