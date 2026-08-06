using System.Text.Json;

namespace OverlayMod.Host.Hotkeys;

/// <summary>
/// Which key combinations drive the run. Stored as text in the data directory so
/// they can be changed without a rebuild; defaults are written on first run.
///
/// Ctrl+Alt combinations are the defaults because Dark Souls III does not use
/// them, so they will not collide with anything bound in-game.
/// </summary>
public sealed record HotkeySettings
{
    public bool Enabled { get; init; } = true;
    public string Start { get; init; } = "Ctrl+Alt+S";
    public string Split { get; init; } = "Ctrl+Alt+D";
    public string Reset { get; init; } = "Ctrl+Alt+R";

    public static HotkeySettings Load(string path)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            // Without this the default encoder writes "Ctrl+Alt+S",
            // which is unreadable in a file the user is meant to edit. These
            // files are never embedded in HTML, so relaxed escaping is safe.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(path), options)
                       ?? new HotkeySettings();

            var defaults = new HotkeySettings();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, options));
            return defaults;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new HotkeySettings();
        }
    }
}
