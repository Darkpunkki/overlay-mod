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

    private RunController NewController() => NewController(new RouteStore(RoutesDir));

    private RunController NewController(RouteStore routes) => new(
        new NoRecords(),
        new RunStateStore(Path.Combine(_dir, "run-state.json")),
        routes,
        new SettingsStore(Path.Combine(_dir, "settings.json")),
        new TrackingSettingsStore(Path.Combine(_dir, "tracking.json")),
        new AttemptStore(Path.Combine(_dir, "attempts.json")),
        new SplitNameStore(Path.Combine(_dir, "names.json")),
        NullLogger<RunController>.Instance);

    /// <summary>
    /// A controller and the very store it reads from. The editor tests write
    /// routes and then ask the controller what it makes of the change, which only
    /// means anything if both are looking at the same loaded list.
    /// </summary>
    private (RunController Controller, RouteStore Routes) NewPair()
    {
        var routes = new RouteStore(RoutesDir);
        return (NewController(routes), routes);
    }

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
    public void TheGlitchlessAnriRouteRunsInTheOrderItIsMeantTo()
    {
        var route = BuiltInRoutes.GlitchlessAnri;

        Assert.Equal(14, route.Splits.Count);
        Assert.Equal(
            new[]
            {
                "Iudex Gundyr", "Vordt of the Boreal Valley", "Anri of Astora", "Crystal Sage",
                "Deacons of the Deep", "Abyss Watchers", "High Lord Wolnir", "Pontiff Sulyvahn",
                "Aldrich, Devourer of Gods", "Yhorm the Giant", "Dancer of the Boreal Valley",
                "Dragonslayer Armour", "Lothric, Younger Prince", "Soul of Cinder",
            },
            route.Splits.Select(s => s.Name));

        // Anri is not a boss and has no boss flag. The split advances on the
        // event flag the game sets when the straight sword is picked up, which
        // is the moment the player asked to split on.
        Assert.Equal(50006030u, route.Splits[2].DefeatFlagId);
        Assert.Equal(route.Splits.Count, route.AutoSplitCount);
        Assert.Equal(ChallengeType.NoHit, route.DefaultChallenge);
    }

    [Fact]
    public void EveryCatalogueSplitCarriesAFlag()
    {
        // A split picked from the catalogue must auto-advance; that is the whole
        // difference between picking one and typing a name.
        Assert.NotEmpty(BuiltInRoutes.Catalogue);
        Assert.All(BuiltInRoutes.Catalogue, s => Assert.NotNull(s.DefeatFlagId));

        // And it must be able to build any built-in route out of what it offers.
        var known = BuiltInRoutes.Catalogue.Select(s => s.Name).ToHashSet();
        foreach (var route in BuiltInRoutes.All)
        foreach (var split in route.Splits)
            Assert.Contains(split.Name, known);
    }

    // --- the editor ---

    [Fact]
    public void SavingANewRouteWritesItAndItComesBack()
    {
        var store = new RouteStore(RoutesDir);
        var result = store.Save(new RouteFile("My route", ChallengeType.NoHit, new[]
        {
            new RouteSplitFile("Iudex Gundyr", true, 14000800),
            new RouteSplitFile("A checkpoint", false, null),
        }));

        Assert.True(result.Saved);
        Assert.Equal("My route", result.Name);

        var reloaded = new RouteStore(RoutesDir).Find("My route");
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.Splits.Count);
        Assert.Equal(1, reloaded.AutoSplitCount);
    }

    [Fact]
    public void SavingCanReorderSplitsWithoutTouchingAnythingElse()
    {
        var store = new RouteStore(RoutesDir);
        var quick = store.Find(BuiltInRoutes.Quick.Name)!;
        var reversed = quick.Splits.Reverse().ToList();

        Assert.True(store.Save(quick with { Splits = reversed }, replacing: quick.Name).Saved);

        var after = new RouteStore(RoutesDir).Find(BuiltInRoutes.Quick.Name)!;
        Assert.Equal(reversed.Select(s => s.Name), after.Splits.Select(s => s.Name));
        Assert.Single(Directory.GetFiles(RoutesDir, "quick-route.json"));
    }

    [Fact]
    public void RenamingLeavesNoFileBehindUnderTheOldName()
    {
        var store = new RouteStore(RoutesDir);
        var quick = store.Find(BuiltInRoutes.Quick.Name)!;

        Assert.True(store.Save(quick with { Name = "My quick route" }, replacing: quick.Name).Saved);

        var reloaded = new RouteStore(RoutesDir);
        Assert.NotNull(reloaded.Find("My quick route"));
        Assert.Null(reloaded.Find(BuiltInRoutes.Quick.Name));
    }

    [Fact]
    public void ANewRouteCannotTakeANameAlreadyInUse()
    {
        var store = new RouteStore(RoutesDir);
        var result = store.Save(new RouteFile(
            BuiltInRoutes.Quick.Name, ChallengeType.NoHit, new[] { new RouteSplitFile("A", false, null) }));

        Assert.False(result.Saved);
        Assert.NotNull(result.Error);

        // And the route it collided with is untouched.
        Assert.Equal(BuiltInRoutes.Quick.Splits.Count, store.Find(BuiltInRoutes.Quick.Name)!.Splits.Count);
    }

    [Fact]
    public void RenamingOntoAnExistingNameIsRefused()
    {
        var store = new RouteStore(RoutesDir);
        var quick = store.Find(BuiltInRoutes.Quick.Name)!;

        var result = store.Save(quick with { Name = BuiltInRoutes.Demo.Name }, replacing: quick.Name);

        Assert.False(result.Saved);
        Assert.NotNull(store.Find(BuiltInRoutes.Quick.Name));
        Assert.Equal(BuiltInRoutes.Demo.Splits.Count, store.Find(BuiltInRoutes.Demo.Name)!.Splits.Count);
    }

    [Fact]
    public void ARouteWithNoUsableSplitsIsRefused()
    {
        var store = new RouteStore(RoutesDir);

        Assert.False(store.Save(new RouteFile("Empty", ChallengeType.NoHit, Array.Empty<RouteSplitFile>())).Saved);
        Assert.False(store.Save(new RouteFile("Blank splits", ChallengeType.NoHit, new[]
        {
            new RouteSplitFile("   ", false, null),
        })).Saved);
    }

    [Fact]
    public void ARouteWithNoUsableNameIsRefusedRatherThanWrittenSomewhereOdd()
    {
        var store = new RouteStore(RoutesDir);
        var splits = new[] { new RouteSplitFile("Iudex Gundyr", true, 14000800u) };

        Assert.False(store.Save(new RouteFile("   ", ChallengeType.NoHit, splits)).Saved);

        // The file name comes from the route name, so a name that slugs to
        // nothing has nowhere to go — and must not land on "routes/.json".
        Assert.False(store.Save(new RouteFile("///", ChallengeType.NoHit, splits)).Saved);
        Assert.False(File.Exists(Path.Combine(RoutesDir, ".json")));
    }

    [Fact]
    public void NamesAreTrimmedAndBounded()
    {
        var store = new RouteStore(RoutesDir);
        store.Save(new RouteFile("  Padded  ", ChallengeType.NoHit, new[]
        {
            new RouteSplitFile("  Iudex Gundyr  ", true, 14000800u),
            new RouteSplitFile(new string('x', 500), false, null),
        }));

        var saved = new RouteStore(RoutesDir).Find("Padded");
        Assert.NotNull(saved);
        Assert.Equal("Iudex Gundyr", saved!.Splits[0].Name);
        Assert.Equal(60, saved.Splits[1].Name.Length);
    }

    [Fact]
    public void ARouteSavedFromTheEditorIsNeverMarkedVerified()
    {
        // Only a live game can earn that, and the editor is not a live game.
        var store = new RouteStore(RoutesDir);
        store.Save(new RouteFile("Claimed", ChallengeType.NoHit, new[]
        {
            new RouteSplitFile("Iudex Gundyr", true, 14000800u),
        })
        { FlagsVerified = true });

        Assert.False(new RouteStore(RoutesDir).Find("Claimed")!.FlagsVerified);
    }

    [Fact]
    public void DeletingRemovesTheFileAndTheRoute()
    {
        var store = new RouteStore(RoutesDir);

        Assert.True(store.Delete(BuiltInRoutes.Quick.Name));
        Assert.Null(store.Find(BuiltInRoutes.Quick.Name));
        Assert.Null(new RouteStore(RoutesDir).Find(BuiltInRoutes.Quick.Name));

        // Nothing to delete twice.
        Assert.False(store.Delete(BuiltInRoutes.Quick.Name));
    }

    [Fact]
    public void DeletingFindsTheFileByWhatIsInItRatherThanByItsName()
    {
        // A hand-written route file may be called anything at all.
        Directory.CreateDirectory(RoutesDir);
        var path = Path.Combine(RoutesDir, "zzz-whatever.json");
        File.WriteAllText(path, """
            {
              "name": "Hand written",
              "defaultChallenge": "NoHit",
              "splits": [ { "name": "Iudex Gundyr", "isBoss": true, "defeatFlagId": 14000800 } ]
            }
            """);

        var store = new RouteStore(RoutesDir);
        Assert.True(store.Delete("Hand written"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void EditingAHandWrittenRouteKeepsItInItsOwnFile()
    {
        Directory.CreateDirectory(RoutesDir);
        var path = Path.Combine(RoutesDir, "zzz-whatever.json");
        File.WriteAllText(path, """
            {
              "name": "Hand written",
              "defaultChallenge": "NoHit",
              "splits": [ { "name": "Iudex Gundyr", "isBoss": true, "defeatFlagId": 14000800 } ]
            }
            """);

        var store = new RouteStore(RoutesDir);
        var route = store.Find("Hand written")!;
        store.Save(route with { Splits = route.Splits.Append(new RouteSplitFile("Vordt", true, 13000800u)).ToList() },
            replacing: "Hand written");

        // Written back where it was, not duplicated under a slug.
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(Path.Combine(RoutesDir, "hand-written.json")));
        Assert.Equal(2, new RouteStore(RoutesDir).Find("Hand written")!.Splits.Count);
    }

    [Fact]
    public void EditingTheSelectedRouteAbandonsTheRunInProgress()
    {
        var (c, store) = NewPair();
        var flags = new NoFlags();

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Settle(c, Play(600_000, 1000), flags);
        c.Tick(Play(601_000, 900), flags, 0);
        Assert.Equal(1, c.Project(Play(601_000, 900)).TotalDamage);

        var demo = store.Find(BuiltInRoutes.Demo.Name)!;
        store.Save(demo with { Splits = demo.Splits.Take(2).ToList() }, replacing: demo.Name);
        c.RoutesChanged(BuiltInRoutes.Demo.Name);

        Assert.Equal("NotStarted", c.Project(GameSnapshot.Detached).Phase);

        // And the next attempt runs the route as it is now.
        Settle(c, Play(700_000, 1000), flags);
        var state = c.Project(Play(700_000, 1000));
        Assert.Equal(2, state.Splits.Count);
        Assert.Equal(0, state.TotalDamage);
    }

    [Fact]
    public void ReloadingAfterAnUnrelatedEditKeepsTheRunGoing()
    {
        // Route files are reloaded wholesale, so every save produces new objects
        // even for routes nobody touched. Abandoning the run on all of them would
        // mean saving some other route cost you your attempt.
        var (c, store) = NewPair();
        var flags = new NoFlags();

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Settle(c, Play(600_000, 1000), flags);
        c.Tick(Play(601_000, 900), flags, 0);

        var quick = store.Find(BuiltInRoutes.Quick.Name)!;
        store.Save(quick with { Splits = quick.Splits.Take(3).ToList() }, replacing: quick.Name);
        c.RoutesChanged();

        var state = c.Project(GameSnapshot.Detached);
        Assert.Equal("Running", state.Phase);
        Assert.Equal(1, state.TotalDamage);
    }

    [Fact]
    public void TheSelectionFollowsARenameRatherThanFallingBack()
    {
        var (c, store) = NewPair();
        c.Select(BuiltInRoutes.Quick.Name, ChallengeType.NoHit);

        var quick = store.Find(BuiltInRoutes.Quick.Name)!;
        store.Save(quick with { Name = "My quick route" }, replacing: quick.Name);
        c.RoutesChanged("My quick route");

        Assert.Equal("My quick route", c.Project(GameSnapshot.Detached).RouteName);
    }

    [Fact]
    public void DeletingTheSelectedRouteFallsBackToSomethingUsable()
    {
        var (c, store) = NewPair();
        c.Select(BuiltInRoutes.Quick.Name, ChallengeType.NoHit);

        store.Delete(BuiltInRoutes.Quick.Name);
        c.RoutesChanged();

        var name = c.Project(GameSnapshot.Detached).RouteName;
        Assert.NotEqual(BuiltInRoutes.Quick.Name, name);
        Assert.NotEmpty(name);
    }

    // --- attempts ---

    [Fact]
    public void EachRunCountsAsOneAttempt()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Assert.Equal(0, c.Attempts.Started);

        Settle(c, Play(600_000, 1000), flags);
        Assert.Equal(1, c.Attempts.Started);
        Assert.Equal(1, c.Project(Play(600_000, 1000)).Attempts.Started);

        // Playing on is not another attempt.
        for (var i = 0; i < 50; i++) c.Tick(Play(600_000 + i * 100, 1000), flags, 0);
        Assert.Equal(1, c.Attempts.Started);

        // Starting over is.
        c.Reset();
        Settle(c, Play(1_000, 1000), flags);
        Assert.Equal(2, c.Attempts.Started);
    }

    [Fact]
    public void AttemptsAreCountedPerChallenge()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Settle(c, Play(600_000, 1000), flags);

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.Speedrun);
        Assert.Equal(0, c.Attempts.Started);

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Assert.Equal(1, c.Attempts.Started);
    }

    [Fact]
    public void TheAttemptCountSurvivesTheSession()
    {
        var first = NewController();
        first.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Settle(first, Play(600_000, 1000), new NoFlags());

        Assert.Equal(1, NewController().Attempts.Started);
    }

    [Fact]
    public void TheAttemptCountCanBeSetByHand()
    {
        var c = NewController();
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);

        Assert.Equal(312, c.SetAttempts(312, 4).Started);
        Assert.Equal(312, c.Project(GameSnapshot.Detached).Attempts.Started);
        Assert.Equal(4, c.Project(GameSnapshot.Detached).Attempts.Finished);
    }

    // --- display names ---

    [Fact]
    public void ARenamedSplitCarriesALabelAndKeepsItsName()
    {
        Directory.CreateDirectory(_dir);
        var names = new SplitNameStore(Path.Combine(_dir, "names.json"));
        names.Update(new Dictionary<string, string> { ["Iudex Gundyr"] = "Gundyr" });

        var c = NewController();
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Settle(c, Play(600_000, 1000), new NoFlags());

        var splits = c.Project(Play(600_000, 1000)).Splits;

        // The name is what everything else is filed under - the personal bests
        // above all - so it stays canonical and the label sits beside it.
        Assert.Equal("Iudex Gundyr", splits[0].Name);
        Assert.Equal("Gundyr", splits[0].Label);

        // Nothing else is renamed, and an unrenamed split carries no label at all
        // rather than a copy of its own name.
        Assert.Null(splits[1].Label);
    }

    [Fact]
    public void AMalformedRouteFileIsSkippedRatherThanLosingTheOthers()
    {
        new RouteStore(RoutesDir);
        var before = new RouteStore(RoutesDir).All.Count;

        File.WriteAllText(Path.Combine(RoutesDir, "broken.json"), "{ not json at all");

        Assert.Equal(before, new RouteStore(RoutesDir).All.Count);
    }

    // --- challenge names written by 0.1.0 ---

    [Fact]
    public void ARouteNamingARemovedChallengeStillLoads()
    {
        // Any% and All Bosses were removed in 0.2.0, and every install that
        // predates it has route files naming one. A strict parse does not fall
        // back to a default here - it throws, and RouteStore skips the whole
        // file, silently removing the route from the picker.
        Directory.CreateDirectory(RoutesDir);
        File.WriteAllText(Path.Combine(RoutesDir, "legacy.json"), """
            {
              "name": "Legacy All Bosses",
              "defaultChallenge": "AllBosses",
              "splits": [ { "name": "Iudex Gundyr", "isBoss": true, "defeatFlagId": 14000800 } ]
            }
            """);

        var route = new RouteStore(RoutesDir).Find("Legacy All Bosses");

        Assert.NotNull(route);
        Assert.Equal(ChallengeType.Speedrun, route!.DefaultChallenge);
    }

    [Fact]
    public void ARouteNamingAnUnrecognisableChallengeFallsBackRatherThanVanishing()
    {
        Directory.CreateDirectory(RoutesDir);
        File.WriteAllText(Path.Combine(RoutesDir, "typo.json"), """
            {
              "name": "Typo Route",
              "defaultChallenge": "no-such-challenge",
              "splits": [ { "name": "Iudex Gundyr", "isBoss": true, "defeatFlagId": 14000800 } ]
            }
            """);

        var route = new RouteStore(RoutesDir).Find("Typo Route");

        Assert.NotNull(route);
        Assert.Equal(ChallengeType.NoDamage, route!.DefaultChallenge);
    }

    [Fact]
    public void ARememberedSelectionNamingARemovedChallengeIsNotLost()
    {
        Directory.CreateDirectory(_dir);
        new RouteStore(RoutesDir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            $$"""{ "routeName": "{{BuiltInRoutes.Quick.Name}}", "challenge": "AnyPercent" }""");

        var state = NewController().Project(GameSnapshot.Detached);

        // The route survives, and the challenge lands on what Any% was ranked by.
        Assert.Equal(BuiltInRoutes.Quick.Name, state.RouteName);
        Assert.Equal("Speedrun", state.ProfileName);
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
        Assert.Equal("No Hit", state.ProfileName);
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

        // No Damage and No Hit rank on different counters, so they must not
        // report the same split metric - that difference is the whole point.
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoDamage);
        var noDamage = c.Project(GameSnapshot.Detached).Display;
        Assert.Equal("Damage", noDamage.SplitMetric);
        Assert.True(noDamage.ShowTotals);

        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Assert.Equal("Hits", c.Project(GameSnapshot.Detached).Display.SplitMetric);

        // Deathless ranks by deaths, so that is what each split must show -
        // showing hits there would compare the wrong thing entirely.
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.Deathless);
        Assert.Equal("Deaths", c.Project(GameSnapshot.Detached).Display.SplitMetric);

        // Speedrun drops the totals footer: its primary metric is the run
        // timer, which the overlay already shows far larger at the top.
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.Speedrun);
        var speedrun = c.Project(GameSnapshot.Detached).Display;
        Assert.Equal("Time", speedrun.SplitMetric);
        Assert.False(speedrun.ShowTotals);
    }

    [Fact]
    public void EverySplitCarriesEveryPersonalBestRegardlessOfProfile()
    {
        var c = NewController();
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.NoHit);
        Settle(c, Play(600_000, 1000), new NoFlags());

        // The payload shape must not change with the profile, so switching
        // challenge never needs a different client.
        var split = c.Project(Play(600_000, 1000)).Splits[0];
        Assert.Null(split.PbDamage);
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
        Assert.Equal(1, c.Project(Play(601_000, 900)).TotalDamage);

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
        c.Select(BuiltInRoutes.Demo.Name, ChallengeType.Speedrun);

        Assert.Equal(0, c.Project(GameSnapshot.Detached).TotalDamage);
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
