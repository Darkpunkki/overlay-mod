namespace OverlayMod.Engine.Tracking;

/// <summary>
/// Decides whether a drop in health was the ground rather than an enemy, by
/// asking one question: had the player just finished falling?
///
/// **Why height and not a damage source.** Dark Souls III does record what hurt
/// you — fall damage arrives as its own SpEffect — but reading that needs a
/// pointer chain nobody here has found or verified, the same wall the boss-HP
/// offset is behind. Player position is already read every tick and is confirmed
/// working, so this measures the descent instead. It is a heuristic and is
/// described as one; <see cref="FallDamageOptions"/> exists so it can be tuned
/// against a real run, and every damage event keeps the descent it measured so
/// its calls can be reviewed after the fact rather than trusted blindly.
///
/// **What it will get wrong.** A real hit taken within the window of landing is
/// absorbed into the fall; being knocked off a ledge is two separate events and
/// reads correctly. Long, steep, *walked* descents are the plausible false
/// positive, which is what the window guards against: walking cannot cover the
/// threshold distance in half a second.
/// </summary>
public sealed class FallDetector
{
    /// <summary>
    /// Beyond this, two consecutive readings are not the same continuous motion —
    /// a warp, a load, or a poll the loop was too busy to take. Either way the
    /// history in front of the gap says nothing about how the player got here.
    /// </summary>
    private const int MaxSampleGapMs = 200;

    /// <summary>A step no fall produces: at thirty polls a second this is hundreds of metres per second.</summary>
    private const float MaxPlausibleStepMetres = 20f;

    private readonly List<(int TimeMs, float Y)> _samples = new(64);

    public FallDamageOptions Options { get; set; } = FallDamageOptions.Default;

    /// <summary>Record where the player is. Call once per tick while the character exists.</summary>
    public void Observe(int timeMs, float y)
    {
        if (!float.IsFinite(y)) return;

        if (_samples.Count > 0)
        {
            var (lastTime, lastY) = _samples[^1];
            var gap = timeMs - lastTime;

            // In-game time can rewind when a save reloads, and a large step is a
            // teleport rather than a fall. Neither leaves usable history.
            if (gap < 0 || gap > MaxSampleGapMs || Math.Abs(y - lastY) > MaxPlausibleStepMetres) _samples.Clear();
        }

        _samples.Add((timeMs, y));

        // Keep only what the longest allowed window could ask about.
        var cutoff = timeMs - 2000;
        var drop = 0;
        while (drop < _samples.Count && _samples[drop].TimeMs < cutoff) drop++;
        if (drop > 0) _samples.RemoveRange(0, drop);
    }

    /// <summary>Forget the history. Called whenever the character stops existing.</summary>
    public void Clear() => _samples.Clear();

    /// <summary>
    /// How far the player has descended from their highest point inside the
    /// window, ending now. Zero when they have not been falling, and never
    /// negative — climbing is not a fall.
    /// </summary>
    public double DescentMetres(int nowMs)
    {
        if (_samples.Count == 0) return 0;

        var currentY = _samples[^1].Y;
        var from = nowMs - Options.WindowMs;
        var highest = currentY;

        for (var i = _samples.Count - 1; i >= 0; i--)
        {
            if (_samples[i].TimeMs < from) break;
            if (_samples[i].Y > highest) highest = _samples[i].Y;
        }

        return Math.Max(0, highest - currentY);
    }

    /// <summary>Whether a descent of this size counts as landing damage.</summary>
    public bool IsFall(double descentMetres) =>
        Options.Enabled && descentMetres >= Options.DescentMetres;
}
