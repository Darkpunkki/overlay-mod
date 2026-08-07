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
/// Damage and fall damage are counted separately rather than one of them being
/// derived at the point of display, because which one a challenge is judged on
/// changes but both are facts about what happened. A run recorded under No Damage
/// can still be compared against a No Hit best afterwards.
/// </summary>
public sealed class SegmentResult
{
    public int IgtMs { get; internal set; }

    /// <summary>Every drop in health, whatever caused it.</summary>
    public int Damage { get; internal set; }

    /// <summary>The subset of <see cref="Damage"/> attributed to landing.</summary>
    public int FallDamage { get; internal set; }

    public int Deaths { get; internal set; }

    /// <summary>Damage dealt by the game rather than by the ground. What No Hit counts.</summary>
    public int Hits => Damage - FallDamage;
}

/// <summary>Live results for a single split, broken into approach and boss segments.</summary>
public sealed class SplitResult
{
    public string Name { get; }
    public bool IsBoss { get; }
    public SegmentResult Approach { get; } = new();
    public SegmentResult Boss { get; } = new();
    public bool Completed { get; internal set; }

    public SplitResult(string name, bool isBoss)
    {
        Name = name;
        IsBoss = isBoss;
    }

    public int Damage => Approach.Damage + Boss.Damage;
    public int FallDamage => Approach.FallDamage + Boss.FallDamage;
    public int Hits => Approach.Hits + Boss.Hits;
    public int Deaths => Approach.Deaths + Boss.Deaths;
    public int IgtMs => Approach.IgtMs + Boss.IgtMs;

    internal SegmentResult Segment(SegmentKind kind) => kind == SegmentKind.Boss ? Boss : Approach;
}

/// <summary>
/// One drop in health, with the evidence that decided whether it was a fall.
///
/// Kept so the fall detector can be reviewed against a real playthrough instead
/// of taken on trust: <see cref="DescentMetres"/> is the measurement that
/// produced <see cref="CountedAsFall"/>, so a threshold that is set wrong is
/// visible rather than merely suspected.
/// </summary>
public sealed record DamageEvent(
    int IgtMs,
    string SplitName,
    int Hp,
    int MaxHp,
    bool Fatal,
    double DescentMetres,
    bool CountedAsFall);
