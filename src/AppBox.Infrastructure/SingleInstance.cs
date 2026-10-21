using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AppBox.Infrastructure;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource cancellation = new();
    private Task? listener;
    public bool IsPrimary { get; }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    public SingleInstance()
    {
        var suffix = WindowsIdentity.GetCurrent().User!.Value + "-" + Process.GetCurrentProcess().SessionId;
        pipeName = "AppBox-" + suffix;
        mutex = new Mutex(true, @"Local\" + pipeName, out var created);
        IsPrimary = created;
    }
    public void Listen(Action<string> activate)
    {
        listener = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
                    await pipe.WaitForConnectionAsync(cancellation.Token);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                    using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
                    await writer.WriteLineAsync(Environment.ProcessId.ToString());
                    var line = await reader.ReadLineAsync(cancellation.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                    activate(line is not null && Guid.TryParse(line, out var id) ? id.ToString() : "");
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException) { }
            }
        });
    }
    public async Task SignalAsync(string folderId)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000);
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var pid = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (int.TryParse(pid, out var processId)) AllowSetForegroundWindow(processId);
        await writer.WriteLineAsync(folderId);
    }
    public void Dispose()
    {
        cancellation.Cancel();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
        // Do not dispose the cancellation source while the asynchronous listener is unwinding.
        _ = listener?.ContinueWith(_ => cancellation.Dispose());
    }
}
