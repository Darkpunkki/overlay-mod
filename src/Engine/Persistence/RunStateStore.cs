using System.Text.Json;
using System.Text.Json.Serialization;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.Persistence;

/// <summary>
/// Persists the run currently in progress, so closing the overlay (or it
/// crashing) does not destroy an attempt that is still live in the save file.
///
/// This is separate from <see cref="IRecordStore"/> on purpose: that stores
/// finished history, this holds exactly one unfinished run and is cleared the
/// moment it completes.
/// </summary>
public sealed class RunStateStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public RunStateStore(string path) => _path = path;

    public RunState? Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<RunState>(File.ReadAllText(_path), Json)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // An unreadable parked run just means starting fresh.
        }
    }

    public void Save(RunState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a checkpoint is survivable; the run continues in memory.
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
