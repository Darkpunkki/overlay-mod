# OverlayMod — implementation plan

The README's milestone list says *what* gets built and in what order. This
document says *how*: concrete designs, file layouts, data contracts and task
checklists for the milestones close enough to plan honestly.

**Keep this in the repo.** An earlier, more detailed plan lived in a tool
directory outside version control and was lost; that is why this one is
committed alongside the code.

Detail is deliberately front-loaded. Milestones 3 and 4 are specified to the
level of "which file, which type, which field". Milestones 5–7 are sketches —
writing them out in the same detail now would be invention, not planning, and
they will be filled in as each becomes next.

---

## Where we actually are

| Area | State |
|---|---|
| Memory access (attach, AOB scan, pointer chains) | **Working, verified live** |
| IGT / loading / player-loaded / position | **Working, verified live** |
| Player HP / MaxHP | **Working, verified live** (2026-06-30) |
| Event-flag reads (boss defeats) | **Implemented, never verified against the game** |
| Boss HP / boss-fight-active | **Not found.** No offset, no candidate |
| `RunTracker` state machine | **Working**, 10 unit tests pass |
| Anything on screen | **Does not exist yet** |

Two consequences worth internalising:

1. `GameSnapshot.BossFightActive` is always `false` today. The tracker reads it
   and attributes correctly, but nothing sets it, so **every hit currently
   counts as "approach"**. The approach-vs-boss split — the project's headline
   feature — is wired end to end but starved of input until Milestone 5.
2. Auto-splitting depends on event-flag reads that have never been confirmed to
   work. Until someone loads a save past Iudex Gundyr and sees the spike print
   `DEAD`, treat auto-splitting as unproven, not as done.

Neither blocks Milestones 3 or 4.

---

## Open questions

Decisions not yet made. Flagged here rather than silently assumed.

- **What starts a run?** A hotkey, an IGT reset (new game), or entering the
  first split's area? LiveSplit parity suggests a hotkey; a DS3 auto-splitter
  suggests IGT. Probably both, with the hotkey as the override. *Needed by M4.*
- **What resets a run?** Death is not a reset in a Deathless profile — it is a
  run-ending failure. The tracker has no "failed" phase yet. *Needed by M5.*
- **Where do routes live?** JSON files on disk under `appdata/routes/` is the
  assumption, editable by hand before the editor exists. *Needed by M5.*
- **Multi-monitor / resolution.** Overlay is authored at a fixed design size and
  scaled by OBS, versus authored responsively. Fixed is simpler and normal for
  stream overlays. *Needed by M4.*

## Decisions already made

- **Transport: Server-Sent Events**, not WebSocket. The data flow is one-way
  (engine → overlay), SSE reconnects automatically, and it is far less code on
  both ends. Revisit only if the overlay ever needs to talk back.
- **Display: OBS Browser Source over game capture.** The deliverable is a
  recorded file with the overlay composited in. Nothing is injected into the
  game and the overlay does not appear on the player's own monitor. An
  always-on-top desktop window is an optional extra, not a requirement.
- **Read-only.** The engine never writes to game memory. This is a stated
  property of the project, not an implementation detail.

---

## Milestone 3 — Server & live data

Get tracker state out of the process and onto a URL, and make the whole thing
runnable **without Dark Souls III**, so the UI can be built at any hour without
launching a game.

### Design

Introduce a snapshot *source* seam so the poll loop does not care whether values
come from the game or a script:

```
ISnapshotSource ──┬── Ds3SnapshotSource    (wraps the existing Ds3Reader)
                  └── FakeSnapshotSource   (scripted run: HP drops, boss flags,
                                            deaths — deterministic, no game)
```

A hosted service polls the source at a fixed rate, feeds each snapshot to
`RunTracker`, projects the result into a view model, and pushes it to every
connected SSE client. The overlay page is served as a static file from the same
host, so OBS points at one URL and nothing needs CORS.

### New files

| Path | Purpose |
|---|---|
| `src/Host/OverlayMod.Host.csproj` | ASP.NET Core (`Microsoft.NET.Sdk.Web`), net8.0 |
| `src/Host/Program.cs` | Minimal API: static files, `/events`, `/api/state` |
| `src/Host/EngineLoop.cs` | `BackgroundService`: poll → tracker → broadcast |
| `src/Host/StateBroadcaster.cs` | Fan-out to connected SSE clients |
| `src/Host/HostOptions.cs` | Port, poll rate, source selection (`--fake`) |
| `src/Engine/GameState/ISnapshotSource.cs` | The seam |
| `src/Engine/GameState/Ds3SnapshotSource.cs` | Adapter over `Ds3Reader` |
| `src/Engine/GameState/FakeSnapshotSource.cs` | Scripted run for development |
| `src/Host/wwwroot/overlay/index.html` | Placeholder page, fleshed out in M4 |
| `tests/Engine.Tests/FakeSnapshotSourceTests.cs` | Script produces expected run |

### Data contract

The payload is a **view model**, deliberately not `GameSnapshot`. The overlay
should never learn about pointer chains, and the engine should be free to change
internals without breaking the page. Emitted as camelCase JSON on every tick:

