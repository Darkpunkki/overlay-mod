namespace OverlayMod.Engine.Memory;

/// <summary>
/// A multi-level pointer: a base address followed by a chain of offsets.
/// Resolution dereferences every offset except the last, which is added to
/// produce the final address that gets read. This mirrors the convention used
/// across the souls-memory community (and game-internal pointer paths):
///
///   addr = base
///   addr = [addr + offset0]      // intermediate: dereferenced
///   addr = [addr + offset1]
///   ...
///   addr =  addr + offsetN       // last: NOT dereferenced -> field address
///
/// A read with an extra offset appends it as a new final offset, so the
/// previously-final offset then gets dereferenced.
/// </summary>
public sealed class Pointer
{
    private readonly ProcessMemory _mem;
    public long BaseAddress { get; }
    private readonly long[] _offsets;

    public Pointer(ProcessMemory mem, long baseAddress, params long[] offsets)
    {
        _mem = mem;
        BaseAddress = baseAddress;
        _offsets = offsets;
    }

    /// <summary>Returns a new pointer with additional offsets appended to the chain.</summary>
    public Pointer Append(params long[] extra)
    {
        if (extra.Length == 0) return this;
        var combined = new long[_offsets.Length + extra.Length];
        Array.Copy(_offsets, combined, _offsets.Length);
        Array.Copy(extra, 0, combined, _offsets.Length, extra.Length);
        return new Pointer(_mem, BaseAddress, combined);
    }

    private long Resolve(long? extra)
    {
        if (BaseAddress == 0) return 0;
        var count = _offsets.Length + (extra.HasValue ? 1 : 0);
        if (count == 0) return BaseAddress;

        var ptr = BaseAddress;
        for (var i = 0; i < count; i++)
        {
            var off = i < _offsets.Length ? _offsets[i] : extra!.Value;
            var addr = ptr + off;
            if (i + 1 < count)
            {
                ptr = _mem.ReadInt64(addr);
                if (ptr == 0) return 0;
            }
            else
            {
                ptr = addr;
            }
        }
        return ptr;
    }

    /// <summary>The final resolved address (0 if any intermediate pointer was null).</summary>
    public long Address => Resolve(null);

    public bool IsNull => Resolve(null) == 0;

    public int ReadInt32(long? offset = null) { var a = Resolve(offset); return a == 0 ? 0 : _mem.ReadInt32(a); }
    public uint ReadUInt32(long? offset = null) { var a = Resolve(offset); return a == 0 ? 0u : _mem.ReadUInt32(a); }
    public long ReadInt64(long? offset = null) { var a = Resolve(offset); return a == 0 ? 0 : _mem.ReadInt64(a); }
    public float ReadFloat(long? offset = null) { var a = Resolve(offset); return a == 0 ? 0f : _mem.ReadFloat(a); }
    public byte ReadByte(long? offset = null) { var a = Resolve(offset); return a == 0 ? (byte)0 : _mem.ReadByte(a); }
}
