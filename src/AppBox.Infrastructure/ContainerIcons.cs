using System.Collections.Concurrent;
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
    public static readonly string[] Categories =
    [
        "General Applications",
        "AppDock Box",
        "Development",
        "Office",
        "Graphics",
        "Utilities",
        "Multimedia",
        "Games",
        "Internet",
        "3D & CAD",
        "Books",
        "Business",
        "Calendar",
        "Cloud",
        "Communication",
        "Contacts",
        "Cooking",
        "Databases",
        "Design",
        "Education",
        "Email",
        "Favorites",
        "File Tools",
        "Finance",
        "Fitness",
        "Health",
        "Home",
        "Knowledge",
        "Music",
        "Photography",
        "Productivity",
        "Scripting",
        "Security",
        "Shopping",
        "Social",
        "System",
        "Tasks",
        "Terminal",
        "Travel",
        "Video",
        "Virtualization"
    ];

    private static readonly Dictionary<string, string> CategoryResources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["General Applications"] = "ContainerIcons/general.png",
        ["General"] = "ContainerIcons/general.png",
        ["AppDock Box"] = "AppDock_128x128.png",
        ["Development"] = "ContainerIcons/development.png",
        ["Office"] = "ContainerIcons/office.png",
        ["Graphics"] = "ContainerIcons/graphics.png",
        ["Utilities"] = "ContainerIcons/utilities.png",
        ["Multimedia"] = "ContainerIcons/multimedia.png",
        ["Games"] = "ContainerIcons/games.png",
        ["Internet"] = "ContainerIcons/internet.png",
        ["3D & CAD"] = "ContainerIcons/3d_cad.png",
        ["3d_cad"] = "ContainerIcons/3d_cad.png",
        ["Books"] = "ContainerIcons/books.png",
        ["Business"] = "ContainerIcons/business.png",
        ["Calendar"] = "ContainerIcons/calendar.png",
        ["Cloud"] = "ContainerIcons/cloud.png",
        ["Cloud & Sync"] = "ContainerIcons/cloud.png",
        ["Communication"] = "ContainerIcons/communication.png",
        ["Contacts"] = "ContainerIcons/contacts.png",
        ["Cooking"] = "ContainerIcons/cooking.png",
        ["Databases"] = "ContainerIcons/databases.png",
        ["AI & Data"] = "ContainerIcons/databases.png",
        ["Design"] = "ContainerIcons/design.png",
        ["Education"] = "ContainerIcons/education.png",
        ["Email"] = "ContainerIcons/email.png",
        ["Favorites"] = "ContainerIcons/favorites.png",
        ["File Tools"] = "ContainerIcons/file_tools.png",
        ["file_tools"] = "ContainerIcons/file_tools.png",
        ["Finance"] = "ContainerIcons/finance.png",
        ["Fitness"] = "ContainerIcons/fitness.png",
        ["Health"] = "ContainerIcons/health.png",
        ["Home"] = "ContainerIcons/home.png",
        ["Knowledge"] = "ContainerIcons/knowledge.png",
        ["Music"] = "ContainerIcons/music.png",
        ["Photography"] = "ContainerIcons/photography.png",
        ["Productivity"] = "ContainerIcons/productivity.png",
        ["Scripting"] = "ContainerIcons/scripting.png",
        ["Security"] = "ContainerIcons/security.png",
        ["Shopping"] = "ContainerIcons/shopping.png",
        ["Social"] = "ContainerIcons/social.png",
        ["Social & Chat"] = "ContainerIcons/social.png",
        ["System"] = "ContainerIcons/system.png",
        ["Tasks"] = "ContainerIcons/tasks.png",
        ["Terminal"] = "ContainerIcons/terminal.png",
        ["Terminal & CLI"] = "ContainerIcons/terminal.png",
        ["Travel"] = "ContainerIcons/travel.png",
        ["Video"] = "ContainerIcons/video.png",
        ["Virtualization"] = "ContainerIcons/virtualization.png"
    };

    private static readonly ConcurrentDictionary<string, BitmapSource> BuiltInCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (long WriteTicks, BitmapSource Image)> FileCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte[]> EncodedBuiltInCache = new(StringComparer.OrdinalIgnoreCase);

    private string BaseDirectory => string.IsNullOrWhiteSpace(directory) ? AppPaths.ActiveDataDirectory : directory;

    public string DirectoryPath => Path.Combine(BaseDirectory, "Icons");

    public string ProductIconPath
    {
        get
        {
            var exeDirIco = Path.Combine(AppPaths.ExecutableDirectory, "AppDock.ico");
            if (File.Exists(exeDirIco) && new System.IO.FileInfo(exeDirIco).Length > 0) return exeDirIco;

            var localIco = Path.Combine(AppContext.BaseDirectory, "AppDock.ico");
            if (File.Exists(localIco) && new System.IO.FileInfo(localIco).Length > 0) return localIco;

            Directory.CreateDirectory(DirectoryPath);
            var persistentIco = Path.Combine(DirectoryPath, "AppDock.ico");
            if (File.Exists(persistentIco) && new System.IO.FileInfo(persistentIco).Length > 0)
                return persistentIco;

            try
            {
                using var stream = typeof(ContainerIcons).Assembly.GetManifestResourceStream("AppDock.ico");
                if (stream is not null)
                {
                    using var fileStream = File.Create(persistentIco);
                    stream.CopyTo(fileStream);
                    return persistentIco;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }

            var bytes = EncodedBuiltInCache.GetOrAdd("AppDock Box", _ => Encode(BuiltIn("AppDock Box")));
            File.WriteAllBytes(persistentIco, bytes);
            return persistentIco;
        }
    }

    public static BitmapSource ProductIcon
    {
        get
        {
            var exeDirIco = Path.Combine(AppPaths.ExecutableDirectory, "AppDock.ico");
            if (File.Exists(exeDirIco))
            {
                try { return Load(exeDirIco); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
            var localIco = Path.Combine(AppContext.BaseDirectory, "AppDock.ico");
            if (File.Exists(localIco))
            {
                try { return Load(localIco); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
            return BuiltIn("AppDock Box");
        }
    }

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
        if (icon is null) return ProductIcon;
        try
        {
            if (icon.Kind == ContainerIconKind.Custom && icon.ResourcePath is { } path && !string.IsNullOrWhiteSpace(path))
            {
                if (File.Exists(path)) return Load(path);
                var fileName = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(fileName))
                {
                    var localCandidate = Path.Combine(DirectoryPath, fileName);
                    if (File.Exists(localCandidate)) return Load(localCandidate);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* Keep the choice so a restored resource can recover. */ }
        return BuiltIn(icon.Kind == ContainerIconKind.BuiltIn ? icon.BuiltIn : "General Applications");
    }

    public string PersistentPath(ContainerIcon? icon)
    {
        if (icon is null) return ProductIconPath;
        byte[] bytes;
        if (icon.Kind != ContainerIconKind.Custom)
        {
            var cat = icon.Kind == ContainerIconKind.BuiltIn ? icon.BuiltIn : "General Applications";
            bytes = EncodedBuiltInCache.GetOrAdd(cat ?? "General Applications", c => Encode(BuiltIn(c)));
        }
        else
        {
            bytes = Encode(Get(icon));
        }
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, Convert.ToHexString(SHA256.HashData(bytes)) + ".ico");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return path;
    }

    public static BitmapSource Load(string file)
    {
        if (!Path.IsPathFullyQualified(file) || file.StartsWith(@"\\") || !File.Exists(file)) throw new ArgumentException("Choose an existing local icon file.");
        var fullPath = Path.GetFullPath(file);
        var ticks = File.GetLastWriteTimeUtc(fullPath).Ticks;
        if (FileCache.TryGetValue(fullPath, out var cached) && cached.WriteTicks == ticks)
            return cached.Image;

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is ".exe" or ".lnk")
        {
            SHGetFileInfo(fullPath, 0, out var info, (uint)Marshal.SizeOf<FileInfo>(), 0x100 /* SHGFI_ICON, large */);
            if (info.Icon == IntPtr.Zero) throw new ArgumentException("Windows could not extract this icon.");
            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                FileCache[fullPath] = (ticks, image);
                return image;
            }
            finally { DestroyIcon(info.Icon); }
        }
        if (extension is not (".ico" or ".png" or ".jpg" or ".jpeg" or ".bmp"))
            throw new ArgumentException("Choose an .ico, .png, .jpg, .bmp, .exe or .lnk file.");
        using var stream = File.OpenRead(fullPath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096) throw new ArgumentException("Choose an image at most 4096 pixels wide and high.");
        frame.Freeze();
        FileCache[fullPath] = (ticks, frame);
        return frame;
    }

    public static BitmapSource BuiltIn(string category)
    {
        var resourceName = CategoryResources.TryGetValue(category ?? "", out var mapped)
            ? mapped
            : "ContainerIcons/general.png";

        return BuiltInCache.GetOrAdd(resourceName, res =>
        {
            using var stream = typeof(ContainerIcons).Assembly.GetManifestResourceStream(res)
                ?? typeof(ContainerIcons).Assembly.GetManifestResourceStream("ContainerIcons/general.png");
            if (stream is not null)
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return CleanOuterMatte(decoder.Frames[0]);
            }

            var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
            bitmap.Freeze();
            return bitmap;
        });
    }

    private static BitmapSource CleanOuterMatte(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
        var pixels = new byte[h * stride];
        converted.CopyPixels(pixels, stride, 0);

        var outside = new bool[w * h];
        var queue = new Queue<int>(w * 4);

        bool IsOuterOrWhiteMatte(int idx)
        {
            byte b = pixels[idx], g = pixels[idx + 1], r = pixels[idx + 2], a = pixels[idx + 3];
            if (a < 245) return true;
            byte min = Math.Min(r, Math.Min(g, b));
            byte max = Math.Max(r, Math.Max(g, b));
            return min > 215 && (max - min) < 25;
        }

        void EnqueueBorder(int x, int y)
        {
            int pos = y * w + x;
            if (!outside[pos] && IsOuterOrWhiteMatte(pos * 4))
            {
                outside[pos] = true;
                queue.Enqueue(pos);
            }
        }

        for (int x = 0; x < w; x++) { EnqueueBorder(x, 0); EnqueueBorder(x, h - 1); }
        for (int y = 0; y < h; y++) { EnqueueBorder(0, y); EnqueueBorder(w - 1, y); }

        int[] dx = [-1, 1, 0, 0], dy = [0, 0, -1, 1];
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int cx = cur % w, cy = cur / w;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d], ny = cy + dy[d];
                if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                {
                    int npos = ny * w + nx;
                    if (!outside[npos] && IsOuterOrWhiteMatte(npos * 4))
                    {
                        outside[npos] = true;
                        queue.Enqueue(npos);
                    }
                }
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int pos = y * w + x;
                int idx = pos * 4;
                if (outside[pos])
                {
                    byte b = pixels[idx], g = pixels[idx + 1], r = pixels[idx + 2], a = pixels[idx + 3];
                    double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                    double darkness = Math.Max(0.0, 1.0 - lum);
                    int shadowA = (int)Math.Round((a / 255.0) * darkness * 255.0 * 0.85);
                    pixels[idx] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = (byte)Math.Clamp(shadowA, 0, 140);
                }
                else
                {
                    bool touchesOutside = false;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + dx[d], ny = y + dy[d];
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && outside[ny * w + nx])
                        {
                            touchesOutside = true;
                            break;
                        }
                    }
                    if (touchesOutside)
                    {
                        byte b = pixels[idx], g = pixels[idx + 1], r = pixels[idx + 2];
                        byte min = Math.Min(r, Math.Min(g, b));
                        if (min > 170)
                        {
                            pixels[idx + 3] = (byte)Math.Min((int)pixels[idx + 3], 150);
                        }
                    }
                }
            }
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
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
