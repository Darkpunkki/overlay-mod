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
- **Tests** (`tests/Engine.Tests`) — tracker logic, resume behaviour, personal
  bests, all driven from synthetic snapshots.

## Run the overlay

No game needed — `--fake` replays a scripted run so the overlay can be developed
and checked on its own:

```powershell
dotnet run --project src/Host -- --fake
```

Then open <http://127.0.0.1:8777/overlay/>, or point an OBS **Browser Source**
at that URL and layer it over your game capture. Drop `--fake` to drive it from
a live game instead.

| Option | Meaning |
|---|---|
| `--fake` | Replay the scripted demo run instead of reading the game |
| `--port <n>` | Port to listen on (default 8777) |
| `--data <dir>` | Run history and checkpoints (default `./appdata`) |
| `?theme=<name>` | URL option: `minimal` or `light` |
| `?scale=<n>` | URL option: scale the overlay, e.g. `1.5` |

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
