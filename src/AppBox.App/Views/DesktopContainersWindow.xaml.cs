using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AppBox.App.Services;
using AppBox.App.ViewModels;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.Views;

public partial class DesktopContainersWindow : Window
{
    private readonly Configuration config;
    private readonly LauncherViewModel model;
    private readonly LauncherWindow launcher;
    private readonly Action persist;
    private readonly Action<string> openSettings;

    private readonly DispatcherTimer hoverOpenTimer;
    private readonly DispatcherTimer closeWatchTimer;

    private PopupBounds currentBounds;
    private PixelRect currentWorkArea;
    private double currentDpiScale = 1.0;

    private bool isDraggingWindow;
    private Point dragMouseDownDip;
    private PixelPoint dragStartCursorPx;
    private double dragStartNormX;
    private double dragStartNormY;

    private bool isContextMenuOpen;
    private Button? pendingHoverButton;
    private AppFolder? pendingHoverFolder;
    private DateTime? mouseLeftBothUtc;
    private bool openedLauncherFromDesktop;

    private WarpMeshRenderer? warpRenderer;
    private EventHandler? warpRenderHandler;
    private ContainerAnimationStyle lastRandomStyle = ContainerAnimationStyle.Genie;
    private readonly Dictionary<Guid, DesktopBoxWindow> boxWindows = [];
    private DesktopRecycleBinWindow? recycleBinWindow;
    private bool isSyncing;

