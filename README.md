# OverlayMod — Dark Souls III run tracker & overlay

A standalone, streamer-friendly overlay for Dark Souls III speed/challenge runs.
It aims to match LiveSplit + auto-splitter (run timer, auto-detected splits,
personal bests, save/export) **and** add first-class **hit and death tracking**,
attributed per split and split into *approach* vs *boss* phases.

A run uses a **challenge profile** (No-Hit, Deathless, Any%, All-Bosses) that
decides what each split shows. For a No-Hit run, the active boss split shows
hits taken in the approach, hits taken from the boss, and the personal best for
that split — alongside a whole-run PB for the best total attempt.

> Status: **early development.** Milestones 1–4 are built: the memory-reading
> foundation, the run-tracking state machine, the localhost host and live state
> stream, and the overlay itself. Auto-splitting works against a scripted
> replay but has not been verified against the real game. See
> [the plan](#milestones).

## ⚠️ Offline / anti-cheat

This tool reads the game's memory. **Only run it offline, with Easy Anti-Cheat
(EAC) disabled** — the standard setup for DS3 speedrunning and practice tools.
Reading game memory while connected online can get your account soft-banned.
Use the community offline launcher / `-noeac` method (Steam must be running;
offline mode is fine). The overlay never writes to the game; it is read-only.

## How it's displayed

The target is **a recorded video with the overlay composited on top**. The
engine hosts a small localhost server; the overlay is a transparent-background
web page that OBS renders as a **Browser Source** layered over your game
capture. OBS records that composite to a file exactly as it would stream it, so
the overlay lands in the recording without ever touching the game's rendering.

Nothing is injected into the game to make this work, and the overlay does *not*
appear on your own monitor while you play — it lives in the capture. (A
transparent always-on-top desktop window showing the same page, for live
feedback while practising, is an optional extra; it reuses the same web UI and
is not on the critical path.)

## Tech

- **Engine** (`src/Engine`) — C# / .NET 8. Reads DS3 memory (process attach, AOB
  signature scanning, RIP-relative + multi-level pointer resolution). MIT.
- **Tracking** (`src/Engine/Tracking`) — `RunTracker`, the pure run-state machine:
  IGT-delta timing, hits from HP drops, deaths, split advancement. No memory access,
  so it is fully testable from synthetic snapshots.
- **Persistence** (`src/Engine/Persistence`) — finished runs and personal bests,
  plus a checkpoint of the run in progress. JSON for now; SQLite in Milestone 6.
- **Host** (`src/Host`) — ASP.NET Core. Polls the game, runs the tracker, serves
  the overlay and streams state over Server-Sent Events on loopback.
- **Spike** (`src/Spike`) — Milestone 1 console app that streams live values.
- Overlay and control pages are embedded in the assembly, so a published build
  is a single executable rather than an exe plus a folder of loose assets.
- **Tests** (`tests/Engine.Tests`) — tracker logic, resume behaviour, personal
  bests, all driven from synthetic snapshots.

## Where the overlay actually appears

The engine serves a web page on loopback. That page can be shown in three
places, and you choose which by where you point something at the URL:

| Where | How | Works today |
|---|---|---|
| **In your recording / stream** | OBS **Browser Source** layered over your game capture | ✅ the intended use |
| **In a browser window** | Just open the URL on a second monitor | ✅ |
| **On top of the game while you play** | Would need a transparent always-on-top window | ❌ not built — optional extra |

It does **not** draw over the game itself. Nothing is injected into Dark Souls
III; the overlay is a separate page that OBS composites on top when recording.
If you want it on your own screen while playing, put it in a browser window on a
second monitor, or wait for the optional desktop-window mode.

## Getting it

Build a standalone executable — one file, no .NET install needed on the machine
that runs it:

```powershell
./scripts/publish.ps1
```

