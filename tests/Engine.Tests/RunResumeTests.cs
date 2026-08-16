using Microsoft.Extensions.Logging.Abstractions;
using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Tracking;
using OverlayMod.Host;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Quitting the game — to the menu or to the desktop — must not destroy a run.
///
/// Dark Souls III keeps in-game time in the save, but writes it periodically
/// rather than continuously, so reloading rewinds the clock to the last save
/// point. The rule therefore cannot be "time must never go backwards": it has to
/// tolerate a rewind while still recognising a different character.
///
/// In-game times here are realistic — a run twenty minutes in — because the
/// distinction between "rewound to the last save" and "a fresh character" only
/// exists at that scale.
/// </summary>
public class RunResumeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "overlaymod-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // --- helpers ---

    private const int TwentyMinutes = 20 * 60 * 1000;

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
        new TrackingSettingsStore(Path.Combine(_dir, "tracking.json")),
        new AttemptStore(Path.Combine(_dir, "attempts.json")),
        new SplitNameStore(Path.Combine(_dir, "names.json")),
        NullLogger<RunController>.Instance);

    /// <summary>
    /// Hold a reading long enough to clear the settle window the controller waits
    /// out after loading in, so it judges a stable value rather than whatever the
    /// game had written on the first frame.
    /// </summary>
    private static void Settle(RunController c, GameSnapshot s, IFlagSource flags, int generation = 0)
    {
        for (var i = 0; i < 25; i++) c.Tick(s, flags, generation);
    }

    private static int HitsOf(RunController c, GameSnapshot s) => c.Project(s).TotalHits;

    // --- starting ---

    [Fact]
    public void RunDoesNotStartWhileSittingInMenus()
    {
        var c = NewController();
        Settle(c, Menu(0), new NoFlags());

        Assert.Equal("NotStarted", c.Project(Menu(0)).Phase);
    }

    [Fact]
    public void RunStartsWhenThePlayerLoadsIntoTheWorld()
    {
        var c = NewController();
        var flags = new NoFlags();

        c.Tick(Menu(0), flags, 0);
        Settle(c, Play(TwentyMinutes, 1000), flags);

        Assert.Equal("Running", c.Project(Play(TwentyMinutes, 1000)).Phase);
    }

    // --- resuming ---

    [Fact]
    public void QuittingToTheMenuAndContinuingTheSameCharacterKeepsTheRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        c.Tick(Play(TwentyMinutes + 1_000, 900), flags, 0);      // a hit
        Assert.Equal(1, HitsOf(c, Play(TwentyMinutes + 1_000, 900)));

        c.Tick(Menu(TwentyMinutes + 1_000), flags, 0);
        Settle(c, Play(TwentyMinutes + 1_000, 1000), flags);

        Assert.Equal(1, HitsOf(c, Play(TwentyMinutes + 1_000, 1000)));
    }

    [Fact]
    public void ASaveRewoundToItsLastSavePointStillResumes()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        c.Tick(Play(TwentyMinutes + 60_000, 900), flags, 0);     // a hit, a minute later
        Assert.Equal(1, HitsOf(c, Play(TwentyMinutes + 60_000, 900)));

        c.Tick(Menu(TwentyMinutes + 60_000), flags, 0);

        // The save had not caught up: reloading rewinds ninety seconds. That is
        // the same character carrying on, not a new run.
        Settle(c, Play(TwentyMinutes - 30_000, 1000), flags);

        var state = c.Project(Play(TwentyMinutes - 30_000, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(1, state.TotalHits);
    }

    [Fact]
    public void StartingANewGameAfterAShortSessionResetsTheCounters()
    {
        var c = NewController();
        var flags = new NoFlags();

        // Two minutes of poking about, then New Game. The old rule asked whether
        // the *previous* run was long, so a short one made the rewind look like
        // an ordinary save-point rewind and the hits carried straight over.
        Settle(c, Play(30_000, 1000), flags);
        c.Tick(Play(31_000, 900), flags, 0);
        Assert.Equal(1, HitsOf(c, Play(31_000, 900)));

        c.Tick(Menu(31_000), flags, 0);
        Settle(c, Play(500, 1000), flags);   // a brand new character

        Assert.Equal(0, HitsOf(c, Play(500, 1000)));
    }

    [Fact]
    public void StartingANewGameAfterALongRunAlsoResets()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        c.Tick(Play(TwentyMinutes + 1_000, 900), flags, 0);
        Assert.Equal(1, HitsOf(c, Play(TwentyMinutes + 1_000, 900)));

        c.Tick(Menu(TwentyMinutes + 1_000), flags, 0);
        Settle(c, Play(2_000, 1000), flags);

        Assert.Equal(0, HitsOf(c, Play(2_000, 1000)));
    }

    [Fact]
    public void AShortRunCarryingOnForwardsIsStillTheSameRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        // The counterpart to the two above: under a minute of in-game time, a
        // clock that has moved *forwards* is the same character continuing.
        Settle(c, Play(20_000, 1000), flags);
        c.Tick(Play(21_000, 900), flags, 0);
        Assert.Equal(1, HitsOf(c, Play(21_000, 900)));

        c.Tick(Menu(21_000), flags, 0);
        Settle(c, Play(25_000, 1000), flags);

        Assert.Equal(1, HitsOf(c, Play(25_000, 1000)));
    }

    [Fact]
    public void QuittingToDesktopAndReturningResumesTheSameRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        c.Tick(Play(TwentyMinutes + 1_000, 900), flags, 0);
        Assert.Equal(1, HitsOf(c, Play(TwentyMinutes + 1_000, 900)));

        c.Tick(GameSnapshot.Detached, flags, 0);
        c.Tick(GameSnapshot.Detached, flags, 0);

        // Relaunched: a new process, so a new source generation.
        c.Tick(Menu(0), flags, 1);
        Settle(c, Play(TwentyMinutes + 1_000, 1000), flags, 1);

        var state = c.Project(Play(TwentyMinutes + 1_000, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(1, state.TotalHits);
    }

    [Fact]
    public void TheRunTimerDoesNotAdvanceWhileTheGameIsClosed()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        c.Tick(Play(TwentyMinutes + 5_000, 1000), flags, 0);
        var beforeQuit = c.Project(Play(TwentyMinutes + 5_000, 1000)).RunIgtMs;

        c.Tick(GameSnapshot.Detached, flags, 0);

        // The menu reports whatever it likes; it must not move the run clock.
        c.Tick(Menu(999_999_999), flags, 1);

        Assert.Equal(beforeQuit, c.Project(GameSnapshot.Detached).RunIgtMs);
    }

    // --- starting over ---

    [Fact]
    public void QuittingToTheMenuAndStartingANewCharacterBeginsANewRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        c.Tick(Play(TwentyMinutes + 1_000, 900), flags, 0);      // a hit on the old run
        Assert.Equal(1, HitsOf(c, Play(TwentyMinutes + 1_000, 900)));

        // Quit to the main menu *without closing the game*: same process, same
        // source generation, so nothing about the connection changes.
        c.Tick(Menu(TwentyMinutes + 1_000), flags, 0);

        // A brand new character - its in-game time starts near zero.
        Settle(c, Play(800, 1000), flags);

        var state = c.Project(Play(800, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(0, state.TotalHits);
    }

    [Fact]
    public void LoadingASaveFarBehindStartsANewRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        c.Tick(Play(TwentyMinutes + 1_000, 900), flags, 0);

        c.Tick(GameSnapshot.Detached, flags, 0);

        // A different character, ten minutes in: far further back than any
        // rewind to a save point could explain.
        Settle(c, Play(10 * 60 * 1000, 1000), flags, 1);

        Assert.Equal(0, HitsOf(c, Play(10 * 60 * 1000, 1000)));
    }

    [Fact]
    public void ALoadingScreenDoesNotStartANewRun()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        c.Tick(Play(TwentyMinutes + 1_000, 900), flags, 0);

        // A bonfire warp: out of play, then back, with time moving forward.
        c.Tick(Loading(TwentyMinutes + 1_000), flags, 0);
        c.Tick(Loading(TwentyMinutes + 1_000), flags, 0);
        Settle(c, Play(TwentyMinutes + 1_500, 1000), flags);

        var state = c.Project(Play(TwentyMinutes + 1_500, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(1, state.TotalHits);
    }

    [Fact]
    public void ReloadingAtDifferentHealthIsNotCountedAsAHit()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        Assert.Equal(0, HitsOf(c, Play(TwentyMinutes, 1000)));

        // Quit at full health, come back on a save with much less.
        c.Tick(Menu(TwentyMinutes), flags, 0);
        Settle(c, Play(TwentyMinutes, 400), flags);

        Assert.Equal(0, HitsOf(c, Play(TwentyMinutes, 400)));
    }

    [Fact]
    public void ReturningAfterAFinishedRunBeginsANewAttempt()
    {
        var c = NewController();
        var flags = new NoFlags();

        Settle(c, Play(TwentyMinutes, 1000), flags);
        c.Tick(Play(TwentyMinutes + 1_000, 900), flags, 0);      // a hit

        for (var i = 0; i < 3; i++) c.Split();
        Assert.Equal("Finished", c.Project(Play(TwentyMinutes + 1_000, 900)).Phase);

        c.Tick(GameSnapshot.Detached, flags, 0);
        Settle(c, Play(TwentyMinutes + 2_000, 1000), flags, 1);

        var state = c.Project(Play(TwentyMinutes + 2_000, 1000));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(0, state.TotalHits);
    }

    // --- surviving a host restart ---

    [Fact]
    public void AnUnfinishedRunIsRecoveredByANewHostProcess()
    {
        var flags = new NoFlags();

        var first = NewController();
        Settle(first, Play(TwentyMinutes, 1000), flags);
        first.Tick(Play(TwentyMinutes + 1_000, 900), flags, 0);   // one hit, checkpointed

        // The overlay is closed and reopened; the game is still where it was.
        var second = NewController();
        Settle(second, Play(TwentyMinutes + 1_000, 900), flags);

        var state = second.Project(Play(TwentyMinutes + 1_000, 900));
        Assert.Equal("Running", state.Phase);
        Assert.Equal(1, state.TotalHits);
    }

    [Fact]
    public void ResettingClearsTheCheckpointSoNoRunIsRecovered()
    {
        var flags = new NoFlags();

        var first = NewController();
        Settle(first, Play(TwentyMinutes, 1000), flags);
        first.Tick(Play(TwentyMinutes + 1_000, 900), flags, 0);
        first.Reset();

        var second = NewController();
        Assert.Equal("NotStarted", second.Project(GameSnapshot.Detached).Phase);
    }
}
