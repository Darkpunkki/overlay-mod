using System.Text.Json.Serialization;

namespace OverlayMod.Engine.Tracking;

[JsonConverter(typeof(ChallengeTypeJsonConverter))]
public enum ChallengeType
{
    NoDamage,
    NoHit,
    Deathless,
    Speedrun,
}

/// <summary>The metric a profile is ranked by, and which value each split shows.</summary>
public enum RunMetric
{
    /// <summary>Every drop in health, whatever caused it — falls included.</summary>
    Damage,

    /// <summary>Damage the player was dealt, with fall damage excluded.</summary>
    Hits,

    Deaths,
    Time,
}

/// <summary>
/// A challenge profile decides what a run is judged on and what the overlay
/// shows. Damage, fall damage, deaths and approach/boss times are always
/// recorded internally — they are cheap and a later profile may want them — but a
/// profile only displays what is relevant to it.
///
/// **No Damage versus No Hit.** They differ in one respect: No Damage counts
/// every drop in health, so mistiming a drop costs you the run; No Hit ignores
/// damage the fall detector attributes to landing, so it measures only what the
/// game dealt you. No Damage is the stricter of the two and the one that needs no
/// heuristic to be correct.
/// </summary>
/// <param name="PrimaryMetric">
/// What the run is ranked by. This is also what each split shows, alongside that
/// split's personal best — a run ranked by time wants per-split times to compare,
/// a No-Hit run wants hits. One metric per profile keeps the overlay narrow and
/// keeps the comparison meaningful.
/// </param>
/// <param name="ShowTotalsFooter">
/// Show the totals footer under the split list. Off for Speedrun, where the
/// primary metric is time and the footer would simply repeat the run timer
/// already sitting at the top of the overlay in a larger font.
/// </param>
public sealed record ChallengeProfile(
    ChallengeType Type,
    string Name,
    RunMetric PrimaryMetric,
    bool ShowTotalsFooter)
{
    public static readonly ChallengeProfile NoDamage = new(
        ChallengeType.NoDamage, "No Damage", RunMetric.Damage, ShowTotalsFooter: true);

    public static readonly ChallengeProfile NoHit = new(
        ChallengeType.NoHit, "No Hit", RunMetric.Hits, ShowTotalsFooter: true);

    public static readonly ChallengeProfile Deathless = new(
        ChallengeType.Deathless, "Deathless", RunMetric.Deaths, ShowTotalsFooter: true);

    public static readonly ChallengeProfile Speedrun = new(
        ChallengeType.Speedrun, "Speedrun", RunMetric.Time, ShowTotalsFooter: false);

    public static ChallengeProfile For(ChallengeType type) => type switch
    {
        ChallengeType.NoDamage => NoDamage,
        ChallengeType.NoHit => NoHit,
        ChallengeType.Deathless => Deathless,
        ChallengeType.Speedrun => Speedrun,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static IReadOnlyList<ChallengeProfile> All => new[] { NoDamage, NoHit, Deathless, Speedrun };
}
