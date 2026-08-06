using System.Reflection;
using Microsoft.Extensions.FileProviders;
using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Tracking;
using OverlayMod.Host;
using OverlayMod.Host.Appearance;
using OverlayMod.Host.Hotkeys;
using OverlayMod.Host.Logging;
using OverlayMod.Host.Tray;

// The overlay host: polls the game (or a scripted fake), runs the tracker, and
// serves both the overlay page and a live state stream on loopback. OBS points a
// Browser Source at the overlay URL; the same URL works in any browser.

var options = OverlayHostOptions.Parse(args);

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
// The published build is windowed and has no console, so the file is the only
// place a failure to attach or a bad route file will ever show up.
builder.Logging.AddProvider(new FileLoggerProvider(options.LogPath));
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ISnapshotSource>(_ =>
    options.UseFake ? new FakeSnapshotSource() : new Ds3Reader());
builder.Services.AddSingleton<IRecordStore>(_ => new JsonRecordStore(options.RecordsPath));
builder.Services.AddSingleton(_ => new RunStateStore(options.RunStatePath));
builder.Services.AddSingleton(_ => new RouteStore(options.RoutesDirectory));
builder.Services.AddSingleton(_ => new SettingsStore(options.SettingsPath));
builder.Services.AddSingleton(_ => new AppearanceStore(options.AppearancePath));
builder.Services.AddSingleton<RunController>();
builder.Services.AddSingleton<StateBroadcaster>();
builder.Services.AddHostedService<EngineLoop>();

// Registered as a singleton as well as a hosted service, so the control page can
// ask what ended up bound.
builder.Services.AddSingleton<HotkeyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HotkeyService>());

var app = builder.Build();

// Pages are embedded in the assembly. During development the physical wwwroot is
// layered in front, so editing a stylesheet needs only a browser refresh; in a
// published build that folder does not exist and the embedded copy serves.
var embedded = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "wwwroot");
var physical = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var developmentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));

IFileProvider pages = embedded;
foreach (var candidate in new[] { physical, developmentRoot })
{
    if (!Directory.Exists(candidate)) continue;
    pages = new CompositeFileProvider(new PhysicalFileProvider(candidate), pages);
    break;
}

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = pages });  // /overlay/ -> /overlay/index.html
app.UseStaticFiles(new StaticFileOptions { FileProvider = pages });

app.MapGet("/", () => Results.Redirect("/overlay/"));

// One-shot state, for debugging and for clients that would rather poll.
app.MapGet("/api/state", (StateBroadcaster bus) => bus.Latest is { } json
    ? Results.Content(json, "application/json")
    : Results.Content("{}", "application/json"));

