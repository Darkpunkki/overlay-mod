namespace OverlayMod.Engine.GameState;

/// <summary>
/// An immutable snapshot of the values we poll from the game each tick. The run
/// tracker consumes a stream of these; tests can synthesize them directly with
/// no game attached.
/// </summary>
public readonly record struct GameSnapshot
{
    /// <summary>True when attached to a live game process with pointers resolved.</summary>
    public bool Attached { get; init; }

    /// <summary>In-game time in milliseconds (pauses during loads); the canonical run timer.</summary>
    public int IgtMs { get; init; }

    /// <summary>True while a loading screen is shown.</summary>
    public bool IsLoading { get; init; }

    /// <summary>True when the player character object is allocated (i.e. in a level).</summary>
    public bool PlayerLoaded { get; init; }

    /// <summary>Player current and max HP. (Verified live against the game.)</summary>
    public int Hp { get; init; }
    public int MaxHp { get; init; }

    /// <summary>
    /// True while a tracked boss fight is active. This is the seam that drives
    /// approach-vs-boss attribution. Populated from boss-HP detection once that
    /// offset is found; until then it stays false (everything counts as approach)
    /// and the run tracker is driven by this flag generically.
    /// </summary>
    public bool BossFightActive { get; init; }

    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }

    public static GameSnapshot Detached => new() { Attached = false };
}
