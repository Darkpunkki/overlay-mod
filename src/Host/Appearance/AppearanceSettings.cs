using System.Text.RegularExpressions;

namespace OverlayMod.Host.Appearance;

/// <summary>
/// How the overlay looks. Every value maps onto a CSS custom property the
/// overlay stylesheet already reads, so changing one restyles the page without
/// any layout code caring.
///
/// Defaults match the stylesheet's own, so a fresh install looks the same
/// whether or not this has ever been saved.
/// </summary>
public sealed partial record AppearanceSettings
{
    /// <summary>Overall size multiplier. Scales the whole overlay rather than stretching a bitmap.</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>
    /// The run clock's size, on top of <see cref="Scale"/>.
    ///
    /// Separate because the clock and the split list are read by different people
    /// for different reasons: the clock is for the viewer, at a glance, from
    /// across a room, while the split list is detail. Scaling the whole overlay to
    /// get a bigger clock costs the space the splits need, so this multiplies only
    /// the timer — the rest of the overlay stays where it was.
    /// </summary>
    public double TimerScale { get; init; } = 1.0;

    /// <summary>
    /// Whether the attempt count appears beside the challenge name.
    ///
    /// On by default: an attempt counter is standard on a run overlay, and the
    /// number is the thing a No-Hit viewer asks about first. It is one small line
    /// and it turns off in one click for anyone who would rather not show it.
    /// </summary>
    public bool ShowAttempts { get; init; } = true;

    /// <summary>
    /// How many split rows show at once. The list scrolls through longer routes,
    /// so this is the overlay's height rather than the route's length: one row
    /// for a minimal display, or thirty to show a whole All Bosses route at once.
    /// </summary>
    public int VisibleSplits { get; init; } = 6;

    public string Text { get; init; } = "#f4f4f5";
    public string Dim { get; init; } = "#9a9aa2";
    public string Accent { get; init; } = "#e0b65c";
    public string Ahead { get; init; } = "#6fbf73";
    public string Behind { get; init; } = "#d8685a";

    /// <summary>Backing panel colour. Its opacity is what makes the overlay more or less see-through.</summary>
    public string Plate { get; init; } = "#0a0a0c";

    public double PlateOpacity { get; init; } = 0.74;

    /// <summary>Text shadow strength, which is what keeps text readable over bright scenes.</summary>
    public double ShadowStrength { get; init; } = 0.9;

    public static AppearanceSettings Default => new();

    /// <summary>
    /// Clamp everything into range and reject malformed colours.
    ///
    /// These values are written straight into CSS custom properties, and they
    /// arrive over HTTP, so nothing unvalidated should reach the page. Anything
    /// that is not a plain six-digit hex colour falls back to the default rather
    /// than being passed through.
    /// </summary>
    public AppearanceSettings Sanitised()
    {
        var d = Default;
        return new AppearanceSettings
        {
            Scale = Math.Clamp(double.IsFinite(Scale) ? Scale : d.Scale, 0.5, 3.0),
            TimerScale = Math.Clamp(double.IsFinite(TimerScale) ? TimerScale : d.TimerScale, 0.4, 3.0),
            ShowAttempts = ShowAttempts,
            VisibleSplits = Math.Clamp(VisibleSplits, 1, 30),
            Text = Colour(Text, d.Text),
            Dim = Colour(Dim, d.Dim),
            Accent = Colour(Accent, d.Accent),
            Ahead = Colour(Ahead, d.Ahead),
            Behind = Colour(Behind, d.Behind),
            Plate = Colour(Plate, d.Plate),
            PlateOpacity = Math.Clamp(double.IsFinite(PlateOpacity) ? PlateOpacity : d.PlateOpacity, 0, 1),
            ShadowStrength = Math.Clamp(double.IsFinite(ShadowStrength) ? ShadowStrength : d.ShadowStrength, 0, 1),
        };
    }

    private static string Colour(string? value, string fallback) =>
        value is not null && HexColour().IsMatch(value) ? value.ToLowerInvariant() : fallback;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();
}
