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

        _currentIgt = snapshot.IgtMs;
        var inPlay = snapshot.Attached && snapshot.PlayerLoaded && !snapshot.IsLoading;
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
