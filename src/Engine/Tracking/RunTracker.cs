using OverlayMod.Engine.GameState;

namespace OverlayMod.Engine.Tracking;

/// <summary>
/// Turns a stream of <see cref="GameSnapshot"/>s into live run state: the run
/// timer, per-split approach/boss times, damage, deaths, and split advancement.
/// Pure logic with no memory access, so it is fully unit-testable from
/// synthetic snapshots.
///
/// Design notes:
///  - Time uses IGT deltas, which already pause during loads; a sanity cap
///    guards against menu/save-load jumps.
///  - Damage is derived from HP decreases, debounced so a multi-tick drop counts
///    once, and classified by <see cref="FallDetector"/> into damage the game
///    dealt (hits) and damage the ground dealt.
///  - Deaths are latched on health reaching zero, not edge-detected. See
///    <see cref="Update"/>.
///  - Approach vs boss attribution is driven by <see cref="GameSnapshot.BossFightActive"/>.
/// </summary>
public sealed class RunTracker
{
    // Ignore IGT jumps larger than this (menu / save load), in milliseconds.
    private const int MaxIgtDeltaMs = 10_000;

    /// <summary>How many damage events to keep for review. Enough to cover a boss fight.</summary>
    private const int RecentDamageCapacity = 40;

    private Route? _route;
    private readonly List<SplitResult> _splits = new();
    private readonly List<DamageEvent> _recentDamage = new(RecentDamageCapacity);
    private readonly FallDetector _fall = new();

    private int _activeIndex;
    private int _runStartIgt;
    private int _currentIgt;

    private bool _hasLastIgt;
    private int _lastIgt;

    private bool _hasPrevHp;
    private int _prevHp;
    private bool _prevDecreasing;

    /// <summary>Set once health has been seen above zero, so attaching to an already-dead player is not a death.</summary>
    private bool _hasSeenAlive;

    /// <summary>Set while health is at zero, so one death is counted however long the body lies there.</summary>
    private bool _deathLatched;

    private bool _activeFlagWasSet;

    public RunPhase Phase { get; private set; } = RunPhase.NotStarted;
    public Route? Route => _route;
    public ChallengeProfile? Profile => _route?.Profile;
    public IReadOnlyList<SplitResult> Splits => _splits;
    public int ActiveIndex => _activeIndex;

    /// <summary>Thresholds for attributing damage to a fall. Safe to change mid-run.</summary>
    public FallDamageOptions FallOptions
    {
        get => _fall.Options;
        set => _fall.Options = value;
    }

    /// <summary>The most recent damage events, oldest first, for reviewing fall attribution.</summary>
    public IReadOnlyList<DamageEvent> RecentDamage => _recentDamage;

    public SplitResult? ActiveSplit =>
        Phase == RunPhase.Running && _activeIndex < _splits.Count ? _splits[_activeIndex] : null;

    /// <summary>Headline run timer: IGT elapsed since the run started.</summary>
    public int RunIgtMs => Phase == RunPhase.NotStarted ? 0 : Math.Max(0, _currentIgt - _runStartIgt);

    /// <summary>
    /// The most recent in-game time seen. Compared against a freshly attached
    /// game's IGT to decide whether a run is being resumed or replaced.
    /// </summary>
    public int CurrentIgt => _currentIgt;

    /// <summary>Every drop in health this run, falls included. What No Damage counts.</summary>
    public int TotalDamage => Sum(static s => s.Damage);

    /// <summary>Damage the game dealt, with falls excluded. What No Hit counts.</summary>
    public int TotalHits => Sum(static s => s.Hits);

    public int TotalFallDamage => Sum(static s => s.FallDamage);
    public int TotalDeaths => Sum(static s => s.Deaths);
    public int TotalSegmentIgtMs => Sum(static s => s.IgtMs);

    /// <summary>The value this run is ranked by, per the profile's primary metric.</summary>
    public int PrimaryValue => Profile?.PrimaryMetric switch
    {
        RunMetric.Damage => TotalDamage,
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
        _activeFlagWasSet = false;
        _recentDamage.Clear();
        ForgetCharacter();

        Phase = route.Splits.Count == 0 ? RunPhase.Finished : RunPhase.Running;
    }

