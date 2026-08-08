namespace OverlayMod.Engine.Tracking;

/// <summary>
/// Decides whether a drop in health was an enemy or a status effect ticking —
/// poison and toxic being the ones that prompted this, and the two that turn a
/// clean No Hit run into a run with a dozen phantom hits in it.
///
/// **Why a pattern and not a status read.** Dark Souls III knows perfectly well
/// that you are poisoned; it is an SpEffect on the player, and reading it would
/// be exact. It is also behind the same wall as the boss-HP offset and the fall
/// SpEffect: a pointer chain nobody here has found, whose layout moves between
/// game patches. A user on an older patch has already had every counter switched
/// off by a memory read that did not land where this build expected, so nothing
/// new is being staked on one. This works from health over time, which is
/// already read and already verified.
///
/// **What a tick looks like: a metronome.** Poison and toxic in Dark Souls III
/// tick once a second, every second, for a proportional bite out of maximum
/// health. Combat is never like that — blows land irregularly and for wildly
/// different amounts — so the discriminating evidence is the *evenness*, not the
/// speed and not the size. Four bites of about the same size, at gaps that are
/// about the same as each other, is a status effect.
///
/// **This is the second design.** 0.2.2 asked instead for gaps between 1.2 s and
/// 4 s and bites under 40 HP, which is three conjunctive guesses about a game
/// nobody here had measured. Real ticks arrive at 1 s — under the floor — so
/// every one of them was rejected before its size was even considered, and the
/// feature was a silent no-op that no setting could rescue. Two lessons are
/// built in here: **a bound whose wrong value disables the feature must not be
/// hard-coded** (hence <see cref="DamageOverTimeOptions.MaxIntervalMs"/> being
/// settable and <see cref="MinIntervalMs"/> being far below anything real), and
/// **regularity does the work that guessed magnitudes cannot**.
///
/// **Why the first three ticks are held rather than counted.** Nothing can be
/// called a tick until the pattern shows itself, so a small drop is parked as
/// <see cref="DamageKind.Pending"/> — counted as damage, but not yet as a hit —
/// until it either joins a run of ticks or ages out into a hit. Held rather than
/// counted-then-retracted because the alternative flickers: a minute of poison
/// would drive the hit counter up and back down every second, which reads as a
/// broken overlay. A real hit that lands under the size ceiling is late to
/// appear by a couple of seconds; it does still appear.
///
/// **What it will get wrong.** Any small, even, repeating damage that is not a
/// status effect — standing in a fire, a trap on a loop — is called a tick.
/// Erring the other way, four identical light hits at four identical intervals
/// go uncounted. Every event keeps the size measured for it and the verdict
/// reached, so both are visible on the control page rather than merely suspected.
/// </summary>
public sealed class DamageOverTimeDetector
{
    /// <summary>
    /// How many ticks it takes to believe in a status effect rather than a
    /// coincidence. Four gives three gaps, which is enough to see whether they
    /// are even; three would give two, and two gaps agreeing is not yet a
    /// rhythm. Hiding real hits is the one failure this must not have.
    /// </summary>
    private const int MinTicks = 4;

    /// <summary>
    /// A sanity bound only. Poison and toxic tick at 1 s and this sits far below
    /// that on purpose — 0.2.2 put the floor *above* the real cadence and
    /// disabled the whole feature. The evenness test below, not this number, is
    /// what keeps a flurry of blows out.
    /// </summary>
    private const int MinIntervalMs = 250;

    /// <summary>How much two ticks may differ in size and still be the same effect.</summary>
    private const double SizeTolerance = 0.25;

    /// <summary>
    /// How much two gaps may differ and still count as even. The absolute floor
    /// absorbs polling jitter: at 30 Hz a tick is seen up to 33 ms late, so two
    /// readings of the same 1 s cadence can differ by that much before anything
    /// has actually changed.
    /// </summary>
    private const double GapTolerance = 0.25;
    private const int GapToleranceFloorMs = 120;

    private readonly record struct Tick(
        DamageEvent Event, SegmentResult Segment, int Damage, int TimeMs, int GapMs);

    private readonly List<Tick> _chain = new(8);
    private bool _confirmed;

    public DamageOverTimeOptions Options { get; set; } = DamageOverTimeOptions.Default;

