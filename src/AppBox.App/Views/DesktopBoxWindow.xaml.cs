using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AppBox.App.Services;
using AppBox.App.ViewModels;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.Views;

public partial class DesktopBoxWindow : Window
{
    public const double IconSizeDip = 64.0;
    public const double GridMarginDip = 20.0;
    public const double GridStepDip = 84.0;

    private readonly Configuration config;
    private readonly LauncherViewModel model;
    private readonly Action persist;
    private readonly Action<string> openSettings;
    private readonly Action<AppFolder> openInFlyout;
    private readonly Action switchStyle;
    private readonly Action toggleDesktopIcons;
    private readonly Action resetPositions;
    private readonly Action disableDesktopContainers;
    private readonly Action? onExpansionStateChanged;

    private DesktopContainerTile tile;
    private int fallbackIndex;
    private (int Col, int Row)? assignedCell;

    private readonly DispatcherTimer hoverExpandTimer = new();
    private readonly DispatcherTimer hoverCollapseTimer = new();

    private bool isExpanded;
    private bool isCollapsing;
    private bool isContextMenuOpen;
    private bool isOccluded;

    private bool isDragging;
    private bool dragMoved;
    private FrameworkElement? dragSurface;
    private Point dragStartScreen;
    private double dragStartWindowLeft;
    private double dragStartWindowTop;
    private PixelRect dragWorkArea;
    private double dragDpiScale = 1.0;

    private double currentAnchorU = 0.1;
    private double currentAnchorV = 0.1;
    private ScreenEdge currentLateralEdge = ScreenEdge.Left;

    private WarpMeshRenderer? warpRenderer;
    private EventHandler? warpRenderHandler;
    private ContainerAnimationStyle lastRandomStyle = ContainerAnimationStyle.Genie;

    public Guid FolderId => tile.Folder.Id;
    public bool IsExpanded => isExpanded;
    public Rect CompactIconDipBounds { get; private set; }
    public Rect ExpandedDipBounds { get; private set; }

    public static bool DoesExpandedBoxOverlapIcon(Rect expandedBounds, Rect iconBounds)
    {
        if (expandedBounds.IsEmpty || iconBounds.IsEmpty ||
            expandedBounds.Width <= 0 || expandedBounds.Height <= 0 ||
            iconBounds.Width <= 0 || iconBounds.Height <= 0)
        {
            return false;
        }

        // Strict interior intersection avoids hiding neighbor icons that only touch an outer edge by 0 DIP.
        return expandedBounds.Left < iconBounds.Right &&
               expandedBounds.Right > iconBounds.Left &&
               expandedBounds.Top < iconBounds.Bottom &&
               expandedBounds.Bottom > iconBounds.Top;
    }

    public DesktopBoxWindow(
        Configuration config,
        LauncherViewModel model,
        DesktopContainerTile tile,
        int fallbackIndex,
        Action persist,
        Action<string> openSettings,
        Action<AppFolder> openInFlyout,
        Action switchStyle,
        Action toggleDesktopIcons,
        Action resetPositions,
        Action disableDesktopContainers,
        Action? onExpansionStateChanged = null)
    {
        InitializeComponent();
        this.config = config;
        this.model = model;
        this.tile = tile;
        this.fallbackIndex = fallbackIndex;
        this.persist = persist;
        this.openSettings = openSettings;
        this.openInFlyout = openInFlyout;
        this.switchStyle = switchStyle;
        this.toggleDesktopIcons = toggleDesktopIcons;
        this.resetPositions = resetPositions;
        this.disableDesktopContainers = disableDesktopContainers;
        this.onExpansionStateChanged = onExpansionStateChanged;

        hoverExpandTimer.Tick += HoverExpandTimer_Tick;
        hoverCollapseTimer.Tick += HoverCollapseTimer_Tick;

        SourceInitialized += (_, _) => ApplyWindowStyle();
    }

