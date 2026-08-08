namespace OverlayMod.Engine.Tracking;

/// <summary>
/// Where the line sits between a hit and a tick of damage over time — poison and
/// toxic being the ones players actually run into.
///
/// Like <see cref="FallDamageOptions"/> these are thresholds on a heuristic, not
/// constants of the game, so they are editable at runtime and every damage event
/// keeps the size that was measured for it.
///
/// **The size is a percentage, not an amount** (0.2.3). Poison in Dark Souls III
/// takes a bite proportional to the player's health, so an absolute ceiling in HP
/// is right for one character and wrong for the next: 0.2.2 shipped 40 HP, which
/// is a fifth of an early character's tick and nothing at all to a late one.
/// </summary>
/// <param name="Enabled">
/// When false, nothing is ever attributed to damage over time and a poison tick
/// counts as a hit. Left as an escape hatch: an overlay that hides real hits is
/// worse than one that counts a few it should not.
/// </param>
/// <param name="MaxTickPercent">
/// The most health a single tick may cost, as a percentage of the player's
/// maximum. Poison and toxic take small proportional bites; anything that hurts
/// is far above this line and is a hit on sight, with no waiting and no
/// pattern-matching.
/// </param>
/// <param name="MaxIntervalMs">
/// The longest gap between two ticks that still reads as the same effect. Poison
/// and toxic both tick once a second, so the default leaves generous room
/// without holding an ordinary small hit in suspense for longer than it takes to
/// notice. Measured in in-game milliseconds, so a loading screen does not age it.
/// </param>
public sealed record DamageOverTimeOptions(bool Enabled, double MaxTickPercent, int MaxIntervalMs)
{
    public static DamageOverTimeOptions Default => new(true, 8.0, 2500);

    public DamageOverTimeOptions Sanitised() => new(
        Enabled,
        Math.Clamp(double.IsFinite(MaxTickPercent) ? MaxTickPercent : 8.0, 0.5, 50.0),

        // The floor is above DamageOverTimeDetector.MinIntervalMs on purpose: a
        // band that excludes itself would classify nothing at all, and silently.
        // That is exactly how 0.2.2 failed, from the other end.
        Math.Clamp(MaxIntervalMs, 600, 15_000));

    /// <summary>
    /// The largest bite that could be a tick, for a player whose maximum health
    /// is <paramref name="healthScale"/>. Never returns zero: a ceiling of zero
    /// would classify nothing, which is the failure this whole file guards against.
    /// </summary>
    public int CeilingFor(int healthScale) =>
        Math.Max(1, (int)Math.Round(MaxTickPercent / 100.0 * Math.Max(1, healthScale)));
}
