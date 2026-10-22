using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppBox.Core;

namespace AppBox.Infrastructure;

// All generated icons live alongside settings, never in temporary taskbar resources.
public sealed class ContainerIcons(string directory)
{
    public static readonly string[] Categories = ["Development", "Office", "Graphics", "Utilities", "Multimedia", "Games", "Internet", "General Applications"];
    private static readonly string[] Glyphs = ["{ }", "≡", "✎", "⚙", "♫", "✚", "◎", "▦"];
    public string DirectoryPath => Path.Combine(directory, "Icons");
    public ContainerIcon Import(string file)
    {
        var image = Load(file); // Fully decode before publishing a resource or changing the model.
        var bytes = Encode(image);
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, Convert.ToHexString(SHA256.HashData(bytes)) + ".ico");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return new() { Kind = ContainerIconKind.Custom, ResourcePath = path };
    }
    public BitmapSource Get(ContainerIcon? icon)
    {
        try { if (icon?.Kind == ContainerIconKind.Custom && icon.ResourcePath is { } path) return Load(path); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* Keep the choice so a restored resource can recover. */ }
        return BuiltIn(icon?.Kind == ContainerIconKind.BuiltIn ? icon.BuiltIn : "General Applications");
    }
    public string PersistentPath(ContainerIcon? icon)
    {
        var bytes = Encode(Get(icon));
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, Convert.ToHexString(SHA256.HashData(bytes)) + ".ico");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return path;
    }
    public static BitmapSource Load(string file)
    {
        if (!Path.IsPathFullyQualified(file) || file.StartsWith(@"\\") || !File.Exists(file)) throw new ArgumentException("Choose an existing local icon file.");
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension is ".exe" or ".lnk")
        {
            SHGetFileInfo(file, 0, out var info, (uint)Marshal.SizeOf<FileInfo>(), 0x100 /* SHGFI_ICON, large */);
            if (info.Icon == IntPtr.Zero) throw new ArgumentException("Windows could not extract this icon.");
            try { var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); return image; }
            finally { DestroyIcon(info.Icon); }
        }
        if (extension is not (".ico" or ".png")) throw new ArgumentException("Choose an .ico, .png, .exe or .lnk file.");
        using var stream = File.OpenRead(file);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096) throw new ArgumentException("Choose an image at most 4096 pixels wide and high.");
        frame.Freeze(); return frame;
    }
    public static BitmapSource BuiltIn(string category)
    {
        var index = Array.IndexOf(Categories, category); if (index < 0) index = 7;
        var colors = new[] { "#5664D8", "#237BBD", "#AD4AA0", "#627485", "#B34C68", "#6D55B0", "#238579", "#386CBA" };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(colors[index])!;
            dc.DrawRoundedRectangle(brush, null, new Rect(4, 12, 120, 104), 22, 22);
            dc.DrawRoundedRectangle(brush, null, new Rect(12, 4, 52, 30), 10, 10);
            var text = new FormattedText(Glyphs[index], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol"), 48, Brushes.White, 1);
            dc.DrawText(text, new Point((128 - text.Width) / 2, (128 - text.Height) / 2 + 4));
        }
        var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    public static byte[] Encode(BitmapSource image)
    {
        // PNG-backed ICO frames retain alpha at all Windows taskbar sizes.
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var frames = new List<byte[]>();
        foreach (var size in sizes)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var scale = Math.Min(size / (double)image.PixelWidth, size / (double)image.PixelHeight);
                var width = image.PixelWidth * scale; var height = image.PixelHeight * scale;
                dc.DrawImage(image, new Rect((size - width) / 2, (size - height) / 2, width, height));
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new MemoryStream(); encoder.Save(png); frames.Add(png.ToArray());
        }
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        return output.ToArray();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo { public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string file, uint attributes, out FileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
