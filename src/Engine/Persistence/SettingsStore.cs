using System.Text.Json;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.Persistence;

/// <summary>What the user last chose to run. Remembered so the next launch starts where they left off.</summary>
public sealed record Selection(string RouteName, ChallengeType Challenge);

/// <summary>Persists the route and challenge selection to a small JSON file.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // ChallengeType brings its own converter, which tolerates the names 0.1.0
        // wrote. Without it a remembered "Any%" selection would throw on load and
        // silently drop the user back to the default route.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public Selection? Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Selection>(File.ReadAllText(_path), Json)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(Selection selection)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(selection, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failing to remember the choice is not worth interrupting a run for.
        }
    }
}
