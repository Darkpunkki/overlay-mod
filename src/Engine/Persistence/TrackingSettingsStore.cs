using System.Text.Json;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.Persistence;

/// <summary>
/// Persists how damage is classified — currently just the fall-damage
/// thresholds.
///
/// These live in a file rather than in the build because they are the one part
/// of the tracker whose right value cannot be known without the game in front of
/// you. A user whose route drops them off a ledge every lap needs to be able to
/// move the line themselves, on the day, without waiting for a release.
/// </summary>
public sealed class TrackingSettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private FallDamageOptions _fallDamage = FallDamageOptions.Default;

    public TrackingSettingsStore(string path)
    {
        _path = path;
        Load();
    }

    public FallDamageOptions FallDamage
    {
        get { lock (_gate) return _fallDamage; }
    }

    /// <summary>Apply new thresholds and persist them. Returns what was actually stored.</summary>
    public FallDamageOptions Update(FallDamageOptions options)
    {
        lock (_gate)
        {
            _fallDamage = options.Sanitised();
            Save();
            return _fallDamage;
        }
    }

    public FallDamageOptions Reset() => Update(FallDamageOptions.Default);

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var parsed = JsonSerializer.Deserialize<FallDamageOptions>(File.ReadAllText(_path), Json);
            if (parsed is not null) _fallDamage = parsed.Sanitised();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A hand-edit gone wrong falls back to the defaults rather than
            // leaving the tracker with no opinion at all.
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_fallDamage, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
