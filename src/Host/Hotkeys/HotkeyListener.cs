using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OverlayMod.Host.Hotkeys;

/// <summary>
/// Registers system-wide hotkeys so a run can be split without leaving the game.
///
/// Uses <c>RegisterHotKey</c> rather than a low-level keyboard hook. A hook would
/// see every keystroke on the machine, which is both more invasive than this
/// needs to be and the sort of thing that makes anti-virus software nervous;
/// RegisterHotKey only ever delivers the specific combinations asked for.
///
/// Win32 requires that registration and message pumping happen on the same
/// thread, so this owns a dedicated background thread running a message loop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HotkeyListener : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;

    private readonly IReadOnlyList<(HotkeyBinding Binding, Action Action)> _hotkeys;
    private readonly Action<string, HotkeyBinding> _onFailed;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);

    private uint _threadId;
    private bool _disposed;

    public HotkeyListener(
        IReadOnlyList<(HotkeyBinding Binding, Action Action)> hotkeys,
        Action<string, HotkeyBinding> onFailed)
    {
        _hotkeys = hotkeys;
        _onFailed = onFailed;

        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "OverlayMod hotkeys",
        };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void Pump()
    {
        _threadId = GetCurrentThreadId();

        var registered = new List<int>();
        for (var i = 0; i < _hotkeys.Count; i++)
        {
            var (binding, _) = _hotkeys[i];

            // A combination already claimed by another application fails here.
            // That is a normal outcome, not a fatal one: report it and carry on
            // without that one key rather than refusing to start.
            if (RegisterHotKey(IntPtr.Zero, i, binding.Modifiers, binding.VirtualKey)) registered.Add(i);
            else _onFailed(Marshal.GetLastWin32Error() == 1409 ? "already in use" : "could not be registered", binding);
        }

        _ready.Set();

        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message != WmHotkey) continue;

                var id = (int)message.WParam;
                if (id >= 0 && id < _hotkeys.Count) _hotkeys[id].Action();
            }
        }
        finally
        {
            foreach (var id in registered) UnregisterHotKey(IntPtr.Zero, id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    // --- Win32 ---

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
