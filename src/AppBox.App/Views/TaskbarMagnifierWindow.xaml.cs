using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AppBox.App.Services;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.Views;

public sealed partial class TaskbarMagnifierWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly MagnifierCanvas canvas;
    private readonly DispatcherTimer animTimer;

    private Configuration? config;
    private ContainerIcons? containerIcons;
    private IReadOnlyList<TaskbarButtonInfo> currentButtons = [];
    private PixelPoint currentCursorPhysical;
    private PixelRect currentTaskbarBounds;
    private double currentDpiScale = 1.0;

    private double globalAlpha = 0.0;
    private double targetAlpha = 0.0;
    private double targetScale = 1.55;
    private bool showLabel = true;
    private TaskbarMagnificationScope scope = TaskbarMagnificationScope.AllTaskbarIcons;

    public TaskbarMagnifierWindow()
    {
        InitializeComponent();

        canvas = new MagnifierCanvas(this);
        Content = canvas;

        animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };
        animTimer.Tick += OnAnimTick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
    }

    public void InitializeDependencies(Configuration cfg, ContainerIcons icons)
    {
        config = cfg;
        containerIcons = icons;
        SyncPreferences();
    }

    public void SyncPreferences()
    {
        if (config is null) return;
        targetScale = Math.Clamp(config.Settings.TaskbarMagnificationScale, 1.25, 1.85);
        showLabel = config.Settings.ShowTaskbarMagnificationLabel;
        scope = config.Settings.TaskbarMagnificationScope;
    }

    public void UpdateState(
        IReadOnlyList<TaskbarButtonInfo> buttons,
        PixelPoint cursorPhysical,
        PixelRect taskbarBounds,
        double dpiScale,
        bool isHovering)
    {
        currentButtons = buttons;
        currentCursorPhysical = cursorPhysical;
        currentTaskbarBounds = taskbarBounds;
        currentDpiScale = dpiScale > 0 ? dpiScale : 1.0;

        targetAlpha = isHovering && buttons.Count > 0 ? 1.0 : 0.0;

        if (targetAlpha > 0.0 && !animTimer.IsEnabled)
        {
            animTimer.Start();
        }

        canvas.InvalidateVisual();
    }

    public void ForceDismiss()
    {
        targetAlpha = 0.0;
        globalAlpha = 0.0;
        animTimer.Stop();
        if (IsVisible)
        {
            Hide();
        }
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (Math.Abs(globalAlpha - targetAlpha) < 0.02)
        {
            globalAlpha = targetAlpha;
            if (globalAlpha <= 0.001)
            {
                animTimer.Stop();
                if (IsVisible)
                {
                    Hide();
                }
                return;
            }
        }
        else
        {
            // Smooth spring lerp
            globalAlpha += (targetAlpha - globalAlpha) * 0.28;
        }

        canvas.InvalidateVisual();
    }

    private sealed class MagnifierCanvas : FrameworkElement
    {
        private readonly TaskbarMagnifierWindow window;

        private static readonly Brush MaskBrush = new SolidColorBrush(Color.FromArgb(235, 24, 27, 34));
        private static readonly Brush CardFillBrush = new SolidColorBrush(Color.FromArgb(220, 28, 33, 44));
        private static readonly Pen CardBorderPen = new(new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)), 1.0);
        private static readonly Brush PillFillBrush = new SolidColorBrush(Color.FromArgb(230, 20, 24, 32));
        private static readonly Pen PillBorderPen = new(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1.0);
        private static readonly Typeface PillTypeface = new(
            new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);

        static MagnifierCanvas()
        {
            MaskBrush.Freeze();
            CardFillBrush.Freeze();
            CardBorderPen.Freeze();
            PillFillBrush.Freeze();
            PillBorderPen.Freeze();
        }

        public MagnifierCanvas(TaskbarMagnifierWindow window)
        {
            this.window = window;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double alpha = window.globalAlpha;
            if (alpha <= 0.002 || window.currentButtons.Count == 0 || window.config is null || window.containerIcons is null)
                return;

            double dpi = window.currentDpiScale;
            double curDipX = window.currentCursorPhysical.X / dpi;
            double curDipY = window.currentCursorPhysical.Y / dpi;

            double windowOriginLeft = window.Left;
            double windowOriginTop = window.Top;

            double maxExtraScale = (window.targetScale - 1.0);
            const double influenceRadiusDip = 110.0;

            TaskbarButtonInfo? closestButton = null;
            double closestDistance = double.MaxValue;
            double closestScale = 1.0;
            double closestCenterDipX = 0;
            double closestTopDipY = 0;

            // Render buttons
            foreach (var btn in window.currentButtons)
            {
                double btnDipLeft = (btn.Bounds.Left / dpi) - windowOriginLeft;
                double btnDipTop = (btn.Bounds.Top / dpi) - windowOriginTop;
                double btnDipWidth = btn.Bounds.Width / dpi;
                double btnDipHeight = btn.Bounds.Height / dpi;

                if (btnDipWidth <= 0 || btnDipHeight <= 0) continue;

                double btnCenterDipX = btnDipLeft + (btnDipWidth / 2.0);
                double btnCenterDipY = btnDipTop + (btnDipHeight / 2.0);

                double dist = Math.Abs((window.currentCursorPhysical.X / dpi) - (btn.Bounds.Left / dpi + btnDipWidth / 2.0));

                bool canMagnify = window.scope == TaskbarMagnificationScope.AllTaskbarIcons || btn.IsAppDock;
                double bellScale = canMagnify
                    ? ContainerWarpMesh.ComputeDockMagnificationScale(dist, influenceRadiusDip, maxExtraScale)
                    : 1.0;

                double effectiveScale = 1.0 + (bellScale - 1.0) * alpha;

                if (effectiveScale <= 1.005)
                {
                    continue;
                }

                // 1. Underlay mask on base slot to hide static native icon
                dc.DrawRoundedRectangle(
                    MaskBrush,
                    null,
                    new Rect(btnDipLeft + 2, btnDipTop + 2, Math.Max(0, btnDipWidth - 4), Math.Max(0, btnDipHeight - 4)),
                    5.0,
                    5.0);

                // 2. Compute elevation
                double lift = (effectiveScale - 1.0) * 26.0;
                double elevatedCenterY = btnCenterDipY - lift;

                // 3. Squircle glass card for hovered item
                if (effectiveScale >= 1.15)
                {
                    double cardSize = Math.Max(38.0, Math.Min(btnDipWidth, btnDipHeight)) * effectiveScale;
                    double cardHalf = cardSize / 2.0;
                    var cardRect = new Rect(btnCenterDipX - cardHalf, elevatedCenterY - cardHalf, cardSize, cardSize);

                    dc.DrawRoundedRectangle(CardFillBrush, CardBorderPen, cardRect, 14.0, 14.0);

                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        closestButton = btn;
                        closestScale = effectiveScale;
                        closestCenterDipX = btnCenterDipX;
                        closestTopDipY = elevatedCenterY - cardHalf;
                    }
                }

                // 4. Draw magnified high-res icon
                var icon = TaskbarIconResolver.ResolveIcon(btn, window.config, window.containerIcons);
                if (icon is not null)
                {
                    double baseIconSize = Math.Min(26.0, Math.Min(btnDipWidth, btnDipHeight) - 8.0);
                    if (baseIconSize < 16.0) baseIconSize = 24.0;
                    double scaledIconSize = baseIconSize * effectiveScale;
                    double iconHalf = scaledIconSize / 2.0;

                    var iconRect = new Rect(btnCenterDipX - iconHalf, elevatedCenterY - iconHalf, scaledIconSize, scaledIconSize);
                    dc.DrawImage(icon, iconRect);
                }
            }

            // 5. Floating Title Pill
            if (window.showLabel && closestButton is not null && closestScale >= 1.25 && !string.IsNullOrWhiteSpace(closestButton.CleanName))
            {
                var text = new FormattedText(
                    closestButton.CleanName,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    PillTypeface,
                    12.0,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                double pillWidth = text.Width + 24.0;
                double pillHeight = 22.0;
                double pillLeft = closestCenterDipX - (pillWidth / 2.0);
                double pillTop = closestTopDipY - pillHeight - 6.0;

                var pillRect = new Rect(pillLeft, pillTop, pillWidth, pillHeight);
                dc.DrawRoundedRectangle(PillFillBrush, PillBorderPen, pillRect, 11.0, 11.0);

                var textPos = new Point(pillLeft + 12.0, pillTop + (pillHeight - text.Height) / 2.0);
                dc.DrawText(text, textPos);
            }
        }
    }
}
