using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AppBox.Core;

namespace AppBox.App.Services;

public static class WindowPlacement
{
    public sealed record Anchor(PixelPoint Cursor, ScreenEdge? LateralEdge = null, PopupBounds? SidebarBounds = null);
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
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder className, int maxCount);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newLong);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
    }

    public static void SetTaskbarVisibility(Window window, bool showInTaskbar)
    {
        if (window.ShowInTaskbar != showInTaskbar)
            window.ShowInTaskbar = showInTaskbar;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        var nextStyle = showInTaskbar
            ? (exStyle & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW
            : (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        if (nextStyle != exStyle)
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(nextStyle));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        try
        {
            var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
            if (Type.GetTypeFromCLSID(clsid) is { } type && Activator.CreateInstance(type) is ITaskbarList taskbar)
            {
                try
                {
                    if (taskbar.HrInit() == 0)
                    {
                        if (showInTaskbar && window.IsVisible)
                            taskbar.AddTab(hwnd);
                        else if (!showInTaskbar)
                            taskbar.DeleteTab(hwnd);
                    }
                }
                finally { Marshal.FinalReleaseComObject(taskbar); }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    public static void Round(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var corners = ThemeService.Windows11StyleEnabled ? 2 : 1; DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
        var theme = dark ? 1 : 0; DwmSetWindowAttribute(hwnd, 20, ref theme, sizeof(int));
    }

    public static void ApplyLauncherStyle(Window window, bool showBorder, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var theme = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref theme, sizeof(int));
        if (showBorder && ThemeService.Windows11StyleEnabled)
        {
            var corners = 2;
            DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
            var borderDefault = unchecked((int)0xFFFFFFFF);
            DwmSetWindowAttribute(hwnd, 34, ref borderDefault, sizeof(int));
        }
        else
        {
            var corners = 1;
            DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
            var borderNone = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 34, ref borderNone, sizeof(int));
        }
    }

    public static void ApplyToolWindowStyle(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        ApplyLauncherStyle(window, showBorder: false, dark);
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

    public static PixelPoint GetCursorPixelPoint()
    {
        GetCursorPos(out var cursor);
        return new(cursor.X, cursor.Y);
    }

    public static Anchor CaptureAnchor()
    {
        GetCursorPos(out var cursor); return new(new(cursor.X, cursor.Y));
    }

    public static (PopupBounds Bounds, PixelRect WorkArea, double DpiScale) PlaceEdgeSidebar(
        Window window,
        ScreenEdge edge,
        double verticalPosition,
        double widthDip,
        double heightDip,
        bool isExpanded,
        SidebarMonitorMode monitorMode = SidebarMonitorMode.PrimaryMonitor,
        int monitorIndex = 0)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = ResolveSidebarMonitor(monitorMode, monitorIndex);
        if (!GetMonitorInfo(monitor, ref info)) return default;
        if (MonitorFromWindow(hwnd, 2) != monitor)
            SetWindowPos(hwnd, IntPtr.Zero, info.Work.Left + 12, info.Work.Top + 12, 0, 0, 0x15);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var work = new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        var bounds = isExpanded
            ? PopupPlacement.CalculateSidebarStrip(work, edge, verticalPosition, scale, widthDip, heightDip)
            : PopupPlacement.CalculateEdgeHandle(work, edge, verticalPosition, scale, widthDip, heightDip);
        SetWindowPos(hwnd, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 /* SWP_NOACTIVATE */);
        return (bounds, work, scale);
    }

    public static IntPtr ResolveSidebarMonitor(SidebarMonitorMode monitorMode, int monitorIndex)
    {
        if (monitorMode == SidebarMonitorMode.CursorMonitor)
        {
            GetCursorPos(out var cursor);
            return MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */);
        }
        if (monitorMode == SidebarMonitorMode.SpecificMonitor)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length > 0)
            {
                var screen = screens[Math.Clamp(monitorIndex, 0, screens.Length - 1)];
                var pt = new Point
                {
                    X = screen.Bounds.Left + screen.Bounds.Width / 2,
                    Y = screen.Bounds.Top + screen.Bounds.Height / 2
                };
                return MonitorFromPoint(pt, 2);
            }
        }
        return MonitorFromPoint(new Point { X = 0, Y = 0 }, 1 /* MONITOR_DEFAULTTOPRIMARY */);
    }

    public static bool IsFullscreenAppActiveOnMonitor(PixelRect workArea)
    {
        try
        {
            if (SHQueryUserNotificationState(out int state) == 0 && (state == 3 /* QUNS_RUNNING_D3D_FULL_SCREEN */ || state == 4 /* QUNS_PRESENTATION_MODE */))
                return true;

            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || !IsWindowVisible(fg) || IsIconic(fg)) return false;

            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;

            var classBuilder = new System.Text.StringBuilder(64);
            if (GetClassNameW(fg, classBuilder, classBuilder.Capacity) > 0)
            {
                var cls = classBuilder.ToString();
                if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                    return false;
            }

            if (!GetWindowRect(fg, out var rect)) return false;

            var fgMonitor = MonitorFromWindow(fg, 2);
            var targetMonitor = MonitorFromPoint(new Point
            {
                X = (workArea.Left + workArea.Right) / 2,
                Y = (workArea.Top + workArea.Bottom) / 2
            }, 2);
            if (fgMonitor != targetMonitor) return false;

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(fgMonitor, ref info)) return false;

            return rect.Left <= info.Monitor.Left &&
                   rect.Top <= info.Monitor.Top &&
                   rect.Right >= info.Monitor.Right &&
                   rect.Bottom >= info.Monitor.Bottom;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
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
        var work = new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        var monitorRect = new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);

        if (anchor.LateralEdge is { } lateralEdge && anchor.SidebarBounds is { } sidebarBounds)
        {
            var lateralBounds = PopupPlacement.CalculateLateralFlyout(work, lateralEdge, sidebarBounds, cursor.Y, scale, window.Width, window.Height);
            SetWindowPos(hwnd, IntPtr.Zero, lateralBounds.X, lateralBounds.Y, lateralBounds.Width, lateralBounds.Height, 0x14);
            var contentWidth = lateralBounds.Width / scale - 2 * PopupPointer.ShadowMargin;
            return new PointerPlacement(false, contentWidth / 2, false);
        }

        var bounds = PopupPlacement.Calculate(monitorRect, work, new(cursor.X, cursor.Y), scale, window.Width, window.Height);
        SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x14);
        return PopupPointer.Calculate(monitorRect, work, anchor.Cursor, bounds, scale, showPointer);
    }
}
