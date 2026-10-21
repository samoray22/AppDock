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
    public ConfigurationStore(string? directory = null) => this.directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppBox");

    public Configuration Load()
    {
        Directory.CreateDirectory(directory);
        if (!File.Exists(FilePath)) return new();
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

    private static void Validate(Configuration config)
    {
        if (config.SchemaVersion != 1 || config.Settings is null || config.Folders is null ||
            !Enum.IsDefined(config.Settings.LayoutMode) || !Enum.IsDefined(config.Settings.Theme)) throw new JsonException("Unsupported configuration.");
        var ids = new HashSet<Guid>();
        foreach (var folder in config.Folders)
        {
            if (folder is null || folder.Id == Guid.Empty || !ids.Add(folder.Id) || string.IsNullOrWhiteSpace(folder.Name) || folder.Name.Length > 60 || folder.Applications is null)
                throw new JsonException("Invalid folder.");
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
