using System.Text.Json;

namespace OverlayMod.Engine.Persistence;

/// <summary>
/// Keeps run history in a single JSON file. Deliberately simple: history is
/// small, it is useful to be able to read and hand-edit during development, and
/// Milestone 6 will move it to SQLite behind <see cref="IRecordStore"/> anyway.
///
/// Two kinds of best are stored, because they mean different things:
///
///  - **Whole-run bests** (total hits, deaths, time) are folded from finished
///    runs only. A total from an abandoned attempt is not comparable.
///  - **Per-split bests** are stored in their own right and updated the moment a
///    split completes, whether or not the run is ever finished. Most attempts
///    end early, so requiring a completed run would throw away nearly every
///    boss result a player ever produces.
/// </summary>
public sealed class JsonRecordStore : IRecordStore
{
    private sealed record FileShape(
        List<RunRecord> Runs,
        Dictionary<string, Dictionary<string, SplitRecord>>? SplitBests);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Route and split names are user-facing text; keep them readable rather
        // than escaped into \uXXXX in a file people may open.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<RunRecord> _runs = new();

    /// <summary>Whole-run bests per route, folded from finished runs.</summary>
    private readonly Dictionary<string, RunBest> _runBests = new(StringComparer.Ordinal);

    /// <summary>Best result per split, per route, from any attempt that got there.</summary>
    private readonly Dictionary<string, Dictionary<string, SplitRecord>> _splitBests = new(StringComparer.Ordinal);

    private sealed record RunBest(int? IgtMs, int? Hits, int? Deaths)
    {
        public static readonly RunBest None = new(null, null, null);
    }

    public JsonRecordStore(string path)
    {
        _path = path;
        Load();
    }

    public PersonalBests BestsFor(string routeName)
    {
        lock (_gate)
        {
            var run = _runBests.TryGetValue(routeName, out var r) ? r : RunBest.None;
            var splits = _splitBests.TryGetValue(routeName, out var s) ? s : new Dictionary<string, SplitRecord>();

            var hits = new Dictionary<string, int>(splits.Count);
            var deaths = new Dictionary<string, int>(splits.Count);
            var times = new Dictionary<string, int>(splits.Count);

            foreach (var (name, best) in splits)
            {
                hits[name] = best.Hits;
                deaths[name] = best.Deaths;
                if (best.IgtMs > 0) times[name] = best.IgtMs;
            }

            return new PersonalBests(run.IgtMs, run.Hits, run.Deaths, hits, deaths, times);
        }
    }

    public void Record(RunRecord run)
    {
        lock (_gate)
        {
            _runs.Add(run);

            var previous = _runBests.TryGetValue(run.RouteName, out var b) ? b : RunBest.None;
            _runBests[run.RouteName] = new RunBest(
                Min(previous.IgtMs, run.RunIgtMs > 0 ? run.RunIgtMs : null),
                Min(previous.Hits, run.TotalHits),
                Min(previous.Deaths, run.TotalDeaths));

            // Folding these again is harmless: each is a minimum, so a split
            // already recorded when it completed simply stays where it is.
            foreach (var split in run.Splits) FoldSplit(run.RouteName, split);

            Save();
        }
    }

    public void RecordSplit(string routeName, SplitRecord split)
    {
        lock (_gate)
        {
            if (!FoldSplit(routeName, split)) return;
            Save();
        }
    }

    /// <summary>Merge one split result into the bests. Returns true if anything changed.</summary>
    private bool FoldSplit(string routeName, SplitRecord split)
    {
        if (!_splitBests.TryGetValue(routeName, out var forRoute))
            _splitBests[routeName] = forRoute = new Dictionary<string, SplitRecord>(StringComparer.Ordinal);

        if (!forRoute.TryGetValue(split.Name, out var current))
        {
            forRoute[split.Name] = split;
            return true;
        }

        // A zero-length split was never really played — an aborted run, or a
        // route edit — and must not become an unbeatable time.
        var igt = current.IgtMs > 0 && split.IgtMs > 0 ? Math.Min(current.IgtMs, split.IgtMs)
            : Math.Max(current.IgtMs, split.IgtMs);

        var merged = new SplitRecord(
            split.Name,
            igt,
            Math.Min(current.Hits, split.Hits),
            Math.Min(current.Deaths, split.Deaths));

        if (merged == current) return false;

        forRoute[split.Name] = merged;
        return true;
    }

    private static int? Min(int? a, int? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var parsed = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Json);
            if (parsed is null) return;

            if (parsed.Runs is not null)
            {
                _runs.AddRange(parsed.Runs);
                foreach (var run in _runs)
                {
                    var previous = _runBests.TryGetValue(run.RouteName, out var b) ? b : RunBest.None;
                    _runBests[run.RouteName] = new RunBest(
                        Min(previous.IgtMs, run.RunIgtMs > 0 ? run.RunIgtMs : null),
                        Min(previous.Hits, run.TotalHits),
                        Min(previous.Deaths, run.TotalDeaths));
                }
            }

            if (parsed.SplitBests is not null)
            {
                foreach (var (route, splits) in parsed.SplitBests)
                foreach (var (_, split) in splits)
                    FoldSplit(route, split);
            }
            else
            {
                // A file written before split bests were stored separately: rebuild
                // them from the finished runs so existing history is not lost.
                foreach (var run in _runs)
                foreach (var split in run.Splits)
                    FoldSplit(run.RouteName, split);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable history must not stop the overlay from
            // running. Start empty; the next result rewrites the file.
            _runs.Clear();
            _runBests.Clear();
            _splitBests.Clear();
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Write beside the target and swap, so an interrupted write cannot
            // leave a half-written history behind.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new FileShape(_runs, _splitBests), Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
