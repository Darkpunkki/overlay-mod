using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Tracking;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Telling poison and toxic apart from an enemy.
///
/// These are written against the tracker rather than the detector directly,
/// because what matters is the number the overlay ends up showing — the
/// classification is only interesting insofar as it moves that number, and the
/// path between the two runs through the debounce and the fall detector.
/// </summary>
public class DamageOverTimeTests
{
    private const int Poison = 20;
    private const int PoisonIntervalMs = 2500;

    private static GameSnapshot Play(int igt, int hp, float y = 0) => new()
    {
        Attached = true,
        PlayerLoaded = true,
        IsLoading = false,
        IgtMs = igt,
        Hp = hp,
        MaxHp = 1000,
        Y = y,
    };

    private static Route RouteOf() =>
        new("test", ChallengeProfile.NoHit, new[] { new RouteSplit("A", false) });

    /// <summary>
    /// A tracker mid-run, plus the cursor the helpers below advance. Health starts
    /// well clear of zero so nothing here is mistaken for a death.
    /// </summary>
    private sealed class Run
    {
        public readonly RunTracker Tracker = new();
        public int Igt;
        public int Hp = 1000;

        public Run()
        {
            Tracker.Start(RouteOf(), Play(0, Hp));
            Tracker.Update(Play(0, Hp));
        }

        /// <summary>Lose health on this tick, the way a single blow or a single tick of poison does.</summary>
        public void Lose(int amount)
        {
            Hp -= amount;
            Tracker.Update(Play(Igt, Hp));
        }

        /// <summary>Let in-game time pass at health, the way the poll loop does between events.</summary>
        public void Idle(int ms)
        {
            var until = Igt + ms;
            while (Igt < until)
            {
                Igt = Math.Min(Igt + 100, until);
                Tracker.Update(Play(Igt, Hp));
            }
        }

        /// <summary>Take <paramref name="count"/> evenly spaced bites of the same size.</summary>
        public void Poisoned(int count, int amount = Poison, int intervalMs = PoisonIntervalMs)
        {
            for (var i = 0; i < count; i++)
            {
                if (i > 0) Idle(intervalMs);
                Lose(amount);
            }
        }
    }

    // --- the report this was written for ---

