using System.Text.Json;
using OverlayMod.Engine.Routes;

namespace OverlayMod.Engine.Persistence;

/// <summary>
/// What each split is <em>called on the overlay</em>, when that should differ
/// from what it is called in the route file. "Soul of Cinder" becomes "Cinder",
/// "Lothric, Younger Prince" becomes "Twin Princes", and a viewer reads the
/// split list at a glance instead of squinting at it.
///
/// **A view, laid over the route, never written into it.** Renaming a split by
/// editing the route file would work too, and would be wrong: personal bests are
/// keyed on the split's name, so a rename there silently orphans every gold split
/// behind that boss. Here the route keeps the canonical name, the store maps it
/// to a label at the moment of projection, and the history underneath is
/// untouched. It also means one map covers every route at once — a player who
/// shortens Aldrich does not have to do it again in each file that contains him.
///
/// Empty is the default. Nothing is pre-filled, because the honest starting point
/// is the name the route actually contains; <see cref="ShortNames"/> is one
/// button away for anyone who wants the short forms.
/// </summary>
public sealed class SplitNameStore
{
    /// <summary>
    /// How long a label may be. The overlay ellipsizes anyway, so this is not
    /// about layout — it is about a pasted essay arriving over HTTP and being
    /// written to disk and into every frame of the state stream thereafter.
    /// </summary>
    private const int MaxLabelLength = 40;

    /// <summary>
    /// How many renames are kept. Far above any real route list, and low enough
    /// that a malformed or malicious POST cannot grow the file without bound.
    /// </summary>
    private const int MaxEntries = 500;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The file is meant to be hand-edited, so accept the wrapper key written
        // either way. Only the wrapper is affected: dictionary keys are the split
        // names themselves and are never case-folded.
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        // Split names are user-facing text in a file people will open and edit.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record FileShape(Dictionary<string, string> Names);

    private readonly string _path;
    private readonly object _gate = new();

    private IReadOnlyDictionary<string, string> _names = Empty;

    private static Dictionary<string, string> Empty => new(StringComparer.Ordinal);

    public SplitNameStore(string path)
    {
        _path = path;
        Load();
    }

    /// <summary>Every rename in effect, canonical name to label.</summary>
    public IReadOnlyDictionary<string, string> All
    {
        get { lock (_gate) return _names; }
    }

    /// <summary>
    /// The label for one split, or null when it is not renamed. Null rather than
    /// the canonical name so the projection can send the field only when it says
    /// something, and the overlay can fall back on its own.
    /// </summary>
    public string? Label(string canonical)
    {
        lock (_gate) return _names.TryGetValue(canonical, out var label) ? label : null;
    }

    /// <summary>
    /// Replace the whole map. A full replace rather than a merge because the
    /// control page edits every row of the current route at once, and clearing a
    /// box has to mean "stop renaming this" — which a merge cannot express.
    /// </summary>
    public IReadOnlyDictionary<string, string> Update(IReadOnlyDictionary<string, string>? names)
    {
        lock (_gate)
        {
            _names = Sanitised(names);
            Save();
            return _names;
        }
    }

    /// <summary>
    /// Fill in the short form of every boss this build knows about, <em>keeping
    /// any rename already set</em>. A name somebody chose by hand outranks the
    /// preset — pressing this should never quietly undo their own work, and
    /// clearing the map is right there for anyone who wants to start over.
    /// </summary>
    public IReadOnlyDictionary<string, string> ApplyShortNames()
    {
        lock (_gate)
        {
            var merged = new Dictionary<string, string>(_names, StringComparer.Ordinal);
            foreach (var (canonical, label) in ShortNames.All) merged.TryAdd(canonical, label);

            _names = Sanitised(merged);
            Save();
            return _names;
        }
    }

    public IReadOnlyDictionary<string, string> Clear() => Update(null);

    /// <summary>
    /// Drop anything that would not survive being displayed: blank keys and
    /// labels, a label identical to the name it replaces (an entry that changes
    /// nothing is just a row to scroll past), and anything past the caps.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Sanitised(IReadOnlyDictionary<string, string>? names)
    {
        var clean = Empty;
        if (names is null) return clean;

        foreach (var (canonical, label) in names)
        {
            if (clean.Count >= MaxEntries) break;
            if (string.IsNullOrWhiteSpace(canonical) || string.IsNullOrWhiteSpace(label)) continue;

            var key = canonical.Trim();
            var value = label.Trim();
            if (value.Length > MaxLabelLength) value = value[..MaxLabelLength].TrimEnd();
            if (value.Length == 0 || string.Equals(key, value, StringComparison.Ordinal)) continue;

            clean[key] = value;
        }

        return clean;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var parsed = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Json);
            _names = Sanitised(parsed?.Names);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A hand-edit gone wrong shows the canonical names rather than none.
            _names = Empty;
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var payload = new FileShape(new Dictionary<string, string>(_names, StringComparer.Ordinal));
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