That produces `publish/OverlayMod.exe` (~180 MB, because the runtime is bundled).
Run it and OverlayMod appears in the notification area; right-click for the
overlay and control-panel links, the data folder, and the log. Routes, history
and settings are written to an `appdata` folder beside the executable.

For development, `dotnet run --project src/Host` keeps a console and prints its
logs there instead.

> **First time testing against the real game?** Follow
> **[docs/LIVE-TESTING.md](docs/LIVE-TESTING.md)** — it covers launching offline
> without EAC, and a checklist of what has never been confirmed on real hardware.

## Playing a tracked run

1. **Launch Dark Souls III offline with EAC disabled.** Non-negotiable — see the
   warning above. Steam can be running; Steam's offline mode is fine.
2. **Start OverlayMod** (`OverlayMod.exe`, or `dotnet run --project src/Host`).
   Order does not matter. It retries attaching once a second and picks the game
   up whenever it appears.
3. **Pick what you're running** at <http://127.0.0.1:8777/control/> — the
   challenge (No-Hit, Deathless, Any%, All Bosses) and the route. The choice is
   remembered for next time.
4. **OBS is only needed if you want it in a recording.** Add a Browser Source
   pointing at <http://127.0.0.1:8777/overlay/> at about **500×420**, and drag it
   above your game capture in the Sources list — the topmost source draws in
   front. To just watch it yourself, open that URL in a browser instead.

   The split list shows a fixed number of rows and scrolls through the route as
   you go, so a 25-boss route needs no more height than a 3-boss one. Use
   `?splits=10` for a taller list.
5. **Play.** The run starts when you load into the world. Quitting the game
   pauses the timer; loading back in resumes the same run.

### Hotkeys

Global, so they work without leaving the game. Change them in
`appdata/hotkeys.json` and restart the host, or turn them off with
`--no-hotkeys`. The control page shows what is actually bound.

| Default | Action |
|---|---|
| `Ctrl+Alt+S` | Start a run (overrides the automatic start) |
| `Ctrl+Alt+D` | Split — needed for bosses with no known defeat flag |
| `Ctrl+Alt+R` | Reset the run |

No game to hand? `--fake` replays a scripted run so the overlay can be developed
and checked on its own:

```powershell
dotnet run --project src/Host -- --fake
```

| Option | Meaning |
|---|---|
| `--fake` | Replay the scripted demo run instead of reading the game |
| `--port <n>` | Port to listen on (default 8777) |
| `--data <dir>` | Routes, history and checkpoints (default `./appdata`) |
| `--no-hotkeys` | Do not register global hotkeys |
| `--no-tray` | Do not show a notification-area icon |
| `?theme=<name>` | Overlay URL option: `minimal` or `light` |
| `?scale=<n>` | Overlay URL option: scale the overlay, e.g. `1.5` |
| `?splits=<n>` | Overlay URL option: split rows shown at once (default 6) |

### Stopping it

OverlayMod runs in the background, so closing a terminal will not stop it and
`Ctrl+C` does nothing. Use **Quit OverlayMod** on the control page, or
right-click the notification-area icon (behind the **^** arrow by the clock) and
choose **Exit**.

## Challenges and routes

The **challenge** decides what the overlay shows and how runs are ranked:

Each split shows whichever metric the challenge is ranked by, next to that
split's personal best — comparing hits on a time-ranked run would tell you
nothing.

| Challenge | Ranked by | Each split shows | Also |
|---|---|---|---|
| No-Hit | Total hits | Hits vs. best | — |
| Deathless | Deaths | Deaths vs. best | — |
| Any% | Time | Time vs. best | Death total |
| All Bosses | Time | Time vs. best | Death total, approach/boss hit breakdown |

Routes are JSON files in `appdata/routes/`, written on first run and meant to be
edited. Add or reorder splits, then hit **Reload from disk** on the control page.
A split carries the boss-defeat event flag that auto-advances it; a split with no
flag has to be advanced manually with the **Split** button.

