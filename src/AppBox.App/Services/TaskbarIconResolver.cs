using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.App.Services;

public static class TaskbarIconResolver
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string TaskbarPinsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string name,
        IntPtr context,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    private static readonly Regex RunningWindowRegex = new(
        @"\s*-\s*(\d+\s+running\s+window[s]?|\d+\s+fen[êe]tre[s]?\s+active[s]?|\d+\s+ge[öo]ffnete[s]?\s+Fenster|\d+\s+ventana[s]?\s+en\s+ejecuci[óo]n|\d+).*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ExtractCleanName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return "";
        string clean = rawName.Trim();
        var match = RunningWindowRegex.Match(clean);
        if (match.Success && match.Index > 0)
        {
            clean = clean[..match.Index].Trim();
        }
        else
        {
            int dashIdx = clean.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx > 0 && dashIdx + 3 < clean.Length)
            {
                string suffix = clean[(dashIdx + 3)..].Trim();
                if (char.IsDigit(suffix.FirstOrDefault()) ||
                    suffix.Contains("running", StringComparison.OrdinalIgnoreCase) ||
                    suffix.Contains("window", StringComparison.OrdinalIgnoreCase))
                {
                    clean = clean[..dashIdx].Trim();
                }
            }
        }
        return clean;
    }

    public static ImageSource? ResolveIcon(
        TaskbarButtonInfo button,
        Configuration config,
        ContainerIcons containerIcons)
    {
        if (button.Icon is not null)
            return button.Icon;

        // 1. AppDock Container Icon
        if (button.IsAppDock)
        {
            if (button.ContainerId is { } id)
            {
                var folder = config.Folders.FirstOrDefault(f => f.Id == id);
                if (folder is not null)
                {
                    try { return containerIcons.Get(folder.Icon); }
                    catch { }
                }
            }
            return ContainerIcons.ProductIcon;
        }

        // 2. Cache check for standard taskbar items
        string cacheKey = !string.IsNullOrEmpty(button.AutomationId) ? button.AutomationId : button.CleanName;
        if (string.IsNullOrWhiteSpace(cacheKey))
            cacheKey = button.Name;

        if (Cache.TryGetValue(cacheKey, out var cached) && cached is not null)
            return cached;

        // 3. Attempt ShellItem from AutomationId (Appid: ...)
        if (!string.IsNullOrEmpty(button.AutomationId) &&
            button.AutomationId.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase))
        {
            string appId = button.AutomationId[6..].Trim();
            var icon = TryLoadShellItemImage("shell:AppsFolder\\" + appId) ??
                       (File.Exists(appId) ? TryLoadShellItemImage(appId) : null);
            if (icon is not null)
            {
                Cache[cacheKey] = icon;
                return icon;
            }
        }

        // 4. Attempt matching pinned shortcut from User Pinned\TaskBar
        if (!string.IsNullOrWhiteSpace(button.CleanName) && Directory.Exists(TaskbarPinsFolder))
        {
            try
            {
                string lnkExact = Path.Combine(TaskbarPinsFolder, button.CleanName + ".lnk");
                if (File.Exists(lnkExact))
                {
                    var icon = TryLoadShellItemImage(lnkExact);
                    if (icon is not null)
                    {
                        Cache[cacheKey] = icon;
                        return icon;
                    }
                }

                // Fuzzy search pinned shortcuts
                var files = Directory.GetFiles(TaskbarPinsFolder, "*.lnk");
                foreach (var file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (string.Equals(fileName, button.CleanName, StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains(button.CleanName, StringComparison.OrdinalIgnoreCase) ||
                        button.CleanName.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        var icon = TryLoadShellItemImage(file);
                        if (icon is not null)
                        {
                            Cache[cacheKey] = icon;
                            return icon;
                        }
                    }
                }
            }
            catch { }
        }

        // 5. Fallback: High quality screen capture of button icon center
        if (button.Bounds.Width > 10 && button.Bounds.Height > 10)
        {
            var captured = CaptureButtonCenter(button.Bounds);
            if (captured is not null)
            {
                Cache[cacheKey] = captured;
                return captured;
            }
        }

        return null;
    }

    public static void ClearCache()
    {
        Cache.Clear();
    }

    private static ImageSource? TryLoadShellItemImage(string targetPath, int pixelSize = 64)
    {
        IShellItemImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(targetPath, IntPtr.Zero, ref iid, out factory);
            if (factory is null) return null;

            factory.GetImage(new NativeSize { Width = pixelSize, Height = pixelSize }, 0x4 /* SIIGBF_ICONONLY */, out bitmap);
            if (bitmap == IntPtr.Zero) return null;

            var image = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (factory is not null) Marshal.FinalReleaseComObject(factory);
        }
    }

    private static ImageSource? CaptureButtonCenter(PixelRect bounds)
    {
        try
        {
            int btnWidth = bounds.Right - bounds.Left;
            int btnHeight = bounds.Bottom - bounds.Top;
            if (btnWidth <= 12 || btnHeight <= 12) return null;

            // The icon in Windows 11 taskbar buttons is typically 24x24 or 32x32 centered inside the button
            int captureSize = Math.Min(36, Math.Min(btnWidth, btnHeight) - 4);
            if (captureSize < 16) captureSize = Math.Min(btnWidth, btnHeight);

            int centerX = (bounds.Left + bounds.Right) / 2;
            int centerY = (bounds.Top + bounds.Bottom) / 2;
            int srcX = centerX - captureSize / 2;
            int srcY = centerY - captureSize / 2;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;

            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = CreateCompatibleBitmap(screenDc, captureSize, captureSize);
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);

            try
            {
                BitBlt(memDc, 0, 0, captureSize, captureSize, screenDc, srcX, srcY, 0x00CC0020 /* SRCCOPY */);
                SelectObject(memDc, oldBitmap);

                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteDC(memDc);
                DeleteObject(hBitmap);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
        catch
        {
            return null;
        }
    }
}
