using System.Diagnostics;
using System.IO;
using AppBox.Core;

namespace AppBox.Infrastructure;

public static class RunningApps
{
    public static HashSet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                names.Add(process.ProcessName);
            }
            catch
            {
                // Process may have exited before query
            }
            finally
            {
                process.Dispose();
            }
        }
        return names;
    }

    public static bool IsRunning(AppEntry entry, HashSet<string> processNames, IShortcutResolver resolver)
    {
        if (entry.Kind == TargetKind.Executable)
        {
            var exeName = Path.GetFileNameWithoutExtension(entry.Target);
            return !string.IsNullOrEmpty(exeName) && processNames.Contains(exeName);
        }

        if (entry.Kind == TargetKind.Shortcut)
        {
            try
            {
                var resolved = resolver.Resolve(entry.Target);
                var exeName = Path.GetFileNameWithoutExtension(resolved.FileName);
                return !string.IsNullOrEmpty(exeName) && processNames.Contains(exeName);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
