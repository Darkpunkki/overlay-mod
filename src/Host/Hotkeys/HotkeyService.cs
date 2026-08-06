using OverlayMod.Engine.GameState;

namespace OverlayMod.Host.Hotkeys;

/// <summary>
/// Wires the configured hotkeys to the run controls for the lifetime of the host.
///
/// Global hotkeys matter more than they might seem: until the boss-defeat flag
/// ids are confirmed, most splits advance manually, and the alternative to a
/// hotkey is alt-tabbing out of the game mid-run.
/// </summary>
public sealed class HotkeyService : IHostedService
{
    private readonly OverlayHostOptions _options;
    private readonly RunController _run;
    private readonly ISnapshotSource _source;
    private readonly ILogger<HotkeyService> _log;

    // Held as IDisposable so teardown is not itself Windows-only; the concrete
    // listener is only ever constructed on Windows.
    private IDisposable? _listener;

    public HotkeyService(
        OverlayHostOptions options,
        RunController run,
        ISnapshotSource source,
        ILogger<HotkeyService> log)
    {
        _options = options;
        _run = run;
        _source = source;
        _log = log;
    }

    /// <summary>What is actually bound, for the control page to display.</summary>
    public IReadOnlyList<(string Action, string Key, bool Active)> Bindings { get; private set; } =
        Array.Empty<(string, string, bool)>();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = HotkeySettings.Load(_options.HotkeysPath);

        if (!settings.Enabled || _options.NoHotkeys)
        {
            _log.LogInformation("Global hotkeys are disabled.");
            return Task.CompletedTask;
        }

        // RegisterHotKey is Win32. Everywhere else the control page buttons and
        // the HTTP endpoints remain the way to drive a run.
        if (!OperatingSystem.IsWindows())
        {
            _log.LogInformation("Global hotkeys need Windows; use the control page instead.");
            return Task.CompletedTask;
        }

        var wanted = new (string Action, string Text, Action Run)[]
        {
            ("start", settings.Start, () => _run.Start(_source.Attached ? _source.TakeSnapshot() : GameSnapshot.Detached)),
            ("split", settings.Split, _run.Split),
            ("reset", settings.Reset, _run.Reset),
        };

        var bindings = new List<(HotkeyBinding, Action)>();
        var reported = new List<(string, string, bool)>();

        foreach (var (action, text, run) in wanted)
        {
            if (HotkeyBinding.TryParse(text, out var binding))
            {
                bindings.Add((binding, () => Invoke(action, run)));
                reported.Add((action, binding.Text, true));
            }
            else
            {
                _log.LogWarning("Hotkey for {Action} could not be understood: '{Text}'.", action, text);
                reported.Add((action, text, false));
            }
        }

        Bindings = reported;
        if (bindings.Count == 0) return Task.CompletedTask;

        _listener = new HotkeyListener(bindings, (reason, binding) =>
            _log.LogWarning("Hotkey {Key} {Reason}.", binding.Text, reason));

        _log.LogInformation(
            "Hotkeys: {Bindings}",
            string.Join(", ", reported.Where(b => b.Item3).Select(b => $"{b.Item2} {b.Item1}")));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Hotkey callbacks run on the message-pump thread, so a throw would kill the
    /// loop and silently take every hotkey with it.
    /// </summary>
    private void Invoke(string action, Action run)
    {
        try
        {
            run();
            _log.LogInformation("Hotkey: {Action}.", action);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Hotkey {Action} failed.", action);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;
        return Task.CompletedTask;
    }
}
