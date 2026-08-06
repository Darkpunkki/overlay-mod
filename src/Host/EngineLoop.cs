using System.Text.Json;
using OverlayMod.Engine.GameState;

namespace OverlayMod.Host;

/// <summary>
/// The heartbeat: poll the snapshot source, feed the run tracker, broadcast the
/// projected state. Runs for the lifetime of the host.
/// </summary>
public sealed class EngineLoop : BackgroundService
{
    /// <summary>Don't rescan the process list at the full poll rate while waiting for the game.</summary>
    private static readonly TimeSpan AttachRetryInterval = TimeSpan.FromSeconds(1);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ISnapshotSource _source;
    private readonly RunController _run;
    private readonly StateBroadcaster _bus;
    private readonly OverlayHostOptions _options;
    private readonly ILogger<EngineLoop> _log;

    private DateTime _lastAttachAttempt = DateTime.MinValue;
    private bool _wasAttached;

    public EngineLoop(
        ISnapshotSource source,
        RunController run,
        StateBroadcaster bus,
        OverlayHostOptions options,
        ILogger<EngineLoop> log)
    {
        _source = source;
        _run = run;
        _bus = bus;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / _options.PollHz));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                // A read can fail if the game exits mid-poll. Drop the connection
                // and let the next tick retry rather than killing the host.
                _log.LogWarning(ex, "Poll failed; will retry.");
                _wasAttached = false;
            }
        }
    }

    private void Tick()
    {
        if (!_source.Attached)
        {
            if (_wasAttached)
            {
                _wasAttached = false;
                _log.LogInformation("Lost the game process; waiting for it to come back.");
            }

            if (DateTime.UtcNow - _lastAttachAttempt < AttachRetryInterval)
            {
                Publish(GameSnapshot.Detached);
                return;
            }

            _lastAttachAttempt = DateTime.UtcNow;
            if (!_source.Attach())
            {
                Publish(GameSnapshot.Detached);
                return;
            }

            _wasAttached = true;
            _log.LogInformation("Attached to {Source}.", _source.Description);
        }

        var snapshot = _source.TakeSnapshot();
        _run.Tick(snapshot, _source.Flags, _source.Generation);
        Publish(snapshot);
    }

    private void Publish(GameSnapshot snapshot) =>
        _bus.Publish(JsonSerializer.Serialize(_run.Project(snapshot), Json));
}
