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
| Fall-damage classification | **Heuristic over player height, unverified live** (0.2.0) |
| Poison / toxic classification | **Heuristic over the rhythm of repeated drops** (0.2.3). Cadence confirmed as 1 s and bite size confirmed proportional to health; the 8% ceiling is still unverified live |
| Active status effects, read outright | **Found 2026-08-09**, not yet wired in. See *Where status effects live* — it replaces both heuristics once the layout is known |
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

- [x] **Packaging.** `scripts/publish.ps1` builds a single self-contained
      `OverlayMod.exe` with a notification-area icon, a file log, and pages
      embedded in the assembly so there is no loose asset folder to lose.
- [x] **A discoverable way to quit.** First real use turned up that a windowed
      process ignores `Ctrl+C`, survives closing its terminal, and hides its tray
      icon behind the notification-area overflow arrow — leaving Task Manager as
      the only exit. The control page now has a Quit button.
- [ ] Route editor, so custom routes do not mean hand-editing JSON. *Optional.*
- [x] **Appearance controls.** Size, colours, panel transparency, shadow strength
      and split-row count, edited on the control page with a live preview over a
      chequerboard. Values are validated server-side before reaching CSS — they
      arrive over HTTP and are written into custom properties, so a malformed
      colour falls back to the default rather than being passed through.
- [x] **A control page that fits on a screen** (0.2.0). Every section collapses
      and remembers whether it was open, the split count is a number field from
      1 to 30 rather than a slider capped at 20, and the build version is shown —
      0.1.0 printed it nowhere, which made every bug report a round trip.

### 2. Complete visuals, differing per challenge

**Done.**

- [x] **Split column follows the profile's metric.** Each split shows damage,
      hits, deaths or time — whichever the challenge is ranked by — against that
      split's own best. Every best is always sent so the payload shape does not
      change with the profile.
- [x] **Four challenges** (0.2.0): No Damage, No Hit, Deathless, Speedrun. Any%
      and All Bosses removed; the All Bosses *routes* are unaffected.
- [x] **Speedrun drops the totals footer** and puts the whole-run best under the
      timer instead, rather than printing the same number twice.
- [x] **Personal-best colouring is mirrored** between the live value and the
      best, so one of the pair is always green and the other red.
- [x] **The route name is off the overlay.** Chosen once, never changes mid-run.
- [ ] **No end-of-run state.** A finished run just stops; there is no "run
      complete" treatment and no acknowledgement of a new personal best.
- [ ] **Abandoned attempts leave no history.** Split bests now survive them, but
      the attempt itself is not recorded, so there is no way to look back over
      how attempts have gone — only at the best.
- [ ] Approach-vs-boss breakdown displays correctly but is **always empty in a
      real game** — nothing sets `BossFightActive`. Blocked on the boss-HP
      offset. *(needs the game)*

### 3. Visible in a window, and composited into a recording

**Done.** Confirmed in OBS 32.2.1 on 2026-08-06: the overlay composites over a
Browser Source and survives into a recorded file. That was the Milestone 4
done-criterion.

- [x] Verified in OBS against a dark background.
- [ ] Worth a second look over bright scenes once a real game is behind it.

### 4. The project's other stated goals

- [ ] **Auto-splitting is unproven** on a live game, though every boss now
      carries a flag id. *(needs the game — see below)*
- [x] **Global hotkeys.** `Ctrl+Alt+S/D/R` for start, split and reset, bound via
      `RegisterHotKey` on a dedicated message-pump thread. Configurable in
      `appdata/hotkeys.json`, disableable with `--no-hotkeys`, and shown on the
      control page. A low-level keyboard hook was rejected: it would see every
      keystroke on the machine, which is both more than this needs and the sort
      of thing anti-virus software objects to.
- [ ] **Export** to `.lss`, CSV and JSON, for LiveSplit parity. *(Milestone 6)*
- [ ] SQLite replacing the JSON store. *Optional — JSON works fine at this
      scale.* *(Milestone 6)*
- [x] Per-split and per-run personal bests, hit and death tracking, run resume,
      route and challenge selection, themes.

### Shortest path to a usable product

One session with the game, working through the verification table below.
Everything that can be built without it has been.

## Pending live verification

