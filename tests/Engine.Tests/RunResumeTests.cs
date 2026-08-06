using Microsoft.Extensions.Logging.Abstractions;
using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Tracking;
using OverlayMod.Host;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Quitting the game to take a break must not destroy a run. Dark Souls III
/// keeps in-game time in the save, so returning with IGT at or ahead of where we
/// left off means the same character carrying on.
/// </summary>
public class RunResumeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "overlaymod-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // --- helpers ---

    private static GameSnapshot Play(int igt, int hp) => new()
    {
        Attached = true,
        PlayerLoaded = true,
        IsLoading = false,
        IgtMs = igt,
        Hp = hp,
        MaxHp = 1000,
    };

    private static GameSnapshot Menu(int igt) => new()
    {
        Attached = true,
        PlayerLoaded = false,
        IsLoading = false,
        IgtMs = igt,
    };

    private static GameSnapshot Loading(int igt) => new()
    {
        Attached = true,
        PlayerLoaded = false,
        IsLoading = true,
        IgtMs = igt,
    };

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

    private RunController NewController() => new(
        new NoRecords(),
        new RunStateStore(Path.Combine(_dir, "run-state.json")),
        new RouteStore(Path.Combine(_dir, "routes")),
        new SettingsStore(Path.Combine(_dir, "settings.json")),
        NullLogger<RunController>.Instance);

    private static int HitsOf(RunController c, GameSnapshot s) => c.Project(s).TotalHits;

    // --- starting ---

    [Fact]
    public void RunDoesNotStartWhileSittingInMenus()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Menu(0), flags, generation: 0);
        c.Tick(Menu(0), flags, generation: 0);

        Assert.Equal("NotStarted", c.Project(Menu(0)).Phase);
    }

    [Fact]
    public void RunStartsWhenThePlayerLoadsIntoTheWorld()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Menu(0), flags, 0);
        c.Tick(Play(10_000, 1000), flags, 0);

        Assert.Equal("Running", c.Project(Play(10_000, 1000)).Phase);
    }

    // --- resuming ---

    [Fact]
    public void QuittingAndReturningWithLaterIgtResumesTheSameRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Play(10_000, 1000), flags, 0);
        c.Tick(Play(11_000, 900), flags, 0);   // one hit
        Assert.Equal(1, HitsOf(c, Play(11_000, 900)));

        // Player quits to desktop.
        c.Tick(GameSnapshot.Detached, flags, 0);
        c.Tick(GameSnapshot.Detached, flags, 0);

        // ...and comes back later. New process, so a new source generation, and
        // the save resumes at the in-game time it was left at.
        c.Tick(Menu(0), flags, 1);
        c.Tick(Play(11_000, 1000), flags, 1);

        var state = c.Project(Play(11_000, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(1, state.TotalHits);
    }

    [Fact]
    public void TheRunTimerDoesNotAdvanceWhileTheGameIsClosed()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Play(10_000, 1000), flags, 0);
        c.Tick(Play(15_000, 1000), flags, 0);
        var beforeQuit = c.Project(Play(15_000, 1000)).RunIgtMs;

        c.Tick(GameSnapshot.Detached, flags, 0);
        c.Tick(GameSnapshot.Detached, flags, 0);

        // The menu reports whatever it likes; it must not move the run clock.
        c.Tick(Menu(999_999), flags, 1);

        Assert.Equal(beforeQuit, c.Project(GameSnapshot.Detached).RunIgtMs);
    }

    [Fact]
    public void ReturningWithEarlierIgtStartsAFreshRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Play(50_000, 1000), flags, 0);
        c.Tick(Play(51_000, 900), flags, 0);   // one hit
        Assert.Equal(1, HitsOf(c, Play(51_000, 900)));

        c.Tick(GameSnapshot.Detached, flags, 0);

        // A different character: its save is far earlier in in-game time.
        c.Tick(Play(500, 1000), flags, 1);

        var state = c.Project(Play(500, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(0, state.TotalHits);
    }

    [Fact]
    public void ReloadingAtDifferentHealthIsNotCountedAsAHit()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Play(10_000, 1000), flags, 0);
        c.Tick(Play(11_000, 1000), flags, 0);
        Assert.Equal(0, HitsOf(c, Play(11_000, 1000)));

        // Quit at full health, come back on a save with much less.
        c.Tick(GameSnapshot.Detached, flags, 0);
        c.Tick(Menu(0), flags, 1);
        c.Tick(Play(11_000, 400), flags, 1);

        Assert.Equal(0, HitsOf(c, Play(11_000, 400)));
    }

    [Fact]
    public void ReturningAfterAFinishedRunBeginsANewAttempt()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Play(10_000, 1000), flags, 0);
        c.Tick(Play(11_000, 900), flags, 0);   // one hit

        // Finish the run by splitting through every boss.
        for (var i = 0; i < 3; i++) c.Split();
        Assert.Equal("Finished", c.Project(Play(11_000, 900)).Phase);

        // Quit, come back, load in again: this is attempt two, not the old one.
        c.Tick(GameSnapshot.Detached, flags, 0);
        c.Tick(Play(12_000, 1000), flags, 1);

        var state = c.Project(Play(12_000, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(0, state.TotalHits);
    }

    [Fact]
    public void QuittingToTheMenuAndStartingANewCharacterBeginsANewRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Play(50_000, 1000), flags, 0);
        c.Tick(Play(51_000, 900), flags, 0);       // a hit on the old run
        Assert.Equal(1, HitsOf(c, Play(51_000, 900)));

        // Quit to the main menu *without closing the game*: same process, same
        // source generation, so nothing about the connection changes.
        c.Tick(Menu(51_000), flags, 0);
        c.Tick(Menu(51_000), flags, 0);

        // Start a new character - its in-game time begins near zero.
        c.Tick(Play(800, 1000), flags, 0);

        var state = c.Project(Play(800, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(0, state.TotalHits);
    }

    [Fact]
    public void QuittingToTheMenuAndContinuingTheSameCharacterKeepsTheRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Play(50_000, 1000), flags, 0);
        c.Tick(Play(51_000, 900), flags, 0);

        c.Tick(Menu(51_000), flags, 0);

        // Same save, so in-game time picks up where it stopped.
        c.Tick(Play(51_000, 1000), flags, 0);

        Assert.Equal(1, HitsOf(c, Play(51_000, 1000)));
    }

    [Fact]
    public void ALoadingScreenDoesNotStartANewRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Play(50_000, 1000), flags, 0);
        c.Tick(Play(51_000, 900), flags, 0);

        // A bonfire warp: out of play, then back, with time moving forward.
        c.Tick(Loading(51_000), flags, 0);
        c.Tick(Loading(51_000), flags, 0);
        c.Tick(Play(51_500, 1000), flags, 0);

        var state = c.Project(Play(51_500, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(1, state.TotalHits);
    }

    // --- surviving a host restart ---

    [Fact]
    public void AnUnfinishedRunIsRecoveredByANewHostProcess()
    {
        var flags = new NoFlags();

        var first = NewController();
        first.Tick(Play(10_000, 1000), flags, 0);
        first.Tick(Play(11_000, 900), flags, 0);   // one hit, checkpointed

        // The overlay is closed and reopened; the game is still where it was.
        var second = NewController();
        second.Tick(Play(11_000, 900), flags, 0);

        var state = second.Project(Play(11_000, 900));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(1, state.TotalHits);
    }

    [Fact]
    public void ResettingClearsTheCheckpointSoNoRunIsRecovered()
    {
        var flags = new NoFlags();

        var first = NewController();
        first.Tick(Play(10_000, 1000), flags, 0);
        first.Tick(Play(11_000, 900), flags, 0);
        first.Reset();

        var second = NewController();
        Assert.Equal("NotStarted", second.Project(GameSnapshot.Detached).Phase);
    }
}
