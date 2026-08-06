using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.Routes;

/// <summary>
/// Routes written to the routes directory on first run, so there is something to
/// select before any route editor exists. They are ordinary files afterwards —
/// edit them, copy them, or add your own; nothing here overwrites a file that
/// already exists.
///
/// **On the flag ids.** Boss names and ordering are plain game knowledge and are
/// reliable. The boss-defeat *event flag ids* are not: only Iudex Gundyr's and
/// the Nameless King's come from a known-good source. Rather than fill the rest
/// in from a guessed numbering pattern — a wrong id fails silently, never firing
/// or firing at the wrong moment — they are left null, which means "split this
/// one manually". Confirming them against a live game is Milestone 5 work; see
/// the pending-verification table in docs/PLAN.md.
/// </summary>
public static class BuiltInRoutes
{
    private static RouteSplitFile Boss(string name, uint? flag = null) => new(name, IsBoss: true, flag);

    /// <summary>
    /// Every main-game boss, in a normal progression order. Not a speedrun route:
    /// reorder or delete splits to match how you actually play.
    /// </summary>
    public static RouteFile AllBosses => new(
        "All Bosses (main game)",
        ChallengeType.AllBosses,
        new[]
        {
            Boss("Iudex Gundyr", 14000800),
            Boss("Vordt of the Boreal Valley"),
            Boss("Curse-rotted Greatwood"),
            Boss("Crystal Sage"),
            Boss("Deacons of the Deep"),
            Boss("Abyss Watchers"),
            Boss("High Lord Wolnir"),
            Boss("Old Demon King"),
            Boss("Pontiff Sulyvahn"),
            Boss("Aldrich, Devourer of Gods"),
            Boss("Yhorm the Giant"),
            Boss("Dancer of the Boreal Valley"),
            Boss("Dragonslayer Armour"),
            Boss("Consumed King Oceiros"),
            Boss("Champion Gundyr"),
            Boss("Ancient Wyvern"),
            Boss("Nameless King", 13200850),
            Boss("Lothric, Younger Prince"),
            Boss("Soul of Cinder"),
        })
    { FlagsVerified = false };

    /// <summary>
    /// The three bosses the scripted fake source plays through. Its flag ids are
    /// whatever that script sets, so this route auto-splits end to end with
    /// <c>--fake</c> regardless of what the real game uses.
    /// </summary>
    public static RouteFile Demo => new(
        "Demo (first three bosses)",
        ChallengeType.NoHit,
        new[]
        {
            Boss("Iudex Gundyr", 14000800),
            Boss("Vordt of the Boreal Valley", 13000800),
            Boss("Curse-rotted Greatwood", 13100800),
        })
    { FlagsVerified = false };

    public static IReadOnlyList<RouteFile> All => new[] { AllBosses, Demo };
}
