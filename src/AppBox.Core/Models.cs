using System.Text.RegularExpressions;

namespace AppBox.Core;

public enum LayoutMode { SingleAppBox, MultipleAppBoxes }
public enum AppTheme { System, Light, Dark }
public enum TargetKind { Executable, Shortcut, Packaged, InternetShortcut }

public sealed class AppEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public TargetKind Kind { get; set; }
    public string Target { get; set; } = "";
}

public sealed class AppFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New folder";
    public List<AppEntry> Applications { get; set; } = [];
}

public sealed class Preferences
{
    public LayoutMode LayoutMode { get; set; }
    public bool ShowApplicationNames { get; set; } = true;
    public bool CloseAfterLaunch { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool EnableAnimations { get; set; } = true;
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
            throw new ArgumentException("This application is already in the folder.");
        if (entry.Id == Guid.Empty || folder.Applications.Any(a => a.Id == entry.Id)) entry.Id = Guid.NewGuid();
        folder.Applications.Add(entry);
    }
    public void Remove(AppFolder folder, AppEntry entry) { RequireFolder(folder); folder.Applications.Remove(entry); }
    public void RenameApplication(AppFolder folder, AppEntry entry, string name)
    {
        RequireFolder(folder);
        if (!folder.Applications.Contains(entry)) throw new ArgumentException("Application not found in this folder.");
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
    private void RequireFolder(AppFolder folder)
    {
        if (!configuration.Folders.Contains(folder)) throw new ArgumentException("Folder not found.");
    }
    private static string CleanName(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 60) throw new ArgumentException("Use a folder name between 1 and 60 characters.");
        return name;
    }
}

public sealed record ResolvedTarget(string FileName, string Arguments = "", string WorkingDirectory = "");
public interface IShortcutResolver { ResolvedTarget Resolve(string path); }
public interface IProcessLauncher { void Start(ResolvedTarget target); }
public sealed record LaunchResult(bool Success, string? Error = null);

public sealed class ApplicationLaunchService(IShortcutResolver shortcuts, IProcessLauncher processes)
{
    private static readonly HashSet<string> Interpreters = new(StringComparer.OrdinalIgnoreCase)
    { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "msiexec.exe" };

    public ResolvedTarget Resolve(AppEntry entry)
    {
        TargetPolicy.Validate(entry);
        if (entry.Kind == TargetKind.Packaged) return new("shell:AppsFolder\\" + entry.Target);
        if (entry.Kind == TargetKind.InternetShortcut) return new(InternetShortcut.ReadTarget(entry.Target));
        var target = entry.Kind == TargetKind.Shortcut ? shortcuts.Resolve(entry.Target) : new ResolvedTarget(entry.Target);
        TargetPolicy.Validate(new AppEntry { Name = entry.Name, Kind = TargetKind.Executable, Target = target.FileName });
        if (Interpreters.Contains(Path.GetFileName(target.FileName)))
            throw new ArgumentException("Command interpreters and indirect command launchers are not supported in Phase 1.");
        if (target.WorkingDirectory.Length > 0 && (!Path.IsPathFullyQualified(target.WorkingDirectory) || !Directory.Exists(target.WorkingDirectory)))
            throw new ArgumentException("The shortcut working directory is unavailable.");
        return target;
    }

    public LaunchResult Launch(AppEntry entry, Func<ResolvedTarget, bool> approve)
    {
        try
        {
            var target = Resolve(entry);
            if (!approve(target)) return new(false);
            processes.Start(target);
            return new(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return new(false, "AppBox couldn’t open “" + entry.Name + "”. " + ex.Message); }
    }
}
