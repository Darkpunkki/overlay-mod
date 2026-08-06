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
    int TotalHits,
    int TotalDeaths,
    PrimaryView Primary,
    BestsView Bests,
    PlayerView Player,
    bool BossFightActive,
    int ActiveIndex,
    IReadOnlyList<SplitView> Splits)
{
    /// <summary>
    /// Bumped whenever the appearance changes. The overlay refetches the settings
    /// only when this moves, which keeps them out of every frame.
    /// </summary>
    public int AppearanceVersion { get; init; }


    public static OverlayState From(
        RunTracker tracker, Route route, PersonalBests bests, GameSnapshot snapshot)
    {
        var splits = new List<SplitView>(tracker.Splits.Count);
        foreach (var s in tracker.Splits)
        {
            splits.Add(new SplitView(
                s.Name,
                s.IsBoss,
                s.Completed,
                s.IgtMs,
                s.Hits,
                s.Deaths,
                SegmentView.From(s.Approach),
                SegmentView.From(s.Boss),
                bests.SplitIgtMs(s.Name),
                bests.SplitHits(s.Name),
                bests.SplitDeaths(s.Name)));
        }

        var profile = route.Profile;
        var bestPrimary = profile.PrimaryMetric switch
        {
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
            new DisplayView(
                profile.PrimaryMetric.ToString(),
                profile.ShowSegmentBreakdown,
                profile.ShowDeaths),
            tracker.RunIgtMs,
            tracker.TotalHits,
            tracker.TotalDeaths,
            new PrimaryView(profile.PrimaryMetric.ToString(), tracker.PrimaryValue, bestPrimary),
            new BestsView(bests.BestRunIgtMs, bests.BestTotalHits, bests.BestTotalDeaths),
            new PlayerView(snapshot.PlayerLoaded, snapshot.IsLoading),
            snapshot.BossFightActive,
            tracker.ActiveIndex,
            splits);
    }
}

/// <summary>
/// What this profile wants shown. Keeps presentation decisions in the profile
/// rather than hard-coded in the page, so adding a profile does not mean editing
/// the overlay's rendering logic.
/// </summary>
/// <param name="SplitMetric">
/// Which value each split shows next to its personal best: "Hits", "Deaths" or
/// "Time". All three are always sent, so the page picks rather than the server
/// pre-flattening — which keeps the payload shape stable across profiles.
/// </param>
public sealed record DisplayView(string SplitMetric, bool ShowSegmentBreakdown, bool ShowDeaths);

/// <summary>The metric this run is ranked by, alongside the best ever achieved.</summary>
public sealed record PrimaryView(string Metric, int Value, int? Best);

/// <summary>Whole-run bests. Null where the route has never been completed.</summary>
public sealed record BestsView(int? RunIgtMs, int? TotalHits, int? TotalDeaths);

/// <summary>
/// Only what the overlay needs to describe the game's state. Health is
/// deliberately absent: the game's own UI already shows it, so duplicating it
/// costs overlay space and viewer attention for nothing. HP is still read and
/// is what hits and deaths are derived from — it just never reaches the screen.
/// </summary>
public sealed record PlayerView(bool Loaded, bool Loading);

public sealed record SegmentView(int IgtMs, int Hits, int Deaths)
{
    public static SegmentView From(SegmentResult r) => new(r.IgtMs, r.Hits, r.Deaths);
}

public sealed record SplitView(
    string Name,
    bool IsBoss,
    bool Completed,
    int IgtMs,
    int Hits,
    int Deaths,
    SegmentView Approach,
    SegmentView Boss,
    int? PbIgtMs,
    int? PbHits,
    int? PbDeaths);
