namespace OverlayMod.Engine.Tracking;

public enum RunPhase
{
    NotStarted,
    Running,
    Finished,
}

/// <summary>The two phases of a split: getting to the boss, and the boss fight itself.</summary>
public enum SegmentKind
{
    Approach,
    Boss,
}

/// <summary>
/// Accumulated results for one segment (approach or boss) of a split.
///
/// Damage is counted once and then whittled down: falls and status ticks are
/// tallied alongside it rather than one figure being derived at the point of
/// display, because which one a challenge is judged on changes but all of them
/// are facts about what happened. A run recorded under No Damage can still be
/// compared against a No Hit best afterwards.
/// </summary>
public sealed class SegmentResult
{
    public int IgtMs { get; internal set; }

    /// <summary>Every drop in health, whatever caused it.</summary>
    public int Damage { get; internal set; }

    /// <summary>The subset of <see cref="Damage"/> attributed to landing.</summary>
    public int FallDamage { get; internal set; }

    /// <summary>The subset of <see cref="Damage"/> attributed to poison, toxic or another effect ticking.</summary>
    public int TickDamage { get; internal set; }

    /// <summary>
    /// Damage small enough to be a status tick, waiting to find out whether more
    /// of the same follows. Excluded from <see cref="Hits"/> while it waits: a
    /// hit that shows up a few seconds late is better than a hit counter that
    /// climbs and falls back for a minute every time the player is poisoned.
    /// </summary>
    public int PendingDamage { get; internal set; }

    public int Deaths { get; internal set; }

    /// <summary>
    /// Damage dealt by an enemy rather than by the ground or by a status effect.
    /// What No Hit counts.
    /// </summary>
    public int Hits => Damage - FallDamage - TickDamage - PendingDamage;
}

/// <summary>Live results for a single split, broken into approach and boss segments.</summary>
public sealed class SplitResult
{
    public string Name { get; }
    public bool IsBoss { get; }
    public SegmentResult Approach { get; } = new();
    public SegmentResult Boss { get; } = new();
    public bool Completed { get; internal set; }

    /// <summary>
    /// Manual correction to the hit count, in either direction. The detectors
    /// are heuristics and the memory read can miss a drop outright, so the
    /// player gets the last word — see <see cref="RunTracker.AdjustHits"/>.
    ///
    /// Kept at the split level rather than inside a segment: a correction says
    /// "this split's count is wrong by N", and the player making it has no way
    /// to know which segment the miscount landed in.
    /// </summary>
    public int HitAdjustment { get; internal set; }

    public SplitResult(string name, bool isBoss)
    {
        Name = name;
        IsBoss = isBoss;
    }

    public int Damage => Approach.Damage + Boss.Damage;
    public int FallDamage => Approach.FallDamage + Boss.FallDamage;
    public int TickDamage => Approach.TickDamage + Boss.TickDamage;
    public int Hits => Approach.Hits + Boss.Hits + HitAdjustment;
    public int Deaths => Approach.Deaths + Boss.Deaths;
    public int IgtMs => Approach.IgtMs + Boss.IgtMs;

    internal SegmentResult Segment(SegmentKind kind) => kind == SegmentKind.Boss ? Boss : Approach;
}

/// <summary>What a drop in health was put down to.</summary>
public enum DamageKind
{
    /// <summary>Small enough to be a status tick; waiting to see whether more follows.</summary>
    Pending,

    /// <summary>An enemy. The only kind No Hit counts.</summary>
    Hit,

    /// <summary>The ground, per <see cref="FallDetector"/>.</summary>
    Fall,

    /// <summary>Poison, toxic or another effect ticking, per <see cref="DamageOverTimeDetector"/>.</summary>
    OverTime,
}

/// <summary>
/// One drop in health, with the evidence behind the verdict reached about it.
///
/// Kept so that both detectors can be reviewed against a real playthrough
/// instead of taken on trust: <see cref="DescentMetres"/> and
/// <see cref="Damage"/> are the measurements that produced <see cref="Kind"/>,
/// so a threshold set wrong is visible rather than merely suspected.
///
/// <see cref="Kind"/> is the one part that is not fixed at construction. A tick
/// of poison cannot be recognised until the next one arrives, so the verdict on
/// a small drop is written a few seconds after the drop itself.
/// </summary>
public sealed record DamageEvent(
    int IgtMs,
    string SplitName,
    int Hp,
    int MaxHp,
    int Damage,
    bool Fatal,
    double DescentMetres)
{
    public DamageKind Kind { get; internal set; } = DamageKind.Pending;

    public bool CountedAsFall => Kind == DamageKind.Fall;
}
