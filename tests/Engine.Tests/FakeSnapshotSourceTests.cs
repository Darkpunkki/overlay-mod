using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Tracking;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// The fake source is the harness the overlay is developed against, so its
/// scripted run has to produce exactly the results it claims to. If these break,
/// every number on screen during development is suspect.
/// </summary>
public class FakeSnapshotSourceTests
{
    /// <summary>A source driven by an explicit clock rather than wall time.</summary>
    private static (FakeSnapshotSource Source, Func<int> Now, Action<int> SetNow) Controlled(
        IReadOnlyList<FakeKeyframe>? script = null, int? loopMs = null)
    {
        var now = 0;
        var source = new FakeSnapshotSource(script, loopMs, () => now);
        return (source, () => now, t => now = t);
    }

    private static Route DemoRoute() => new(
        "demo",
        ChallengeProfile.NoHit,
        new[]
        {
            new RouteSplit("Iudex Gundyr", true, 14000800),
            new RouteSplit("Vordt of the Boreal Valley", true, 13000800),
            new RouteSplit("Curse-rotted Greatwood", true, 13100800),
        });

    /// <summary>Replay the demo script into a tracker at a fixed step.</summary>
    private static RunTracker RunDemo(int untilMs, int stepMs = 50)
    {
        var (source, _, setNow) = Controlled();
        var tracker = new RunTracker();
        var started = false;

        for (var t = 0; t <= untilMs; t += stepMs)
        {
            setNow(t);
            var snapshot = source.TakeSnapshot();

            if (!started && snapshot.PlayerLoaded)
            {
                tracker.Start(DemoRoute(), snapshot);
                started = true;
            }

            if (started) tracker.Update(snapshot, source.Flags);
        }

        return tracker;
    }

    // --- the script's own behaviour ---

    [Fact]
    public void PlayerIsNotLoadedBeforeTheFirstLoadingScreenCompletes()
    {
        var (source, _, setNow) = Controlled();

        setNow(0);
        Assert.False(source.TakeSnapshot().PlayerLoaded);

        setNow(2_000);
        Assert.True(source.TakeSnapshot().IsLoading);

        setNow(4_000);
        var snapshot = source.TakeSnapshot();
        Assert.True(snapshot.PlayerLoaded);
        Assert.False(snapshot.IsLoading);
        Assert.Equal(1050, snapshot.Hp);
    }

    [Fact]
    public void IgtPausesDuringLoadingScreens()
    {
        var (source, _, setNow) = Controlled();

        // The first loading screen runs 1000ms -> 3500ms, so IGT should stall at 1000.
        setNow(1_000);
        var beforeLoad = source.TakeSnapshot().IgtMs;

        setNow(3_500);
        var afterLoad = source.TakeSnapshot().IgtMs;

        Assert.Equal(1_000, beforeLoad);
        Assert.Equal(1_000, afterLoad);

        // ...and resumes afterwards.
        setNow(5_500);
        Assert.Equal(3_000, source.TakeSnapshot().IgtMs);
    }

    [Fact]
    public void BossFlagsBecomeSetOnlyAfterTheirKeyframe()
    {
        var (source, _, setNow) = Controlled();

        setNow(30_000);
        Assert.False(source.Flags.IsEventFlagSet(14000800));

        setNow(31_500);
        Assert.True(source.Flags.IsEventFlagSet(14000800));
        Assert.False(source.Flags.IsEventFlagSet(13000800));
    }

    [Fact]
    public void GenerationAdvancesWhenTheScriptLoops()
    {
        var (source, _, setNow) = Controlled();

        setNow(50_000);
        Assert.Equal(0, source.Generation);

        setNow(FakeSnapshotSource.DemoRunLoopMs + 1_000);
        Assert.Equal(1, source.Generation);

        // A new pass is a fresh run, so flags from the previous pass are gone.
        Assert.False(source.Flags.IsEventFlagSet(14000800));
    }

    // --- what the tracker makes of it ---

    [Fact]
    public void DemoRunProducesTheDocumentedTotals()
    {
        var tracker = RunDemo(untilMs: 105_000);

        Assert.Equal(RunPhase.Finished, tracker.Phase);
        Assert.Equal(10, tracker.TotalDamage);
        Assert.Equal(9, tracker.TotalHits);
        Assert.Equal(1, tracker.TotalDeaths);
        Assert.All(tracker.Splits, s => Assert.True(s.Completed));
    }

    [Fact]
    public void DemoRunIncludesAFall_SoNoDamageAndNoHitDisagree()
    {
        var tracker = RunDemo(untilMs: 105_000);

        // The script drops the player down a shaft on the way to Greatwood. It
        // is there so the difference between the two challenges is visible with
        // --fake, without needing the game to reproduce it.
        Assert.Equal(1, tracker.TotalFallDamage);
        Assert.Equal(1, tracker.Splits[2].Approach.FallDamage);
        Assert.Equal(2, tracker.Splits[2].Approach.Damage);
        Assert.Equal(1, tracker.Splits[2].Approach.Hits);
    }

    [Fact]
    public void DemoRunAttributesHitsToApproachAndBossSegments()
    {
        var tracker = RunDemo(untilMs: 105_000);

        // Split 1: one hit on the way in, two during the fight.
        Assert.Equal(1, tracker.Splits[0].Approach.Hits);
        Assert.Equal(2, tracker.Splits[0].Boss.Hits);

        // Split 2: one on approach, then three in the fight across a death and retry.
        Assert.Equal(1, tracker.Splits[1].Approach.Hits);
        Assert.Equal(3, tracker.Splits[1].Boss.Hits);
        Assert.Equal(1, tracker.Splits[1].Boss.Deaths);

        // Split 3: one each, no deaths.
        Assert.Equal(1, tracker.Splits[2].Approach.Hits);
        Assert.Equal(1, tracker.Splits[2].Boss.Hits);
        Assert.Equal(0, tracker.Splits[2].Deaths);
    }

    [Fact]
    public void DemoRunAdvancesSplitsOnBossDefeatFlags()
    {
        // Just after Iudex's flag, the tracker should be on the second split.
        Assert.Equal(1, RunDemo(untilMs: 33_000).ActiveIndex);

        // Just after Vordt's, the third.
        Assert.Equal(2, RunDemo(untilMs: 79_000).ActiveIndex);
    }
}
