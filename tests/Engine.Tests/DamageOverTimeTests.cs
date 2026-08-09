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
/// path between the two runs through the debounce, the health scale and the fall
/// detector.
///
/// **The numbers here are the game's, not invented.** Poison and toxic in Dark
/// Souls III tick <em>once a second</em> for a bite proportional to maximum
/// health. 0.2.2 was written against a guess of one tick every 2.5 seconds for a
/// flat 40 HP, passed its own tests, and did nothing whatsoever in a real game.
/// </summary>
public class DamageOverTimeTests
{
    private const int MaxHealth = 1000;

    /// <summary>The real cadence: once a second, every second.</summary>
    private const int PoisonIntervalMs = 1000;

    /// <summary>A plausible bite — proportional to health, and well under the ceiling.</summary>
    private const int Poison = 20;

    private static GameSnapshot Play(int igt, int hp, float y = 0, int maxHp = MaxHealth) => new()
    {
        Attached = true,
        PlayerLoaded = true,
        IsLoading = false,
        IgtMs = igt,
        Hp = hp,
        MaxHp = maxHp,
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
        public int Hp = MaxHealth;
        public int MaxHp = MaxHealth;

        public Run(int maxHp = MaxHealth)
        {
            MaxHp = maxHp;
            Hp = maxHp;
            Tracker.Start(RouteOf(), Play(0, Hp, maxHp: maxHp));
            Tracker.Update(Play(0, Hp, maxHp: maxHp));
        }

        /// <summary>Lose health on this tick, the way a single blow or a single tick of poison does.</summary>
        public void Lose(int amount)
        {
            Hp -= amount;
            Tracker.Update(Play(Igt, Hp, maxHp: MaxHp));
        }

        /// <summary>Let in-game time pass at health, the way the poll loop does between events.</summary>
        public void Idle(int ms)
        {
            var until = Igt + ms;
            while (Igt < until)
            {
                Igt = Math.Min(Igt + 33, until);   // roughly the real 30 Hz poll
                Tracker.Update(Play(Igt, Hp, maxHp: MaxHp));
            }
        }

        /// <summary>Rest at a bonfire: health back to maximum, and the poison cured.</summary>
        public void HealToFull()
        {
            Hp = MaxHp;
            Tracker.Update(Play(Igt, Hp, maxHp: MaxHp));
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
    public void PoisonAtTheGamesRealCadence_IsNotCountedAsHits()
    {
        var run = new Run();
        run.Poisoned(count: 12);          // twelve seconds of poison
        run.Idle(4_000);

        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(12, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void ToxicBitesHarderButTicksTheSame()
    {
        var run = new Run();
        run.Poisoned(count: 10, amount: 60);   // 6% of maximum health a second
        run.Idle(4_000);

        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(10, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void TheBiteScalesWithTheCharacter_SoAnEarlyOneIsCoveredToo()
    {
        // A freshly started character has a fraction of the health a late one
        // does, and poison takes a fraction of the damage to match. An absolute
        // ceiling in HP is right for one of them at best — which is how 0.2.2
        // came to do nothing.
        var early = new Run(maxHp: 450);
        early.Poisoned(count: 8, amount: 9);
        early.Idle(4_000);

        var late = new Run(maxHp: 1600);
        late.Poisoned(count: 8, amount: 32);
        late.Idle(4_000);

        Assert.Equal(0, early.Tracker.TotalHits);
        Assert.Equal(8, early.Tracker.TotalTickDamage);
        Assert.Equal(0, late.Tracker.TotalHits);
        Assert.Equal(8, late.Tracker.TotalTickDamage);
    }

    [Fact]
    public void PoisonTicks_AreStillDamage_SoNoDamageIsUnaffected()
    {
        var run = new Run();
        run.Poisoned(count: 12);
        run.Idle(4_000);

        // No Damage counts every drop in health. Poison is damage; it is just not
        // a hit. Only No Hit was ever meant to disagree with this number.
        Assert.Equal(12, run.Tracker.TotalDamage);
    }

    [Fact]
    public void TheFirstThreeTicks_AreHeldRatherThanCounted()
    {
        var run = new Run();
        run.Poisoned(count: 3);

        // Three bites give two gaps, and two gaps agreeing is not yet a rhythm.
        // Neither a hit nor a tick until the fourth settles it — counting them
        // and retracting later would make the hit counter flicker once a second
        // for as long as the poison lasted.
        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
        Assert.Equal(3, run.Tracker.TotalDamage);

        run.Idle(PoisonIntervalMs);
        run.Lose(Poison);

        Assert.Equal(4, run.Tracker.TotalTickDamage);
        Assert.Equal(0, run.Tracker.TotalHits);
    }

    [Fact]
    public void ASmallHitThatDoesNotRepeat_BecomesAHit()
    {
        var run = new Run();
        run.Lose(Poison);

        Assert.Equal(0, run.Tracker.TotalHits); // still waiting to see

        run.Idle(4_000);

        // Nothing followed it, so it was never a status effect. Late, but counted.
        Assert.Equal(1, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void ThreeSmallHits_WithTheVariationCombatAlwaysHas_AreStillHits()
    {
        var run = new Run();
        run.Lose(40);
        run.Idle(900);   run.Lose(47);
        run.Idle(1_300); run.Lose(38);
        run.Idle(4_000);

        // Three is one gap short of what it takes to believe in a rhythm partway
        // through, so a run this size is only ever resolved as poison if it is
        // too precise for combat to have produced. Blows that differ by a fifth
        // in size and a third in spacing are not.
        Assert.Equal(3, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void AShortPoisoningCutOffEarly_IsStillResolvedAsPoison()
    {
        var run = new Run();
        run.Poisoned(count: 3);   // poisoned at the bonfire, then cured by resting
        run.Idle(4_000);

        // This is the bonfire report: poison procs, three ticks land, sitting
        // cures it, and the run ends before a fourth tick could confirm it.
        // Charging three hits for that is the worst possible answer, and the
        // ticks are identical to a precision no enemy achieves.
        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(3, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void TwoTicks_AreStillNeverEnough()
    {
        var run = new Run();
        run.Poisoned(count: 2);
        run.Idle(4_000);

        // One gap is a coincidence however exact it looks.
        Assert.Equal(2, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    // --- resting at a bonfire, which is where a poisoned player goes ---
    //
    // Every one of these charged a hit before 0.2.4, for a player who took none.
    // They share a cause: the rhythm was being demanded of every tick rather than
    // used to recognise the effect, so the ordinary end of an ordinary poisoning
    // orphaned a tick and billed it.

    [Fact]
    public void TheLastTickBeforeACure_ArrivesOffTheBeat_AndIsStillPoison()
    {
        var run = new Run();
        run.Poisoned(count: 8);

        // The sit animation runs on; one last tick lands late, out of step.
        run.Idle(2_000);
        run.Lose(Poison);
        run.HealToFull();
        run.Idle(4_000);

        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(9, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void TheFirstTickAfterAHeal_IsABiggerBite_AndIsStillPoison()
    {
        var run = new Run();
        run.Poisoned(count: 8, amount: 10);

        // The bite is a share of health, so healing makes the next one larger.
        // Estus mid-fight does this as surely as a bonfire does.
        run.HealToFull();
        run.Idle(PoisonIntervalMs); run.Lose(30);
        run.Idle(PoisonIntervalMs); run.Lose(30);
        run.Idle(4_000);

        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(10, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void RestingAfterALongPoisoning_CostsNothing()
    {
        var run = new Run();
        run.Poisoned(count: 8);
        run.HealToFull();
        run.Idle(5_000);

        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(8, run.Tracker.TotalTickDamage);
    }

    // --- what must not be mistaken for poison ---

    [Fact]
    public void UnevenlySpacedSmallHits_AreNotARhythm()
    {
        var run = new Run();
        run.Lose(Poison);
        run.Idle(400);  run.Lose(Poison);
        run.Idle(1_500); run.Lose(Poison);
        run.Idle(700);  run.Lose(Poison);
        run.Idle(4_000);

        // Same size, similar ballpark of spacing, but not a metronome. Evenness
        // is the whole discriminator now that the cadence is known to be fast.
        Assert.Equal(4, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void AMeleeCombo_IsNotAStatusEffect()
    {
        var run = new Run();
        run.Lose(40);
        run.Idle(320); run.Lose(45);
        run.Idle(280); run.Lose(38);
        run.Idle(500); run.Lose(44);
        run.Idle(4_000);

        Assert.Equal(4, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void RepeatedRealHits_AreTooLargeToBeTicks()
    {
        var run = new Run();
        run.Poisoned(count: 6, amount: 120);   // 12% of maximum health each
        run.Idle(4_000);

        Assert.Equal(6, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void SmallHitsOfVeryDifferentSizes_DoNotFormAPattern()
    {
        var run = new Run();
        run.Lose(6);
        run.Idle(PoisonIntervalMs); run.Lose(60);
        run.Idle(PoisonIntervalMs); run.Lose(9);
        run.Idle(PoisonIntervalMs); run.Lose(55);
        run.Idle(4_000);

        Assert.Equal(4, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void ARealHitDuringPoison_IsStillCounted()
    {
        var run = new Run();
        run.Poisoned(count: 4);          // the rhythm is established

        run.Idle(400);
        run.Lose(300);                   // an enemy connects between two ticks
        run.Idle(600);
        run.Lose(Poison);                // the poison carries on, on its own clock

        run.Idle(PoisonIntervalMs); run.Lose(Poison);
        run.Idle(4_000);

        // A blow landing between two ticks must neither be swallowed by the
        // pattern nor break it.
        Assert.Equal(1, run.Tracker.TotalHits);
        Assert.Equal(6, run.Tracker.TotalTickDamage);
        Assert.Equal(7, run.Tracker.TotalDamage);
    }

    [Fact]
    public void AFall_IsStillAFall_AndDoesNotJoinThePattern()
    {
        var run = new Run();
        run.Poisoned(count: 4);
        run.Idle(400);

        // Drop far enough, fast enough, to be landing damage — and for exactly
        // as much health as a tick of the poison costs, so nothing but the
        // descent distinguishes them.
        run.Tracker.Update(Play(run.Igt, run.Hp, y: 14));
        run.Igt += 150;
        run.Hp -= Poison;
        run.Tracker.Update(Play(run.Igt, run.Hp, y: 0));

        run.Idle(4_000);

        Assert.Equal(1, run.Tracker.TotalFallDamage);
        Assert.Equal(4, run.Tracker.TotalTickDamage);
        Assert.Equal(0, run.Tracker.TotalHits);
    }

    // --- the health scale ---

    [Fact]
    public void TheCeilingIsStillUsableWhenMaxHealthReadsZero()
    {
        // The 0.2.0 failure, rebuilt in a new place: a percentage of a maximum
        // that reads zero is a ceiling of zero, and every tick would count as a
        // hit again on exactly the game builds that already suffer most.
        var run = new Run(maxHp: 0);
        run.Hp = 1000;
        run.Tracker.Update(Play(0, 1000, maxHp: 0));

        run.Poisoned(count: 8);
        run.Idle(4_000);

        Assert.Equal(0, run.Tracker.TotalHits);
        Assert.Equal(8, run.Tracker.TotalTickDamage);
    }

    // --- settings and interruptions ---

    [Fact]
    public void TurningItOff_CountsPoisonAsHitsAgain()
    {
        var run = new Run();
        run.Tracker.OverTimeOptions = DamageOverTimeOptions.Default with { Enabled = false };

        run.Poisoned(count: 8);
        run.Idle(4_000);

        Assert.Equal(8, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void TurningItOffMidPattern_SettlesWhatWasHeldAsHits()
    {
        var run = new Run();
        run.Poisoned(count: 3);

        run.Tracker.OverTimeOptions = DamageOverTimeOptions.Default with { Enabled = false };
        run.Idle(100);

        // Held damage must not stay excluded from the hit count once the thing
        // that would have explained it is switched off.
        Assert.Equal(3, run.Tracker.TotalHits);
    }

    [Fact]
    public void ALoadingScreen_SettlesHeldDamageAsHits()
    {
        var run = new Run();
        run.Poisoned(count: 3);

        run.Tracker.Update(new GameSnapshot
        {
            Attached = true,
            PlayerLoaded = false,
            IsLoading = true,
            IgtMs = run.Igt,
        });

        // The evidence that would have settled these can never arrive now. The
        // safe reading is the one that cannot make an invalid run look clean.
        Assert.Equal(3, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void ATighterCeilingCanBeSet_IfItIsSwallowingRealHits()
    {
        var run = new Run();
        run.Tracker.OverTimeOptions = DamageOverTimeOptions.Default with { MaxTickPercent = 1.0 };

        run.Poisoned(count: 8, amount: 60);   // 6% of maximum health, now over the line
        run.Idle(4_000);

        Assert.Equal(8, run.Tracker.TotalHits);
        Assert.Equal(0, run.Tracker.TotalTickDamage);
    }

    [Fact]
    public void HeldDamageIsNotCarriedAcrossACheckpoint_SoResumingCountsItAsAHit()
    {
        var run = new Run();
        run.Poisoned(count: 3);

        var state = run.Tracker.Capture();
        Assert.NotNull(state);

        var resumed = new RunTracker();
        Assert.True(resumed.Restore(RouteOf(), state!));

        // A restored run has no history for the pattern to be completed against,
        // so what was still in doubt resolves the safe way rather than staying
        // excluded from the hit count for the rest of the run.
        Assert.Equal(3, resumed.TotalDamage);
        Assert.Equal(3, resumed.TotalHits);
        Assert.Equal(0, resumed.TotalTickDamage);
    }

    [Fact]
    public void ConfirmedTicksSurviveACheckpoint()
    {
        var run = new Run();
        run.Poisoned(count: 6);

        var resumed = new RunTracker();
        Assert.True(resumed.Restore(RouteOf(), run.Tracker.Capture()!));

        Assert.Equal(6, resumed.TotalDamage);
        Assert.Equal(6, resumed.TotalTickDamage);
        Assert.Equal(0, resumed.TotalHits);
    }
}