    public DesktopContainersWindow(
        Configuration config,
        LauncherViewModel model,
        LauncherWindow launcher,
        Action persist,
        Action<string> openSettings)
    {
        InitializeComponent();
        this.config = config;
        this.model = model;
        this.launcher = launcher;
        this.persist = persist;
        this.openSettings = openSettings;

        DataContext = model;
        model.PropertyChanged += OnModelPropertyChanged;

        hoverOpenTimer = new DispatcherTimer();
        hoverOpenTimer.Tick += OnHoverOpenTimerTick;

        closeWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        closeWatchTimer.Tick += OnCloseWatchTimerTick;

        SourceInitialized += (_, _) => ApplyWindowStyle();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyPlacement);
        Activated += (_, _) => WindowPlacement.SendToDesktopBottom(this);
        Closing += (_, e) =>
        {
            if (Application.Current?.ShutdownMode != ShutdownMode.OnExplicitShutdown && Application.Current?.MainWindow is { IsLoaded: true })
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void ApplyWindowStyle()
    {
        WindowPlacement.ApplyToolWindowStyle(this, ThemeService.Dark);
        NativeWindowStyle.DisableAcrylic(this);
        WindowPlacement.SendToDesktopBottom(this);
    }

    public bool IsCursorOverDesktopContainers()
    {
        if (recycleBinWindow?.IsCursorOverBox() == true)
        {
            return true;
        }
        if (config.Settings.DesktopDisplayStyle == DesktopDisplayStyle.ExpandedBoxes && boxWindows.Count > 0)
        {
            foreach (var win in boxWindows.Values)
            {
                if (win.IsCursorOverBox()) return true;
            }
        }
        if (!IsVisible) return false;
        var cursor = WindowPlacement.GetCursorPixelPoint();
        return cursor.X >= currentBounds.X - 6 &&
               cursor.X <= currentBounds.X + currentBounds.Width + 6 &&
               cursor.Y >= currentBounds.Y - 6 &&
               cursor.Y <= currentBounds.Y + currentBounds.Height + 6;
    }

    public void SyncWithConfig()
    {
        if (isSyncing) return;
        isSyncing = true;
        try
        {
            DesktopIconsService.SetDesktopIconsHidden(config.Settings.HideDesktopIcons);
            SyncRecycleBinWindow();

            if (!config.Settings.EnableDesktopContainers)
            {
                StopAllTimers();
                StopWarpAnimation();
                CloseAllBoxWindows();
                if (IsVisible) Hide();
                return;
            }

            model.RefreshDesktopContainers();

            if (config.Settings.DesktopDisplayStyle == DesktopDisplayStyle.ExpandedBoxes && model.DesktopContainers.Count > 0)
            {
                StopAllTimers();
                StopWarpAnimation();
                if (IsVisible) Hide();
                SyncExpandedBoxWindows();
                return;
            }

            CloseAllBoxWindows();
            UpdateModeVisuals();

            if (!IsVisible)
            {
                Show();
            }
            ApplyWindowStyle();
            ApplyPlacement();
        }
        finally
        {
            isSyncing = false;
        }
    }

    private void SyncExpandedBoxWindows()
    {
        var occupiedCells = new HashSet<(int Col, int Row)>();
        if (recycleBinWindow is { IsVisible: true })
        {
            occupiedCells.Add(recycleBinWindow.CurrentGridCell);
        }

        var activeIds = new HashSet<Guid>();
        for (int i = 0; i < model.DesktopContainers.Count; i++)
        {
            var tile = model.DesktopContainers[i];
            activeIds.Add(tile.Folder.Id);

            if (!boxWindows.TryGetValue(tile.Folder.Id, out var win))
            {
                win = new DesktopBoxWindow(
                    config,
                    model,
                    tile,
                    i,
                    persist,
                    openSettings,
                    folder => OpenContainerFlyout(folder),
                    () => SwitchStyle_Click(this, new RoutedEventArgs()),
                    () => ToggleDesktopIcons_Click(this, new RoutedEventArgs()),
                    ResetAllDesktopPositions,
                    () => DisableDesktopContainers_Click(this, new RoutedEventArgs()),
                    UpdateExpandedBoxOcclusion);
                boxWindows[tile.Folder.Id] = win;
                win.Show();
                win.ApplyWindowStyle();
            }

            win.UpdateFromTile(tile, i, occupiedCells);
        }

        foreach (var staleId in boxWindows.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            if (boxWindows.Remove(staleId, out var staleWin))
            {
                staleWin.Cleanup();
                staleWin.Close();
            }
        }

        UpdateExpandedBoxOcclusion();
    }

    private void UpdateExpandedBoxOcclusion()
    {
        var expandedBoxes = boxWindows.Values
            .Where(b => b.IsExpanded)
            .ToList();

        foreach (var box in boxWindows.Values)
        {
            bool occluded = !box.IsExpanded && expandedBoxes.Any(expanded =>
                expanded.FolderId != box.FolderId &&
                DesktopBoxWindow.DoesExpandedBoxOverlapIcon(expanded.ExpandedDipBounds, box.CompactIconDipBounds));
            box.SetOccludedByExpandedBox(occluded);
        }

        if (recycleBinWindow != null)
        {
            bool binOccluded = expandedBoxes.Any(expanded =>
                DesktopBoxWindow.DoesExpandedBoxOverlapIcon(expanded.ExpandedDipBounds, recycleBinWindow.CompactIconDipBounds));
            recycleBinWindow.SetOccludedByExpandedBox(binOccluded);
        }
    }

    private void CloseAllBoxWindows()
    {
        foreach (var win in boxWindows.Values)
        {
            win.Cleanup();
            win.Close();
        }
        boxWindows.Clear();
    }

    private void SyncRecycleBinWindow()
    {
        if (config.Settings.HideDesktopIcons)
        {
            if (recycleBinWindow is null)
            {
                recycleBinWindow = new DesktopRecycleBinWindow(
                    config,
                    persist,
                    openSettings,
                    ResetAllDesktopPositions);
                recycleBinWindow.Show();
                recycleBinWindow.ApplyWindowStyle();
            }
            recycleBinWindow.ApplyVisualAndPlacement();
        }
        else
        {
            CloseRecycleBinWindow();
        }
    }

    private void CloseRecycleBinWindow()
    {
        if (recycleBinWindow is not null)
        {
            recycleBinWindow.Cleanup();
            recycleBinWindow.Close();
            recycleBinWindow = null;
        }
    }

    private void ResetAllDesktopPositions()
    {
        config.Settings.DesktopDockX = 0.82;
        config.Settings.DesktopDockY = 0.12;
        config.Settings.DesktopRecycleBinX = 0.015;
        config.Settings.DesktopRecycleBinY = 0.02;
        foreach (var folder in config.Folders.Concat(config.DesktopFolders))
        {
            folder.DesktopX = null;
            folder.DesktopY = null;
        }
        try { persist(); } catch { }
        SyncWithConfig();
    }

    public void NotifyLauncherClosed()
    {
        openedLauncherFromDesktop = false;
        closeWatchTimer.Stop();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isSyncing || !config.Settings.EnableDesktopContainers) return;
        if (e.PropertyName is nameof(LauncherViewModel.HasDesktopContainers) or nameof(LauncherViewModel.HasFolders))
        {
            if (config.Settings.DesktopDisplayStyle == DesktopDisplayStyle.ExpandedBoxes)
            {
                if (e.PropertyName == nameof(LauncherViewModel.HasDesktopContainers) && model.DesktopContainers.Count > 0)
                {
                    SyncExpandedBoxWindows();
                }
                else
                {
                    SyncWithConfig();
                }
            }
            else if (IsVisible)
            {
                UpdateModeVisuals();
                ApplyPlacement();
            }
        }
    }