```jsonc
{
  "attached": true,
  "phase": "Running",              // NotStarted | Running | Finished
  "runIgtMs": 754320,
  "totalHits": 3,
  "totalDeaths": 0,
  "primary": { "metric": "Hits", "value": 3 },   // per challenge profile
  "player": { "hp": 412, "maxHp": 1050, "loaded": true, "loading": false },
  "bossFightActive": false,
  "activeIndex": 2,
  "splits": [
    {
      "name": "Iudex Gundyr",
      "isBoss": true,
      "completed": true,
      "igtMs": 180000,
      "hits": 1,
      "deaths": 0,
      "approach": { "igtMs": 120000, "hits": 0, "deaths": 0 },
      "boss":     { "igtMs":  60000, "hits": 1, "deaths": 0 },
      "pbIgtMs": null,             // null until Milestone 6
      "pbHits": null
    }
  ]
}
```

Sending the full state every tick — rather than diffs — keeps the client
stateless, makes reconnection free, and is trivially cheap at this size.

### Tasks

- [ ] `ISnapshotSource` + adapt `Ds3Reader` behind it (no behaviour change)
- [ ] `FakeSnapshotSource`: scripted run with timed HP drops, a death, and boss
      flag transitions, looping so the UI always has motion
- [ ] Host project, referenced from `OverlayMod.sln`
- [ ] `EngineLoop` at ~30 Hz driving `RunTracker`
- [ ] View-model projection + camelCase serialisation
- [ ] `GET /events` (SSE) and `GET /api/state` (one-shot, for debugging)
- [ ] Static file serving for `wwwroot/`
- [ ] `--fake` flag; default to real game, fall back gracefully when not attached
- [ ] Unit tests for the fake source and the projection

### Done when

`dotnet run --project src/Host -- --fake`, then `localhost:PORT/overlay` in
Chrome shows numbers moving, with DS3 not running.

---

## Milestone 4 — Overlay UI  ← the headline goal

A transparent-background page that looks correct layered over gameplay, built
entirely against the fake source.

### New files

| Path | Purpose |
|---|---|
| `src/Host/wwwroot/overlay/index.html` | Structure |
| `src/Host/wwwroot/overlay/overlay.css` | Layout + theme via CSS custom properties |
| `src/Host/wwwroot/overlay/overlay.js` | SSE subscribe, render, reconnect |
| `src/Host/wwwroot/overlay/themes/*.css` | Alternate looks, opt-in by query string |

### Layout

Roughly LiveSplit's vertical stack, since that is what viewers already parse:

- **Run timer** — large, top or bottom, IGT.
- **Split list** — name, time, completed/active/pending states. Active split
  highlighted; the list windows around it rather than showing all of them.
- **Active split detail** — the differentiator. Approach hits versus boss hits,
  shown separately, with deaths.
- **Run totals** — hits, deaths, and whichever metric the profile ranks by.
- **Disconnected state** — a quiet indicator when `attached` is false, not a
  wall of errors baked into a recording.

Requirements: `body { background: transparent }`, no scrollbars, no reliance on
hover or interaction, and legibility over both bright and dark scenes — text
needs a shadow or plate, since Firelink Shrine and the Ringed City are very
different backdrops.

### Tasks

- [ ] SSE client with automatic reconnect and a stale-data indicator
- [ ] Timer formatting shared with the engine's conventions (`h:mm:ss.mmm`)
- [ ] Split list rendering with windowing around the active split
- [ ] Approach/boss hit breakdown for the active split
- [ ] Theme via CSS custom properties; one alternate theme to prove it works
- [ ] Transparency and no-scrollbar verification in an actual Browser Source

### Done when

1. OBS scene: Game Capture (or Display Capture) on DS3, Browser Source above it
   pointed at `http://localhost:PORT/overlay`, sized to the canvas.
2. Start recording, play for a minute, stop.
3. **The output file shows the overlay composited over gameplay.**

That recording is the project's original goal met.

---

## Milestone 5 — Auto-splitting for real

Sketch only; expand when it becomes next.

- Verify event-flag reads live (load a save past Iudex, confirm `DEAD`). This is
  a two-minute check that has been outstanding for a while and gates everything
  else here.
- Find boss HP / boss-fight-active. The known-good lead: the module-bag pattern
  that worked for player HP (`PlayerIns + ModuleBagOffset → +0x18 → +0xD8`)
  should apply to boss character instances too. Populate
  `GameSnapshot.BossFightActive` and the approach/boss split comes alive.
- DS3 boss and area database: names, defeat flag ids, ordering.
- Route files (JSON) plus the challenge profiles that decide what splits show.
- Resolve the "what starts/resets a run" question above.

## Milestone 6 — Persistence

SQLite via `Microsoft.Data.Sqlite`. Completed runs, per-split bests, gold
splits, personal-best comparison feeding the `pbIgtMs`/`pbHits` fields already
reserved in the contract. Export to `.lss` (LiveSplit), CSV and JSON.

## Milestone 7 — Packaging

Tray host so it is not a console window, global hotkeys (start/split/reset),
theme selection, self-contained single-file publish, first-run setup that
explains the offline/EAC requirement.

## Optional — desktop overlay window

Any time after Milestone 4. A borderless, click-through, always-on-top WebView2
window pointed at the same URL, giving live feedback while practising. Reuses
the entire web UI. Known wrinkle: WebView2 renders into a child HWND, so
click-through needs the extended window styles applied to the child too, not
just the top-level window.
