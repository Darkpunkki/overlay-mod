using OverlayMod.Engine.Persistence;
using Xunit;

namespace OverlayMod.Engine.Tests;

/// <summary>
/// Personal bests are what the overlay compares a live run against, so the
/// folding rules have to be right: best is the minimum of each metric, taken
/// per split across all runs rather than only from the single best run.
/// </summary>
public class JsonRecordStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "overlaymod-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "records.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static RunRecord Run(string route, int totalHits, int runMs, params (string Name, int Hits, int Ms)[] splits)
    {
        var list = new List<SplitRecord>();
        foreach (var s in splits) list.Add(new SplitRecord(s.Name, s.Ms, s.Hits, 0));
        return new RunRecord(route, "No-Hit", DateTimeOffset.UnixEpoch, runMs, totalHits, 0, list);
    }

    private static RunRecord DeathRun(string route, params (string Name, int Deaths)[] splits)
    {
        var list = new List<SplitRecord>();
        var total = 0;
        foreach (var s in splits) { list.Add(new SplitRecord(s.Name, 1000, 0, s.Deaths)); total += s.Deaths; }
        return new RunRecord(route, "Deathless", DateTimeOffset.UnixEpoch, 1000, 0, total, list);
    }

    [Fact]
    public void AnUnknownRouteHasNoBests()
    {
        var store = new JsonRecordStore(Path_);
        var bests = store.BestsFor("nothing here");

        Assert.Null(bests.BestTotalHits);
        Assert.Null(bests.SplitHits("Iudex Gundyr"));
    }

    [Fact]
    public void BestsAreTheMinimumAcrossRuns()
    {
        var store = new JsonRecordStore(Path_);
        store.Record(Run("r", totalHits: 5, runMs: 90_000, ("A", 3, 40_000), ("B", 2, 50_000)));
        store.Record(Run("r", totalHits: 7, runMs: 70_000, ("A", 6, 30_000), ("B", 1, 40_000)));

        var bests = store.BestsFor("r");

        Assert.Equal(5, bests.BestTotalHits);      // the better of 5 and 7
        Assert.Equal(70_000, bests.BestRunIgtMs);  // the faster of the two
    }

    [Fact]
    public void PerSplitBestsAreTakenAcrossDifferentRuns()
    {
        var store = new JsonRecordStore(Path_);
        store.Record(Run("r", 5, 90_000, ("A", 3, 40_000), ("B", 2, 50_000)));
        store.Record(Run("r", 7, 70_000, ("A", 6, 30_000), ("B", 1, 40_000)));

        var bests = store.BestsFor("r");

        // A's best came from the first run, B's from the second - no single run
        // achieved both.
        Assert.Equal(3, bests.SplitHits("A"));
        Assert.Equal(1, bests.SplitHits("B"));
        Assert.Equal(30_000, bests.SplitIgtMs("A"));
        Assert.Equal(40_000, bests.SplitIgtMs("B"));
    }

    [Fact]
    public void AZeroLengthSplitDoesNotBecomeAnUnbeatableTimeBest()
    {
        var store = new JsonRecordStore(Path_);
        store.Record(Run("r", 0, 50_000, ("A", 0, 50_000)));
        store.Record(Run("r", 0, 0, ("A", 0, 0)));   // never actually played

        Assert.Equal(50_000, store.BestsFor("r").SplitIgtMs("A"));
    }

    [Fact]
    public void PerSplitDeathBestsAreTrackedToo()
    {
        var store = new JsonRecordStore(Path_);
        store.Record(DeathRun("r", ("A", 3), ("B", 0)));
        store.Record(DeathRun("r", ("A", 1), ("B", 2)));

        var bests = store.BestsFor("r");

        Assert.Equal(1, bests.SplitDeaths("A"));
        Assert.Equal(0, bests.SplitDeaths("B"));
    }

    // --- per-split bests from unfinished attempts ---

    [Fact]
    public void ASplitBestIsEarnedWhenTheBossDies_NotWhenTheRunFinishes()
    {
        var store = new JsonRecordStore(Path_);

        // An attempt that beat Iudex cleanly and was then abandoned.
        store.RecordSplit("r", new SplitRecord("Iudex Gundyr", 30_000, 0, 0));

        Assert.Equal(0, store.BestsFor("r").SplitHits("Iudex Gundyr"));
    }

    [Fact]
    public void AnAbandonedAttemptDoesNotCreateAWholeRunBest()
    {
        var store = new JsonRecordStore(Path_);
        store.RecordSplit("r", new SplitRecord("Iudex Gundyr", 30_000, 0, 0));

        // Totals from a run that was never finished are not comparable.
        var bests = store.BestsFor("r");
        Assert.Null(bests.BestTotalHits);
        Assert.Null(bests.BestRunIgtMs);
    }

    [Fact]
    public void TheBestSplitSurvivesALaterWorseAttempt()
    {
        var store = new JsonRecordStore(Path_);
        store.RecordSplit("r", new SplitRecord("Iudex Gundyr", 30_000, 0, 0));
        store.RecordSplit("r", new SplitRecord("Iudex Gundyr", 45_000, 4, 1));

        var bests = store.BestsFor("r");
        Assert.Equal(0, bests.SplitHits("Iudex Gundyr"));
        Assert.Equal(30_000, bests.SplitIgtMs("Iudex Gundyr"));
    }

    [Fact]
    public void SplitBestsSurviveReopeningTheStore()
    {
        var store = new JsonRecordStore(Path_);
        store.RecordSplit("r", new SplitRecord("Iudex Gundyr", 30_000, 1, 0));

        Assert.Equal(1, new JsonRecordStore(Path_).BestsFor("r").SplitHits("Iudex Gundyr"));
    }

    [Fact]
    public void ExistingHistoryWithoutStoredSplitBestsIsMigrated()
    {
        // A file written before split bests were kept separately.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, """
            {
              "runs": [
                {
                  "routeName": "r",
                  "profileName": "No-Hit",
                  "completedAt": "2026-01-01T00:00:00+00:00",
                  "runIgtMs": 60000,
                  "totalHits": 5,
                  "totalDeaths": 0,
                  "splits": [ { "name": "A", "igtMs": 60000, "hits": 5, "deaths": 0 } ]
                }
              ]
            }
            """);

        var bests = new JsonRecordStore(Path_).BestsFor("r");

        Assert.Equal(5, bests.BestTotalHits);
        Assert.Equal(5, bests.SplitHits("A"));   // rebuilt from the run
    }

    [Fact]
    public void RoutesAreTrackedSeparately()
    {
        var store = new JsonRecordStore(Path_);
        store.Record(Run("first", 2, 10_000, ("A", 2, 10_000)));
        store.Record(Run("second", 9, 20_000, ("A", 9, 20_000)));

        Assert.Equal(2, store.BestsFor("first").BestTotalHits);
        Assert.Equal(9, store.BestsFor("second").BestTotalHits);
    }

    [Fact]
    public void HistorySurvivesReopeningTheStore()
    {
        var store = new JsonRecordStore(Path_);
        store.Record(Run("r", 4, 60_000, ("A", 4, 60_000)));

        var reopened = new JsonRecordStore(Path_);

        Assert.Equal(4, reopened.BestsFor("r").BestTotalHits);
        Assert.Equal(4, reopened.BestsFor("r").SplitHits("A"));
    }

    [Fact]
    public void ACorruptHistoryFileIsIgnoredRatherThanFatal()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, "{ this is not json");

        var store = new JsonRecordStore(Path_);

        Assert.Null(store.BestsFor("r").BestTotalHits);

        // ...and the store still works from there on.
        store.Record(Run("r", 1, 10_000, ("A", 1, 10_000)));
        Assert.Equal(1, store.BestsFor("r").BestTotalHits);
    }
}
