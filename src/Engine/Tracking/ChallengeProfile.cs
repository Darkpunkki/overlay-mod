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
/// A challenge profile decides what a run is judged on and what each split
/// emphasises. Hits and deaths are always recorded internally (cheap); the
/// primary metric drives PB ranking and the overlay's active-split boxes.
/// </summary>
public sealed record ChallengeProfile(ChallengeType Type, string Name, RunMetric PrimaryMetric)
{
    public static readonly ChallengeProfile NoHit = new(ChallengeType.NoHit, "No-Hit", RunMetric.Hits);
    public static readonly ChallengeProfile Deathless = new(ChallengeType.Deathless, "Deathless", RunMetric.Deaths);
    public static readonly ChallengeProfile AnyPercent = new(ChallengeType.AnyPercent, "Any%", RunMetric.Time);
    public static readonly ChallengeProfile AllBosses = new(ChallengeType.AllBosses, "All Bosses", RunMetric.Time);

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