    private void UpdateModeVisuals()
    {
        bool isExpandedStyle = config.Settings.DesktopDisplayStyle == DesktopDisplayStyle.ExpandedBoxes;
        CompactRoot.Visibility = isExpandedStyle ? Visibility.Collapsed : Visibility.Visible;
        ExpandedRoot.Visibility = isExpandedStyle ? Visibility.Visible : Visibility.Collapsed;

        bool isEmpty = model.DesktopContainers.Count == 0;
        CompactEmptyButton.Visibility = (!isExpandedStyle && isEmpty) ? Visibility.Visible : Visibility.Collapsed;
        ExpandedEmptyCard.Visibility = (isExpandedStyle && isEmpty) ? Visibility.Visible : Visibility.Collapsed;

        CompactDragGrip.Visibility = config.Settings.LockDesktopContainers ? Visibility.Collapsed : Visibility.Visible;
        RootContent.Opacity = Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
    }

    private (double WidthDip, double HeightDip) CalculateDesiredSizeDip()
    {
        int count = model.DesktopContainers.Count;
        if (config.Settings.DesktopDisplayStyle == DesktopDisplayStyle.CompactFlyout)
        {
            if (count == 0)
            {
                return (240, 88);
            }
            int cols = Math.Clamp(count, 1, 4);
            int rows = (int)Math.Ceiling(count / (double)cols);
            double w = cols * 86 + 36;
            double h = rows * 94 + 42;
            return (w, h);
        }
        else
        {
            if (count == 0)
            {
                return (328, 96);
            }
            double totalHeight = 20;
            foreach (var box in model.DesktopContainers)
            {
                if (box.IsCollapsed)
                {
                    totalHeight += 54;
                }
                else
                {
                    int appCount = box.Applications.Count;
                    int appRows = appCount == 0 ? 1 : (int)Math.Ceiling(appCount / 4.0);
                    totalHeight += 52 + appRows * 84 + 12;
                }
            }
            return (344, Math.Min(760, Math.Max(96, totalHeight)));
        }
    }

    public void ApplyPlacement()
    {
        if (!IsVisible) return;

        var (widthDip, heightDip) = CalculateDesiredSizeDip();
        Width = widthDip;
        Height = heightDip;

        var (bounds, work, scale) = WindowPlacement.PlaceDesktopContainers(
            this,
            config.Settings.DesktopDockX,
            config.Settings.DesktopDockY,
            widthDip,
            heightDip,
            config.Settings.SidebarMonitorMode,
            config.Settings.SidebarMonitorIndex);

        currentBounds = bounds;
        currentWorkArea = work;
        currentDpiScale = scale > 0 ? scale : 1.0;
        WindowPlacement.SendToDesktopBottom(this);
    }

