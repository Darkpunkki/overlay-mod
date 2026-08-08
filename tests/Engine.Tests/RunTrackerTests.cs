using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Tracking;
using Xunit;

namespace OverlayMod.Engine.Tests;

public class RunTrackerTests
{
    // --- helpers ---

    private static GameSnapshot Play(int igt, int hp, bool boss = false, float y = 0) => new()
    {
        Attached = true,
        PlayerLoaded = true,
        IsLoading = false,
        IgtMs = igt,
        Hp = hp,
        MaxHp = 1000,
        BossFightActive = boss,
        Y = y,
    };

    /// <summary>
    /// A loading screen the game raises while the character is still allocated.
    /// Dark Souls III does this as the death fade begins, which is why deaths
    /// cannot be detected only while fully in play.
    /// </summary>
    private static GameSnapshot LoadingWithBody(int igt, int hp) => new()
    {
        Attached = true,
        PlayerLoaded = true,
        IsLoading = true,
        IgtMs = igt,
        Hp = hp,
        MaxHp = 1000,
    };

    private static GameSnapshot Loading(int igt) => new()
    {
        Attached = true,
        PlayerLoaded = false,
        IsLoading = true,
        IgtMs = igt,
    };

    private static Route RouteOf(params RouteSplit[] splits) =>
        new("test", ChallengeProfile.NoHit, splits);

    /// <summary>
    /// Hold a reading for long enough to outlast the death confirmation. A real
    /// corpse lies there for seconds; this is a fraction of one.
    /// </summary>
    private static void Hold(RunTracker t, GameSnapshot s, int ticks = 6)
    {
        for (var i = 0; i < ticks; i++) t.Update(s);
    }

    private sealed class FakeFlags : IFlagSource
    {
        public readonly HashSet<uint> Set = new();
        public bool IsEventFlagSet(uint flagId) => Set.Contains(flagId);
    }

    // --- lifecycle ---

