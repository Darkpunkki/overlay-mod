using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Host;

/// <summary>
/// Owns the run state and serialises access to it. The engine loop ticks it
/// while HTTP endpoints may start, split or reset concurrently, so every entry
/// point takes the same lock.
/// </summary>
public sealed class RunController
{
    private readonly object _gate = new();
    private readonly RunTracker _tracker = new();

    private Route _route = DemoRoute.Create();
    private int _lastGeneration = -1;

    public Route Route
    {
        get { lock (_gate) return _route; }
    }

    /// <summary>
    /// Feed one polled snapshot into the tracker.
    ///
    /// Run start is currently automatic: the run begins as soon as the player is
    /// loaded into the world. That is a stand-in — whether runs should start on a
    /// hotkey, on an IGT reset, or on entering the first split's area is an open
    /// design question (see docs/PLAN.md) to be settled in Milestone 4.
    /// </summary>
    public void Tick(GameSnapshot snapshot, IFlagSource flags, int generation)
    {
        lock (_gate)
        {
            // A new source generation means a re-attach or a fake-script loop:
            // whatever run was in progress no longer refers to anything real.
            if (generation != _lastGeneration)
            {
                _lastGeneration = generation;
                _tracker.Reset();
            }

            if (_tracker.Phase == RunPhase.NotStarted && snapshot.Attached && snapshot.PlayerLoaded)
                _tracker.Start(_route, snapshot);

            _tracker.Update(snapshot, flags);
        }
    }

    public void Start(GameSnapshot snapshot)
    {
        lock (_gate)
        {
            _tracker.Reset();
            _tracker.Start(_route, snapshot);
        }
    }

    public void Split()
    {
        lock (_gate) _tracker.Split();
    }

    public void Reset()
    {
        lock (_gate) _tracker.Reset();
    }

    public OverlayState Project(GameSnapshot snapshot)
    {
        lock (_gate) return OverlayState.From(_tracker, _route, snapshot);
    }
}