    private void StopWarpAnimation()
    {
        if (warpRenderHandler is not null)
        {
            CompositionTarget.Rendering -= warpRenderHandler;
            warpRenderHandler = null;
        }
        DesktopWarpViewport.Visibility = Visibility.Collapsed;
        RootContent.Visibility = Visibility.Visible;
    }

    private void PlayDesktopWarpTransition(Point? anchorNorm = null)
    {
        if (!config.Settings.EnableAnimations || !SystemParameters.ClientAreaAnimation || !IsVisible)
            return;

        StopWarpAnimation();
        UpdateLayout();

        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (w < 16 || h < 16) return;

        DesktopWarpCamera.Position = new System.Windows.Media.Media3D.Point3D(w / 2.0, h / 2.0, Math.Max(w, h) * 1.35);
        warpRenderer ??= new WarpMeshRenderer(DesktopWarpViewport, DesktopWarpCamera, DesktopWarpGeometryModel, DesktopWarpMesh);
        if (!warpRenderer.CaptureAndBindMaterial(RootContent, w, h))
            return;

        var style = config.Settings.AnimationStyle == ContainerAnimationStyle.Random
            ? ContainerWarpMesh.GetRandomConcreteStyle(lastRandomStyle)
            : config.Settings.AnimationStyle;
        lastRandomStyle = style;

        double anchorU = anchorNorm?.X ?? 0.5;
        double anchorV = anchorNorm?.Y ?? 0.12;
        int durationMs = ContainerWarpMesh.GetDurationMs(style, isOpening: true, config.Settings.AnimationDurationMs);

        double initialCollapse = ContainerWarpMesh.ComputeCollapseProgress(style, 0.0, isOpening: true);
        warpRenderer.UpdatePositions(style, w, h, anchorU, anchorV, null, initialCollapse);
        DesktopWarpViewport.Visibility = Visibility.Visible;
        RootContent.Visibility = Visibility.Hidden;

        long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            double t = Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);
            double collapse = ContainerWarpMesh.ComputeCollapseProgress(style, t, isOpening: true);
            warpRenderer.UpdatePositions(style, w, h, anchorU, anchorV, null, collapse);

