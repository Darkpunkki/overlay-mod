using OverlayMod.Engine.GameState;

namespace OverlayMod.Engine.Tracking;

/// <summary>
/// Turns a stream of <see cref="GameSnapshot"/>s into live run state: the run
/// timer, per-split approach/boss times, hits, deaths, and split advancement.
/// Pure logic with no memory access, so it is fully unit-testable from
/// synthetic snapshots.
///
/// Design notes:
///  - Time uses IGT deltas, which already pause during loads; a sanity cap
///    guards against menu/save-load jumps.
///  - Hits are derived from HP decreases, debounced so a multi-tick drop counts
///    once. (Classifying enemy hits vs fall/DoT damage needs SpEffect reads and
///    is a later refinement; for now any HP loss is a hit.)
///  - Deaths are HP->0 transitions — version-independent, no extra offsets.
///  - Approach vs boss attribution is driven by <see cref="GameSnapshot.BossFightActive"/>.
/// </summary>
public sealed class RunTracker
{
    // Ignore IGT jumps larger than this (menu / save load), in milliseconds.
    private const int MaxIgtDeltaMs = 10_000;

    private Route? _route;
    private readonly List<SplitResult> _splits = new();

    private int _activeIndex;
    private int _runStartIgt;
    private int _currentIgt;

    private bool _hasLastIgt;
    private int _lastIgt;

    private bool _hasPrevHp;
    private int _prevHp;
    private bool _prevDecreasing;

    private bool _activeFlagWasSet;

    public RunPhase Phase { get; private set; } = RunPhase.NotStarted;
    public Route? Route => _route;
    public ChallengeProfile? Profile => _route?.Profile;
    public IReadOnlyList<SplitResult> Splits => _splits;
    public int ActiveIndex => _activeIndex;

    public SplitResult? ActiveSplit =>
        Phase == RunPhase.Running && _activeIndex < _splits.Count ? _splits[_activeIndex] : null;

    /// <summary>Headline run timer: IGT elapsed since the run started.</summary>
    public int RunIgtMs => Phase == RunPhase.NotStarted ? 0 : Math.Max(0, _currentIgt - _runStartIgt);

    /// <summary>
    /// The most recent in-game time seen. Compared against a freshly attached
    /// game's IGT to decide whether a run is being resumed or replaced.
    /// </summary>
    public int CurrentIgt => _currentIgt;

    public int TotalHits => Sum(static s => s.Hits);
    public int TotalDeaths => Sum(static s => s.Deaths);
    public int TotalSegmentIgtMs => Sum(static s => s.IgtMs);

    /// <summary>The value this run is ranked by, per the profile's primary metric.</summary>
    public int PrimaryValue => Profile?.PrimaryMetric switch
    {
        RunMetric.Hits => TotalHits,
        RunMetric.Deaths => TotalDeaths,
        RunMetric.Time => RunIgtMs,
        _ => 0,
    };

    public void Start(Route route, GameSnapshot snapshot)
    {
        _route = route;
        _splits.Clear();
        foreach (var s in route.Splits) _splits.Add(new SplitResult(s.Name, s.IsBoss));

        _activeIndex = 0;
        _runStartIgt = snapshot.IgtMs;
        _currentIgt = snapshot.IgtMs;
        _hasLastIgt = false;
        _lastIgt = snapshot.IgtMs;
        _hasPrevHp = false;
        _prevHp = 0;
        _prevDecreasing = false;
        _activeFlagWasSet = false;

        Phase = route.Splits.Count == 0 ? RunPhase.Finished : RunPhase.Running;
    }

    public void Reset()
    {
        _splits.Clear();
        _route = null;
        _activeIndex = 0;
        Phase = RunPhase.NotStarted;
    }

    /// <summary>Advance to the next split manually (LiveSplit-style hotkey).</summary>
    public void Split()
    {
        if (Phase == RunPhase.Running) AdvanceSplit();
    }