    [Fact]
    public void Start_PutsTrackerInRunningWithFreshSplits()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false), new RouteSplit("B", true, 100u)), Play(0, 1000));

        Assert.Equal(RunPhase.Running, t.Phase);
        Assert.Equal(2, t.Splits.Count);
        Assert.Equal(0, t.ActiveIndex);
        Assert.Equal("A", t.ActiveSplit!.Name);
    }

    [Fact]
    public void Update_DoesNothingBeforeStart()
    {
        var t = new RunTracker();
        t.Update(Play(1000, 500));
        Assert.Equal(RunPhase.NotStarted, t.Phase);
    }

    // --- timing ---

    [Fact]
    public void Timer_AccumulatesIgtDeltas_AndExcludesLoads()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(1000, 1000));

        t.Update(Play(1000, 1000)); // baseline
        t.Update(Play(1500, 1000)); // +500
        t.Update(Loading(1500));    // load: not counted, baseline dropped
        t.Update(Play(1500, 1000)); // re-baseline after load
        t.Update(Play(2000, 1000)); // +500

        Assert.Equal(1000, t.ActiveSplit!.Approach.IgtMs);
        Assert.Equal(1000, t.RunIgtMs); // 2000 - 1000
    }

    [Fact]
    public void Timer_IgnoresImplausibleJumps()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));
        t.Update(Play(0, 1000));        // baseline
        t.Update(Play(5_000_000, 1000)); // huge jump (save load) -> ignored
        Assert.Equal(0, t.ActiveSplit!.Approach.IgtMs);
    }

    // --- damage ---

    [Fact]
    public void Damage_CountsDistinctDrops_DebounceConsecutive_IgnoreHeals()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));

        t.Update(Play(0, 1000)); // baseline
        t.Update(Play(0, 900));  // hit 1
        Assert.Equal(1, t.ActiveSplit!.Approach.Damage);

        t.Update(Play(0, 800));  // consecutive drop -> debounced
        Assert.Equal(1, t.ActiveSplit.Approach.Damage);

        t.Update(Play(0, 800));  // stable -> resets decreasing
        t.Update(Play(0, 950));  // heal -> no hit
        Assert.Equal(1, t.ActiveSplit.Approach.Damage);

        t.Update(Play(0, 780));  // new distinct drop -> hit 2
        Assert.Equal(2, t.ActiveSplit.Approach.Damage);

        // Both drops are far too large to be a status tick, so both are hits on
        // sight rather than being held to see whether a rhythm develops.
        // Nothing fell, so every one of them is a hit as well as damage.
        Assert.Equal(2, t.ActiveSplit.Approach.Hits);
        Assert.Equal(0, t.ActiveSplit.Approach.FallDamage);
    }

    // --- fall damage: what separates No Hit from No Damage ---

    [Fact]
    public void Fall_DamageAfterADropIsNotAHit()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000, y: 20));

        t.Update(Play(0, 1000, y: 20));      // standing on a ledge
        t.Update(Play(100, 1000, y: 12));    // falling
        t.Update(Play(200, 1000, y: 4));
        t.Update(Play(300, 900, y: 0));      // landed hard

        var split = t.ActiveSplit!;
        Assert.Equal(1, split.Approach.Damage);
        Assert.Equal(1, split.Approach.FallDamage);
        Assert.Equal(0, split.Approach.Hits);   // No Hit is unharmed by this
        Assert.Equal(1, t.TotalDamage);
        Assert.Equal(0, t.TotalHits);
    }

    [Fact]
    public void Fall_DamageOnLevelGroundIsAnOrdinaryHit()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));

        t.Update(Play(0, 1000));
        t.Update(Play(100, 1000));
        t.Update(Play(200, 900));

        Assert.Equal(1, t.ActiveSplit!.Approach.Hits);
        Assert.Equal(0, t.ActiveSplit.Approach.FallDamage);
    }

    [Fact]
    public void Fall_AnOldDescentDoesNotExcuseALaterHit()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000, y: 20));

        t.Update(Play(0, 1000, y: 20));
        t.Update(Play(100, 1000, y: 0));   // landed safely
        t.Update(Play(900, 1000, y: 0));   // well outside the window
        t.Update(Play(1000, 900, y: 0));   // hit by something, long after

        Assert.Equal(1, t.ActiveSplit!.Approach.Hits);
        Assert.Equal(0, t.ActiveSplit.Approach.FallDamage);
    }

    [Fact]
    public void Fall_DetectionCanBeTurnedOff()
    {
        var t = new RunTracker { FallOptions = FallDamageOptions.Default with { Enabled = false } };
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000, y: 20));

        t.Update(Play(0, 1000, y: 20));
        t.Update(Play(200, 900, y: 0));

        // Off means No Hit counts what No Damage counts, which is the honest
        // behaviour when the heuristic is not trusted.
        Assert.Equal(1, t.ActiveSplit!.Approach.Hits);
        Assert.Equal(0, t.ActiveSplit.Approach.FallDamage);
    }

    [Fact]
    public void Fall_ATeleportIsNotAFall()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000, y: 500));

        // A bonfire warp moves the player hundreds of metres between two ticks.
        // Treating that as a descent would excuse the next hit taken.
        t.Update(Play(0, 1000, y: 500));
        t.Update(Play(100, 1000, y: 0));
        t.Update(Play(200, 900, y: 0));

        Assert.Equal(1, t.ActiveSplit!.Approach.Hits);
    }

    [Fact]
    public void RecentDamage_KeepsTheEvidenceForEachCall()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000, y: 20));

        t.Update(Play(0, 1000, y: 20));
        t.Update(Play(200, 900, y: 0));

        var e = Assert.Single(t.RecentDamage);
        Assert.True(e.CountedAsFall);
        Assert.Equal(20, e.DescentMetres, 1);
        Assert.Equal("A", e.SplitName);
        Assert.False(e.Fatal);
    }

    // --- deaths ---

    [Fact]
    public void Death_CountsOnce_PlusKillingBlow_AndSurvivesRespawn()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));

        t.Update(Play(0, 1000)); // baseline
        Hold(t, Play(0, 0));     // death: 1 death + killing-blow hit
        Assert.Equal(1, t.ActiveSplit!.Approach.Deaths);
        Assert.Equal(1, t.ActiveSplit.Approach.Hits);

        Hold(t, Play(0, 0));      // still dead, no change
        t.Update(Loading(0));     // load to bonfire
        t.Update(Play(0, 1000));  // respawn refill -> not a hit

        Assert.Equal(1, t.ActiveSplit.Approach.Deaths);
        Assert.Equal(1, t.ActiveSplit.Approach.Hits);
    }

    [Fact]
    public void Death_IsCountedEvenWhenTheLoadingFlagRisesOnTheSameTick()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));

        t.Update(Play(0, 1000));

        // The game raises its loading flag as the death fade starts, so the
        // tick where health first reads zero may not be an in-play tick at all.
        // Detecting death as an edge needs both of its neighbours and loses it.
        Hold(t, LoadingWithBody(0, 0));

        Assert.Equal(1, t.ActiveSplit!.Approach.Deaths);
    }

    [Fact]
    public void Death_IsCountedWhenTheFirstZeroReadingIsTheOnlyOneSeen()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));

        t.Update(Play(0, 1000));
        t.Update(GameSnapshot.Detached);  // a dropped poll, or the game stuttering
        Hold(t, Play(0, 0));              // the corpse is all we ever see

        Assert.Equal(1, t.ActiveSplit!.Approach.Deaths);
    }

    [Fact]
    public void Death_IsNotInventedByAttachingToAPlayerWhoIsAlreadyDead()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 0));

        // Health has never been seen above zero, so this is a reading we cannot
        // interpret rather than a death that just happened.
        Hold(t, Play(0, 0), ticks: 60);

        Assert.Equal(0, t.ActiveSplit!.Approach.Deaths);
    }

    [Fact]
    public void Death_LyingDeadForManyTicksIsStillOneDeath()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));
        t.Update(Play(0, 1000));

        for (var i = 0; i < 300; i++) t.Update(Play(i * 33, 0));

        Assert.Equal(1, t.ActiveSplit!.Approach.Deaths);
        Assert.Equal(1, t.ActiveSplit.Approach.Damage);
    }

    [Fact]
    public void Death_TwoDeathsInOneSplitBothCount()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));

        t.Update(Play(0, 1000));
        Hold(t, Play(0, 0));        // first death
        t.Update(Loading(0));       // back to the bonfire
        t.Update(Play(0, 1000));
        Hold(t, Play(0, 0));        // second death

        Assert.Equal(2, t.ActiveSplit!.Approach.Deaths);
    }

    [Fact]
    public void Death_UnpopulatedHealthReadingsAfterALoadAreNotDeaths()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));
        t.Update(Play(0, 1000));
        t.Update(Loading(0));

        // The first frames after a load can report zeroes the game has not
        // written yet. They pass in an instant, which is what tells them apart
        // from a corpse - not any second reading from memory.
        t.Update(new GameSnapshot { Attached = true, PlayerLoaded = true, Hp = 0, MaxHp = 0 });
        t.Update(new GameSnapshot { Attached = true, PlayerLoaded = true, Hp = 0, MaxHp = 0 });
        t.Update(Play(0, 1000));

        Assert.Equal(0, t.ActiveSplit!.Approach.Deaths);
        Assert.Equal(0, t.ActiveSplit.Approach.Damage);
    }

    [Fact]
    public void Health_IsTrackedWhenMaxHealthReadsZero()
    {
        // 0.2.0 required MaxHp > 0 as proof the reading was real, which switched
        // every counter off on a game where that offset reads zero: damage, hits
        // and deaths all stuck at zero while the timer carried on, because the
        // timer needs no such reading. Nothing here may depend on max health.
        var t = new RunTracker();
        var alive = new GameSnapshot { Attached = true, PlayerLoaded = true, IgtMs = 0, Hp = 1000, MaxHp = 0 };
        var hurt = alive with { Hp = 900 };

        t.Start(RouteOf(new RouteSplit("A", false)), alive);
        t.Update(alive);
        t.Update(hurt);

        Assert.Equal(1, t.ActiveSplit!.Approach.Damage);
        Assert.Equal(1, t.ActiveSplit.Approach.Hits);
    }

    [Fact]
    public void Death_IsCountedWhenMaxHealthReadsZero()
    {
        var t = new RunTracker();
        var alive = new GameSnapshot { Attached = true, PlayerLoaded = true, Hp = 1000, MaxHp = 0 };

        t.Start(RouteOf(new RouteSplit("A", false)), alive);
        t.Update(alive);
        Hold(t, alive with { Hp = 0 });

        Assert.Equal(1, t.ActiveSplit!.Approach.Deaths);
    }

    [Fact]
    public void Hits_AreNotInventedAcrossALoadingScreen()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));
        t.Update(Play(0, 1000));

        // Health readings taken during a load describe a world being torn down
        // and rebuilt. Comparing them against the last one seen in play would
        // book damage nobody took.
        t.Update(LoadingWithBody(0, 400));
        t.Update(LoadingWithBody(0, 200));
        t.Update(Play(0, 1000));

        Assert.Equal(0, t.ActiveSplit!.Approach.Damage);
    }

    // --- approach vs boss attribution ---

    [Fact]
    public void Attribution_RoutesHitsToActiveSegment()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("Boss", true)), Play(0, 1000));

        t.Update(Play(0, 1000));            // baseline, approach
        t.Update(Play(0, 900));             // approach hit
        t.Update(Play(0, 900, boss: true)); // boss starts (stable read resets decreasing)
        t.Update(Play(0, 800, boss: true)); // boss hit

        Assert.Equal(1, t.ActiveSplit!.Approach.Hits);
        Assert.Equal(1, t.ActiveSplit.Boss.Hits);
    }

    // --- splitting ---

    [Fact]
    public void AutoSplit_AdvancesOnFlagRisingEdge_AndFinishes()
    {
        var flags = new FakeFlags();
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("Boss A", true, 100u), new RouteSplit("Boss B", true, 200u)), Play(0, 1000));

        t.Update(Play(0, 1000), flags); // no flags set
        Assert.Equal(0, t.ActiveIndex);

        flags.Set.Add(100);
        t.Update(Play(0, 1000), flags); // rising edge -> advance
        Assert.Equal(1, t.ActiveIndex);
        Assert.True(t.Splits[0].Completed);

        flags.Set.Add(200);
        t.Update(Play(0, 1000), flags); // advance past last -> finished
        Assert.Equal(RunPhase.Finished, t.Phase);
        Assert.True(t.Splits[1].Completed);
    }

    [Fact]
    public void ManualSplit_AdvancesAndFinishes()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false), new RouteSplit("B", false)), Play(0, 1000));

        t.Split();
        Assert.Equal(1, t.ActiveIndex);

        t.Split();
        Assert.Equal(RunPhase.Finished, t.Phase);
    }

    [Fact]
    public void PrimaryValue_FollowsProfileMetric()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));
        t.Update(Play(0, 1000));
        t.Update(Play(0, 900)); // 1 hit

        // NoHit profile -> primary metric is hits
        Assert.Equal(1, t.PrimaryValue);
        Assert.Equal(1, t.TotalHits);
    }

    [Fact]
    public void PrimaryValue_CountsAFallForNoDamageButNotForNoHit()
    {
        static RunTracker RunOneFall(ChallengeProfile profile)
        {
            var t = new RunTracker();
            t.Start(new Route("test", profile, new[] { new RouteSplit("A", false) }), Play(0, 1000, y: 20));
            t.Update(Play(0, 1000, y: 20));
            t.Update(Play(200, 900, y: 0));
            return t;
        }

        // The same fall, judged by two challenges. This is the difference the
        // whole feature exists for.
        Assert.Equal(1, RunOneFall(ChallengeProfile.NoDamage).PrimaryValue);
        Assert.Equal(0, RunOneFall(ChallengeProfile.NoHit).PrimaryValue);
    }
}
