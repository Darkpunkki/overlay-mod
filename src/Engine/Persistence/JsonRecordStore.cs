using System.Text.Json;
using System.Text.Json.Serialization;

namespace OverlayMod.Engine.Persistence;

/// <summary>
/// Keeps run history in a single JSON file. Deliberately simple: history is
/// small, it is useful to be able to read and hand-edit during development, and
/// Milestone 6 will move it to SQLite behind <see cref="IRecordStore"/> anyway.
///
/// Two kinds of best are stored, because they mean different things:
///
///  - **Whole-run bests** (damage, hits, deaths, time) are folded from finished
///    runs only. A total from an abandoned attempt is not comparable.
///  - **Per-split bests** are stored in their own right and updated the moment a
///    split completes, whether or not the run is ever finished. Most attempts
///    end early, so requiring a completed run would throw away nearly every
///    boss result a player ever produces.
///
/// **Schema 2 (0.2.0)** split "hits" into damage and hits. See <see cref="Migrate"/>.
/// </summary>
public sealed class JsonRecordStore : IRecordStore
{
    /// <summary>
    /// Bumped when the meaning of a stored field changes. Version 1 (implicit —
    /// the field did not exist) recorded one counter called "hits" that included
    /// fall damage.
    /// </summary>
    private const int CurrentSchema = 2;

    private sealed record FileShape(
        List<RunRecord> Runs,
        Dictionary<string, Dictionary<string, SplitRecord>>? SplitBests)
    {
        public int Schema { get; init; }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Route and split names are user-facing text; keep them readable rather
        // than escaped into \uXXXX in a file people may open.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // A best that has never been set is absent, not null.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<RunRecord> _runs = new();

    /// <summary>Whole-run bests per route, folded from finished runs.</summary>
    private readonly Dictionary<string, RunBest> _runBests = new(StringComparer.Ordinal);

    /// <summary>Best result per split, per route, from any attempt that got there.</summary>
    private readonly Dictionary<string, Dictionary<string, SplitRecord>> _splitBests = new(StringComparer.Ordinal);

    private sealed record RunBest(int? IgtMs, int? Damage, int? Hits, int? Deaths)
    {
        public static readonly RunBest None = new(null, null, null, null);
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

            var damage = new Dictionary<string, int>(splits.Count);
            var hits = new Dictionary<string, int>(splits.Count);
            var deaths = new Dictionary<string, int>(splits.Count);
            var times = new Dictionary<string, int>(splits.Count);

            foreach (var (name, best) in splits)
            {
                damage[name] = best.Damage;
                if (best.Hits is { } h) hits[name] = h;
                deaths[name] = best.Deaths;
                if (best.IgtMs > 0) times[name] = best.IgtMs;
            }

            return new PersonalBests(run.IgtMs, run.Damage, run.Hits, run.Deaths, damage, hits, deaths, times);
        }
    }

    public void Record(RunRecord run)
    {
        lock (_gate)
        {
            _runs.Add(run);
            FoldRun(run);

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

    public void CorrectSplitHits(string routeName, string splitName, int hits)
    {
        lock (_gate)
        {
            // A split that was never banked has nothing standing to correct. The
            // live run holds the corrected count; it banks normally on completion.
            if (!_splitBests.TryGetValue(routeName, out var forRoute)) return;
            if (!forRoute.TryGetValue(splitName, out var current)) return;
            if (current.Hits == hits) return;

            forRoute[splitName] = current with { Hits = hits };
            Save();
        }
    }

    private void FoldRun(RunRecord run)
    {
        var previous = _runBests.TryGetValue(run.RouteName, out var b) ? b : RunBest.None;
        _runBests[run.RouteName] = new RunBest(
            Min(previous.IgtMs, run.RunIgtMs > 0 ? run.RunIgtMs : null),
            Min(previous.Damage, run.TotalDamage),
            Min(previous.Hits, run.TotalHits),
            Min(previous.Deaths, run.TotalDeaths));
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
            Math.Min(current.Damage, split.Damage),
            Math.Min(current.Deaths, split.Deaths))
        {
            // Null means "never measured", which must not win a minimum against
            // a real result — nor be overwritten by one.
            Hits = Min(current.Hits, split.Hits),
        };

        if (merged == current) return false;

        forRoute[split.Name] = merged;
        return true;
    }

    private static int? Min(int? a, int? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    /// <summary>
    /// Bring a file written before 0.2.0 forward.
    ///
    /// That version had one counter, called hits, which went up on every drop in
    /// health — fall damage included. That is precisely what damage means now, so
    /// the old numbers move across intact and become No Damage bests. The hit
    /// count is left null rather than copied: nothing in an old file records
    /// whether a given hit was the ground, so a No Hit best cannot be recovered
    /// from it and inventing one would put an unbeatable target on screen.
    /// </summary>
    private static RunRecord Migrate(RunRecord run) => run with
    {
        TotalDamage = run.TotalDamage > 0 ? run.TotalDamage : run.TotalHits ?? 0,
        TotalHits = null,
        Splits = run.Splits.Select(Migrate).ToList(),
    };

    private static SplitRecord Migrate(SplitRecord split) => split with
    {
        Damage = split.Damage > 0 ? split.Damage : split.Hits ?? 0,
        Hits = null,
    };

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var parsed = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Json);
            if (parsed is null) return;

            var legacy = parsed.Schema < CurrentSchema;

            if (parsed.Runs is not null)
            {
                foreach (var run in parsed.Runs) _runs.Add(legacy ? Migrate(run) : run);
                foreach (var run in _runs) FoldRun(run);
            }

            if (parsed.SplitBests is not null)
            {
                foreach (var (route, splits) in parsed.SplitBests)
                foreach (var (_, split) in splits)
                    FoldSplit(route, legacy ? Migrate(split) : split);
            }
            else
            {
                // A file written before split bests were stored separately: rebuild
                // them from the finished runs so existing history is not lost.
                foreach (var run in _runs)
                foreach (var split in run.Splits)
                    FoldSplit(run.RouteName, split);
            }

            if (legacy) Save();
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
            var shape = new FileShape(_runs, _splitBests) { Schema = CurrentSchema };
            File.WriteAllText(temp, JsonSerializer.Serialize(shape, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
