using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AppBox.Core;

namespace AppBox.Infrastructure;

public sealed class ConfigurationStore
{
    private readonly string directory;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string? RecoveryMessage { get; private set; }
    public string FilePath => Path.Combine(directory, "settings.json");
    private readonly string? legacyDirectory;
    public string DirectoryPath => directory;
    public ConfigurationStore(string? directory = null, string? legacyDirectory = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        this.directory = directory ?? Path.Combine(local, "AppDock");
        this.legacyDirectory = legacyDirectory ?? (directory is null ? Path.Combine(local, "AppBox") : null);
    }

    public Configuration Load()
    {
        Directory.CreateDirectory(directory);
        // Copy the original bytes once; never alter or remove the legacy directory.
        // A failed copy leaves the source intact and prevents starting with an empty config.
        if (!ConfigurationExists(FilePath) && legacyDirectory is not null && ConfigurationExists(Path.Combine(legacyDirectory, "settings.json")))
        {
            var staged = Path.Combine(directory, Guid.NewGuid() + ".migration");
            try
            {
                File.Copy(Path.Combine(legacyDirectory, "settings.json"), staged, false);
                File.Move(staged, FilePath, false);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        if (!ConfigurationExists(FilePath)) return new();
        try
        {
            var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(FilePath), Json) ?? throw new JsonException("Empty configuration.");
            Validate(config);
            return config;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            var backup = Path.Combine(directory, $"settings.invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
            File.Copy(FilePath, backup, false);
            RecoveryMessage = "Your configuration could not be read. The original was preserved at " + backup;
            return new();
        }
    }

    public void Save(Configuration config)
    {
        Validate(config);
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, Guid.NewGuid() + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { JsonSerializer.Serialize(stream, config, Json); stream.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
            else File.Move(temp, FilePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static bool ConfigurationExists(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new IOException("A directory occupies the settings file path: " + path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // Inaccessible settings are not equivalent to a new installation.
    }

    private static void Validate(Configuration config)
    {
        if (config.SchemaVersion != 1 || config.Settings is null || config.Folders is null ||
            !Enum.IsDefined(config.Settings.LayoutMode) || !Enum.IsDefined(config.Settings.Theme)) throw new JsonException("Unsupported configuration.");
        config.Settings.OpeningBehavior = TaskbarCapabilities.EffectiveBehavior(config.Settings.OpeningBehavior);
        config.Settings.HoverOpeningDelayMs = Math.Clamp(config.Settings.HoverOpeningDelayMs, 100, 2000);
        config.Settings.HoverClosingDelayMs = Math.Clamp(config.Settings.HoverClosingDelayMs, 100, 2000);
        config.Settings.IconSize = Math.Clamp(config.Settings.IconSize, 24, 64);
        config.Settings.GridColumns = Math.Clamp(config.Settings.GridColumns, 2, 8);
        config.Settings.GridRows = Math.Clamp(config.Settings.GridRows, 1, 8);
        if (!Enum.IsDefined(config.Settings.SortMode)) config.Settings.SortMode = SortMode.Manual;
        if (!Enum.IsDefined(config.Settings.AnimationStyle)) config.Settings.AnimationStyle = ContainerAnimationStyle.Genie;
        config.Settings.AnimationDurationMs = config.Settings.AnimationDurationMs is >= 50 and <= 2000 ? config.Settings.AnimationDurationMs : 320;
        if (!Enum.IsDefined(config.Settings.SidebarLayoutMode)) config.Settings.SidebarLayoutMode = LayoutMode.MultipleAppBoxes;
        if (!config.Settings.EnableTaskbar && !config.Settings.EnableEdgeSidebar) config.Settings.EnableTaskbar = true;
        if (!Enum.IsDefined(config.Settings.SidebarEdge)) config.Settings.SidebarEdge = ScreenEdge.Right;
        if (!Enum.IsDefined(config.Settings.SidebarOpeningBehavior)) config.Settings.SidebarOpeningBehavior = ContainerOpeningBehavior.ClickOrHover;
        config.Settings.SidebarVerticalPosition = double.IsFinite(config.Settings.SidebarVerticalPosition)
            ? Math.Clamp(config.Settings.SidebarVerticalPosition, 0.05, 0.95)
            : 0.35;
        config.Settings.SidebarHandleHeight = Math.Clamp(config.Settings.SidebarHandleHeight, 48, 320);
        config.Settings.SidebarHandleWidth = Math.Clamp(config.Settings.SidebarHandleWidth, 3, 16);
        config.Settings.SidebarHandleOpacity = double.IsFinite(config.Settings.SidebarHandleOpacity)
            ? Math.Clamp(config.Settings.SidebarHandleOpacity, 0.0, 1.0)
            : 0.55;
        if (!Enum.IsDefined(config.Settings.SidebarHandleColorMode)) config.Settings.SidebarHandleColorMode = SidebarHandleColorMode.ThemeAccent;
        if (string.IsNullOrWhiteSpace(config.Settings.SidebarHandleCustomColor) || !Regex.IsMatch(config.Settings.SidebarHandleCustomColor.Trim(), @"^#[0-9A-Fa-f]{6}$"))
            config.Settings.SidebarHandleCustomColor = "#00B7C3";
        else
            config.Settings.SidebarHandleCustomColor = config.Settings.SidebarHandleCustomColor.Trim().ToUpperInvariant();
        if (!Enum.IsDefined(config.Settings.SidebarMonitorMode)) config.Settings.SidebarMonitorMode = SidebarMonitorMode.PrimaryMonitor;
        config.Settings.SidebarMonitorIndex = Math.Clamp(config.Settings.SidebarMonitorIndex, 0, 15);
        config.Settings.SidebarRecentAppsCount = Math.Clamp(config.Settings.SidebarRecentAppsCount, 1, 5);
        var ids = new HashSet<Guid>();
        foreach (var folder in config.Folders)
        {
            if (folder is null || folder.Id == Guid.Empty || !ids.Add(folder.Id) || string.IsNullOrWhiteSpace(folder.Name) || folder.Name.Length > 60 || folder.Applications is null)
                throw new JsonException("Invalid folder.");
            folder.Icon ??= new();
            if (!Enum.IsDefined(folder.Icon.Kind)) folder.Icon = new();
            if (folder.SortModeOverride is { } sortOverride && !Enum.IsDefined(sortOverride)) folder.SortModeOverride = null;
            if (folder.GridColumnsOverride is { } colsOverride) folder.GridColumnsOverride = Math.Clamp(colsOverride, 2, 8);
            if (folder.GridRowsOverride is { } rowsOverride) folder.GridRowsOverride = Math.Clamp(rowsOverride, 1, 8);
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in folder.Applications)
            {
                if (app is null || app.Id == Guid.Empty || !ids.Add(app.Id) || !targets.Add(app.Target)) throw new JsonException("Duplicate application.");
                // Missing files remain visible and removable; launch validates existence again.
                TargetPolicy.Validate(app, _ => true);
            }
        }
        if (config.SelectedFolderId is not null && !config.Folders.Any(f => f.Id == config.SelectedFolderId)) config.SelectedFolderId = config.Folders.FirstOrDefault()?.Id;
    }

    public void ExportBackup(Configuration config, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Choose a valid backup file path.");
        Validate(config);
        var fullDest = Path.GetFullPath(destinationPath);
        var parentDir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(parentDir)) Directory.CreateDirectory(parentDir);

        if (fullDest.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(config, Json);
            File.WriteAllBytes(fullDest, jsonBytes);
            return;
        }

        var tempZip = Path.Combine(Path.GetTempPath(), "AppDock-Export-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                var settingsEntry = archive.CreateEntry("settings.json", CompressionLevel.Optimal);
                using (var entryStream = settingsEntry.Open())
                {
                    JsonSerializer.Serialize(entryStream, config, Json);
                }

                var addedIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in config.Folders)
                {
                    if (folder.Icon is { Kind: ContainerIconKind.Custom, ResourcePath: { } resPath } &&
                        !string.IsNullOrWhiteSpace(resPath) && File.Exists(resPath))
                    {
                        var fileName = Path.GetFileName(resPath);
                        if (!string.IsNullOrEmpty(fileName) && addedIcons.Add(fileName))
                        {
                            archive.CreateEntryFromFile(resPath, "icons/" + fileName, CompressionLevel.Optimal);
                        }
                    }
                }
            }
            File.Move(tempZip, fullDest, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
        }
    }

    public Configuration ImportBackup(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("The selected backup file could not be found.", sourcePath);

        var fullSource = Path.GetFullPath(sourcePath);
        Directory.CreateDirectory(directory);

        if (fullSource.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var importedJson = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(fullSource), Json)
                ?? throw new JsonException("Empty configuration file.");
            Validate(importedJson);
            Save(importedJson);
            return importedJson;
        }

        using var archive = ZipFile.OpenRead(fullSource);
        var settingsEntry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, "settings.json", StringComparison.OrdinalIgnoreCase) || string.Equals(e.Name, "settings.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The backup archive does not contain a valid settings.json file.");

        Configuration imported;
        using (var stream = settingsEntry.Open())
        {
            imported = JsonSerializer.Deserialize<Configuration>(stream, Json)
                ?? throw new JsonException("Empty configuration in backup archive.");
        }

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("icons/", StringComparison.OrdinalIgnoreCase) &&
                entry.Name.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                var safeName = Path.GetFileName(entry.Name);
                if (!string.IsNullOrEmpty(safeName))
                {
                    var destIconPath = Path.Combine(directory, safeName);
                    entry.ExtractToFile(destIconPath, overwrite: true);
                }
            }
        }

        foreach (var folder in imported.Folders)
        {
            if (folder.Icon is { Kind: ContainerIconKind.Custom, ResourcePath: { } resPath } && !string.IsNullOrWhiteSpace(resPath))
            {
                var fileName = Path.GetFileName(resPath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    var localCandidate = Path.Combine(directory, fileName);
                    if (File.Exists(localCandidate))
                    {
                        folder.Icon.ResourcePath = localCandidate;
                    }
                }
            }
        }

        Validate(imported);
        Save(imported);
        return imported;
    }
}
