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
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Round(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var corners = 2; DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
        var theme = dark ? 1 : 0; DwmSetWindowAttribute(hwnd, 20, ref theme, sizeof(int));
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
