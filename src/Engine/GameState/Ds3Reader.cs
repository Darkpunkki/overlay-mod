using OverlayMod.Engine.Memory;
using OverlayMod.Engine.Tracking;

namespace OverlayMod.Engine.GameState;

/// <summary>
/// Reads live state from a running Dark Souls III process. Attach once (it
/// finds the process, detects the version, and resolves the static pointers via
/// AOB scanning), then call <see cref="TakeSnapshot"/> on a poll loop.
///
/// Values fall into two confidence tiers:
///  - Verified-from-source: IGT, loading state, player-loaded, position. These
///    come directly from the auto-splitter pointer paths and are high-confidence.
///  - Seeded (pending live verification): HP / MaxHP. The chain is principled
///    (PlayerIns -> module bag -> data module) but the exact data-module field
///    offsets must be confirmed against the live game — that's Milestone 1's job.
/// </summary>
public sealed class Ds3Reader : ISnapshotSource, IFlagSource
{
    private const string ProcessName = "darksoulsiii"; // matched case-insensitively

    private ProcessMemory? _mem;
    private int _generation;

    private Pointer _gameDataMan = null!;
    private Pointer _loading = null!;
    private Pointer _playerSlot = null!;   // [.. +0x80] holds PlayerIns; non-zero => loaded
    private Pointer _physics = null!;       // player physics module (position)
    private Pointer _playerData = null!;    // player data module (HP)
    private long _playerInsStatic;          // resolved static that points at WorldChrMan
    private long _fieldAreaStatic;          // resolved static that points at FieldArea
    private long _eventFlagManStatic;       // resolved static that points at SprjEventFlagMan

    public Ds3Version Version { get; private set; }

    public bool Attached => _mem is { HasExited: false };

    public string Description => Attached ? $"Dark Souls III ({Version})" : "Dark Souls III (not attached)";

    /// <summary>
    /// Bumped on every successful attach. A new attach means a new game session,
    /// so whatever run was in progress no longer refers to anything real.
    /// </summary>
    public int Generation => _generation;

    IFlagSource ISnapshotSource.Flags => this;

    /// <summary>
    /// Find the process and resolve pointers. Returns false if the game isn't
    /// running or a signature failed to resolve (e.g. an unexpected version).
    /// </summary>
    public bool Attach()
    {
        Detach();

        var mem = ProcessMemory.TryAttach(ProcessName);
        if (mem == null) return false;

        try
        {
            Version = Ds3Version.FromProcess(mem.Process);

            var gameDataMan = mem.ScanRelative(Ds3Signatures.GameDataMan, 3, 7);
            var playerIns = mem.ScanRelative(Ds3Signatures.PlayerIns, 3, 7);
            var loading = mem.ScanRelative(Ds3Signatures.Loading, 2, 7);

            if (gameDataMan is null || playerIns is null || loading is null)
            {
                mem.Dispose();
                return false;
            }

            _playerInsStatic = playerIns.Value;
            _gameDataMan = new Pointer(mem, gameDataMan.Value, 0);
            _loading = new Pointer(mem, loading.Value);
            _playerSlot = new Pointer(mem, playerIns.Value, 0, 0x80);
            _physics = new Pointer(mem, playerIns.Value, 0, 0x40, 0x28);
            _playerData = new Pointer(mem, playerIns.Value, 0, 0x80, Version.ModuleBagOffset, 0x18);

            // Event-flag statics are optional: if a signature ever fails to resolve,
            // flag reads return false rather than aborting the whole attach.
            _fieldAreaStatic = mem.ScanRelative(Ds3Signatures.FieldArea, 3, 7) ?? 0;
            _eventFlagManStatic = mem.ScanRelative(Ds3Signatures.SprjEventFlagMan, 3, 11) ?? 0;

            _mem = mem;
            _generation++;
            return true;
        }
        catch
        {
            mem.Dispose();
            return false;
        }
    }

    public void Detach()
    {
        _mem?.Dispose();
        _mem = null;
    }

    // --- Verified-from-source reads ---

    public int IgtMs => Attached ? _gameDataMan.ReadInt32(Version.IgtOffset) : 0;

    public bool IsLoading => Attached && _loading.ReadInt32(-1) != 0;

    public bool IsPlayerLoaded => Attached && _playerSlot.ReadInt64() != 0;

    public (float X, float Y, float Z) Position =>
        Attached ? (_physics.ReadFloat(0x80), _physics.ReadFloat(0x84), _physics.ReadFloat(0x88)) : default;