    public void Reset()
    {
        _splits.Clear();
        _recentDamage.Clear();
        _route = null;
        _activeIndex = 0;
        Phase = RunPhase.NotStarted;
        ForgetCharacter();
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

        // --- damage & deaths from HP transitions ---
        //
        // Health is tracked whenever the character object exists, which is a
        // wider condition than being in play: the game raises its loading flag
        // while the death animation is still running, and a death that needs the
        // ticks either side of it to be in play is lost exactly when the flag
        // comes up early. Deaths are what Deathless is judged on, so missing one
        // is not a rounding error.
        //
        // A zero MaxHP means the data module has not been populated yet — the
        // first frames after a load read zeros — and a zero read there is
        // indistinguishable from a corpse.
        var characterPresent = snapshot.Attached && snapshot.PlayerLoaded && snapshot.MaxHp > 0;

        if (characterPresent)
        {
            _fall.Observe(snapshot.IgtMs, snapshot.Y);
            TrackHealth(segment, snapshot);
        }
        else
        {
            ForgetLastReading();
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

    /// <summary>
    /// Turn this tick's health reading into damage and deaths.
    ///
    /// Death is <em>latched on zero</em> rather than detected as a transition
    /// from a positive previous reading. An edge needs both of its neighbours,
    /// and the tick where health first reads zero is exactly the tick the game
    /// may also flip its loading flag or free the character — so the edge is
    /// there to be missed. Zero health while the character exists is a death
    /// whether or not the tick before it was observed; the latch clears when
    /// health returns, so lying dead for ten seconds still counts once.
    /// </summary>
    private void TrackHealth(SegmentResult segment, in GameSnapshot snapshot)
    {
        var hp = snapshot.Hp;

        if (hp <= 0)
        {
            if (_hasSeenAlive && !_deathLatched)
            {
                _deathLatched = true;
                segment.Deaths++;
                RecordDamage(segment, snapshot, fatal: true); // the killing blow is itself damage
            }

            _prevDecreasing = false;
        }
        else
        {
            _deathLatched = false;
            _hasSeenAlive = true;

            if (_hasPrevHp && hp < _prevHp)
            {
                // A drop spread over several ticks is one hit, not one per tick.
                if (!_prevDecreasing) RecordDamage(segment, snapshot, fatal: false);
                _prevDecreasing = true;
            }
            else
            {
                // Stable or healed: end the current decrease so the next distinct
                // drop counts as a new hit.
                _prevDecreasing = false;
            }
        }

        _prevHp = hp;
        _hasPrevHp = true;
    }

    private void RecordDamage(SegmentResult segment, in GameSnapshot snapshot, bool fatal)
    {
        var descent = _fall.DescentMetres(snapshot.IgtMs);
        var isFall = _fall.IsFall(descent);

        segment.Damage++;
        if (isFall) segment.FallDamage++;

        if (_recentDamage.Count == RecentDamageCapacity) _recentDamage.RemoveAt(0);
        _recentDamage.Add(new DamageEvent(
            snapshot.IgtMs,
            _activeIndex < _splits.Count ? _splits[_activeIndex].Name : "",
            snapshot.Hp,
            snapshot.MaxHp,
            fatal,
            Math.Round(descent, 2),
            isFall));
    }

    /// <summary>
    /// Forget the previous reading, so the next one is a baseline rather than a
    /// comparison. A respawn at full health after a reload is not a heal, and
    /// the ground the player was standing on before a loading screen says
    /// nothing about where they are now.
    ///
    /// Deliberately does *not* touch the death latch or whether the player has
    /// been seen alive. Those describe the character, not the last tick, and a
    /// gap in the readings — a dropped poll, a stutter — must neither lose a
    /// death nor count one twice because of it.
    /// </summary>
    private void ForgetLastReading()
    {
        _hasPrevHp = false;
        _prevHp = 0;
        _prevDecreasing = false;
        _fall.Clear();
    }

    /// <summary>
    /// Forget the character as well: this is a different run, so a corpse on
    /// screen at this moment is a state we have no history for rather than a
    /// death that just happened.
    /// </summary>
    private void ForgetCharacter()
    {
        ForgetLastReading();
        _hasSeenAlive = false;
        _deathLatched = false;
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
                s.Approach.IgtMs, s.Approach.Damage, s.Approach.FallDamage, s.Approach.Deaths,
                s.Boss.IgtMs, s.Boss.Damage, s.Boss.FallDamage, s.Boss.Deaths));
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
            split.Approach.Damage = s.ApproachDamage;
            split.Approach.FallDamage = s.ApproachFallDamage;
            split.Approach.Deaths = s.ApproachDeaths;
            split.Boss.IgtMs = s.BossIgtMs;
            split.Boss.Damage = s.BossDamage;
            split.Boss.FallDamage = s.BossFallDamage;
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
        _activeFlagWasSet = false;
        _recentDamage.Clear();
        ForgetCharacter();

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
