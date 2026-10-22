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
}