    // --- Seeded reads (pending live verification) ---

    public int Hp => Attached ? _playerData.ReadInt32(0xD8) : 0;
    public int MaxHp => Attached ? _playerData.ReadInt32(0xE0) : 0;

    // --- Debug helpers (handy while verifying the seeded chains against the game) ---

    /// <summary>The WorldChrMan instance address ([playerInsStatic]).</summary>
    public long WorldChrMan => Attached ? _mem!.ReadInt64(_playerInsStatic) : 0;

    /// <summary>The player character object (PlayerIns) address.</summary>
    public long PlayerInsAddress => Attached ? new Pointer(_mem!, _playerInsStatic, 0, 0x80, 0).Address : 0;

    // --- Event flags (boss-defeat / bonfire / item) — pending live verification ---

    bool IFlagSource.IsEventFlagSet(uint flagId) => ReadEventFlag(flagId);

    /// <summary>
    /// Returns whether a DS3 event flag is set. This re-implements the game's own
    /// flag-lookup: decompose the id, resolve its storage "category" (via the
    /// FieldArea world-block tables for area-scoped flags), then index into the
    /// SprjEventFlagMan bit table. Boss-defeat ids come from the boss table
    /// (e.g. Nameless King = 13200850). Needs a live sanity check before relying on it.
    /// </summary>
    public bool ReadEventFlag(uint id)
    {
        if (!Attached) return false;
        var mem = _mem!;

        var a = (int)(id / 10000000 % 10);
        var area = (int)(id / 100000 % 100);
        var b = (int)(id / 10000 % 10);
        var c = (int)(id / 1000 % 10);

        var category = -1;
        if (area >= 90 || area + b == 0)
        {
            category = 0;
        }
        else
        {
            if (_fieldAreaStatic == 0) return false;
            var fieldArea = mem.ReadInt64(_fieldAreaStatic);
            if (fieldArea == 0) return false;

            var worldInfoOwner = mem.ReadInt64(fieldArea + 0x10);
            if (worldInfoOwner == 0) return false;

            var size = mem.ReadInt32(worldInfoOwner + 0x8);
            var vector = mem.ReadInt64(worldInfoOwner + 0x10);
            if (vector == 0) return false;

            for (var i = 0; i < size; i++)
            {
                var entry = vector + (long)i * 0x38;
                if (mem.ReadByte(entry + 0xb) != area) continue;

                var count = mem.ReadByte(entry + 0x20);
                if (count >= 1)
                {
                    var blockBase = mem.ReadInt64(entry + 0x28);
                    var index = 0;
                    var found = false;
                    while (true)
                    {
                        var flag = mem.ReadInt32(blockBase + (long)index * 0x70 + 0x8);
                        if (((flag >> 16) & 0xff) == b && (uint)flag >> 24 == (uint)area)
                        {
                            found = true;
                            break;
                        }
                        if (++index >= count) break;
                    }
                    if (found)
                        category = mem.ReadInt32(blockBase + (long)index * 0x70 + 0x20);
                }
                break; // matched the area entry; stop scanning
            }

            if (category > -1) category++;
        }

        if (category < 0 || _eventFlagManStatic == 0) return false;

        var eventFlagMan = mem.ReadInt64(_eventFlagManStatic);
        if (eventFlagMan == 0) return false;
        var table = mem.ReadInt64(eventFlagMan + 0x218);
        if (table == 0) return false;
        var bucket = mem.ReadInt64(table + (long)a * 0x18);
        if (bucket == 0) return false;

        var resultBase = ((long)c << 4) + bucket + (long)category * 0xa8;
        var word = (int)((id % 1000) >> 5) * 4;
        var value = mem.ReadUInt32(resultBase + word);
        var bit = 0x1f - (int)(id % 1000 & 0x1f);
        return (value & (1u << bit)) != 0;
    }

    public GameSnapshot TakeSnapshot()
    {
        if (!Attached) return GameSnapshot.Detached;

        var loaded = IsPlayerLoaded;
        var (x, y, z) = loaded ? Position : default;
        return new GameSnapshot
        {
            Attached = true,
            IgtMs = IgtMs,
            IsLoading = IsLoading,
            PlayerLoaded = loaded,
            Hp = loaded ? Hp : 0,
            MaxHp = loaded ? MaxHp : 0,
            X = x,
            Y = y,
            Z = z,
        };
    }

    public void Dispose() => Detach();
}