// The live stream the overlay subscribes to. Server-Sent Events: one-way,
// reconnects on its own, and far less machinery than a WebSocket would need.
app.MapGet("/events", async (HttpContext ctx, StateBroadcaster bus, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    ctx.Response.Headers["X-Accel-Buffering"] = "no"; // defeat proxy buffering

    var (id, reader) = bus.Subscribe();
    try
    {
        // Render immediately rather than waiting for the next poll tick.
        if (bus.Latest is { } latest) await Send(ctx.Response, latest, ct);

        await foreach (var json in reader.ReadAllAsync(ct))
            await Send(ctx.Response, json, ct);
    }
    catch (OperationCanceledException)
    {
        // Client navigated away or OBS closed the source. Normal.
    }
    finally
    {
        bus.Unsubscribe(id);
    }

    static async Task Send(HttpResponse response, string json, CancellationToken ct)
    {
        // SSE framing. The payload is single-line JSON, so no escaping is needed.
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
});

// What can be run, and what is currently selected. The control page uses these;
// they are also the answer to "how do I pick a challenge" before there is a
// route editor.
app.MapGet("/api/routes", (RouteStore routes, RunController rc) =>
{
    var (currentRoute, currentProfile) = rc.Current;
    return Results.Ok(new
    {
        selected = new { route = currentRoute.Name, challenge = currentProfile.Type.ToString() },
        challenges = ChallengeProfile.All.Select(p => new { type = p.Type.ToString(), name = p.Name }),
        routes = routes.All.Select(r => new
        {
            name = r.Name,
            defaultChallenge = r.DefaultChallenge.ToString(),
            splits = r.Splits.Count,
            autoSplits = r.AutoSplitCount,
            flagsVerified = r.FlagsVerified,
        }),
    });
});

app.MapPost("/api/routes/select", (SelectRequest body, RunController rc) =>
{
    if (!Enum.TryParse<ChallengeType>(body.Challenge, ignoreCase: true, out var challenge))
        return Results.BadRequest(new { error = $"Unknown challenge '{body.Challenge}'." });

    return rc.Select(body.Route, challenge)
        ? Results.Ok(new { selected = true })
        : Results.NotFound(new { error = $"No route named '{body.Route}'." });
});

// Re-read the routes directory, so hand-edited route files can be picked up
// without restarting the host.
app.MapPost("/api/routes/reload", (RouteStore routes) =>
{
    routes.Reload();
    return Results.Ok(new { routes = routes.All.Count });
});

// Write any built-in route that is missing. Routes are only seeded into an empty
// directory, so this is how a newly added built-in reaches an existing install -
// and how to undo deleting one by mistake.
app.MapPost("/api/routes/restore", (RouteStore routes) =>
{
    var added = routes.RestoreBuiltIns();
    return Results.Ok(new { added, routes = routes.All.Count });
});

// Read arbitrary event flags. This exists for the live verification session:
// watching candidates flip while killing a boss is how they get confirmed.
app.MapGet("/api/flags", (string ids, ISnapshotSource source) =>
{
    var result = new Dictionary<string, bool>();
    foreach (var part in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        if (uint.TryParse(part, out var id)) result[part] = source.Flags.IsEventFlagSet(id);

    return Results.Ok(new { attached = source.Attached, flags = result });
});

// Every intermediate value from a flag lookup, plus the resolved statics. When a
// boss dies and the split does not advance, this says which pointer hop broke
// rather than leaving it to guesswork.
app.MapGet("/api/diagnostics", (ISnapshotSource source, uint? flag) =>
{
    if (source is not Ds3Reader reader)
        return Results.Ok(new { note = "diagnostics need the live game reader", source = source.Description });

    var snapshot = reader.TakeSnapshot();
    return Results.Ok(new
    {
        source = reader.Description,
        version = reader.Version.ToString(),
        attached = reader.Attached,
        snapshot = new
        {
            snapshot.IgtMs,
            snapshot.IsLoading,
            snapshot.PlayerLoaded,
            snapshot.Hp,
            snapshot.MaxHp,
        },
        pointers = new
        {
            worldChrMan = $"0x{reader.WorldChrMan:X}",
            playerIns = $"0x{reader.PlayerInsAddress:X}",
        },
        flag = reader.DiagnoseFlag(flag ?? 14000800),
    });
});

// What the global hotkeys ended up bound to, so the control page can show them
// rather than the user having to guess or open the config file.
app.MapGet("/api/hotkeys", (HotkeyService hotkeys) => Results.Ok(new
{
    bindings = hotkeys.Bindings.Select(b => new { action = b.Action, key = b.Key, active = b.Active }),
}));

// How the overlay looks. The overlay refetches this whenever the version in the
// state stream moves, so edits from the control page land in OBS immediately.
app.MapGet("/api/appearance", (AppearanceStore store) =>
    Results.Ok(new { version = store.Version, settings = store.Current }));

app.MapPost("/api/appearance", (AppearanceSettings settings, AppearanceStore store) =>
{
    var applied = store.Update(settings);
    return Results.Ok(new { version = store.Version, settings = applied });
});

app.MapPost("/api/appearance/reset", (AppearanceStore store) =>
{
    var applied = store.Reset();
    return Results.Ok(new { version = store.Version, settings = applied });
});

// Shut the host down. The tray icon can do this too, but it hides behind the
// notification-area overflow arrow, and a windowed process ignores Ctrl+C — so
// without this the only way out is Task Manager.
app.MapPost("/api/quit", (IHostApplicationLifetime lifetime, ILogger<Program> log) =>
{
    log.LogInformation("Shutdown requested from the control page.");
    lifetime.StopApplication();
    return Results.Ok(new { stopping = true });
});

// Manual run control, mirroring LiveSplit's hotkeys. The same actions the global
// hotkeys trigger, for when a browser is more convenient.
var run = app.MapGroup("/api/run");
run.MapPost("/start", (RunController rc, ISnapshotSource src) =>
{
    rc.Start(src.Attached ? src.TakeSnapshot() : GameSnapshot.Detached);
    return Results.Ok(new { started = true });
});
run.MapPost("/split", (RunController rc) => { rc.Split(); return Results.Ok(new { split = true }); });
run.MapPost("/reset", (RunController rc) => { rc.Reset(); return Results.Ok(new { reset = true }); });

var source = app.Services.GetRequiredService<ISnapshotSource>();
var selected = app.Services.GetRequiredService<RunController>().Current;

var banner = $"""

    OverlayMod host
      source   : {source.Description}
      running  : {selected.Route.Name} as {selected.Profile.Name}
                 ({selected.Route.AutoSplitCount}/{selected.Route.Splits.Count} splits auto-advance)
      overlay  : {options.OverlayUrl}
      control  : {options.ControlUrl}
      data     : {Path.GetFullPath(options.DataDirectory)}
      log      : {Path.GetFullPath(options.LogPath)}

    Open the control URL to pick a route and challenge.
    Point an OBS Browser Source at the overlay URL.

    """;

Console.WriteLine(banner);
app.Services.GetRequiredService<ILogger<Program>>().LogInformation("Started. {Banner}", banner);

// The tray icon owns its own thread; the web host keeps this one. Exit from the
// tray asks the host to stop, which lets app.Run() return and unwind normally.
TrayIcon? tray = null;
if (!options.NoTray && OperatingSystem.IsWindows())
{
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    tray = TrayIcon.Start(options, () => lifetime.StopApplication());
}

try
{
    app.Run();
}
finally
{
    tray?.Dispose();
}

/// <summary>Body of a route-selection request.</summary>
internal sealed record SelectRequest(string Route, string Challenge);
