using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AppBox.Core;

namespace AppBox.Infrastructure;

public sealed class ContainerLaunchers(string directory, string executable)
{
    public string DirectoryPath => Path.Combine(directory, "Launchers");
    public string ShortcutPath(Guid id) => Path.Combine(DirectoryPath, id.ToString("N") + ".lnk");
    public static string Identity(Guid id) => "AppDock.Container." + id.ToString("N");
    public string MainShortcutPath => Path.Combine(DirectoryPath, "AppDock.lnk");
    public void UpdateMain(string productIcon)
    {
        Directory.CreateDirectory(DirectoryPath);
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = ((dynamic)shell!).CreateShortcut(MainShortcutPath);
            ((dynamic)link).TargetPath = executable; ((dynamic)link).Arguments = "";
            ((dynamic)link).WorkingDirectory = Path.GetDirectoryName(executable)!;
            ((dynamic)link).Description = "AppDock"; ((dynamic)link).IconLocation = productIcon + ",0"; ((dynamic)link).Save();
        }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
        TaskbarIdentity.SetShortcut(MainShortcutPath, WindowsJumpLists.MainIdentity);
    }
    public void Sync(Configuration config)
    {
        Directory.CreateDirectory(DirectoryPath);
        // Retain inactive shortcuts so existing user pins never lose their target or icon.
        if (config.Settings.LayoutMode != LayoutMode.MultipleAppBoxes) return;
        foreach (var folder in config.Folders.Where(f => f.IsActive || File.Exists(ShortcutPath(f.Id)))) Update(folder);
    }
    public void Update(AppFolder folder)
    {
        Directory.CreateDirectory(DirectoryPath);
        var icon = new ContainerIcons(directory).PersistentPath(folder.Icon);
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = ((dynamic)shell!).CreateShortcut(ShortcutPath(folder.Id));
            ((dynamic)link).TargetPath = executable;
            ((dynamic)link).Arguments = "--container " + folder.Id;
            ((dynamic)link).WorkingDirectory = Path.GetDirectoryName(executable)!;
            ((dynamic)link).Description = "AppDock — " + folder.Name;
            ((dynamic)link).IconLocation = icon + ",0";
            ((dynamic)link).Save();
        }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
        TaskbarIdentity.SetShortcut(ShortcutPath(folder.Id), Identity(folder.Id));
    }
    public void ApplyWindow(Window window, AppFolder? folder)
    {
        window.Title = folder is null ? "AppDock" : folder.Name + " — AppDock";
        window.Icon = folder is null ? ContainerIcons.Load(Path.Combine(AppContext.BaseDirectory, "AppDock.ico")) : new ContainerIcons(directory).Get(folder.Icon);
        var icon = folder is null ? Path.Combine(AppContext.BaseDirectory, "AppDock.ico") : new ContainerIcons(directory).PersistentPath(folder.Icon);
        TaskbarIdentity.SetWindow(window, folder is null ? "AppBox.Desktop" : Identity(folder.Id),
            "\"" + executable + "\"" + (folder is null ? "" : " --container " + folder.Id), icon, window.Title);
    }
}

// Documented property stores only. Never touch Explorer's taskband registry or pinned files.
internal static class TaskbarIdentity
{
    [StructLayout(LayoutKind.Sequential)] private struct Key { public Guid Format; public uint Id; public Key(uint id) { Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); Id = id; } }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Value { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Text; }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out Key key); void GetValue(ref Key key, out Value value);
        void SetValue(ref Key key, ref Value value); void Commit();
    }
    [DllImport("shell32.dll", PreserveSig = false)] private static extern void SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, out IPropertyStore store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] private static extern void SHGetPropertyStoreFromParsingName(string path, IntPtr context, uint flags, ref Guid iid, out IPropertyStore store);
    private static void Set(IPropertyStore store, uint id, string text)
    {
        var key = new Key(id); var value = new Value { Type = 31, Text = Marshal.StringToCoTaskMemUni(text) };
        try { store.SetValue(ref key, ref value); } finally { Marshal.FreeCoTaskMem(value.Text); }
    }
    public static void SetShortcut(string path, string identity)
    {
        var iid = typeof(IPropertyStore).GUID; SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 2, ref iid, out var store);
        try { Set(store, 5, identity); store.Commit(); } finally { Marshal.FinalReleaseComObject(store); }
    }
    public static void SetWindow(Window window, string identity, string command, string icon, string title)
    {
        var iid = typeof(IPropertyStore).GUID; SHGetPropertyStoreForWindow(new WindowInteropHelper(window).EnsureHandle(), ref iid, out var store);
        try { Set(store, 2, command); Set(store, 3, icon + ",0"); Set(store, 4, title); Set(store, 5, identity); } finally { Marshal.FinalReleaseComObject(store); }
    }
}
