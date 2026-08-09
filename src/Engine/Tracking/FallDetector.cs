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
/// **A fall is a descent, not a window** (0.2.4). This originally asked only how
/// far the player had dropped inside the last half-second, which sees the end of
/// a long fall and not the drop that set it up. Landing on a ledge and sliding
/// straight off it read as under two metres and was charged as a hit, and even
/// the falls it did recognise were reported far shorter than they were. It now
/// follows the descent itself: it begins where the player started losing height,
/// survives the moment of clipping a ledge on the way down, and ends when they
/// stop descending. The window still decides two things, which is what it is
/// good for — whether a descent is fast enough to be a fall at all, and how long
/// after landing the damage may arrive.
///
/// **What it will get wrong.** A real hit taken within the window of landing is
/// absorbed into the fall; being knocked off a ledge is two separate events and
/// reads correctly. Long, steep, *walked* descents are the plausible false
/// positive, which is what the window guards against: walking cannot cover the
/// threshold distance in half a second, so it never arms a descent — and
/// <see cref="MaxEpisodeMs"/> stops a real fall from arming the walk downhill
/// that follows it.
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

    /// <summary>
    /// A pause longer than this ends a descent. Long enough to see through the
    /// moment a player clips a ledge on the way down or bounces off a slope,
    /// short enough that landing and then deliberately walking off somewhere
    /// else is two separate falls rather than one.
    /// </summary>
    private const int RestMs = 250;

    /// <summary>
    /// However long a descent has been going, stop treating it as one fall after
    /// this. Landing on a downhill slope and then trudging down it for ten
    /// seconds is not still the fall, and without a cap the descent would keep
    /// accumulating and write off every hit taken on the way.
    /// </summary>
    private const int MaxEpisodeMs = 3000;

    /// <summary>Height changes smaller than this are noise, not descending.</summary>
    private const float DescendingEpsilonMetres = 0.05f;

    private readonly List<(int TimeMs, float Y)> _samples = new(64);

    // The current descent, tracked across polls rather than re-derived from a
    // fixed window. See the class comment for why a window alone is not enough.
    private bool _episode;
    private bool _episodeQualified;
    private float _episodeTopY;
    private int _episodeStartMs;
    private int _lastDescentMs;

    public FallDamageOptions Options { get; set; } = FallDamageOptions.Default;

    /// <summary>Record where the player is. Call once per tick while the character exists.</summary>
    public void Observe(int timeMs, float y)
    {
        if (!float.IsFinite(y)) return;

        var hasPrevious = _samples.Count > 0;
        var lastY = hasPrevious ? _samples[^1].Y : y;

        if (hasPrevious)
        {
            var gap = timeMs - _samples[^1].TimeMs;

            // In-game time can rewind when a save reloads, and a large step is a
            // teleport rather than a fall. Neither leaves usable history.
            if (gap < 0 || gap > MaxSampleGapMs || Math.Abs(y - lastY) > MaxPlausibleStepMetres)
            {
                _samples.Clear();
                EndEpisode();
                hasPrevious = false;
            }
        }

        _samples.Add((timeMs, y));

        // Keep only what the longest allowed window could ask about.
        var cutoff = timeMs - 2000;
        var drop = 0;
        while (drop < _samples.Count && _samples[drop].TimeMs < cutoff) drop++;
        if (drop > 0) _samples.RemoveRange(0, drop);

        if (hasPrevious && y < lastY - DescendingEpsilonMetres)
        {
            var broken = !_episode
                || timeMs - _lastDescentMs > RestMs
                || timeMs - _episodeStartMs > MaxEpisodeMs;

            if (broken)
            {
                _episode = true;
                _episodeQualified = false;
                _episodeStartMs = timeMs;
                _episodeTopY = lastY;   // the height this descent began from
            }

            _lastDescentMs = timeMs;
            if (lastY > _episodeTopY) _episodeTopY = lastY;
        }

        // Arm the episode the first time it looks like a fall rather than a walk
        // downhill. This is the original test, unchanged — it is only its *reach*
        // that was wrong, never what it recognised.
        if (_episode && !_episodeQualified && WindowedDescent(timeMs) >= Options.DescentMetres)
            _episodeQualified = true;
    }

    /// <summary>Forget the history. Called whenever the character stops existing.</summary>
    public void Clear()
    {
        _samples.Clear();
        EndEpisode();
    }

    private void EndEpisode()
    {
        _episode = false;
        _episodeQualified = false;
    }

    /// <summary>
    /// How far the player has descended from their highest point inside the
    /// window, ending now. Zero when they have not been falling, and never
    /// negative — climbing is not a fall.
    /// </summary>
    public double DescentMetres(int nowMs)
    {
        if (_samples.Count == 0) return 0;

        var windowed = WindowedDescent(nowMs);

        // A descent that has already shown itself to be a fall is measured from
        // where it began, however long ago that was — a drop onto a ledge and an
        // immediate slide off it is one fall, and the window only ever sees the
        // last part of it. The window still decides how long *after* landing the
        // damage may arrive.
        if (_episodeQualified && nowMs - _lastDescentMs <= Options.WindowMs)
            return Math.Max(windowed, Math.Max(0, _episodeTopY - _samples[^1].Y));

        return windowed;
    }

    /// <summary>The original measurement: how far below the highest point of the last window the player is.</summary>
    private double WindowedDescent(int nowMs)
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
