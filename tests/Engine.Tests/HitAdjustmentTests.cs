using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Tracking;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// The player's manual corrections to the hit count. The detectors are
/// heuristics and the memory read can miss a drop outright, so the player gets
/// the last word — but only over hits, only for the run in progress, only for
/// splits it has reached, and never below zero.
/// </summary>
public class HitAdjustmentTests
{
    private static GameSnapshot Play(int igt, int hp) => new()
    {
        Attached = true,
        PlayerLoaded = true,
        IsLoading = false,
        IgtMs = igt,
        Hp = hp,
        MaxHp = 1000,
    };

    private static Route RouteOf(params RouteSplit[] splits) =>
        new("test", ChallengeProfile.NoHit, splits);

    /// <summary>A run with one real hit already counted on the first split.</summary>
    private static RunTracker TrackerWithOneHit()
    {
        var t = new RunTracker();
        t.Start(RouteOf(new RouteSplit("A", true), new RouteSplit("B", true)), Play(0, 1000));
        t.Update(Play(0, 1000));   // baseline
        t.Update(Play(100, 900));  // 100 HP: over the tick ceiling, a hit on sight
        return t;
    }

    [Fact]
    public void Adjust_MovesHitsInBothDirections_AndLeavesDamageAlone()
    {
        var t = TrackerWithOneHit();
        Assert.Equal(1, t.TotalHits);

        Assert.True(t.AdjustHits(0, 1));
        Assert.Equal(2, t.Splits[0].Hits);
        Assert.Equal(2, t.TotalHits);

        Assert.True(t.AdjustHits(0, -1));
        Assert.Equal(1, t.TotalHits);

        // The correction is about what No Hit says; what No Damage measured
        // is a fact, and stays one.
        Assert.Equal(1, t.TotalDamage);
    }

    [Fact]
    public void Adjust_RefusesToTakeACountBelowZero()
    {
        var t = TrackerWithOneHit();

        Assert.False(t.AdjustHits(0, -2));
        Assert.Equal(1, t.Splits[0].Hits);

        Assert.True(t.AdjustHits(0, -1));
        Assert.False(t.AdjustHits(0, -1));
        Assert.Equal(0, t.Splits[0].Hits);
    }

    [Fact]
    public void Adjust_RefusesASplitTheRunHasNotReached()
    {
        var t = TrackerWithOneHit();

        Assert.False(t.AdjustHits(1, 1));  // still ahead of the run
        Assert.False(t.AdjustHits(-1, 1));
        Assert.False(t.AdjustHits(2, 1));
        Assert.Equal(1, t.TotalHits);
    }

    [Fact]
    public void Adjust_WorksOnACompletedSplit_WhileTheRunGoesOn()
    {
        var t = TrackerWithOneHit();
        t.Split(); // A completed, B active

        Assert.True(t.AdjustHits(0, 1));
        Assert.Equal(2, t.Splits[0].Hits);
        Assert.Equal(2, t.TotalHits);
    }

    [Fact]
    public void Adjust_RefusesWithoutARunInProgress()
    {
        var fresh = new RunTracker();
        Assert.False(fresh.AdjustHits(0, 1));

        var finished = TrackerWithOneHit();
        finished.Split();
        finished.Split(); // no splits left: the run is over, and history is history
        Assert.Equal(RunPhase.Finished, finished.Phase);
        Assert.False(finished.AdjustHits(0, 1));
    }

    [Fact]
    public void CaptureAndRestore_CarryTheCorrection()
    {
        var t = TrackerWithOneHit();
        Assert.True(t.AdjustHits(0, 2));

        var state = t.Capture();
        Assert.NotNull(state);

        var restored = new RunTracker();
        Assert.True(restored.Restore(
            RouteOf(new RouteSplit("A", true), new RouteSplit("B", true)), state!));

        Assert.Equal(2, restored.Splits[0].HitAdjustment);
        Assert.Equal(3, restored.Splits[0].Hits);
        Assert.Equal(3, restored.TotalHits);
    }
}
