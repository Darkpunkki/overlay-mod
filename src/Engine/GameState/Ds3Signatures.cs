using OverlayMod.Engine.Memory;

namespace OverlayMod.Engine.GameState;

/// <summary>
/// AOB signatures that locate DS3's static structures. These match code that
/// references the structures via RIP-relative addressing, so they are stable
/// across the game's ASLR. Sourced from the DS3 reverse-engineering community
/// (LiveSplit/SoulSplitter auto-splitter, practice-tool, Cheat Engine tables);
/// re-implemented here independently. DS3 receives no further patches, so these
/// are effectively frozen.
/// </summary>
internal static class Ds3Signatures
{
    // mov rcx, [rip+x] ; ... -> &GameDataMan (IGT, play stats)
    public static readonly AobPattern GameDataMan = new(
        "48 8b 0d ? ? ? ? 4c 8d 44 24 40 45 33 c9 48 8b d3 40 88 74 24 28 44 88 74 24 20");

    // mov rcx, [rip+x] ; ... -> &WorldChrMan (player character / position / HP)
    public static readonly AobPattern PlayerIns = new(
        "48 8b 0d ? ? ? ? 45 33 c0 48 8d 55 e7 e8 ? ? ? ? 0f 2f 73 70 72 0d f3 ? ? ? ? ? ? ? ? 0f 11 43 70");

    // mov byte ptr [rip+x], imm8 -> &IsLoading flag
    public static readonly AobPattern Loading = new(
        "c6 05 ? ? ? ? ? e8 ? ? ? ? 84 c0 0f 94 c0 e9");

    // mov qword ptr [rip+x], 0 -> &SprjEventFlagMan (boss-defeat / bonfire / item flags)
    public static readonly AobPattern SprjEventFlagMan = new(
        "48 c7 05 ? ? ? ? 00 00 00 00 48 8b 7c 24 38 c7 46 54 ff ff ff ff 48 83 c4 20 5e c3");

    // mov r15, [rip+x] -> &FieldArea (world-block info; needed to resolve flag categories)
    public static readonly AobPattern FieldArea = new(
        "4c 8b 3d ? ? ? ? 8b 45 87 83 f8 ff 74 69 48 8d 4d 8f 48 89 4d 9f 89 45 8f 48 8d 55 8f 49 8b 4f 10");
}
