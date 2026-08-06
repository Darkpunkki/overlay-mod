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
| `RunTracker` state machine | **Working**, unit tested |
| Host, SSE stream, fake source | **Working** (Milestone 3), 17 unit tests pass |
| Overlay page | **Renders real data**; visual design pass is Milestone 4 |

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

## What is left for a working product

Three things define "working" for this project, plus the goals it set for
itself. Measured honestly against each:

### 1. Choose a challenge before starting the game

**Essentially done.** The control page picks route and challenge, the choice
persists, and the host runs happily before the game exists.

- [ ] **Packaging.** Starting it means `dotnet run` from a terminal, which is a
      developer workflow, not a product. Needs a self-contained exe and a tray
      icon. *(Milestone 7)*
- [ ] Route editor, so custom routes do not mean hand-editing JSON. *Optional.*

### 2. Complete visuals, differing per challenge

**Done for No-Hit. Incomplete for the time-based profiles.**

- [ ] **Time profiles have no time comparison.** Any% and All Bosses show a
      split time, but the PB column shows *hits* and `pbIgtMs` is never used, so
      a time-ranked run cannot see whether it is ahead or behind. The PB column
      needs to follow the profile's metric.
- [ ] **No end-of-run state.** A finished run just stops; there is no "run
      complete" treatment and no acknowledgement of a new personal best.
- [ ] Approach-vs-boss breakdown displays correctly but is **always empty in a
      real game** — nothing sets `BossFightActive`. Blocked on the boss-HP
      offset. *(needs the game)*

### 3. Visible in a window, and composited into a recording

**Believed working, never actually verified.**

- [ ] **Nobody has run it in OBS.** Transparency, sizing, and absence of
      scrollbars in a Browser Source are all unconfirmed. This is the Milestone 4
      done-criterion and needs OBS, though not the game.

### 4. The project's other stated goals

- [ ] **Auto-splitting is unproven**, and 17 of 19 bosses have no known flag id,
      so they split manually. *(needs the game — see below)*
- [ ] **Global hotkeys.** Now essential rather than polish: with most splits
      manual, the alternative is alt-tabbing to a browser mid-run, which is
      unusable. *(Milestone 7)*
- [ ] **Export** to `.lss`, CSV and JSON, for LiveSplit parity. *(Milestone 6)*
- [ ] SQLite replacing the JSON store. *Optional — JSON works fine at this
      scale.* *(Milestone 6)*
- [x] Per-split and per-run personal bests, hit and death tracking, run resume,
      route and challenge selection, themes.

### Shortest path to a usable product

Hotkeys and packaging, then one session with the game to fill in the flag ids.
Everything else is refinement.

## Pending live verification

Everything here is written, unit-tested where it can be, and **unproven against
the real game**. Deliberately batched rather than checked one at a time, so a
single session with DS3 running can clear the lot. Nothing in this list blocks
work that does not touch it.

| # | What | How to check | Why it matters |
|---|---|---|---|
| 1 | **Event-flag reads** | Load a save past Iudex Gundyr; the spike's `IudexGundyr` column should read `DEAD` | All auto-splitting depends on it |
| 2 | **IGT persists across a restart** | Note IGT, quit to desktop, relaunch, load in; IGT should resume at or above the noted value | The entire resume-a-run feature rests on this |
| 3 | **IGT at the main menu** | Watch the spike's IGT while sitting at the menu | The tracker assumes menu IGT is meaningless and ignores it; if it reads as a huge or negative value the resume comparison needs a guard |
| 4 | **Boss-defeat flag ids** | `GET /api/flags?ids=13000800,13100800,…` while killing a boss; whichever flips is that boss's id | Only Iudex (`14000800`) and the Nameless King (`13200850`) are known. Every other split in the All Bosses route is manual until this is done |
| 5 | **Player-loaded timing** | Watch when `player` flips true relative to regaining control | Decides whether runs start slightly early, during the fade-in |
| 6 | **Boss HP / boss-fight-active** | Not yet possible — no offset found | Blocks approach-vs-boss attribution entirely |

