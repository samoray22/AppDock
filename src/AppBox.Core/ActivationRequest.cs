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

public sealed record TaskbarHoverTarget(bool IsMainLauncher, Guid? ContainerId)
{
    public static TaskbarHoverTarget Main { get; } = new(true, null);
    public static TaskbarHoverTarget ForContainer(Guid id) => new(false, id);
}

public static class TaskbarCapabilities
{
    public const bool SupportsPinnedHover = true;
    public const string HoverLimitation = "Choose whether pinned taskbar containers open on click, hover, or both.";
    public static ContainerOpeningBehavior EffectiveBehavior(ContainerOpeningBehavior requested) =>
        Enum.IsDefined(requested) ? requested : ContainerOpeningBehavior.Click;

    public static TaskbarHoverTarget? MatchHoveredButton(
        Configuration config,
        string? automationId,
        string? buttonName,
        IReadOnlyDictionary<string, Guid?>? pinnedShortcutsByName = null)
    {
        if (!config.Settings.EnableTaskbar || config.Settings.OpeningBehavior == ContainerOpeningBehavior.Click)
            return null;

        var autoId = automationId?.Trim() ?? "";
        if (autoId.Length > 0)
        {
            const string containerPrefix = "AppDock.Container.";
            int idx = autoId.IndexOf(containerPrefix, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = autoId[(idx + containerPrefix.Length)..].Trim();
                var token = new string(rest.TakeWhile(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
                if (Guid.TryParse(token, out var id))
                {
                    if (config.Settings.LayoutMode == LayoutMode.SingleAppBox)
                        return TaskbarHoverTarget.Main;
                    var folder = config.Folders.FirstOrDefault(f => f.Id == id && f.IsActive);
                    return folder is not null ? TaskbarHoverTarget.ForContainer(folder.Id) : null;
                }
            }

            if (autoId.Contains("AppBox.Desktop", StringComparison.OrdinalIgnoreCase) ||
                autoId.Contains("AppDock.Main", StringComparison.OrdinalIgnoreCase))
            {
                return TaskbarHoverTarget.Main;
            }

            // Check if the exact AutomationId (such as Microsoft.AutoGenerated.{GUID}) is in our pinned shortcut map
            if (pinnedShortcutsByName is not null)
            {
                var strippedAppId = autoId.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase)
                    ? autoId["Appid:".Length..].Trim()
                    : autoId;
                if (pinnedShortcutsByName.TryGetValue(autoId, out var byAutoId) ||
                    (strippedAppId.Length > 0 && pinnedShortcutsByName.TryGetValue(strippedAppId, out byAutoId)))
                {
                    if (byAutoId is null || config.Settings.LayoutMode == LayoutMode.SingleAppBox)
                        return TaskbarHoverTarget.Main;
                    var folder = config.Folders.FirstOrDefault(f => f.Id == byAutoId.Value && f.IsActive);
                    if (folder is not null)
                        return TaskbarHoverTarget.ForContainer(folder.Id);
                }
            }

            // Reject explicit foreign AppIds (excluding Microsoft.AutoGenerated.{...} which Windows assigns to shortcuts with args)
            if (autoId.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase) &&
                !autoId.Contains("AppDock", StringComparison.OrdinalIgnoreCase) &&
                !autoId.Contains("AppBox", StringComparison.OrdinalIgnoreCase) &&
                !autoId.Contains("Microsoft.AutoGenerated.", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var rawName = buttonName?.Trim() ?? "";
        if (rawName.Length == 0) return null;

        var candidates = ExtractCandidateNames(rawName);

        foreach (var candidate in candidates)
        {
            if (candidate.Equals("AppDock", StringComparison.OrdinalIgnoreCase))
                return TaskbarHoverTarget.Main;

            const string descPrefix = "AppDock — ";
            if (candidate.StartsWith(descPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var folderPart = candidate[descPrefix.Length..].Trim();
                var folder = config.Folders.FirstOrDefault(f => f.IsActive && f.Name.Equals(folderPart, StringComparison.OrdinalIgnoreCase));
                if (folder is not null)
                    return config.Settings.LayoutMode == LayoutMode.SingleAppBox ? TaskbarHoverTarget.Main : TaskbarHoverTarget.ForContainer(folder.Id);
            }

            const string windowSuffix = " — AppDock";
            if (candidate.EndsWith(windowSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var folderPart = candidate[..^windowSuffix.Length].Trim();
                var folder = config.Folders.FirstOrDefault(f => f.IsActive && f.Name.Equals(folderPart, StringComparison.OrdinalIgnoreCase));
                if (folder is not null)
                    return config.Settings.LayoutMode == LayoutMode.SingleAppBox ? TaskbarHoverTarget.Main : TaskbarHoverTarget.ForContainer(folder.Id);
            }
        }

        if (pinnedShortcutsByName is not null)
        {
            foreach (var candidate in candidates)
            {
                if (pinnedShortcutsByName.TryGetValue(candidate, out var mappedId))
                {
                    if (mappedId is null)
                        return TaskbarHoverTarget.Main;
                    if (config.Settings.LayoutMode == LayoutMode.SingleAppBox)
                        return TaskbarHoverTarget.Main;
                    var folder = config.Folders.FirstOrDefault(f => f.Id == mappedId.Value && f.IsActive);
                    if (folder is not null)
                        return TaskbarHoverTarget.ForContainer(folder.Id);
                }
            }
        }

        return null;
    }

    private static List<string> ExtractCandidateNames(string rawName)
    {
        var list = new List<string>();
        void AddCandidate(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length > 0 && !list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                list.Add(trimmed);
        }

        var lines = rawName.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            AddCandidate(line);
            int lastDash = line.LastIndexOf(" - ", StringComparison.Ordinal);
            if (lastDash > 0)
                AddCandidate(line[..lastDash]);
            int firstDash = line.IndexOf(" - ", StringComparison.Ordinal);
            if (firstDash > 0 && firstDash != lastDash)
                AddCandidate(line[..firstDash]);
            int commaIdx = line.IndexOf(", ", StringComparison.Ordinal);
            if (commaIdx > 0)
                AddCandidate(line[..commaIdx]);
        }
        return list;
    }
}