Everything here is written, unit-tested where it can be, and **unproven against
the real game**. Deliberately batched rather than checked one at a time, so a
single session with DS3 running can clear the lot. Nothing in this list blocks
work that does not touch it.

| # | What | How to check | Why it matters |
|---|---|---|---|
| 0 | ~~**Attaching at all**~~ | ✅ **Confirmed 2026-08-06** on game version **1.15.2.0** — AOB scanning and pointer resolution work | — |
| 0b | ~~**Timer and hit counting**~~ | ✅ **Confirmed** — the timer starts on a new game and hits register | — |
| 1 | ~~**Event-flag reads**~~ | ✅ **Confirmed 2026-08-06.** Iudex reads dead on a save where he is; six other bosses read alive. The lookup was missing a dereference — the computed address holds a *pointer* to the flag block, not the bits | — |
| 2 | **IGT persists across a restart** | Note IGT, quit to desktop, relaunch, load in; IGT should resume at or above the noted value | The entire resume-a-run feature rests on this |
| 3 | **IGT at the main menu** | Watch IGT while sitting at the menu | The tracker assumes menu IGT is meaningless and ignores it; if it reads as a huge or negative value the resume comparison needs a guard |
| 4 | **Boss-defeat flag ids** | Kill any boss and watch its split advance, or `GET /api/flags?ids=…` to check one directly | All 25 ids are now filled in from the published table, and the two already known match it — but none have been seen flipping in a real run |
| 5 | **Player-loaded timing** | Watch when `player` flips true relative to regaining control | Decides whether runs start slightly early, during the fade-in |
| 6 | **Boss HP / boss-fight-active** | Not yet possible — no offset found | Blocks approach-vs-boss attribution entirely |
| 7 | **Fall-damage classification** | Take a survivable fall, then an ordinary hit, and read `GET /api/hits` back. See LIVE-TESTING 4.7 | The whole of No Hit rests on it, and the thresholds can only be set against a real game |
| 8 | **Deaths register at all** | Die three times in one split under Deathless; the count should read three | Was broken before 0.2.0 and the fix is unproven live — see the decision on latching below |
| 10 | **The active-status-effect list** | ~~Search for it~~ ✅ **Found 2026-08-09** — see *Where status effects live*, below. Wiring it into the tracker is the outstanding work, not finding it | The one unfound read behind fall attribution, poison attribution and boss-fight detection |
| 9 | **Poison / toxic classification** | Get poisoned under No Hit, let it run its course, then read `GET /api/hits` back. See LIVE-TESTING 4.8 | Cadence (1 s) and proportional bites are now known. What is still a guess is the **8%** ceiling: too low and every tick is a hit again; too high and real chip damage disappears. `/api/hits` reports the ceiling in HP next to the ticks so the two can be compared directly |

**[docs/LIVE-TESTING.md](LIVE-TESTING.md) is the walkthrough for this** — how to
launch offline without EAC, and each check written as a pass/fail with what to
report back.

Checks 1–3 and 5 are observation only and need nothing but the spike running.
Check 4 is what `GET /api/flags?ids=…` exists for — it reads arbitrary event
flags from the live game, so candidate ids can be watched while a boss dies.
Check 6 is a research task, not a verification.

## Where status effects live (found 2026-08-09, on game version 1.15.2.0)

Both damage classifiers are heuristics because the reading that would say
outright what hurt you had never been found. **It has now been found**, by
searching for it rather than by writing down somebody's offset — the tooling
that did it is on the branch `speffect-search`, and this is what it turned up.

From the player character, through the module table already used for health:

| Path | Holds |
|---|---|
| `module slot 0x70` → pointer at `+0x668` → `+0x150` | `int`. **-1 when nothing is active**, `4004` while poisoned |
| the same block, `+0x154` | `float`. Rests at `0`, then runs `0.9997` downwards — a **fraction of the effect remaining** |

Four independent searches converged on it: as a whole field it separated
poisoned from clear with a score of **1.000** on two values; as bits, `+0x150`
bit 0 and `+0x154` bit 21 each flipped **four times across two poisonings**,
which is the count that had to be there; and as a duration it counted down over
94 values. The bit findings are the same fact seen twice — bit 0 is `-1`
becoming `4004`, and bit 21 is the mantissa of a float near one.

**What is still unknown**, and what the next session on this should establish:

