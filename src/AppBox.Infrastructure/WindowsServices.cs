using System.Collections.Concurrent;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AppBox.Core;

namespace AppBox.Infrastructure;

public sealed class WindowsShortcutResolver : IShortcutResolver
{
    private static readonly ConcurrentDictionary<string, (long WriteTicks, ResolvedTarget Target)> Cache = new(StringComparer.OrdinalIgnoreCase);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    }

    public ResolvedTarget Resolve(string path)
    {
        var fullPath = Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : path;
        var ticks = File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath).Ticks : -1;
        if (ticks >= 0 && Cache.TryGetValue(fullPath, out var cached) && cached.WriteTicks == ticks)
            return cached.Target;

        var resolved = ResolveCore(fullPath);
        if (ticks >= 0)
            Cache[fullPath] = (ticks, resolved);
        return resolved;
    }

    private static ResolvedTarget ResolveCore(string path)
    {
        try
        {
            var link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                ((IPersistFile)link).Load(path, 0);
                var target = new StringBuilder(1024);
                var args = new StringBuilder(1024);
                var workDir = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                link.GetArguments(args, args.Capacity);
                link.GetWorkingDirectory(workDir, workDir.Capacity);
                if (target.Length > 0)
                    return new(target.ToString(), args.ToString(), workDir.ToString());
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        catch (COMException)
        {
            // Fall back to WScript.Shell below if direct IPersistFile loading fails.
        }

        object? shell = null, fallbackLink = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            fallbackLink = ((dynamic)shell!).CreateShortcut(path);
            return new((string)((dynamic)fallbackLink).TargetPath, (string)((dynamic)fallbackLink).Arguments, (string)((dynamic)fallbackLink).WorkingDirectory);
        }
        finally
        {
            if (fallbackLink is not null) Marshal.FinalReleaseComObject(fallbackLink);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }
}

public sealed class WindowsProcessLauncher : IProcessLauncher
{
    public void Start(ResolvedTarget target)
    {
        var psi = new ProcessStartInfo(target.FileName)
        {
            UseShellExecute = true,
            Arguments = target.Arguments,
            WorkingDirectory = target.WorkingDirectory
        };
        if (!string.IsNullOrEmpty(target.Verb)) psi.Verb = target.Verb;
        using var process = Process.Start(psi);
    }
}

public static class WindowsFileLocation
{
    public static void Open(AppEntry entry)
    {
        if (entry.Kind == TargetKind.Packaged) throw new ArgumentException("This application does not expose a file location.");
        TargetPolicy.Validate(entry);
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "/select,\"" + entry.Target + "\"") { UseShellExecute = true });
    }
}

public static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Task Manager / Settings > Startup apps store their on/off state here; an odd first byte means disabled.
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>True only when Windows will actually start AppDock at sign-in.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var name = key?.GetValue("AppDock") is string ? "AppDock" : key?.GetValue("AppBox") is string ? "AppBox" : null;
        if (name is null || TargetExists(key!.GetValue(name) as string) == false) return false;
        return !IsDisabledByUser(name);
    }

    public static void SetEnabled(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            if (enabled) key.SetValue("AppDock", Command());
            else key.DeleteValue("AppDock", false);
            key.DeleteValue("AppBox", false);
        }
        using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath);
        // An explicit opt-in from AppDock settings overrides an earlier "Disabled" from Task Manager.
        if (enabled) approved.SetValue("AppDock", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        else approved.DeleteValue("AppDock", false);
        approved.DeleteValue("AppBox", false);
    }

    /// <summary>Points a broken startup entry (target moved, renamed or deleted) at the running executable.</summary>
    public static void RepairPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key is null) return;
        var current = key.GetValue("AppDock") as string ?? key.GetValue("AppBox") as string;
        if (current is null || string.Equals(current, Command(), StringComparison.OrdinalIgnoreCase)) return;
        // Only heal broken entries; a second copy (e.g. a debug build) must not take over a working one.
        if (TargetExists(current) != false) return;
        key.SetValue("AppDock", Command());
        key.DeleteValue("AppBox", false);
    }

    private static bool IsDisabledByUser(string name)
    {
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
        return approved?.GetValue(name) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }

    private static bool? TargetExists(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var trimmed = command.Trim();
        var path = trimmed.StartsWith('"') ? trimmed[1..].Split('"')[0] : trimmed.Split(' ')[0];
        return File.Exists(path);
    }

    private static string Command() => "\"" + AppPaths.Executable + "\" --background";
}

public static class AppPaths
{
    public const string PortableFolderName = "AppDock_Data";

