using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AppBox.App.Services;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.Views;

public partial class DesktopRecycleBinWindow : Window
{
    private readonly Configuration config;
    private readonly Action persist;
    private readonly Action<string> openSettings;
    private readonly Action resetPositions;
    private readonly DispatcherTimer statusTimer;

    private bool isContextMenuOpen;
    private bool isDragging;
    private bool dragMoved;
    private Point dragStartScreen;
    private double dragStartWindowLeft;
    private double dragStartWindowTop;
    private PixelRect dragWorkArea;
    private double dragDpiScale = 1.0;

    public (int Col, int Row) CurrentGridCell { get; private set; }
    public Rect CompactIconDipBounds { get; private set; }

    private bool isOccluded;

    public DesktopRecycleBinWindow(
        Configuration config,
        Action persist,
        Action<string> openSettings,
        Action resetPositions)
    {
        InitializeComponent();
        this.config = config;
        this.persist = persist;
        this.openSettings = openSettings;
        this.resetPositions = resetPositions;

        SourceInitialized += (_, _) => ApplyWindowStyle();

        statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        statusTimer.Tick += (_, _) => RefreshStatus();
        statusTimer.Start();

        RefreshStatus();
    }

    public void ApplyWindowStyle()
    {
        if (new WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        WindowPlacement.ApplyToolWindowStyle(this, ThemeService.Dark);
        NativeWindowStyle.DisableAcrylic(this);
        WindowPlacement.SendToDesktopBottom(this);
    }

    public void ApplyVisualAndPlacement()
    {
        BinBadge.Cursor = config.Settings.LockDesktopContainers ? Cursors.Hand : Cursors.SizeAll;
        if (!IsMouseOver && !isContextMenuOpen && !isDragging)
        {
            RootContent.Opacity = Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
        }

        var (work, scale, hwnd) = WindowPlacement.ResolveDesktopWorkArea(
            this,
            config.Settings.SidebarMonitorMode,
            config.Settings.SidebarMonitorIndex);

        Width = DesktopBoxWindow.IconSizeDip;
        Height = DesktopBoxWindow.IconSizeDip;

        if (work.Right - work.Left >= 32 && work.Bottom - work.Top >= 32)
        {
            bool isDefault = Math.Abs(config.Settings.DesktopRecycleBinX - 0.015) < 1e-4 &&
                             Math.Abs(config.Settings.DesktopRecycleBinY - 0.02) < 1e-4;
            CurrentGridCell = isDefault
                ? (0, 0)
                : DesktopBoxWindow.SnapNormToGridCell(config.Settings.DesktopRecycleBinX, config.Settings.DesktopRecycleBinY, work, scale);

            var (leftDip, topDip) = DesktopBoxWindow.GridCellToDip(CurrentGridCell.Col, CurrentGridCell.Row, work, scale);
            CompactIconDipBounds = new Rect(leftDip, topDip, DesktopBoxWindow.IconSizeDip, DesktopBoxWindow.IconSizeDip);
            int xPx = (int)Math.Round(leftDip * scale);
            int yPx = (int)Math.Round(topDip * scale);
            int wPx = (int)Math.Ceiling(DesktopBoxWindow.IconSizeDip * scale);
            int hPx = (int)Math.Ceiling(DesktopBoxWindow.IconSizeDip * scale);
            WindowPlacement.SetWindowPixelBounds(hwnd, xPx, yPx, wPx, hPx);
        }

        RefreshStatus();
    }

    public void RefreshStatus()
    {
        var (itemCount, _) = DesktopIconsService.GetRecycleBinStatus();
        bool isFull = itemCount > 0;
        RecycleBinImage.Source = ShellIcon.GetRecycleBinIcon(isFull);
        BinBadge.ToolTip = isFull ? $"Recycle Bin ({itemCount} item(s))" : "Recycle Bin (Empty)";
        if (EmptyBinMenuItem is not null)
        {
            EmptyBinMenuItem.Header = "Empty Recycle Bin";
            EmptyBinMenuItem.IsEnabled = isFull;
        }
    }

    public bool IsCursorOverBox()
    {
        if (!IsVisible || isOccluded) return false;
        var pt = WindowPlacement.GetCursorPixelPoint();
        var dpi = VisualTreeHelper.GetDpi(this);
        var scale = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double leftPx = Left * scale;
        double topPx = Top * scale;
        double rightPx = (Left + ActualWidth) * scale;
        double bottomPx = (Top + ActualHeight) * scale;
        return pt.X >= leftPx && pt.X <= rightPx && pt.Y >= topPx && pt.Y <= bottomPx;
    }

    private void AnimateIconScale(double target)
    {
        if (!config.Settings.EnableAnimations)
        {
            IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            IconScale.ScaleX = target;
            IconScale.ScaleY = target;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(130);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, duration) { EasingFunction = ease });
        IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, duration) { EasingFunction = ease });
    }

    public void SetOccludedByExpandedBox(bool occluded)
    {
        if (isOccluded == occluded) return;
        isOccluded = occluded;

        if (occluded)
        {
            AnimateIconScale(1.0);
            RootContent.Visibility = Visibility.Hidden;
            RootContent.IsHitTestVisible = false;
        }
        else
        {
            RootContent.Visibility = Visibility.Visible;
            RootContent.IsHitTestVisible = true;
            if (!IsMouseOver && !isContextMenuOpen && !isDragging)
            {
                RootContent.Opacity = Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
            }
        }
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        if (isOccluded) return;
        RootContent.Opacity = 1.0;
        AnimateIconScale(1.08);
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        AnimateIconScale(1.0);
        if (!isContextMenuOpen && !isDragging)
        {
            RootContent.Opacity = Math.Clamp(config.Settings.DesktopContainerOpacity, 0.30, 1.00);
        }
    }

    private void BinBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        isDragging = true;
        dragMoved = false;

        var pt = WindowPlacement.GetCursorPixelPoint();
        dragStartScreen = new Point(pt.X, pt.Y);
        dragStartWindowLeft = Left;
        dragStartWindowTop = Top;

        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
        var wa = screen.WorkingArea;
        dragWorkArea = new PixelRect(wa.Left, wa.Top, wa.Right, wa.Bottom);
        var dpi = VisualTreeHelper.GetDpi(this);
        dragDpiScale = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;

        BinBadge.CaptureMouse();
        e.Handled = true;
    }

    private void BinBadge_MouseMove(object sender, MouseEventArgs e)
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
    }

    private void BinBadge_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDragging) return;
        isDragging = false;
        BinBadge.ReleaseMouseCapture();

        if (dragMoved && !config.Settings.LockDesktopContainers)
        {
            var (workLeftDip, workTopDip, _, _, maxCols, maxRows) = DesktopBoxWindow.GetGridDimensions(dragWorkArea, dragDpiScale);
            int col = Math.Clamp((int)Math.Round((Left - workLeftDip - DesktopBoxWindow.GridMarginDip) / DesktopBoxWindow.GridStepDip), 0, maxCols - 1);
            int row = Math.Clamp((int)Math.Round((Top - workTopDip - DesktopBoxWindow.GridMarginDip) / DesktopBoxWindow.GridStepDip), 0, maxRows - 1);
            CurrentGridCell = (col, row);

            var (normX, normY) = DesktopBoxWindow.GridCellToNorm(col, row, dragWorkArea, dragDpiScale);
            config.Settings.DesktopRecycleBinX = normX;
            config.Settings.DesktopRecycleBinY = normY;
            ApplyVisualAndPlacement();
            try { persist(); } catch { }
        }
        else if (!dragMoved)
        {
            DesktopIconsService.OpenRecycleBin();
        }
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            DesktopIconsService.MoveFilesToRecycleBin(files);
            RefreshStatus();
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        isContextMenuOpen = true;
        RefreshStatus();
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
        }
    }

    private void OpenBin_Click(object sender, RoutedEventArgs e) => DesktopIconsService.OpenRecycleBin();

    private void EmptyBin_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        DesktopIconsService.EmptyRecycleBin(hwnd);
        RefreshStatus();
    }

    private void ToggleLockPosition_Click(object sender, RoutedEventArgs e)
    {
        config.Settings.LockDesktopContainers = !config.Settings.LockDesktopContainers;
        try { persist(); } catch { }
        ApplyVisualAndPlacement();
    }

    private void ResetPositions_Click(object sender, RoutedEventArgs e) => resetPositions();
    private void DesktopSettings_Click(object sender, RoutedEventArgs e) => openSettings("Desktop");

    public void Cleanup()
    {
        statusTimer.Stop();
    }
}