- **The layout.** Whether `+0x150` is one slot of an array of effects or the
  only one, and if an array, its stride and length. That decides whether this
  reads every status effect or just this one. `GET /api/prospect/block` on the
  `speffect-search` branch dumps the block for exactly this question.
- **The other identifiers.** `4004` is poison here. Toxic is unknown, and
  whether fall damage appears in this list at all is unknown.
- **Every other game version.** None of these offsets is confirmed anywhere but
  1.15.2.0.

**When it is wired in, it goes in beside the heuristics rather than instead of
them.** A build where the pointer does not land must fall back to what 0.2.4
does, not switch the feature off — that is the 0.2.0 failure, and this is
exactly the kind of read that caused it.

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
- **The event-flag lookup ends in a pointer, not the bits.** `(c << 4) + bucket +
  category * 0xa8` is the address of a *pointer* to the block of flag bits; it
  must be dereferenced before reading the word. Reading in place returns the
  upper half of that pointer — a value like `0x00007FF3` — which is never the
  flag asked about and is sometimes non-zero in the bit being tested, so it
  reported some bosses dead that were alive. Confirmed against a live game.
  **A word that looks like `0x00007FFx` is the signature of this mistake.**
- **A run starts when the player loads into the world.** Not on a hotkey, not on
  an IGT reset. Being in a level is what "a run has begun" means; menus and
  loading screens are not part of it.
- **Quitting the game does not end a run.** DS3 keeps in-game time in the save,
  so on returning the save's IGT is compared against the last value seen: at or
  ahead of it means the same character continuing and the run resumes; behind it
  means a different or fresh character and a new run starts. A *finished* run is
  always replaced on return — loading back in is the next attempt.
- **The profile decides what is displayed**, not the page. Each profile shows one
  metric per split — the one it is ranked by — with no per-split times unless it
  is ranked on time. Every metric is still recorded underneath regardless of what
  is shown.
- **Damage and hits are counted separately, and both are stored.** "Damage" is
  every drop in health; "hits" is damage minus what the fall detector attributed
  to landing and minus what the damage-over-time detector attributed to a status
  effect. Deriving one from the other at display time was the alternative and is
  wrong: which one a challenge is judged on changes, but both are facts about
  what happened, so a run recorded under No Damage stays comparable against a No
  Hit best.
- **Fall damage is told apart by height, not by damage source.** Dark Souls III
  applies its own SpEffect for a fall, which would be exact — but reading it
  needs a pointer chain nobody here has found or verified, the same wall the
  boss-HP offset is behind. Player position is already read every tick and is
  confirmed working, so `FallDetector` asks whether the player had just finished
  descending. **This is a heuristic and is presented as one:** the thresholds
  live in `appdata/tracking.json` and on the control page, and every damage event
  keeps the descent it measured so `GET /api/hits` can be read back after a run.
  *The SpEffect read replaces it when the offset is found — same seam, different
  classifier — and is worth doing in the same session as the boss-HP hunt.*
- **Poison and toxic are told apart by shape, not by cause** (0.2.2), and behind
  the same wall for the same reason: the game knows perfectly well that you are
  poisoned, and reading it would be exact, but it is another unverified pointer
  chain whose layout moves between patches. `DamageOverTimeDetector` instead
  looks for what a tick *looks like*.
- **What a tick looks like is a metronome, not a magnitude** (0.2.3, after 0.2.2
  shipped broken). Poison and toxic tick **once a second, every second**, for a
  bite **proportional to maximum health**. 0.2.2 asked instead for gaps of
  1.2–4 s and bites under a flat 40 HP — three conjunctive guesses about a game
  nobody here had measured — and the floor sat *above* the real cadence, so every
  tick was rejected before its size was considered and the feature did nothing at
  all. Two rules came out of it:
  - **A bound whose wrong value disables the feature must not be hard-coded.**
    The 1.2 s floor was the only setting not on the control page, and it was the
    one that was wrong. `MaxIntervalMs` is now settable down to 600 ms and
    `MinIntervalMs` sits at 250 ms, far below anything real.
  - **Regularity does the work that guessed magnitudes cannot.** Requiring four
    bites whose *gaps match each other* is a far stronger and more portable
    discriminator than any absolute band, because combat damage is irregular in
    both timing and size no matter what the numbers happen to be.
