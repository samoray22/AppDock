using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        config.Settings.IconSize = Math.Clamp(config.Settings.IconSize, 24, 64);
        var ids = new HashSet<Guid>();
        foreach (var folder in config.Folders)
        {
            if (folder is null || folder.Id == Guid.Empty || !ids.Add(folder.Id) || string.IsNullOrWhiteSpace(folder.Name) || folder.Name.Length > 60 || folder.Applications is null)
                throw new JsonException("Invalid folder.");
            folder.Icon ??= new();
            if (!Enum.IsDefined(folder.Icon.Kind)) folder.Icon = new();
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
}
