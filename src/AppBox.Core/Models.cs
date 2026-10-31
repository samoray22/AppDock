using System.Text.RegularExpressions;

namespace AppBox.Core;

public enum LayoutMode { SingleAppBox, MultipleAppBoxes }
public enum ScreenEdge { Left, Right }
public enum SidebarMonitorMode { PrimaryMonitor, CursorMonitor, SpecificMonitor }
public enum SidebarHandleColorMode { ThemeAccent, ContainerTint, Custom }
public enum AppTheme { System, Light, Dark }
public enum TargetKind { Executable, Shortcut, Packaged, InternetShortcut }

public enum SortMode { Manual, RecentFirst }

public sealed class AppEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public TargetKind Kind { get; set; }
    public string Target { get; set; } = "";

    public DateTime? LastLaunchedUtc { get; set; }
}

public sealed class AppFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Container";
    public bool IsActive { get; set; } = true;
    public ContainerIcon Icon { get; set; } = new();
    public SortMode? SortModeOverride { get; set; }
    public int? GridColumnsOverride { get; set; }
    public int? GridRowsOverride { get; set; }
    public List<AppEntry> Applications { get; set; } = [];
}

public enum ContainerIconKind { Default, BuiltIn, Custom }
public sealed class ContainerIcon
{
    public ContainerIconKind Kind { get; set; }
    public string BuiltIn { get; set; } = "General Applications";
    public string? ResourcePath { get; set; }
}

public sealed class Preferences
{
    public LayoutMode LayoutMode { get; set; }
    public ContainerOpeningBehavior OpeningBehavior { get; set; } = ContainerOpeningBehavior.Click;
    public int HoverOpeningDelayMs { get; set; } = 400;
    public int HoverClosingDelayMs { get; set; } = 400;
    public bool ShowPopupPointer { get; set; } = true;
    public int IconSize { get; set; } = 48;
    public int GridColumns { get; set; } = 4;
    public int GridRows { get; set; } = 4;
    public bool ShowApplicationNames { get; set; } = true;
    public bool ShortenAppNames { get; set; } = true;

    public SortMode SortMode { get; set; } = SortMode.Manual;
    public bool CloseAfterLaunch { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool EnableAnimations { get; set; } = true;
    public ContainerAnimationStyle AnimationStyle { get; set; } = ContainerAnimationStyle.Genie;
    public bool EnableDockMagnification { get; set; } = true;
    public bool EnableLaunchBounce { get; set; } = true;
    public bool EnableCardTiltOnHover { get; set; } = true;
    public bool EnablePoofOnRemove { get; set; } = true;
    public bool UseWindows11Style { get; set; } = true;
    public bool ShowContainerBorder { get; set; } = false;
    public bool AllowCommandLineShells { get; set; } = true;
    public bool EnableTaskbar { get; set; } = true;
    public bool EnableEdgeSidebar { get; set; } = false;
    public LayoutMode SidebarLayoutMode { get; set; } = LayoutMode.MultipleAppBoxes;
    public ScreenEdge SidebarEdge { get; set; } = ScreenEdge.Right;
    public double SidebarVerticalPosition { get; set; } = 0.35;
    public int SidebarHandleHeight { get; set; } = 136;
    public int SidebarHandleWidth { get; set; } = 6;
    public double SidebarHandleOpacity { get; set; } = 0.55;
    public SidebarHandleColorMode SidebarHandleColorMode { get; set; } = SidebarHandleColorMode.ThemeAccent;
    public string SidebarHandleCustomColor { get; set; } = "#00B7C3";
    public bool SidebarAutoHideOnFullscreen { get; set; } = true;
    public SidebarMonitorMode SidebarMonitorMode { get; set; } = SidebarMonitorMode.PrimaryMonitor;
    public int SidebarMonitorIndex { get; set; } = 0;
    public bool SidebarShowRecentApps { get; set; } = true;
    public int SidebarRecentAppsCount { get; set; } = 3;
    public ContainerOpeningBehavior SidebarOpeningBehavior { get; set; } = ContainerOpeningBehavior.ClickOrHover;
    public AppTheme Theme { get; set; } = AppTheme.System;
}

public sealed class Configuration
{
    public int SchemaVersion { get; set; } = 1;
    public Preferences Settings { get; set; } = new();
    public List<AppFolder> Folders { get; set; } = [];
    public Guid? SelectedFolderId { get; set; }
}

public static partial class TargetPolicy
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*![A-Za-z0-9._-]+$")]
    private static partial Regex AppIdPattern();

