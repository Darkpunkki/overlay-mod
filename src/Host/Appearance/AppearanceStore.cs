using System.Text.Json;

namespace OverlayMod.Host.Appearance;

/// <summary>
/// Holds the current appearance and persists it.
///
/// Carries a version that increments on every change. The overlay watches that
/// number in the state stream and refetches only when it moves, so a restyle
/// reaches an OBS Browser Source immediately without the settings themselves
/// riding along in every frame thirty times a second.
/// </summary>
public sealed class AppearanceStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();

    private AppearanceSettings _current;
    private int _version;

    public AppearanceStore(string path)
    {
        _path = path;
        _current = Load();
    }

    public AppearanceSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public int Version
    {
        get { lock (_gate) return _version; }
    }

    public AppearanceSettings Update(AppearanceSettings settings)
    {
        var sanitised = settings.Sanitised();

        lock (_gate)
        {
            _current = sanitised;
            _version++;
            Save(sanitised);
        }

        return sanitised;
    }

    public AppearanceSettings Reset() => Update(AppearanceSettings.Default);

    private AppearanceSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return AppearanceSettings.Default;

            var parsed = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(_path), Json);
            return (parsed ?? AppearanceSettings.Default).Sanitised();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AppearanceSettings.Default;
        }
    }

    private void Save(AppearanceSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a look is not worth interrupting a run for.
        }
    }
}
