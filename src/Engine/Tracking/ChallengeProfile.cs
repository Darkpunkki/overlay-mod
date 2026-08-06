namespace OverlayMod.Engine.Tracking;

public enum ChallengeType
{
    NoHit,
    Deathless,
    AnyPercent,
    AllBosses,
}

/// <summary>The metric a profile is ranked by, and which split box is emphasised.</summary>
public enum RunMetric
{
    Hits,
    Deaths,
    Time,
}

/// <summary>
/// A challenge profile decides what a run is judged on and what the overlay
/// shows. Hits, deaths and approach/boss times are always recorded internally —
/// they are cheap and a later profile may want them — but a profile only
/// displays what is relevant to it. A No-Hit runner cares about hits per boss
/// and the total clock, not per-boss times.
/// </summary>
/// <param name="PrimaryMetric">What the run is ranked by, for PB comparison.</param>
/// <param name="ShowSplitTimes">Show a per-split time column.</param>
/// <param name="ShowSegmentBreakdown">Show approach and boss hits separately rather than combined.</param>
public sealed record ChallengeProfile(
    ChallengeType Type,
    string Name,
    RunMetric PrimaryMetric,
    bool ShowSplitTimes,
    bool ShowSegmentBreakdown)
{
    public static readonly ChallengeProfile NoHit =
        new(ChallengeType.NoHit, "No-Hit", RunMetric.Hits, ShowSplitTimes: false, ShowSegmentBreakdown: false);

    public static readonly ChallengeProfile Deathless =
        new(ChallengeType.Deathless, "Deathless", RunMetric.Deaths, ShowSplitTimes: false, ShowSegmentBreakdown: false);

    public static readonly ChallengeProfile AnyPercent =
        new(ChallengeType.AnyPercent, "Any%", RunMetric.Time, ShowSplitTimes: true, ShowSegmentBreakdown: false);

    public static readonly ChallengeProfile AllBosses =
        new(ChallengeType.AllBosses, "All Bosses", RunMetric.Time, ShowSplitTimes: true, ShowSegmentBreakdown: true);

    public static ChallengeProfile For(ChallengeType type) => type switch
    {
        ChallengeType.NoHit => NoHit,
        ChallengeType.Deathless => Deathless,
        ChallengeType.AnyPercent => AnyPercent,
        ChallengeType.AllBosses => AllBosses,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static IReadOnlyList<ChallengeProfile> All => new[] { NoHit, Deathless, AnyPercent, AllBosses };
}
