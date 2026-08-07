namespace OverlayMod.Engine.Tracking;

/// <summary>
/// How aggressively damage is attributed to landing rather than to an enemy.
///
/// These are thresholds on a heuristic, not constants of the game, so they are
/// editable at runtime: the only way to know whether 3 metres in half a second
/// is the right line is to watch it against a real playthrough. Damage events
/// keep the descent that was measured for exactly that reason.
/// </summary>
/// <param name="Enabled">
/// When false, nothing is ever attributed to a fall and No Hit counts what No
/// Damage counts. That is the honest setting if the detector is misjudging a
/// particular route — an overlay that undercounts hits is worse than one that
/// admits it cannot tell.
/// </param>
/// <param name="DescentMetres">How far the player must have dropped for landing damage to be plausible.</param>
/// <param name="WindowMs">How recently that drop must have happened, in in-game milliseconds.</param>
public sealed record FallDamageOptions(bool Enabled, double DescentMetres, int WindowMs)
{
    public static FallDamageOptions Default => new(true, 3.0, 500);

    public FallDamageOptions Sanitised() => new(
        Enabled,
        Math.Clamp(double.IsFinite(DescentMetres) ? DescentMetres : 3.0, 0.5, 50.0),
        Math.Clamp(WindowMs, 100, 2000));
}