    [Fact]
    public void PoisonTicks_AreNotCountedAsHits()
    {
        var run = new Run();
        run.Poisoned(count: 6);
        run.Idle(5_000);

        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(6, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void PoisonTicks_AreStillDamage_SoNoDamageIsUnaffected()
    {
        var run = new Run();
        run.Poisoned(count: 6);
        run.Idle(5_000);

        // No Damage counts every drop in health. Poison is damage; it is just not
        // a hit. Only No Hit was ever meant to disagree with this number.
        Assert.Equal(6, run.Tracker.TotalDamage);
    }

    [Fact]
    public void TheFirstTwoTicks_AreHeldRatherThanCounted()
    {
        var run = new Run();
        run.Poisoned(count: 2);

        // Nothing can be shown to be poison yet, so neither is a hit and neither
        // is a tick. Counting them and retracting later would make the hit
        // counter flicker for as long as the effect lasted.
        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
        Assert.Equal(2, run.Tracker.TotalDamage);

        run.Idle(PoisonIntervalMs);
        run.Lose(Poison);

        Assert.Equal(3, run.Tracker.TotalTickDamage);
        Assert.Equal(0, run.Tracker.TotalHits);
    }

    [Fact]
    public void ASmallHitThatDoesNotRepeat_BecomesAHit()
    {
        var run = new Run();
        run.Lose(Poison);

        Assert.Equal(0, run.Tracker.TotalHits); // still waiting to see

        run.Idle(6_000);

        // Nothing followed it, so it was never a status effect. Late, but counted.
        Assert.Equal(1, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void TwoSmallHits_AreNotEnoughEvidence_AndBothBecomeHits()
    {
        var run = new Run();
        run.Poisoned(count: 2);
        run.Idle(6_000);

        // Two is a coincidence an enemy can produce; three is a rhythm. Erring
        // this way costs a couple of over-counted hits, and the other way hides
        // real ones.
        Assert.Equal(2, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    // --- what must not be mistaken for poison ---

    [Fact]
    public void AFastRunOfSmallHits_IsACombo_NotAStatusEffect()
    {
        var run = new Run();
        run.Poisoned(count: 5, intervalMs: 400);
        run.Idle(6_000);

        // Five light blows in two seconds is an enemy. The floor on spacing is
        // the whole reason this does not swallow a combo.
        Assert.Equal(5, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void RepeatedRealHits_AreTooLargeToBeTicks()
    {
        var run = new Run();
        run.Poisoned(count: 5, amount: 120);
        run.Idle(6_000);

        Assert.Equal(5, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void SmallHitsOfVeryDifferentSizes_DoNotFormAPattern()
    {
        var run = new Run();
        run.Lose(6);
        run.Idle(PoisonIntervalMs);
        run.Lose(35);
        run.Idle(PoisonIntervalMs);
        run.Lose(9);
        run.Idle(6_000);

        // A status effect bites the same amount every time. Three unrelated
        // scrapes do not become poison by being small.
        Assert.Equal(3, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void ARealHitDuringPoison_IsStillCounted()
    {
        var run = new Run();
        run.Poisoned(count: 3);          // 0, 2500, 5000 — the pattern is established

        run.Idle(1_000);
        run.Lose(180);                   // an enemy connects while the poison runs on

        run.Idle(1_500);
        run.Lose(Poison);                // 7500 — still on the poison's own rhythm
        run.Idle(PoisonIntervalMs);
        run.Lose(Poison);                // 10000
        run.Idle(6_000);

        // A blow landing between two ticks must neither be swallowed by the
        // pattern nor break it.
        Assert.Equal(1, run.Tracker.TotalHits);
        Assert.Equal(5, run.Tracker.TotalTickDamage);
        Assert.Equal(6, run.Tracker.TotalDamage);
    }

    [Fact]
    public void AFall_IsStillAFall_AndDoesNotJoinThePattern()
    {
        var run = new Run();
        run.Poisoned(count: 3);
        run.Idle(400);

        // Drop far enough, fast enough, to be landing damage — and for exactly
        // as much health as a tick of the poison costs, so nothing but the
        // descent distinguishes them.
        run.Tracker.Update(Play(run.Igt, run.Hp, y: 14));
        run.Igt += 150;
        run.Hp -= Poison;
        run.Tracker.Update(Play(run.Igt, run.Hp, y: 0));

        run.Idle(6_000);

        Assert.Equal(1, run.Tracker.TotalFallDamage);
        Assert.Equal(3, run.Tracker.TotalTickDamage);
        Assert.Equal(0, run.Tracker.TotalHits);
    }

    // --- settings and interruptions ---

    [Fact]
    public void TurningItOff_CountsPoisonAsHitsAgain()
    {
        var run = new Run();
        run.Tracker.OverTimeOptions = DamageOverTimeOptions.Default with { Enabled = false };

        run.Poisoned(count: 6);
        run.Idle(6_000);

        Assert.Equal(6, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void TurningItOffMidPattern_SettlesWhatWasHeldAsHits()
    {
        var run = new Run();
        run.Poisoned(count: 2);

        run.Tracker.OverTimeOptions = DamageOverTimeOptions.Default with { Enabled = false };
        run.Idle(100);

        // Held damage must not stay excluded from the hit count once the thing
        // that would have explained it is switched off.
        Assert.Equal(2, run.Tracker.TotalHits);
    }

    [Fact]
    public void ALoadingScreen_SettlesHeldDamageAsHits()
    {
        var run = new Run();
        run.Poisoned(count: 2);

        run.Tracker.Update(new GameSnapshot
        {
            Attached = true,
            PlayerLoaded = false,
            IsLoading = true,
            IgtMs = run.Igt,
        });

        // The evidence that would have settled these can never arrive now. The
        // safe reading is the one that cannot make an invalid run look clean.
        Assert.Equal(2, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void AWiderThresholdCanBeSet_ForSomethingThatBitesHarder()
    {
        var run = new Run();
        run.Tracker.OverTimeOptions = DamageOverTimeOptions.Default with { MaxTickDamage = 150 };

        run.Poisoned(count: 4, amount: 120);
        run.Idle(6_000);

        Assert.Equal(4, run.Tracker.TotalTickDamage);
        Assert.Equal(0, run.Tracker.TotalHits);
    }

    [Fact]
    public void HeldDamageIsNotCarriedAcrossACheckpoint_SoResumingCountsItAsAHit()
    {
        var run = new Run();
        run.Poisoned(count: 2);

        var state = run.Tracker.Capture();
        Assert.NotNull(state);

        var resumed = new RunTracker();
        Assert.True(resumed.Restore(RouteOf(), state!));

        // A restored run has no history for the pattern to be completed against,
        // so what was still in doubt resolves the safe way rather than staying
        // excluded from the hit count for the rest of the run.
        Assert.Equal(2, resumed.TotalDamage);
        Assert.Equal(2, resumed.TotalHits);
        Assert.Equal(0, resumed.TotalTickDamage);
    }

    [Fact]
    public void ConfirmedTicksSurviveACheckpoint()
    {
        var run = new Run();
        run.Poisoned(count: 4);

        var resumed = new RunTracker();
        Assert.True(resumed.Restore(RouteOf(), run.Tracker.Capture()!));

        Assert.Equal(4, resumed.TotalDamage);
        Assert.Equal(4, resumed.TotalTickDamage);
        Assert.Equal(0, resumed.TotalHits);
    }
}
