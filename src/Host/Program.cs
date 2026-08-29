using System.Reflection;
using Microsoft.Extensions.FileProviders;
using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Routes;
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
builder.Services.AddSingleton(_ => new TrackingSettingsStore(options.TrackingPath));
builder.Services.AddSingleton(_ => new AppearanceStore(options.AppearancePath));
builder.Services.AddSingleton(_ => new AttemptStore(options.AttemptsPath));
builder.Services.AddSingleton(_ => new SplitNameStore(options.SplitNamesPath));
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
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = pages,
    // Never let a client hold onto these.
    //
    // The page is markup and script that have to agree with each other: the
    // script looks up elements by id, and an update that renames one breaks the
    // pairing. A cache that serves a stale half of the pair produces a page that
    // loads, throws on the first frame, and then sits there — which reads as
    // "the overlay is frozen", not as "the overlay is broken", and hides the
    // cause completely.
    //
    // OBS's browser source keeps its own cache, separate from any browser and
    // not cleared by restarting OBS, so this is not hypothetical: the same
    // machine can show a working overlay in a browser tab and a stale one in
    // the recording. Without a Cache-Control header a client is free to guess
    // how long these stay fresh, and CEF guesses generously.
    //
    // "no-cache" means revalidate, not "don't store" — the ETag still makes
    // that a 304 in the ordinary case, and these files total a few KB over
    // loopback either way.
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
    },
});

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
    var attempts = rc.Attempts;

    return Results.Ok(new
    {
        selected = new { route = currentRoute.Name, challenge = currentProfile.Type.ToString() },
        attempts = new { started = attempts.Started, finished = attempts.Finished },
        challenges = ChallengeProfile.All.Select(p => new { type = p.Type.ToString(), name = p.Name }),

        // Full split lists rather than counts, because the editor needs them and
        // a second request per route to fetch what this endpoint already has open
        // in memory would be pure ceremony. Five routes of twenty-six splits is a
        // few kilobytes.
        routes = routes.All.Select(r => new
        {
            name = r.Name,
            defaultChallenge = r.DefaultChallenge.ToString(),
            splitCount = r.Splits.Count,
            autoSplits = r.AutoSplitCount,
            flagsVerified = r.FlagsVerified,
            splits = r.Splits.Select(s => new { name = s.Name, isBoss = s.IsBoss, defeatFlagId = s.DefeatFlagId }),
        }),

        // Everything the editor can add as an auto-advancing split, with the flag
        // id attached. Typing a boss's name instead produces a manual split,
        // which is a fine thing to want but should not happen by accident.
        catalogue = BuiltInRoutes.Catalogue.Select(s => new
        {
            name = s.Name,
            isBoss = s.IsBoss,
            defeatFlagId = s.DefeatFlagId,
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
app.MapPost("/api/routes/reload", (RouteStore routes, RunController rc) =>
{
    routes.Reload();
    rc.RoutesChanged();
    return Results.Ok(new { routes = routes.All.Count });
});

// Write any built-in route that is missing. Routes are only seeded into an empty
// directory, so this is how a newly added built-in reaches an existing install -
// and how to undo deleting one by mistake.
app.MapPost("/api/routes/restore", (RouteStore routes, RunController rc) =>
{
    var added = routes.RestoreBuiltIns();
    if (added > 0) rc.RoutesChanged();
    return Results.Ok(new { added, routes = routes.All.Count });
});

// Write a route from the editor. `replacing` names the route this is an edit of,
// which is how a rename is told from a new route that happens to collide with an
// existing name.
app.MapPost("/api/routes/save", (SaveRouteRequest body, RouteStore routes, RunController rc) =>
{
    // The default challenge is a hint - the selection overrides it - so an
    // unrecognised one falls back rather than refusing the save, matching how
    // challenge names are read everywhere else.
    if (!Enum.TryParse<ChallengeType>(body.Challenge, ignoreCase: true, out var challenge))
        challenge = ChallengeType.NoDamage;

    var splits = new List<RouteSplitFile>();
    foreach (var s in body.Splits ?? Array.Empty<SaveSplitRequest>())
        splits.Add(new RouteSplitFile(s.Name ?? "", s.IsBoss, s.DefeatFlagId));

    // Whether this edits the route currently being run. Read before the save,
    // because after a rename the old name no longer matches anything.
    var wasSelected = body.Replacing is not null
        && string.Equals(rc.Current.Route.Name, body.Replacing, StringComparison.OrdinalIgnoreCase);

    var result = routes.Save(new RouteFile(body.Name ?? "", challenge, splits), body.Replacing);
    if (!result.Saved) return Results.BadRequest(new { error = result.Error });

    // Follow the route through a rename, so renaming what you are running does
    // not drop the selection back to the default. Saving any *other* route
    // leaves the selection alone: creating a route is not choosing to run it.
    rc.RoutesChanged(wasSelected ? result.Name : null);
    return Results.Ok(new { saved = true, name = result.Name, routes = routes.All.Count });
});

app.MapPost("/api/routes/delete", (DeleteRouteRequest body, RouteStore routes, RunController rc) =>
{
    if (!routes.Delete(body.Name ?? ""))
        return Results.NotFound(new { error = $"There is no route file for '{body.Name}'." });

    rc.RoutesChanged();
    return Results.Ok(new { deleted = true, routes = routes.All.Count });
});

// What each split is called on the overlay. A view over the routes, never written
// into them: personal bests are keyed on the name in the route file, so renaming
// here cannot orphan the history behind a boss.
app.MapGet("/api/names", (SplitNameStore names) => Results.Ok(new { names = names.All }));

app.MapPost("/api/names", (NamesRequest body, SplitNameStore names) =>
    Results.Ok(new { names = names.Update(body.Names) }));

// Fill in the short form of every boss this build knows about.
app.MapPost("/api/names/short", (SplitNameStore names) =>
    Results.Ok(new { names = names.ApplyShortNames() }));

app.MapPost("/api/names/reset", (SplitNameStore names) =>
    Results.Ok(new { names = names.Clear() }));

// The attempt count for whatever is selected. Writable, because nobody starts
// using this on their first attempt.
app.MapPost("/api/attempts", (AttemptsRequest body, RunController rc) =>
{
    var applied = rc.SetAttempts(body.Started, body.Finished);
    return Results.Ok(new { attempts = new { started = applied.Started, finished = applied.Finished } });
});

app.MapPost("/api/attempts/reset", (RunController rc) =>
{
    var applied = rc.SetAttempts(0, 0);
    return Results.Ok(new { attempts = new { started = applied.Started, finished = applied.Finished } });
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

// How damage is classified. Both detectors are heuristics — one over player
// height, one over the size and rhythm of repeated small drops — so their
// thresholds are settings rather than constants. The only way to know whether
// they are right is to watch them against a real playthrough.
//
// Either half of the body may be omitted, meaning "leave that one as it is".
app.MapGet("/api/tracking", (TrackingSettingsStore store) =>
    Results.Ok(new { fallDamage = store.FallDamage, damageOverTime = store.DamageOverTime }));

app.MapPost("/api/tracking", (TrackingUpdate body, TrackingSettingsStore store) =>
{
    var (fall, overTime) = store.Update(body.FallDamage, body.DamageOverTime);
    return Results.Ok(new { fallDamage = fall, damageOverTime = overTime });
});

app.MapPost("/api/tracking/reset", (TrackingSettingsStore store) =>
{
    var (fall, overTime) = store.Reset();
    return Results.Ok(new { fallDamage = fall, damageOverTime = overTime });
});

// The recent damage events, with the size and the descent measured for each, so
// the detectors' calls can be reviewed rather than taken on trust. This is the
// counterpart to the thresholds above: change one, play, read this back.
app.MapGet("/api/hits", (RunController rc) => Results.Ok(new
{
    // The tick ceiling is a percentage, and the events below are in health. Both
    // are needed to tell "the setting is wrong" from "the detector is wrong".
    healthScale = rc.TickScale.HealthScale,
    tickCeiling = rc.TickScale.TickCeiling,
    events = rc.RecentDamage.Select(e => new
    {
        igtMs = e.IgtMs,
        split = e.SplitName,
        hp = e.Hp,
        maxHp = e.MaxHp,
        damage = e.Damage,
        fatal = e.Fatal,
        descentMetres = e.DescentMetres,
        kind = e.Kind.ToString(),
        countedAsFall = e.CountedAsFall,
    }),
}));

// Which build this is. A log without a version number turns every bug report
// into a round trip.
app.MapGet("/api/about", (ISnapshotSource source) => Results.Ok(new
{
    version = OverlayMod.Host.BuildInfo.Version,
    source = source.Description,
    dataDirectory = Path.GetFullPath(options.DataDirectory),
}));

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

// Manual hit corrections, for when a detector called a real hit something else
// — or a hit never registered at all. Hits only: damage is measured, not
// guessed, so there is nothing there to correct.
run.MapPost("/hits", (AdjustHitsRequest body, RunController rc) =>
    rc.AdjustHits(body.SplitIndex, body.Delta)
        ? Results.Ok(new { adjusted = true })
        : Results.BadRequest(new
        {
            error = "Only a split the run in progress has reached can be corrected, and hits cannot go below zero.",
        }));

var source = app.Services.GetRequiredService<ISnapshotSource>();
var selected = app.Services.GetRequiredService<RunController>().Current;

var banner = $"""

    OverlayMod {BuildInfo.Version}
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

/// <summary>
/// A route as the editor sends it. <paramref name="Replacing"/> is the name the
/// route had before this edit — absent when creating one, equal to
/// <paramref name="Name"/> when editing in place, and different when renaming.
/// </summary>
internal sealed record SaveRouteRequest(
    string? Replacing,
    string? Name,
    string? Challenge,
    IReadOnlyList<SaveSplitRequest>? Splits);

internal sealed record SaveSplitRequest(string? Name, bool IsBoss, uint? DefeatFlagId);

internal sealed record DeleteRouteRequest(string? Name);

/// <summary>A full replacement of the display-name map; null clears it.</summary>
internal sealed record NamesRequest(Dictionary<string, string>? Names);

internal sealed record AttemptsRequest(int Started, int Finished);

/// <summary>A manual hit correction: which split, and by how much (either sign).</summary>
internal sealed record AdjustHitsRequest(int SplitIndex, int Delta);

/// <summary>
/// Body of a tracking-settings update. Both halves are optional: the control
/// page edits one card at a time, and sending only the card that changed keeps
/// two people with the page open from overwriting each other's other card.
/// </summary>
internal sealed record TrackingUpdate(
    FallDamageOptions? FallDamage,
    DamageOverTimeOptions? DamageOverTime);
