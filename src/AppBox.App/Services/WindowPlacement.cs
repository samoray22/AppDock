using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AppBox.Core;

namespace AppBox.App.Services;

public static class WindowPlacement
{
    public sealed record Anchor(PixelPoint Cursor);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Round(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var corners = ThemeService.Windows11StyleEnabled ? 2 : 1; DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
        var theme = dark ? 1 : 0; DwmSetWindowAttribute(hwnd, 20, ref theme, sizeof(int));
    }

    public static void FitSettingsToWorkArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle; if (hwnd == IntPtr.Zero) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return;
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var width = (info.Work.Right - info.Work.Left - 16) / scale;
        var height = (info.Work.Bottom - info.Work.Top - 16) / scale;
        window.MinWidth = Math.Min(740, width); window.MinHeight = Math.Min(540, height);
        window.MaxWidth = width; window.MaxHeight = height;
        window.Width = Math.Min(window.Width, width); window.Height = Math.Min(window.Height, height);
        if (GetWindowRect(hwnd, out var rect))
            SetWindowPos(hwnd, IntPtr.Zero, Math.Clamp(rect.Left, info.Work.Left + 8, Math.Max(info.Work.Left + 8, info.Work.Right - (rect.Right - rect.Left) - 8)), Math.Clamp(rect.Top, info.Work.Top + 8, Math.Max(info.Work.Top + 8, info.Work.Bottom - (rect.Bottom - rect.Top) - 8)), 0, 0, 0x15);
    }
    public static Anchor CaptureAnchor()
    {
        GetCursorPos(out var cursor); return new(new(cursor.X, cursor.Y));
    }
    public static PointerPlacement Place(Window window, Anchor? anchor = null, bool showPointer = false)
    {
        anchor ??= CaptureAnchor();
        var cursor = new Point { X = anchor.Cursor.X, Y = anchor.Cursor.Y };
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromPoint(cursor, 2);
        if (!GetMonitorInfo(monitor, ref info)) return default;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        // Move into the target monitor before reading its effective per-window DPI.
        if (MonitorFromWindow(hwnd, 2) != monitor)
            SetWindowPos(hwnd, IntPtr.Zero, info.Work.Left + 12, info.Work.Top + 12, 0, 0, 0x15);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var bounds = PopupPlacement.Calculate(new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom), new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom), new(cursor.X, cursor.Y), scale, window.Width, window.Height);
        SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x14);
        return PopupPointer.Calculate(new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom), new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom), anchor.Cursor, bounds, scale, showPointer);
    }
}
