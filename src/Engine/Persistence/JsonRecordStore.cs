using System.Text.Json;

namespace OverlayMod.Engine.Persistence;

/// <summary>
/// Keeps finished runs in a single JSON file. Deliberately simple: run history
/// is small (a run per attempt, a handful of splits each), it is useful to be
/// able to read and hand-edit during development, and Milestone 6 will move it
/// to SQLite behind <see cref="IRecordStore"/> anyway.
///
/// Bests are folded incrementally as runs are recorded rather than recomputed on
/// every read, since the overlay asks for them on every poll tick.
/// </summary>
public sealed class JsonRecordStore : IRecordStore
{
    private sealed record FileShape(List<RunRecord> Runs);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<RunRecord> _runs = new();
    private readonly Dictionary<string, PersonalBests> _bests = new(StringComparer.Ordinal);

    public JsonRecordStore(string path)
    {
        _path = path;
        Load();
    }

    public PersonalBests BestsFor(string routeName)
    {
        lock (_gate) return _bests.TryGetValue(routeName, out var b) ? b : PersonalBests.Empty;
    }

    public void Record(RunRecord run)
    {
        lock (_gate)
        {
            _runs.Add(run);
            _bests[run.RouteName] = Fold(BestsForUnlocked(run.RouteName), run);
            Save();
        }
    }

    private PersonalBests BestsForUnlocked(string routeName) =>
        _bests.TryGetValue(routeName, out var b) ? b : PersonalBests.Empty;

    /// <summary>Merge one run into a route's bests, taking the minimum of each metric.</summary>
    private static PersonalBests Fold(PersonalBests bests, RunRecord run)
    {
        var splitHits = new Dictionary<string, int>(bests.BestSplitHits);
        var splitTimes = new Dictionary<string, int>(bests.BestSplitIgtMs);

        foreach (var s in run.Splits)
        {
            splitHits[s.Name] = splitHits.TryGetValue(s.Name, out var h) ? Math.Min(h, s.Hits) : s.Hits;

            // A zero-length split means it was never actually played (an aborted
            // run, or a route edit); it should not become an unbeatable best.
            if (s.IgtMs > 0)
                splitTimes[s.Name] = splitTimes.TryGetValue(s.Name, out var t) ? Math.Min(t, s.IgtMs) : s.IgtMs;
        }

        return new PersonalBests(
            Min(bests.BestRunIgtMs, run.RunIgtMs > 0 ? run.RunIgtMs : null),
            Min(bests.BestTotalHits, run.TotalHits),
            Min(bests.BestTotalDeaths, run.TotalDeaths),
            splitHits,
            splitTimes);
    }

    private static int? Min(int? a, int? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var parsed = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Json);
            if (parsed?.Runs is null) return;

            _runs.AddRange(parsed.Runs);
            foreach (var run in _runs)
                _bests[run.RouteName] = Fold(BestsForUnlocked(run.RouteName), run);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable history must not stop the overlay from
            // running. Start empty; the next finished run rewrites the file.
            _runs.Clear();
            _bests.Clear();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Write beside the target and swap, so an interrupted write cannot leave
        // a half-written history behind.
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileShape(_runs), Json));
        File.Move(temp, _path, overwrite: true);
    }
}
