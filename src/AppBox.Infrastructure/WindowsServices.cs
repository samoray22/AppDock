using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AppBox.Core;

namespace AppBox.Infrastructure;

public sealed class WindowsShortcutResolver : IShortcutResolver
{
    public ResolvedTarget Resolve(string path)
    {
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = ((dynamic)shell!).CreateShortcut(path);
            return new((string)((dynamic)link).TargetPath, (string)((dynamic)link).Arguments, (string)((dynamic)link).WorkingDirectory);
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }
}

public sealed class WindowsProcessLauncher : IProcessLauncher
{
    public void Start(ResolvedTarget target)
    {
        using var process = Process.Start(new ProcessStartInfo(target.FileName)
        { UseShellExecute = true, Arguments = target.Arguments, WorkingDirectory = target.WorkingDirectory });
    }
}

public static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return string.Equals(key?.GetValue("AppBox") as string, Command(), StringComparison.Ordinal);
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (enabled) key.SetValue("AppBox", Command());
        else key.DeleteValue("AppBox", false);
    }
    private static string Command() => "\"" + Environment.ProcessPath + "\" --background";
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
    public IReadOnlyList<AppEntry> Discover()
    {
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
        return results.DistinctBy(a => a.Target, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

public static class ShellIcon
{
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width; public int Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { void GetImage(Size size, int flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string name, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    public static ImageSource? Get(AppEntry entry)
    {
        IShellItemImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            var target = entry.Kind == TargetKind.Packaged ? "shell:AppsFolder\\" + entry.Target : entry.Target;
            SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out factory);
            factory.GetImage(new Size { Width = 64, Height = 64 }, 0x4 /* ICONONLY */, out bitmap);
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze(); return image;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException) { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (factory is not null) Marshal.FinalReleaseComObject(factory);
        }
    }
}
