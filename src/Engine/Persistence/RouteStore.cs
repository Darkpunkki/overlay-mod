using System.Text.Json;
using System.Text.Json.Serialization;
using OverlayMod.Engine.Routes;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.Persistence;

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

    private readonly string _directory;
    private readonly object _gate = new();
    private List<RouteFile> _routes = new();

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
                            loaded.Add(route);
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

        lock (_gate) _routes = loaded;
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