    public void Update(GameSnapshot snapshot, IFlagSource? flags = null)
    {
        if (Phase != RunPhase.Running) return;

        var inPlay = snapshot.Attached && snapshot.PlayerLoaded && !snapshot.IsLoading;

        // Only in-play readings move the clock. At the main menu and during
        // loads the game's IGT field is not meaningful, and letting it through
        // would both jump the run timer and corrupt the value the resume check
        // compares against.
        if (inPlay) _currentIgt = snapshot.IgtMs;
        var kind = snapshot.BossFightActive ? SegmentKind.Boss : SegmentKind.Approach;
        var segment = _splits[_activeIndex].Segment(kind);

        // --- timing ---
        if (inPlay && _hasLastIgt)
        {
            var delta = snapshot.IgtMs - _lastIgt;
            if (delta > 0 && delta <= MaxIgtDeltaMs) segment.IgtMs += delta;
        }
        _hasLastIgt = inPlay;
        _lastIgt = snapshot.IgtMs;

        // --- hits & deaths from HP transitions ---
        if (inPlay)
        {
            var hp = snapshot.Hp;
            if (_hasPrevHp)
            {
                if (hp <= 0 && _prevHp > 0)
                {
                    segment.Deaths++;
                    segment.Hits++; // the killing blow is itself a hit
                    _prevDecreasing = false;
                }
                else if (hp < _prevHp)
                {
                    if (!_prevDecreasing) segment.Hits++;
                    _prevDecreasing = true;
                }
                else
                {
                    // Stable or healed: end the current decrease so the next
                    // distinct drop counts as a new hit.
                    _prevDecreasing = false;
                }
            }
            _prevHp = hp;
            _hasPrevHp = true;
        }
        else
        {
            _hasPrevHp = false;
            _prevDecreasing = false;
        }

        // --- auto-split on boss-defeat flag (rising edge) ---
        var routeSplit = _route!.Splits[_activeIndex];
        if (routeSplit.DefeatFlagId is { } flagId && flags != null)
        {
            var set = flags.IsEventFlagSet(flagId);
            if (set && !_activeFlagWasSet) AdvanceSplit();
            else _activeFlagWasSet = set;
        }
    }

    /// <summary>Capture progress for storage, so the run can outlive the process.</summary>
    public RunState? Capture()
    {
        if (_route is null || Phase == RunPhase.NotStarted) return null;

        var splits = new List<SplitState>(_splits.Count);
        foreach (var s in _splits)
        {
            splits.Add(new SplitState(
                s.Name, s.IsBoss, s.Completed,
                s.Approach.IgtMs, s.Approach.Hits, s.Approach.Deaths,
                s.Boss.IgtMs, s.Boss.Hits, s.Boss.Deaths));
        }

        return new RunState(_route.Name, _runStartIgt, _currentIgt, _activeIndex, Phase, splits);
    }

    /// <summary>
    /// Rehydrate a captured run. Returns false — leaving the tracker untouched —
    /// if the state does not describe this route, which is the case when a stored
    /// run is loaded after its route has been edited.
    /// </summary>
    public bool Restore(Route route, RunState state)
    {
        if (state.RouteName != route.Name) return false;
        if (state.Splits.Count != route.Splits.Count) return false;

        for (var i = 0; i < route.Splits.Count; i++)
            if (state.Splits[i].Name != route.Splits[i].Name) return false;

        _route = route;
        _splits.Clear();
        foreach (var s in state.Splits)
        {
            var split = new SplitResult(s.Name, s.IsBoss) { Completed = s.Completed };
            split.Approach.IgtMs = s.ApproachIgtMs;
            split.Approach.Hits = s.ApproachHits;
            split.Approach.Deaths = s.ApproachDeaths;
            split.Boss.IgtMs = s.BossIgtMs;
            split.Boss.Hits = s.BossHits;
            split.Boss.Deaths = s.BossDeaths;
            _splits.Add(split);
        }

        _activeIndex = Math.Clamp(state.ActiveIndex, 0, Math.Max(0, _splits.Count));
        _runStartIgt = state.RunStartIgt;
        _currentIgt = state.CurrentIgt;
        Phase = state.Phase;

        // Deliberately not restored: the previous tick's IGT and HP. A restored
        // run has a gap behind it, and treating the first new reading as a delta
        // would invent time or a phantom hit. Both re-arm on the next tick.
        _hasLastIgt = false;
        _lastIgt = state.CurrentIgt;
        _hasPrevHp = false;
        _prevHp = 0;
        _prevDecreasing = false;
        _activeFlagWasSet = false;

        return true;
    }

    private void AdvanceSplit()
    {
        _splits[_activeIndex].Completed = true;
        _activeIndex++;
        _activeFlagWasSet = false;
        if (_activeIndex >= _splits.Count) Phase = RunPhase.Finished;
    }

    private int Sum(Func<SplitResult, int> selector)
    {
        var total = 0;
        foreach (var s in _splits) total += selector(s);
        return total;
    }
}
