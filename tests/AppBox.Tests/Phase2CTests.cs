using System.Resources;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppBox.App.Services;
using AppBox.App.ViewModels;
using AppBox.Core;
using AppBox.Infrastructure;
namespace AppBox.Tests;
internal static class Phase2CTests
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("Layout card properties use the existing enum and preserve all container data", () =>
        {
            var config = new Configuration(); var manager = new FolderManager(config); var a = manager.Create("Active"); var b = manager.Create("Inactive"); b.IsActive = false;
            a.Icon = new() { Kind = ContainerIconKind.Custom, ResourcePath = "retained-custom.ico" }; a.Applications.Add(new() { Name = "Notepad", Target = Path.Combine(Environment.SystemDirectory, "notepad.exe") });
            var id = a.Id; var appId = a.Applications[0].Id; var store = new ConfigurationStore(Path.Combine(root, "cards"));
            var vm = new SettingsViewModel(config, () => store.Save(config));
            vm.IsMultipleLayout = true; var saved = store.Load(); Check(saved.Settings.LayoutMode == LayoutMode.MultipleAppBoxes && !vm.IsSingleLayout);
            vm.IsSingleLayout = true; saved = store.Load(); Check(saved.Settings.LayoutMode == LayoutMode.SingleAppBox && !vm.IsMultipleLayout);
            Check(saved.Folders[0].Id == id && saved.Folders[0].Applications[0].Id == appId && saved.Folders[0].Icon.ResourcePath == "retained-custom.ico" && !saved.Folders[1].IsActive);
        });
        test("All General toggle values survive reload through the settings service", () =>
        {
            var config = new Configuration(); var store = new ConfigurationStore(Path.Combine(root, "toggles")); var startup = false;
            var vm = new SettingsViewModel(config, () => store.Save(config), v => startup = v, () => startup, true);
            vm.ShowAppNames = false; vm.AutoCloseAfterLaunch = false; vm.StartWithWindows = true; vm.UseWindows11Style = false;
            var loaded = store.Load(); Check(!loaded.Settings.ShowApplicationNames && !loaded.Settings.CloseAfterLaunch && loaded.Settings.StartWithWindows && !loaded.Settings.UseWindows11Style && startup);
            var restored = new SettingsViewModel(loaded, () => store.Save(loaded), v => startup = v, () => startup, true);
            Check(!restored.ShowAppNames && !restored.AutoCloseAfterLaunch && restored.StartWithWindows && !restored.UseWindows11Style);
        });
        test("Constructing settings reflects startup without changing registration", () =>
        {
            var config = new Configuration(); config.Settings.StartWithWindows = true; var writes = 0;
            var vm = new SettingsViewModel(config, () => { }, _ => writes++, () => true);
            Check(vm.StartWithWindows && writes == 0); vm.StartWithWindows = true; Check(writes == 0);
        });
        test("Failed General preference saves restore selection and show a nonblocking error", () =>
        {
            var config = new Configuration(); var vm = new SettingsViewModel(config, () => throw new IOException("disk unavailable"));
            vm.IsMultipleLayout = true; vm.ShowAppNames = false; vm.AutoCloseAfterLaunch = false;
            Check(vm.IsSingleLayout && !vm.IsMultipleLayout && vm.ShowAppNames && vm.AutoCloseAfterLaunch && vm.PreferenceError.Contains("could not be saved"));
        });
        test("Startup preference rollback restores registration after a save failure", () =>
        {
            var config = new Configuration(); var startup = false;
            var vm = new SettingsViewModel(config, () => throw new IOException("disk unavailable"), v => startup = v, () => startup);
            vm.StartWithWindows = true; Check(!startup && !vm.StartWithWindows && vm.PreferenceError.Length > 0);
        });
        test("Unavailable Windows style remains disabled without claiming a backdrop", () =>
        {
            var config = new Configuration(); config.Settings.UseWindows11Style = false; var saves = 0;
            var vm = new SettingsViewModel(config, () => saves++, windows11StyleAvailable: false);
            vm.UseWindows11Style = true; Check(!vm.Windows11StyleAvailable && !vm.UseWindows11Style && saves == 0);
        });
        test("Legacy configuration retains safe General option defaults", () =>
        {
            var dir = Path.Combine(root, "general-legacy"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"Settings\":{\"LayoutMode\":\"MultipleAppBoxes\",\"ShowApplicationNames\":false,\"CloseAfterLaunch\":false}}");
            var loaded = new ConfigurationStore(dir).Load(); Check(loaded.Settings.LayoutMode == LayoutMode.MultipleAppBoxes && !loaded.Settings.ShowApplicationNames && !loaded.Settings.CloseAfterLaunch && loaded.Settings.UseWindows11Style);
        });
        test("Official multi-resolution ICO and PNG branding are embedded runtime resources", () =>
        {
            var ico = Resource("resources/appdock.ico"); using var stream = new MemoryStream(ico);
            var frames = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
            Check(new[] { 16, 24, 32, 48, 64, 128, 256 }.All(size => frames.Any(f => f.PixelWidth == size)));
            foreach (var size in new[] { 32, 64, 128 })
            {
                using var png = new MemoryStream(Resource($"resources/branding/appdock{size}.png"));
                Check(BitmapDecoder.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].PixelWidth == size);
            }
        });
        test("Default main shortcut uses official icon and preserves stable identity", () =>
        {
            var directory = Path.Combine(root, "main-brand"); Directory.CreateDirectory(directory); var icon = Path.Combine(directory, "AppDock.ico"); File.WriteAllBytes(icon, Resource("resources/appdock.ico"));
            var launchers = new ContainerLaunchers(directory, Environment.ProcessPath!); launchers.UpdateMain(icon); var file = launchers.MainShortcutPath; launchers.UpdateMain(icon);
            Check(Directory.GetFiles(launchers.DirectoryPath, "*.lnk").Length == 1 && Identity(file) == WindowsJumpLists.MainIdentity);
            object? shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!); link = ((dynamic)shell!).CreateShortcut(file);
                Check(((string)((dynamic)link).IconLocation).StartsWith(icon, StringComparison.OrdinalIgnoreCase) && ((string)((dynamic)link).Arguments).Length == 0);
            }
            finally { if (link is not null) Marshal.FinalReleaseComObject(link); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
        });
        test("Light and Dark settings palettes provide readable text and blue selected states", () =>
        {
            foreach (var dark in new[] { false, true })
            {
                var palette = ThemeService.Palette(dark);
                foreach (var key in new[] { "Accent", "Line", "SelectionFill", "ToggleOff", "PreviewSurface", "SidebarSurface" }) Check(palette.ContainsKey(key));
                Check(Contrast(palette["Ink"], palette["Card"]) > 7 && Contrast(palette["Muted"], palette["Card"]) > 4.5);
                Check(palette["SelectionFill"] != palette["Card"] && Contrast(palette["AccentText"], palette["Hover"]) > 4.5);
            }
            Check(ThemeService.Palette(false)["Accent"] == "#0078D4");
        });
    }
    private static string Identity(string path)
    {
        object? shell = null, directory = null, item = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            directory = ((dynamic)shell!).NameSpace(Path.GetDirectoryName(path)!); item = ((dynamic)directory).ParseName(Path.GetFileName(path));
            return (string)((dynamic)item).ExtendedProperty("System.AppUserModel.ID");
        }
        finally { if (item is not null) Marshal.FinalReleaseComObject(item); if (directory is not null) Marshal.FinalReleaseComObject(directory); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
    }
    private static byte[] Resource(string key)
    {
        using var stream = typeof(ThemeService).Assembly.GetManifestResourceStream("AppDock.g.resources")!;
        using var reader = new ResourceReader(stream);
        foreach (System.Collections.DictionaryEntry entry in reader)
            if ((string)entry.Key == key && entry.Value is Stream value) { using var output = new MemoryStream(); value.CopyTo(output); return output.ToArray(); }
        throw new Exception("Branding resource missing: " + key);
    }
    private static double Contrast(string first, string second)
    {
        static double Light(string text)
        {
            var c = (Color)ColorConverter.ConvertFromString(text);
            static double Part(byte b) { var n = b / 255.0; return n <= 0.04045 ? n / 12.92 : Math.Pow((n + 0.055) / 1.055, 2.4); }
            return 0.2126 * Part(c.R) + 0.7152 * Part(c.G) + 0.0722 * Part(c.B);
        }
        var a = Light(first); var b = Light(second); return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
}