    /// <summary>The running executable, whatever it was renamed to (e.g. AppDock_v2.9.0.exe).</summary>
    public static string Executable { get; } =
        Environment.ProcessPath is { Length: > 0 } path && !path.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? path
            : Path.Combine(AppContext.BaseDirectory, "AppDock.exe");

    /// <summary>The directory where the running executable resides.</summary>
    public static string ExecutableDirectory { get; } =
        Path.GetDirectoryName(Executable) is { Length: > 0 } dir ? dir : AppContext.BaseDirectory;

    /// <summary>Portable data subfolder next to the executable (&lt;ExeDir&gt;\AppDock_Data).</summary>
    public static string PortableDataDirectory { get; set; } =
        Path.Combine(ExecutableDirectory, PortableFolderName);

    /// <summary>Standard Windows LocalAppData folder (%LOCALAPPDATA%\AppDock).</summary>
    public static string LocalAppDataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppDock");

    /// <summary>Legacy LocalAppData folder (%LOCALAPPDATA%\AppBox) for one-time migration.</summary>
    public static string LegacyLocalAppDataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppBox");

    private static string? activeDataDirectory;

    /// <summary>Currently active data directory (Portable or LocalAppData).</summary>
    public static string ActiveDataDirectory
    {
        get => activeDataDirectory ?? ResolveAutomaticDirectory();
        set => activeDataDirectory = value;
    }

    public static bool IsPortableMode => IsPortableDirectory(ActiveDataDirectory);

    public static bool IsPortableDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        try
        {
            var normDir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normPortable = Path.GetFullPath(PortableDataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normDir, normPortable, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveAutomaticDirectory()
    {
        bool hasPortable = ConfigurationStore.HasSettingsFile(PortableDataDirectory);
        bool hasAppData = ConfigurationStore.HasSettingsFile(LocalAppDataDirectory) ||
                          ConfigurationStore.HasSettingsFile(LegacyLocalAppDataDirectory);

        if (hasPortable && !hasAppData) return PortableDataDirectory;
        return LocalAppDataDirectory;
    }

    public static bool IsDirectoryWritable(string directory)
    {
        var existedBefore = Directory.Exists(directory);
        var probeFile = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(probeFile, [1]);
            File.Delete(probeFile);
            if (!existedBefore && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                try { Directory.Delete(directory); } catch { }
            }
            return true;
        }
        catch
        {
            try { if (File.Exists(probeFile)) File.Delete(probeFile); } catch { }
            return false;
        }
    }
}

public static class SystemTheme
{
    public static bool IsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}

public sealed class ApplicationDiscovery
{
    private static IReadOnlyList<AppEntry>? cachedCatalog;
    private static long cacheTimestamp;
    private static readonly object CacheLock = new();

