using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AppBox.Core;

namespace AppBox.Infrastructure;

// ICustomDestinationList supports an explicit AppID per list, unlike WPF's process-bound JumpList.
public static class WindowsJumpLists
{
    public const string MainIdentity = "AppBox.Desktop";
    public static void Publish(string identity, string executable, string icon, Guid? container)
    {
        object? nativeList = null, collection = null, removed = null; var begun = false;
        var links = new List<object>();
        try
        {
            nativeList = Activator.CreateInstance(Type.GetTypeFromCLSID(new("77f10cf0-3db5-4966-b520-b7c54fd35ed6"))!);
            var list = (ICustomDestinationList)nativeList!;
            list.SetAppID(identity);
            var iid = typeof(IObjectArray).GUID;
            list.BeginList(out _, ref iid, out removed); begun = true;
            collection = Activator.CreateInstance(Type.GetTypeFromCLSID(new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a"))!);
            var objects = (IObjectCollection)collection!;
            foreach (var task in TaskbarCommands.ForContainer(container))
            {
                var nativeLink = Activator.CreateInstance(Type.GetTypeFromCLSID(new("00021401-0000-0000-C000-000000000046"))!)!;
                links.Add(nativeLink); var link = (IShellLink)nativeLink;
                link.SetPath(executable); link.SetArguments(task.Request.Arguments);
                link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
                link.SetIconLocation(icon, 0); link.SetDescription(task.Title);
                var properties = (IPropertyStore)nativeLink;
                var key = new PropertyKey { Format = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), Id = 2 };
                var value = new PropVariant { Type = 31, Text = Marshal.StringToCoTaskMemUni(task.Title) };
                try { properties.SetValue(ref key, ref value); properties.Commit(); } finally { Marshal.FreeCoTaskMem(value.Text); }
                objects.AddObject(nativeLink);
            }
            list.AddUserTasks((IObjectArray)collection!); list.CommitList(); begun = false;
        }
        finally
        {
            if (begun && nativeList is ICustomDestinationList unfinished) { try { unfinished.AbortList(); } catch (COMException) { } }
            foreach (var link in links) Marshal.FinalReleaseComObject(link);
            if (removed is not null) Marshal.FinalReleaseComObject(removed);
            if (collection is not null) Marshal.FinalReleaseComObject(collection);
            if (nativeList is not null) Marshal.FinalReleaseComObject(nativeList);
        }
    }
    public static void DeleteDiagnosticList(string identity)
    {
        if (!identity.StartsWith("AppDock.Diagnostics.", StringComparison.Ordinal)) throw new ArgumentException("Only isolated diagnostic lists can be removed here.");
        var value = Activator.CreateInstance(Type.GetTypeFromCLSID(new("77f10cf0-3db5-4966-b520-b7c54fd35ed6"))!)!;
        try { ((ICustomDestinationList)value).DeleteList(identity); } finally { Marshal.FinalReleaseComObject(value); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Text; }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key); void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value); void Commit();
    }
    [ComImport, Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray { void GetCount(out uint count); void GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value); }
    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint count); void GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object value); void AddFromArray(IObjectArray array); void RemoveObjectAt(uint index); void Clear();
    }
    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
        void BeginList(out uint slots, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray array); void AppendKnownCategory(uint category);
        void AddUserTasks(IObjectArray tasks); void CommitList();
        void GetRemovedDestinations(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string id); void AbortList();
    }
    [ComImport, Guid("000214f9-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, IntPtr data, uint flags);
        void GetIDList(out IntPtr list); void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int length); void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int length); void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int length); void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short key); void SetHotkey(short key); void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int length, out int index); void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved); void Resolve(IntPtr window, uint flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
