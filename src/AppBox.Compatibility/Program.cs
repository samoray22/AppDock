using System.Diagnostics;
var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "AppDock.exe")) { UseShellExecute = false };
foreach (var argument in args) start.ArgumentList.Add(argument);
using var process = Process.Start(start);
