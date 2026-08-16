using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Tracking;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// The attempt count. Small, but it is the number a No-Hit runner is asked about
/// most, so losing it or double-counting it is worse than it sounds.
/// </summary>
public class AttemptStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "overlaymod-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "attempts.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AnUncountedRouteStartsAtZero()
    {
        var store = new AttemptStore(Path_);

        Assert.Equal(AttemptCount.None, store.Get("Quick route", ChallengeType.NoHit));
    }

    [Fact]
    public void BeginningAnAttemptCountsIt()
    {
        var store = new AttemptStore(Path_);

        Assert.Equal(1, store.Begin("Quick route", ChallengeType.NoHit).Started);
        Assert.Equal(2, store.Begin("Quick route", ChallengeType.NoHit).Started);
        Assert.Equal(0, store.Get("Quick route", ChallengeType.NoHit).Finished);
    }

    [Fact]
    public void FinishingDoesNotAlsoCountAStart()
    {
        // Every finished run began at Begin. Counting a start here too would
        // credit the same attempt twice.
        var store = new AttemptStore(Path_);
        store.Begin("Quick route", ChallengeType.NoHit);
        var after = store.Finish("Quick route", ChallengeType.NoHit);

        Assert.Equal(1, after.Started);
        Assert.Equal(1, after.Finished);
    }

    [Fact]
    public void ChallengesAreCountedApart()
    {
        // "My 300th No Hit attempt" and "my 4th Speedrun of the same route" are
        // separate tallies; adding them together would describe neither.
        var store = new AttemptStore(Path_);
        store.Begin("Quick route", ChallengeType.NoHit);
        store.Begin("Quick route", ChallengeType.NoHit);
        store.Begin("Quick route", ChallengeType.Speedrun);

        Assert.Equal(2, store.Get("Quick route", ChallengeType.NoHit).Started);
        Assert.Equal(1, store.Get("Quick route", ChallengeType.Speedrun).Started);
    }

    [Fact]
    public void RoutesAreCountedApart()
    {
        var store = new AttemptStore(Path_);
        store.Begin("Quick route", ChallengeType.NoHit);

        Assert.Equal(0, store.Get("All Bosses (main game)", ChallengeType.NoHit).Started);
    }

    [Fact]
    public void TheCountSurvivesARestart()
    {
        var first = new AttemptStore(Path_);
        first.Begin("Quick route", ChallengeType.Deathless);
        first.Begin("Quick route", ChallengeType.Deathless);
        first.Finish("Quick route", ChallengeType.Deathless);

        var second = new AttemptStore(Path_);
        var count = second.Get("Quick route", ChallengeType.Deathless);

        Assert.Equal(2, count.Started);
        Assert.Equal(1, count.Finished);
    }

    [Fact]
    public void TheCountCanBeSetOutright()
    {
        // Nobody starts using this on their first attempt.
        var store = new AttemptStore(Path_);
        store.Set("Quick route", ChallengeType.NoHit, 312, 4);

        var count = new AttemptStore(Path_).Get("Quick route", ChallengeType.NoHit);
        Assert.Equal(312, count.Started);
        Assert.Equal(4, count.Finished);

        // And counting carries on from there rather than from zero.
        Assert.Equal(313, store.Begin("Quick route", ChallengeType.NoHit).Started);
    }

    [Fact]
    public void MoreFinishesThanStartsIsRaisedRatherThanRejected()
    {
        var store = new AttemptStore(Path_);

        // The finished figure is the one that was typed on purpose.
        var count = store.Set("Quick route", ChallengeType.NoHit, 2, 9);
        Assert.Equal(9, count.Started);
        Assert.Equal(9, count.Finished);
    }

    [Fact]
    public void NegativeCountsAreTreatedAsZero()
    {
        var store = new AttemptStore(Path_);

        Assert.Equal(AttemptCount.None, store.Set("Quick route", ChallengeType.NoHit, -5, -2));
    }

    [Fact]
    public void ResettingZeroesOnlyTheRouteAndChallengeAskedFor()
    {
        var store = new AttemptStore(Path_);
        store.Set("Quick route", ChallengeType.NoHit, 40, 1);
        store.Set("Quick route", ChallengeType.Speedrun, 7, 2);

        store.Reset("Quick route", ChallengeType.NoHit);

        Assert.Equal(AttemptCount.None, store.Get("Quick route", ChallengeType.NoHit));
        Assert.Equal(7, store.Get("Quick route", ChallengeType.Speedrun).Started);
    }

    [Fact]
    public void ACorruptFileStartsFromZeroRatherThanRefusingToRun()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, "{ not json at all");

        Assert.Equal(AttemptCount.None, new AttemptStore(Path_).Get("Quick route", ChallengeType.NoHit));
    }

    [Fact]
    public void ARouteNameWithPunctuationInItIsKeptWhole()
    {
        // The store is nested rather than composite-keyed precisely so that a
        // route can be called anything at all.
        var store = new AttemptStore(Path_);
        store.Begin("Glitchless route, Anri | 2nd try", ChallengeType.NoHit);

        Assert.Equal(1, new AttemptStore(Path_).Get("Glitchless route, Anri | 2nd try", ChallengeType.NoHit).Started);
    }
}
