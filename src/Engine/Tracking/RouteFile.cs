namespace OverlayMod.Engine.Tracking;

/// <summary>
/// The on-disk form of a route: an ordered list of splits, plus the challenge it
/// is normally run under. Separate from <see cref="Route"/> because a route is
/// just a list of things to beat — the same boss list can be run as No-Hit or as
/// Any%, so the profile is chosen when the route is loaded, not baked into it.
/// </summary>
public sealed record RouteFile(
    string Name,
    ChallengeType DefaultChallenge,
    IReadOnlyList<RouteSplitFile> Splits)
{
    /// <summary>
    /// False while any boss-defeat flag id in this route is still guesswork.
    /// Auto-splitting cannot be trusted until a live game confirms them, so the
    /// overlay says so rather than quietly mis-splitting.
    /// </summary>
    public bool FlagsVerified { get; init; }

    public Route ToRoute(ChallengeProfile profile)
    {
        var splits = new List<RouteSplit>(Splits.Count);
        foreach (var s in Splits) splits.Add(new RouteSplit(s.Name, s.IsBoss, s.DefeatFlagId));
        return new Route(Name, profile, splits);
    }

    /// <summary>How many splits can auto-advance; the rest need a manual split.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int AutoSplitCount
    {
        get
        {
            var n = 0;
            foreach (var s in Splits) if (s.DefeatFlagId is not null) n++;
            return n;
        }
    }
}

/// <summary>
/// One split in a route file. <paramref name="DefeatFlagId"/> is the DS3 event
/// flag that marks the boss dead; null means this split has no known flag and
/// must be advanced manually.
/// </summary>
public sealed record RouteSplitFile(string Name, bool IsBoss, uint? DefeatFlagId);