    public static (double WorkLeftDip, double WorkTopDip, double WorkWidthDip, double WorkHeightDip, int MaxCols, int MaxRows) GetGridDimensions(PixelRect work, double scale)
    {
        double s = scale > 0 && double.IsFinite(scale) ? scale : 1.0;
        double workLeftDip = work.Left / s;
        double workTopDip = work.Top / s;
        double workWidthDip = Math.Max(IconSizeDip + GridMarginDip * 2, (work.Right - work.Left) / s);
        double workHeightDip = Math.Max(IconSizeDip + GridMarginDip * 2, (work.Bottom - work.Top) / s);
        int maxCols = Math.Max(1, (int)Math.Floor((workWidthDip - GridMarginDip * 2 + (GridStepDip - IconSizeDip)) / GridStepDip));
        int maxRows = Math.Max(1, (int)Math.Floor((workHeightDip - GridMarginDip * 2 + (GridStepDip - IconSizeDip)) / GridStepDip));
        return (workLeftDip, workTopDip, workWidthDip, workHeightDip, maxCols, maxRows);
    }

    public static (int Col, int Row) SnapNormToGridCell(double normX, double normY, PixelRect work, double scale)
    {
        var (_, _, workWidthDip, workHeightDip, maxCols, maxRows) = GetGridDimensions(work, scale);
        double availW = Math.Max(1.0, workWidthDip - IconSizeDip);
        double availH = Math.Max(1.0, workHeightDip - IconSizeDip);
        double leftOffsetDip = Math.Clamp(normX, 0.0, 1.0) * availW;
        double topOffsetDip = Math.Clamp(normY, 0.0, 1.0) * availH;
        int col = Math.Clamp((int)Math.Round((leftOffsetDip - GridMarginDip) / GridStepDip), 0, maxCols - 1);
        int row = Math.Clamp((int)Math.Round((topOffsetDip - GridMarginDip) / GridStepDip), 0, maxRows - 1);
        return (col, row);
    }

    public static (double LeftDip, double TopDip) GridCellToDip(int col, int row, PixelRect work, double scale)
    {
        var (workLeftDip, workTopDip, workWidthDip, workHeightDip, maxCols, maxRows) = GetGridDimensions(work, scale);
        int c = Math.Clamp(col, 0, maxCols - 1);
        int r = Math.Clamp(row, 0, maxRows - 1);
        double leftDip = Math.Clamp(workLeftDip + GridMarginDip + c * GridStepDip, workLeftDip + 8, workLeftDip + workWidthDip - IconSizeDip - 8);
        double topDip = Math.Clamp(workTopDip + GridMarginDip + r * GridStepDip, workTopDip + 8, workTopDip + workHeightDip - IconSizeDip - 8);
        return (leftDip, topDip);
    }

    public static (double NormX, double NormY) GridCellToNorm(int col, int row, PixelRect work, double scale)
    {
        var (workLeftDip, workTopDip, workWidthDip, workHeightDip, _, _) = GetGridDimensions(work, scale);
        var (leftDip, topDip) = GridCellToDip(col, row, work, scale);
        double availW = Math.Max(1.0, workWidthDip - IconSizeDip);
        double availH = Math.Max(1.0, workHeightDip - IconSizeDip);
        double normX = Math.Clamp((leftDip - workLeftDip) / availW, 0.0, 1.0);
        double normY = Math.Clamp((topDip - workTopDip) / availH, 0.0, 1.0);
        return (normX, normY);
    }

    public void UpdateFromTile(DesktopContainerTile newTile, int newFallbackIndex, HashSet<(int Col, int Row)>? occupiedCells = null)
    {
        tile = newTile;
        fallbackIndex = newFallbackIndex;
        assignedCell = null;

        CompactIconImage.Source = tile.Icon;
        CompactBadge.ToolTip = $"{tile.Folder.Name} ({tile.CountText})";
        FolderIconImage.Source = tile.Icon;
        FolderNameText.Text = tile.Folder.Name;
        CountBadgeText.Text = tile.CountText;
        AppsList.ItemsSource = tile.Applications;
        EmptyText.Visibility = tile.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        ApplyVisualState();
        ApplyPlacement(occupiedCells);
    }

