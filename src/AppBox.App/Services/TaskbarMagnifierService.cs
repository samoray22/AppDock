using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using AppBox.App.Views;
using AppBox.Core;
using AppBox.Infrastructure;
using Forms = System.Windows.Forms;

namespace AppBox.App.Services;

public sealed class TaskbarMagnifierService
{
    private readonly Configuration config;
    private readonly string iconDirectory;
    private readonly ContainerLaunchers launchers;
    private readonly Func<bool> isSettingsOpen;
    private readonly Func<bool> isLauncherOpen;

    private readonly DispatcherTimer pollTimer;
    private readonly AutoResetEvent scanSignal = new(false);
    private readonly object scanLock = new();
    private Thread? scanThread;
    private volatile bool disposed;

    private TaskbarMagnifierWindow? window;
    private ContainerIcons? containerIcons;

    private IReadOnlyList<TaskbarButtonInfo> cachedButtons = [];
    private IntPtr activeTaskbarHwnd = IntPtr.Zero;
    private PixelRect activeTaskbarBounds;
    private DateTime lastScanUtc = DateTime.MinValue;
    private bool scanInFlight;

    private Dictionary<string, Guid?> cachedPinnedTargets = new(StringComparer.OrdinalIgnoreCase);
    private DateTime lastPinnedScanUtc = DateTime.MinValue;

