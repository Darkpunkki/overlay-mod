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
///    once, and then classified: <see cref="FallDetector"/> picks out what the
///    ground did and <see cref="DamageOverTimeDetector"/> what poison and toxic
///    did, leaving hits as what is left.
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

    /// <summary>
    /// Consecutive readings of zero health that confirm a death.
    ///
    /// At the default 30 Hz poll this is about a seventh of a second — longer
    /// than a pointer chain reads zero while it is being rebuilt around a load,
    /// and far shorter than a body lies on the ground. Counted in ticks rather
    /// than in-game time on purpose: in-game time stops during exactly the loads
    /// this is meant to see through.
    /// </summary>
    private const int DeathConfirmTicks = 4;

    private Route? _route;
    private readonly List<SplitResult> _splits = new();
    private readonly List<DamageEvent> _recentDamage = new(RecentDamageCapacity);
    private readonly FallDetector _fall = new();
    private readonly DamageOverTimeDetector _overTime = new();

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

    /// <summary>Consecutive zero-health readings, so a momentary bad read is not a death.</summary>
    private int _zeroHpTicks;

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

    /// <summary>Thresholds for attributing damage to poison, toxic or another effect ticking. Safe to change mid-run.</summary>
    public DamageOverTimeOptions OverTimeOptions
    {
        get => _overTime.Options;
        set => _overTime.Options = value;
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

    /// <summary>Damage an enemy dealt, with falls and status ticks excluded. What No Hit counts.</summary>
    public int TotalHits => Sum(static s => s.Hits);

    public int TotalFallDamage => Sum(static s => s.FallDamage);

    /// <summary>Damage attributed to poison, toxic or another effect ticking.</summary>
    public int TotalTickDamage => Sum(static s => s.TickDamage);
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
        // Health is read whenever the character object exists, which is a wider
        // condition than being in play, so that a death is still seen if the
        // game raises its loading flag while the death animation is running.
        //
        // Nothing here may depend on max health. 0.2.0 required MaxHp > 0 as
        // proof the data module was populated, and on a game where that offset
        // reads zero it silently switched every counter off — damage, hits and
        // deaths all stuck at zero while the timer, which needs no such reading,
        // carried on. A guard that can disable the entire feature it protects is
        // worse than the transient it was guarding against; persistence does
        // that job instead, and needs no offset to be right. See DeathConfirmTicks.
        var characterPresent = snapshot.Attached && snapshot.PlayerLoaded;

        if (characterPresent)
        {
            if (inPlay)
            {
                _fall.Observe(snapshot.IgtMs, snapshot.Y);

                // Ahead of this tick's damage, so a run of poison ticks that has
                // stopped is closed out before a fresh drop is offered to it.
                _overTime.Advance(snapshot.IgtMs);
            }

            TrackHealth(segment, snapshot, inPlay);
        }
        else
        {
            ForgetLastReading();
            _zeroHpTicks = 0;
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
    /// there to be missed. The latch clears when health returns, so lying dead
    /// for ten seconds still counts once.
    ///
    /// A corpse is told from a bad reading by how long it lasts, not by any
    /// second opinion from memory: the pointer chain being torn down around a
    /// load reads zero for an instant, a dead player reads zero for seconds.
    ///
    /// Hits, unlike deaths, are counted only while in play. Two health readings
    /// taken either side of a loading screen describe different worlds, and
    /// subtracting one from the other invents damage nobody took.
    /// </summary>
    private void TrackHealth(SegmentResult segment, in GameSnapshot snapshot, bool inPlay)
    {
        var hp = snapshot.Hp;

        if (hp <= 0)
        {
            _zeroHpTicks++;

            // _hasSeenAlive keeps this from firing when the overlay attaches to a
            // player who is already dead: that is a state with no history behind
            // it, not a death that just happened.
            if (_hasSeenAlive && !_deathLatched && _zeroHpTicks >= DeathConfirmTicks)
            {
                _deathLatched = true;
                segment.Deaths++;
                RecordDamage(segment, snapshot, fatal: true); // the killing blow is itself damage
            }

            // Leave the previous reading alone: respawning at full health is not
            // a heal, and the health before the death is the right baseline for
            // the next drop.
            _prevDecreasing = false;
            return;
        }

        _zeroHpTicks = 0;
        _deathLatched = false;
        _hasSeenAlive = true;

        if (!inPlay)
        {
            ForgetLastReading();
            return;
        }

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

        _prevHp = hp;
        _hasPrevHp = true;
    }

    private void RecordDamage(SegmentResult segment, in GameSnapshot snapshot, bool fatal)
    {
        // How much health this cost. Unmeasurable when there is no previous
        // reading to subtract from, which happens only for a death seen without
        // a live reading in front of it.
        int? amount = _hasPrevHp ? Math.Max(0, _prevHp - snapshot.Hp) : null;

        var descent = _fall.DescentMetres(snapshot.IgtMs);
        var isFall = _fall.IsFall(descent);

        segment.Damage++;
        if (isFall) segment.FallDamage++;

        var damage = new DamageEvent(
            snapshot.IgtMs,
            _activeIndex < _splits.Count ? _splits[_activeIndex].Name : "",
            snapshot.Hp,
            snapshot.MaxHp,
            amount ?? 0,
            fatal,
            Math.Round(descent, 2))
        {
            Kind = isFall ? DamageKind.Fall : DamageKind.Pending,
        };

        // The fall detector has first refusal: the ground and a status effect are
        // not competing explanations, and a landing is decided on the spot.
        _overTime.Offer(damage, segment, amount, snapshot.IgtMs);

        if (_recentDamage.Count == RecentDamageCapacity) _recentDamage.RemoveAt(0);
        _recentDamage.Add(damage);
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

        // Anything still waiting to be shown to be a status tick will never get
        // its evidence now, and settles as a hit.
        _overTime.Flush();
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
        _zeroHpTicks = 0;
    }

    /// <summary>Capture progress for storage, so the run can outlive the process.</summary>
    public RunState? Capture()
    {
        if (_route is null || Phase == RunPhase.NotStarted) return null;

        var splits = new List<SplitState>(_splits.Count);
        foreach (var s in _splits)
        {
            // Damage still waiting on a verdict is deliberately not carried over.
            // A restored run has no history behind it for the pattern to be
            // completed against, so those settle as hits — the safe direction.
            splits.Add(new SplitState(
                s.Name, s.IsBoss, s.Completed,
                s.Approach.IgtMs, s.Approach.Damage, s.Approach.FallDamage, s.Approach.Deaths,
                s.Boss.IgtMs, s.Boss.Damage, s.Boss.FallDamage, s.Boss.Deaths,
                s.Approach.TickDamage, s.Boss.TickDamage));
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
            split.Approach.TickDamage = s.ApproachTickDamage;
            split.Approach.Deaths = s.ApproachDeaths;
            split.Boss.IgtMs = s.BossIgtMs;
            split.Boss.Damage = s.BossDamage;
            split.Boss.FallDamage = s.BossFallDamage;
            split.Boss.TickDamage = s.BossTickDamage;
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
