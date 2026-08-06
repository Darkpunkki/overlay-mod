using OverlayMod.Engine.Tracking;

namespace OverlayMod.Host;

/// <summary>
/// The placeholder route used until routes are loaded from disk (Milestone 5).
/// Its splits line up with the boss-defeat flags in
/// <see cref="Engine.GameState.FakeSnapshotSource.DemoRun"/>, so running the host
/// with <c>--fake</c> produces a complete three-split run.
///
/// The flag ids are the community-documented boss-defeat flags, but they have not
/// been verified against a live game yet — that check is part of Milestone 5.
/// </summary>
public static class DemoRoute
{
    public static Route Create() => new(
        "Demo (first three bosses)",
        ChallengeProfile.NoHit,
        new[]
        {
            new RouteSplit("Iudex Gundyr", IsBoss: true, DefeatFlagId: 14000800),
            new RouteSplit("Vordt of the Boreal Valley", IsBoss: true, DefeatFlagId: 13000800),
            new RouteSplit("Curse-rotted Greatwood", IsBoss: true, DefeatFlagId: 13100800),
        });
}
