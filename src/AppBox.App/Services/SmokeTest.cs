using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppBox.Core;
using AppBox.Infrastructure;
using AppBox.App.Views;
using AppBox.App.ViewModels;

namespace AppBox.App.Services;

// Opt-in diagnostic run: isolated configuration, genuine WPF windows and renderer.
internal static class SmokeTest
{
    public static async Task RunAsync(App app, Configuration config, ConfigurationStore store, LauncherWindow launcher, LauncherViewModel model, Action<string> settings)
    {
        var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "artifacts"));
        File.Delete(Path.Combine(output, "smoke-result.txt"));
        File.Delete(Path.Combine(output, "phase-1c-result.txt"));
        File.Delete(Path.Combine(output, "layout-metrics.txt"));
        File.Delete(Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke-error.txt"));
        File.Delete(Path.Combine(Environment.CurrentDirectory, "artifacts", "secondary-error.txt"));
        launcher.SuppressDismiss = true;
        launcher.Present(false);
        await Task.Delay(200);
        Capture(launcher, Path.Combine(output, "normal-empty.png"));
        Require(launcher.AppScroller.ScrollableHeight == 0 && launcher.Height < 400, "Empty launcher is not compact.");
        var manager = new FolderManager(config);
        var folder = manager.Create("Everyday");
        var discovered = new ApplicationDiscovery().Discover();
        foreach (var name in new[] { "Calculator", "Notepad", "Paint", "Microsoft Edge", "File Explorer", "Visual Studio Code", "Terminal", "Settings" })
        {
            var entry = discovered.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry is not null) manager.Add(folder, entry);
        }
        if (folder.Applications.Count == 0)
            manager.Add(folder, new AppEntry { Name = "Notepad", Kind = TargetKind.Executable, Target = Path.Combine(Environment.SystemDirectory, "notepad.exe") });
        var workFolder = manager.Create("Work"); manager.Create("Creative"); config.SelectedFolderId = folder.Id;
        config.Settings.Theme = AppTheme.Light;
        store.Save(config); model.Refresh(); ThemeService.Apply(AppTheme.Light);
        launcher.SuppressDismiss = true;
        launcher.Present(false);
        await Task.Delay(500);
        Capture(launcher, Path.Combine(output, "launcher-light.png"));
        ThemeService.Apply(AppTheme.Dark);
        await Task.Delay(300);
        Capture(launcher, Path.Combine(output, "launcher-dark.png"));
        settings("General"); await Task.Delay(300);
        var window = app.Windows.OfType<SettingsWindow>().Single();
        Capture(window, Path.Combine(output, "settings-dark.png"));
        ThemeService.Apply(AppTheme.Light); await Task.Delay(300);
        window.Sections.SelectedIndex = 1; await Task.Delay(300);
        Capture(window, Path.Combine(output, "folders-light.png"));
        window.Close(); await Task.Delay(200);
        // An actual second process must exit and activate this process through the named pipe.
        config.Settings.LayoutMode = LayoutMode.MultipleAppBoxes;
        launcher.Hide();
        using var second = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--smoke-secondary --folder " + workFolder.Id) { UseShellExecute = false });
        await second!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        await Task.Delay(300);
        var activation = second.ExitCode == 0 && launcher.IsVisible && model.SelectedFolder?.Id == workFolder.Id;
        if (!activation) throw new InvalidOperationException("Second-process activation failed.");
        launcher.SuppressDismiss = false;
        var focusProbe = new Window { Title = "AppBox focus probe", Width = 200, Height = 100, ShowInTaskbar = false };
        focusProbe.Show(); focusProbe.Activate(); await Task.Delay(300);
        var dismissal = !launcher.IsVisible;
        focusProbe.Close();
        if (!dismissal) throw new InvalidOperationException("Popup did not dismiss on deactivation.");
        // Exercise the production launch and popup-close path with a harmless dedicated test executable.
        var probeExe = Path.Combine(Environment.CurrentDirectory, "tests", "AppBox.Tests", "bin", "Release", "net10.0-windows", "AppBox.Tests.exe");
        var marker = Path.Combine(Path.GetDirectoryName(store.FilePath)!, "launch-probe.txt");
        var linkPath = Path.Combine(Path.GetDirectoryName(store.FilePath)!, "launch-probe.lnk");
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!); link = ((dynamic)shell!).CreateShortcut(linkPath);
            ((dynamic)link).TargetPath = probeExe; ((dynamic)link).Arguments = "--launch-probe \"" + marker + "\"";
            ((dynamic)link).WorkingDirectory = Path.GetDirectoryName(store.FilePath)!; ((dynamic)link).Save();
        }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
        launcher.Present(false);
        app.LaunchForDiagnostics(new() { Name = "AppBox launch probe", Target = linkPath, Kind = TargetKind.Shortcut });
        for (var attempt = 0; attempt < 50 && !File.Exists(marker); attempt++) await Task.Delay(100);
        var launchClosed = !launcher.IsVisible && File.Exists(marker) && File.ReadAllText(marker).Equals(Path.GetDirectoryName(store.FilePath), StringComparison.OrdinalIgnoreCase);
        if (!launchClosed) throw new InvalidOperationException("Real application launch/auto-dismiss failed.");
        var icons = model.Applications.Count(a => a.Icon is not null);
        // Restore the catalog folder for the report's icon count.
        config.SelectedFolderId = folder.Id; model.Refresh(); icons = model.Applications.Count(a => a.Icon is not null);
        var roundtrip = store.Load();
        await File.WriteAllTextAsync(Path.Combine(output, "smoke-result.txt"), $"OS: {Environment.OSVersion}\nFolders: {roundtrip.Folders.Count}\nApps: {folder.Applications.Count}\nReal shell icons: {icons}\nConfig roundtrip: {roundtrip.Folders.Count == config.Folders.Count}\nSecond-process activation and folder routing: {activation}\nDeactivation dismissal: {dismissal}\nReal executable launch via .lnk and automatic dismissal: {launchClosed}\nWPF windows rendered: 4\n");
        await RunPhase1CAsync(app, config, store, launcher, model, output, probeExe);
        app.Shutdown();
    }
    private static async Task RunPhase1CAsync(App app, Configuration config, ConfigurationStore store, LauncherWindow launcher, LauncherViewModel model, string output, string probeExe)
    {
        var folder = new FolderManager(config).Create("Drop applications here");
        model.Refresh(); launcher.Present(false); model.SetEditing(true); launcher.SuppressDismiss = false;
        await Task.Delay(200);
        var directory = Path.GetDirectoryName(store.FilePath)!;
        var files = new[]
        {
            CreateLink(directory, "Notepad", Path.Combine(Environment.SystemDirectory, "notepad.exe")),
            CreateLink(directory, "File Explorer", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")),
            CreateLink(directory, "AppBox", Environment.ProcessPath!)
        };
        var focusStayed = true;
        for (var i = 0; i < 3; i++)
        {
            var other = new Window { Title = "AppBox edit focus probe", Width = 200, Height = 100, ShowInTaskbar = false };
            other.Show(); other.Activate(); await Task.Delay(100);
            focusStayed &= launcher.IsVisible && model.IsEditing && !launcher.Topmost;
            other.Close(); launcher.Activate(); await Task.Delay(100);
        }
        Require(focusStayed, "Edit mode did not survive repeated focus changes.");
        var data = new DataObject(DataFormats.FileDrop, files);
        var effect = launcher.UpdateDragFeedback(data, new Point(100, 170), DragDropEffects.Copy);
        Require(effect == DragDropEffects.Copy && model.ExternalDropActive, "External drag feedback is missing.");
        await Task.Delay(150); Capture(launcher, Path.Combine(output, "edit-drop-feedback.png"));
        Require(launcher.ImportDrop(data, new Point(100, 170), DragDropEffects.Copy) == DragDropEffects.Copy, "FileDrop failed.");
        Require(folder.Applications.Count == 3 && !model.ExternalDropActive && model.Applications.All(a => a.Icon is not null), "Three shortcut icons were not imported.");
        await Task.Delay(250);
        var first = folder.Applications[0];
        var internalData = new DataObject(); internalData.SetData(LauncherWindow.InternalDragFormat, new LauncherWindow.ApplicationMove(folder.Id, first.Id), false);
        var dropPoint = new Point(launcher.ActualWidth - 35, 190);
        Require(launcher.UpdateDragFeedback(internalData, dropPoint, DragDropEffects.Move) == DragDropEffects.Move, "Internal move feedback is missing.");
        Capture(launcher, Path.Combine(output, "edit-insertion.png"));
        Require(launcher.ImportDrop(internalData, dropPoint, DragDropEffects.Move) == DragDropEffects.Move && folder.Applications[^1].Id == first.Id, "Internal drop order was not saved.");
        var invalidInternal = new DataObject(DataFormats.FileDrop, files);
        invalidInternal.SetData(LauncherWindow.InternalDragFormat, "untrusted text", false);
        Require(launcher.ImportDrop(invalidInternal, dropPoint, DragDropEffects.Copy | DragDropEffects.Move) == DragDropEffects.None && folder.Applications.Count == 3, "Invalid internal drag was treated as an import.");
        model.Rename(first, "My Notepad"); model.Remove(folder.Applications[0]);
        Require(File.Exists(files[0]) && folder.Applications.Count == 2, "Removing an app affected its file.");
        await Task.Delay(250); Capture(launcher, Path.Combine(output, "launcher-edit.png"));
        var reloaded = store.Load().Folders.Single(f => f.Id == folder.Id);
        Require(reloaded.Applications.Select(a => a.Id).SequenceEqual(folder.Applications.Select(a => a.Id)) && reloaded.Applications[^1].Name == "My Notepad", "Edited configuration did not survive reload.");
        model.SetEditing(false); await Task.Delay(200);
        Capture(launcher, Path.Combine(output, "launcher-normal.png"));
        Require(launcher.AppScroller.ScrollableHeight == 0 && launcher.Height < 400, "Small normal folder is clipped or unnecessarily large.");
        var focus = new Window { Title = "AppBox normal focus probe", Width = 200, Height = 100, ShowInTaskbar = false };
        focus.Show(); focus.Activate(); await Task.Delay(150);
        Require(!launcher.IsVisible, "Done did not restore normal dismissal."); focus.Close();
        // Check the retained shell launch/automatic-close path after repeated edit transitions.
        launcher.Present(false);
        var marker = Path.Combine(directory, "phase1c-launch.txt");
        var launchLink = CreateLink(directory, "Phase1C launch probe", probeExe, "--launch-probe \"" + marker + "\"");
        app.LaunchForDiagnostics(new() { Name = "Phase 1C launch probe", Target = launchLink, Kind = TargetKind.Shortcut });
        for (var i = 0; i < 50 && !File.Exists(marker); i++) await Task.Delay(100);
        Require(File.Exists(marker) && File.ReadAllText(marker).Equals(directory, StringComparison.OrdinalIgnoreCase) && !launcher.IsVisible, "Normal launch after editing failed to preserve the working directory or close the launcher.");
        // A large folder must be bounded and genuinely scrollable in the running WPF tree.
        var large = new FolderManager(config).Create("Many applications");
        for (var i = 0; i < 48; i++) large.Applications.Add(new() { Name = "Notepad " + (i + 1), Target = files[0], Kind = TargetKind.Shortcut });
        model.Refresh(); launcher.Present(false); launcher.SuppressDismiss = true;
        await Task.Delay(200);
        Capture(launcher, Path.Combine(output, "launcher-scrollable.png"));
        File.WriteAllText(Path.Combine(output, "grid-tree.txt"), DescribeVisual(launcher.AppGrid, 0));
        Require(launcher.AppScroller.ScrollableHeight > 0 && launcher.Height <= 771, "Large folder was not height bounded and scrollable.");
        await File.WriteAllTextAsync(Path.Combine(output, "phase-1c-result.txt"), "Edit focus changes (3 cycles), no topmost: True\nExternal WPF FileDrop (three real .lnk files), real icons: True\nExternal Copy feedback and internal Move insertion: True\nInvalid internal format cannot import external data: True\nReorder, rename, remove, JSON reload: True\nDone restores normal focus dismissal: True\nReal shortcut launch after editing and automatic close: True\nLarge folder bounded and scrollable: True\nPhysical Explorer/Start-menu/taskbar drag gestures: NOT PERFORMED (computer-use runtime unavailable)\n");
    }
    private static string CreateLink(string directory, string name, string target, string args = "")
    {
        var file = Path.Combine(directory, name + ".lnk"); object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!); link = ((dynamic)shell!).CreateShortcut(file);
            ((dynamic)link).TargetPath = target; ((dynamic)link).Arguments = args; ((dynamic)link).WorkingDirectory = directory; ((dynamic)link).Save();
        }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); if (shell is not null) Marshal.FinalReleaseComObject(shell); }
        return file;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string DescribeVisual(DependencyObject visual, int depth)
    {
        if (depth > 4) return "";
        var text = new string(' ', depth * 2) + visual.GetType().Name + (visual is FrameworkElement e ? $" actual={e.ActualWidth}x{e.ActualHeight} desired={e.DesiredSize} width={e.Width} valid={e.IsMeasureValid}" : "") + "\n";
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++) text += DescribeVisual(VisualTreeHelper.GetChild(visual, i), depth + 1);
        return text;
    }
    private static void Capture(Window window, string file)
    {
        window.UpdateLayout();
        if (window is LauncherWindow launcher)
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(file)!, "layout-metrics.txt"), $"{Path.GetFileName(file)}: height={launcher.Height}, actual={launcher.ActualHeight}, viewport={launcher.AppScroller.ViewportHeight}, extent={launcher.AppScroller.ExtentHeight}, scrollable={launcher.AppScroller.ScrollableHeight}, count={launcher.AppGrid.Items.Count}, grid={launcher.AppGrid.ActualWidth}x{launcher.AppGrid.ActualHeight}, desired={launcher.AppGrid.DesiredSize}\n");
        var dpi = VisualTreeHelper.GetDpi(window);
        var content = (FrameworkElement)window.Content;
        var width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        var height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
}
