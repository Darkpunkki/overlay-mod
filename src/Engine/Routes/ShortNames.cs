namespace OverlayMod.Engine.Routes;

/// <summary>
/// The short form of each boss's name, for players who would rather read
/// "Cinder" than "Soul of Cinder" on a stream at 720p.
///
/// This is a <em>preset</em>, not a policy. The display names are an ordinary
/// editable map — one button fills it in with these and every entry can then be
/// changed or cleared. Nothing here is applied automatically: a fresh install
/// shows the names the route file actually contains, because renaming things
/// behind a user's back makes their route file and their overlay disagree.
///
/// **Only the display changes.** Personal bests are keyed on the name in the
/// route, so shortening a name never orphans the history behind it.
///
/// Names already short enough are deliberately absent rather than mapped to
/// themselves — an entry that changes nothing is noise in the file and one more
/// row to scroll past on the control page.
/// </summary>
public static class ShortNames
{
    public static IReadOnlyDictionary<string, string> All { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Iudex Gundyr"] = "Gundyr",
            ["Vordt of the Boreal Valley"] = "Vordt",
            ["Curse-rotted Greatwood"] = "Greatwood",
            ["Anri of Astora"] = "Anri",
            ["Crystal Sage"] = "Sage",
            ["Deacons of the Deep"] = "Deacons",
            ["Abyss Watchers"] = "Abyss",
            ["High Lord Wolnir"] = "Wolnir",
            ["Old Demon King"] = "Demon King",
            ["Pontiff Sulyvahn"] = "Pontiff",
            ["Aldrich, Devourer of Gods"] = "Aldrich",
            ["Yhorm the Giant"] = "Yhorm",
            ["Dancer of the Boreal Valley"] = "Dancer",
            ["Dragonslayer Armour"] = "Dragonslayer",
            ["Oceiros, the Consumed King"] = "Oceiros",
            ["Champion Gundyr"] = "Champion",
            ["Ancient Wyvern"] = "Wyvern",

            // The community name, and the one a viewer will recognise. The route
            // files carry the flag's own name because that is the character whose
            // death sets it.
            ["Lothric, Younger Prince"] = "Twin Princes",

            ["Sister Friede"] = "Friede",
            ["Champion's Gravetender & Greatwolf"] = "Gravetender",
            ["Halflight, Spear of the Church"] = "Halflight",
            ["Darkeater Midir"] = "Midir",
            ["Slave Knight Gael"] = "Gael",
            ["Soul of Cinder"] = "Cinder",
        };
}