    public static void WarmUpInBackground()
    {
        var thread = new Thread(() =>
        {
            try { _ = new ApplicationDiscovery().Discover(); }
            catch { /* Best-effort background warmup */ }
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public IReadOnlyList<AppEntry> Discover()
    {
        var now = Environment.TickCount64;
        if (cachedCatalog is { } current && now - Interlocked.Read(ref cacheTimestamp) < 30_000)
            return current;

        lock (CacheLock)
        {
            if (cachedCatalog is { } lockedCurrent && Environment.TickCount64 - cacheTimestamp < 30_000)
                return lockedCurrent;

            var results = new List<AppEntry>();
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
            {
                if (!Directory.Exists(dir)) continue;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(dir, "*.lnk", options))
                    results.Add(new() { Name = Path.GetFileNameWithoutExtension(file), Target = file, Kind = TargetKind.Shortcut });
            }
            object? shell = null, folder = null, items = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
                items = ((dynamic)folder!).Items();
                foreach (object item in (dynamic)items)
                {
                    try
                    {
                        string id = ((dynamic)item).ExtendedProperty("System.AppUserModel.ID") as string ?? "";
                        var entry = new AppEntry { Name = (string)((dynamic)item).Name, Target = id, Kind = TargetKind.Packaged };
                        try { TargetPolicy.Validate(entry); results.Add(entry); } catch (ArgumentException) { }
                    }
                    finally { Marshal.FinalReleaseComObject(item); }
                }
            }
            catch (COMException) { /* Start menu results remain available if the AppsFolder catalog is unavailable. */ }
            finally
            {
                if (items is not null) Marshal.FinalReleaseComObject(items);
                if (folder is not null) Marshal.FinalReleaseComObject(folder);
                if (shell is not null) Marshal.FinalReleaseComObject(shell);
            }
            var finalized = results.DistinctBy(a => a.Target, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            cachedCatalog = finalized;
            Interlocked.Exchange(ref cacheTimestamp, Environment.TickCount64);
            return finalized;
        }
    }
}

public static class ShellIcon
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? cachedUrlIcon;
    private static ImageSource? cachedRecycleBinEmpty;
    private static ImageSource? cachedRecycleBinFull;

    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width; public int Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { void GetImage(Size size, int flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string name, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo { public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string file, uint attributes, out FileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", SetLastError = false)]
    private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

    public static ImageSource? GetRecycleBinIcon(bool isFull)
    {
        if (isFull && cachedRecycleBinFull is not null) return cachedRecycleBinFull;
        if (!isFull && cachedRecycleBinEmpty is not null) return cachedRecycleBinEmpty;

        var sii = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        int hr = SHGetStockIconInfo(isFull ? 32u : 31u, 0x00000100 /* SHGSI_ICON */, ref sii);
        if (hr == 0 && sii.hIcon != IntPtr.Zero)
        {
            try
            {
                var img = Imaging.CreateBitmapSourceFromHIcon(sii.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                img.Freeze();
                if (isFull) cachedRecycleBinFull = img;
                else cachedRecycleBinEmpty = img;
                return img;
            }
            finally
            {
                DestroyIcon(sii.hIcon);
            }
        }
        return null;
    }

    public static void PreloadInBackground(IEnumerable<AppEntry> entries)
    {
        var list = entries.ToList();
        if (list.Count == 0) return;
        var thread = new Thread(() =>
        {
            foreach (var entry in list)
            {
                try { _ = Get(entry); }
                catch { /* Ignore background preload errors */ }
            }
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static ImageSource? Get(AppEntry entry)
    {
        // Use the Windows association icon without honoring a .url file's arbitrary IconFile directive.
        if (entry.Kind == TargetKind.InternetShortcut)
        {
            if (cachedUrlIcon is not null) return cachedUrlIcon;
            SHGetFileInfo(".url", 0x80, out var info, (uint)Marshal.SizeOf<FileInfo>(), 0x110);
            if (info.Icon == IntPtr.Zero) return null;
            try
            {
                var icon = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
                cachedUrlIcon = icon;
                return icon;
            }
            finally { DestroyIcon(info.Icon); }
        }

        var cacheKey = BuildCacheKey(entry);
        if (Cache.TryGetValue(cacheKey, out var existing) && existing is not null)
            return existing;

        IShellItemImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            var target = entry.Kind == TargetKind.Packaged ? "shell:AppsFolder\\" + entry.Target : entry.Target;
            SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out factory);
            factory.GetImage(new Size { Width = 64, Height = 64 }, 0x4 /* ICONONLY */, out bitmap);
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            Cache[cacheKey] = image;
            return image;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or IOException or UnauthorizedAccessException) { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (factory is not null) Marshal.FinalReleaseComObject(factory);
        }
    }

    private static string BuildCacheKey(AppEntry entry)
    {
        if (entry.Kind == TargetKind.Packaged)
            return "P:" + entry.Target;
        var ticks = File.Exists(entry.Target) ? File.GetLastWriteTimeUtc(entry.Target).Ticks : 0;
        return $"{(int)entry.Kind}:{entry.Target}:{ticks}";
    }
}

public static class DesktopIconsService
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private static bool hiddenByApp;

    public static IntPtr FindDesktopListView()
    {
        try
        {
            var progman = FindWindowW("Progman", null);
            var defView = IntPtr.Zero;
            if (progman != IntPtr.Zero)
            {
                defView = FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            }

            if (defView == IntPtr.Zero)
            {
                EnumWindows((topHwnd, _) =>
                {
                    var childDef = FindWindowExW(topHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (childDef != IntPtr.Zero)
                    {
                        defView = childDef;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }

            if (defView == IntPtr.Zero) return IntPtr.Zero;
            var listView = FindWindowExW(defView, IntPtr.Zero, "SysListView32", null);
            return listView != IntPtr.Zero ? listView : defView;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static bool AreDesktopIconsHidden()
    {
        var hwnd = FindDesktopListView();
        if (hwnd == IntPtr.Zero) return false;
        return !IsWindowVisible(hwnd);
    }

    public static bool IsHiddenByAppDock => hiddenByApp;

    public static bool IsStardockFencesRunning()
    {
        try
        {
            var procs = Process.GetProcessesByName("Fences");
            if (procs.Length > 0)
            {
                foreach (var p in procs) p.Dispose();
                return true;
            }

            // Stardock Fences also runs in-process inside explorer.exe via DesktopDock.dll;
            // check the active per-user Fences registry state when installed.
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Stardock\Fences");
            if (key is not null)
            {
                var installDir = key.GetValue("InstallDir") as string ?? key.GetValue("Path") as string;
                if (!string.IsNullOrWhiteSpace(installDir) && Directory.Exists(installDir))
                {
                    var disabled = key.GetValue("DisableFences");
                    if (disabled is int d && d != 0) return false;
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    public static bool IsStardockFencesDetected()
    {
        try
        {
            if (IsStardockFencesRunning()) return true;
            using var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Stardock\Fences");
            if (hkcu is not null) return true;
            using var hklm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Stardock\ObjectDesktop\Fences");
            if (hklm is not null) return true;
        }
        catch
        {
        }
        return false;
    }

    public enum DesktopVisibilityAction { None, Hide, Show }

    public static (DesktopVisibilityAction Action, bool NewHiddenByApp) EvaluateTransition(
        bool hideRequested,
        bool currentlyVisible,
        bool wasHiddenByApp,
        bool explicitUserAction,
        bool fencesRunning)
    {
        if (hideRequested)
        {
            if (!currentlyVisible)
            {
                // Already hidden externally (e.g. by Fences or Windows Explorer) or earlier by AppDock.
                return (DesktopVisibilityAction.None, wasHiddenByApp);
            }

            if (!explicitUserAction && fencesRunning)
            {
                // Avoid forcefully hiding SysListView32 behind Stardock Fences' back on automatic startup sync.
                return (DesktopVisibilityAction.None, false);
            }

            return (DesktopVisibilityAction.Hide, true);
        }
        else
        {
            if (currentlyVisible)
            {
                return (DesktopVisibilityAction.None, false);
            }

            // Only unhide if AppDock was the one that hid it, or the user explicitly toggled it in UI.
            if (wasHiddenByApp || explicitUserAction)
            {
                return (DesktopVisibilityAction.Show, false);
            }

            return (DesktopVisibilityAction.None, false);
        }
    }

    public static void SetDesktopIconsHidden(bool hide, bool explicitUserAction = false)
    {
        try
        {
            var hwnd = FindDesktopListView();
            if (hwnd == IntPtr.Zero) return;

            bool currentlyVisible = IsWindowVisible(hwnd);
            bool fencesRunning = IsStardockFencesRunning();
            var (action, newHiddenByApp) = EvaluateTransition(hide, currentlyVisible, hiddenByApp, explicitUserAction, fencesRunning);
            hiddenByApp = newHiddenByApp;

            if (action == DesktopVisibilityAction.Hide)
            {
                ShowWindow(hwnd, SW_HIDE);
            }
            else if (action == DesktopVisibilityAction.Show)
            {
                ShowWindow(hwnd, SW_SHOW);
            }
        }
        catch
        {
        }
    }

    public static void RestoreOnExit(bool restoreOnExit)
    {
        if (restoreOnExit && hiddenByApp)
        {
            SetDesktopIconsHidden(false, explicitUserAction: false);
        }
    }

    public static IReadOnlyList<string> DiscoverDesktopShortcutFiles()
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.*", options))
                {
                    var ext = Path.GetExtension(file);
                    if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var nameNoExt = Path.GetFileNameWithoutExtension(file);
                    if (nameNoExt.StartsWith("AppDock", StringComparison.OrdinalIgnoreCase) ||
                        nameNoExt.StartsWith("AppBox", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (seen.Add(file))
                    {
                        files.Add(file);
                    }
                }
            }
            catch
            {
            }
        }

        return files;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public static (long ItemCount, long TotalBytes) GetRecycleBinStatus()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            int hr = SHQueryRecycleBinW(null, ref info);
            if (hr == 0)
            {
                return (Math.Max(0, info.i64NumItems), Math.Max(0, info.i64Size));
            }
        }
        catch { }
        return (0, 0);
    }

    public static void OpenRecycleBin()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
        }
        catch { }
    }

    public static void EmptyRecycleBin(IntPtr ownerHwnd)
    {
        try
        {
            _ = SHEmptyRecycleBinW(ownerHwnd, null, 0);
        }
        catch { }
    }

    public static int MoveFilesToRecycleBin(IEnumerable<string> paths)
    {
        int count = 0;
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    count++;
                }
                else if (Directory.Exists(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    count++;
                }
            }
            catch { }
        }
        return count;
    }
}
