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

    /// <summary>
    /// Readings to discard after loading into the world before judging whether the
    /// run continues. Immediately after a load the game has not necessarily
    /// populated in-game time yet, and a transient zero read at that moment is
    /// indistinguishable from a brand new character.
    /// </summary>
    private const int SettleTicks = 20;

    /// <summary>
    /// How far in-game time may go backwards and still count as the same run.
    ///
    /// Dark Souls III writes in-game time into the save periodically rather than
    /// continuously, so reloading rewinds the clock to the last save point. A
    /// strict "must not go backwards" rule therefore threw away a perfectly good
    /// run every time the player quit to the menu and carried on.
    /// </summary>
    private const int ResumeRewindToleranceMs = 5 * 60 * 1000;

    /// <summary>
    /// Below this, a save looks like a character that has barely been played.
    /// Used so that loading a fresh character mid-session is still recognised as a
    /// new run even when the previous run was short enough to fall inside the
    /// rewind tolerance.
    /// </summary>
    private const int FreshCharacterIgtMs = 60 * 1000;

    private readonly object _gate = new();
    private readonly RunTracker _tracker = new();
    private readonly IRecordStore _records;
    private readonly RunStateStore _parked;
    private readonly RouteStore _routes;
    private readonly SettingsStore _settings;
    private readonly TrackingSettingsStore _tracking;
    private readonly ILogger<RunController> _log;

    private RouteFile _routeFile;
    private ChallengeProfile _profile;
    private Route _route;
    private PersonalBests _bests;

    /// <summary>Set whenever contact with the game is lost, forcing a resume-or-restart decision.</summary>
    private bool _awaitingResumeDecision = true;

    private int _lastGeneration = -1;
    private int _settleTicks;

    private DateTime _lastCheckpoint = DateTime.MinValue;

    /// <summary>
    /// The parts of a run whose change must be checkpointed immediately rather
    /// than waiting for the timer. Losing one to a crash would corrupt exactly
    /// the number the run is judged on.
    /// </summary>
    private (int Index, int Damage, int Deaths, RunPhase Phase) _lastCheckpointKey = (-1, -1, -1, RunPhase.NotStarted);

    public RunController(
        IRecordStore records,
        RunStateStore parked,
        RouteStore routes,
        SettingsStore settings,
        TrackingSettingsStore tracking,
        ILogger<RunController> log)
    {
        _records = records;
        _parked = parked;
        _routes = routes;
        _settings = settings;
        _tracking = tracking;
        _log = log;

        // Restore the last route and challenge chosen. If that route has since
        // been renamed or deleted, fall back rather than starting with nothing.
        var saved = _settings.Load();
        _routeFile = (saved is null ? null : _routes.Find(saved.RouteName)) ?? _routes.Default;
        _profile = ChallengeProfile.For(saved?.Challenge ?? _routeFile.DefaultChallenge);
        _route = _routeFile.ToRoute(_profile);
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

    /// <summary>What is currently selected, for the control page to display.</summary>
    public (RouteFile Route, ChallengeProfile Profile) Current
    {
        get { lock (_gate) return (_routeFile, _profile); }
    }

    /// <summary>
    /// The most recent damage events, newest first, with the descent that decided
    /// whether each was a fall. This is how the fall thresholds get tuned: take a
    /// run, then read back what the detector actually called.
    /// </summary>
    public IReadOnlyList<DamageEvent> RecentDamage
    {
        get { lock (_gate) return _tracker.RecentDamage.Reverse().ToList(); }
    }

    /// <summary>
    /// Choose what to run. Changing either the route or the challenge abandons any
    /// run in progress: the splits or the thing being measured have changed, so
    /// carrying the old numbers forward would be meaningless.
    /// </summary>
    public bool Select(string routeName, ChallengeType challenge)
    {
        lock (_gate)
        {
            var file = _routes.Find(routeName);
            if (file is null) return false;

            var unchanged = ReferenceEquals(file, _routeFile) && _profile.Type == challenge;
            if (unchanged) return true;

            _routeFile = file;
            _profile = ChallengeProfile.For(challenge);
            _route = _routeFile.ToRoute(_profile);
            _bests = _records.BestsFor(_route.Name);

            _tracker.Reset();
            _parked.Clear();
            _lastCheckpointKey = (-1, -1, -1, RunPhase.NotStarted);
            _awaitingResumeDecision = true;

            _settings.Save(new Selection(_routeFile.Name, challenge));
            _log.LogInformation("Selected route {Route} as {Challenge}.", _routeFile.Name, _profile.Name);
            return true;
        }
    }

    /// <summary>
    /// Feed one polled snapshot into the tracker.
    ///
    /// Runs start automatically when the player loads into the world — being in
    /// a level is what "a run has begun" means; menus and loading screens are not
    /// part of it.
    ///
    /// Quitting the game does not end a run. Dark Souls III keeps in-game time in
    /// the save, so on returning we compare the save's clock against the last
    /// reading we trusted. It is allowed to have gone backwards a little — the
    /// game saves periodically rather than continuously, so reloading rewinds to
    /// the last save point — but a save that has barely been played, or one far
    /// behind, is a different character and starts a new run.
    /// </summary>
    public void Tick(GameSnapshot snapshot, IFlagSource flags, int generation)
    {
        lock (_gate)
        {
            // Picked up every tick rather than at construction, so editing the
            // fall thresholds on the control page takes effect on the run in
            // progress — which is the only run anyone is ever tuning against.
            _tracker.FallOptions = _tracking.FallDamage;

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

                // Leaving the world at all - a loading screen, quitting to the
                // main menu, or closing the game - means the next time we are in
                // play we cannot assume it is the same run. Quitting to the menu
                // and starting a new character keeps the same process, so
                // without this the old run simply carried on with its hits.
                _awaitingResumeDecision = true;
                _settleTicks = 0;
                return;
            }

            if (_awaitingResumeDecision)
            {
                // Let readings settle before judging. Acting on the first frame
                // after a load risks reading in-game time before the game has
                // written it, which looks exactly like a brand new character.
                if (++_settleTicks < SettleTicks) return;

                _awaitingResumeDecision = false;
                _settleTicks = 0;
                ResolveResume(snapshot);
            }

            if (_tracker.Phase == RunPhase.NotStarted) StartNew(snapshot);

            var wasRunning = _tracker.Phase == RunPhase.Running;
            var indexBefore = _tracker.ActiveIndex;
            _tracker.Update(snapshot, flags);
            RecordSplitsCompletedSince(indexBefore);

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

        // How far the save's clock sits behind the last reading we trusted.
        // Negative means it moved forward, which is the ordinary case.
        var rewindMs = _tracker.CurrentIgt - snapshot.IgtMs;

        // A save that has barely been played, whose clock has gone backwards, is
        // a different character rather than the same one rewound.
        //
        // This used to ask whether the *previous* run was long — which is the
        // wrong side of the comparison, and meant starting a new game after a
        // short session carried the old run's hits straight into it. Two minutes
        // of testing, then New Game, and the counter kept climbing: the rewind
        // was under a minute, comfortably inside the tolerance below, and
        // nothing else objected.
        //
        // Below a minute of in-game time, a backwards clock is called a new
        // character even though a genuine save could rewind that far. Both
        // readings are cheap to be wrong about there — a run under a minute old
        // has nothing in it worth carrying — and the same mistake at the other
        // end of a two-hour attempt is not survivable, which is what the rewind
        // tolerance protects.
        var freshCharacter = snapshot.IgtMs < FreshCharacterIgtMs && snapshot.IgtMs < _tracker.CurrentIgt;
        var sameRun = !freshCharacter && rewindMs <= ResumeRewindToleranceMs;

        _log.LogInformation(
            "Back in play: save IGT {Igt}ms, last seen {Last}ms, rewind {Rewind}ms -> {Decision}.",
            snapshot.IgtMs, _tracker.CurrentIgt, rewindMs, sameRun ? "resuming this run" : "starting a new run");

        // Nothing to adjust when resuming: the run timer is an in-game-time
        // difference, so if the save rewound to its last save point the timer
        // rewinds with it. That is the honest reading — the player really did
        // lose that progress and will replay it.
        if (!sameRun) StartNew(snapshot);
    }

    private void StartNew(GameSnapshot snapshot)
    {
        _tracker.Reset();
        _tracker.Start(_route, snapshot);
        _bests = _records.BestsFor(_route.Name);
        _lastCheckpointKey = (-1, -1, -1, RunPhase.NotStarted);
        Checkpoint(force: true);
    }

    /// <summary>
    /// File every split that finished on this tick, so a personal best per boss
    /// is earned the moment that boss dies rather than only if the whole run is
    /// completed. Most attempts end early; waiting for a finish would discard
    /// nearly every result a player produces.
    /// </summary>
    private void RecordSplitsCompletedSince(int fromIndex)
    {
        var to = Math.Min(_tracker.ActiveIndex, _tracker.Splits.Count);
        if (to <= fromIndex) return;

        for (var i = fromIndex; i < to; i++)
        {
            var s = _tracker.Splits[i];
            _records.RecordSplit(_route.Name, new SplitRecord(s.Name, s.IgtMs, s.Damage, s.Deaths) { Hits = s.Hits });
        }

        _bests = _records.BestsFor(_route.Name);
    }

    private void OnRunFinished()
    {
        var splits = new List<SplitRecord>(_tracker.Splits.Count);
        foreach (var s in _tracker.Splits)
            splits.Add(new SplitRecord(s.Name, s.IgtMs, s.Damage, s.Deaths) { Hits = s.Hits });

        _records.Record(new RunRecord(
            _route.Name,
            _route.Profile.Name,
            DateTimeOffset.Now,
            _tracker.RunIgtMs,
            _tracker.TotalDamage,
            _tracker.TotalDeaths,
            splits)
        { TotalHits = _tracker.TotalHits });

        _bests = _records.BestsFor(_route.Name);
        _parked.Clear();

        _log.LogInformation(
            "Run finished: {Damage} damage ({Falls} from falls), {Deaths} deaths, {Time}ms.",
            _tracker.TotalDamage, _tracker.TotalFallDamage, _tracker.TotalDeaths, _tracker.RunIgtMs);
    }

    /// <summary>
    /// Checkpoint the in-progress run. Anything that changes hits, deaths, the
    /// active split or the phase is written straight away; the mere passage of
    /// time is rate-limited, since that only costs a couple of seconds of clock.
    /// </summary>
    private void Checkpoint(bool force = false)
    {
        if (_tracker.Capture() is not { } state) return;

        var key = (state.ActiveIndex, _tracker.TotalDamage, _tracker.TotalDeaths, state.Phase);
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
            var indexBefore = _tracker.ActiveIndex;
            _tracker.Split();
            RecordSplitsCompletedSince(indexBefore);

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
