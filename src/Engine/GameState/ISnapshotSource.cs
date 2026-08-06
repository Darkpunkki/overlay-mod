using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.GameState;

/// <summary>
/// Where the poll loop gets its values. <see cref="Ds3Reader"/> is the live
/// implementation; <see cref="FakeSnapshotSource"/> replays a scripted run so
/// the overlay can be built and tested with the game closed.
/// </summary>
public interface ISnapshotSource : IDisposable
{
    /// <summary>Human-readable description of the source, for startup logging.</summary>
    string Description { get; }

    bool Attached { get; }

    /// <summary>
    /// Increments whenever the source starts a fresh timeline: a re-attach to the
    /// game, or the fake source looping its script. A change means "this is a new
    /// session, previous run state is meaningless" and triggers a run reset.
    /// </summary>
    int Generation { get; }

    /// <summary>Try to (re)attach. Returns <see cref="Attached"/>.</summary>
    bool Attach();

    GameSnapshot TakeSnapshot();

    /// <summary>Event-flag lookups, for boss-defeat auto-splitting.</summary>
    IFlagSource Flags { get; }
}
