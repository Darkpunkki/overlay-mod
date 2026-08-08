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
/// **What a tick looks like.** Small, repeated, slow and even. Three drops of
/// about the same small size, spaced between <see cref="MinIntervalMs"/> and
/// <see cref="DamageOverTimeOptions.MaxIntervalMs"/> apart, are a status effect;
/// anything bigger is a hit the moment it lands. The lower bound on spacing is
/// what keeps a melee combo out: several blows of similar size do arrive in a
/// row, but they arrive in well under a second.
///
/// **Why the first two ticks are held rather than counted.** Nothing can be
/// called a tick until a second one follows it, so a small drop is parked as
/// <see cref="DamageKind.Pending"/> — counted as damage, but not yet as a hit —
/// until it either joins a run of ticks or ages out into a hit. Held rather than
/// counted-then-retracted because the alternative flickers: a minute of poison
/// would drive the hit counter up and back down every few seconds, which reads
/// as a broken overlay. A real hit that lands under the size threshold is late
/// to appear by a few seconds; it does still appear.
///
/// **What it will get wrong.** Any small, slow, regular damage that is not a
/// status effect — standing in a fire, a trap on a loop — is called a tick.
/// Erring the other way, three identical light hits spaced a couple of seconds
/// apart go uncounted. Every event keeps the size measured for it and the
/// verdict reached, so both are visible on the control page rather than merely
/// suspected.
/// </summary>
public sealed class DamageOverTimeDetector
{
    /// <summary>
    /// How many ticks it takes to believe in a status effect rather than a
    /// coincidence. Two is not enough: an enemy landing the same attack twice a
    /// couple of seconds apart would qualify, and hiding real hits is the one
    /// failure this must not have.
    /// </summary>
    private const int MinTicks = 3;

    /// <summary>
    /// The fastest two drops may arrive and still be a status effect. A combo,
    /// a repeating trap and a spell being spammed all land far quicker than this.
    /// </summary>
    private const int MinIntervalMs = 1200;

    /// <summary>How much two ticks may differ in size and still be the same effect.</summary>
    private const double SizeTolerance = 0.25;

    private readonly record struct Tick(DamageEvent Event, SegmentResult Segment, int Damage, int TimeMs);

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
    public void Offer(DamageEvent damage, SegmentResult segment, int? damageAmount, int timeMs)
    {
        // A fall has already been attributed to the ground; it is not up for
        // reclassification, and it must not extend a run of ticks either.
        if (damage.Kind == DamageKind.Fall) return;

        if (!Options.Enabled || damageAmount is not { } amount || amount > Options.MaxTickDamage)
        {
            damage.Kind = DamageKind.Hit;
            return;
        }

        // A small drop that does not continue the current run of ticks ends it,
        // and starts a run of its own.
        if (_chain.Count > 0 && !Continues(_chain[^1], amount, timeMs)) Flush();

        damage.Kind = DamageKind.Pending;
        segment.PendingDamage++;
        _chain.Add(new Tick(damage, segment, amount, timeMs));

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

    private bool Continues(Tick last, int damageAmount, int timeMs)
    {
        var gap = timeMs - last.TimeMs;
        if (gap < MinIntervalMs || gap > Options.MaxIntervalMs) return false;

        var larger = Math.Max(damageAmount, last.Damage);
        return Math.Abs(damageAmount - last.Damage) <= Math.Max(2, larger * SizeTolerance);
    }

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