Checks 1–3 and 5 are observation only and need nothing but the spike running.
Check 4 is what `GET /api/flags?ids=…` exists for — it reads arbitrary event
flags from the live game, so candidate ids can be watched while a boss dies.
Check 6 is a research task, not a verification.

## Open questions

Decisions not yet made. Flagged here rather than silently assumed.

- **Multi-monitor / resolution.** Overlay is authored at a fixed design size and
  scaled by OBS, versus authored responsively. Fixed is simpler and normal for
  stream overlays. *Partly settled:* `?scale=` exists; whether that is enough in
  practice is a question for the first real recording.

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
- **A run starts when the player loads into the world.** Not on a hotkey, not on
  an IGT reset. Being in a level is what "a run has begun" means; menus and
  loading screens are not part of it.
- **Quitting the game does not end a run.** DS3 keeps in-game time in the save,
  so on returning the save's IGT is compared against the last value seen: at or
  ahead of it means the same character continuing and the run resumes; behind it
  means a different or fresh character and a new run starts. A *finished* run is
  always replaced on return — loading back in is the next attempt.
- **The profile decides what is displayed**, not the page. No-Hit shows combined
  hits per split, with no per-split times, no approach/boss breakdown and no
  death counter — a death there is a failed run, not a statistic. Time-based
  profiles show times and deaths. Every metric is still recorded underneath
  regardless of what is shown.
- **Health is never displayed, on any profile.** The game's own UI already shows
  it, so repeating it costs overlay space and viewer attention for nothing. HP is
  still read — hits and deaths are derived from it — but it does not reach the
  view model at all.
- **Personal bests are per-split as well as per-run** ("gold splits"), so the
  overlay can show progress against the best each boss has *ever* been rather
  than only against one best run.
- **Routes are JSON files in `appdata/routes/`**, seeded on first run and meant
  to be hand-edited. Seeding happens only when the directory has no routes at
  all — per-missing-file seeding would resurrect a route the user deliberately
  deleted, leaving no way to be rid of it.
- **Route and challenge are chosen separately.** A route is just an ordered list
  of things to beat; the same boss list can be run as No-Hit or as Any%. The
  route names a default challenge, but the selection overrides it and is
  remembered in `appdata/settings.json`.
- **Changing either abandons the run in progress.** The splits, or the thing
  being measured, have changed — carrying the old numbers forward would be
  meaningless.
- **A run is never automatically failed.** A hit under No-Hit, or a death under
  Deathless, does not end the run — players want to finish an attempt even after
  it stops being a clean one, and the personal-best comparison is what tells them
  how the attempt is going. There is deliberately no "failed" phase.
- **Unknown boss flag ids are left null, not guessed.** A wrong id fails
  silently: it never fires, or fires at the wrong moment. Null means "advance
  this split manually", which is honest and works. Only Iudex Gundyr
  (`14000800`) and the Nameless King (`13200850`) come from a known-good source.

---

## Milestone 3 — Server & live data ✅

**Built. Three deviations from the design below, all deliberate:**

- **No `Ds3SnapshotSource` adapter.** `Ds3Reader` already had exactly the shape
  `ISnapshotSource` needed, so it implements the interface directly. An adapter
  would have been pure indirection.
- **`ISnapshotSource.Generation` was added**, which the original design missed.
  Something has to say "this is a new session, discard the run" — a re-attach to
  the game, or the fake script looping. Without it the demo run never resets.
