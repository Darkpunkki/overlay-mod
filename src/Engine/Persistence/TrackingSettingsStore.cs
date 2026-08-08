using System.Text.Json;
using System.Text.Json.Serialization;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.Persistence;

/// <summary>
/// Persists how damage is classified: the fall-damage thresholds, and the ones
/// that tell a poison tick from a hit.
///
/// These live in a file rather than in the build because they are the one part
/// of the tracker whose right value cannot be known without the game in front of
/// you. A user whose route drops them off a ledge every lap, or whose boss room
/// is knee-deep in poison swamp, needs to be able to move the line themselves,
/// on the day, without waiting for a release.
/// </summary>
public sealed class TrackingSettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// The file as it is written now. 0.2.1 wrote the fall options at the root
    /// with no wrapper, so <see cref="Load"/> falls back to reading that shape
    /// rather than discarding a user's tuned thresholds on upgrade.
    /// </summary>
    private sealed record FileShape(
        [property: JsonPropertyName("fallDamage")] FallDamageOptions? FallDamage,
        [property: JsonPropertyName("damageOverTime")] DamageOverTimeOptions? DamageOverTime);

    private readonly string _path;
    private readonly object _gate = new();

    private FallDamageOptions _fallDamage = FallDamageOptions.Default;
    private DamageOverTimeOptions _damageOverTime = DamageOverTimeOptions.Default;

    public TrackingSettingsStore(string path)
    {
        _path = path;
        Load();
    }

    public FallDamageOptions FallDamage
    {
        get { lock (_gate) return _fallDamage; }
    }

    public DamageOverTimeOptions DamageOverTime
    {
        get { lock (_gate) return _damageOverTime; }
    }

    /// <summary>
    /// Apply new thresholds and persist them. Either half may be null, meaning
    /// "leave that one alone" — the control page edits one card at a time.
    /// Returns what was actually stored, which is what the caller should display.
    /// </summary>
    public (FallDamageOptions Fall, DamageOverTimeOptions OverTime) Update(
        FallDamageOptions? fallDamage,
        DamageOverTimeOptions? damageOverTime)
    {
        lock (_gate)
        {
            if (fallDamage is not null) _fallDamage = fallDamage.Sanitised();
            if (damageOverTime is not null) _damageOverTime = damageOverTime.Sanitised();

            Save();
            return (_fallDamage, _damageOverTime);
        }
    }

    public (FallDamageOptions Fall, DamageOverTimeOptions OverTime) Reset() =>
        Update(FallDamageOptions.Default, DamageOverTimeOptions.Default);

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var text = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<FileShape>(text, Json);
            if (parsed is null) return;

            if (parsed.FallDamage is { } fall)
            {
                _fallDamage = fall.Sanitised();
            }
            else if (JsonSerializer.Deserialize<FallDamageOptions>(text, Json) is { } legacy)
            {
                // The 0.2.1 shape: the fall options themselves, unwrapped.
                _fallDamage = legacy.Sanitised();
            }

            if (parsed.DamageOverTime is { } overTime) _damageOverTime = Filled(overTime).Sanitised();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A hand-edit gone wrong falls back to the defaults rather than
            // leaving the tracker with no opinion at all.
        }
    }

    /// <summary>
    /// Restore any field the stored file did not have.
    ///
    /// 0.2.2 wrote the tick ceiling as an amount of health (<c>maxTickDamage</c>)
    /// rather than a percentage of it, so a file it wrote leaves
    /// <see cref="DamageOverTimeOptions.MaxTickPercent"/> at zero — which
    /// <see cref="DamageOverTimeOptions.Sanitised"/> would then clamp up to the
    /// smallest legal value and quietly hand that user a ceiling far tighter
    /// than any real tick. Nobody can legitimately have stored a zero, so a zero
    /// means "this field was not there" and the default belongs in its place.
    /// The dropped setting is no loss: it was measured in the wrong unit against
    /// a detector that did not work.
    /// </summary>
    private static DamageOverTimeOptions Filled(DamageOverTimeOptions stored) => stored with
    {
        MaxTickPercent = stored.MaxTickPercent > 0
            ? stored.MaxTickPercent
            : DamageOverTimeOptions.Default.MaxTickPercent,
        MaxIntervalMs = stored.MaxIntervalMs > 0
            ? stored.MaxIntervalMs
            : DamageOverTimeOptions.Default.MaxIntervalMs,
    };

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var payload = new FileShape(_fallDamage, _damageOverTime);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
