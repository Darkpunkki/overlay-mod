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
/// <param name="PrimaryMetric">
/// What the run is ranked by. This is also what each split shows, alongside that
/// split's personal best — a run ranked by time wants per-split times to compare,
/// a No-Hit run wants hits. One metric per profile keeps the overlay narrow and
/// keeps the comparison meaningful.
/// </param>
/// <param name="ShowSegmentBreakdown">Show approach and boss hits separately rather than combined.</param>
/// <param name="ShowDeaths">
/// Show a separate death total. Off for No-Hit, where a death is a failed run
/// rather than a statistic, and off for Deathless, where deaths are already the
/// primary metric and would simply appear twice.
/// </param>
public sealed record ChallengeProfile(
    ChallengeType Type,
    string Name,
    RunMetric PrimaryMetric,
    bool ShowSegmentBreakdown,
    bool ShowDeaths)
{
    public static readonly ChallengeProfile NoHit = new(
        ChallengeType.NoHit, "No-Hit", RunMetric.Hits,
        ShowSegmentBreakdown: false, ShowDeaths: false);

    public static readonly ChallengeProfile Deathless = new(
        ChallengeType.Deathless, "Deathless", RunMetric.Deaths,
        ShowSegmentBreakdown: false, ShowDeaths: false);

    public static readonly ChallengeProfile AnyPercent = new(
        ChallengeType.AnyPercent, "Any%", RunMetric.Time,
        ShowSegmentBreakdown: false, ShowDeaths: true);

    public static readonly ChallengeProfile AllBosses = new(
        ChallengeType.AllBosses, "All Bosses", RunMetric.Time,
        ShowSegmentBreakdown: true, ShowDeaths: true);

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
