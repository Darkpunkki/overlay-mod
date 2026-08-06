namespace OverlayMod.Engine.Persistence;

/// <summary>
/// Stores finished runs and answers what the best results on a route are.
///
/// The JSON implementation is a stopgap so personal bests work now; Milestone 6
/// replaces it with SQLite behind this same interface, which the overlay never
/// sees either way.
/// </summary>
public interface IRecordStore
{
    /// <summary>Best results for a route, or <see cref="PersonalBests.Empty"/> if it has never been run.</summary>
    PersonalBests BestsFor(string routeName);

    /// <summary>File a finished run: whole-run bests, and its splits.</summary>
    void Record(RunRecord run);

    /// <summary>
    /// File one split the moment it is completed, without waiting for the run to
    /// finish. Most attempts end early, so a personal best per boss has to come
    /// from any attempt that got that far — otherwise a great fight in a run
    /// later abandoned would count for nothing.
    ///
    /// Whole-run bests deliberately do not work this way: those only mean
    /// something for a run that was actually completed.
    /// </summary>
    void RecordSplit(string routeName, SplitRecord split);
}
