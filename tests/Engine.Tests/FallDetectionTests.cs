using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Tracking;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Telling the ground apart from an enemy.
///
/// Written against the tracker with the player's height moving over time, rather
/// than against <see cref="FallDetector"/> in isolation, because the thing that
/// was wrong was never the measurement — it was how far back the measurement
/// could see. That only shows up when a fall is played out at the speed a real
/// one happens at.
/// </summary>
public class FallDetectionTests
{
    private const int MaxHealth = 1000;

    private static GameSnapshot Play(int igt, int hp, float y) => new()
    {
        Attached = true,
        PlayerLoaded = true,
        IsLoading = false,
        IgtMs = igt,
        Hp = hp,
        MaxHp = MaxHealth,
        Y = y,
    };

    private static Route RouteOf() =>
        new("test", ChallengeProfile.NoHit, new[] { new RouteSplit("A", false) });

    /// <summary>A run whose player can be moved up and down as time passes.</summary>
    private sealed class Sim
    {
        public readonly RunTracker Tracker = new();
        public int Igt;
        public int Hp = MaxHealth;
        public float Y;

        public Sim()
        {
            Tracker.Start(RouteOf(), Play(0, Hp, 0));
            Tracker.Update(Play(0, Hp, 0));
        }

        /// <summary>Let time pass, moving vertically at <paramref name="metresPerSecond"/> (negative is down).</summary>
        public void Step(int ms, float metresPerSecond = 0)
        {
            var until = Igt + ms;
            while (Igt < until)
            {
                var dt = Math.Min(33, until - Igt);   // the real poll rate
                Igt += dt;
                Y += metresPerSecond * dt / 1000f;
                Tracker.Update(Play(Igt, Hp, Y));
            }
        }

        public void Lose(int amount)
        {
            Hp -= amount;
            Tracker.Update(Play(Igt, Hp, Y));
        }

        public double LastDescent => Tracker.RecentDamage[^1].DescentMetres;
    }

    [Fact]
    public void ASingleLongDrop_IsAFall()
    {
        var sim = new Sim();
        sim.Step(500);
        sim.Step(900, metresPerSecond: -12);
        sim.Lose(150);
        sim.Step(2_000);

        Assert.Equal(1, sim.Tracker.TotalFallDamage);
        Assert.Equal(0, sim.Tracker.TotalHits);
    }

    [Fact]
    public void ADropOntoALedgeAndAnImmediateSlideOff_IsOneFall()
    {
        var sim = new Sim();
        sim.Step(500);
        sim.Step(700, metresPerSecond: -12);   // down to the ledge
        sim.Step(150);                          // touch down, briefly
        sim.Step(900, metresPerSecond: -4);     // slide off it, slower
        sim.Lose(150);                          // the game bills the whole descent
        sim.Step(2_000);

        // The reported bug. A window that only reaches back half a second sees
        // the slide and not the drop that set it up, calls the descent small,
        // and charges a hit for landing.
        Assert.Equal(1, sim.Tracker.TotalFallDamage);
        Assert.Equal(0, sim.Tracker.TotalHits);
    }

    [Fact]
    public void TheDescentIsReportedFromWhereTheFallBegan()
    {
        var sim = new Sim();
        sim.Step(500);
        sim.Step(700, metresPerSecond: -12);
        sim.Step(150);
        sim.Step(900, metresPerSecond: -4);
        sim.Lose(150);

        // Roughly 8.4 m of drop plus 3.6 m of slide. Measured over a window it
        // read under two metres, which is not merely a failed verdict — it is a
        // wrong number in the damage log the thresholds get tuned against.
        Assert.True(sim.LastDescent > 11, $"descent read as {sim.LastDescent:0.0} m");
    }

    // --- what must not be written off as a fall ---

    [Fact]
    public void WalkingDownALongSlope_IsNotAFall()
    {
        var sim = new Sim();
        sim.Step(500);
        sim.Step(6_000, metresPerSecond: -2);   // twelve metres, on foot
        sim.Lose(150);
        sim.Step(2_000);

        // Height alone is not the test and never was: no fall is this slow, and
        // a descent that never looked like one must not arm anything.
        Assert.Equal(0, sim.Tracker.TotalFallDamage);
        Assert.Equal(1, sim.Tracker.TotalHits);
    }

    [Fact]
    public void AFallFollowedByALongWalkDownhill_StopsBeingTheFall()
    {
        var sim = new Sim();
        sim.Step(500);
        sim.Step(700, metresPerSecond: -12);    // a genuine fall...
        sim.Step(5_000, metresPerSecond: -2);   // ...then a long trudge down a slope
        sim.Lose(150);
        sim.Step(2_000);

        // Without a cap on how long one descent may run, the fall would still be
        // arming attribution five seconds later and would write off every hit
        // taken on the way down.
        Assert.Equal(0, sim.Tracker.TotalFallDamage);
        Assert.Equal(1, sim.Tracker.TotalHits);
    }

    [Fact]
    public void LandingAndThenBeingHitMuchLater_IsAHit()
    {
        var sim = new Sim();
        sim.Step(500);
        sim.Step(900, metresPerSecond: -12);
        sim.Step(2_000);                        // stood on the ground a while
        sim.Lose(150);
        sim.Step(1_000);

        Assert.Equal(0, sim.Tracker.TotalFallDamage);
        Assert.Equal(1, sim.Tracker.TotalHits);
    }

    [Fact]
    public void StandingStillAndBeingHit_IsAHit()
    {
        var sim = new Sim();
        sim.Step(2_000);
        sim.Lose(150);
        sim.Step(1_000);

        Assert.Equal(0, sim.Tracker.TotalFallDamage);
        Assert.Equal(1, sim.Tracker.TotalHits);
    }
}
