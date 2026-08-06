using System.Runtime.InteropServices;

namespace OverlayMod.Engine.Memory;

/// <summary>
/// Minimal P/Invoke surface for reading another process's memory. Read-only:
/// the overlay never writes to the game, which keeps it side-effect free.
/// </summary>
internal static class NativeMethods
{
    [Flags]
    internal enum ProcessAccess : uint
    {
        VmRead = 0x0010,
        QueryInformation = 0x0400,
        QueryLimitedInformation = 0x1000,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(ProcessAccess dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);
}
