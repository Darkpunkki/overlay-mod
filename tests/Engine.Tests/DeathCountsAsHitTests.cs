using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Tracking;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Dying is taking a hit, whatever killed you.
///
/// Both damage classifiers exist to set aside damage the player was not really
/// dealt — the ground, a poisoning running its course. Neither reading survives
/// the player dying of it: falling to your death is not the ground doing what
/// the ground does, and a poisoning you never cured is not a tick to be
/// discounted. Under No Hit a run that ends in a corpse must never read as
/// clean, so the fatal event skips both classifiers and counts as a hit.
/// </summary>
public class DeathCountsAsHitTests
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

    /// <summary>A run whose player can be moved, hurt and killed as time passes.</summary>
    private sealed class Sim
    {
        public readonly RunTracker Tracker = new();
        public int Igt;
        public int Hp = MaxHealth;
        public float Y;

        public Sim(int startingHp = MaxHealth)
        {
            Hp = startingHp;
            Tracker.Start(new Route("test", ChallengeProfile.NoHit, new[] { new RouteSplit("A", false) }),
                Play(0, Hp, 0));
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
            Hp = Math.Max(0, Hp - amount);
            Tracker.Update(Play(Igt, Hp, Y));
        }

        /// <summary>Drop to zero and lie there long enough for the death to be confirmed.</summary>
        public void Die()
        {
            Hp = 0;
            Tracker.Update(Play(Igt, Hp, Y));
            Step(1_000);
        }

        public DamageEvent LastDamage => Tracker.RecentDamage[^1];
    }

    [Fact]
    public void AnOrdinaryDeath_IsOneHit()
    {
        var sim = new Sim();
        sim.Step(500);
        sim.Die();

        Assert.Equal(1, sim.Tracker.TotalDeaths);
        Assert.Equal(1, sim.Tracker.TotalHits);
        Assert.Equal(1, sim.Tracker.TotalDamage);
    }

    [Fact]
    public void LyingDeadForAWhile_StillCountsOnce()
    {
        var sim = new Sim();
        sim.Step(500);
        sim.Die();
        sim.Step(20_000);

        Assert.Equal(1, sim.Tracker.TotalDeaths);
        Assert.Equal(1, sim.Tracker.TotalHits);
    }

    [Fact]
    public void FallingToYourDeath_IsAHitRatherThanFallDamage()
    {
        var sim = new Sim();
        sim.Step(500);
        sim.Step(1_500, metresPerSecond: -25);   // a drop nothing survives
        sim.Die();

        Assert.Equal(1, sim.Tracker.TotalDeaths);
        Assert.Equal(0, sim.Tracker.TotalFallDamage);
        Assert.Equal(1, sim.Tracker.TotalHits);

        // The descent is still measured, so a fatal fall can be read back on the
        // control page like any other event — it is the attribution that changes.
        Assert.True(sim.LastDamage.DescentMetres > 10);
        Assert.True(sim.LastDamage.Fatal);
        Assert.Equal(DamageKind.Hit, sim.LastDamage.Kind);
        Assert.False(sim.LastDamage.CountedAsFall);
    }

    [Fact]
    public void ASurvivableFall_IsStillNotAHit()
    {
        // The contrast that makes the test above mean something: the ground is
        // still the ground right up until it kills you.
        var sim = new Sim();
        sim.Step(500);
        sim.Step(900, metresPerSecond: -12);
        sim.Lose(150);
        sim.Step(2_000);

        Assert.Equal(0, sim.Tracker.TotalDeaths);
        Assert.Equal(1, sim.Tracker.TotalFallDamage);
        Assert.Equal(0, sim.Tracker.TotalHits);
    }

    [Fact]
    public void DyingOfPoison_BillsOneHitAndLeavesTheTicksAlone()
    {
        // Eight even bites, which is a metronome and therefore poison, and then
        // the one that finishes the job.
        var sim = new Sim(startingHp: 200);
        sim.Step(500);

        for (var i = 0; i < 8; i++)
        {
            sim.Lose(24);
            sim.Step(1_000);
        }

        Assert.Equal(8, sim.Tracker.TotalTickDamage);
        Assert.Equal(0, sim.Tracker.TotalHits);

        sim.Die();

        // The poisoning is still a poisoning. Dying of it is the hit.
        Assert.Equal(8, sim.Tracker.TotalTickDamage);
        Assert.Equal(1, sim.Tracker.TotalDeaths);
        Assert.Equal(1, sim.Tracker.TotalHits);
        Assert.Equal(DamageKind.Hit, sim.LastDamage.Kind);
    }

    [Fact]
    public void DyingToAnEnemy_IsNotCountedTwice()
    {
        // Two blows, the second fatal: two hits, not three.
        var sim = new Sim();
        sim.Step(500);
        sim.Lose(400);
        sim.Step(1_000);
        sim.Die();

        Assert.Equal(2, sim.Tracker.TotalHits);
        Assert.Equal(1, sim.Tracker.TotalDeaths);
    }
}