    public static void Validate(AppEntry entry, Func<string, bool>? exists = null)
    {
        if (string.IsNullOrWhiteSpace(entry.Name)) throw new ArgumentException("Enter an application name.");
        if (!Enum.IsDefined(entry.Kind)) throw new ArgumentException("Unknown application type.");
        if (entry.Kind == TargetKind.Packaged)
        {
            if (!AppIdPattern().IsMatch(entry.Target)) throw new ArgumentException("Invalid packaged application identity.");
            return;
        }
        var extension = entry.Kind switch { TargetKind.Executable => ".exe", TargetKind.InternetShortcut => ".url", _ => ".lnk" };
        if (!Path.IsPathFullyQualified(entry.Target) || entry.Target.StartsWith(@"\\") ||
            !string.Equals(Path.GetExtension(entry.Target), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a local, absolute " + extension + " file.");
        if (!(exists ?? File.Exists)(entry.Target)) throw new ArgumentException("This application file no longer exists.");
    }
}

public sealed class FolderManager(Configuration configuration)
{
    public AppFolder Create(string name)
    {
        var folder = new AppFolder { Name = CleanName(name) };
        configuration.Folders.Add(folder);
        configuration.SelectedFolderId = folder.Id;
        return folder;
    }
    public void Rename(AppFolder folder, string name) { RequireFolder(folder); folder.Name = CleanName(name); }
    public void Delete(AppFolder folder)
    {
        RequireFolder(folder);
        configuration.Folders.Remove(folder);
        if (configuration.SelectedFolderId == folder.Id) configuration.SelectedFolderId = configuration.Folders.FirstOrDefault()?.Id;
    }
    public void Add(AppFolder folder, AppEntry entry, Func<string, bool>? exists = null)
    {
        RequireFolder(folder);
        TargetPolicy.Validate(entry, exists);
        if (folder.Applications.Any(a => string.Equals(a.Target, entry.Target, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("This application is already in the container.");
        if (entry.Id == Guid.Empty || folder.Applications.Any(a => a.Id == entry.Id)) entry.Id = Guid.NewGuid();
        folder.Applications.Add(entry);
    }
    public void Insert(AppFolder folder, AppEntry entry, int index)
    {
        RequireFolder(folder);
        TargetPolicy.Validate(entry);
        if (folder.Applications.Any(a => a.Id == entry.Id || string.Equals(a.Target, entry.Target, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("This application is already in the container.");
        if (index < 0 || index > folder.Applications.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "Invalid insertion index.");
        folder.Applications.Insert(index, entry);
    }
    public void Remove(AppFolder folder, AppEntry entry) { RequireFolder(folder); folder.Applications.Remove(entry); }
    public void RenameApplication(AppFolder folder, AppEntry entry, string name)
    {
        RequireFolder(folder);
        if (!folder.Applications.Contains(entry)) throw new ArgumentException("Application not found in this container.");
        entry.Name = CleanName(name);
    }
    // insertionIndex identifies a gap in the original list, including the gap after the last item.
    public void Reorder(AppFolder folder, AppEntry entry, int insertionIndex)
    {
        RequireFolder(folder);
        var from = folder.Applications.IndexOf(entry);
        if (from < 0 || insertionIndex < 0 || insertionIndex > folder.Applications.Count) throw new ArgumentException("Invalid application position.");
        if (insertionIndex > from) insertionIndex--;
        folder.Applications.RemoveAt(from);
        folder.Applications.Insert(insertionIndex, entry);
    }
    public void Move(AppFolder folder, AppEntry entry, int offset)
    {
        RequireFolder(folder);
        var from = folder.Applications.IndexOf(entry);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= folder.Applications.Count) return;
        folder.Applications.RemoveAt(from); folder.Applications.Insert(to, entry);
    }
    public void MoveToTop(AppFolder folder, AppEntry entry)
    {
        RequireFolder(folder);
        var from = folder.Applications.IndexOf(entry);
        if (from <= 0) return;
        folder.Applications.RemoveAt(from);
        folder.Applications.Insert(0, entry);
    }
    public void MoveToBottom(AppFolder folder, AppEntry entry)
    {
        RequireFolder(folder);
        var from = folder.Applications.IndexOf(entry);
        if (from < 0 || from >= folder.Applications.Count - 1) return;
        folder.Applications.RemoveAt(from);
        folder.Applications.Add(entry);
    }
    public void MoveFolder(AppFolder folder, int offset)
    {
        RequireFolder(folder);
        var from = configuration.Folders.IndexOf(folder);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= configuration.Folders.Count) return;
        configuration.Folders.RemoveAt(from);
        configuration.Folders.Insert(to, folder);
    }
    public void MoveFolderToTop(AppFolder folder)
    {
        RequireFolder(folder);
        var from = configuration.Folders.IndexOf(folder);
        if (from <= 0) return;
        configuration.Folders.RemoveAt(from);
        configuration.Folders.Insert(0, folder);
    }
    public void MoveFolderToBottom(AppFolder folder)
    {
        RequireFolder(folder);
        var from = configuration.Folders.IndexOf(folder);
        if (from < 0 || from >= configuration.Folders.Count - 1) return;
        configuration.Folders.RemoveAt(from);
        configuration.Folders.Add(folder);
    }
    public void ReorderFolder(AppFolder folder, int targetIndex)
    {
        RequireFolder(folder);
        var from = configuration.Folders.IndexOf(folder);
        if (from < 0 || targetIndex < 0 || targetIndex >= configuration.Folders.Count || from == targetIndex) return;
        configuration.Folders.RemoveAt(from);
        configuration.Folders.Insert(targetIndex, folder);
    }
    private void RequireFolder(AppFolder folder)
    {
        if (!configuration.Folders.Contains(folder)) throw new ArgumentException("Container not found.");
    }
    private static string CleanName(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 60) throw new ArgumentException("Use a container name between 1 and 60 characters.");
        return name;
    }
}

public sealed record ResolvedTarget(string FileName, string Arguments = "", string WorkingDirectory = "", string Verb = "");

public sealed record LaunchOptions(bool KeepOpen = false, bool Elevated = false);
public interface IShortcutResolver { ResolvedTarget Resolve(string path); }
public interface IProcessLauncher { void Start(ResolvedTarget target); }
public sealed record LaunchResult(bool Success, string? Error = null);

public sealed class ApplicationLaunchService(IShortcutResolver shortcuts, IProcessLauncher processes, Func<bool>? allowCommandLineShells = null)
{
    private static readonly HashSet<string> ScriptHosts = new(StringComparer.OrdinalIgnoreCase)
    { "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "msiexec.exe" };

    private static readonly HashSet<string> InteractiveShells = new(StringComparer.OrdinalIgnoreCase)
    { "cmd.exe", "powershell.exe", "pwsh.exe" };

    public static void ValidateCommandArguments(string fileName, string arguments)
    {
        var exe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(arguments)) return;
        var args = arguments.Trim();

        if (exe.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            var parts = Regex.Split(args, @"\s+");
            foreach (var part in parts)
            {
                if (part.Equals("/c", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("-c", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("/k", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("-k", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("/r", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"This shortcut contains suspicious command arguments ({part}) that execute indirect scripts or commands.");
                }
            }
            if (args.Contains('&') || args.Contains('|'))
            {
                throw new ArgumentException("This shortcut contains suspicious command arguments (command chaining operators) that execute indirect scripts or commands.");
            }
        }
        else if (exe.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
                 exe.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
        {
            var parts = Regex.Split(args, @"\s+");
            for (int i = 0; i < parts.Length; i++)
            {
                var token = parts[i];
                var normalized = token.StartsWith('-') || token.StartsWith('/') ? token[1..] : "";
                if (string.IsNullOrEmpty(normalized)) continue;

                if (normalized.Equals("c", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("comm", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"This shortcut contains suspicious command arguments ({token}) that execute indirect scripts or commands.");
                }

                if (normalized.Equals("e", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("ec", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("enc", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"This shortcut contains suspicious command arguments ({token}) that execute encoded scripts or payloads.");
                }

                if (normalized.Equals("f", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("file", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"This shortcut contains suspicious command arguments ({token}) that execute external script files.");
                }

                if (normalized.Equals("w", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("window", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < parts.Length && (parts[i + 1].Equals("hidden", StringComparison.OrdinalIgnoreCase) || parts[i + 1] == "1"))
                    {
                        throw new ArgumentException($"This shortcut contains suspicious command arguments ({token} {parts[i + 1]}) that hide the console window.");
                    }
                }

                if (normalized.Equals("ep", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("exec", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < parts.Length && (parts[i + 1].Equals("bypass", StringComparison.OrdinalIgnoreCase) || parts[i + 1].Equals("unrestricted", StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new ArgumentException($"This shortcut contains suspicious command arguments ({token} {parts[i + 1]}) that bypass execution security policies.");
                    }
                }
            }
        }
    }

    public ResolvedTarget Resolve(AppEntry entry)
    {
        TargetPolicy.Validate(entry);
        if (entry.Kind == TargetKind.Packaged) return new("shell:AppsFolder\\" + entry.Target);
        if (entry.Kind == TargetKind.InternetShortcut) return new(InternetShortcut.ReadTarget(entry.Target));
        var target = entry.Kind == TargetKind.Shortcut ? shortcuts.Resolve(entry.Target) : new ResolvedTarget(entry.Target);
        TargetPolicy.Validate(new AppEntry { Name = entry.Name, Kind = TargetKind.Executable, Target = target.FileName });

        var exeName = Path.GetFileName(target.FileName);
        if (ScriptHosts.Contains(exeName))
            throw new ArgumentException($"Script hosts and indirect execution engines ({exeName}) are not supported.");

        if (InteractiveShells.Contains(exeName))
        {
            if (!(allowCommandLineShells?.Invoke() ?? true))
                throw new ArgumentException("Command-line shells (PowerShell, Command Prompt) are disabled in AppDock Settings.");

            ValidateCommandArguments(target.FileName, target.Arguments);
        }

        if (target.WorkingDirectory.Length > 0 && (!Path.IsPathFullyQualified(target.WorkingDirectory) || !Directory.Exists(target.WorkingDirectory)))
            throw new ArgumentException("The shortcut working directory is unavailable.");
        return target;
    }

    public LaunchResult Launch(AppEntry entry) => Launch(entry, new());

    public LaunchResult Launch(AppEntry entry, LaunchOptions options)
    {
        try
        {
            if (options.Elevated && entry.Kind is TargetKind.Packaged or TargetKind.InternetShortcut)
                return new(false, "This app type can't run as administrator.");

            var resolved = Resolve(entry);
            var target = options.Elevated ? resolved with { Verb = "runas" } : resolved;
            processes.Start(target);
            return new(true);
        }
        catch (System.ComponentModel.Win32Exception win32) when (win32.NativeErrorCode == 1223)
        {
            return new(false, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ex.InnerException is System.ComponentModel.Win32Exception innerWin32 && innerWin32.NativeErrorCode == 1223)
                return new(false, null);
            if (ex.HResult == unchecked((int)0x800704C7))
                return new(false, null);
            return new(false, "AppDock couldn’t open “" + entry.Name + "”. " + ex.Message);
        }
    }
}
