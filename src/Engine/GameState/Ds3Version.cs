using System.Diagnostics;

namespace OverlayMod.Engine.GameState;

/// <summary>
/// DS3 shifted a few structure offsets across patches. We only need the ones
/// that affect values we read; the rest are resolved via AOB scanning and are
/// version-independent. The vast majority of players run the final patch
/// (1.15.x), for which the "late" offsets apply.
/// </summary>
public readonly record struct Ds3Version(int Major, int Minor, string Raw)
{
    public static Ds3Version FromProcess(Process process)
    {
        var raw = process.MainModule?.FileVersionInfo.ProductVersion ?? "0.0.0.0";
        if (Version.TryParse(raw, out var v))
            return new Ds3Version(v.Major, v.Minor, raw);
        return new Ds3Version(0, 0, raw);
    }

    /// <summary>
    /// In-game-time lives at GameDataMan + this offset. 0x9C up to and including
    /// v1.07; 0xA4 from v1.08 onward.
    /// </summary>
    public long IgtOffset => Minor >= 8 ? 0xA4 : 0x9C;

    /// <summary>
    /// Offset of the character module-bag pointer inside PlayerIns. The data
    /// module (HP) hangs off bag+0x18; the time-act/animation module off bag+0x80.
    /// Shifts by patch: 0x1F70 (=v1.04), 0x1F80 (v1.05-1.12), 0x1F90 (v1.13+).
    /// </summary>
    public long ModuleBagOffset => Minor switch
    {
        <= 4 => 0x1F70,
        >= 5 and <= 12 => 0x1F80,
        _ => 0x1F90,
    };

    public override string ToString() => Raw;
}
