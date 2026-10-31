using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using AppBox.App.ViewModels;
using AppBox.App.Views;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.Services;

public sealed class TaskbarHoverWatcher
{
    private readonly Configuration config;
    private readonly LauncherViewModel model;
    private readonly LauncherWindow launcher;
    private readonly ContainerLaunchers launchers;
    private readonly Func<bool> isSettingsOpen;
    private readonly Action<TaskbarHoverTarget, WindowPlacement.Anchor> openFromHover;

    private readonly DispatcherTimer timer;
    private readonly AutoResetEvent probeSignal = new(false);
    private readonly object probeLock = new();
    private Thread? probeThread;
    private volatile bool disposed;
    private ProbeRequest? pendingRequest;

    private Dictionary<string, Guid?> cachedPinnedTargets = new(StringComparer.OrdinalIgnoreCase);
    private DateTime lastPinnedScanUtc = DateTime.MinValue;

    private bool queryInFlight;
    private PixelPoint lastQueriedPoint = new(-99999, -99999);
    private DateTime lastQueryUtc = DateTime.MinValue;
    private PixelRect? currentButtonBounds;
    private TaskbarHoverTarget? currentHoverTarget;
    private DateTime? hoverEnteredUtc;

    private TaskbarHoverTarget? activeOpenedTarget;
    private PixelRect? activeOpenedButtonBounds;
    private DateTime? mouseLeftBothUtc;
    private PixelRect? suppressedButtonBounds;

    private sealed record ProbeRequest(PixelPoint Point, IntPtr TaskbarHwnd, IReadOnlyDictionary<string, Guid?> PinnedTargets);

    public bool OpenedFromTaskbarHover { get; private set; }

    public TaskbarHoverWatcher(
        Configuration config,
        LauncherViewModel model,
        LauncherWindow launcher,
        ContainerLaunchers launchers,
        Func<bool> isSettingsOpen,
        Action<TaskbarHoverTarget, WindowPlacement.Anchor> openFromHover)
    {
        this.config = config;
        this.model = model;
        this.launcher = launcher;
        this.launchers = launchers;
        this.isSettingsOpen = isSettingsOpen;
        this.openFromHover = openFromHover;

        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += OnTimerTick;
    }