    private sealed record ScanRequest(IntPtr TaskbarHwnd, IReadOnlyDictionary<string, Guid?> PinnedTargets);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Win32Point pt);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hWnd, out Win32Rect lpRect);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    public TaskbarMagnifierService(
        Configuration config,
        string iconDirectory,
        ContainerLaunchers launchers,
        Func<bool> isSettingsOpen,
        Func<bool> isLauncherOpen)
    {
        this.config = config;
        this.iconDirectory = iconDirectory;
        this.launchers = launchers;
        this.isSettingsOpen = isSettingsOpen;
        this.isLauncherOpen = isLauncherOpen;

        pollTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(20) // 50Hz tracking
        };
        pollTimer.Tick += OnPollTick;
    }

    private void EnsureWindow()
    {
        if (window is not null || disposed) return;

        containerIcons ??= new ContainerIcons(iconDirectory);
        window = new TaskbarMagnifierWindow();
        window.InitializeDependencies(config, containerIcons);
    }

    private void EnsureScanThread()
    {
        if (scanThread is not null || disposed) return;

        scanThread = new Thread(ScanWorkerLoop)
        {
            Name = "AppDock.TaskbarMagnifierScanner",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        scanThread.SetApartmentState(ApartmentState.STA);
        scanThread.Start();
    }

    public void SyncWithConfig()
    {
        RefreshPinnedTargets(force: true);

        bool enabled = config.Settings.EnableTaskbarMagnification;
        if (!enabled)
        {
            pollTimer.Stop();
            window?.ForceDismiss();
            cachedButtons = [];
            activeTaskbarHwnd = IntPtr.Zero;
            return;
        }

        EnsureWindow();
        window?.SyncPreferences();
        EnsureScanThread();

        if (!pollTimer.IsEnabled)
        {
            pollTimer.Start();
        }
    }

    public void NotifyLauncherOpened()
    {
        window?.ForceDismiss();
    }

    public void NotifyLauncherClosed()
    {
        if (config.Settings.EnableTaskbarMagnification && !pollTimer.IsEnabled)
        {
            pollTimer.Start();
        }
    }

    public void Cleanup()
    {
        disposed = true;
        pollTimer.Stop();
        try { scanSignal.Set(); } catch { }
        if (window is not null)
        {
            try
            {
                window.ForceDismiss();
                window.Close();
            }
            catch { }
            window = null;
        }
        cachedButtons = [];
    }

    private void RefreshPinnedTargets(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - lastPinnedScanUtc).TotalSeconds < 4)
            return;
        lastPinnedScanUtc = now;
        cachedPinnedTargets = launchers.GetPinnedTaskbarTargets(config);
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (!config.Settings.EnableTaskbarMagnification)
        {
            pollTimer.Stop();
            window?.ForceDismiss();
            return;
        }

        if (isSettingsOpen() || isLauncherOpen())
        {
            window?.ForceDismiss();
            return;
        }

        if (Forms.Control.MouseButtons != Forms.MouseButtons.None)
        {
            window?.ForceDismiss();
            return;
        }

        var cursor = WindowPlacement.GetCursorPixelPoint();
        bool isNear = IsCursorNearTaskbar(cursor, out var hwnd, out var taskbarBounds);

        if (isNear && WindowPlacement.IsFullscreenAppActiveOnMonitor(taskbarBounds))
        {
            window?.ForceDismiss();
            return;
        }

        EnsureWindow();
        if (window is null) return;

        double dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
        if (dpi <= 0) dpi = 1.0;

        if (isNear)
        {
            if (activeTaskbarHwnd != hwnd)
            {
                activeTaskbarHwnd = hwnd;
                activeTaskbarBounds = taskbarBounds;
                cachedButtons = [];
                lastScanUtc = DateTime.MinValue;
            }

            // Position overlay window over the taskbar with 80 DIPs of headroom for elevated cards and pills
            double winLeft = taskbarBounds.Left / dpi;
            double winTop = (taskbarBounds.Top / dpi) - 80.0;
            double winWidth = taskbarBounds.Width / dpi;
            double winHeight = (taskbarBounds.Height / dpi) + 80.0;

            if (Math.Abs(window.Left - winLeft) > 1 || Math.Abs(window.Top - winTop) > 1 ||
                Math.Abs(window.Width - winWidth) > 1 || Math.Abs(window.Height - winHeight) > 1)
            {
                window.Left = winLeft;
                window.Top = winTop;
                window.Width = winWidth;
                window.Height = winHeight;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            var now = DateTime.UtcNow;
            if ((cachedButtons.Count == 0 || (now - lastScanUtc).TotalSeconds > 2.0) && !scanInFlight)
            {
                RefreshPinnedTargets(force: false);
                TriggerScan(hwnd, cachedPinnedTargets);
            }

            window.UpdateState(cachedButtons, cursor, taskbarBounds, dpi, isHovering: true);
        }
        else
        {
            if (window.IsVisible)
            {
                window.UpdateState(cachedButtons, cursor, activeTaskbarBounds, dpi, isHovering: false);
            }
        }
    }

    private void TriggerScan(IntPtr hwnd, IReadOnlyDictionary<string, Guid?> pinnedTargets)
    {
        scanInFlight = true;
        EnsureScanThread();
        lock (scanLock)
        {
            pendingScan = new ScanRequest(hwnd, pinnedTargets);
        }
        scanSignal.Set();
    }

    private ScanRequest? pendingScan;

    private void ScanWorkerLoop()
    {
        while (!disposed)
        {
            scanSignal.WaitOne(500);
            if (disposed) break;

            ScanRequest? req;
            lock (scanLock)
            {
                req = pendingScan;
                pendingScan = null;
            }

            if (req is not null)
            {
                var buttons = ExecuteScan(req.TaskbarHwnd, req.PinnedTargets);
                if (!disposed && window is not null)
                {
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        scanInFlight = false;
                        lastScanUtc = DateTime.UtcNow;
                        if (buttons.Count > 0)
                        {
                            cachedButtons = buttons;
                        }
                    });
                }
            }
        }
    }

    private IReadOnlyList<TaskbarButtonInfo> ExecuteScan(IntPtr hwnd, IReadOnlyDictionary<string, Guid?> pinnedTargets)
    {
        var result = new List<TaskbarButtonInfo>();
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null) return result;

            var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            var buttons = root.FindAll(TreeScope.Descendants, buttonCondition);

            for (int i = 0; i < buttons.Count; i++)
            {
                try
                {
                    var btn = buttons[i];
                    var rect = btn.Current.BoundingRectangle;
                    if (rect.IsEmpty || rect.Width < 14 || rect.Height < 14) continue;

                    var pxRect = new PixelRect(
                        (int)Math.Round(rect.Left),
                        (int)Math.Round(rect.Top),
                        (int)Math.Round(rect.Right),
                        (int)Math.Round(rect.Bottom));

                    string autoId = btn.Current.AutomationId ?? "";
                    string name = btn.Current.Name ?? "";

                    // Filter out non-app system buttons
                    if (IsSystemTaskbarButton(autoId, name))
                        continue;

                    bool isAppDock = CheckIsAppDock(autoId, name, pinnedTargets, out var containerId);
                    string cleanName = isAppDock && containerId is null && name.StartsWith("AppDock", StringComparison.OrdinalIgnoreCase)
                        ? "AppDock"
                        : TaskbarIconResolver.ExtractCleanName(name);

                    if (string.IsNullOrWhiteSpace(cleanName))
                        cleanName = name;

                    result.Add(new TaskbarButtonInfo(
                        pxRect,
                        name,
                        cleanName,
                        autoId,
                        isAppDock,
                        containerId,
                        null));
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        // Sort buttons by physical Left position across taskbar
        result.Sort((a, b) => a.Bounds.Left.CompareTo(b.Bounds.Left));
        return result;
    }

    private static bool IsSystemTaskbarButton(string autoId, string name)
    {
        if (autoId.Contains("Start", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Start", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Démarrer", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Inicio", StringComparison.OrdinalIgnoreCase))
            return true;

        if (autoId.Contains("Search", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Search", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rechercher", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Buscar", StringComparison.OrdinalIgnoreCase))
            return true;

        if (autoId.Contains("TaskView", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Task View", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Affichage des tâches", StringComparison.OrdinalIgnoreCase))
            return true;

        if (autoId.Contains("Widgets", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Widgets", StringComparison.OrdinalIgnoreCase))
            return true;

        if (autoId.Contains("Notification", StringComparison.OrdinalIgnoreCase) ||
            autoId.Contains("SystemTray", StringComparison.OrdinalIgnoreCase) ||
            autoId.Contains("Clock", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("System Clock", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private bool CheckIsAppDock(
        string autoId,
        string name,
        IReadOnlyDictionary<string, Guid?> pinnedTargets,
        out Guid? containerId)
    {
        containerId = null;

        const string containerPrefix = "AppDock.Container.";
        int idx = autoId.IndexOf(containerPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rest = autoId[(idx + containerPrefix.Length)..].Trim();
            var token = new string(rest.TakeWhile(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
            if (Guid.TryParse(token, out var id))
            {
                containerId = id;
                return true;
            }
        }

        if (autoId.Contains("AppBox.Desktop", StringComparison.OrdinalIgnoreCase) ||
            autoId.Contains("AppDock.Main", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pinnedTargets.TryGetValue(autoId, out var matchId))
        {
            containerId = matchId;
            return true;
        }

        string stripped = autoId.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase) ? autoId[6..].Trim() : autoId;
        if (stripped.Length > 0 && pinnedTargets.TryGetValue(stripped, out matchId))
        {
            containerId = matchId;
            return true;
        }

        if (name.StartsWith("AppDock", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var folder in config.Folders)
        {
            if (name.Equals(folder.Name, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("AppDock - " + folder.Name, StringComparison.OrdinalIgnoreCase))
            {
                containerId = folder.Id;
                return true;
            }
        }

        return false;
    }

    private static bool IsCursorNearTaskbar(PixelPoint cursor, out IntPtr hwnd, out PixelRect taskbarBounds)
    {
        hwnd = IntPtr.Zero;
        taskbarBounds = default;

        // 1. Direct hit-test from cursor point
        var foundHwnd = WindowFromPoint(new Win32Point { X = cursor.X, Y = cursor.Y });
        if (foundHwnd != IntPtr.Zero)
        {
            var root = GetAncestor(foundHwnd, 2 /* GA_ROOT */);
            if (root != IntPtr.Zero && IsTaskbarClass(root))
            {
                if (GetWindowRect(root, out var wr))
                {
                    hwnd = root;
                    taskbarBounds = new PixelRect(wr.Left, wr.Top, wr.Right, wr.Bottom);
                    return true;
                }
            }
        }

        // 2. Check primary taskbar Shell_TrayWnd bounds (including 35px hover extension)
        var primary = FindWindowW("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero && GetWindowRect(primary, out var pr))
        {
            var r = new PixelRect(pr.Left, pr.Top, pr.Right, pr.Bottom);
            if (Contains(r, cursor, margin: 35))
            {
                hwnd = primary;
                taskbarBounds = r;
                return true;
            }
        }

        // 3. Check secondary taskbars
        IntPtr sec = IntPtr.Zero;
        while ((sec = FindWindowExW(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (GetWindowRect(sec, out var sr))
            {
                var r = new PixelRect(sr.Left, sr.Top, sr.Right, sr.Bottom);
                if (Contains(r, cursor, margin: 35))
                {
                    hwnd = sec;
                    taskbarBounds = r;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsTaskbarClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        if (GetClassNameW(hwnd, sb, sb.Capacity) <= 0)
            return false;
        var cls = sb.ToString();
        return cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static bool Contains(PixelRect rect, PixelPoint pt, int margin = 0)
    {
        return pt.X >= rect.Left - margin &&
               pt.X <= rect.Right + margin &&
               pt.Y >= rect.Top - margin &&
               pt.Y <= rect.Bottom + margin;
    }
}
