namespace OverlayMod.Engine.Persistence;

/// <summary>
/// A finished run, kept so later runs can be compared against it.
///
/// <paramref name="TotalDamage"/> counts every drop in health; <see cref="TotalHits"/>
/// counts only what the game dealt. Runs recorded before 0.2.0 knew nothing about
/// the distinction, so their hit count is null rather than guessed at — see
/// <see cref="JsonRecordStore"/>.
/// </summary>
public sealed record RunRecord(
    string RouteName,
    string ProfileName,
    DateTimeOffset CompletedAt,
    int RunIgtMs,
    int TotalDamage,
    int TotalDeaths,
    IReadOnlyList<SplitRecord> Splits)
{
    /// <summary>Damage excluding falls. Null on runs recorded before fall damage was told apart.</summary>
    public int? TotalHits { get; init; }
}

/// <summary>One split's result. <see cref="Hits"/> is null when it predates fall detection.</summary>
public sealed record SplitRecord(string Name, int IgtMs, int Damage, int Deaths)
{
    public int? Hits { get; init; }
}

/// <summary>
/// The best results seen so far on one route. Every metric is tracked regardless
/// of which one the current profile ranks by, so switching profiles does not
/// throw away history.
///
/// Per-split bests are the best each split has *ever* been, taken across
/// different runs — LiveSplit's "gold splits". A run that beats every split
/// best is a theoretically perfect run, which is exactly the thing a No-Hit
/// runner wants to see themselves closing in on.
///
/// Damage and hits are kept apart all the way down. They are different
/// questions — "did anything touch my health" versus "did anything hit me" — and
/// folding one into the other would let a No Damage attempt set a best that a No
/// Hit attempt could never fairly be measured against.
/// </summary>
public sealed record PersonalBests(
    int? BestRunIgtMs,
    int? BestTotalDamage,
    int? BestTotalHits,
    int? BestTotalDeaths,
    IReadOnlyDictionary<string, int> BestSplitDamage,
    IReadOnlyDictionary<string, int> BestSplitHits,
    IReadOnlyDictionary<string, int> BestSplitDeaths,
    IReadOnlyDictionary<string, int> BestSplitIgtMs)
{
    public static readonly PersonalBests Empty = new(
        null, null, null, null,
        new Dictionary<string, int>(),
        new Dictionary<string, int>(),
        new Dictionary<string, int>(),
        new Dictionary<string, int>());

    public int? SplitDamage(string name) => BestSplitDamage.TryGetValue(name, out var v) ? v : null;

    public int? SplitHits(string name) => BestSplitHits.TryGetValue(name, out var v) ? v : null;

    public int? SplitDeaths(string name) => BestSplitDeaths.TryGetValue(name, out var v) ? v : null;

    public int? SplitIgtMs(string name) => BestSplitIgtMs.TryGetValue(name, out var v) ? v : null;
}