    /// <summary>
    /// Classify a drop in health that has just been recorded. The verdict is
    /// written to <see cref="DamageEvent.Kind"/>, either now or once enough of
    /// the pattern has arrived to reach one.
    /// </summary>
    /// <param name="damageAmount">
    /// Health lost, or null when there was no previous reading to subtract from.
    /// An unmeasured drop cannot be shown to be small, so it is a hit.
    /// </param>
    /// <param name="healthScale">
    /// What the player's maximum health is taken to be, for turning the
    /// percentage ceiling into an amount. See <see cref="RunTracker"/> for why
    /// this is not simply <c>MaxHp</c>.
    /// </param>
    public void Offer(
        DamageEvent damage, SegmentResult segment, int? damageAmount, int timeMs, int healthScale)
    {
        // A fall has already been attributed to the ground; it is not up for
        // reclassification, and it must not extend a run of ticks either.
        if (damage.Kind == DamageKind.Fall) return;

        if (!Options.Enabled || damageAmount is not { } amount || amount > Options.CeilingFor(healthScale))
        {
            // Deliberately does not disturb the chain: being hit while poisoned
            // is ordinary, and must neither be swallowed by the pattern nor
            // break it.
            damage.Kind = DamageKind.Hit;
            return;
        }

        var gap = _chain.Count > 0 ? timeMs - _chain[^1].TimeMs : 0;

        // A small drop that does not continue the current run of ticks ends it,
        // and starts a run of its own.
        if (_chain.Count > 0 && !Continues(amount, gap))
        {
            Flush();
            gap = 0;
        }

        damage.Kind = DamageKind.Pending;
        segment.PendingDamage++;
        _chain.Add(new Tick(damage, segment, amount, timeMs, gap));

        // Confirming promotes everything held so far, not just this one, so the
        // ticks that were only suspected are settled by the same evidence.
        if (_confirmed || _chain.Count >= MinTicks) Confirm();
    }

    /// <summary>
    /// Age the current run of ticks. Call once per polled tick while in play, so
    /// that a status effect wearing off releases anything still held rather than
    /// leaving it excluded from the hit count forever.
    /// </summary>
    public void Advance(int nowMs)
    {
        if (_chain.Count == 0) return;

        // Time going backwards means a reload, which is not a gap in one effect.
        var age = nowMs - _chain[^1].TimeMs;
        if (Options.Enabled && age >= 0 && age <= Options.MaxIntervalMs) return;

        Flush();
    }

    /// <summary>
    /// Give up on the pattern and settle everything outstanding as a hit.
    ///
    /// Called when the readings behind us stop describing the same stretch of
    /// play — a loading screen, a reload, a run reset. Settling as hits rather
    /// than as ticks is deliberate: an unproven tick is a hit we have not
    /// finished doubting, and the safe direction to resolve it is the one that
    /// cannot make an invalid run look clean.
    /// </summary>
    public void Flush()
    {
        foreach (var t in _chain)
        {
            if (t.Event.Kind != DamageKind.Pending) continue;
            t.Event.Kind = DamageKind.Hit;
            t.Segment.PendingDamage--;
        }

        _chain.Clear();
        _confirmed = false;
    }

    /// <summary>
    /// Whether this drop carries on the run of ticks: near enough in size, and
    /// arriving at near enough the same spacing as the one before it.
    /// </summary>
    private bool Continues(int amount, int gap)
    {
        if (gap < MinIntervalMs || gap > Options.MaxIntervalMs) return false;

        var last = _chain[^1];
        if (!Similar(amount, last.Damage, SizeTolerance, floor: 2)) return false;

        // The evenness test, which is the whole discriminator. Skipped for the
        // second tick of a run, which has no previous gap to be even with.
        return last.GapMs == 0 || Similar(gap, last.GapMs, GapTolerance, GapToleranceFloorMs);
    }

    private static bool Similar(int a, int b, double tolerance, int floor) =>
        Math.Abs(a - b) <= Math.Max(floor, Math.Max(a, b) * tolerance);

    private void Confirm()
    {
        _confirmed = true;

        foreach (var t in _chain)
        {
            if (t.Event.Kind != DamageKind.Pending) continue;
            t.Event.Kind = DamageKind.OverTime;
            t.Segment.PendingDamage--;
            t.Segment.TickDamage++;
        }
    }
}
