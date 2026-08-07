using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.Routes;

/// <summary>
/// Routes written to the routes directory on first run, so there is something to
/// select before any route editor exists. They are ordinary files afterwards —
/// edit them, copy them, or add your own; nothing here overwrites a file that
/// already exists.
///
/// **Provenance of the flag ids.** Boss-defeat event flag ids are constants of
/// the game. They were read off the table published by the open-source
/// SoulSplitter project, which is where Iudex Gundyr's and the Nameless King's
/// already-known values came from — both match, which cross-checks the rest.
/// These are numeric facts about Dark Souls III, not code: no SoulSplitter code
/// is used here, consistent with the credits in the README.
///
/// They still have not been observed flipping on a live game. That check is
/// cheap now that they are all present — see the pending-verification table in
/// docs/PLAN.md — and `FlagsVerified` stays false until it is done.
///
/// Split ordering is a normal progression, not a speedrun route. Reorder or
/// delete splits to match how you actually play.
/// </summary>
public static class BuiltInRoutes
{
    private static RouteSplitFile Boss(string name, uint flag) => new(name, IsBoss: true, flag);

    // --- Main game ---

    private static readonly RouteSplitFile IudexGundyr = Boss("Iudex Gundyr", 14000800);
    private static readonly RouteSplitFile Vordt = Boss("Vordt of the Boreal Valley", 13000800);
    private static readonly RouteSplitFile Greatwood = Boss("Curse-rotted Greatwood", 13100800);
    private static readonly RouteSplitFile CrystalSage = Boss("Crystal Sage", 13300850);
    private static readonly RouteSplitFile Deacons = Boss("Deacons of the Deep", 13500800);
    private static readonly RouteSplitFile AbyssWatchers = Boss("Abyss Watchers", 13300800);
    private static readonly RouteSplitFile Wolnir = Boss("High Lord Wolnir", 13800800);
    private static readonly RouteSplitFile OldDemonKing = Boss("Old Demon King", 13800830);
    private static readonly RouteSplitFile Pontiff = Boss("Pontiff Sulyvahn", 13700850);
    private static readonly RouteSplitFile Aldrich = Boss("Aldrich, Devourer of Gods", 13700800);
    private static readonly RouteSplitFile Yhorm = Boss("Yhorm the Giant", 13900800);
    private static readonly RouteSplitFile Dancer = Boss("Dancer of the Boreal Valley", 13000890);
    private static readonly RouteSplitFile DragonslayerArmour = Boss("Dragonslayer Armour", 13010800);
    private static readonly RouteSplitFile Oceiros = Boss("Oceiros, the Consumed King", 13000830);
    private static readonly RouteSplitFile ChampionGundyr = Boss("Champion Gundyr", 14000830);
    private static readonly RouteSplitFile AncientWyvern = Boss("Ancient Wyvern", 13200800);
    private static readonly RouteSplitFile NamelessKing = Boss("Nameless King", 13200850);
    private static readonly RouteSplitFile TwinPrinces = Boss("Lothric, Younger Prince", 13410830);
    private static readonly RouteSplitFile SoulOfCinder = Boss("Soul of Cinder", 14100800);

    // --- Ashes of Ariandel / The Ringed City ---

    private static readonly RouteSplitFile SisterFriede = Boss("Sister Friede", 14500800);
    private static readonly RouteSplitFile Gravetender = Boss("Champion's Gravetender & Greatwolf", 14500860);
    private static readonly RouteSplitFile DemonPrince = Boss("Demon Prince", 15000800);
    private static readonly RouteSplitFile Halflight = Boss("Halflight, Spear of the Church", 15100800);
    private static readonly RouteSplitFile Midir = Boss("Darkeater Midir", 15100850);
    private static readonly RouteSplitFile Gael = Boss("Slave Knight Gael", 15110800);

    private static readonly RouteSplitFile[] MainGame =
    {
        IudexGundyr, Vordt, Greatwood, CrystalSage, Deacons, AbyssWatchers,
        Wolnir, OldDemonKing, Pontiff, Aldrich, Yhorm, Dancer,
        DragonslayerArmour, Oceiros, ChampionGundyr, AncientWyvern, NamelessKing,
        TwinPrinces, SoulOfCinder,
    };

    public static RouteFile AllBosses => new(
        "All Bosses (main game)",
        ChallengeType.Speedrun,
        MainGame)
    { FlagsVerified = false };

    /// <summary>
    /// Main game plus both DLCs, with the DLC bosses placed before Soul of
    /// Cinder — the usual all-bosses ordering, since the Kiln ends the run.
    /// </summary>
    public static RouteFile AllBossesWithDlc => new(
        "All Bosses (with DLC)",
        ChallengeType.Speedrun,
        MainGame[..^1]
            .Concat(new[] { SisterFriede, Gravetender, DemonPrince, Halflight, Midir, Gael, SoulOfCinder })
            .ToArray())
    { FlagsVerified = false };

    /// <summary>
    /// The bosses a normal completion actually goes through. Dark Souls III can
    /// be finished without killing everything, so an all-bosses list is the wrong
    /// shape for most runs — this is the shorter path to the Kiln.
    /// </summary>
    public static RouteFile Quick => new(
        "Quick route",
        ChallengeType.NoDamage,
        new[]
        {
            IudexGundyr,
            Vordt,
            AbyssWatchers,
            Wolnir,
            Dancer,
            CrystalSage,
            Deacons,
            Pontiff,
            Yhorm,
            Aldrich,
            DragonslayerArmour,
            TwinPrinces,
            SoulOfCinder,
        })
    { FlagsVerified = false };

    /// <summary>
    /// The three bosses the scripted fake source plays through, for developing
    /// against <c>--fake</c> without the game.
    /// </summary>
    public static RouteFile Demo => new(
        "Demo (first three bosses)",
        ChallengeType.NoDamage,
        new[] { IudexGundyr, Vordt, Greatwood })
    { FlagsVerified = false };

    public static IReadOnlyList<RouteFile> All => new[] { Quick, AllBosses, AllBossesWithDlc, Demo };
}
