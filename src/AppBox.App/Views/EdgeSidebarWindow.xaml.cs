using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AppBox.App.Services;
using AppBox.App.ViewModels;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.Views;

public partial class EdgeSidebarWindow : Window
{
    private readonly Configuration config;
    private readonly LauncherViewModel model;
    private readonly LauncherWindow launcher;
    private readonly Action persist;
    private readonly Action<string> openSettings;

    private readonly DispatcherTimer openTimer;
    private readonly DispatcherTimer containerHoverTimer;
    private readonly DispatcherTimer closeWatchTimer;
    private readonly DispatcherTimer environmentTimer;

    private PopupBounds currentBounds;
    private PixelRect currentWorkArea;
    private double currentDpiScale = 1.0;
    private IntPtr currentMonitor;

    private bool isExpanded;
    private bool isContextMenuOpen;
    private bool isDraggingHandle;
    private bool isExternalDragOver;
    private bool isSuppressedByFullscreen;
    private Point handleMouseDownPoint;
    private DateTime? mouseLeftBothUtc;
    private Button? pendingHoverButton;
    private AppFolder? pendingHoverFolder;
    private bool openedLauncherFromSidebar;

    public bool IsExpanded => isExpanded;
    public PopupBounds CurrentBounds => currentBounds;

    public EdgeSidebarWindow(
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

        openTimer = new DispatcherTimer();
        openTimer.Tick += OnOpenTimerTick;

        containerHoverTimer = new DispatcherTimer();
        containerHoverTimer.Tick += OnContainerHoverTimerTick;

        closeWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        closeWatchTimer.Tick += OnCloseWatchTimerTick;

        environmentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        environmentTimer.Tick += OnEnvironmentTimerTick;

        SourceInitialized += (_, _) => ApplyWindowStyle();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyPlacement);
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
        UpdateHandleVisuals();
    }

    public bool IsCursorOverSidebar()
    {
        if (!IsVisible || isSuppressedByFullscreen) return false;
        var cursor = WindowPlacement.GetCursorPixelPoint();
        return cursor.X >= currentBounds.X - 4 &&
               cursor.X <= currentBounds.X + currentBounds.Width + 4 &&
               cursor.Y >= currentBounds.Y - 4 &&
               cursor.Y <= currentBounds.Y + currentBounds.Height + 4;
    }

    public void SyncWithConfig()
    {
        if (!config.Settings.EnableEdgeSidebar)
        {
            StopAllTimers();
            environmentTimer.Stop();
            isExpanded = false;
            isSuppressedByFullscreen = false;
            if (IsVisible) Hide();
            return;
        }

        model.RefreshSidebarRecentApps();
        UpdateModeVisuals();
        UpdateHandleVisuals();

        if (!IsVisible)
        {
            Show();
        }
        ApplyWindowStyle();
        ApplyPlacement();
        if (!environmentTimer.IsEnabled)
        {
            environmentTimer.Start();
        }
    }

    public void CollapseAfterLaunch()
    {
        if (!isExpanded) return;
        openedLauncherFromSidebar = false;
        Collapse(alsoCloseLauncher: false);
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsVisible) return;
        if (e.PropertyName is nameof(LauncherViewModel.HasFolders) or nameof(LauncherViewModel.HeaderBadge) or nameof(LauncherViewModel.IsOverview) or nameof(LauncherViewModel.HasSidebarRecentApps) or nameof(LauncherViewModel.SelectedFolder))
        {
            UpdateModeVisuals();
            UpdateHandleVisuals();
            if (isExpanded) ApplyPlacement();
        }
    }

    private void OnEnvironmentTimerTick(object? sender, EventArgs e)
    {
        if (!config.Settings.EnableEdgeSidebar || !IsVisible) return;

        // 1. Follow Mouse Cursor Monitor when collapsed
        if (!isExpanded && !isDraggingHandle && config.Settings.SidebarMonitorMode == SidebarMonitorMode.CursorMonitor)
        {
            var nextMonitor = WindowPlacement.ResolveSidebarMonitor(SidebarMonitorMode.CursorMonitor, 0);
            if (nextMonitor != IntPtr.Zero && nextMonitor != currentMonitor)
            {
                ApplyPlacement();
            }
        }

        // 2. Fullscreen / Game Auto-Hide (Do Not Disturb Mode)
        if (config.Settings.SidebarAutoHideOnFullscreen)
        {
            bool fullscreenActive = !isExpanded && !isDraggingHandle && WindowPlacement.IsFullscreenAppActiveOnMonitor(currentWorkArea);
            if (fullscreenActive != isSuppressedByFullscreen)
            {
                isSuppressedByFullscreen = fullscreenActive;
                if (fullscreenActive)
                {
                    openTimer.Stop();
                    HandleRoot.Visibility = Visibility.Collapsed;
                    HandleRoot.IsHitTestVisible = false;
                }
                else
                {
                    HandleRoot.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
                    HandleRoot.IsHitTestVisible = true;
                    UpdateHandleVisuals();
                }
            }
        }
        else if (isSuppressedByFullscreen)
        {
            isSuppressedByFullscreen = false;
            HandleRoot.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
            HandleRoot.IsHitTestVisible = true;
            UpdateHandleVisuals();
        }
    }

    private void UpdateModeVisuals()
    {
        bool single = config.Settings.SidebarLayoutMode == LayoutMode.SingleAppBox;
        SingleModeButton.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        ContainersScroller.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        EmptyContainersButton.Visibility = (!single && model.Containers.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

        bool showRecent = config.Settings.SidebarShowRecentApps && model.SidebarRecentApps.Count > 0;
        RecentAppsSection.Visibility = showRecent ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHandleVisuals()
    {
        HandlePill.Width = Math.Clamp(config.Settings.SidebarHandleWidth, 3, 16);
        HandlePill.Height = Math.Clamp(config.Settings.SidebarHandleHeight, 48, 320);
        HandlePill.Opacity = Math.Clamp(config.Settings.SidebarHandleOpacity, 0.0, 1.0);
        HandlePill.HorizontalAlignment = config.Settings.SidebarEdge == ScreenEdge.Left
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        HandlePill.Background = ResolveHandleBrush();
    }

    private Brush ResolveHandleBrush()
    {
        try
        {
            if (config.Settings.SidebarHandleColorMode == SidebarHandleColorMode.Custom &&
                !string.IsNullOrWhiteSpace(config.Settings.SidebarHandleCustomColor))
            {
                if (ColorConverter.ConvertFromString(config.Settings.SidebarHandleCustomColor.Trim()) is Color customColor)
                {
                    var brush = new SolidColorBrush(customColor);
                    brush.Freeze();
                    return brush;
                }
            }
            else if (config.Settings.SidebarHandleColorMode == SidebarHandleColorMode.ContainerTint)
            {
                var targetIcon = (model.SelectedFolder is { } sel ? model.Containers.FirstOrDefault(c => c.Folder.Id == sel.Id)?.Icon : null)
                                 ?? model.Containers.FirstOrDefault()?.Icon;
                if (IconTint.Extract(targetIcon, ThemeService.Dark) is { } extracted)
                {
                    var brush = new SolidColorBrush(extracted.Tint);
                    brush.Freeze();
                    return brush;
                }
            }
        }
        catch
        {
        }
        return (Brush)FindResource("Accent");
    }

    private double CalculateExpandedHeightDip()
    {
        double recentHeight = (config.Settings.SidebarShowRecentApps && model.SidebarRecentApps.Count > 0)
            ? model.SidebarRecentApps.Count * 52.0 + 14.0
            : 0.0;

        if (config.Settings.SidebarLayoutMode == LayoutMode.SingleAppBox)
        {
            return 164.0 + recentHeight;
        }

        int count = Math.Max(1, model.Containers.Count);
        double maxContainerScroller = Math.Max(180.0, 410.0 - recentHeight * 0.5);
        ContainersScroller.MaxHeight = maxContainerScroller;
        double itemsHeight = Math.Min(count * 58.0, maxContainerScroller);
        return itemsHeight + recentHeight + 106.0;
    }

    public void ApplyPlacement()
    {
        if (!IsVisible) return;

        double widthDip = isExpanded ? 74 : Math.Max(14, config.Settings.SidebarHandleWidth + 8);
        double heightDip = isExpanded ? CalculateExpandedHeightDip() : Math.Clamp(config.Settings.SidebarHandleHeight, 48, 320) + 16;

        Width = widthDip;
        Height = heightDip;

        currentMonitor = WindowPlacement.ResolveSidebarMonitor(config.Settings.SidebarMonitorMode, config.Settings.SidebarMonitorIndex);
        var (bounds, work, scale) = WindowPlacement.PlaceEdgeSidebar(
            this,
            config.Settings.SidebarEdge,
            config.Settings.SidebarVerticalPosition,
            widthDip,
            heightDip,
            isExpanded,
            config.Settings.SidebarMonitorMode,
            config.Settings.SidebarMonitorIndex);

        currentBounds = bounds;
        currentWorkArea = work;
        currentDpiScale = scale > 0 ? scale : 1.0;
    }

    public void Expand(bool openSingleOverview = true)
    {
        if (!config.Settings.EnableEdgeSidebar || isSuppressedByFullscreen) return;
        openTimer.Stop();

        model.RefreshSidebarRecentApps();
        UpdateModeVisuals();
        UpdateHandleVisuals();

        bool wasExpanded = isExpanded;
        isExpanded = true;
        HandleRoot.Visibility = Visibility.Collapsed;
        StripRoot.Visibility = Visibility.Visible;

        ApplyPlacement();

        if (!wasExpanded && config.Settings.EnableAnimations && SystemParameters.ClientAreaAnimation)
        {
            double startX = config.Settings.SidebarEdge == ScreenEdge.Right ? 24.0 : -24.0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            StripTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(startX, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            StripRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(130)) { EasingFunction = ease });
        }
        else
        {
            StripTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            StripRoot.BeginAnimation(OpacityProperty, null);
            StripTranslate.X = 0;
            StripRoot.Opacity = 1.0;
        }

        mouseLeftBothUtc = null;
        closeWatchTimer.Start();

        if (config.Settings.SidebarLayoutMode == LayoutMode.SingleAppBox && openSingleOverview)
        {
            bool shouldAutoOpenOverview = config.Settings.SidebarOpeningBehavior is ContainerOpeningBehavior.Hover or ContainerOpeningBehavior.ClickOrHover;
            if (shouldAutoOpenOverview)
            {
                OpenOverviewFlyout();
            }
        }
    }

    public void Collapse(bool alsoCloseLauncher = false)
    {
        StopAllTimers();
        if (alsoCloseLauncher && openedLauncherFromSidebar && launcher.IsVisible && !model.IsEditing)
        {
            openedLauncherFromSidebar = false;
            launcher.Hide();
        }

        isExpanded = false;
        HandleRoot.Visibility = isSuppressedByFullscreen ? Visibility.Collapsed : Visibility.Visible;
        StripRoot.Visibility = Visibility.Collapsed;
        UpdateHandleVisuals();
        ApplyPlacement();
    }

    private void StopAllTimers()
    {
        openTimer.Stop();
        containerHoverTimer.Stop();
        closeWatchTimer.Stop();
        mouseLeftBothUtc = null;
        pendingHoverButton = null;
        pendingHoverFolder = null;
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        if (isSuppressedByFullscreen) return;
        mouseLeftBothUtc = null;
        if (!isExpanded && !isDraggingHandle)
        {
            HandlePill.Opacity = Math.Max(0.65, Math.Min(1.0, config.Settings.SidebarHandleOpacity + 0.35));
            int delay = Math.Clamp(config.Settings.HoverOpeningDelayMs, 100, 2000);
            openTimer.Interval = TimeSpan.FromMilliseconds(delay);
            openTimer.Stop();
            openTimer.Start();
        }
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        openTimer.Stop();
        containerHoverTimer.Stop();
        if (!isExpanded)
        {
            UpdateHandleVisuals();
        }
    }

    private void OnOpenTimerTick(object? sender, EventArgs e)
    {
        openTimer.Stop();
        if (!isExpanded && !isDraggingHandle && !isSuppressedByFullscreen && (IsCursorOverSidebar() || isExternalDragOver))
        {
            Expand(openSingleOverview: true);
        }
    }

    private void OnCloseWatchTimerTick(object? sender, EventArgs e)
    {
        if (!isExpanded)
        {
            closeWatchTimer.Stop();
            return;
        }

        if (isContextMenuOpen || isExternalDragOver || model.IsEditing || launcher.SuppressDismiss)
        {
            mouseLeftBothUtc = null;
            return;
        }

        bool overSidebar = IsCursorOverSidebar();
        bool overLauncher = IsCursorOverLauncher();

        if (overSidebar || overLauncher)
        {
            mouseLeftBothUtc = null;
            return;
        }

        mouseLeftBothUtc ??= DateTime.UtcNow;
        int closeDelayMs = Math.Clamp(config.Settings.HoverClosingDelayMs, 100, 2000);
        if ((DateTime.UtcNow - mouseLeftBothUtc.Value).TotalMilliseconds >= closeDelayMs)
        {
            Collapse(alsoCloseLauncher: true);
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

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (isSuppressedByFullscreen) return;
        handleMouseDownPoint = e.GetPosition(this);
        isDraggingHandle = false;
        HandleRoot.CaptureMouse();
        e.Handled = true;
    }

    private void Handle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!HandleRoot.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;

        var currentPos = e.GetPosition(this);
        if (!isDraggingHandle && Math.Abs(currentPos.Y - handleMouseDownPoint.Y) > 5)
        {
            isDraggingHandle = true;
            openTimer.Stop();
        }

        if (isDraggingHandle && currentWorkArea.Bottom - currentWorkArea.Top > 64)
        {
            var cursor = WindowPlacement.GetCursorPixelPoint();
            double ratio = (cursor.Y - currentWorkArea.Top) / (double)(currentWorkArea.Bottom - currentWorkArea.Top);
            config.Settings.SidebarVerticalPosition = Math.Clamp(ratio, 0.05, 0.95);
            ApplyPlacement();
        }
    }

    private void Handle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (HandleRoot.IsMouseCaptured)
        {
            HandleRoot.ReleaseMouseCapture();
        }

        if (isDraggingHandle)
        {
            isDraggingHandle = false;
            try { persist(); } catch { }
        }
        else
        {
            Expand(openSingleOverview: false);
            if (config.Settings.SidebarLayoutMode == LayoutMode.SingleAppBox)
            {
                OpenOverviewFlyout();
            }
        }
        e.Handled = true;
    }

    private WindowPlacement.Anchor CreateLateralAnchor(FrameworkElement? targetElement = null)
    {
        int centerY = currentBounds.Y + currentBounds.Height / 2;
        if (targetElement is not null && PresentationSource.FromVisual(targetElement) is not null)
        {
            try
            {
                var screenPoint = targetElement.PointToScreen(new Point(targetElement.ActualWidth / 2, targetElement.ActualHeight / 2));
                centerY = (int)Math.Round(screenPoint.Y);
            }
            catch
            {
            }
        }
        var centerX = config.Settings.SidebarEdge == ScreenEdge.Left
            ? currentBounds.X + currentBounds.Width
            : currentBounds.X;
        return new WindowPlacement.Anchor(
            new PixelPoint(centerX, centerY),
            config.Settings.SidebarEdge,
            currentBounds);
    }

    private void OpenOverviewFlyout(FrameworkElement? button = null)
    {
        openedLauncherFromSidebar = true;
        launcher.SetLateralMode(true);
        model.OpenOverview();
        launcher.Present(config.Settings.EnableAnimations, CreateLateralAnchor(button ?? SingleModeButton));
    }

    private void OpenContainerFlyout(AppFolder folder, FrameworkElement? button = null)
    {
        openedLauncherFromSidebar = true;
        launcher.SetLateralMode(true);
        bool alreadyOpenSameFolder = launcher.IsVisible && !model.IsOverview && model.SelectedFolder?.Id == folder.Id;
        model.OpenContainer(folder, allowBackToOverview: false);
        launcher.Present(!alreadyOpenSameFolder && config.Settings.EnableAnimations, CreateLateralAnchor(button));
    }

    private void SidebarList_MouseMove(object sender, MouseEventArgs e)
    {
        if (!model.DockMagnificationEnabled || !SystemParameters.ClientAreaAnimation) return;
        if (sender is ItemsControl list)
        {
            MicroAnimations.ApplyDockMagnification(list, e.GetPosition(list), verticalStrip: true, influenceRadiusDip: 96, maxExtraScale: 0.30);
        }
    }

    private void SidebarList_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is ItemsControl list)
        {
            MicroAnimations.ResetDockMagnification(list);
        }
    }

    private void RecentAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AppTile tile } btn)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (model.LaunchBounceEnabled && SystemParameters.ClientAreaAnimation && (ctrl || !config.Settings.CloseAfterLaunch))
            {
                MicroAnimations.PlayLaunchBounce(btn);
            }
            model.LaunchApp(tile.Entry, new LaunchOptions(KeepOpen: ctrl, Elevated: shift));
            if (!ctrl && config.Settings.CloseAfterLaunch)
            {
                Collapse(alsoCloseLauncher: true);
            }
        }
    }

    private void SingleModeButton_Click(object sender, RoutedEventArgs e)
    {
        OpenOverviewFlyout(sender as FrameworkElement);
    }

    private void SingleModeButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (config.Settings.SidebarOpeningBehavior is ContainerOpeningBehavior.Hover or ContainerOpeningBehavior.ClickOrHover)
        {
            if (!launcher.IsVisible || !model.IsOverview)
            {
                OpenOverviewFlyout(sender as FrameworkElement);
            }
        }
    }

    private void ContainerButton_Click(object sender, RoutedEventArgs e)
    {
        containerHoverTimer.Stop();
        if (sender is Button { DataContext: ContainerTile tile } button)
        {
            OpenContainerFlyout(tile.Folder, button);
        }
    }

    private void ContainerButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (config.Settings.SidebarOpeningBehavior is not (ContainerOpeningBehavior.Hover or ContainerOpeningBehavior.ClickOrHover))
            return;

        if (sender is Button { DataContext: ContainerTile tile } button)
        {
            pendingHoverButton = button;
            pendingHoverFolder = tile.Folder;
            int dwell = launcher.IsVisible
                ? Math.Clamp(config.Settings.HoverOpeningDelayMs / 3, 75, 220)
                : Math.Clamp(config.Settings.HoverOpeningDelayMs / 2, 100, 350);
            containerHoverTimer.Interval = TimeSpan.FromMilliseconds(dwell);
            containerHoverTimer.Stop();
            containerHoverTimer.Start();
        }
    }

    private void ContainerButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender == pendingHoverButton)
        {
            containerHoverTimer.Stop();
            pendingHoverButton = null;
            pendingHoverFolder = null;
        }
    }

    private void OnContainerHoverTimerTick(object? sender, EventArgs e)
    {
        containerHoverTimer.Stop();
        if (pendingHoverFolder is { } folder && pendingHoverButton is { IsMouseOver: true } button)
        {
            OpenContainerFlyout(folder, button);
        }
    }

    private void ContainerButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { DataContext: ContainerTile tile })
        {
            model.SelectedFolder = tile.Folder;
        }
    }

    // Drag-and-Drop onto Edge Handle and Sidebar Strip
    private void Window_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (!ShellDropReader.HasCandidates(e.Data)) return;
        isExternalDragOver = true;
        mouseLeftBothUtc = null;
        if (!isExpanded && !isSuppressedByFullscreen)
        {
            HandlePill.Opacity = 1.0;
            openTimer.Interval = TimeSpan.FromMilliseconds(200);
            openTimer.Stop();
            openTimer.Start();
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!ShellDropReader.HasCandidates(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        isExternalDragOver = true;
        mouseLeftBothUtc = null;
        e.Effects = DragDropEffects.Copy;
    }

    private void Window_PreviewDragLeave(object sender, DragEventArgs e)
    {
        isExternalDragOver = false;
        if (!isExpanded)
        {
            openTimer.Stop();
            UpdateHandleVisuals();
        }
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        isExternalDragOver = false;
        if (e.Handled || !ShellDropReader.HasCandidates(e.Data)) return;
        var items = ShellDropReader.Read(e.Data);
        var targetFolder = model.SelectedFolder ?? model.Folders.FirstOrDefault();
        if (model.ImportIntoFolder(targetFolder, items))
        {
            if (targetFolder is not null)
            {
                OpenContainerFlyout(targetFolder);
            }
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ContainerButton_DragOver(object sender, DragEventArgs e)
    {
        if (!ShellDropReader.HasCandidates(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        isExternalDragOver = true;
        if (sender is Button { DataContext: ContainerTile tile } button)
        {
            if (!launcher.IsVisible || model.IsOverview || model.SelectedFolder?.Id != tile.Folder.Id)
            {
                OpenContainerFlyout(tile.Folder, button);
            }
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ContainerButton_Drop(object sender, DragEventArgs e)
    {
        isExternalDragOver = false;
        if (!ShellDropReader.HasCandidates(e.Data)) return;
        if (sender is Button { DataContext: ContainerTile tile } button)
        {
            var items = ShellDropReader.Read(e.Data);
            model.ImportIntoFolder(tile.Folder, items);
            OpenContainerFlyout(tile.Folder, button);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void SingleModeButton_DragOver(object sender, DragEventArgs e)
    {
        if (!ShellDropReader.HasCandidates(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        isExternalDragOver = true;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void SingleModeButton_Drop(object sender, DragEventArgs e)
    {
        isExternalDragOver = false;
        if (!ShellDropReader.HasCandidates(e.Data)) return;
        var items = ShellDropReader.Read(e.Data);
        var targetFolder = model.SelectedFolder ?? model.Folders.FirstOrDefault();
        model.ImportIntoFolder(targetFolder, items);
        if (model.SelectedFolder is { } folder)
        {
            OpenContainerFlyout(folder, SingleModeButton);
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void AllContainers_Click(object sender, RoutedEventArgs e)
    {
        openedLauncherFromSidebar = true;
        launcher.SetLateralMode(true);
        model.OpenOverview();
        launcher.Present(config.Settings.EnableAnimations, CreateLateralAnchor(sender as FrameworkElement));
    }

    private void EditContainer_Click(object sender, RoutedEventArgs e)
    {
        openedLauncherFromSidebar = true;
        launcher.SetLateralMode(true);
        var target = model.SelectedFolder ?? model.Folders.FirstOrDefault();
        if (target is null)
        {
            openSettings("Containers");
            return;
        }
        model.OpenContainer(target);
        launcher.Present(config.Settings.EnableAnimations, CreateLateralAnchor(sender as FrameworkElement));
        model.SetEditing(true);
    }

    private void ManageContainers_Click(object sender, RoutedEventArgs e)
    {
        Collapse(alsoCloseLauncher: true);
        openSettings("Containers");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Collapse(alsoCloseLauncher: true);
        openSettings("Sidebar");
    }

    private void SwitchEdge_Click(object sender, RoutedEventArgs e)
    {
        config.Settings.SidebarEdge = config.Settings.SidebarEdge == ScreenEdge.Right ? ScreenEdge.Left : ScreenEdge.Right;
        try { persist(); } catch { }
        SyncWithConfig();
    }

    private void SwitchSidebarLayout_Click(object sender, RoutedEventArgs e)
    {
        config.Settings.SidebarLayoutMode = config.Settings.SidebarLayoutMode == LayoutMode.SingleAppBox
            ? LayoutMode.MultipleAppBoxes
            : LayoutMode.SingleAppBox;
        try { persist(); } catch { }
        SyncWithConfig();
    }

    private void DisableSidebar_Click(object sender, RoutedEventArgs e)
    {
        config.Settings.EnableEdgeSidebar = false;
        if (!config.Settings.EnableTaskbar)
        {
            config.Settings.EnableTaskbar = true;
        }
        try { persist(); } catch { }
        SyncWithConfig();
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e) => isContextMenuOpen = true;
    private void ContextMenu_Closed(object sender, RoutedEventArgs e) => isContextMenuOpen = false;

    public void Cleanup()
    {
        StopAllTimers();
        environmentTimer.Stop();
        model.PropertyChanged -= OnModelPropertyChanged;
    }
}
