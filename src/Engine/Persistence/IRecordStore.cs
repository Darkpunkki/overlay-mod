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

    /// <summary>File a finished run and fold it into that route's bests.</summary>
    void Record(RunRecord run);
}