- **`HostOptions` is named `OverlayHostOptions`**, and `Route` is bound via a
  global using alias in the host `.csproj`. ASP.NET Core ships its own
  `HostOptions` and `Routing.Route`; shadowing framework names invites
  confusion later.

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
| `src/Host/Program.cs` | Minimal API: static files, `/events`, `/api/state`, `/api/run/*` |
| `src/Host/EngineLoop.cs` | `BackgroundService`: poll → tracker → broadcast |
| `src/Host/StateBroadcaster.cs` | Fan-out to connected SSE clients |
| `src/Host/RunController.cs` | Owns run state; serialises loop and HTTP access |
| `src/Host/OverlayState.cs` | The view model and its projection |
| `src/Host/DemoRoute.cs` | Placeholder route until routes load from disk |
| `src/Host/OverlayHostOptions.cs` | Port, poll rate, source selection (`--fake`) |
| `src/Engine/GameState/ISnapshotSource.cs` | The seam |
| `src/Engine/GameState/FakeSnapshotSource.cs` | Scripted run for development |
| `src/Host/wwwroot/overlay/` | `index.html`, `overlay.css`, `overlay.js` |
| `tests/Engine.Tests/FakeSnapshotSourceTests.cs` | Script produces expected run |

### Data contract

The payload is a **view model**, deliberately not `GameSnapshot`. The overlay
should never learn about pointer chains, and the engine should be free to change
internals without breaking the page. Emitted as camelCase JSON on every tick:

```jsonc
{
  "attached": true,
  "phase": "Running",              // NotStarted | Running | Finished
  "routeName": "Demo (first three bosses)",
  "profileName": "No-Hit",
  "runIgtMs": 754320,
  "totalHits": 3,
  "totalDeaths": 0,
  "primary": { "metric": "Hits", "value": 3, "best": 9 },   // per challenge profile
  "display": { "showSplitTimes": false, "showSegmentBreakdown": false, "showDeaths": false },
  "player": { "loaded": true, "loading": false },
  "bests": { "runIgtMs": 92498, "totalHits": 9, "totalDeaths": 1 },
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
      "pbIgtMs": 27493,            // best this split has ever been
      "pbHits": 3
    }
  ]
}
```

Sending the full state every tick — rather than diffs — keeps the client
stateless, makes reconnection free, and is trivially cheap at this size.

### Tasks

- [x] `ISnapshotSource`, implemented directly by `Ds3Reader`
- [x] `FakeSnapshotSource`: scripted run with timed HP drops, a death, and boss
      flag transitions, looping so the UI always has motion
- [x] Host project, referenced from `OverlayMod.sln`
- [x] `EngineLoop` at 30 Hz driving `RunTracker`, with throttled attach retry
- [x] View-model projection + camelCase serialisation
- [x] `GET /events` (SSE) and `GET /api/state` (one-shot, for debugging)
- [x] `POST /api/run/{start,split,reset}` for manual control
- [x] Static file serving for `wwwroot/`
- [x] `--fake` flag; default to real game, fall back gracefully when not attached
- [x] Unit tests for the fake source and the tracker's reading of it

### Done when

`dotnet run --project src/Host -- --fake`, then `localhost:PORT/overlay` in
Chrome shows numbers moving, with DS3 not running.

**Verified.** The stream delivers ~31 events/second, IGT tracks wall time, and
one pass of the demo script produces 9 hits, 1 death and three auto-split boss
kills — matching the unit tests.

---

## Milestone 4 — Overlay UI  ← the headline goal

A transparent-background page that looks correct layered over gameplay, built
entirely against the fake source.

### Files

| Path | Purpose |
|---|---|
| `src/Host/wwwroot/overlay/index.html` | Structure |
| `src/Host/wwwroot/overlay/overlay.css` | Layout + theme via CSS custom properties |
| `src/Host/wwwroot/overlay/overlay.js` | SSE subscribe, render, reconnect |
| `src/Host/wwwroot/overlay/themes/minimal.css` | No plates, text on the capture |
| `src/Host/wwwroot/overlay/themes/light.css` | Dark text on a pale plate |

### Layout

Roughly LiveSplit's vertical stack, since that is what viewers already parse:

