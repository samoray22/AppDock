using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
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

    public void UpdateMain(string? productIcon = null)
    {
        Directory.CreateDirectory(DirectoryPath);
        var iconPath = !string.IsNullOrEmpty(productIcon) && File.Exists(productIcon)
            ? productIcon
            : new ContainerIcons(directory).ProductIconPath;
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = ((dynamic)shell!).CreateShortcut(MainShortcutPath);
            ((dynamic)link).TargetPath = executable; ((dynamic)link).Arguments = "";
            ((dynamic)link).WorkingDirectory = Path.GetDirectoryName(executable)!;
            ((dynamic)link).Description = "AppDock"; ((dynamic)link).IconLocation = iconPath + ",0"; ((dynamic)link).Save();
        }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
        TaskbarIdentity.SetShortcut(MainShortcutPath, WindowsJumpLists.MainIdentity);
        NotifyShellUpdate(MainShortcutPath);
    }

    public void Sync(Configuration config)
    {
        Directory.CreateDirectory(DirectoryPath);
        // Retain inactive shortcuts so existing user pins never lose their target or icon.
        if (!config.Settings.EnableTaskbar || config.Settings.LayoutMode != LayoutMode.MultipleAppBoxes) return;
        foreach (var folder in config.Folders.Where(f => f.IsActive || File.Exists(ShortcutPath(f.Id)))) Update(folder);
        NotifyShellUpdate();
    }

    public void Update(AppFolder folder)
    {
        Directory.CreateDirectory(DirectoryPath);
        var icon = new ContainerIcons(directory).PersistentPath(folder.Icon);
        var primaryPath = ShortcutPath(folder.Id);
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = ((dynamic)shell!).CreateShortcut(primaryPath);
            ((dynamic)link).TargetPath = executable;
            ((dynamic)link).Arguments = "--container " + folder.Id;
            ((dynamic)link).WorkingDirectory = Path.GetDirectoryName(executable)!;
            ((dynamic)link).Description = "AppDock — " + folder.Name;
            ((dynamic)link).IconLocation = icon + ",0";
            ((dynamic)link).Save();
        }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); if (shell is not null) Marshal.FinalReleaseComObject(shell); }

        TaskbarIdentity.SetShortcut(primaryPath, Identity(folder.Id));
        NotifyShellUpdate(primaryPath);

        // Also update any pinned shortcuts Windows created in User Pinned / ImplicitAppShortcuts
        UpdatePinnedTaskbarShortcuts(folder.Id, icon, folder.Name);
    }

    public bool PinMainToTaskbar()
    {
        UpdateMain();
        return PinShortcutToTaskbar(MainShortcutPath, "AppDock", WindowsJumpLists.MainIdentity);
    }

    public bool PinToTaskbar(AppFolder folder)
    {
        Update(folder);
        return PinShortcutToTaskbar(ShortcutPath(folder.Id), SanitizeLinkName(folder.Name), Identity(folder.Id));
    }

    private bool PinShortcutToTaskbar(string sourceLnk, string linkName, string identity)
    {
        if (!File.Exists(sourceLnk)) return false;
        object? listObj = null, linkObj = null;
        IntPtr pPersist = IntPtr.Zero, pShellLink = IntPtr.Zero;
        try
        {
            var pinnedDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            Directory.CreateDirectory(pinnedDir);

            var pinnedPath = Path.Combine(pinnedDir, linkName + ".lnk");
            var clsidPinnedList = new Guid("90AA3A4E-1CBA-4233-B8BB-535773D48449");
            var typePinnedList = Type.GetTypeFromCLSID(clsidPinnedList);
            if (typePinnedList is null) return false;

            listObj = Activator.CreateInstance(typePinnedList);
            if (listObj is not IPinnedList3 pinnedList) return false;

            // Check if already pinned in TaskBar
            if (File.Exists(pinnedPath))
            {
                var pidlExisting = ILCreateFromPathW(pinnedPath);
                if (pidlExisting != IntPtr.Zero)
                {
                    try
                    {
                        if (pinnedList.IsPinned(pidlExisting) == 0)
                        {
                            pinnedList.ItemChangeNotify(pidlExisting, pidlExisting);
                            NotifyShellUpdate(pinnedPath);
                            return true;
                        }
                    }
                    finally { ILFree(pidlExisting); }
                }
            }

            var clsidShellLink = new Guid("00021401-0000-0000-C000-000000000046");
            var typeShellLink = Type.GetTypeFromCLSID(clsidShellLink);
            if (typeShellLink is null) return false;

            linkObj = Activator.CreateInstance(typeShellLink)!;
            ((IPersistFile)linkObj).Load(sourceLnk, 0);

            pPersist = Marshal.GetComInterfaceForObject(linkObj, typeof(IPersistFile));
            var iidShellLinkW = new Guid("000214F9-0000-0000-C000-000000000046");
            if (Marshal.QueryInterface(pPersist, in iidShellLinkW, out pShellLink) != 0 || pShellLink == IntPtr.Zero)
                return false;

            int hr = pinnedList.PinShellLink(linkName, pShellLink);
            if (File.Exists(pinnedPath))
            {
                TaskbarIdentity.SetShortcut(pinnedPath, identity);
                NotifyPinnedItemChanged(pinnedPath);
                NotifyShellUpdate(pinnedPath);
            }
            return hr == 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
        finally
        {
            if (pShellLink != IntPtr.Zero) Marshal.Release(pShellLink);
            if (pPersist != IntPtr.Zero) Marshal.Release(pPersist);
            if (linkObj is not null) Marshal.FinalReleaseComObject(linkObj);
            if (listObj is not null) Marshal.FinalReleaseComObject(listObj);
        }
    }

    public bool UnpinMainFromTaskbar()
    {
        return UnpinShortcutFromTaskbar(MainShortcutPath, "AppDock", WindowsJumpLists.MainIdentity, requireEmptyArgs: true);
    }

    public bool UnpinFromTaskbar(AppFolder folder)
    {
        var targetArg1 = "--container " + folder.Id.ToString();
        var targetArg2 = "--container " + folder.Id.ToString("N");
        return UnpinShortcutFromTaskbar(ShortcutPath(folder.Id), SanitizeLinkName(folder.Name), Identity(folder.Id),
            argsFilter: args => args.Contains(targetArg1, StringComparison.OrdinalIgnoreCase) || args.Contains(targetArg2, StringComparison.OrdinalIgnoreCase));
    }

    private bool UnpinShortcutFromTaskbar(string sourceLnk, string linkName, string identity, bool requireEmptyArgs = false, Func<string, bool>? argsFilter = null)
    {
        var unpinned = false;
        object? listObj = null;
        try
        {
            var clsidPinnedList = new Guid("90AA3A4E-1CBA-4233-B8BB-535773D48449");
            var typePinnedList = Type.GetTypeFromCLSID(clsidPinnedList);
            var pinnedList = typePinnedList is not null ? Activator.CreateInstance(typePinnedList) as IPinnedList3 : null;
            listObj = pinnedList;

            void TryUnpinPidl(IntPtr pidl)
            {
                if (pidl == IntPtr.Zero || pinnedList is null) return;
                if (pinnedList.GetPinnedItem(pidl, out var resolved) == 0 && resolved != IntPtr.Zero)
                {
                    try
                    {
                        if (pinnedList.Modify(resolved, IntPtr.Zero, 18) == 0 ||
                            pinnedList.LegacyModify(resolved, IntPtr.Zero) == 0 ||
                            pinnedList.Modify(resolved, IntPtr.Zero, 0) == 0)
                            unpinned = true;
                    }
                    finally { ILFree(resolved); }
                }
                if (pinnedList.Modify(pidl, IntPtr.Zero, 18) == 0 ||
                    pinnedList.LegacyModify(pidl, IntPtr.Zero) == 0 ||
                    pinnedList.Modify(pidl, IntPtr.Zero, 0) == 0)
                    unpinned = true;
            }

            if (pinnedList is not null && pinnedList.GetPinnedItemForAppID(identity, out var appIdPidl) == 0 && appIdPidl != IntPtr.Zero)
            {
                try { TryUnpinPidl(appIdPidl); }
                finally { ILFree(appIdPidl); }
            }

            if (File.Exists(sourceLnk))
            {
                var sourcePidl = ILCreateFromPathW(sourceLnk);
                if (sourcePidl != IntPtr.Zero)
                {
                    try { TryUnpinPidl(sourcePidl); }
                    finally { ILFree(sourcePidl); }
                }
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var pinnedDir = Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            var safeName = linkName + ".lnk";
            object? shell = null;

            try
            {
                if (Directory.Exists(pinnedDir))
                {
                    foreach (var file in Directory.EnumerateFiles(pinnedDir, "*.lnk", SearchOption.TopDirectoryOnly).ToList())
                    {
                        var matches = string.Equals(Path.GetFileName(file), safeName, StringComparison.OrdinalIgnoreCase);
                        if (!matches && (requireEmptyArgs || argsFilter is not null))
                        {
                            try
                            {
                                shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                                var link = ((dynamic)shell!).CreateShortcut(file);
                                try
                                {
                                    string target = (string)(((dynamic)link).TargetPath ?? "");
                                    string args = (string)(((dynamic)link).Arguments ?? "");
                                    if (string.Equals(target, executable, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (requireEmptyArgs && string.IsNullOrWhiteSpace(args))
                                            matches = true;
                                        else if (argsFilter is not null && argsFilter(args))
                                            matches = true;
                                    }
                                }
                                finally { Marshal.FinalReleaseComObject(link); }
                            }
                            catch (Exception ex) when (ex is not OutOfMemoryException) { }
                        }

                        if (!matches) continue;

                        var pidl = ILCreateFromPathW(file);
                        if (pidl != IntPtr.Zero)
                        {
                            try { TryUnpinPidl(pidl); }
                            finally { ILFree(pidl); }
                        }

                        if (File.Exists(file))
                        {
                            try { File.Delete(file); unpinned = true; }
                            catch (Exception ex) when (ex is not OutOfMemoryException) { }
                        }
                        NotifyShellUpdate(file);
                    }
                }
            }
            finally
            {
                if (shell is not null) Marshal.FinalReleaseComObject(shell);
            }

            pinnedList?.UpdateForRemovedItemsAsNecessary();
            if (unpinned) NotifyShellUpdate();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        finally
        {
            if (listObj is not null) Marshal.FinalReleaseComObject(listObj);
        }
        return unpinned;
    }

    public static string SanitizeLinkName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (cleaned.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^4].Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "AppDock Container" : cleaned;
    }

    public Dictionary<string, Guid?> GetPinnedTaskbarTargets(Configuration config)
    {
        var result = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppDock"] = null
        };

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var taskbarRoots = new[]
            {
                Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
                Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\ImplicitAppShortcuts")
            };

            var resolver = new WindowsShortcutResolver();
            var exeName = Path.GetFileName(executable);

            foreach (var root in taskbarRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    try
                    {
                        var resolved = resolver.Resolve(file);
                        var targetExe = Path.GetFileName(resolved.FileName);
                        bool isAppDock = string.Equals(resolved.FileName, executable, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(targetExe, exeName, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(targetExe, "AppDock.exe", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(targetExe, "AppBox.App.exe", StringComparison.OrdinalIgnoreCase);
                        if (!isAppDock) continue;

                        var linkName = Path.GetFileNameWithoutExtension(file).Trim();
                        var args = resolved.Arguments?.Trim() ?? "";
                        var shortcutAppId = TaskbarIdentity.GetShortcut(file);
                        if (string.IsNullOrEmpty(args))
                        {
                            if (!string.IsNullOrEmpty(linkName))
                                result[linkName] = null;
                            if (!string.IsNullOrEmpty(shortcutAppId))
                            {
                                result[shortcutAppId] = null;
                                result["Appid: " + shortcutAppId] = null;
                            }
                        }
                        else
                        {
                            foreach (var folder in config.Folders)
                            {
                                var arg1 = "--container " + folder.Id.ToString();
                                var arg2 = "--container " + folder.Id.ToString("N");
                                var arg3 = "--folder " + folder.Id.ToString();
                                if (args.Contains(arg1, StringComparison.OrdinalIgnoreCase) ||
                                    args.Contains(arg2, StringComparison.OrdinalIgnoreCase) ||
                                    args.Contains(arg3, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!string.IsNullOrEmpty(linkName))
                                        result[linkName] = folder.Id;
                                    if (!string.IsNullOrEmpty(shortcutAppId))
                                    {
                                        result[shortcutAppId] = folder.Id;
                                        result["Appid: " + shortcutAppId] = folder.Id;
                                    }
                                    result[folder.Name] = folder.Id;
                                    result[SanitizeLinkName(folder.Name)] = folder.Id;
                                    result[folder.Id.ToString("N")] = folder.Id;
                                    result[folder.Id.ToString()] = folder.Id;
                                    result["AppDock — " + folder.Name] = folder.Id;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { }
                }
            }

            foreach (var folder in config.Folders.Where(f => f.IsActive))
            {
                result.TryAdd(folder.Name, folder.Id);
                result.TryAdd(SanitizeLinkName(folder.Name), folder.Id);
                result.TryAdd(folder.Id.ToString("N"), folder.Id);
                result.TryAdd(folder.Id.ToString(), folder.Id);
                result.TryAdd("AppDock — " + folder.Name, folder.Id);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }

        return result;
    }

    private void UpdatePinnedTaskbarShortcuts(Guid id, string icon, string name)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var taskbarRoots = new[]
            {
                Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
                Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\ImplicitAppShortcuts")
            };

            var targetArg1 = "--container " + id.ToString();
            var targetArg2 = "--container " + id.ToString("N");
            object? shell = null;

            try
            {
                foreach (var root in taskbarRoots)
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        try
                        {
                            shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                            var link = ((dynamic)shell!).CreateShortcut(file);
                            try
                            {
                                string args = (string)(((dynamic)link).Arguments ?? "");
                                if (!string.IsNullOrEmpty(args) && (
                                    args.Contains(targetArg1, StringComparison.OrdinalIgnoreCase) ||
                                    args.Contains(targetArg2, StringComparison.OrdinalIgnoreCase)))
                                {
                                    ((dynamic)link).TargetPath = executable;
                                    ((dynamic)link).IconLocation = icon + ",0";
                                    ((dynamic)link).Description = "AppDock — " + name;
                                    ((dynamic)link).Save();
                                    TaskbarIdentity.SetShortcut(file, Identity(id));
                                    NotifyPinnedItemChanged(file);
                                    NotifyShellUpdate(file);
                                }
                            }
                            finally { Marshal.FinalReleaseComObject(link); }
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException) { }
                    }
                }
            }
            finally
            {
                if (shell is not null) Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    private static void NotifyPinnedItemChanged(string file)
    {
        object? listObj = null;
        var pidl = IntPtr.Zero;
        try
        {
            pidl = ILCreateFromPathW(file);
            if (pidl == IntPtr.Zero) return;
            var clsidPinnedList = new Guid("90AA3A4E-1CBA-4233-B8BB-535773D48449");
            var typePinnedList = Type.GetTypeFromCLSID(clsidPinnedList);
            if (typePinnedList is null) return;
            listObj = Activator.CreateInstance(typePinnedList);
            if (listObj is IPinnedList3 pinnedList)
                pinnedList.ItemChangeNotify(pidl, pidl);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        finally
        {
            if (pidl != IntPtr.Zero) ILFree(pidl);
            if (listObj is not null) Marshal.FinalReleaseComObject(listObj);
        }
    }

    public void ApplyWindow(Window window, AppFolder? folder)
    {
        var icons = new ContainerIcons(directory);
        window.Title = folder is null ? "AppDock" : folder.Name + " — AppDock";
        window.Icon = folder is null ? ContainerIcons.ProductIcon : icons.Get(folder.Icon);
        var icon = folder is null ? icons.ProductIconPath : icons.PersistentPath(folder.Icon);
        try
        {
            TaskbarIdentity.SetWindow(window, folder is null ? "AppBox.Desktop" : Identity(folder.Id),
                "\"" + executable + "\"" + (folder is null ? "" : " --container " + folder.Id), icon, window.Title);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string pszPath);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNE_UPDATEITEM = 0x00002000;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint SHCNF_PATHW = 0x0005;

    [ComImport, Guid("0DD79AE2-D156-45D4-9EEB-3B549769E940"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPinnedList3
    {
        [PreserveSig] int EnumObjects(out IntPtr ppv);
        [PreserveSig] int GetPinnableInfo(IntPtr pdtobj, int pinnableFlag, out IntPtr ppv, out IntPtr ppv2, out IntPtr ppsz, out int pcch);
        [PreserveSig] int IsPinnable(IntPtr pdtobj, int pinnableFlag);
        [PreserveSig] int Resolve(IntPtr hwnd, uint flags, IntPtr pidl, out IntPtr ppidl);
        [PreserveSig] int LegacyModify(IntPtr unpin, IntPtr pin);
        [PreserveSig] int GetChangeCount(out uint pcc);
        [PreserveSig] int IsPinned(IntPtr pidl);
        [PreserveSig] int GetPinnedItem(IntPtr pidl, out IntPtr ppidl);
        [PreserveSig] int GetAppIDForPinnedItem(IntPtr pidl, out IntPtr ppszAppID);
        [PreserveSig] int ItemChangeNotify(IntPtr pidlOld, IntPtr pidlNew);
        [PreserveSig] int UpdateForRemovedItemsAsNecessary();
        [PreserveSig] int PinShellLink([MarshalAs(UnmanagedType.LPWStr)] string pszLink, IntPtr psl);
        [PreserveSig] int GetPinnedItemForAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID, out IntPtr ppidl);
        [PreserveSig] int Modify(IntPtr unpin, IntPtr pin, int caller);
    }

    public static void NotifyShellUpdate(string? path = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(path))
            {
                var p = Marshal.StringToHGlobalUni(path);
                try { SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, p, IntPtr.Zero); }
                finally { Marshal.FreeHGlobal(p); }
            }
            else
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
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
    public static string? GetShortcut(string path)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0 /* GPS_DEFAULT */, ref iid, out var store);
            try
            {
                var key = new Key(5);
                store.GetValue(ref key, out var val);
                if (val.Type == 31 && val.Text != IntPtr.Zero)
                {
                    try { return Marshal.PtrToStringUni(val.Text); }
                    finally { Marshal.FreeCoTaskMem(val.Text); }
                }
            }
            finally { Marshal.FinalReleaseComObject(store); }
        }
        catch { }
        return null;
    }
    public static void SetWindow(Window window, string identity, string command, string icon, string title)
    {
        var iid = typeof(IPropertyStore).GUID; SHGetPropertyStoreForWindow(new WindowInteropHelper(window).EnsureHandle(), ref iid, out var store);
        try { Set(store, 2, command); Set(store, 3, icon + ",0"); Set(store, 4, title); Set(store, 5, identity); store.Commit(); } finally { Marshal.FinalReleaseComObject(store); }
    }
}
