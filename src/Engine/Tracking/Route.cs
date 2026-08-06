namespace OverlayMod.Engine.Tracking;

/// <summary>
/// One split in a route. A boss split carries the DS3 event-flag id that marks
/// the boss as defeated, which auto-advances the split when it flips true.
/// </summary>
public sealed record RouteSplit(string Name, bool IsBoss, uint? DefeatFlagId = null);

/// <summary>An ordered list of splits the player runs under a chosen profile.</summary>
public sealed record Route(string Name, ChallengeProfile Profile, IReadOnlyList<RouteSplit> Splits);