Three routes ship: **All Bosses (main game)**, **All Bosses (with DLC)** and a
**Demo** route used by `--fake`. Every boss carries its defeat flag, so all
splits can auto-advance.

> ⚠️ **Auto-splitting has not been confirmed on a live game yet.** The flag ids
> are the game's own constants and cross-check against the two values already
> known, but nobody has watched them flip in a real run. Until that happens,
> keep an eye on whether splits land where they should — and the **Split** hotkey
> is there if one does not.

## Build & run the spike

The repo uses .NET 8. If `dotnet` isn't on your PATH, it was installed
user-locally at `%LOCALAPPDATA%\Microsoft\dotnet` (open a fresh terminal so the
persisted PATH/`DOTNET_ROOT` apply).

```powershell
dotnet build OverlayMod.sln -c Debug
dotnet run --project src/Spike
```

Then launch DS3 (offline, EAC off) and watch the table. **Milestone 1 verification
checklist** — confirm each value tracks reality:

| Value         | Confidence            | What to check                                            |
|---------------|-----------------------|----------------------------------------------------------|
| IGT           | high (from autosplit) | ticks up during play; pauses on loading screens          |
| load          | high                  | `yes` during loading screens                             |
| player        | high                  | `yes` in a level, `no` at the main menu                  |
| position      | high                  | x/y/z change as you move                                 |
| HP            | verified live         | matches your health bar; drops when you take a hit       |
| IudexGundyr   | **unverified**        | reads `DEAD` on a save where Iudex is already killed     |

The HP chain was confirmed against the live game. The remaining check is the
**event-flag** column: load a save past Iudex Gundyr and confirm it reads `DEAD`.
Boss-defeat flags drive auto-splitting, so that read has to be trustworthy
before Milestone 5.

## Milestones

The list below is the roadmap — *what* gets built, in what order. For the
*how* — designs, file layouts, the engine↔overlay data contract and task
checklists — see **[docs/PLAN.md](docs/PLAN.md)**.

1. **Memory spike** ✅ — attach + read IGT, loading, player-loaded, position, HP.
   Verified against the live game.
2. **Engine core** ✅ — `GameSnapshot` + `RunTracker` state machine: IGT-delta
   timing, hits, deaths, approach/boss segments, split advancement (+ unit tests).
3. **Server & live data** ✅ — localhost host broadcasting tracker state as JSON
   over Server-Sent Events, plus a **scripted fake source** so the overlay can be
   built and tested with no game running.
4. **Overlay UI** — transparent-background page: run timer, split list with
   personal bests, profile-driven display, themes and scaling. Built and running
   against the fake source.
   *Done when:* a test clip recorded in OBS (game capture + browser source on
   top) has the overlay in the file. **This is the headline goal.**
5. **Auto-splitting for real** — verify event-flag reads live, find the boss-HP /
   boss-fight-active offset, DS3 boss & area database, route + profile editor.
6. **Persistence** — SQLite (personal bests already work via JSON),
   `.lss`/CSV/JSON export.
7. **Packaging** — tray host, hotkeys, self-contained single-file build.

Milestones 3 and 4 need neither a running game nor the unsolved boss-HP offset,
so the visible overlay can be finished before auto-splitting is solved. Things
awaiting a live game are batched in
[docs/PLAN.md](docs/PLAN.md#pending-live-verification) rather than checked
piecemeal.

*Optional, any time after 4:* a transparent click-through desktop window hosting
the same overlay page, for live feedback while practising.

## Credits

DS3 memory layouts are documented by the reverse-engineering community. Pointer
*facts* (signatures, offsets, the boss event-flag table) were referenced from
the open-source [SoulSplitter](https://github.com/FrankvdStam/SoulSplitter),
[darksoulsiii-practice-tool](https://github.com/veeenu/darksoulsiii-practice-tool),
and community Cheat Engine tables, and re-implemented independently here. No code
was copied from those (GPL/other-licensed) projects.

## License

MIT — see [LICENSE](LICENSE).
