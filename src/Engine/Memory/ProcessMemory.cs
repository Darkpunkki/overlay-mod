using System.Diagnostics;

namespace OverlayMod.Engine.Memory;

/// <summary>
/// Attaches to a target process and provides typed reads plus AOB scanning of
/// its main module. All reads are best-effort: a failed read returns the zero
/// value rather than throwing, so the polling loop is resilient to transient
/// states (loading screens, the player object not yet allocated, etc.).
/// </summary>
public sealed class ProcessMemory : IDisposable
{
    public Process Process { get; }
    private readonly IntPtr _handle;
    public long ModuleBase { get; }
    public int ModuleSize { get; }
    private byte[]? _moduleCache;

    private ProcessMemory(Process process, IntPtr handle)
    {
        Process = process;
        _handle = handle;
        var module = process.MainModule
            ?? throw new InvalidOperationException("Target process has no main module.");
        ModuleBase = module.BaseAddress.ToInt64();
        ModuleSize = module.ModuleMemorySize;
    }

    /// <summary>Attach to the first running process with the given name (no ".exe"), or null.</summary>
    public static ProcessMemory? TryAttach(string processName)
    {
        Process? proc = null;
        foreach (var p in Process.GetProcessesByName(processName))
        {
            if (proc == null && !p.HasExited) proc = p;
            else p.Dispose();
        }
        if (proc == null) return null;

        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccess.VmRead | NativeMethods.ProcessAccess.QueryInformation,
            false, proc.Id);
        if (handle == IntPtr.Zero)
        {
            proc.Dispose();
            return null;
        }

        try
        {
            return new ProcessMemory(proc, handle);
        }
        catch
        {
            NativeMethods.CloseHandle(handle);
            proc.Dispose();
            return null;
        }
    }

    public bool HasExited => Process.HasExited;

    public byte[]? ReadBytes(long address, int length)
    {
        if (address == 0 || length <= 0) return null;
        var buffer = new byte[length];
        if (!NativeMethods.ReadProcessMemory(_handle, (IntPtr)address, buffer, length, out var read)
            || read.ToInt64() != length)
        {
            return null;
        }
        return buffer;
    }

    public long ReadInt64(long address) { var b = ReadBytes(address, 8); return b == null ? 0 : BitConverter.ToInt64(b, 0); }
    public int ReadInt32(long address) { var b = ReadBytes(address, 4); return b == null ? 0 : BitConverter.ToInt32(b, 0); }
    public uint ReadUInt32(long address) { var b = ReadBytes(address, 4); return b == null ? 0u : BitConverter.ToUInt32(b, 0); }
    public float ReadFloat(long address) { var b = ReadBytes(address, 4); return b == null ? 0f : BitConverter.ToSingle(b, 0); }
    public byte ReadByte(long address) { var b = ReadBytes(address, 1); return b == null ? (byte)0 : b[0]; }

    /// <summary>
    /// A snapshot of the main module's bytes, read in chunks so an occasional
    /// unreadable page doesn't abort the whole scan. Cached after first use.
    /// </summary>
    private byte[] ModuleBytes()
    {
        if (_moduleCache != null) return _moduleCache;

        var data = new byte[ModuleSize];
        const int chunk = 0x10000; // 64 KiB
        for (var offset = 0; offset < ModuleSize; offset += chunk)
        {
            var len = Math.Min(chunk, ModuleSize - offset);
            var block = ReadBytes(ModuleBase + offset, len);
            if (block != null) Array.Copy(block, 0, data, offset, len);
            // else: leave zero-filled (unreadable region — patterns won't match there anyway)
        }
        _moduleCache = data;
        return data;
    }

    /// <summary>Absolute address of the first match of <paramref name="pattern"/> in the main module.</summary>
    public long? ScanAob(AobPattern pattern)
    {
        var data = ModuleBytes();
        if (data.Length == 0) return null;
        var idx = pattern.IndexIn(data);
        return idx < 0 ? null : ModuleBase + idx;
    }

    /// <summary>
    /// Resolve a RIP-relative reference. The matched instruction lives at the
    /// scanned address; a 4-byte signed displacement sits at +<paramref name="displacementOffset"/>,
    /// and the instruction is <paramref name="instructionLength"/> bytes long.
    /// The referenced absolute address is (match + instructionLength + displacement).
    /// </summary>
    public long? ScanRelative(AobPattern pattern, int displacementOffset, int instructionLength)
    {
        var match = ScanAob(pattern);
        if (match == null) return null;
        var disp = ReadInt32(match.Value + displacementOffset);
        return match.Value + instructionLength + disp;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) NativeMethods.CloseHandle(_handle);
        Process.Dispose();
    }
}
