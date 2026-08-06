namespace OverlayMod.Engine.Tracking;

/// <summary>
/// A serialisable capture of run progress, so a run survives quitting the game —
/// or the overlay host — and picks up where it left off.
///
/// Dark Souls III stores in-game time in the save file, so IGT continues across
/// a restart rather than resetting. That is what makes resuming work: the run
/// timer is an IGT difference, and both ends of that difference survive.
/// </summary>
public sealed record RunState(
    string RouteName,
    int RunStartIgt,
    int CurrentIgt,
    int ActiveIndex,
    RunPhase Phase,
    IReadOnlyList<SplitState> Splits);

/// <summary>One split's accumulated results, flattened for storage.</summary>
public sealed record SplitState(
    string Name,
    bool IsBoss,
    bool Completed,
    int ApproachIgtMs,
    int ApproachHits,
    int ApproachDeaths,
    int BossIgtMs,
    int BossHits,
    int BossDeaths);