- **A pattern is how an effect is recognised, not a condition it keeps
  satisfying** (0.2.4). Both classifiers had the same shape of bug: the test that
  *identifies* the thing was also being used to decide whether it was still
  happening. Poison had to keep hitting its rhythm, so the last tick before a
  bonfire cure — off the beat — and the first tick after any heal — a bigger bite,
  since the bite is a share of health — were orphaned and billed as hits. Falls
  were measured inside a half-second window, so a drop onto a ledge followed by a
  slide off it was measured from the ledge and billed as a hit. Both are now
  *episodes*: established once, then continued on much weaker conditions until
  they demonstrably end. The strict test still guards the entry, which is where
  false positives are actually created.
- **The tick ceiling is a percentage of health, and never a gate.** It is taken
  against the highest of every `MaxHp` *and* every current-HP reading seen this
  run, so a build where `MaxHp` reads zero degrades to a conservative scale
  instead of a ceiling of zero — which would switch the classifier off silently,
  which is the 0.2.0 failure rebuilt in a new place. `GET /api/hits` reports the
  scale and the resulting ceiling in HP alongside the events, because a
  percentage setting cannot be checked against a column of HP figures otherwise.

  **A small drop is held rather than counted** until the fourth one settles it.
  Counting it and retracting later was the alternative and reads as a broken
  overlay — a minute of poison would drive the hit counter up and back down every
  few seconds. Holding costs a few seconds of latency on a genuinely small hit,
  which is the better error: it appears late rather than not at all. Everything
  still unresolved when the readings break — a loading screen, a reload, a
  checkpoint being restored — settles as a **hit**, because an unproven tick is a
  hit we have not finished doubting, and that is the only direction that cannot
  make an invalid run look clean.

  It cannot distinguish poison from standing in a fire, and it says so on the
  control page. *The same SpEffect read that fixes fall damage fixes this too.*
- **No counter may depend on a memory read whose only job is to corroborate
  another one.** 0.2.0 required `MaxHp > 0` before it would look at health at
  all, as proof the data module had been populated. It was a new dependency on
  an offset nothing else used, and on an older game patch — where it does not
  land where we expect — it switched damage, hits and deaths off together while
  the run timer carried on, because the timer needs no such reading. Every
  challenge that counts something looked broken and Speedrun looked fine.
  **A guard that can disable the whole feature it protects is worse than the
  transient it guards against.** Persistence replaced it: zero health across
  several consecutive readings tells a corpse from a pointer being rebuilt, and
  needs no offset to be right. Version-dependent reads are a liability in
  proportion to how much rests on them, and the version table only covers what
  has actually been seen.
- **Health is shown on the control page, and only there.** It stays off the
  overlay and out of the view model for the reasons already recorded, but damage,
  hits and deaths are all derived from that one reading, so when no counter moves
  it is the fact that explains it. The control page fetches it from
  `/api/diagnostics` rather than the state stream, so nothing rides along at
  30 Hz. Found the hard way: a user on an older patch, where `0 / 0` on a live
  character says in one glance what a log full of zeros does not.
- **A new character is recognised by its own clock, not by the previous run's.**
  The check asked whether the run being replaced was longer than a minute, which
  meant that after a short session — testing, most obviously — starting a new
  game looked like an ordinary save-point rewind and carried the old counters
  into the new character. It asks about the save just loaded instead. Below a
  minute of in-game time a backwards clock is called a new character even though
  a genuine save could rewind that far: both readings are cheap to be wrong about
  there, and the same mistake two hours into an attempt is not, which is what the
  rewind tolerance exists for.
- **A death is latched on zero health, not detected as an edge.** The original
  code required a positive reading followed by a zero one, which needs both
  neighbours of the transition. The game raises its loading flag as the death
  fade begins, so the tick where health first reads zero is exactly the tick that
  may not be in play — and the death was silently lost. Health at zero while the
  character exists is now a death whether or not the previous tick was observed;
  the latch clears when health returns. **The latch and "has been seen alive"
  survive gaps in the readings** (a dropped poll, a stutter) and are cleared only
  when a run starts, so a gap can neither lose a death nor produce a second one.
  A zero `MaxHp` means the data module is not populated yet and is ignored
  entirely — the frames right after a load read zeros the game has not written.
