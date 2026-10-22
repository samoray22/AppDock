using System.Runtime.InteropServices;
using System.Windows;
using AppBox.App.ViewModels;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.Tests;

internal static class Phase1CTests
{
    public static void Run(Action<string, Action> test, string root)
    {
        var directory = Path.Combine(root, "phase-1c"); Directory.CreateDirectory(directory);
        var exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var shortcuts = Enumerable.Range(1, 3).Select(i => CreateShortcut(directory, "Application " + i, exe, "\"document " + i + ".txt\"", directory)).ToArray();
        var importer = new ApplicationImportService(new WindowsShortcutResolver());
        test("Edit state transitions are idempotent and transient", () =>
        {
            var state = new LauncherInteraction(); Check(!state.IsEditing && state.CanLaunch);
            state.BeginEdit(); state.BeginEdit(); Check(state.IsEditing && !state.CanLaunch);
            state.EndEdit(); state.EndEdit(); Check(!state.IsEditing && state.CanLaunch);
        });
        test("Normal focus and launch dismissal are restored after Done", () =>
        {
            var state = new LauncherInteraction(); Check(state.DismissOnFocusLost(false) && !state.DismissOnFocusLost(true));
            state.BeginEdit(); Check(!state.DismissOnFocusLost(false) && !state.DismissAfterLaunch(true, true));
            state.EndEdit(); Check(state.DismissOnFocusLost(false) && state.DismissAfterLaunch(true, true) && !state.DismissAfterLaunch(false, true) && !state.DismissAfterLaunch(true, false));
        });
        test("Real shortcut import keeps arguments and working directory without execution", () =>
        {
            var entry = importer.Describe(shortcuts[0]); var target = new WindowsShortcutResolver().Resolve(entry.Target);
            Check(entry.Kind == TargetKind.Shortcut && entry.Name == "Application 1" && target.Arguments == "\"document 1.txt\"" && target.WorkingDirectory == directory);
        });
        test("Multiple-item import partially accepts supported files and reports each failure", () =>
        {
            var config = new Configuration(); var folder = new FolderManager(config).Create("Import");
            var result = importer.Import(config, folder, shortcuts.Concat([Path.Combine(directory, "missing.exe"), Path.Combine(directory, "readme.txt")]));
            Check(result.Added.Count == 3 && result.Rejected.Count == 2 && folder.Applications.Count == 3);
        });
        test("Equivalent shortcut copies are duplicates; distinct arguments are retained", () =>
        {
            var copy = Path.Combine(directory, "copy.lnk"); File.Copy(shortcuts[0], copy);
            var config = new Configuration(); var folder = new FolderManager(config).Create("Duplicates");
            var result = importer.Import(config, folder, [shortcuts[0], copy, shortcuts[1], shortcuts[0].ToUpperInvariant()]);
            Check(result.Added.Count == 2 && result.Rejected.Count == 2);
        });
        test("Unsafe shortcut target is rejected during import", () =>
        {
            var file = CreateShortcut(directory, "Command", Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c echo forbidden", directory);
            Throws(() => importer.Describe(file));
        });
        test("HTTP URL shortcut imports and resolves; unsafe URL schemes are refused", () =>
        {
            var file = Path.Combine(directory, "Website.url"); File.WriteAllText(file, "[InternetShortcut]\nURL=https://example.com/Applications\nIconFile=\\\\server\\icon.ico");
            var entry = importer.Describe(file); Check(entry.Kind == TargetKind.InternetShortcut && InternetShortcut.ReadTarget(entry.Target) == "https://example.com/Applications");
            foreach (var value in new[] { "file:///C:/Windows/notepad.exe", "javascript:alert(1)", "shell:AppsFolder", "https://user:password@example.com/" })
            { File.WriteAllText(file, "[InternetShortcut]\nURL=" + value); Throws(() => importer.Describe(file)); }
        });
        test("Shell app reference must match the installed discovery catalog", () =>
        {
            const string id = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
            var catalogImporter = new ApplicationImportService(new WindowsShortcutResolver(), () => [new() { Name = "Calculator", Kind = TargetKind.Packaged, Target = id }]);
            Check(catalogImporter.Describe("shell:AppsFolder\\" + id).Name == "Calculator");
            Check(catalogImporter.Describe("::{4234d49b-0245-4df3-b780-3893943456e1}\\" + id).Kind == TargetKind.Packaged);
            Throws(() => catalogImporter.Describe("shell:AppsFolder\\Unknown_package!App"));
        });
        test("Internet shortcut launch dispatches its validated URL, never arbitrary schemes", () =>
        {
            var file = Path.Combine(directory, "Launch website.url"); File.WriteAllText(file, "[InternetShortcut]\nURL=https://example.com/AppBox");
            var process = new RecordingProcess(); var service = new ApplicationLaunchService(new WindowsShortcutResolver(), process);
            Check(service.Launch(importer.Describe(file)).Success && process.Last?.FileName == "https://example.com/AppBox");
            File.WriteAllText(file, "[InternetShortcut]\nURL=file:///C:/Windows/notepad.exe");
            Check(!service.Launch(new() { Name = "Unsafe", Target = file, Kind = TargetKind.InternetShortcut }).Success);
        });
        test("Malformed Shell IDList data is rejected before native resolution", () =>
        {
            foreach (var bytes in new[] { Array.Empty<byte>(), new byte[12], new byte[] { 255, 255, 255, 255, 0, 0, 0, 0, 0, 0, 0, 0 } }) Throws(() => ShellDropReader.ReadShellIdList(bytes));
        });
        test("WPF FileDrop supports multiple files and ignores unrelated formats", () =>
        {
            var data = new DataObject(DataFormats.FileDrop, shortcuts); Check(ShellDropReader.HasCandidates(data) && ShellDropReader.Read(data).Count == 3);
            Check(!ShellDropReader.HasCandidates(new DataObject(DataFormats.Text, "cmd.exe")));
        });
        test("Real Shell IDList clipboard format resolves a local shortcut", () =>
        {
            SHParseDisplayName(shortcuts[0], IntPtr.Zero, out var pidl, 0, out _);
            try
            {
                var size = checked((int)ILGetSize(pidl)); var bytes = new byte[14 + size];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 12);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 14);
                Marshal.Copy(pidl, bytes, 14, size);
                var data = new DataObject(ShellDropReader.ShellIdListFormat, new MemoryStream(bytes));
                Check(ShellDropReader.Read(data).Single().Equals(shortcuts[0], StringComparison.OrdinalIgnoreCase));
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        });
        test("Insertion-gap ordering preserves IDs at both ends", () =>
        {
            var config = new Configuration(); var manager = new FolderManager(config); var folder = manager.Create("Order");
            importer.Import(config, folder, shortcuts); var first = folder.Applications[0]; var last = folder.Applications[2];
            manager.Reorder(folder, first, 3); Check(folder.Applications[2] == first);
            manager.Reorder(folder, last, 0); Check(folder.Applications[0] == last && folder.Applications.Select(a => a.Id).Distinct().Count() == 3);
            manager.Reorder(folder, last, 1); Check(folder.Applications[0] == last);
        });
        test("Launcher edit imports, rename, reorder and remove survive reload", () =>
        {
            var config = new Configuration(); var folder = new FolderManager(config).Create("Saved edits");
            var store = new ConfigurationStore(Path.Combine(directory, "saved"));
            var vm = new LauncherViewModel(config, _ => throw new Exception("Must not launch in edit mode."), () => store.Save(config));
            vm.SetEditing(true); Check(vm.Import(shortcuts));
            var first = vm.Applications[0].Entry; vm.LaunchCommand.Execute(vm.Applications[0]);
            vm.Rename(first, "My renamed app"); vm.Reorder(folder.Id, first.Id, 3); vm.Remove(vm.Applications[0].Entry);
            vm.SetEditing(false);
            var loaded = store.Load(); Check(loaded.Folders[0].Id == folder.Id && loaded.Folders[0].Applications.Count == 2 && loaded.Folders[0].Applications[^1].Name == "My renamed app");
            Check(!new LauncherViewModel(loaded, _ => { }).IsEditing && File.Exists(shortcuts[0]));
        });
        test("Normal mode blocks edit mutations and restores one-click command execution", () =>
        {
            var config = new Configuration(); var folder = new FolderManager(config).Create("Normal"); importer.Import(config, folder, [shortcuts[0]]);
            var launches = 0; var vm = new LauncherViewModel(config, _ => launches++);
            Check(!vm.Import([shortcuts[1]])); vm.Remove(folder.Applications[0]); Check(folder.Applications.Count == 1);
            vm.SetEditing(true); vm.LaunchCommand.Execute(vm.Applications[0]); Check(launches == 0);
            vm.SetEditing(false); vm.LaunchCommand.Execute(vm.Applications[0]); Check(launches == 1);
        });
        test("Failed saves roll back launcher modifications and show failure", () =>
        {
            var config = new Configuration(); var folder = new FolderManager(config).Create("Rollback"); importer.Import(config, folder, [shortcuts[0]]);
            var vm = new LauncherViewModel(config, _ => { }, () => throw new IOException("disk unavailable")); vm.SetEditing(true);
            vm.Rename(folder.Applications[0], "Unsaved"); Check(folder.Applications[0].Name == "Application 1" && vm.Status.Contains("not saved"));
            Check(!vm.Import([shortcuts[1]]) && folder.Applications.Count == 1);
        });
        test("Reorder refuses cross-folder or stale internal identities", () =>
        {
            var config = new Configuration(); var manager = new FolderManager(config); var folder = manager.Create("Current"); importer.Import(config, folder, shortcuts);
            var vm = new LauncherViewModel(config, _ => { }); vm.SetEditing(true);
            Check(!vm.Reorder(Guid.NewGuid(), folder.Applications[0].Id, 0)); Check(!vm.Reorder(folder.Id, Guid.NewGuid(), 0)); Check(folder.Applications.Count == 3);
        });
        test("Adaptive sizing is compact, grows by grid rows and is bounded", () =>
        {
            Check(LauncherSizing.DesiredHeight(0, false) < LauncherSizing.DesiredHeight(1, false));
            Check(LauncherSizing.DesiredHeight(1, false) == LauncherSizing.DesiredHeight(4, false));
            Check(LauncherSizing.DesiredHeight(5, false) > LauncherSizing.DesiredHeight(4, false));
            Check(LauncherSizing.DesiredHeight(1000, true) <= 770 && LauncherSizing.DesiredHeight(0, true) > LauncherSizing.DesiredHeight(0, false));
            Check(LauncherSizing.GridHeight(8, 3) == 354);
        });
    }
    internal static string CreateShortcut(string directory, string name, string target, string arguments, string workingDirectory)
    {
        var file = Path.Combine(directory, name + ".lnk"); object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!); shortcut = ((dynamic)shell!).CreateShortcut(file);
            ((dynamic)shortcut).TargetPath = target; ((dynamic)shortcut).Arguments = arguments; ((dynamic)shortcut).WorkingDirectory = workingDirectory; ((dynamic)shortcut).Save();
        }
        finally { if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
        return file;
    }
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private sealed class RecordingProcess : IProcessLauncher { public ResolvedTarget? Last { get; private set; } public void Start(ResolvedTarget target) => Last = target; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] private static extern void SHParseDisplayName(string path, IntPtr context, out IntPtr pidl, uint attributes, out uint resultAttributes);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(IntPtr pidl);
    private static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection."); }
}
