using System.Text.Json;
using System.Text.Json.Serialization;
using OverlayMod.Engine.Routes;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.Persistence;

/// <summary>
/// What came of writing a route. Carries the name it was actually stored under,
/// which is the trimmed and bounded form of what was asked for — the caller needs
/// that to re-select the route it just renamed, and guessing at it would drop the
/// selection back to the default at exactly the wrong moment.
/// </summary>
public sealed record RouteSaveResult(bool Saved, string? Error, string? Name)
{
    public static RouteSaveResult Ok(string name) => new(true, null, name);

    public static RouteSaveResult Failed(string error) => new(false, error, null);
}

/// <summary>
/// Loads route files from a directory, seeding the built-ins the first time so
/// there is something to pick before a route editor exists. Files are plain JSON
/// and meant to be hand-edited; a malformed one is skipped with the rest still
/// loading, since one bad file should not leave you with no routes at all.
/// </summary>
public sealed class RouteStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // No JsonStringEnumConverter here on purpose: ChallengeType carries its
        // own converter, which reads names that no longer exist instead of
        // throwing. A throw would not fall back to a default — it would take the
        // whole route file down with it (see Reload), so every install that
        // predates 0.2.0 would silently lose its All Bosses routes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Longest route or split name accepted from the editor. Names arrive over
    /// HTTP and end up in a file name, on the overlay and in the personal-best
    /// keys, so they are bounded rather than trusted.
    /// </summary>
    private const int MaxNameLength = 60;

    /// <summary>Most splits one route may contain. Well past the longest real route, which is 26.</summary>
    private const int MaxSplits = 100;

    private readonly string _directory;
    private readonly object _gate = new();
    private List<RouteFile> _routes = new();

    /// <summary>
    /// Which file each loaded route came from, so a rename or a delete acts on
    /// the file that actually holds it. Deliberately not derived from the name:
    /// a hand-written file may be called anything, and guessing at its path would
    /// leave a duplicate behind on rename and silently fail on delete.
    /// </summary>
    private Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

    public RouteStore(string directory)
    {
        _directory = directory;
        Seed();
        Reload();
    }

    /// <summary>Routes currently on disk, in name order. Never empty in practice.</summary>
    public IReadOnlyList<RouteFile> All
    {
        get { lock (_gate) return _routes; }
    }

    public RouteFile? Find(string name)
    {
        lock (_gate)
        {
            foreach (var r in _routes)
                if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }
    }

    /// <summary>The route to use when nothing is selected, or the selection is gone.</summary>
    public RouteFile Default => Find(BuiltInRoutes.Demo.Name) ?? All[0];

    public void Reload()
    {
        var loaded = new List<RouteFile>();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (Directory.Exists(_directory))
            {
                foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
                {
                    try
                    {
                        var route = JsonSerializer.Deserialize<RouteFile>(File.ReadAllText(path), Json);
                        if (route is not null && !string.IsNullOrWhiteSpace(route.Name) && route.Splits.Count > 0)
                        {
                            loaded.Add(route);
                            paths[route.Name] = path;
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip this file; a hand-edit gone wrong should not take
                        // the other routes down with it.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (loaded.Count == 0) loaded.AddRange(BuiltInRoutes.All);
        loaded.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        lock (_gate)
        {
            _routes = loaded;
            _paths = paths;
        }
    }

    /// <summary>
    /// Write a route from the editor, optionally replacing one under a different
    /// name.
    ///
    /// Renaming is a write plus a delete rather than a move, so the file always
    /// ends up named after the route it holds. <strong>A rename does start the
    /// personal bests over</strong>, because those are keyed on the route name —
    /// the control page says so before it lets you do it.
    /// </summary>
    public RouteSaveResult Save(RouteFile route, string? replacing = null)
    {
        var cleaned = Sanitised(route, out var problem);
        if (cleaned is null) return RouteSaveResult.Failed(problem!);

        lock (_gate)
        {
            // A second route by the same name would make the picker ambiguous and
            // send both routes' personal bests into one bucket. Overwriting is
            // allowed for exactly one case: editing a route under its own name.
            var editingInPlace = replacing is not null
                && string.Equals(replacing, cleaned.Name, StringComparison.OrdinalIgnoreCase);
            var renaming = !string.IsNullOrWhiteSpace(replacing) && !editingInPlace;

            if (FindLocked(cleaned.Name) is not null && !editingInPlace)
                return RouteSaveResult.Failed($"There is already a route called '{cleaned.Name}'.");

            try
            {
                Directory.CreateDirectory(_directory);

                // Overwrite in place where the route already lives, so a
                // hand-created file keeps its own name instead of being
                // duplicated under a slug.
                var path = _paths.TryGetValue(cleaned.Name, out var known)
                    ? known
                    : Path.Combine(_directory, FileNameFor(cleaned.Name));

                File.WriteAllText(path, JsonSerializer.Serialize(cleaned, Json));

                if (renaming) DeleteFileFor(replacing!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return RouteSaveResult.Failed("Could not write the route file. Is the folder read-only?");
            }
        }

        Reload();
        return RouteSaveResult.Ok(cleaned.Name);
    }

    /// <summary>
    /// Delete a route's file. Returns false when there was nothing to delete —
    /// which includes a built-in that only exists in memory because the folder is
    /// unwritable. Built-ins are recoverable with <see cref="RestoreBuiltIns"/>,
    /// which is why deleting one is allowed at all.
    /// </summary>
    public bool Delete(string name)
    {
        bool deleted;
        lock (_gate) deleted = DeleteFileFor(name);

        if (deleted) Reload();
        return deleted;
    }

    /// <summary>Remove the file holding this route. Caller holds the lock.</summary>
    private bool DeleteFileFor(string name)
    {
        try
        {
            if (_paths.TryGetValue(name, out var path) && File.Exists(path))
            {
                File.Delete(path);
                return true;
            }

            // Not loaded from a file we know about: fall back to the name this
            // route would have been written under. A name with no letters or
            // digits in it slugs to nothing, and deleting "appdata/routes/.json"
            // would be a different file entirely.
            var fileName = FileNameFor(name);
            var slug = Path.Combine(_directory, fileName);
            if (fileName != ".json" && File.Exists(slug))
            {
                File.Delete(slug);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    /// <summary>
    /// Bring a route from the editor into a shape that is safe to write and to
    /// display: trimmed, bounded, and with at least one split. Returns null and
    /// sets <paramref name="problem"/> when it cannot be.
    /// </summary>
    private static RouteFile? Sanitised(RouteFile route, out string? problem)
    {
        problem = null;

        var name = (route.Name ?? "").Trim();
        if (name.Length == 0) { problem = "A route needs a name."; return null; }
        if (name.Length > MaxNameLength) name = name[..MaxNameLength].TrimEnd();

        // The file name is derived from the route name, so a name made entirely
        // of punctuation has nowhere to be written to.
        if (FileNameFor(name) == ".json")
        {
            problem = "That name has no letters or digits in it, so there is no file name to give it.";
            return null;
        }

        var splits = new List<RouteSplitFile>(Math.Min(route.Splits?.Count ?? 0, MaxSplits));
        foreach (var split in route.Splits ?? Array.Empty<RouteSplitFile>())
        {
            if (splits.Count >= MaxSplits) break;

            var splitName = (split?.Name ?? "").Trim();
            if (splitName.Length == 0) continue;
            if (splitName.Length > MaxNameLength) splitName = splitName[..MaxNameLength].TrimEnd();

            splits.Add(new RouteSplitFile(splitName, split!.IsBoss, split.DefeatFlagId));
        }

        if (splits.Count == 0) { problem = "A route needs at least one split."; return null; }

        return new RouteFile(name, route.DefaultChallenge, splits)
        {
            // Only a live game can earn this, and the editor is not a live game.
            FlagsVerified = false,
        };
    }

    private RouteFile? FindLocked(string name)
    {
        foreach (var r in _routes)
            if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) return r;
        return null;
    }

    /// <summary>
    /// Write any built-in route that is not already on disk, and reload.
    /// Returns how many were added.
    ///
    /// This is the deliberate counterpart to <see cref="Seed"/> only running on
    /// an empty directory: automatic seeding would resurrect routes the user
    /// deleted, so instead new built-ins — and ones removed by accident — arrive
    /// when explicitly asked for.
    /// </summary>
    public int RestoreBuiltIns()
    {
        var added = 0;

        try
        {
            Directory.CreateDirectory(_directory);
            foreach (var route in BuiltInRoutes.All)
            {
                if (Find(route.Name) is not null) continue;

                var path = Path.Combine(_directory, FileNameFor(route.Name));
                if (File.Exists(path)) continue;

                File.WriteAllText(path, JsonSerializer.Serialize(route, Json));
                added++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (added > 0) Reload();
        return added;
    }

    /// <summary>
    /// Write the built-in routes, but only when there are no route files at all.
    /// Seeding per-missing-file would resurrect a built-in the user deliberately
    /// deleted, leaving no way to be rid of it. New built-ins reach an existing
    /// install through <see cref="RestoreBuiltIns"/> instead.
    /// </summary>
    private void Seed()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            if (Directory.EnumerateFiles(_directory, "*.json").Any()) return;

            foreach (var route in BuiltInRoutes.All)
                File.WriteAllText(
                    Path.Combine(_directory, FileNameFor(route.Name)),
                    JsonSerializer.Serialize(route, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only location: fall back to the in-memory built-ins.
        }
    }

    /// <summary>Turn a route name into a tidy file name: lower case, runs of punctuation collapsed to one dash.</summary>
    private static string FileNameFor(string routeName)
    {
        var slug = new System.Text.StringBuilder(routeName.Length);
        foreach (var c in routeName)
        {
            if (char.IsLetterOrDigit(c)) slug.Append(char.ToLowerInvariant(c));
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        return slug.ToString().Trim('-') + ".json";
    }
}
