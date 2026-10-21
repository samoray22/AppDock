using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace AppBox.Infrastructure;

public static class ShellDropReader
{
    public const string ShellIdListFormat = "Shell IDList Array";
    public static bool HasCandidates(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop, false))
            return data.GetData(DataFormats.FileDrop, false) is string[] files && files.Any(ApplicationImportService.IsSupportedFile);
        return data.GetDataPresent(ShellIdListFormat, false);
    }
    public static IReadOnlyList<string> Read(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop, false) && data.GetData(DataFormats.FileDrop, false) is string[] files)
            return files.Length <= 256 ? files : throw new ArgumentException("Drop at most 256 applications at a time.");
        if (data.GetDataPresent(ShellIdListFormat, false) && data.GetData(ShellIdListFormat, false) is Stream stream)
        {
            if (!stream.CanSeek || stream.Length > 1024 * 1024) throw new ArgumentException("This Shell drop cannot be read safely.");
            stream.Position = 0; var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
            return ReadShellIdList(bytes);
        }
        throw new ArgumentException("This drag source does not supply supported application files or Shell references. Try Browse files or Start menu / installed in Settings.");
    }

    [DllImport("shell32.dll")] private static extern IntPtr ILCombine(IntPtr parent, IntPtr child);
    [DllImport("shell32.dll", PreserveSig = false)] private static extern void SHGetNameFromIDList(IntPtr pidl, uint nameType, out IntPtr name);

    public static IReadOnlyList<string> ReadShellIdList(byte[] bytes)
    {
        // Validate every offset and item boundary before passing a PIDL to the Shell.
        if (bytes.Length < 12 || bytes.Length > 1024 * 1024) throw new ArgumentException("Invalid Shell item data.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (count is 0 or > 256 || 4L + (count + 1L) * 4 > bytes.Length) throw new ArgumentException("Invalid Shell item count.");
        var offsets = new int[count + 1];
        for (var i = 0; i < offsets.Length; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 + i * 4));
            if (offset < 4L + offsets.Length * 4L || offset > bytes.Length - 2) throw new ArgumentException("Invalid Shell item offset.");
            offsets[i] = (int)offset;
            var pos = (int)offset;
            while (true)
            {
                if (pos > bytes.Length - 2) throw new ArgumentException("Incomplete Shell item.");
                var size = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pos));
                if (size == 0) break;
                if (size < 2 || size > bytes.Length - pos) throw new ArgumentException("Invalid Shell item length.");
                pos += size;
            }
        }
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var address = pinned.AddrOfPinnedObject(); var items = new List<string>();
            foreach (var offset in offsets.Skip(1))
            {
                var combined = ILCombine(address + offsets[0], address + offset);
                if (combined == IntPtr.Zero) throw new ArgumentException("Windows could not resolve this Shell item.");
                IntPtr name = IntPtr.Zero;
                try { SHGetNameFromIDList(combined, 0x80028000 /* DESKTOPABSOLUTEPARSING */, out name); items.Add(Marshal.PtrToStringUni(name) ?? ""); }
                finally { Marshal.FreeCoTaskMem(name); Marshal.FreeCoTaskMem(combined); }
            }
            return items;
        }
        finally { pinned.Free(); }
    }
}
