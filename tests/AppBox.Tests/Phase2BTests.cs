using AppBox.Core;
using AppBox.Infrastructure;
namespace AppBox.Tests;
internal static class Phase2BTests
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("Jump List actions preserve stable container IDs through arguments and IPC", () =>
        {
            Check(TaskbarCommands.ForContainer(null).Select(t => t.Title).SequenceEqual(new[] { "AppDock Settings", "Edit Container", "Manage Containers" }));
            foreach (Guid? id in new Guid?[] { null, Guid.NewGuid() })
            foreach (var task in TaskbarCommands.ForContainer(id))
            {
                var parsed = ActivationRequest.ParseArguments(task.Request.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                Check(parsed == task.Request && parsed.ContainerId == id);
                Check(ActivationRequest.TryFromWire(parsed.ToWire(), out var decoded) && decoded == parsed);
            }
        });
        test("Legacy bare-GUID IPC and folder argument remain compatible", () =>
        {
            var id = Guid.NewGuid(); Check(ActivationRequest.TryFromWire(id.ToString(), out var request) && request.ContainerId == id);
            Check(ActivationRequest.ParseArguments(["--folder", id.ToString()]) == request);
            Check(ActivationRequest.TryFromWire("", out request) && request.Command == ActivationCommand.Open && request.ContainerId is null);
        });
        test("Malformed and ambiguous commands never route to another container", () =>
        {
            foreach (var args in new string[][] { ["--container"], ["--container", "invalid"], ["--settings", "--edit-container"], ["--container", Guid.Empty.ToString()], ["--container", Guid.NewGuid().ToString(), "--folder", Guid.NewGuid().ToString()] })
            { var rejected = false; try { ActivationRequest.ParseArguments(args); } catch (ArgumentException) { rejected = true; } Check(rejected); }
            foreach (var wire in new[] { "v2:99:", "v2:1:bad", "v2:2:bad:extra", "--settings", "not-a-guid" }) Check(!ActivationRequest.TryFromWire(wire, out _));
        });
        test("Explicit container resolution does not fall back; inactive edits are rejected", () =>
        {
            var config = new Configuration(); var manager = new FolderManager(config); var a = manager.Create("A"); var b = manager.Create("B"); b.IsActive = false;
            Check(new ActivationRequest(ActivationCommand.EditContainer, a.Id).Resolve(config, true) == a);
            Check(new ActivationRequest(ActivationCommand.EditContainer, b.Id).Resolve(config, true) is null);
            Check(new ActivationRequest(ActivationCommand.Settings, b.Id).Resolve(config, false) == b);
            Check(new ActivationRequest(ContainerId: Guid.NewGuid()).Resolve(config, false) is null);
        });
        test("Native Jump List tasks publish and replace for an isolated explicit AppID", () =>
        {
            var identity = "AppDock.Diagnostics." + Guid.NewGuid().ToString("N");
            try
            {
                var exe = Environment.ProcessPath!; var icons = new ContainerIcons(Path.Combine(root, "jump-icons")); var icon = icons.PersistentPath(new());
                WindowsJumpLists.Publish(identity, exe, icon, Guid.NewGuid()); WindowsJumpLists.Publish(identity, exe, icon, Guid.NewGuid());
            }
            finally { WindowsJumpLists.DeleteDiagnosticList(identity); }
        });
        test("Secondary instance forwards a command through a current-user diagnostic pipe", () =>
        {
            var scope = Guid.NewGuid().ToString(); using var first = new SingleInstance(scope); Check(first.IsPrimary);
            var received = new TaskCompletionSource<ActivationRequest>(TaskCreationOptions.RunContinuationsAsynchronously); first.Listen(r => received.TrySetResult(r));
            using var second = new SingleInstance(scope); Check(!second.IsPrimary);
            var request = new ActivationRequest(ActivationCommand.EditContainer, Guid.NewGuid()); second.SignalAsync(request).GetAwaiter().GetResult();
            Check(received.Task.Wait(TimeSpan.FromSeconds(5)) && received.Task.Result == request);
        });
        test("Bottom pointer follows cursor fallback and clamps clear of rounded corners", () =>
        {
            var monitor = new PixelRect(0, 0, 1920, 1080); var work = new PixelRect(0, 0, 1920, 1032);
            foreach (var x in new[] { 1, 900, 1919 })
            {
                var cursor = new PixelPoint(x, 1050); var popup = PopupPlacement.Calculate(monitor, work, cursor, 1, 300, 200);
                var pointer = PopupPointer.Calculate(monitor, work, cursor, popup, 1, true);
                Check(pointer.Visible && pointer.UsesCursorFallback && pointer.CenterDip >= 26 && pointer.CenterDip <= 258);
            }
        });
        test("Pointer conversion supports fractional DPI and negative secondary-monitor origins", () =>
        {
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                var monitor = new PixelRect(-1920, -200, 0, 880); var work = new PixelRect(-1920, -200, 0, 832); var cursor = new PixelPoint(-1000, 850);
                var popup = PopupPlacement.Calculate(monitor, work, cursor, scale, 300, 200);
                var pointer = PopupPointer.Calculate(monitor, work, cursor, popup, scale, true);
                Check(pointer.Visible && Math.Abs(pointer.CenterDip - ((cursor.X - popup.X) / scale - 8)) < 0.001);
            }
        });
        test("Pointer can be disabled and is hidden for unsupported taskbar edges", () =>
        {
            var monitor = new PixelRect(0, 0, 1920, 1080); var work = new PixelRect(0, 0, 1920, 1032); var bounds = new PopupBounds(500, 500, 300, 200);
            Check(!PopupPointer.Calculate(monitor, work, new(600, 1050), bounds, 1, false).Visible);
            Check(!PopupPointer.Calculate(monitor, new(48, 0, 1920, 1080), new(20, 500), bounds, 1, true).Visible);
            Check(PopupPointer.Calculate(monitor, work, new(800, 700), bounds, 1, true).CenterDip == 142);
        });
        test("Popup states cancel stale animation completion and repeated opening", () =>
        {
            var state = new LauncherInteraction(); Check(state.State == PopupState.Closed && state.TryBeginOpen()); var token = state.Generation;
            Check(!state.TryBeginOpen()); state.CompleteOpen(token); Check(state.State == PopupState.Open);
            state.BeginClose(); state.CompleteClose(); state.CompleteOpen(token); Check(state.State == PopupState.Closed);
            state.TryBeginOpen(); token = state.Generation; state.BeginEdit(); state.CompleteOpen(token); Check(state.State == PopupState.Editing && !state.DismissOnFocusLost(false));
            state.EndEdit(); Check(state.State == PopupState.Open && state.DismissOnFocusLost(false));
        });
        test("Unsupported hover cannot schedule opening, change click popup state or bypass editing", () =>
        {
            var state = new LauncherInteraction(); Check(!state.TryBeginOpen(fromHover: true) && state.State == PopupState.Closed);
            state.TryBeginOpen(); state.CompleteOpen(state.Generation); var token = state.Generation;
            Check(!state.TryBeginOpen(fromHover: true) && state.Generation == token && state.State == PopupState.Open);
            state.BeginEdit(); Check(!state.TryBeginOpen(fromHover: true) && state.State == PopupState.Editing);
            state.BeginClose(); state.CompleteClose(); Check(!state.TryBeginOpen(fromHover: true) && state.State == PopupState.Closed);
        });
        test("Interaction options and delays persist; unsupported hover uses click fallback", () =>
        {
            var store = new ConfigurationStore(Path.Combine(root, "interaction")); var config = new Configuration();
            Check(config.Settings.OpeningBehavior == ContainerOpeningBehavior.Click && config.Settings.ShowPopupPointer);
            config.Settings.HoverOpeningDelayMs = 750; config.Settings.HoverClosingDelayMs = 900; config.Settings.ShowPopupPointer = false; config.Settings.EnableAnimations = false;
            config.Settings.OpeningBehavior = ContainerOpeningBehavior.ClickOrHover; store.Save(config); var saved = store.Load().Settings;
            Check(saved.OpeningBehavior == ContainerOpeningBehavior.Click && saved.HoverOpeningDelayMs == 750 && saved.HoverClosingDelayMs == 900 && !saved.ShowPopupPointer && !saved.EnableAnimations);
            config.Settings.HoverOpeningDelayMs = -1; config.Settings.HoverClosingDelayMs = int.MaxValue; store.Save(config); saved = store.Load().Settings;
            Check(saved.HoverOpeningDelayMs == 100 && saved.HoverClosingDelayMs == 2000);
        });
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
}