- **Challenge names on disk are read leniently, never strictly.** A route file
  whose challenge fails to parse is *skipped entirely* by `RouteStore`, so a
  strict parse does not fall back to a default — it silently removes the route
  from the picker. Removing Any% and All Bosses in 0.2.0 would have deleted the
  All Bosses routes from every existing install. `ChallengeTypeJsonConverter` maps
  what 0.1.0 wrote and falls back to No Damage rather than throwing.
- **`NoHit` was deliberately not remapped when its meaning narrowed.** In 0.1.0
  it counted fall damage, which is now No Damage. Pointing the old name at the
  challenge that still bears it means an existing selection lands on the stricter
  reading rather than on a differently-named one; the release notes say so, and
  No Damage is one click away. **Stored bests went the other way**: those numbers
  counted every drop in health, so they migrate into damage, and the hit best is
  left null. Nothing in an old file says which of those hits was the ground, and
  inventing one would put an unbeatable target on screen.
- **Health is never displayed, on any profile.** The game's own UI already shows
  it, so repeating it costs overlay space and viewer attention for nothing. HP is
  still read — hits and deaths are derived from it — but it does not reach the
  view model at all.
- **Appearance is stored server-side, not in the overlay URL.** A version number
  rides along in the state stream and the overlay refetches only when it moves.
  That keeps the settings out of every frame at 30Hz while still landing a
  restyle in OBS immediately — and means the Browser Source URL never has to
  change.
- **Personal bests are per-split as well as per-run** ("gold splits"), so the
  overlay can show progress against the best each boss has *ever* been rather
  than only against one best run.
- **A split's best is earned the moment that boss dies**, whether or not the run
  is ever finished. Most attempts end early, so requiring a completed run would
  discard nearly every boss result a player produces. **Whole-run bests are the
  opposite**: they only come from a finished run, because a total from an
  abandoned attempt is not comparable to one from a completed one. The two are
  stored separately for that reason — `splitBests` alongside `runs`.
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
- **Leaving the world always re-checks whether the run continues.** Not just a
  re-attach: quitting to the main menu and starting a new character keeps the
  same process alive, and originally the old run simply carried on with its hits
  intact. Any transition out of play — menu, loading screen or quit — now re-runs
  the comparison on the way back in. Found in live testing.
- **In-game time is allowed to go backwards a little.** The first version of that
  comparison demanded the save's clock never regress, which threw away a good run
  every time the player quit to the menu and continued — DS3 writes in-game time
  to the save periodically, so reloading rewinds to the last save point. A rewind
  within five minutes now counts as the same run; a save under a minute old
  counts as a fresh character regardless. Also found in live testing.
- **Readings are given time to settle after loading in.** The first frames after
  a load can report in-game time the game has not written yet, and a transient
  zero is indistinguishable from a new character. The resume decision waits.
- **The run timer follows in-game time, including backwards.** If the save rewinds
  to its last save point, the run timer rewinds with it. That is the honest
  reading: the player really did lose that progress and will replay it.
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
  "profileName": "No Hit",
  "runIgtMs": 754320,
  "totalDamage": 4,                // every drop in health
  "totalHits": 3,                  // ...minus what was attributed to a fall
  "totalDeaths": 0,
  "primary": { "metric": "Hits", "value": 3, "best": 9 },   // per challenge profile
  "display": { "splitMetric": "Hits", "showTotals": true },
  "player": { "loaded": true, "loading": false },
  "bests": { "runIgtMs": 92498, "totalDamage": 11, "totalHits": 9, "totalDeaths": 1 },
  "bossFightActive": false,
  "activeIndex": 2,
  "splits": [
    {
      "name": "Iudex Gundyr",
      "isBoss": true,
      "completed": true,
      "igtMs": 180000,
      "damage": 2,
      "hits": 1,
      "deaths": 0,
      // Still sent, still displayed by nothing: the breakdown returns in
      // Milestone 5, once boss-fight detection has an offset to stand on.
      "approach": { "igtMs": 120000, "damage": 1, "hits": 0, "deaths": 0 },
      "boss":     { "igtMs":  60000, "damage": 1, "hits": 1, "deaths": 0 },
      "pbIgtMs": 27493,            // best this split has ever been
      "pbDamage": 4,
      "pbHits": 3,
      "pbDeaths": 0
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