            if (t >= 1.0)
            {
                StopWarpAnimation();
            }
        };

        warpRenderHandler = handler;
        CompositionTarget.Rendering += handler;
    }

    private WindowPlacement.Anchor CreateDesktopAnchor(FrameworkElement? button = null)
    {
        int centerY = currentBounds.Y + currentBounds.Height / 2;
        if (button is not null && PresentationSource.FromVisual(button) is not null)
        {
            try
            {
                var pt = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                centerY = (int)Math.Round(pt.Y);
            }
            catch
            {
            }
        }

        int workCenterX = (currentWorkArea.Left + currentWorkArea.Right) / 2;
        int dockCenterX = currentBounds.X + currentBounds.Width / 2;
        ScreenEdge effectiveEdge = dockCenterX <= workCenterX ? ScreenEdge.Left : ScreenEdge.Right;
        int anchorX = effectiveEdge == ScreenEdge.Left
            ? currentBounds.X + currentBounds.Width
            : currentBounds.X;

        return new WindowPlacement.Anchor(
            new PixelPoint(anchorX, centerY),
            effectiveEdge,
            currentBounds);
    }

    private void OpenContainerFlyout(AppFolder folder, FrameworkElement? button = null, bool editMode = false)
    {
        openedLauncherFromDesktop = true;
        launcher.SetLateralMode(true);
        bool alreadyOpenSameFolder = launcher.IsVisible && !model.IsOverview && model.SelectedFolder?.Id == folder.Id;
        model.OpenContainer(folder, allowBackToOverview: false);
        launcher.Present(!alreadyOpenSameFolder && config.Settings.EnableAnimations, CreateDesktopAnchor(button));
        if (editMode)
        {
            model.SetEditing(true);
        }
        mouseLeftBothUtc = null;
        closeWatchTimer.Start();
    }

    private void CompactContainer_Click(object sender, RoutedEventArgs e)
    {
        hoverOpenTimer.Stop();
        if (sender is Button { DataContext: DesktopContainerTile tile } button)
        {
            OpenContainerFlyout(tile.Folder, button);
        }
    }

    private void CompactContainer_MouseEnter(object sender, MouseEventArgs e)
    {
        if (config.Settings.DesktopOpeningBehavior is not (ContainerOpeningBehavior.Hover or ContainerOpeningBehavior.ClickOrHover))
            return;

        if (sender is Button { DataContext: DesktopContainerTile tile } button)
        {
            pendingHoverButton = button;
            pendingHoverFolder = tile.Folder;
            int delay = Math.Clamp(config.Settings.HoverOpeningDelayMs, 100, 2000);
            hoverOpenTimer.Interval = TimeSpan.FromMilliseconds(delay);
            hoverOpenTimer.Stop();
            hoverOpenTimer.Start();
        }
    }

    private void CompactContainer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender == pendingHoverButton)
        {
            hoverOpenTimer.Stop();
            pendingHoverButton = null;
            pendingHoverFolder = null;
        }
    }

    private void OnHoverOpenTimerTick(object? sender, EventArgs e)
    {
        hoverOpenTimer.Stop();
        if (pendingHoverFolder is { } folder && pendingHoverButton is { IsMouseOver: true } button)
        {
            OpenContainerFlyout(folder, button);
        }
    }

    private void OnCloseWatchTimerTick(object? sender, EventArgs e)
    {
        if (!openedLauncherFromDesktop || !launcher.IsVisible)
        {
            closeWatchTimer.Stop();
            return;
        }

        if (config.Settings.DesktopOpeningBehavior is ContainerOpeningBehavior.Click)
        {
            return;
        }

        if (isContextMenuOpen || model.IsEditing || launcher.SuppressDismiss || launcher.IsContextMenuOpen)
        {
            mouseLeftBothUtc = null;
            return;
        }

        if (IsCursorOverDesktopContainers() || IsCursorOverLauncher())
        {
            mouseLeftBothUtc = null;
            return;
        }

        mouseLeftBothUtc ??= DateTime.UtcNow;
        int closeDelayMs = Math.Clamp(config.Settings.HoverClosingDelayMs, 100, 2000);
        if ((DateTime.UtcNow - mouseLeftBothUtc.Value).TotalMilliseconds >= closeDelayMs)
        {
            openedLauncherFromDesktop = false;
            closeWatchTimer.Stop();
            launcher.Hide();
        }
    }

    private bool IsCursorOverLauncher()
    {
        if (!launcher.IsVisible) return false;
        var cursor = WindowPlacement.GetCursorPixelPoint();
        double scale = currentDpiScale > 0 ? currentDpiScale : 1.0;
        double leftPx = launcher.Left * scale;
        double topPx = launcher.Top * scale;
        double rightPx = (launcher.Left + launcher.ActualWidth) * scale;
        double bottomPx = (launcher.Top + launcher.ActualHeight) * scale;

        return cursor.X >= leftPx - 14 &&
               cursor.X <= rightPx + 14 &&
               cursor.Y >= topPx - 10 &&
               cursor.Y <= bottomPx + 10;
    }

    private void CompactList_MouseMove(object sender, MouseEventArgs e)
    {
        if (!model.DockMagnificationEnabled || !SystemParameters.ClientAreaAnimation) return;
        if (sender is ItemsControl list)
        {
            MicroAnimations.ApplyDockMagnification(list, e.GetPosition(list), verticalStrip: false, influenceRadiusDip: 105, maxExtraScale: 0.25);
        }
    }

    private void CompactList_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is ItemsControl list)
        {
            MicroAnimations.ResetDockMagnification(list);
        }
    }

    private void OpenBoxInFlyout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DesktopContainerTile tile } button)
        {
            OpenContainerFlyout(tile.Folder, button, editMode: false);
        }
    }

    private void ToggleBoxCollapse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DesktopContainerTile tile })
        {
            tile.IsCollapsed = !tile.IsCollapsed;
            try { persist(); } catch { }
            ApplyPlacement();
            PlayDesktopWarpTransition(new Point(0.5, 0.12));
        }
    }

    private void DesktopAppTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AppTile tile } btn)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (model.LaunchBounceEnabled && SystemParameters.ClientAreaAnimation)
            {
                MicroAnimations.PlayLaunchBounce(btn);
            }
            model.LaunchApp(tile.Entry, new LaunchOptions(KeepOpen: true, Elevated: shift));
        }
    }

    private void DesktopAppTile_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed && model.CardTiltEnabled && SystemParameters.ClientAreaAnimation && sender is FrameworkElement fe)
        {
            MicroAnimations.ApplyCardTilt(fe, e.GetPosition(fe));
        }
    }

    private void DesktopAppTile_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            MicroAnimations.ResetCardTilt(fe);
        }
    }

    private static AppTile? MenuTile(object sender)
    {
        if (sender is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu { PlacementTarget: Button { DataContext: AppTile tile } })
            return tile;
        return null;
    }

    private void DesktopAppMenuLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTile(sender) is { } tile)
        {
            model.LaunchApp(tile.Entry, new LaunchOptions(KeepOpen: true));
        }
    }

    private void DesktopAppMenuLocation_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTile(sender) is { } tile && tile.Entry.Kind != TargetKind.Packaged && File.Exists(tile.Entry.Target))
        {
            try { WindowsFileLocation.Open(tile.Entry); } catch { }
        }
    }

    // Drag-to-reposition on Desktop
    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (config.Settings.LockDesktopContainers) return;
        if (e.OriginalSource is DependencyObject dep)
        {
            var curr = dep;
            while (curr is not null && curr != sender)
            {
                if (curr is Button) return;
                curr = VisualTreeHelper.GetParent(curr);
            }
        }

        dragMouseDownDip = e.GetPosition(this);
        dragStartCursorPx = WindowPlacement.GetCursorPixelPoint();
        dragStartNormX = config.Settings.DesktopDockX;
        dragStartNormY = config.Settings.DesktopDockY;
        isDraggingWindow = false;
        if (sender is UIElement el) el.CaptureMouse();
        e.Handled = true;
    }

    private void DragSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not UIElement el || !el.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        if (config.Settings.LockDesktopContainers) return;

        var pos = e.GetPosition(this);
        if (!isDraggingWindow && (Math.Abs(pos.X - dragMouseDownDip.X) > 4 || Math.Abs(pos.Y - dragMouseDownDip.Y) > 4))
        {
            isDraggingWindow = true;
            hoverOpenTimer.Stop();
        }

        if (isDraggingWindow)
        {
            int availW = Math.Max(1, currentWorkArea.Width - currentBounds.Width);
            int availH = Math.Max(1, currentWorkArea.Height - currentBounds.Height);
            var cursor = WindowPlacement.GetCursorPixelPoint();
            double dx = (cursor.X - dragStartCursorPx.X) / (double)availW;
            double dy = (cursor.Y - dragStartCursorPx.Y) / (double)availH;
            config.Settings.DesktopDockX = Math.Clamp(dragStartNormX + dx, 0.01, 0.96);
            config.Settings.DesktopDockY = Math.Clamp(dragStartNormY + dy, 0.02, 0.94);
            ApplyPlacement();
        }
    }

    private void DragSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement el && el.IsMouseCaptured)
        {
            el.ReleaseMouseCapture();
        }

        if (isDraggingWindow)
        {
            isDraggingWindow = false;
            try { persist(); } catch { }
            e.Handled = true;
        }
    }

    // Drag and Drop Files into Desktop Containers
    private void ContainerBox_DragOver(object sender, DragEventArgs e)
    {
        if (!ShellDropReader.HasCandidates(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ContainerBox_Drop(object sender, DragEventArgs e)
    {
        if (!ShellDropReader.HasCandidates(e.Data)) return;
        if (sender is FrameworkElement { DataContext: DesktopContainerTile tile })
        {
            var items = ShellDropReader.Read(e.Data);
            bool isSeparate = config.Settings.DesktopContentSource == DesktopContentSource.SeparateDesktopContainers;
            model.ImportIntoFolder(tile.Folder, items, isDesktopSeparate: isSeparate);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        mouseLeftBothUtc = null;
        RootContent.Opacity = 1.0;
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        hoverOpenTimer.Stop();
        RootContent.Opacity = Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
    }

    private void SwitchStyle_Click(object sender, RoutedEventArgs e)
    {
        config.Settings.DesktopDisplayStyle = config.Settings.DesktopDisplayStyle == DesktopDisplayStyle.CompactFlyout
            ? DesktopDisplayStyle.ExpandedBoxes
            : DesktopDisplayStyle.CompactFlyout;
        try { persist(); } catch { }
        SyncWithConfig();
        PlayDesktopWarpTransition(new Point(0.5, 0.15));
    }

    private void ToggleDesktopIcons_Click(object sender, RoutedEventArgs e)
    {
        bool currentlyHidden = DesktopIconsService.AreDesktopIconsHidden();
        bool nextHide = !currentlyHidden;
        config.Settings.HideDesktopIcons = nextHide;
        DesktopIconsService.SetDesktopIconsHidden(nextHide, explicitUserAction: true);
        try { persist(); } catch { }
        SyncWithConfig();
    }

    private void ToggleLockPosition_Click(object sender, RoutedEventArgs e)
    {
        config.Settings.LockDesktopContainers = !config.Settings.LockDesktopContainers;
        try { persist(); } catch { }
        SyncWithConfig();
    }

    private void DesktopSettings_Click(object sender, RoutedEventArgs e)
    {
        openSettings("Desktop");
    }

    private void DisableDesktopContainers_Click(object sender, RoutedEventArgs e)
    {
        if (!config.Settings.EnableTaskbar && !config.Settings.EnableEdgeSidebar)
        {
            config.Settings.EnableTaskbar = true;
        }
        config.Settings.EnableDesktopContainers = false;
        try { persist(); } catch { }
        SyncWithConfig();
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        isContextMenuOpen = true;
        bool iconsHidden = DesktopIconsService.AreDesktopIconsHidden();
        if (ToggleDesktopIconsMenuItem is not null)
        {
            ToggleDesktopIconsMenuItem.Header = LocalizationService.Get(iconsHidden ? "Desktop.Menu.ShowDesktopIcons" : "Desktop.Menu.HideDesktopIcons");
        }
        if (ToggleLockPositionMenuItem is not null)
        {
            ToggleLockPositionMenuItem.Header = LocalizationService.Get(config.Settings.LockDesktopContainers ? "Desktop.Menu.UnlockPosition" : "Desktop.Menu.LockPosition");
        }
    }
    private void ContextMenu_Closed(object sender, RoutedEventArgs e) => isContextMenuOpen = false;

    private void StopAllTimers()
    {
        hoverOpenTimer.Stop();
        closeWatchTimer.Stop();
        mouseLeftBothUtc = null;
        pendingHoverButton = null;
        pendingHoverFolder = null;
    }

    public void Cleanup()
    {
        StopAllTimers();
        StopWarpAnimation();
        CloseAllBoxWindows();
        CloseRecycleBinWindow();
        model.PropertyChanged -= OnModelPropertyChanged;
    }
}
