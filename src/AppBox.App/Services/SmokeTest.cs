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
        File.Delete(Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke-error.txt"));
        File.Delete(Path.Combine(Environment.CurrentDirectory, "artifacts", "secondary-error.txt"));
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
        app.Shutdown();
    }
    private static void Capture(Window window, string file)
    {
        window.UpdateLayout();
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
