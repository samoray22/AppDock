using System.Runtime.InteropServices;
using System.Text.Json;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.Tests;

internal static class Program
{
    private static int failures;
    private static int passed;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "AppBox-Tests-" + Guid.NewGuid());
    private static string Exe => Path.Combine(Environment.SystemDirectory, "notepad.exe");
    private static AppEntry Entry(string? target = null) => new() { Name = "Notepad", Target = target ?? Exe };
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--launch-probe") { File.WriteAllText(args[1], Environment.CurrentDirectory); return 0; }
        Directory.CreateDirectory(Root);
        Test("Folder create, rename, delete and stable ID", () =>
        {
            var config = new Configuration(); var manager = new FolderManager(config);
            var folder = manager.Create("Tools"); var id = folder.Id; manager.Rename(folder, "Work");
            Check(folder.Id == id && folder.Name == "Work"); manager.Delete(folder); Check(config.Folders.Count == 0 && config.SelectedFolderId is null);
        });
        Test("Folder names validated", () => { var manager = new FolderManager(new()); Throws<ArgumentException>(() => manager.Create("  ")); Throws<ArgumentException>(() => manager.Create(new string('a', 61))); });
        Test("Application add, remove and ordering", () =>
        {
            var config = new Configuration(); var manager = new FolderManager(config); var folder = manager.Create("Tools");
            var a = Entry(); var b = Entry(Path.Combine(Environment.SystemDirectory, "mspaint.exe")); manager.Add(folder, a, _ => true); manager.Add(folder, b, _ => true);
            manager.Move(folder, b, -1); Check(folder.Applications[0] == b); manager.Move(folder, b, -1); Check(folder.Applications[0] == b); manager.Remove(folder, a); Check(folder.Applications.Count == 1);
        });
        Test("Duplicate target prevention is case insensitive", () =>
        {
            var config = new Configuration(); var manager = new FolderManager(config); var folder = manager.Create("Tools"); manager.Add(folder, Entry());
            Throws<ArgumentException>(() => manager.Add(folder, Entry(Exe.ToUpperInvariant())));
        });
        Test("Invalid, relative, remote and missing paths rejected", () =>
        {
            foreach (var value in new[] { "notepad.exe", @"\\server\share\app.exe", @"C:\missing\not-here.exe", @"C:\Windows\script.cmd", "https://example.com/app.exe" })
                Throws<ArgumentException>(() => TargetPolicy.Validate(Entry(value)));
        });
        Test("Packaged identity validation", () =>
        {
            TargetPolicy.Validate(new() { Name = "Calculator", Kind = TargetKind.Packaged, Target = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" });
            Throws<ArgumentException>(() => TargetPolicy.Validate(new() { Name = "Bad", Kind = TargetKind.Packaged, Target = "shell:AppsFolder\\bad & command" }));
        });
        Test("Atomic persistence and backup", () =>
        {
            var store = new ConfigurationStore(Path.Combine(Root, "persistence")); var config = new Configuration(); var manager = new FolderManager(config); var folder = manager.Create("Work"); manager.Add(folder, Entry()); store.Save(config);
            config.Settings.ShowApplicationNames = false; store.Save(config); var loaded = store.Load();
            Check(loaded.Folders[0].Id == folder.Id && loaded.Folders[0].Applications[0].Target == Exe && !loaded.Settings.ShowApplicationNames);
            Check(File.Exists(store.FilePath + ".bak")); Check(!Directory.EnumerateFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp").Any());
        });
        Test("Both layout modes and preferences persist", () =>
        {
            var store = new ConfigurationStore(Path.Combine(Root, "modes"));
            foreach (var mode in Enum.GetValues<LayoutMode>())
            {
                var config = new Configuration { Settings = new() { LayoutMode = mode, Theme = AppTheme.Dark, CloseAfterLaunch = false, StartWithWindows = true, EnableAnimations = false } };
                store.Save(config); var loaded = store.Load(); Check(loaded.Settings.LayoutMode == mode && loaded.Settings.Theme == AppTheme.Dark && !loaded.Settings.CloseAfterLaunch && loaded.Settings.StartWithWindows && !loaded.Settings.EnableAnimations);
            }
        });
        Test("Malformed JSON preserved and gracefully recovered", () =>
        {
            var dir = Path.Combine(Root, "malformed"); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "settings.json"), "{broken");
            var store = new ConfigurationStore(dir); Check(store.Load().Folders.Count == 0 && store.RecoveryMessage is not null); Check(Directory.EnumerateFiles(dir, "settings.invalid-*.json").Count() == 1);
        });
        Test("Null and unknown configuration rejected", () =>
        {
            foreach (var data in new[] { "null", "{\"Settings\":null}", "{\"SchemaVersion\":9}", "{\"Settings\":{\"LayoutMode\":99}}", "{\"Folders\":[null]}" })
            {
                var dir = Path.Combine(Root, Guid.NewGuid().ToString()); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "settings.json"), data);
                var store = new ConfigurationStore(dir); store.Load(); Check(store.RecoveryMessage is not null);
            }
        });
        Test("Real .lnk resolves arguments and working directory", () =>
        {
            var file = Path.Combine(Root, "Notepad.lnk"); object? shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!); shortcut = ((dynamic)shell!).CreateShortcut(file);
                ((dynamic)shortcut).TargetPath = Exe; ((dynamic)shortcut).Arguments = "\"sample file.txt\""; ((dynamic)shortcut).WorkingDirectory = Root; ((dynamic)shortcut).Save();
            }
            finally { if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
            var target = new WindowsShortcutResolver().Resolve(file); Check(target.FileName.Equals(Exe, StringComparison.OrdinalIgnoreCase) && target.Arguments == "\"sample file.txt\"" && target.WorkingDirectory == Root);
        });
        Test("Launch failure becomes a friendly error", () =>
        {
            var runner = new FakeProcess { Fail = true }; var service = new ApplicationLaunchService(new FakeShortcut(), runner);
            var result = service.Launch(Entry(), _ => true); Check(!result.Success && result.Error!.Contains("Notepad") && result.Error.Contains("test failure"));
        });
        Test("Approval required and cancellation does not launch", () =>
        {
            var runner = new FakeProcess(); var service = new ApplicationLaunchService(new FakeShortcut(), runner);
            Check(!service.Launch(Entry(), _ => false).Success && runner.Count == 0); Check(service.Launch(Entry(), _ => true).Success && runner.Count == 1);
        });
        Test("Missing target never reaches process launcher", () =>
        {
            var runner = new FakeProcess(); var service = new ApplicationLaunchService(new FakeShortcut(), runner);
            var result = service.Launch(Entry(Path.Combine(Root, "absent.exe")), _ => true); Check(!result.Success && runner.Count == 0 && result.Error is not null);
        });
        Test("Shortcut arguments and directory preserved to launcher", () =>
        {
            var lnk = Path.Combine(Root, "test.lnk"); File.WriteAllText(lnk, "test");
            var resolver = new FakeShortcut { Target = new(Exe, "\"one two.txt\"", Root) }; var runner = new FakeProcess();
            var result = new ApplicationLaunchService(resolver, runner).Launch(new() { Name = "Test", Kind = TargetKind.Shortcut, Target = lnk }, _ => true);
            Check(result.Success && runner.Last == resolver.Target);
        });
        Test("Command interpreter shortcuts refused", () =>
        {
            var lnk = Path.Combine(Root, "test.lnk"); var runner = new FakeProcess();
            var service = new ApplicationLaunchService(new FakeShortcut { Target = new(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c echo unwanted") }, runner);
            Check(!service.Launch(new() { Name = "Bad", Kind = TargetKind.Shortcut, Target = lnk }, _ => true).Success && runner.Count == 0);
        });
        Test("Packaged launches use shell identity", () =>
        {
            var runner = new FakeProcess(); var entry = new AppEntry { Name = "Calculator", Kind = TargetKind.Packaged, Target = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" };
            Check(new ApplicationLaunchService(new FakeShortcut(), runner).Launch(entry, _ => true).Success && runner.Last!.FileName == "shell:AppsFolder\\" + entry.Target);
        });
        Test("Real Windows ShellExecute starts an executable with arguments", () =>
        {
            var marker = Path.Combine(Root, "launch-marker.txt");
            new WindowsProcessLauncher().Start(new ResolvedTarget(Environment.ProcessPath!, "--launch-probe \"" + marker + "\"", Root));
            Check(SpinWait.SpinUntil(() => File.Exists(marker), TimeSpan.FromSeconds(5)));
            Check(File.ReadAllText(marker).Equals(Root, StringComparison.OrdinalIgnoreCase));
        });
        Test("Popup follows bottom taskbar pointer and clamps at edges", () =>
        {
            var bounds = PopupPlacement.Calculate(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032), new(1900, 1050), 1);
            Check(bounds.X == 1920 - 468 - 8 && bounds.Y + bounds.Height < 1032);
        });
        Test("Popup supports negative monitor origins and scaled small working areas", () =>
        {
            var bounds = PopupPlacement.Calculate(new(-1280, -200, 0, 824), new(-1280, -200, 0, 776), new(-900, 800), 2);
            Check(bounds.Width == 936 && bounds.Height == 960 && bounds.X >= -1272 && bounds.X + bounds.Width <= -8 && bounds.Y >= -192 && bounds.Y + bounds.Height <= 768);
        });
        Test("Popup geometry handles top, left and right taskbars", () =>
        {
            var monitor = new PixelRect(0, 0, 1920, 1080);
            Check(PopupPlacement.Calculate(monitor, new(0, 48, 1920, 1080), new(500, 20), 1).Y == 58);
            Check(PopupPlacement.Calculate(monitor, new(48, 0, 1920, 1080), new(20, 500), 1).X == 58);
            var right = PopupPlacement.Calculate(monitor, new(0, 0, 1872, 1080), new(1900, 500), 1);
            Check(right.X + right.Width == 1862);
        });
        Console.WriteLine($"\n{passed} passed; {failures} failed.");
        Directory.Delete(Root, true);
        return failures == 0 ? 0 : 1;
    }
    private static void Test(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); } }
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class FakeShortcut : IShortcutResolver { public ResolvedTarget Target { get; set; } = new(Exe); public ResolvedTarget Resolve(string path) => Target; }
    private sealed class FakeProcess : IProcessLauncher
    {
        public bool Fail { get; init; } public int Count { get; private set; } public ResolvedTarget? Last { get; private set; }
        public void Start(ResolvedTarget target) { if (Fail) throw new IOException("test failure"); Count++; Last = target; }
    }
}
