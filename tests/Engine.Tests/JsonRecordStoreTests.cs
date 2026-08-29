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

    /// <summary>A run where nothing was attributed to a fall, so damage and hits agree.</summary>
    private static RunRecord Run(string route, int totalHits, int runMs, params (string Name, int Hits, int Ms)[] splits)
    {
        var list = new List<SplitRecord>();
        foreach (var s in splits) list.Add(new SplitRecord(s.Name, s.Ms, s.Hits, 0) { Hits = s.Hits });
        return new RunRecord(route, "No Hit", DateTimeOffset.UnixEpoch, runMs, totalHits, 0, list)
        { TotalHits = totalHits };
    }

    private static RunRecord DeathRun(string route, params (string Name, int Deaths)[] splits)
    {
        var list = new List<SplitRecord>();
        var total = 0;
        foreach (var s in splits) { list.Add(new SplitRecord(s.Name, 1000, 0, s.Deaths) { Hits = 0 }); total += s.Deaths; }
        return new RunRecord(route, "Deathless", DateTimeOffset.UnixEpoch, 1000, 0, total, list)
        { TotalHits = 0 };
    }

    /// <summary>One split banked from an attempt, with no fall damage in it.</summary>
    private static SplitRecord Split(string name, int igtMs, int hits, int deaths) =>
        new(name, igtMs, hits, deaths) { Hits = hits };

    [Fact]
    public void AnUnknownRouteHasNoBests()
    {
        var store = new JsonRecordStore(Path_);
        var bests = store.BestsFor("nothing here");

        Assert.Null(bests.BestTotalDamage);
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
        store.RecordSplit("r", Split("Iudex Gundyr", 30_000, 0, 0));

        Assert.Equal(0, store.BestsFor("r").SplitHits("Iudex Gundyr"));
    }

    [Fact]
    public void AnAbandonedAttemptDoesNotCreateAWholeRunBest()
    {
        var store = new JsonRecordStore(Path_);
        store.RecordSplit("r", Split("Iudex Gundyr", 30_000, 0, 0));

        // Totals from a run that was never finished are not comparable.
        var bests = store.BestsFor("r");
        Assert.Null(bests.BestTotalHits);
        Assert.Null(bests.BestRunIgtMs);
    }

    [Fact]
    public void TheBestSplitSurvivesALaterWorseAttempt()
    {
        var store = new JsonRecordStore(Path_);
        store.RecordSplit("r", Split("Iudex Gundyr", 30_000, 0, 0));
        store.RecordSplit("r", Split("Iudex Gundyr", 45_000, 4, 1));

        var bests = store.BestsFor("r");
        Assert.Equal(0, bests.SplitHits("Iudex Gundyr"));
        Assert.Equal(30_000, bests.SplitIgtMs("Iudex Gundyr"));
    }

    [Fact]
    public void SplitBestsSurviveReopeningTheStore()
    {
        var store = new JsonRecordStore(Path_);
        store.RecordSplit("r", Split("Iudex Gundyr", 30_000, 1, 0));

        Assert.Equal(1, new JsonRecordStore(Path_).BestsFor("r").SplitHits("Iudex Gundyr"));
    }

    // --- manual corrections to a banked hit best ---

    [Fact]
    public void CorrectingBankedHits_CanRaiseThem_WhichAFoldNeverCould()
    {
        var store = new JsonRecordStore(Path_);
        store.RecordSplit("r", Split("Iudex Gundyr", 30_000, 1, 0));

        // The player says two hits landed, not one. RecordSplit would keep the
        // flattering minimum; the correction replaces it.
        store.CorrectSplitHits("r", "Iudex Gundyr", 2);

        var bests = store.BestsFor("r");
        Assert.Equal(2, bests.SplitHits("Iudex Gundyr"));

        // Only hits moved; the rest of the banked record stands.
        Assert.Equal(30_000, bests.SplitIgtMs("Iudex Gundyr"));

        // And the correction is on disk, not only in memory.
        Assert.Equal(2, new JsonRecordStore(Path_).BestsFor("r").SplitHits("Iudex Gundyr"));
    }

    [Fact]
    public void CorrectingASplitThatWasNeverBanked_DoesNothing()
    {
        var store = new JsonRecordStore(Path_);
        store.CorrectSplitHits("r", "Iudex Gundyr", 2);

        Assert.Null(store.BestsFor("r").SplitHits("Iudex Gundyr"));
    }

    [Fact]
    public void ExistingHistoryWithoutStoredSplitBestsIsMigrated()
    {
        // A file written before split bests were kept separately.
        WriteLegacyHistory();

        var bests = new JsonRecordStore(Path_).BestsFor("r");

        Assert.Equal(5, bests.BestTotalDamage);
        Assert.Equal(5, bests.SplitDamage("A"));   // rebuilt from the run
    }

    // --- the 0.1.0 -> 0.2.0 split of "hits" into damage and hits ---

    [Fact]
    public void LegacyHitsBecomeDamage_BecauseThatIsWhatTheyCounted()
    {
        // 0.1.0 counted every drop in health under the name "hits", which is
        // exactly what damage means now. Those numbers are still true, so they
        // carry over rather than being discarded.
        WriteLegacyHistory();

        var bests = new JsonRecordStore(Path_).BestsFor("r");

        Assert.Equal(5, bests.BestTotalDamage);
        Assert.Equal(5, bests.SplitDamage("A"));
    }

    [Fact]
    public void LegacyHistoryLeavesTheHitBestUnset_BecauseFallsWereNeverToldApart()
    {
        // Nothing in an old file says which of those five was the ground, so a
        // No Hit best cannot be recovered from it. Null is the honest answer;
        // inventing one would put an unbeatable target on screen.
        WriteLegacyHistory();

        var bests = new JsonRecordStore(Path_).BestsFor("r");

        Assert.Null(bests.BestTotalHits);
        Assert.Null(bests.SplitHits("A"));
    }

    [Fact]
    public void AMigratedFileIsRewrittenSoTheMigrationHappensOnlyOnce()
    {
        WriteLegacyHistory();
        new JsonRecordStore(Path_);

        // Reopening must not treat the already-migrated damage as legacy hits
        // again - the second pass would have nothing to move and would blank it.
        Assert.Equal(5, new JsonRecordStore(Path_).BestsFor("r").BestTotalDamage);
        Assert.Contains("\"schema\"", File.ReadAllText(Path_));
    }

    /// <summary>Run history exactly as 0.1.0 wrote it: one counter, called hits, no schema marker.</summary>
    private void WriteLegacyHistory()
    {
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
