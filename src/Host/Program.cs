using OverlayMod.Engine.GameState;
using OverlayMod.Engine.Persistence;
using OverlayMod.Engine.Tracking;
using OverlayMod.Host;

// The overlay host: polls the game (or a scripted fake), runs the tracker, and
// serves both the overlay page and a live state stream on loopback. OBS points a
// Browser Source at the overlay URL; the same URL works in any browser.

var options = OverlayHostOptions.Parse(args);

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ISnapshotSource>(_ =>
    options.UseFake ? new FakeSnapshotSource() : new Ds3Reader());
builder.Services.AddSingleton<IRecordStore>(_ => new JsonRecordStore(options.RecordsPath));
builder.Services.AddSingleton(_ => new RunStateStore(options.RunStatePath));
builder.Services.AddSingleton(_ => new RouteStore(options.RoutesDirectory));
builder.Services.AddSingleton(_ => new SettingsStore(options.SettingsPath));
builder.Services.AddSingleton<RunController>();
builder.Services.AddSingleton<StateBroadcaster>();
builder.Services.AddHostedService<EngineLoop>();

var app = builder.Build();

app.UseDefaultFiles();  // maps /overlay/ to /overlay/index.html
app.UseStaticFiles();

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

// Read arbitrary event flags. This exists for the live verification session:
// boss-defeat flag ids are mostly unconfirmed, and watching candidates flip
// while killing a boss is how they get confirmed.
app.MapGet("/api/flags", (string ids, ISnapshotSource source) =>
{
    var result = new Dictionary<string, bool>();
    foreach (var part in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        if (uint.TryParse(part, out var id)) result[part] = source.Flags.IsEventFlagSet(id);

    return Results.Ok(new { attached = source.Attached, flags = result });
});

// Manual run control, mirroring LiveSplit's hotkeys. Wired to real hotkeys in
// Milestone 7; for now these make the tracker testable from a browser or curl.
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
Console.WriteLine($"""

    OverlayMod host
      source   : {source.Description}
      running  : {selected.Route.Name} as {selected.Profile.Name}
                 ({selected.Route.AutoSplitCount}/{selected.Route.Splits.Count} splits auto-advance)
      overlay  : {options.OverlayUrl}
      control  : {options.ControlUrl}
      data     : {Path.GetFullPath(options.DataDirectory)}

    Open the control URL to pick a route and challenge.
    Point an OBS Browser Source at the overlay URL. Ctrl+C to stop.

    """);

app.Run();

/// <summary>Body of a route-selection request.</summary>
internal sealed record SelectRequest(string Route, string Challenge);
