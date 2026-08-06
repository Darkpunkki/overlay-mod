namespace OverlayMod.Engine.Persistence;

/// <summary>A finished run, kept so later runs can be compared against it.</summary>
public sealed record RunRecord(
    string RouteName,
    string ProfileName,
    DateTimeOffset CompletedAt,
    int RunIgtMs,
    int TotalHits,
    int TotalDeaths,
    IReadOnlyList<SplitRecord> Splits);

public sealed record SplitRecord(string Name, int IgtMs, int Hits, int Deaths);

/// <summary>
/// The best results seen so far on one route. Every metric is tracked regardless
/// of which one the current profile ranks by, so switching profiles does not
/// throw away history.
///
/// Per-split bests are the best each split has *ever* been, taken across
/// different runs — LiveSplit's "gold splits". A run that beats every split
/// best is a theoretically perfect run, which is exactly the thing a No-Hit
/// runner wants to see themselves closing in on.
/// </summary>
public sealed record PersonalBests(
    int? BestRunIgtMs,
    int? BestTotalHits,
    int? BestTotalDeaths,
    IReadOnlyDictionary<string, int> BestSplitHits,
    IReadOnlyDictionary<string, int> BestSplitIgtMs)
{
    public static readonly PersonalBests Empty = new(
        null, null, null,
        new Dictionary<string, int>(),
        new Dictionary<string, int>());

    public int? SplitHits(string name) => BestSplitHits.TryGetValue(name, out var v) ? v : null;

    public int? SplitIgtMs(string name) => BestSplitIgtMs.TryGetValue(name, out var v) ? v : null;
}
