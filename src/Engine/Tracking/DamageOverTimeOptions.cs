namespace OverlayMod.Engine.Tracking;

/// <summary>
/// Where the line sits between a hit and a tick of damage over time — poison and
/// toxic being the ones players actually run into, but the rule is written in
/// terms of what a tick looks like rather than what caused it.
///
/// Like <see cref="FallDamageOptions"/> these are thresholds on a heuristic, not
/// constants of the game, so they are editable at runtime and every damage event
/// keeps the size that was measured for it.
/// </summary>
/// <param name="Enabled">
/// When false, nothing is ever attributed to damage over time and a poison tick
/// counts as a hit — which is what 0.2.1 did, and what the report that prompted
/// this asked to change. Left as an escape hatch: an overlay that hides real
/// hits is worse than one that counts a few it should not.
/// </param>
/// <param name="MaxTickDamage">
/// The most health a single tick may cost. Poison and toxic take small, fixed
/// bites; anything that hurts is far above this line and is a hit on sight, with
/// no waiting and no pattern-matching.
/// </param>
/// <param name="MaxIntervalMs">
/// The longest gap between two ticks that still reads as the same effect. Beyond
/// this the run of ticks has ended, and anything still unresolved becomes a hit.
/// Measured in in-game milliseconds, so a loading screen does not age it.
/// </param>
public sealed record DamageOverTimeOptions(bool Enabled, int MaxTickDamage, int MaxIntervalMs)
{
    public static DamageOverTimeOptions Default => new(true, 40, 4000);

    public DamageOverTimeOptions Sanitised() => new(
        Enabled,
        Math.Clamp(MaxTickDamage, 1, 500),

        // The floor is above DamageOverTimeDetector.MinIntervalMs on purpose: a
        // band that excludes itself would classify nothing at all, and silently.
        Math.Clamp(MaxIntervalMs, 1500, 15_000));
}
