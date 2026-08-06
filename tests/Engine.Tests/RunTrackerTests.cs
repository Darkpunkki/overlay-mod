using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Tracking;
using Xunit;

namespace OverlayMod.Engine.Tests;

public class RunTrackerTests
{
    // --- helpers ---

    private static GameSnapshot Play(int igt, int hp, bool boss = false) => new()
    {
        Attached = true,
        PlayerLoaded = true,
        IsLoading = false,
        IgtMs = igt,
        Hp = hp,
        MaxHp = 1000,
        BossFightActive = boss,
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

    // --- hits ---

    [Fact]
    public void Hits_CountDistinctDrops_DebounceConsecutive_IgnoreHeals()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));

        t.Update(Play(0, 1000)); // baseline
        t.Update(Play(0, 900));  // hit 1
        Assert.Equal(1, t.ActiveSplit!.Approach.Hits);

        t.Update(Play(0, 800));  // consecutive drop -> debounced
        Assert.Equal(1, t.ActiveSplit.Approach.Hits);

        t.Update(Play(0, 800));  // stable -> resets decreasing
        t.Update(Play(0, 950));  // heal -> no hit
        Assert.Equal(1, t.ActiveSplit.Approach.Hits);

        t.Update(Play(0, 900));  // new distinct drop -> hit 2
        Assert.Equal(2, t.ActiveSplit.Approach.Hits);
    }

    // --- deaths ---

    [Fact]
    public void Death_CountsOnce_PlusKillingBlow_AndSurvivesRespawn()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", false)), Play(0, 1000));

        t.Update(Play(0, 1000)); // baseline
        t.Update(Play(0, 0));    // death: 1 death + killing-blow hit
        Assert.Equal(1, t.ActiveSplit!.Approach.Deaths);
        Assert.Equal(1, t.ActiveSplit.Approach.Hits);

        t.Update(Play(0, 0));     // still dead, no change
        t.Update(Loading(0));     // load to bonfire
        t.Update(Play(0, 1000));  // respawn refill -> not a hit

        Assert.Equal(1, t.ActiveSplit.Approach.Deaths);
        Assert.Equal(1, t.ActiveSplit.Approach.Hits);
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
}