    public void ApplyWindowStyle()
    {
        if (new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        WindowPlacement.ApplyToolWindowStyle(this, ThemeService.Dark);
        NativeWindowStyle.DisableAcrylic(this);
        WindowPlacement.SendToDesktopBottom(this);
    }

    private void ApplyVisualState()
    {
        CompactBadge.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        BoxCard.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;

        var cursor = config.Settings.LockDesktopContainers ? Cursors.Hand : Cursors.SizeAll;
        CompactBadge.Cursor = cursor;
        HeaderBar.Cursor = cursor;

        if (!IsMouseOver && !isContextMenuOpen && !isDragging)
        {
            RootContent.Opacity = Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
        }
    }

    private (double WidthDip, double HeightDip) CalculateDesiredSizeDip()
    {
        if (!isExpanded)
        {
            return (IconSizeDip, IconSizeDip);
        }

        const double boxWidth = 332;
        int appCount = tile.Applications.Count;
        int appRows = appCount == 0 ? 1 : (int)Math.Ceiling(appCount / 4.0);
        double contentHeight = appCount == 0 ? 44 : Math.Min(420, appRows * 82 + 12);
        return (boxWidth, 56 + contentHeight);
    }

    private (int Col, int Row) ResolveGridCell(PixelRect work, double scale, HashSet<(int Col, int Row)>? occupiedCells)
    {
        var (_, _, _, _, maxCols, maxRows) = GetGridDimensions(work, scale);

        if (assignedCell is { } existing && occupiedCells is null)
        {
            return (Math.Clamp(existing.Col, 0, maxCols - 1), Math.Clamp(existing.Row, 0, maxRows - 1));
        }

        int col;
        int row;
        if (tile.Folder.DesktopX is { } dx && tile.Folder.DesktopY is { } dy)
        {
            (col, row) = SnapNormToGridCell(dx, dy, work, scale);
        }
        else
        {
            int slot = (config.Settings.HideDesktopIcons ? 1 : 0) + fallbackIndex;
            col = slot % maxCols;
            row = Math.Min(maxRows - 1, slot / maxCols);
        }

        if (occupiedCells is not null)
        {
            int totalCells = maxCols * maxRows;
            int startSlot = row * maxCols + col;
            for (int attempt = 0; attempt < totalCells && occupiedCells.Contains((col, row)); attempt++)
            {
                int nextSlot = (startSlot + attempt + 1) % totalCells;
                col = nextSlot % maxCols;
                row = nextSlot / maxCols;
            }
            occupiedCells.Add((col, row));
        }

        assignedCell = (col, row);
        return (col, row);
    }

    public void ApplyPlacement(HashSet<(int Col, int Row)>? occupiedCells = null)
    {
        var (work, scale, hwnd) = WindowPlacement.ResolveDesktopWorkArea(
            this,
            config.Settings.SidebarMonitorMode,
            config.Settings.SidebarMonitorIndex);
        if (work.Right - work.Left < 32 || work.Bottom - work.Top < 32) return;

        var (workLeftDip, workTopDip, workWidthDip, workHeightDip, _, _) = GetGridDimensions(work, scale);
        var (col, row) = ResolveGridCell(work, scale, occupiedCells);
        var (iconLeftDip, iconTopDip) = GridCellToDip(col, row, work, scale);
        var (widthDip, heightDip) = CalculateDesiredSizeDip();

        double targetLeftDip = iconLeftDip;
        double targetTopDip = iconTopDip;

        if (isExpanded)
        {
            double workRightDip = workLeftDip + workWidthDip;
            double workBottomDip = workTopDip + workHeightDip;

            double spaceRight = (workRightDip - 8) - iconLeftDip;
            double spaceLeft = (iconLeftDip + IconSizeDip) - (workLeftDip + 8);
            bool opensRight = spaceRight >= widthDip || spaceRight >= spaceLeft;

            targetLeftDip = opensRight
                ? Math.Clamp(iconLeftDip, workLeftDip + 8, Math.Max(workLeftDip + 8, workRightDip - widthDip - 8))
                : Math.Clamp(iconLeftDip + IconSizeDip - widthDip, workLeftDip + 8, Math.Max(workLeftDip + 8, workRightDip - widthDip - 8));

            double spaceDown = (workBottomDip - 8) - iconTopDip;
            double spaceUp = (iconTopDip + IconSizeDip) - (workTopDip + 8);
            bool opensDown = spaceDown >= heightDip || spaceDown >= spaceUp;

            targetTopDip = opensDown
                ? Math.Clamp(iconTopDip, workTopDip + 8, Math.Max(workTopDip + 8, workBottomDip - heightDip - 8))
                : Math.Clamp(iconTopDip + IconSizeDip - heightDip, workTopDip + 8, Math.Max(workTopDip + 8, workBottomDip - heightDip - 8));

            currentAnchorU = Math.Clamp(((iconLeftDip + IconSizeDip * 0.5) - targetLeftDip) / widthDip, 0.04, 0.96);
            currentAnchorV = Math.Clamp(((iconTopDip + IconSizeDip * 0.5) - targetTopDip) / heightDip, 0.04, 0.96);
            currentLateralEdge = opensRight ? ScreenEdge.Left : ScreenEdge.Right;
        }

        CompactIconDipBounds = new Rect(iconLeftDip, iconTopDip, IconSizeDip, IconSizeDip);
        ExpandedDipBounds = isExpanded
            ? new Rect(targetLeftDip, targetTopDip, widthDip, heightDip)
            : Rect.Empty;

        Width = widthDip;
        Height = heightDip;

        int xPx = (int)Math.Round(targetLeftDip * scale);
        int yPx = (int)Math.Round(targetTopDip * scale);
        int wPx = (int)Math.Ceiling(widthDip * scale);
        int hPx = (int)Math.Ceiling(heightDip * scale);
        WindowPlacement.SetWindowPixelBounds(hwnd, xPx, yPx, wPx, hPx);
    }

    public bool IsCursorOverBox()
    {
        if (!IsVisible) return false;
        var pt = WindowPlacement.GetCursorPixelPoint();
        var dpi = VisualTreeHelper.GetDpi(this);
        var scale = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double leftPx = Left * scale;
        double topPx = Top * scale;
        double rightPx = (Left + ActualWidth) * scale;
        double bottomPx = (Top + ActualHeight) * scale;
        return pt.X >= leftPx && pt.X <= rightPx && pt.Y >= topPx && pt.Y <= bottomPx;
    }

    private void AnimateCompactIconScale(double target)
    {
        if (!config.Settings.EnableAnimations)
        {
            CompactIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            CompactIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            CompactIconScale.ScaleX = target;
            CompactIconScale.ScaleY = target;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(130);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        CompactIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, duration) { EasingFunction = ease });
        CompactIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, duration) { EasingFunction = ease });
    }

    public void SetOccludedByExpandedBox(bool occluded)
    {
        if (isOccluded == occluded) return;
        isOccluded = occluded;

        if (occluded)
        {
            hoverExpandTimer.Stop();
            AnimateCompactIconScale(1.0);
            RootContent.Visibility = Visibility.Hidden;
            RootContent.IsHitTestVisible = false;
        }
        else
        {
            RootContent.Visibility = Visibility.Visible;
            RootContent.IsHitTestVisible = true;
            if (!IsMouseOver && !isContextMenuOpen && !isDragging && !isExpanded)
            {
                RootContent.Opacity = Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
            }
        }
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        if (isOccluded) return;
        hoverCollapseTimer.Stop();
        if (isCollapsing)
        {
            StopWarpAnimation();
            isExpanded = true;
            ApplyVisualState();
            ApplyPlacement();
        }
        RootContent.Opacity = 1.0;
        if (!isExpanded)
        {
            AnimateCompactIconScale(1.08);
        }

        if (isExpanded || isDragging) return;

        var behavior = config.Settings.DesktopOpeningBehavior;
        if (behavior is ContainerOpeningBehavior.Hover or ContainerOpeningBehavior.ClickOrHover)
        {
            hoverExpandTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(config.Settings.HoverOpeningDelayMs, 80, 2000));
            hoverExpandTimer.Start();
        }
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        hoverExpandTimer.Stop();
        AnimateCompactIconScale(1.0);
        if (!isContextMenuOpen && !isDragging)
        {
            RootContent.Opacity = Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
        }

        if (isExpanded && !isContextMenuOpen && !isDragging)
        {
            hoverCollapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(config.Settings.HoverClosingDelayMs, 100, 2000));
            hoverCollapseTimer.Start();
        }
    }

    private void HoverExpandTimer_Tick(object? sender, EventArgs e)
    {
        hoverExpandTimer.Stop();
        if (!IsMouseOver || isExpanded || isDragging) return;

        ExpandBox();
    }

    private void HoverCollapseTimer_Tick(object? sender, EventArgs e)
    {
        hoverCollapseTimer.Stop();
        if (IsMouseOver || isContextMenuOpen || isDragging || !isExpanded) return;

        CollapseBox();
    }

    private void ExpandBox()
    {
        hoverExpandTimer.Stop();
        hoverCollapseTimer.Stop();
        if (isCollapsing)
        {
            StopWarpAnimation();
        }
        else if (isExpanded)
        {
            return;
        }

        isExpanded = true;
        AnimateCompactIconScale(1.0);
        ApplyVisualState();
        ApplyPlacement();
        onExpansionStateChanged?.Invoke();
        PlayBoxWarpTransition(isOpening: true);
    }

    private void CollapseBox()
    {
        hoverExpandTimer.Stop();
        hoverCollapseTimer.Stop();
        if (!isExpanded || isCollapsing) return;

        isCollapsing = true;
        PlayBoxWarpTransition(isOpening: false, onComplete: () =>
        {
            isCollapsing = false;
            isExpanded = false;
            ApplyVisualState();
            ApplyPlacement();
            onExpansionStateChanged?.Invoke();
        });
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CollapseBox();
    }

    private void OpenInFlyout_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        openInFlyout(tile.Folder);
    }

    // --- Drag to Freely Reposition Individual Square Container / Expanded Header ---
    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindAncestor<Button>(dep) is not null) return;

        isDragging = true;
        dragMoved = false;
        hoverExpandTimer.Stop();
        hoverCollapseTimer.Stop();

        var pt = WindowPlacement.GetCursorPixelPoint();
        dragStartScreen = new Point(pt.X, pt.Y);
        dragStartWindowLeft = Left;
        dragStartWindowTop = Top;

        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
        var wa = screen.WorkingArea;
        dragWorkArea = new PixelRect(wa.Left, wa.Top, wa.Right, wa.Bottom);
        var dpi = VisualTreeHelper.GetDpi(this);
        dragDpiScale = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;

        dragSurface = sender as FrameworkElement;
        dragSurface?.CaptureMouse();
        e.Handled = true;
    }

    private void DragSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isDragging || e.LeftButton != MouseButtonState.Pressed) return;

        var pt = WindowPlacement.GetCursorPixelPoint();
        double dxPx = pt.X - dragStartScreen.X;
        double dyPx = pt.Y - dragStartScreen.Y;

        if (!dragMoved && (Math.Abs(dxPx) > 4 || Math.Abs(dyPx) > 4))
        {
            dragMoved = true;
        }

        if (!dragMoved || config.Settings.LockDesktopContainers) return;

        double newLeft = dragStartWindowLeft + (dxPx / dragDpiScale);
        double newTop = dragStartWindowTop + (dyPx / dragDpiScale);

        double workLeftDip = dragWorkArea.Left / dragDpiScale;
        double workTopDip = dragWorkArea.Top / dragDpiScale;
        double workRightDip = dragWorkArea.Right / dragDpiScale;
        double workBottomDip = dragWorkArea.Bottom / dragDpiScale;

        double minLeft = workLeftDip + 8;
        double maxLeft = Math.Max(minLeft, workRightDip - ActualWidth - 8);
        double minTop = workTopDip + 8;
        double maxTop = Math.Max(minTop, workBottomDip - ActualHeight - 8);

        Left = Math.Clamp(newLeft, minLeft, maxLeft);
        Top = Math.Clamp(newTop, minTop, maxTop);

        if (isExpanded)
        {
            ExpandedDipBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
            onExpansionStateChanged?.Invoke();
        }
    }

    private void DragSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDragging) return;
        isDragging = false;
        dragSurface?.ReleaseMouseCapture();
        dragSurface = null;

        if (dragMoved && !config.Settings.LockDesktopContainers)
        {
            var (workLeftDip, workTopDip, _, _, maxCols, maxRows) = GetGridDimensions(dragWorkArea, dragDpiScale);
            int col = Math.Clamp((int)Math.Round((Left - workLeftDip - GridMarginDip) / GridStepDip), 0, maxCols - 1);
            int row = Math.Clamp((int)Math.Round((Top - workTopDip - GridMarginDip) / GridStepDip), 0, maxRows - 1);
            assignedCell = (col, row);

            var (normX, normY) = GridCellToNorm(col, row, dragWorkArea, dragDpiScale);
            tile.Folder.DesktopX = normX;
            tile.Folder.DesktopY = normY;
            ApplyPlacement();
            try { persist(); } catch { }
        }
        else if (!dragMoved)
        {
            if (!isExpanded)
            {
                ExpandBox();
            }
            else
            {
                CollapseBox();
            }
        }
        e.Handled = true;
    }

    // --- App Tile Launching & Micro-Animations ---
    private void AppTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AppTile appTile } btn)
        {
            if (config.Settings.EnableAnimations && config.Settings.EnableLaunchBounce)
            {
                MicroAnimations.PlayLaunchBounce(btn);
            }
            bool elevated = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift);
            bool keepOpen = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            model.LaunchApp(appTile.Entry, new LaunchOptions(keepOpen, elevated));
            if (!keepOpen && isExpanded)
            {
                CollapseBox();
            }
        }
    }

    private void AppTile_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is Button btn && config.Settings.EnableAnimations && config.Settings.EnableCardTiltOnHover)
        {
            MicroAnimations.ApplyCardTilt(btn, e.GetPosition(btn));
        }
    }

    private void AppTile_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button btn)
        {
            MicroAnimations.ResetCardTilt(btn);
        }
    }

    private void AppMenuLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: AppTile appTile })
        {
            model.LaunchApp(appTile.Entry, new LaunchOptions());
            if (isExpanded)
            {
                CollapseBox();
            }
        }
    }

    private void AppMenuLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: AppTile appTile })
        {
            var target = appTile.Entry.Target;
            if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
            {
                try { Process.Start("explorer.exe", $"/select,\"{target}\""); } catch { }
            }
        }
    }

    // --- Drag-and-Drop onto Individual Desktop Container ---
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (ShellDropReader.HasCandidates(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!ShellDropReader.HasCandidates(e.Data)) return;
        var items = ShellDropReader.Read(e.Data);
        bool isSeparate = config.Settings.DesktopContentSource == DesktopContentSource.SeparateDesktopContainers;
        model.ImportIntoFolder(tile.Folder, items, isDesktopSeparate: isSeparate);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    // --- 3-D Mesh Warp Animation ---
    private void PlayBoxWarpTransition(bool isOpening, Action? onComplete = null)
    {
        StopWarpAnimation();
        if (!config.Settings.EnableAnimations)
        {
            onComplete?.Invoke();
            return;
        }

        UpdateLayout();
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (w < 20 || h < 20)
        {
            onComplete?.Invoke();
            return;
        }

        warpRenderer ??= new WarpMeshRenderer(BoxWarpViewport, BoxWarpCamera, BoxWarpGeometryModel, BoxWarpMesh);
        if (!warpRenderer.CaptureAndBindMaterial(RootContent, w, h))
        {
            onComplete?.Invoke();
            return;
        }

        var configuredStyle = config.Settings.AnimationStyle;
        var style = configuredStyle == ContainerAnimationStyle.Random
            ? ContainerWarpMesh.GetRandomConcreteStyle(lastRandomStyle)
            : configuredStyle;
        if (configuredStyle == ContainerAnimationStyle.Random) lastRandomStyle = style;

        int durationMs = ContainerWarpMesh.GetDurationMs(style, isOpening, config.Settings.AnimationDurationMs);
        double initialCollapse = ContainerWarpMesh.ComputeCollapseProgress(style, 0.0, isOpening);
        warpRenderer.UpdatePositions(style, w, h, currentAnchorU, currentAnchorV, currentLateralEdge, initialCollapse);

        BoxWarpViewport.Visibility = Visibility.Visible;
        RootContent.Opacity = 0.0;

        long startTimestamp = Stopwatch.GetTimestamp();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            double t = Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);
            double collapse = ContainerWarpMesh.ComputeCollapseProgress(style, t, isOpening);
            warpRenderer.UpdatePositions(style, w, h, currentAnchorU, currentAnchorV, currentLateralEdge, collapse);

            if (t >= 1.0)
            {
                StopWarpAnimation();
                onComplete?.Invoke();
            }
        };

        warpRenderHandler = handler;
        CompositionTarget.Rendering += handler;
    }

    private void StopWarpAnimation()
    {
        isCollapsing = false;
        if (warpRenderHandler is not null)
        {
            CompositionTarget.Rendering -= warpRenderHandler;
            warpRenderHandler = null;
        }
        BoxWarpViewport.Visibility = Visibility.Collapsed;
        RootContent.Opacity = IsMouseOver
            ? 1.0
            : Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
    }

    // --- Context Menu ---
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

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        isContextMenuOpen = false;
        if (!IsMouseOver && !isDragging)
        {
            RootContent.Opacity = Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
            if (isExpanded)
            {
                hoverCollapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(config.Settings.HoverClosingDelayMs, 100, 2000));
                hoverCollapseTimer.Start();
            }
        }
    }

    private void SwitchStyle_Click(object sender, RoutedEventArgs e) => switchStyle();
    private void ToggleDesktopIcons_Click(object sender, RoutedEventArgs e) => toggleDesktopIcons();
    private void ToggleLockPosition_Click(object sender, RoutedEventArgs e)
    {
        config.Settings.LockDesktopContainers = !config.Settings.LockDesktopContainers;
        try { persist(); } catch { }
        ApplyVisualState();
    }
    private void ResetPositions_Click(object sender, RoutedEventArgs e) => resetPositions();
    private void DesktopSettings_Click(object sender, RoutedEventArgs e) => openSettings("Desktop");
    private void DisableDesktopContainers_Click(object sender, RoutedEventArgs e) => disableDesktopContainers();

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    public void Cleanup()
    {
        hoverExpandTimer.Stop();
        hoverCollapseTimer.Stop();
        StopWarpAnimation();
    }
}
