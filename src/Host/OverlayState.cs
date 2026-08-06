using OverlayMod.Engine.GameState;
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
    int RunIgtMs,
    int TotalHits,
    int TotalDeaths,
    PrimaryView Primary,
    PlayerView Player,
    bool BossFightActive,
    int ActiveIndex,
    IReadOnlyList<SplitView> Splits)
{
    public static OverlayState From(RunTracker tracker, Route route, GameSnapshot snapshot)
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
                // Personal bests arrive with persistence in Milestone 6.
                PbIgtMs: null,
                PbHits: null));
        }

        return new OverlayState(
            snapshot.Attached,
            tracker.Phase.ToString(),
            route.Name,
            route.Profile.Name,
            tracker.RunIgtMs,
            tracker.TotalHits,
            tracker.TotalDeaths,
            new PrimaryView(route.Profile.PrimaryMetric.ToString(), tracker.PrimaryValue),
            new PlayerView(snapshot.Hp, snapshot.MaxHp, snapshot.PlayerLoaded, snapshot.IsLoading),
            snapshot.BossFightActive,
            tracker.ActiveIndex,
            splits);
    }
}

/// <summary>The metric this run is ranked by, per the challenge profile.</summary>
public sealed record PrimaryView(string Metric, int Value);

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
