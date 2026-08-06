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
        public void RecordSplit(string routeName, SplitRecord split) { }
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

    /// <summary>Hold a reading long enough to clear the controller's settle window.</summary>
    private static void Settle(RunController c, GameSnapshot s, IFlagSource flags)
    {
        for (var i = 0; i < 25; i++) c.Tick(s, flags, 0);
    }

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
    public void RestoringWritesBackOnlyTheMissingBuiltIns()
    {
        var store = new RouteStore(RoutesDir);
        var before = store.All.Count;

        foreach (var f in Directory.GetFiles(RoutesDir, "*.json"))
            if (File.ReadAllText(f).Contains(BuiltInRoutes.Quick.Name)) File.Delete(f);
        store.Reload();
        Assert.Null(store.Find(BuiltInRoutes.Quick.Name));

        Assert.Equal(1, store.RestoreBuiltIns());
        Assert.NotNull(store.Find(BuiltInRoutes.Quick.Name));
        Assert.Equal(before, store.All.Count);

        // Nothing missing the second time.
        Assert.Equal(0, store.RestoreBuiltIns());
    }

    [Fact]
    public void RestoringLeavesEditedRoutesAlone()
    {
        var store = new RouteStore(RoutesDir);
        var path = Directory.GetFiles(RoutesDir, "*.json")[0];
        var edited = File.ReadAllText(path).Replace("\"isBoss\": true", "\"isBoss\": false");
        File.WriteAllText(path, edited);

        store.RestoreBuiltIns();

        Assert.Equal(edited, File.ReadAllText(path));
    }

    [Fact]
    public void TheQuickRouteIsTheShorterPathToTheKiln()
    {
        var quick = BuiltInRoutes.Quick;

        Assert.Equal(13, quick.Splits.Count);
        Assert.Equal("Iudex Gundyr", quick.Splits[0].Name);
        Assert.Equal("Soul of Cinder", quick.Splits[^1].Name);
        Assert.True(quick.Splits.Count < BuiltInRoutes.AllBosses.Splits.Count);
        Assert.Equal(quick.Splits.Count, quick.AutoSplitCount);
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
    public void EveryBuiltInSplitCanAutoAdvance()
    {
        foreach (var route in BuiltInRoutes.All)
        {
            Assert.Equal(route.Splits.Count, route.AutoSplitCount);

            // Sourced, but not yet seen flipping on a live game.
            Assert.False(route.FlagsVerified);
        }
    }

    [Fact]
    public void TheDlcRouteExtendsTheMainGameAndStillEndsAtTheKiln()
    {
        var main = BuiltInRoutes.AllBosses.Splits;
        var dlc = BuiltInRoutes.AllBossesWithDlc.Splits;

        Assert.True(dlc.Count > main.Count);
        Assert.Equal(main[^1].Name, dlc[^1].Name);      // Soul of Cinder last in both
        Assert.Equal(main[0].Name, dlc[0].Name);
    }

    [Fact]
    public void BossFlagIdsAreUnique()
    {
        // A duplicated id would silently split the wrong boss.
        foreach (var route in BuiltInRoutes.All)
        {
            var ids = route.Splits.Where(s => s.DefeatFlagId is not null).Select(s => s.DefeatFlagId!.Value);
            Assert.Equal(route.Splits.Count, ids.Distinct().Count());
        }
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
        Assert.Equal("Hits", noHit.SplitMetric);
        Assert.False(noHit.ShowDeaths);
        Assert.False(noHit.ShowSegmentBreakdown);

        // Deathless ranks by deaths, so that is what each split must show -
        // showing hits there would compare the wrong thing entirely.
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.Deathless);
        Assert.Equal("Deaths", c.Project(GameSnapshot.Detached).Display.SplitMetric);

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.AllBosses);
        var allBosses = c.Project(GameSnapshot.Detached).Display;
        Assert.Equal("Time", allBosses.SplitMetric);
        Assert.True(allBosses.ShowDeaths);
        Assert.True(allBosses.ShowSegmentBreakdown);
    }

    [Fact]
    public void EverySplitCarriesAllThreePersonalBestsRegardlessOfProfile()
    {
        var c = NewController();
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Settle(c, Play(600_000, 1000), new NoFlags());

        // The payload shape must not change with the profile, so switching
        // challenge never needs a different client.
        var split = c.Project(Play(600_000, 1000)).Splits[0];
        Assert.Null(split.PbHits);
        Assert.Null(split.PbDeaths);
        Assert.Null(split.PbIgtMs);
    }

    [Fact]
    public void ChangingTheSelectionAbandonsTheRunInProgress()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Settle(c, Play(600_000, 1000), flags);
        c.Tick(Play(601_000, 900), flags, 0);      // a hit on the old route
        Assert.Equal(1, c.Project(Play(601_000, 900)).TotalHits);

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
        Settle(c, Play(600_000, 1000), flags);
        c.Tick(Play(601_000, 900), flags, 0);

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
