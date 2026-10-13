using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qingying.Core;

namespace Qingying;

public sealed class UserSettings
{
    public int Volume { get; set; } = 80;

    public bool IsMuted { get; set; }

    public double Rate { get; set; } = 1;

    public RepeatMode Repeat { get; set; } = RepeatMode.Off;

    public AspectMode Aspect { get; set; } = AspectMode.Fit;

    public bool PlaylistVisible { get; set; } = true;

    public double PlaylistWidth { get; set; } = 320;

    public double WindowWidth { get; set; } = 1120;

    public double WindowHeight { get; set; } = 700;

    public List<string> Playlist { get; set; } = [];

    public List<PlaylistSnapshot> PlaylistTree { get; set; } = [];

    public int LastIndex { get; set; } = -1;

    public double Brightness { get; set; } = 1;

    public double Contrast { get; set; } = 1;

    public double Saturation { get; set; } = 1;

    public double Gamma { get; set; } = 1;

    public int Hue { get; set; }

    public string SnapshotFolder { get; set; } = string.Empty;

    public ShortcutSettings Shortcuts { get; set; } = new();
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public SettingsStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Qingying");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "settings.json");
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }
}
