using Microsoft.Extensions.Logging.Abstractions;
using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Routes;
using OverlayMod.Engine.Tracking;
using OverlayMod.Host;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Choosing a route and a challenge is how a user says what they are running.
/// It has to survive a restart, and changing it has to abandon the run in
/// progress rather than carry meaningless numbers across.
/// </summary>
public class RouteSelectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "overlaymod-tests", Guid.NewGuid().ToString("N"));

    private string RoutesDir => Path.Combine(_dir, "routes");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class NoRecords : IRecordStore
    {
        public PersonalBests BestsFor(string routeName) => PersonalBests.Empty;
        public void Record(RunRecord run) { }
    }

    private sealed class NoFlags : IFlagSource
    {
        public bool IsEventFlagSet(uint flagId) => false;
    }

    private static GameSnapshot Play(int igt, int hp) => new()
    {
        Attached = true,
        PlayerLoaded = true,
        IsLoading = false,
        IgtMs = igt,
        Hp = hp,
        MaxHp = 1000,
    };

    private RunController NewController() => new(
        new NoRecords(),
        new RunStateStore(Path.Combine(_dir, "run-state.json")),
        new RouteStore(RoutesDir),
        new SettingsStore(Path.Combine(_dir, "settings.json")),
        NullLogger<RunController>.Instance);

    // --- the store ---

    [Fact]
    public void BuiltInRoutesAreWrittenToDiskOnFirstRun()
    {
        var store = new RouteStore(RoutesDir);

        Assert.NotEmpty(Directory.GetFiles(RoutesDir, "*.json"));
        Assert.NotNull(store.Find(BuiltInRoutes.Demo.Name));
        Assert.NotNull(store.Find(BuiltInRoutes.AllBosses.Name));
    }

    [Fact]
    public void SeedingDoesNotOverwriteAnEditedRouteFile()
    {
        new RouteStore(RoutesDir);

        // Stand in for the user editing a route by hand.
        var path = Directory.GetFiles(RoutesDir, "*.json")[0];
        var edited = File.ReadAllText(path).Replace("\"isBoss\": true", "\"isBoss\": false");
        File.WriteAllText(path, edited);

        new RouteStore(RoutesDir);

        Assert.Equal(edited, File.ReadAllText(path));
    }

    [Fact]
    public void ADeletedBuiltInRouteStaysDeleted()
    {
        new RouteStore(RoutesDir);
        foreach (var f in Directory.GetFiles(RoutesDir, "*.json"))
            if (File.ReadAllText(f).Contains(BuiltInRoutes.AllBosses.Name)) File.Delete(f);

        // Re-seeding per missing file would resurrect it, leaving no way to
        // remove a route you do not want.
        Assert.Null(new RouteStore(RoutesDir).Find(BuiltInRoutes.AllBosses.Name));
    }

    [Fact]
    public void AnEmptyRoutesDirectoryIsSeededAgain()
    {
        new RouteStore(RoutesDir);
        foreach (var f in Directory.GetFiles(RoutesDir, "*.json")) File.Delete(f);

        Assert.NotEmpty(new RouteStore(RoutesDir).All);
    }

    [Fact]
    public void AMalformedRouteFileIsSkippedRatherThanLosingTheOthers()
    {
        new RouteStore(RoutesDir);
        var before = new RouteStore(RoutesDir).All.Count;

        File.WriteAllText(Path.Combine(RoutesDir, "broken.json"), "{ not json at all");

        Assert.Equal(before, new RouteStore(RoutesDir).All.Count);
    }

    [Fact]
    public void AutoSplitCountReflectsHowManySplitsHaveKnownFlags()
    {
        // The demo route's ids match the fake script, so all three auto-advance.
        Assert.Equal(3, BuiltInRoutes.Demo.AutoSplitCount);

        // The main-game route mostly does not, which is why it warns.
        Assert.False(BuiltInRoutes.AllBosses.FlagsVerified);
        Assert.True(BuiltInRoutes.AllBosses.AutoSplitCount < BuiltInRoutes.AllBosses.Splits.Count);
    }

    // --- selection ---

    [Fact]
    public void SelectingARouteChangesWhatTheOverlayReports()
    {
        var c = NewController();

        Assert.True(c.Select(BuiltInRoutes.AllBosses.Name, ChallengeType.NoHit));

        var state = c.Project(GameSnapshot.Detached);
        Assert.Equal(BuiltInRoutes.AllBosses.Name, state.RouteName);
        Assert.Equal("No-Hit", state.ProfileName);
    }

    [Fact]
    public void SelectingAnUnknownRouteIsRejectedAndChangesNothing()
    {
        var c = NewController();
        var before = c.Project(GameSnapshot.Detached).RouteName;

        Assert.False(c.Select("no such route", ChallengeType.NoHit));
        Assert.Equal(before, c.Project(GameSnapshot.Detached).RouteName);
    }

    [Fact]
    public void TheChallengeDecidesWhatTheOverlayShows()
    {
        var c = NewController();

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        var noHit = c.Project(GameSnapshot.Detached).Display;
        Assert.False(noHit.ShowDeaths);
        Assert.False(noHit.ShowSplitTimes);

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.AllBosses);
        var allBosses = c.Project(GameSnapshot.Detached).Display;
        Assert.True(allBosses.ShowDeaths);
        Assert.True(allBosses.ShowSplitTimes);
    }

    [Fact]
    public void ChangingTheSelectionAbandonsTheRunInProgress()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        c.Tick(Play(10_000, 1000), flags, 0);
        c.Tick(Play(11_000, 900), flags, 0);       // a hit on the old route
        Assert.Equal(1, c.Project(Play(11_000, 900)).TotalHits);

        c.Select(BuiltInRoutes.AllBosses.Name, ChallengeType.NoHit);

        var state = c.Project(GameSnapshot.Detached);
        Assert.Equal("NotStarted", state.Phase);
        Assert.Equal(0, state.TotalHits);
    }

    [Fact]
    public void SwitchingChallengeOnTheSameRouteAlsoAbandonsTheRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        c.Tick(Play(10_000, 1000), flags, 0);
        c.Tick(Play(11_000, 900), flags, 0);

        // The thing being measured changed, so the numbers so far mean nothing.
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.AnyPercent);

        Assert.Equal(0, c.Project(GameSnapshot.Detached).TotalHits);
    }

    [Fact]
    public void TheSelectionIsRememberedByTheNextSession()
    {
        var first = NewController();
        first.Select(BuiltInRoutes.AllBosses.Name, ChallengeType.Deathless);

        var second = NewController();
        var state = second.Project(GameSnapshot.Detached);

        Assert.Equal(BuiltInRoutes.AllBosses.Name, state.RouteName);
        Assert.Equal("Deathless", state.ProfileName);
    }

    [Fact]
    public void ASelectionPointingAtADeletedRouteFallsBack()
    {
        var first = NewController();
        first.Select(BuiltInRoutes.AllBosses.Name, ChallengeType.NoHit);

        foreach (var f in Directory.GetFiles(RoutesDir, "*.json"))
            if (File.ReadAllText(f).Contains(BuiltInRoutes.AllBosses.Name)) File.Delete(f);

        // Must still come up with something usable rather than failing to start.
        var second = NewController();
        Assert.NotEqual(BuiltInRoutes.AllBosses.Name, second.Project(GameSnapshot.Detached).RouteName);
    }
}
