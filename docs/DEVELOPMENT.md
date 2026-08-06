# Development

## Build and test

Needs the .NET 8 SDK. If `dotnet` is not on your PATH it may be installed
user-locally at `%LOCALAPPDATA%\Microsoft\dotnet` — open a fresh terminal so the
persisted `PATH` and `DOTNET_ROOT` apply.

```powershell
dotnet build OverlayMod.sln
dotnet test OverlayMod.sln
```

Run the host with a console and live logs:

```powershell
dotnet run --project src/Host -- --fake
```

`--fake` replays a scripted run, so the whole overlay can be developed with the
game closed. Debug builds keep a console; Release builds are windowed and live in
the notification area.

Overlay and control pages are embedded in the assembly for release, but during
development the physical `wwwroot` folder is layered in front — editing a
stylesheet needs only a browser refresh, not a rebuild.

## Shipping a release

```powershell
./scripts/publish.ps1                              # ~180 MB, needs nothing installed
./scripts/publish.ps1 -Slim -Output publish-slim   # a few MB, needs .NET 8
```

Then attach the executable to a GitHub release:

```powershell
gh release create v0.1.0 publish/OverlayMod.exe --title "OverlayMod v0.1.0" --notes "..."
```

The repository is public, so release assets download anonymously — that link is
shareable with anyone.

> **Commits use a GitHub noreply address** (`122255916+Darkpunkki@…`), set as this
> repository's local `user.email`. The history was rewritten before the repository
> went public to keep a real address out of it. If you clone fresh elsewhere, set
> it again before committing, or your global identity will end up in public
> history.

The executable is self-contained and verified to run on a machine with no .NET
available, binding loopback only. Recipients need Windows x64 and nothing else.

The self-contained build is the right default: someone who wants to track a run
should not have to install a runtime first. The slim build is worth offering
alongside it for people who already have .NET.

The executable is unsigned, so Windows SmartScreen will warn on first run. Signing
needs a code-signing certificate; until then the README tells people what to
expect, which is better than them wondering.

## Layout

| Project | What it is |
|---|---|
| `src/Engine` | Memory reading, run tracking, persistence. No UI, no web. |
| `src/Host` | ASP.NET Core host: polls the game, serves the overlay and control pages, hotkeys, tray icon. |
| `src/Spike` | The Milestone 1 console harness. Still useful for eyeballing raw values. |
| `tests/Engine.Tests` | Everything testable without the game. |

### How it fits together

```
Ds3Reader ──┐
            ├── ISnapshotSource ── EngineLoop ── RunTracker ── OverlayState ──┐
FakeSource ─┘                          │                                      │
                                  RunController                          SSE /events
                                       │                                      │
                            records / routes / settings                  overlay page
```

- **`ISnapshotSource`** is the seam that makes everything testable.
  `FakeSnapshotSource` replays a keyframe script, so the tracker, the server and
  the overlay can all be exercised with no game running.
- **`RunTracker`** is pure logic over a stream of `GameSnapshot`s — no memory
  access, so it is driven entirely from synthetic data in tests.
- **`RunController`** owns run state and decides the awkward questions: when a run
  starts, and whether returning to the world continues the same run or begins a
  new one.
- **`OverlayState`** is a view model, deliberately not the engine's own types. The
  page never learns about pointer chains.

### Data files

All under the data directory (`appdata` beside the executable by default):

| File | Contents |
|---|---|
| `routes/*.json` | Route definitions; hand-editable |
| `records.json` | Finished runs, and per-split bests |
| `run-state.json` | The run in progress, so it survives a restart |
| `settings.json` | Selected route and challenge |
| `appearance.json` | Overlay colours, size, transparency |
| `hotkeys.json` | Key bindings |
| `overlaymod.log` | The log |

## Working on the memory layer

This is the part that breaks, because it depends on the game's internal layout.

- **The spike** (`dotnet run --project src/Spike`) prints raw values live and is
  the quickest way to see whether a read is sane.
- **`GET /api/diagnostics?flag=<id>`** reports every step of an event-flag lookup
  and names the one that failed. Flag reads walk a chain of pointers through
  reverse-engineered structures, so "which hop broke" is the only useful question.
- **A value that looks like `0x00007FFx` is a pointer, not data.** That is what a
  missing dereference looks like, and it is how the event-flag bug was found.

Anything unverified against a real game is tracked in
[PLAN.md](PLAN.md#pending-live-verification) rather than assumed to work.

## Conventions

- Docs are updated in the same commit as the change. `PLAN.md` records decisions
  and the reasoning behind them; resolved questions move from "Open questions"
  into "Decisions already made" rather than quietly disappearing.
- Anything that has not been confirmed against the live game says so.
- Tests use realistic values where the realism matters — in-game times of twenty
  minutes rather than ten seconds, because the difference between "the save
  rewound" and "a different character" only exists at that scale.