    private void EnsureProbeThread()
    {
        if (probeThread is not null || disposed)
            return;

        probeThread = new Thread(ProbeWorkerLoop)
        {
            Name = "AppDock.TaskbarHoverUIA",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        probeThread.SetApartmentState(ApartmentState.STA);
        probeThread.Start();
    }

    private void ProbeWorkerLoop()
    {
        IUIAutomation? nativeUia = null;
        try
        {
            var clsid8 = new Guid("E22AD333-B25F-460C-83D0-0581107395C9");
            var clsid7 = new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E");
            var type = Type.GetTypeFromCLSID(clsid8) ?? Type.GetTypeFromCLSID(clsid7);
            if (type is not null)
                nativeUia = Activator.CreateInstance(type) as IUIAutomation;
        }
        catch
        {
        }

        try
        {
            while (!disposed)
            {
                probeSignal.WaitOne(500);
                if (disposed) break;

                ProbeRequest? req;
                lock (probeLock)
                {
                    req = pendingRequest;
                    pendingRequest = null;
                }

                if (req is not null)
                {
                    ExecuteProbeOnSta(req, nativeUia);
                }
            }
        }
        finally
        {
            if (nativeUia is not null)
            {
                try { Marshal.FinalReleaseComObject(nativeUia); } catch { }
            }
        }
    }

    public void SyncWithConfig()
    {
        RefreshPinnedTargets(force: true);
        bool enabled = config.Settings.EnableTaskbar &&
                       config.Settings.OpeningBehavior is ContainerOpeningBehavior.Hover or ContainerOpeningBehavior.ClickOrHover;
        if (!enabled)
        {
            ResetHoverState();
            OpenedFromTaskbarHover = false;
            activeOpenedTarget = null;
            activeOpenedButtonBounds = null;
            timer.Stop();
            return;
        }

        EnsureProbeThread();
        if (!timer.IsEnabled)
        {
            timer.Start();
        }
    }

    public void NotifyLauncherClosed()
    {
        if (OpenedFromTaskbarHover && activeOpenedButtonBounds is { } bounds)
        {
            var cursor = WindowPlacement.GetCursorPixelPoint();
            if (Contains(bounds, cursor, margin: 6))
            {
                // Prevent immediate re-open if dismissed via Escape while cursor still rests on the button.
                suppressedButtonBounds = bounds;
            }
        }
        OpenedFromTaskbarHover = false;
        activeOpenedTarget = null;
        activeOpenedButtonBounds = null;
        mouseLeftBothUtc = null;
        hoverEnteredUtc = null;
        currentButtonBounds = null;
        currentHoverTarget = null;
    }

    public void NotifyOpenedByClick()
    {
        OpenedFromTaskbarHover = false;
        mouseLeftBothUtc = null;
        hoverEnteredUtc = null;
    }

    public bool IsCursorOverHoveredTaskbarButton()
    {
        var cursor = WindowPlacement.GetCursorPixelPoint();
        if (currentButtonBounds is { } b1 && currentHoverTarget is not null && Contains(b1, cursor, margin: 8))
            return true;
        if (activeOpenedButtonBounds is { } b2 && Contains(b2, cursor, margin: 8))
            return true;
        return false;
    }

    public void Cleanup()
    {
        disposed = true;
        timer.Stop();
        try { probeSignal.Set(); } catch { }
        ResetHoverState();
        OpenedFromTaskbarHover = false;
    }

    private void ResetHoverState()
    {
        currentButtonBounds = null;
        currentHoverTarget = null;
        hoverEnteredUtc = null;
        mouseLeftBothUtc = null;
        lastQueriedPoint = new(-99999, -99999);
    }

    private void RefreshPinnedTargets(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - lastPinnedScanUtc).TotalSeconds < 4)
            return;
        lastPinnedScanUtc = now;
        cachedPinnedTargets = launchers.GetPinnedTaskbarTargets(config);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!config.Settings.EnableTaskbar ||
            config.Settings.OpeningBehavior is not (ContainerOpeningBehavior.Hover or ContainerOpeningBehavior.ClickOrHover))
        {
            timer.Stop();
            return;
        }

        if (isSettingsOpen() || model.IsEditing || launcher.SuppressDismiss || launcher.IsContextMenuOpen)
        {
            hoverEnteredUtc = null;
            mouseLeftBothUtc = null;
            return;
        }

        var cursor = WindowPlacement.GetCursorPixelPoint();

        // 1. Handle auto-closing when the launcher was opened via taskbar hover
        if (OpenedFromTaskbarHover && launcher.IsVisible && !launcher.IsLateralFlyout)
        {
            bool overLauncher = IsCursorOverLauncher(cursor);
            bool overActiveButton = activeOpenedButtonBounds is { } activeBounds && Contains(activeBounds, cursor, margin: 12);
            bool overPendingButton = currentHoverTarget is not null && currentButtonBounds is { } pendingBounds && Contains(pendingBounds, cursor, margin: 8);

            if (overLauncher || overActiveButton || overPendingButton)
            {
                mouseLeftBothUtc = null;
            }
            else
            {
                mouseLeftBothUtc ??= DateTime.UtcNow;
                int closeDelayMs = Math.Clamp(config.Settings.HoverClosingDelayMs, 100, 2000);
                if ((DateTime.UtcNow - mouseLeftBothUtc.Value).TotalMilliseconds >= closeDelayMs)
                {
                    OpenedFromTaskbarHover = false;
                    activeOpenedTarget = null;
                    activeOpenedButtonBounds = null;
                    mouseLeftBothUtc = null;
                    launcher.Hide();
                    return;
                }
            }
        }

        // Do not start a new hover open while mouse buttons are pressed
        if (System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.None)
        {
            hoverEnteredUtc = null;
            return;
        }

        // Clear suppression once cursor leaves the dismissed button
        if (suppressedButtonBounds is { } suppressed)
        {
            if (!Contains(suppressed, cursor, margin: 6))
                suppressedButtonBounds = null;
            else
                return;
        }

        // 2. Check if cursor is still inside an already-matched AppDock taskbar button bounds
        if (currentHoverTarget is not null && currentButtonBounds is { } knownBounds && Contains(knownBounds, cursor, margin: 2))
        {
            EvaluateHoverDwell();
            return;
        }

        // Cursor left the previous matched button bounds
        currentButtonBounds = null;
        currentHoverTarget = null;
        hoverEnteredUtc = null;

        // 3. Fast Win32 check: only query UIA if the cursor is over a Windows Taskbar window
        if (!IsCursorInTaskbarWindow(cursor, out var taskbarHwnd))
        {
            lastQueriedPoint = new(-99999, -99999);
            return;
        }

        // Avoid redundant UIA queries if cursor has not moved since the last negative probe (unless >350ms elapsed)
        var now = DateTime.UtcNow;
        if (DistanceSquared(cursor, lastQueriedPoint) < 4 && (now - lastQueryUtc).TotalMilliseconds < 350)
            return;

        if (queryInFlight && DistanceSquared(cursor, lastQueriedPoint) < 9)
            return;

        RefreshPinnedTargets(force: false);
        var snapshotPinned = cachedPinnedTargets;
        lastQueriedPoint = cursor;
        lastQueryUtc = now;
        queryInFlight = true;

        EnsureProbeThread();
        lock (probeLock)
        {
            pendingRequest = new ProbeRequest(cursor, taskbarHwnd, snapshotPinned);
        }
        probeSignal.Set();
    }

    private void ExecuteProbeOnSta(ProbeRequest req, IUIAutomation? nativeUia)
    {
        var queryPoint = req.Point;
        var pinnedTargets = req.PinnedTargets;
        TaskbarHoverTarget? matchedTarget = null;
        PixelRect? matchedBounds = null;

        try
        {
            // Stage 1: Native COM IUIAutomation v3 (supports Windows 11 XAML Islands directly)
            if (nativeUia is not null)
            {
                (matchedTarget, matchedBounds) = ProbeWithNativeUia(nativeUia, queryPoint, pinnedTargets);
            }

            // Stage 2: Managed AutomationElement.FromPoint fallback
            if (matchedTarget is null)
            {
                var element = AutomationElement.FromPoint(new Point(queryPoint.X, queryPoint.Y));
                if (element is not null)
                {
                    (matchedTarget, matchedBounds) = InspectElementAndAncestors(element, queryPoint, pinnedTargets);
                }
            }

            // Stage 3: Fallback scan of taskbar buttons by BoundingRectangle if FromPoint hit an outer XAML island or frame
            if (matchedTarget is null && req.TaskbarHwnd != IntPtr.Zero)
            {
                (matchedTarget, matchedBounds) = ScanTaskbarButtonsAtPoint(req.TaskbarHwnd, queryPoint, pinnedTargets);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        if (disposed) return;

        launcher.Dispatcher.BeginInvoke(() =>
        {
            queryInFlight = false;
            if (matchedTarget is null || matchedBounds is not { } validBounds)
            {
                return;
            }

            var liveCursor = WindowPlacement.GetCursorPixelPoint();
            if (!Contains(validBounds, liveCursor, margin: 6))
                return;

            currentButtonBounds = validBounds;
            currentHoverTarget = matchedTarget;
            hoverEnteredUtc = DateTime.UtcNow;
            EvaluateHoverDwell();
        });
    }

    private (TaskbarHoverTarget? Target, PixelRect? Bounds) ProbeWithNativeUia(
        IUIAutomation uia,
        PixelPoint queryPoint,
        IReadOnlyDictionary<string, Guid?> pinnedTargets)
    {
        IUIAutomationElement? element = null;
        IUIAutomationTreeWalker? walker = null;
        try
        {
            if (uia.ElementFromPoint(new Win32Point { X = queryPoint.X, Y = queryPoint.Y }, out element) != 0 || element is null)
                return (null, null);

            uia.get_RawViewWalker(out walker);
            IUIAutomationElement? current = element;
            element = null; // Ownership transferred to 'current' loop variable
            PixelRect? buttonRect = null;

            for (int depth = 0; depth < 8 && current is not null; depth++)
            {
                try
                {
                    string cls = GetUiaString(current, 30012 /* UIA_ClassNamePropertyId */);
                    string autoId = GetUiaString(current, 30011 /* UIA_AutomationIdPropertyId */);
                    string name = GetUiaString(current, 30005 /* UIA_NamePropertyId */);
                    int ctrlType = GetUiaInt(current, 30003 /* UIA_ControlTypePropertyId */);
                    var rect = GetUiaRect(current, 30001 /* UIA_BoundingRectanglePropertyId */);

                    if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Taskbar.TaskbarFrameAutomationPeer" or "MSTaskListWClass" or "MSTaskSwWClass" or "ReBarWindow32")
                        break;

                    if (rect is { } r && (r.Right - r.Left) >= 14 && (r.Right - r.Left) <= 280 && (r.Bottom - r.Top) >= 14 && (r.Bottom - r.Top) <= 160)
                    {
                        if (ctrlType == 50000 /* UIA_ButtonControlTypeId */ || cls.Contains("Button", StringComparison.OrdinalIgnoreCase) || buttonRect is null)
                            buttonRect = r;
                    }

                    var candidate = TaskbarCapabilities.MatchHoveredButton(config, autoId, name, pinnedTargets);
                    if (candidate is not null)
                    {
                        var finalRect = buttonRect ?? new PixelRect(queryPoint.X - 20, queryPoint.Y - 20, queryPoint.X + 20, queryPoint.Y + 20);
                        return (candidate, finalRect);
                    }

                    if (autoId.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase) &&
                        !autoId.Contains("AppDock", StringComparison.OrdinalIgnoreCase) &&
                        !autoId.Contains("AppBox", StringComparison.OrdinalIgnoreCase) &&
                        !autoId.Contains("Microsoft.AutoGenerated.", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    if (walker is null || walker.GetParentElement(current, out var parent) != 0 || parent is null)
                        break;

                    Marshal.FinalReleaseComObject(current);
                    current = parent;
                }
                catch
                {
                    break;
                }
            }

            if (current is not null)
            {
                try { Marshal.FinalReleaseComObject(current); } catch { }
            }
        }
        catch
        {
        }
        finally
        {
            if (element is not null) { try { Marshal.FinalReleaseComObject(element); } catch { } }
            if (walker is not null) { try { Marshal.FinalReleaseComObject(walker); } catch { } }
        }
        return (null, null);
    }

    private static string GetUiaString(IUIAutomationElement el, int propertyId)
    {
        if (el.GetCurrentPropertyValue(propertyId, out var val) == 0 && val is string s)
            return s;
        return "";
    }

    private static int GetUiaInt(IUIAutomationElement el, int propertyId)
    {
        if (el.GetCurrentPropertyValue(propertyId, out var val) == 0 && val is int i)
            return i;
        return 0;
    }

    private static PixelRect? GetUiaRect(IUIAutomationElement el, int propertyId)
    {
        if (el.GetCurrentPropertyValue(propertyId, out var val) == 0 && val is double[] arr && arr.Length == 4)
        {
            double left = arr[0], top = arr[1], width = arr[2], height = arr[3];
            if (double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0)
            {
                return new PixelRect(
                    (int)Math.Round(left),
                    (int)Math.Round(top),
                    (int)Math.Round(left + width),
                    (int)Math.Round(top + height));
            }
        }
        return null;
    }

    private (TaskbarHoverTarget? Target, PixelRect? Bounds) InspectElementAndAncestors(
        AutomationElement startElement,
        PixelPoint queryPoint,
        IReadOnlyDictionary<string, Guid?> pinnedTargets)
    {
        var walker = TreeWalker.RawViewWalker;
        AutomationElement? current = startElement;
        PixelRect? buttonRect = null;

        for (int depth = 0; depth < 8 && current is not null; depth++)
        {
            string cls;
            string autoId;
            string name;
            Rect rect;
            ControlType? ctrlType;

            try
            {
                var info = current.Current;
                cls = info.ClassName ?? "";
                autoId = info.AutomationId ?? "";
                name = info.Name ?? "";
                rect = info.BoundingRectangle;
                ctrlType = info.ControlType;
            }
            catch
            {
                break;
            }

            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Taskbar.TaskbarFrameAutomationPeer" or "MSTaskListWClass" or "MSTaskSwWClass" or "ReBarWindow32")
                break;

            if (!rect.IsEmpty && rect.Width >= 14 && rect.Width <= 280 && rect.Height >= 14 && rect.Height <= 160)
            {
                var pxRect = new PixelRect(
                    (int)Math.Round(rect.Left),
                    (int)Math.Round(rect.Top),
                    (int)Math.Round(rect.Right),
                    (int)Math.Round(rect.Bottom));

                if (ctrlType == ControlType.Button || cls.Contains("Button", StringComparison.OrdinalIgnoreCase) || buttonRect is null)
                    buttonRect = pxRect;
            }

            var candidate = TaskbarCapabilities.MatchHoveredButton(config, autoId, name, pinnedTargets);
            if (candidate is not null)
            {
                var finalRect = buttonRect ?? new PixelRect(queryPoint.X - 20, queryPoint.Y - 20, queryPoint.X + 20, queryPoint.Y + 20);
                return (candidate, finalRect);
            }

            // Stop walking higher only if this is an explicit foreign AppId (excluding Microsoft.AutoGenerated)
            if (autoId.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase) &&
                !autoId.Contains("AppDock", StringComparison.OrdinalIgnoreCase) &&
                !autoId.Contains("AppBox", StringComparison.OrdinalIgnoreCase) &&
                !autoId.Contains("Microsoft.AutoGenerated.", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            try { current = walker.GetParent(current); }
            catch { break; }
        }

        return (null, null);
    }

    private (TaskbarHoverTarget? Target, PixelRect? Bounds) ScanTaskbarButtonsAtPoint(
        IntPtr taskbarHwnd,
        PixelPoint queryPoint,
        IReadOnlyDictionary<string, Guid?> pinnedTargets)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbarHwnd);
            if (root is null) return (null, null);

            var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            var buttons = root.FindAll(TreeScope.Descendants, buttonCondition);
            for (int i = 0; i < buttons.Count; i++)
            {
                try
                {
                    var btn = buttons[i];
                    var rect = btn.Current.BoundingRectangle;
                    if (rect.IsEmpty || rect.Width < 14 || rect.Width > 280) continue;

                    var pxRect = new PixelRect(
                        (int)Math.Round(rect.Left),
                        (int)Math.Round(rect.Top),
                        (int)Math.Round(rect.Right),
                        (int)Math.Round(rect.Bottom));

                    if (!Contains(pxRect, queryPoint, margin: 2))
                        continue;

                    var autoId = btn.Current.AutomationId ?? "";
                    var name = btn.Current.Name ?? "";
                    var match = TaskbarCapabilities.MatchHoveredButton(config, autoId, name, pinnedTargets);
                    if (match is not null)
                    {
                        return (match, pxRect);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return (null, null);
    }

    private void EvaluateHoverDwell()
    {
        if (currentHoverTarget is not { } target || currentButtonBounds is not { } bounds || hoverEnteredUtc is not { } enteredUtc)
            return;

        bool alreadyOpen = launcher.IsVisible && !launcher.IsLateralFlyout;
        if (alreadyOpen && IsTargetAlreadyDisplayed(target))
        {
            activeOpenedTarget = target;
            activeOpenedButtonBounds = bounds;
            return;
        }

        int requiredDwellMs = alreadyOpen
            ? Math.Clamp(config.Settings.HoverOpeningDelayMs / 3, 75, 220)
            : Math.Clamp(config.Settings.HoverOpeningDelayMs, 100, 2000);

        if ((DateTime.UtcNow - enteredUtc).TotalMilliseconds < requiredDwellMs)
            return;

        hoverEnteredUtc = null;
        activeOpenedTarget = target;
        activeOpenedButtonBounds = bounds;
        mouseLeftBothUtc = null;
        OpenedFromTaskbarHover = true;

        int centerX = (bounds.Left + bounds.Right) / 2;
        int centerY = (bounds.Top + bounds.Bottom) / 2;
        var anchor = new WindowPlacement.Anchor(new PixelPoint(centerX, centerY));
        openFromHover(target, anchor);
    }

    private bool IsTargetAlreadyDisplayed(TaskbarHoverTarget target)
    {
        if (!launcher.IsVisible || launcher.IsLateralFlyout)
            return false;

        if (target.IsMainLauncher || config.Settings.LayoutMode == LayoutMode.SingleAppBox)
            return model.IsOverview;

        return !model.IsOverview && model.SelectedFolder?.Id == target.ContainerId;
    }

    private bool IsCursorOverLauncher(PixelPoint cursor)
    {
        if (!launcher.IsVisible)
            return false;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(launcher).Handle;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var winRect))
        {
            // Include a 24px bridge zone around the popup so moving from the taskbar button into the popup is seamless
            var rect = new PixelRect(winRect.Left, winRect.Top, winRect.Right, winRect.Bottom);
            if (Contains(rect, cursor, margin: 24))
                return true;
        }
        return false;
    }

    private static bool Contains(PixelRect rect, PixelPoint pt, int margin = 0)
    {
        return pt.X >= rect.Left - margin &&
               pt.X <= rect.Right + margin &&
               pt.Y >= rect.Top - margin &&
               pt.Y <= rect.Bottom + margin;
    }

    private static int DistanceSquared(PixelPoint a, PixelPoint b)
    {
        int dx = a.X - b.X;
        int dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static bool IsCursorInTaskbarWindow(PixelPoint pt, out IntPtr taskbarHwnd)
    {
        taskbarHwnd = IntPtr.Zero;
        var hwnd = WindowFromPoint(new Win32Point { X = pt.X, Y = pt.Y });
        if (hwnd != IntPtr.Zero)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId)
                return false;

            var root = GetAncestor(hwnd, 2 /* GA_ROOT */);
            if (root != IntPtr.Zero && IsTaskbarClass(root))
            {
                taskbarHwnd = root;
                return true;
            }

            var rootOwner = GetAncestor(hwnd, 3 /* GA_ROOTOWNER */);
            if (rootOwner != IntPtr.Zero && IsTaskbarClass(rootOwner))
            {
                taskbarHwnd = rootOwner;
                return true;
            }
        }

        // Also check primary/secondary taskbar bounding rects directly (handles XAML island tooltips/popups)
        var primaryTray = FindWindowW("Shell_TrayWnd", null);
        if (primaryTray != IntPtr.Zero && GetWindowRect(primaryTray, out var primaryRect))
        {
            var r = new PixelRect(primaryRect.Left, primaryRect.Top, primaryRect.Right, primaryRect.Bottom);
            if (Contains(r, pt, margin: 0))
            {
                taskbarHwnd = primaryTray;
                return true;
            }
        }

        IntPtr secondaryTray = IntPtr.Zero;
        while ((secondaryTray = FindWindowExW(IntPtr.Zero, secondaryTray, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (GetWindowRect(secondaryTray, out var secRect))
            {
                var r = new PixelRect(secRect.Left, secRect.Top, secRect.Right, secRect.Bottom);
                if (Contains(r, pt, margin: 0))
                {
                    taskbarHwnd = secondaryTray;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [PreserveSig] int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2, out int areSame);
        [PreserveSig] int CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2, out int areSame);
        [PreserveSig] int GetRootElement(out IUIAutomationElement root);
        [PreserveSig] int ElementFromHandle(IntPtr hwnd, out IUIAutomationElement element);
        [PreserveSig] int ElementFromPoint(Win32Point pt, out IUIAutomationElement element);
        [PreserveSig] int GetFocusedElement(out IUIAutomationElement element);
        [PreserveSig] int GetRootElementBuildCache(IntPtr cacheRequest, out IUIAutomationElement root);
        [PreserveSig] int ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheRequest, out IUIAutomationElement element);
        [PreserveSig] int ElementFromPointBuildCache(Win32Point pt, IntPtr cacheRequest, out IUIAutomationElement element);
        [PreserveSig] int GetFocusedElementBuildCache(IntPtr cacheRequest, out IUIAutomationElement element);
        [PreserveSig] int CreateTreeWalker(IntPtr pCondition, out IUIAutomationTreeWalker walker);
        [PreserveSig] int get_ControlViewWalker(out IUIAutomationTreeWalker walker);
        [PreserveSig] int get_ContentViewWalker(out IUIAutomationTreeWalker walker);
        [PreserveSig] int get_RawViewWalker(out IUIAutomationTreeWalker walker);
    }

    [ComImport, Guid("4042C624-389C-4AFC-A630-9DF854A541FC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTreeWalker
    {
        [PreserveSig] int GetParentElement(IUIAutomationElement element, out IUIAutomationElement parent);
    }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        [PreserveSig] int SetFocus();
        [PreserveSig] int GetRuntimeId(out IntPtr runtimeId);
        [PreserveSig] int FindFirst(int scope, IntPtr condition, out IUIAutomationElement found);
        [PreserveSig] int FindAll(int scope, IntPtr condition, out IntPtr found);
        [PreserveSig] int FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IUIAutomationElement found);
        [PreserveSig] int FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found);
        [PreserveSig] int BuildUpdatedCache(IntPtr cacheRequest, out IUIAutomationElement updatedElement);
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object retVal);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Win32Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Win32Rect rect);
}
