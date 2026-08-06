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

/// <summary>Accumulated results for one segment (approach or boss) of a split.</summary>
public sealed class SegmentResult
{
    public int IgtMs { get; internal set; }
    public int Hits { get; internal set; }
    public int Deaths { get; internal set; }
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

    public int Hits => Approach.Hits + Boss.Hits;
    public int Deaths => Approach.Deaths + Boss.Deaths;
    public int IgtMs => Approach.IgtMs + Boss.IgtMs;

    internal SegmentResult Segment(SegmentKind kind) => kind == SegmentKind.Boss ? Boss : Approach;
}
