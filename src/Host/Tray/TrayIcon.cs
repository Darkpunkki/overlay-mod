using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace OverlayMod.Host.Tray;

/// <summary>
/// A notification-area icon, so the published build is something you launch and
/// forget rather than a console window you must leave open and not accidentally
/// close.
///
/// Runs its own message loop on a dedicated STA thread. The web host owns the
/// main thread via <c>app.Run()</c>, and Windows Forms needs a single-threaded
/// apartment for menus and dialogs to behave, so the two cannot share.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIcon : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);

    private NotifyIcon? _icon;
    private ApplicationContext? _context;
    private IntPtr _iconHandle;
    private bool _disposed;

    private TrayIcon(OverlayHostOptions options, Action onExit)
    {
        _thread = new Thread(() => Run(options, onExit))
        {
            IsBackground = true,
            Name = "OverlayMod tray",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Show the icon. <paramref name="onExit"/> is invoked when the user picks Exit.</summary>
    public static TrayIcon Start(OverlayHostOptions options, Action onExit) => new(options, onExit);

    private void Run(OverlayHostOptions options, Action onExit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open overlay", null, (_, _) => Open(options.OverlayUrl));
        menu.Items.Add("Open control panel", null, (_, _) => Open(options.ControlUrl));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Copy overlay URL for OBS", null, (_, _) => CopyOverlayUrl(options.OverlayUrl));
        menu.Items.Add("Open data folder", null, (_, _) => Open(Path.GetFullPath(options.DataDirectory)));
        menu.Items.Add("Open log file", null, (_, _) => Open(Path.GetFullPath(options.LogPath)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => onExit());

        _icon = new NotifyIcon
        {
            Icon = BuildIcon(out _iconHandle),
            // Tooltips are capped at 63 characters, so this has to stay short.
            Text = $"OverlayMod {BuildInfo.Version} — port {options.Port}",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Double-click is the conventional "show me the thing" gesture.
        _icon.DoubleClick += (_, _) => Open(options.ControlUrl);

        _context = new ApplicationContext();
        _ready.Set();

        Application.Run(_context);

        _icon.Visible = false;
        _icon.Dispose();
        menu.Dispose();
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
    }

    /// <summary>
    /// Draw the icon rather than shipping a binary asset: a dark disc with a gold
    /// ring, echoing the overlay's own palette.
    /// </summary>
    private static Icon BuildIcon(out IntPtr handle)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var disc = new SolidBrush(Color.FromArgb(230, 18, 18, 21));
            g.FillEllipse(disc, 1, 1, 30, 30);

            using var ring = new Pen(Color.FromArgb(255, 224, 182, 92), 3.5f);
            g.DrawEllipse(ring, 8, 8, 16, 16);
        }

        handle = bitmap.GetHicon();

        // Clone so the Icon survives the handle being destroyed on shutdown.
        using var fromHandle = Icon.FromHandle(handle);
        return (Icon)fromHandle.Clone();
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // Nothing registered to open it, or the file is not there yet.
        }
    }

    private void CopyOverlayUrl(string url)
    {
        try
        {
            Clipboard.SetText(url);
            _icon?.ShowBalloonTip(3000, "OverlayMod", "Overlay URL copied — paste it into an OBS Browser Source.", ToolTipIcon.Info);
        }
        catch (ExternalException)
        {
            // Another process had the clipboard locked; not worth reporting.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _context?.ExitThread();
        _thread.Join(TimeSpan.FromSeconds(3));
        _ready.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
