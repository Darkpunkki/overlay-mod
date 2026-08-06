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
                bests.SplitHits(s.Name)));
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
            new DisplayView(profile.ShowSplitTimes, profile.ShowSegmentBreakdown),
            tracker.RunIgtMs,
            tracker.TotalHits,
            tracker.TotalDeaths,
            new PrimaryView(profile.PrimaryMetric.ToString(), tracker.PrimaryValue, bestPrimary),
            new BestsView(bests.BestRunIgtMs, bests.BestTotalHits, bests.BestTotalDeaths),
            new PlayerView(snapshot.Hp, snapshot.MaxHp, snapshot.PlayerLoaded, snapshot.IsLoading),
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
public sealed record DisplayView(bool ShowSplitTimes, bool ShowSegmentBreakdown);

/// <summary>The metric this run is ranked by, alongside the best ever achieved.</summary>
public sealed record PrimaryView(string Metric, int Value, int? Best);

/// <summary>Whole-run bests. Null where the route has never been completed.</summary>
public sealed record BestsView(int? RunIgtMs, int? TotalHits, int? TotalDeaths);

public sealed record PlayerView(int Hp, int MaxHp, bool Loaded, bool Loading);

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
    int? PbHits);
