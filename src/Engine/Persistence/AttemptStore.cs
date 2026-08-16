using System.Text.Json;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.Persistence;

/// <summary>
/// How many attempts one route has seen under one challenge, and how many of
/// them were carried all the way to the end.
/// </summary>
public sealed record AttemptCount(int Started, int Finished)
{
    public static readonly AttemptCount None = new(0, 0);
}

/// <summary>
/// Counts attempts, per route and challenge, in <c>appdata/attempts.json</c>.
///
/// **Why not in the record store.** That file holds results — times, damage,
/// personal bests — and folds them into minimums. An attempt count is neither a
/// result nor a minimum: it goes up when a run <em>starts</em>, which is exactly
/// the moment nothing has happened yet. Keeping it separate also leaves the
/// record store's shape untouched for the SQLite move that replaces it.
///
/// **Why per challenge and not per route alone.** Changing the challenge already
/// abandons the run in progress, because the thing being measured has changed.
/// The same is true of the attempt count: "my 300th No Hit attempt" and "my 4th
/// Speedrun of the same route" are separate tallies, and adding them together
/// would describe neither.
///
/// The count is deliberately writable. Nobody starts using this on their first
/// attempt — they arrive with a number already in their head, or in LiveSplit —
/// and a counter that insists on starting from one is a counter they will ignore.
/// </summary>
public sealed class AttemptStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Route names are user-facing text and this file is meant to be legible.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Route name → challenge name → count. Nested rather than composite-keyed:
    /// a route may be called anything at all, so any separator character chosen
    /// for a flat key is a name somebody can legitimately use.
    /// </summary>
    private sealed record FileShape(Dictionary<string, Dictionary<string, AttemptCount>> Routes);

    private readonly string _path;
    private readonly object _gate = new();

    private Dictionary<string, Dictionary<string, AttemptCount>> _routes =
        new(StringComparer.OrdinalIgnoreCase);

    public AttemptStore(string path)
    {
        _path = path;
        Load();
    }

    public AttemptCount Get(string routeName, ChallengeType challenge)
    {
        lock (_gate) return Lookup(routeName, challenge);
    }

    /// <summary>Count one more attempt on this route and challenge.</summary>
    public AttemptCount Begin(string routeName, ChallengeType challenge)
    {
        lock (_gate)
        {
            var current = Lookup(routeName, challenge);
            return Store(routeName, challenge, current with { Started = current.Started + 1 });
        }
    }

    /// <summary>
    /// Count one attempt as finished. Deliberately does not also count a start:
    /// every finished run began at <see cref="Begin"/>, and adding one here would
    /// count the same attempt twice.
    /// </summary>
    public AttemptCount Finish(string routeName, ChallengeType challenge)
    {
        lock (_gate)
        {
            var current = Lookup(routeName, challenge);
            return Store(routeName, challenge, current with { Finished = current.Finished + 1 });
        }
    }

    /// <summary>
    /// Set the count outright — for arriving with a tally from somewhere else, or
    /// for correcting one. Negative values are treated as zero, and a count that
    /// claims more finishes than starts is raised to match rather than rejected:
    /// the finished figure is the one the user typed on purpose.
    /// </summary>
    public AttemptCount Set(string routeName, ChallengeType challenge, int started, int finished)
    {
        lock (_gate)
        {
            var f = Math.Max(0, finished);
            return Store(routeName, challenge, new AttemptCount(Math.Max(Math.Max(0, started), f), f));
        }
    }

    public AttemptCount Reset(string routeName, ChallengeType challenge) =>
        Set(routeName, challenge, 0, 0);

    private AttemptCount Lookup(string routeName, ChallengeType challenge) =>
        _routes.TryGetValue(routeName, out var byChallenge)
        && byChallenge.TryGetValue(challenge.ToString(), out var count)
            ? count
            : AttemptCount.None;

    private AttemptCount Store(string routeName, ChallengeType challenge, AttemptCount count)
    {
        if (!_routes.TryGetValue(routeName, out var byChallenge))
            _routes[routeName] = byChallenge = new Dictionary<string, AttemptCount>(StringComparer.Ordinal);

        byChallenge[challenge.ToString()] = count;
        Save();
        return count;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var parsed = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Json);
            if (parsed?.Routes is null) return;

            var loaded = new Dictionary<string, Dictionary<string, AttemptCount>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (route, byChallenge) in parsed.Routes)
            {
                if (byChallenge is null) continue;

                var counts = new Dictionary<string, AttemptCount>(StringComparer.Ordinal);
                foreach (var (challenge, count) in byChallenge)
                {
                    if (count is null) continue;
                    counts[challenge] = new AttemptCount(Math.Max(0, count.Started), Math.Max(0, count.Finished));
                }

                loaded[route] = counts;
            }

            _routes = loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable tally is not worth refusing to run over. Start from
            // zero; the next attempt writes the file afresh.
            _routes = new Dictionary<string, Dictionary<string, AttemptCount>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new FileShape(_routes), Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the count is not worth interrupting a run for.
        }
    }
}
