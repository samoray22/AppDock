namespace AppBox.Core;

public enum ActivationCommand { Open, Settings, EditContainer, ManageContainers }
public sealed record ActivationRequest(ActivationCommand Command = ActivationCommand.Open, Guid? ContainerId = null)
{
    public static ActivationRequest ParseArguments(IReadOnlyList<string> args)
    {
        Guid? container = null; var command = ActivationCommand.Open; var hasCommand = false;
        for (var i = 0; i < args.Count; i++)
        {
            var value = args[i];
            if (value is "--container" or "--folder")
            {
                if (container is not null || ++i >= args.Count || !Guid.TryParse(args[i], out var id) || id == Guid.Empty)
                    throw new ArgumentException("A valid, unambiguous container ID is required.");
                container = id;
            }
            else if (value is "--settings" or "--edit-container" or "--manage-containers")
            {
                if (hasCommand) throw new ArgumentException("Choose one AppDock action.");
                hasCommand = true;
                command = value switch { "--settings" => ActivationCommand.Settings, "--edit-container" => ActivationCommand.EditContainer, _ => ActivationCommand.ManageContainers };
            }
            else if (value is not ("--background" or "--smoke-test" or "--smoke-secondary"))
                throw new ArgumentException("Unknown AppDock argument: " + value);
        }
        return new(command, container);
    }
    public string Arguments => (Command switch { ActivationCommand.Open => "", ActivationCommand.Settings => "--settings", ActivationCommand.EditContainer => "--edit-container", ActivationCommand.ManageContainers => "--manage-containers", _ => throw new ArgumentException("Unknown command.") })
        + (ContainerId is { } id ? (Command == ActivationCommand.Open ? "" : " ") + "--container " + id : "");
    public string ToWire() => Command == ActivationCommand.Open ? ContainerId?.ToString() ?? "" : "v2:" + (int)Command + ":" + ContainerId?.ToString();
    public static bool TryFromWire(string? text, out ActivationRequest request)
    {
        request = new();
        if (text == "") return true;
        if (Guid.TryParse(text, out var legacy) && legacy != Guid.Empty) { request = new(ContainerId: legacy); return true; }
        var parts = text?.Split(':');
        if (parts is not { Length: 3 } || parts[0] != "v2" || !int.TryParse(parts[1], out var command) || !Enum.IsDefined((ActivationCommand)command)) return false;
        Guid? container = null;
        if (parts[2].Length > 0) { if (!Guid.TryParse(parts[2], out var id) || id == Guid.Empty) return false; container = id; }
        request = new((ActivationCommand)command, container); return true;
    }
    public AppFolder? Resolve(Configuration config, bool requireActive) => ContainerId is { } id
        ? config.Folders.FirstOrDefault(f => f.Id == id && (!requireActive || f.IsActive))
        : config.Folders.FirstOrDefault(f => f.Id == config.SelectedFolderId && (!requireActive || f.IsActive)) ?? config.Folders.FirstOrDefault(f => !requireActive || f.IsActive);
}

public sealed record TaskbarTask(string Title, ActivationRequest Request);
public static class TaskbarCommands
{
    public static IReadOnlyList<TaskbarTask> ForContainer(Guid? id) => [
        new("AppDock Settings", new(ActivationCommand.Settings, id)),
        new("Edit Container", new(ActivationCommand.EditContainer, id)),
        new("Manage Containers", new(ActivationCommand.ManageContainers, id))];
}

public enum ContainerOpeningBehavior { Click, Hover, ClickOrHover }
public static class TaskbarCapabilities
{
    public const bool SupportsPinnedHover = false;
    public const string HoverLimitation = "Hover opening is unavailable: Windows exposes no validated hover-to-container identity mechanism for independently pinned launchers. Click to open remains available.";
    public static ContainerOpeningBehavior EffectiveBehavior(ContainerOpeningBehavior requested) => ContainerOpeningBehavior.Click;
}
