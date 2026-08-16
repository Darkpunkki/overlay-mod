using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Host;

/// <summary>
/// What the overlay page receives. This is deliberately a view model rather than
/// the engine's own types: the page never learns about pointer chains, and the
/// engine stays free to change internals without breaking the overlay.
/// Serialised as camelCase JSON.
/// </summary>
public sealed record OverlayState(
    bool Attached,
    string Phase,
    string RouteName,
    string ProfileName,
    DisplayView Display,
    int RunIgtMs,
    int TotalDamage,
    int TotalHits,
    int TotalDeaths,
    PrimaryView Primary,
    BestsView Bests,
    PlayerView Player,
    bool BossFightActive,
    int ActiveIndex,
    AttemptsView Attempts,
    IReadOnlyList<SplitView> Splits)
{
    /// <summary>
    /// Bumped whenever the appearance changes. The overlay refetches the settings
    /// only when this moves, which keeps them out of every frame.
    /// </summary>
    public int AppearanceVersion { get; init; }


    /// <param name="labels">
    /// Renames, canonical split name to what the overlay should call it. Applied
    /// here rather than in the route so the personal bests keyed on the canonical
    /// name survive a rename — see <see cref="SplitNameStore"/>.
    /// </param>
    public static OverlayState From(
        RunTracker tracker,
        Route route,
        PersonalBests bests,
        AttemptCount attempts,
        IReadOnlyDictionary<string, string> labels,
        GameSnapshot snapshot)
    {
        var splits = new List<SplitView>(tracker.Splits.Count);
        foreach (var s in tracker.Splits)
        {
            splits.Add(new SplitView(
                s.Name,
                s.IsBoss,
                s.Completed,
                s.IgtMs,
                s.Damage,
                s.Hits,
                s.Deaths,
                SegmentView.From(s.Approach),
                SegmentView.From(s.Boss),
                bests.SplitIgtMs(s.Name),
                bests.SplitDamage(s.Name),
                bests.SplitHits(s.Name),
                bests.SplitDeaths(s.Name))
            {
                Label = labels.TryGetValue(s.Name, out var label) ? label : null,
            });
        }

        var profile = route.Profile;
        var bestPrimary = profile.PrimaryMetric switch
        {
            RunMetric.Damage => bests.BestTotalDamage,
            RunMetric.Hits => bests.BestTotalHits,
            RunMetric.Deaths => bests.BestTotalDeaths,
            RunMetric.Time => bests.BestRunIgtMs,
            _ => null,
        };

        return new OverlayState(
            snapshot.Attached,
            tracker.Phase.ToString(),
            route.Name,
            profile.Name,
            new DisplayView(profile.PrimaryMetric.ToString(), profile.ShowTotalsFooter),
            tracker.RunIgtMs,
            tracker.TotalDamage,
            tracker.TotalHits,
            tracker.TotalDeaths,
            new PrimaryView(profile.PrimaryMetric.ToString(), tracker.PrimaryValue, bestPrimary),
            new BestsView(bests.BestRunIgtMs, bests.BestTotalDamage, bests.BestTotalHits, bests.BestTotalDeaths),
            new PlayerView(snapshot.PlayerLoaded, snapshot.IsLoading),
            snapshot.BossFightActive,
            tracker.ActiveIndex,
            new AttemptsView(attempts.Started, attempts.Finished),
            splits);
    }
}

/// <summary>
/// What this profile wants shown. Keeps presentation decisions in the profile
/// rather than hard-coded in the page, so adding a profile does not mean editing
/// the overlay's rendering logic.
/// </summary>
/// <param name="SplitMetric">
/// Which value each split shows next to its personal best: "Damage", "Hits",
/// "Deaths" or "Time". All four are always sent, so the page picks rather than
/// the server pre-flattening — which keeps the payload shape stable across
/// profiles.
/// </param>
/// <param name="ShowTotals">
/// Whether the totals footer appears. Off for Speedrun, whose primary metric is
/// already the run timer at the top of the overlay.
/// </param>
public sealed record DisplayView(string SplitMetric, bool ShowTotals);

/// <summary>The metric this run is ranked by, alongside the best ever achieved.</summary>
public sealed record PrimaryView(string Metric, int Value, int? Best);

/// <summary>Whole-run bests. Null where the route has never been completed.</summary>
public sealed record BestsView(int? RunIgtMs, int? TotalDamage, int? TotalHits, int? TotalDeaths);

/// <summary>
/// Only what the overlay needs to describe the game's state. Health is
/// deliberately absent: the game's own UI already shows it, so duplicating it
/// costs overlay space and viewer attention for nothing. HP is still read and
/// is what damage and deaths are derived from — it just never reaches the screen.
/// </summary>
public sealed record PlayerView(bool Loaded, bool Loading);

/// <summary>
/// One segment's results. Still sent although nothing displays it today: the
/// approach-versus-boss breakdown returns in Milestone 5, once boss-fight
/// detection has an offset to stand on.
/// </summary>
public sealed record SegmentView(int IgtMs, int Damage, int Hits, int Deaths)
{
    public static SegmentView From(SegmentResult r) => new(r.IgtMs, r.Damage, r.Hits, r.Deaths);
}

/// <summary>
/// How many attempts this route has seen under this challenge, and how many were
/// finished. <paramref name="Finished"/> is sent although the overlay shows only
/// the count: "attempt 214, 3 finished" is a different and sometimes more
/// interesting sentence, and the page can start saying it without a server change.
/// </summary>
public sealed record AttemptsView(int Started, int Finished);

public sealed record SplitView(
    string Name,
    bool IsBoss,
    bool Completed,
    int IgtMs,
    int Damage,
    int Hits,
    int Deaths,
    SegmentView Approach,
    SegmentView Boss,
    int? PbIgtMs,
    int? PbDamage,
    int? PbHits,
    int? PbDeaths)
{
    /// <summary>
    /// What to show instead of <see cref="Name"/>, or null when the split is not
    /// renamed. Null rather than a copy of the name so the overlay's fallback is
    /// the one obvious behaviour and the payload does not repeat itself.
    ///
    /// <see cref="Name"/> stays canonical because it is the key everything else
    /// is filed under — the personal bests above included.
    /// </summary>
    public string? Label { get; init; }
}