- **Run timer** — large, top, IGT.
- **Split list** — name, hits, personal best, and a time column only for
  profiles that want it. Active split carries an accent bar; the list windows
  around it rather than showing all of them.
- **Active split detail** — approach hits versus boss hits. Shown only for
  profiles that ask for it; hidden on No-Hit.
- **Run totals** — the profile's primary metric with its personal best
  alongside, plus deaths where the profile shows them.
- **Status line** — one quiet line when the stream drops or the game is absent,
  not a wall of errors baked into a recording.

Requirements: `body { background: transparent }`, no scrollbars, no reliance on
hover or interaction, and legibility over both bright and dark scenes — text
needs a shadow or plate, since Firelink Shrine and the Ringed City are very
different backdrops.

Sizes are in `rem` against a root font size scaled by `--om-scale`, so
`?scale=1.5` enlarges the overlay proportionally instead of OBS stretching and
blurring a bitmap. `?theme=<name>` loads `themes/<name>.css`; the name is
matched against `^[a-z0-9-]{1,32}$` so a crafted URL cannot pull in a stylesheet
from elsewhere.

### Tasks

- [x] SSE client with automatic reconnect and a stale-data indicator
- [x] Timer formatting matching the engine's conventions (`h:mm:ss.mmm`)
- [x] Split list rendering with windowing around the active split
- [x] Personal-best column, with the live value coloured against it
- [x] Approach/boss hit breakdown, for profiles that show it
- [x] Profile-driven display: split times, breakdown and deaths all conditional
- [x] Theme via CSS custom properties, plus `?theme=` and `?scale=`
- [ ] **Needs a browser:** transparency, no scrollbars, legibility, layout
- [ ] **Needs OBS + the game:** the recording that defines "done"

### Done when

1. OBS scene: Game Capture (or Display Capture) on DS3, Browser Source above it
   pointed at `http://localhost:PORT/overlay`, sized to the canvas.
2. Start recording, play for a minute, stop.
3. **The output file shows the overlay composited over gameplay.**

That recording is the project's original goal met.

---

## Milestone 5 — Auto-splitting for real

Partly done. The parts that needed no game are built:

- [x] **Route files** (`appdata/routes/*.json`), seeded on first run, hot
      reloadable, hand-editable.
- [x] **Challenge profiles** driving what the overlay shows, selectable
      independently of the route and remembered across restarts.
- [x] **Control page** at `/control/` for choosing both, plus manual
      start/split/reset. This is the answer to "where do I decide what I'm
      running".
- [x] **Boss list** for the main game — names and ordering.
- [x] **Flag probe** (`GET /api/flags?ids=…`) so the unknown flag ids can be
      identified in one session rather than guessed.

Remaining, all needing the game:

- [ ] Confirm event-flag reads work at all (pending-verification #1).
- [ ] Identify the boss-defeat flag ids and fill them into the route files
      (#4). Until then most splits in the All Bosses route are manual.
- [ ] Find boss HP / boss-fight-active (#6). The known-good lead: the module-bag
      pattern that worked for player HP (`PlayerIns + ModuleBagOffset → +0x18 →
      +0xD8`) should apply to boss character instances too. Populating
      `GameSnapshot.BossFightActive` brings approach-vs-boss attribution alive.
- [ ] Route editor in the control page, so splits can be reordered without
      editing JSON. Not blocked by the game, just not urgent while the files are
      simple.

## Milestone 6 — Persistence

**Partly done ahead of schedule.** Personal bests were needed for Milestone 4's
overlay to be meaningful, so `JsonRecordStore` was built as a stopgap: finished
runs land in `appdata/records.json`, and per-run and per-split bests are folded
from them. `RunStateStore` separately checkpoints the *in-progress* run so it
survives closing the overlay.

Remaining for this milestone: replace the JSON store with SQLite via
`Microsoft.Data.Sqlite` behind the existing `IRecordStore` interface (the
overlay is unaffected either way), and add export to `.lss` (LiveSplit), CSV
and JSON.

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
