using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppBox.Core;
using AppBox.Infrastructure;
using AppBox.App.ViewModels;
using AppBox.App.Views;
namespace AppBox.Tests;
internal static class Phase2ATests
{
    public static void Run(Action<string, Action> test, string root)
    {
        var dir = Path.Combine(root, "phase2a"); Directory.CreateDirectory(dir);
        var exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        test("Legacy migration preserves exact data, IDs, order, settings and source", () =>
        {
            var old = Path.Combine(dir, "AppBox"); var next = Path.Combine(dir, "AppDock"); Directory.CreateDirectory(old);
            var id = Guid.NewGuid(); var app = Guid.NewGuid();
            var json = $$$"""{"Folders":[{"Id":"{{{id}}}","Name":"Legacy","Applications":[{"Id":"{{{app}}}","Name":"Notepad","Target":{{{JsonSerializer.Serialize(exe)}}}}]}],"SelectedFolderId":"{{{id}}}","Settings":{"LayoutMode":"MultipleAppBoxes","ShowApplicationNames":false}}""";
            File.WriteAllText(Path.Combine(old, "settings.json"), json);
            var store = new ConfigurationStore(next, old); var config = store.Load();
            Check(config.Folders[0].Id == id && config.Folders[0].Applications[0].Id == app && config.Folders[0].IsActive && config.Folders[0].Icon.Kind == ContainerIconKind.Default);
            Check(config.SelectedFolderId == id && !config.Settings.ShowApplicationNames && config.Settings.LayoutMode == LayoutMode.MultipleAppBoxes);
            Check(File.ReadAllText(store.FilePath) == json); config.Folders[0].Name = "Changed"; store.Save(config);
            Check(File.ReadAllText(Path.Combine(old, "settings.json")) == json && store.Load().Folders[0].Name == "Changed");
        });
        test("New config wins over legacy, even when malformed", () =>
        {
            var old = Path.Combine(dir, "AppBox"); var next = Path.Combine(dir, "existing"); Directory.CreateDirectory(next);
            File.WriteAllText(Path.Combine(next, "settings.json"), "{bad"); var store = new ConfigurationStore(next, old);
            Check(store.Load().Folders.Count == 0 && store.RecoveryMessage is not null);
        });
        test("Activation and icons persist without changing identity or applications", () =>
        {
            var config = new Configuration(); var folder = new FolderManager(config).Create("Icons"); folder.Applications.Add(new() { Name = "Notepad", Target = exe });
            var id = folder.Id; var app = folder.Applications[0].Id; folder.Icon = new() { Kind = ContainerIconKind.BuiltIn, BuiltIn = "Development" }; folder.IsActive = false;
            var store = new ConfigurationStore(Path.Combine(dir, "persist")); store.Save(config); var loaded = store.Load().Folders.Single();
            Check(!loaded.IsActive && loaded.Icon.BuiltIn == "Development" && loaded.Id == id && loaded.Applications[0].Id == app);
            loaded.Icon = new(); loaded.IsActive = true; config.Folders[0] = loaded; store.Save(config);
            Check(store.Load().Folders[0].Icon.Kind == ContainerIconKind.Default && loaded.Id == id);
        });
        var icons = new ContainerIcons(dir);
        var png = Path.Combine(dir, "transparent.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(ContainerIcons.BuiltIn("Graphics")));
        using (var stream = File.Create(png)) encoder.Save(stream);
        test("Custom PNG loads, keeps alpha and survives source deletion", () =>
        {
            var choice = icons.Import(png);
            var config = new Configuration(); var folder = new FolderManager(config).Create("Custom"); folder.Icon = choice;
            var store = new ConfigurationStore(Path.Combine(dir, "custom-persist")); store.Save(config);
            choice = store.Load().Folders.Single().Icon; Check(choice.Kind == ContainerIconKind.Custom);
            var copy = icons.Get(choice); Check(copy.PixelWidth == 256 && File.Exists(choice.ResourcePath));
            var converted = new FormatConvertedBitmap(copy, PixelFormats.Bgra32, null, 0); var pixel = new byte[4]; converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0); Check(pixel[3] == 0);
            File.Move(png, png + ".moved"); Check(icons.Get(choice).PixelWidth == 256); File.Move(png + ".moved", png);
        });
        test("ICO, executable and shortcut custom sources decode without launching", () =>
        {
            var ico = icons.PersistentPath(new()); Check(ContainerIcons.Load(ico).PixelWidth == 256);
            var link = Phase1CTests.CreateShortcut(dir, "Icon source", exe, "", dir);
            foreach (var source in new[] { ico, exe, link }) Check(File.Exists(icons.Import(source).ResourcePath));
        });
        test("Missing, invalid and unsupported sources fail gracefully; missing resource falls back", () =>
        {
            foreach (var source in new[] { Path.Combine(dir, "absent.png"), Path.Combine(dir, "bad.ico"), Path.Combine(dir, "file.txt") })
            { if (!source.Contains("absent")) File.WriteAllText(source, "bad"); var failed = false; try { icons.Import(source); } catch { failed = true; } Check(failed); }
            Check(icons.Get(new() { Kind = ContainerIconKind.Custom, ResourcePath = "missing.ico" }).PixelWidth == 128);
            Check(icons.Get(new() { Kind = ContainerIconKind.BuiltIn, BuiltIn = "unknown" }).PixelWidth == 128);
        });
        test("Built-in library and persistent taskbar ICO frames remain available", () =>
        {
            foreach (var category in ContainerIcons.Categories)
            {
                var icon = new ContainerIcon { Kind = ContainerIconKind.BuiltIn, BuiltIn = category }; var path = icons.PersistentPath(icon);
                Check(path.StartsWith(Path.Combine(dir, "Icons")) && path == icons.PersistentPath(icon));
                using var stream = File.OpenRead(path); var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Check(decoder.Frames.Count == 7 && decoder.Frames.Any(f => f.PixelWidth == 16) && decoder.Frames.Any(f => f.PixelWidth == 256));
            }
        });
        test("Stable launcher shortcut, icon updates, duplicate prevention and container isolation", () =>
        {
            var config = new Configuration { Settings = new() { LayoutMode = LayoutMode.MultipleAppBoxes } };
            var manager = new FolderManager(config); var a = manager.Create("A"); var b = manager.Create("B");
            b.Icon = new() { Kind = ContainerIconKind.BuiltIn, BuiltIn = "Games" };
            var launchers = new ContainerLaunchers(dir, exe); launchers.Sync(config); var path = launchers.ShortcutPath(a.Id); var original = ReadLink(path);
            var other = File.ReadAllBytes(launchers.ShortcutPath(b.Id)); a.Name = "Renamed"; a.Icon = icons.Import(png); launchers.Update(a);
            var changed = ReadLink(path); Check(ReadIdentity(path) == ContainerLaunchers.Identity(a.Id)); Check(original.Args == changed.Args && changed.Args == "--container " + a.Id && original.Icon != changed.Icon);
            Check(other.SequenceEqual(File.ReadAllBytes(launchers.ShortcutPath(b.Id)))); launchers.Sync(config);
            Check(Directory.GetFiles(launchers.DirectoryPath, "*.lnk").Length == 2);
            a.IsActive = false; launchers.Sync(config); Check(File.Exists(path) && a.Icon.Kind == ContainerIconKind.Custom);
            config.Settings.LayoutMode = LayoutMode.SingleAppBox; launchers.Sync(config); Check(File.Exists(path));
        });
        test("Single-mode overview filters inactive containers; empty active containers launch", () =>
        {
            var config = new Configuration(); var manager = new FolderManager(config); var a = manager.Create("A"); var b = manager.Create("B"); b.IsActive = false;
            var vm = new LauncherViewModel(config, _ => { }); Check(vm.IsOverview && vm.Containers.Count == 1);
            vm.OpenContainer(a); Check(!vm.IsOverview && vm.Applications.Count == 0); vm.SetEditing(true); Check(vm.IsEditing);
            vm.ToggleActive(); Check(!a.IsActive && vm.Containers.Count == 0 && config.Folders.Count == 2);
        });
        test("Failed container rename and activation saves restore state", () =>
        {
            var config = new Configuration(); var folder = new FolderManager(config).Create("Original");
            var vm = new LauncherViewModel(config, _ => { }, () => throw new IOException("disk unavailable"));
            vm.RenameContainer("Unsaved"); Check(folder.Name == "Original" && vm.Status.Contains("not saved"));
            vm.ToggleActive(); Check(folder.IsActive && vm.Status.Contains("not saved"));
        });
        test("Direct launch needs no approval callback", () =>
        {
            var process = new ProcessProbe(); var result = new ApplicationLaunchService(new WindowsShortcutResolver(), process).Launch(new() { Name = "Notepad", Target = exe });
            Check(result.Success && process.Starts == 1);
        });
        test("Container deletion only removes references, never custom icon or app files", () =>
        {
            var config = new Configuration(); var manager = new FolderManager(config); var folder = manager.Create("Delete"); folder.Icon = icons.Import(png);
            folder.Applications.Add(new() { Name = "Notepad", Target = exe }); manager.Delete(folder);
            Check(config.Folders.Count == 0 && File.Exists(exe) && File.Exists(folder.Icon.ResourcePath));
        });
    }
    private static string ReadIdentity(string path)
    {
        object? shell = null, directory = null, item = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            directory = ((dynamic)shell!).NameSpace(Path.GetDirectoryName(path)!);
            item = ((dynamic)directory).ParseName(Path.GetFileName(path));
            return (string)((dynamic)item).ExtendedProperty("System.AppUserModel.ID");
        }
        finally { if (item is not null) Marshal.FinalReleaseComObject(item); if (directory is not null) Marshal.FinalReleaseComObject(directory); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
    }
    private static (string Args, string Icon) ReadLink(string path)
    {
        object? shell = null, link = null;
        try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!); link = ((dynamic)shell!).CreateShortcut(path); return ((string)((dynamic)link).Arguments, (string)((dynamic)link).IconLocation); }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private sealed class ProcessProbe : IProcessLauncher { public int Starts; public void Start(ResolvedTarget target) => Starts++; }
}
