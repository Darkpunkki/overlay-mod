using System.Text.Json.Serialization;

namespace OverlayMod.Engine.Tracking;

/// <summary>
/// A serialisable capture of run progress, so a run survives quitting the game —
/// or the overlay host — and picks up where it left off.
///
/// Dark Souls III stores in-game time in the save file, so IGT continues across
/// a restart rather than resetting. That is what makes resuming work: the run
/// timer is an IGT difference, and both ends of that difference survive.
/// </summary>
public sealed record RunState(
    string RouteName,
    int RunStartIgt,
    int CurrentIgt,
    int ActiveIndex,
    RunPhase Phase,
    IReadOnlyList<SplitState> Splits);

/// <summary>One split's accumulated results, flattened for storage.</summary>
public sealed record SplitState(
    string Name,
    bool IsBoss,
    bool Completed,
    int ApproachIgtMs,
    int ApproachDamage,
    int ApproachFallDamage,
    int ApproachDeaths,
    int BossIgtMs,
    int BossDamage,
    int BossFallDamage,
    int BossDeaths)
{
    /// <summary>
    /// What 0.1.0 called hits. It counted every drop in health, which is damage
    /// under the current names — so a run parked by the older version is carried
    /// forward rather than resuming with its damage silently zeroed.
    /// </summary>
    [JsonPropertyName("approachHits")]
    public int? LegacyApproachHits { get; init; }

    [JsonPropertyName("bossHits")]
    public int? LegacyBossHits { get; init; }

    /// <summary>Fold any legacy field into its current equivalent.</summary>
    public SplitState Migrated()
    {
        if (LegacyApproachHits is null && LegacyBossHits is null) return this;

        return this with
        {
            ApproachDamage = ApproachDamage > 0 ? ApproachDamage : LegacyApproachHits ?? 0,
            BossDamage = BossDamage > 0 ? BossDamage : LegacyBossHits ?? 0,
            LegacyApproachHits = null,
            LegacyBossHits = null,
        };
    }
}
