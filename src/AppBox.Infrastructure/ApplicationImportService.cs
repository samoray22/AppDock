using System.Diagnostics;
using System.IO;
using AppBox.Core;

namespace AppBox.Infrastructure;

public sealed record ImportFailure(string Item, string Reason);
public sealed record ImportResult(IReadOnlyList<AppEntry> Added, IReadOnlyList<ImportFailure> Rejected);

// Shared by launcher drops, its file picker, and Settings. Resolving never starts a process.
public sealed class ApplicationImportService(IShortcutResolver shortcuts, Func<IReadOnlyList<AppEntry>>? discover = null)
{
    private readonly ApplicationLaunchService validator = new(shortcuts, new NeverLaunch());
    public static bool IsSupportedFile(string path) => new[] { ".exe", ".lnk", ".url" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public ImportResult Import(Configuration config, AppFolder folder, IEnumerable<string> items)
    {
        var added = new List<AppEntry>(); var rejected = new List<ImportFailure>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var app in folder.Applications) keys.Add(IdentityOrReference(app));
        var manager = new FolderManager(config);
        foreach (var item in items)
        {
            try
            {
                var entry = Describe(item);
                var key = IdentityOrReference(entry);
                if (keys.Contains(key)) throw new ArgumentException("This application is already in the folder.");
                manager.Add(folder, entry);
                keys.Add(key); added.Add(entry);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            { rejected.Add(new(Path.GetFileName(item), ex.Message)); }
        }
        return new(added, rejected);
    }

    public AppEntry Describe(string item)
    {
        var id = ShellAppId(item);
        if (id is not null)
        {
            var known = (discover ?? (() => new ApplicationDiscovery().Discover()))().FirstOrDefault(a => a.Kind == TargetKind.Packaged && string.Equals(a.Target, id, StringComparison.OrdinalIgnoreCase));
            if (known is null) throw new ArgumentException("This Shell application is not in the Windows application catalog.");
            return new() { Name = known.Name, Target = known.Target, Kind = known.Kind };
        }
        if (!IsSupportedFile(item)) throw new ArgumentException("Choose an .exe, .lnk, or HTTP/HTTPS .url shortcut.");
        if (!Path.IsPathFullyQualified(item) || item.StartsWith(@"\\")) throw new ArgumentException("Choose a local application file.");
        var file = Path.GetFullPath(item);
        var extension = Path.GetExtension(file);
        var entry = new AppEntry
        {
            Name = Path.GetFileNameWithoutExtension(file), Target = file,
            Kind = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ? TargetKind.Executable : extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ? TargetKind.InternetShortcut : TargetKind.Shortcut
        };
        validator.Resolve(entry); // Validate links and URL schemes now, then revalidate again when launched.
        if (entry.Kind == TargetKind.Executable)
        {
            var description = FileVersionInfo.GetVersionInfo(file).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) entry.Name = description.Trim();
        }
        return entry;
    }
    private string IdentityOrReference(AppEntry entry)
    {
        try
        {
            var resolved = validator.Resolve(entry);
            var target = entry.Kind is TargetKind.Packaged or TargetKind.InternetShortcut ? resolved.FileName : Path.GetFullPath(resolved.FileName);
            // Command arguments are case-sensitive; do not collapse distinct invocation arguments.
            return (entry.Kind == TargetKind.InternetShortcut ? target : target.ToUpperInvariant()) + "\0" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(resolved.Arguments)) + "\0" + resolved.WorkingDirectory.ToUpperInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { return entry.Kind + "\0" + entry.Target.ToUpperInvariant(); }
    }
    private static string? ShellAppId(string item)
    {
        foreach (var prefix in new[] { @"shell:AppsFolder\", @"::{4234d49b-0245-4df3-b780-3893943456e1}\" })
            if (item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var id = item[prefix.Length..];
                TargetPolicy.Validate(new() { Name = "Windows app", Kind = TargetKind.Packaged, Target = id }); return id;
            }
        return null;
    }
    private sealed class NeverLaunch : IProcessLauncher { public void Start(ResolvedTarget target) => throw new InvalidOperationException("Imports must never execute applications."); }
}
