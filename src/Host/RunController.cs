using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Host;

/// <summary>
/// Owns the run state and serialises access to it. The engine loop ticks it
/// while HTTP endpoints may start, split or reset concurrently, so every entry
/// point takes the same lock.
/// </summary>
public sealed class RunController
{
    /// <summary>How often an in-progress run is checkpointed to disk.</summary>
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly RunTracker _tracker = new();
    private readonly IRecordStore _records;
    private readonly RunStateStore _parked;
    private readonly ILogger<RunController> _log;

    private Route _route = DemoRoute.Create();
    private PersonalBests _bests;

    /// <summary>Set whenever contact with the game is lost, forcing a resume-or-restart decision.</summary>
    private bool _awaitingResumeDecision = true;

    private int _lastGeneration = -1;

    private DateTime _lastCheckpoint = DateTime.MinValue;

    /// <summary>
    /// The parts of a run whose change must be checkpointed immediately rather
    /// than waiting for the timer. Losing a hit to a crash would corrupt exactly
    /// the number a No-Hit run is judged on.
    /// </summary>
    private (int Index, int Hits, int Deaths, RunPhase Phase) _lastCheckpointKey = (-1, -1, -1, RunPhase.NotStarted);

    public RunController(IRecordStore records, RunStateStore parked, ILogger<RunController> log)
    {
        _records = records;
        _parked = parked;
        _log = log;
        _bests = _records.BestsFor(_route.Name);

        // A run left unfinished by a previous session is picked up here; whether
        // it is actually resumed is decided once the game is back and in play.
        if (_parked.Load() is { } state && _tracker.Restore(_route, state))
            _log.LogInformation("Recovered an unfinished run at split {Index}.", state.ActiveIndex);
    }

    public Route Route
    {
        get { lock (_gate) return _route; }
    }

    /// <summary>
    /// Feed one polled snapshot into the tracker.
    ///
    /// Runs start automatically when the player loads into the world — being in
    /// a level is what "a run has begun" means; menus and loading screens are not
    /// part of it.
    ///
    /// Quitting the game does not end a run. Dark Souls III keeps in-game time in
    /// the save, so on returning we compare the save's IGT against the last value
    /// we saw: at or ahead of it means the same character carrying on, and the run
    /// resumes. Behind it means a different or fresh character, and a new run starts.
    /// </summary>
    public void Tick(GameSnapshot snapshot, IFlagSource flags, int generation)
    {
        lock (_gate)
        {
            // A fresh source generation - a re-attach, or the fake script looping -
            // means the timeline restarted. That is not automatically a new run,
            // so re-run the resume check rather than assuming either way.
            if (generation != _lastGeneration)
            {
                _lastGeneration = generation;
                _awaitingResumeDecision = true;
            }

            if (!snapshot.Attached)
            {
                // The game is gone. Freeze the run exactly where it is and
                // re-decide when it comes back.
                _awaitingResumeDecision = true;
                return;
            }

            var inPlay = snapshot.PlayerLoaded && !snapshot.IsLoading;

            if (!inPlay)
            {
                // At a menu or on a loading screen. Still tick the tracker: it
                // uses these to stop the clock and to disarm HP tracking, so a
                // reload at different health is not mistaken for a hit.
                _tracker.Update(snapshot, flags);
                return;
            }

            if (_awaitingResumeDecision)
            {
                _awaitingResumeDecision = false;
                ResolveResume(snapshot);
            }

            if (_tracker.Phase == RunPhase.NotStarted) StartNew(snapshot);

            var wasRunning = _tracker.Phase == RunPhase.Running;
            _tracker.Update(snapshot, flags);

            if (wasRunning && _tracker.Phase == RunPhase.Finished) OnRunFinished();
            else Checkpoint();
        }
    }

    /// <summary>Decide whether a returning game continues the parked run or replaces it.</summary>
    private void ResolveResume(GameSnapshot snapshot)
    {
        // A completed run is history. Loading back into the world is the start of
        // the next attempt, not a return to the finished one. Without this a
        // finished run would sit on screen forever, never replaced.
        if (_tracker.Phase == RunPhase.Finished)
        {
            _log.LogInformation("Previous run is complete; starting a new attempt.");
            StartNew(snapshot);
            return;
        }

        if (_tracker.Phase != RunPhase.Running) return;

        if (snapshot.IgtMs >= _tracker.CurrentIgt)
        {
            _log.LogInformation(
                "Resuming run at split {Index}; in-game time picked up at {Igt}ms.",
                _tracker.ActiveIndex, snapshot.IgtMs);
            return;
        }

        _log.LogInformation(
            "In-game time went backwards ({Igt}ms < {Last}ms) - treating this as a new run.",
            snapshot.IgtMs, _tracker.CurrentIgt);
        StartNew(snapshot);
    }

    private void StartNew(GameSnapshot snapshot)
    {
        _tracker.Reset();
        _tracker.Start(_route, snapshot);
        _bests = _records.BestsFor(_route.Name);
        _lastCheckpointKey = (-1, -1, -1, RunPhase.NotStarted);
        Checkpoint(force: true);
    }

    private void OnRunFinished()
    {
        var splits = new List<SplitRecord>(_tracker.Splits.Count);
        foreach (var s in _tracker.Splits)
            splits.Add(new SplitRecord(s.Name, s.IgtMs, s.Hits, s.Deaths));

        _records.Record(new RunRecord(
            _route.Name,
            _route.Profile.Name,
            DateTimeOffset.Now,
            _tracker.RunIgtMs,
            _tracker.TotalHits,
            _tracker.TotalDeaths,
            splits));

        _bests = _records.BestsFor(_route.Name);
        _parked.Clear();

        _log.LogInformation(
            "Run finished: {Hits} hits, {Deaths} deaths, {Time}ms.",
            _tracker.TotalHits, _tracker.TotalDeaths, _tracker.RunIgtMs);
    }

    /// <summary>
    /// Checkpoint the in-progress run. Anything that changes hits, deaths, the
    /// active split or the phase is written straight away; the mere passage of
    /// time is rate-limited, since that only costs a couple of seconds of clock.
    /// </summary>
    private void Checkpoint(bool force = false)
    {
        if (_tracker.Capture() is not { } state) return;

        var key = (state.ActiveIndex, _tracker.TotalHits, _tracker.TotalDeaths, state.Phase);
        var changed = key != _lastCheckpointKey;
        if (!force && !changed && DateTime.UtcNow - _lastCheckpoint < CheckpointInterval) return;

        _lastCheckpoint = DateTime.UtcNow;
        _lastCheckpointKey = key;
        _parked.Save(state);
    }

    public void Start(GameSnapshot snapshot)
    {
        lock (_gate)
        {
            _awaitingResumeDecision = false;
            StartNew(snapshot);
        }
    }

    public void Split()
    {
        lock (_gate)
        {
            var wasRunning = _tracker.Phase == RunPhase.Running;
            _tracker.Split();
            if (wasRunning && _tracker.Phase == RunPhase.Finished) OnRunFinished();
            else Checkpoint(force: true);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _tracker.Reset();
            _parked.Clear();
            _lastCheckpointKey = (-1, -1, -1, RunPhase.NotStarted);
            _awaitingResumeDecision = false;
        }
    }

    public OverlayState Project(GameSnapshot snapshot)
    {
        lock (_gate) return OverlayState.From(_tracker, _route, _bests, snapshot);
    }
}
