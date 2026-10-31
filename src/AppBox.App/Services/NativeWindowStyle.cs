using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace AppBox.App.Services;

public sealed record WindowStyleResult(bool RoundedCornersApplied, bool SystemBackdropApplied, string Detail);
public static class NativeWindowStyle
{
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    public static bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
    public static WindowStyleResult Apply(Window window, bool enabled, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var corner = enabled ? 2 : 1; var theme = dark ? 1 : 0;
        var cornerResult = DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        DwmSetWindowAttribute(hwnd, 20, ref theme, sizeof(int));
        var rounded = enabled && cornerResult >= 0 && DwmGetWindowAttribute(hwnd, 33, out var actualCorner, sizeof(int)) >= 0 && actualCorner == 2;
        var backdrop = enabled && Supported && !window.AllowsTransparency ? 2 : 1;
        var accepted = DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)) >= 0;
        var margins = backdrop == 2 ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins();
        var extended = DwmExtendFrameIntoClientArea(hwnd, ref margins) >= 0;
        var applied = backdrop == 2 && accepted && extended && DwmGetWindowAttribute(hwnd, 38, out var actualBackdrop, sizeof(int)) >= 0 && actualBackdrop == 2;
        if (applied)
        {
            window.Background = Brushes.Transparent;
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        }
        else
        {
            backdrop = 1; DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            margins = new(); DwmExtendFrameIntoClientArea(hwnd, ref margins);
            window.SetResourceReference(Window.BackgroundProperty, "Surface");
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target && window.Background is SolidColorBrush brush) target.BackgroundColor = brush.Color;
        }
        return new(rounded, applied, applied
            ? (rounded ? "Rounded corners and Windows system backdrop applied." : "Windows system backdrop applied.") + " Windows may use a solid fallback for Mica."
            : enabled ? "System backdrop is unavailable; using the theme surface with supported corner styling." : "Windows 11 style is off.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static bool IsTransparencyEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("EnableTransparency");
            return val is int i && i != 0;
        }
        catch
        {
            return true;
        }
    }

    public static bool IsBatterySaverActive()
    {
        try
        {
            var status = System.Windows.Forms.SystemInformation.PowerStatus;
            return status.BatteryLifePercent < 0.20 && status.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryApplyAcrylic(Window window, Color tintColor, out string reason)
    {
        if (!Supported || !ThemeService.Windows11StyleEnabled)
        {
            reason = "Windows 11 style is disabled.";
            DisableAcrylic(window);
            return false;
        }

        if (!IsTransparencyEnabled())
        {
            reason = "Windows transparency effects are turned off.";
            DisableAcrylic(window);
            return false;
        }

        if (IsBatterySaverActive())
        {
            reason = "Battery saver is active.";
            DisableAcrylic(window);
            return false;
        }

        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = (tintColor.A << 24) | (tintColor.B << 16) | (tintColor.G << 8) | tintColor.R
            };
            int size = Marshal.SizeOf(accent);
            var pData = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pData, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = 19,
                    Data = pData,
                    SizeOfData = size
                };
                int result = SetWindowCompositionAttribute(hwnd, ref data);
                if (result != 0)
                {
                    reason = "Acrylic blur applied successfully.";
                    return true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pData);
            }
        }
        catch (Exception ex)
        {
            reason = "Acrylic composition failed: " + ex.Message;
            DisableAcrylic(window);
            return false;
        }

        reason = "System rejected acrylic composition.";
        DisableAcrylic(window);
        return false;
    }

    public static void DisableAcrylic(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var accent = new AccentPolicy { AccentState = AccentState.ACCENT_DISABLED };
            int size = Marshal.SizeOf(accent);
            var pData = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pData, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = 19,
                    Data = pData,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(pData);
            }
        }
        catch
        {
        }
    }

}
