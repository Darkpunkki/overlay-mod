# OverlayMod

A run tracker and overlay for **Dark Souls III**, built for recording and
streaming challenge runs.

It times your run, follows your route, splits automatically when a boss dies, and
counts the **hits and deaths you take on each boss** — comparing every one
against your best. Point OBS at it and the overlay lands in your recording.

![status](https://img.shields.io/badge/status-early%20development-orange)

---

## ⚠️ Read this first

OverlayMod reads Dark Souls III's memory. **Only run it offline, with Easy
Anti-Cheat disabled.** Reading game memory while connected can get your account
soft-banned.

This is the standard setup for DS3 speedrunning and practice tools, and
[docs/LIVE-TESTING.md](docs/LIVE-TESTING.md) walks through it. OverlayMod itself
never writes to the game — it only reads.

---

## Install

**[Download the latest release](https://github.com/Darkpunkki/overlay-mod/releases/latest)**
and put `OverlayMod.exe` in a folder of its own — it writes its settings and
history into an `appdata` folder beside itself.

It is a single file and needs **nothing else installed**: no .NET, no runtime, no
installer. Windows 64-bit only.

Or build it yourself:

```powershell
git clone https://github.com/Darkpunkki/overlay-mod
cd overlay-mod
./scripts/publish.ps1
```

Windows will warn that the publisher is unknown, because the executable is
unsigned. Choose **More info → Run anyway**.

### Sharing it with someone

Point them at the [releases page](https://github.com/Darkpunkki/overlay-mod/releases/latest),
or send them the one `.exe` directly — that is genuinely all they need, verified
on a machine with no .NET available. Worth telling them:

- **SmartScreen will warn** on first run; it is unsigned.
- **Antivirus may flag it.** It reads another process's memory and registers
  global hotkeys, which looks unusual without the context.
- They still need **Dark Souls III running offline with EAC disabled** — see the
  warning above. The overlay cannot do that for them.

It listens on **loopback only** (`127.0.0.1`), so nothing is exposed to the
network and Windows Firewall will not prompt.

## Use it

**1. Launch Dark Souls III offline with EAC disabled.**

In short: set Steam to offline mode, then run `DarkSoulsIII.exe` **directly**
from the game folder (right-click the game in Steam → Manage → Browse local
files) rather than pressing Play. Check Task Manager afterwards: `EasyAntiCheat`
must not be running. Full steps are in
[docs/LIVE-TESTING.md](docs/LIVE-TESTING.md).

**2. Run `OverlayMod.exe`.**

It goes straight to the notification area — no window opens. Order does not
matter; it picks the game up whenever it appears.

**3. Open the control panel: <http://127.0.0.1:8777/control/>**

Choose your **challenge** and **route**. Both are remembered next time.

**4. Play.**

Your run starts when you load into the world. Boss kills split automatically.
Quitting the game pauses the timer; loading back in carries on where you left
off.

### Stopping it

**Quit OverlayMod** on the control panel, or right-click the notification-area
icon (behind the **^** arrow by the clock) → **Exit**. Closing a terminal will
not stop it.

## Use it with OBS

1. **Sources → + → Browser**
2. **URL:** `http://127.0.0.1:8777/overlay/`
3. **Size:** about **500 × 420**
4. Drag it **above** your game capture in the Sources list — the top source draws
   in front

The overlay has a transparent background, so it composites straight onto your
capture. The split list shows a fixed number of rows and scrolls through the
route, so a 25-boss route needs no more height than a 3-boss one.

If it looks small, resize the **source** rather than scaling the layer — scaling
blurs it. Better still, raise **Size** in the control panel.

## Challenges

The challenge decides what the overlay shows and what your runs are judged on.

| Challenge | Judged on | Each split shows |
|---|---|---|
| **No Damage** | Every drop in health | Damage taken, against your best for that boss |
| **No Hit** | Damage, **excluding falls and poison** | Hits taken, against your best for that boss |
| **Deathless** | Deaths | Deaths, against your best for that boss |
| **Speedrun** | Time | Split time, against your best for that boss |

**No Damage and No Hit differ in two respects.** No Damage counts everything, so
mistiming a drop or wading through a swamp costs you the run. No Hit sets aside
damage you took landing and damage from poison, so it measures only what an enemy
dealt you. No Damage is the stricter of the two, and the one that needs no
guesswork to be correct — see below.

Speedrun shows no hit counter at all: the run clock, and each split against its
own best. Beating a split's best paints it green and paints the best red; falling
behind swaps them, so the better number is always the green one.

A run is never failed automatically. Take a hit in a No Hit run and it keeps
counting — finish the attempt and see how it compares.

**Dying counts as a hit, whatever killed you** — a fall, poison, a bottomless
pit. Falls and poison are set aside from the No Hit count everywhere except when
they kill you, which is the settled convention among No-Hit runners and the one
reading that cannot make an invalid run look valid.

### How falls and poison are told apart

**Falls, by watching how far you dropped just before you lost health:** more than
3 metres inside the preceding half-second, by default.

**Poison and toxic, by the shape of the damage:** they tick once a second, every
second, for a bite proportional to your maximum health — a metronome, which
combat never is. So the detector looks for four or more small bites of about the
same size at gaps about the same as each other. Anything that really hurts is
over the size ceiling (8% of your health by default) and counts as a hit the
moment it lands. Because it takes a fourth bite to see the rhythm, the first
three wait a couple of seconds before being called — so a genuinely small hit
shows up a little late rather than a poison tick showing up at all.

**Neither applies to a killing blow.** Both exist to set aside damage you were
not really dealt, and neither reading survives you dying of it. A fatal fall and
a poisoning you never cured are hits.

Neither of these is **a reading of what damaged you**. The game does record that,
but through a pointer chain this project has not found. So both will get things
wrong at the edges — most obviously a real hit landing within a few frames of
your feet touching down, and anything else that ticks away at you slowly, like
standing in a fire, being called poison.

Which is why you can check them. The control panel's **What counts as a hit**
section lists recent damage with the health each cost, the drop measured, and the
call it made, and lets you move the thresholds or switch either one off. Off
means No Hit counts exactly what No Damage counts, which is the honest setting if
it is misjudging your route.

### Fixing the count by hand

However the thresholds are set, some edge will slip through eventually. **Hit
corrections**, under **Run** on the control panel, lists every split the run has
reached with a **+** and **−** beside it: add the hit the mod missed, remove the
one it should not have counted. The overlay follows immediately — and so does
the personal best for that boss, even after the boss is dead: the banked best is
re-filed as if the count had been right when it died, so a miscount never stands
as an unbeatable target.

Corrections apply to the run in progress, and to the hit count only. Damage is
measured rather than guessed, so there is nothing there to correct — and a
finished run is history.

### Personal bests

Two kinds, and they are earned differently:

- **Per boss** — banked the moment that boss dies, whether or not you finish. An
  abandoned run still improves your best Iudex.
- **Whole run** — only from a run you actually completed.

## Routes

| Route | Splits |
|---|---|
| **Quick route** | 13 — a normal completion |
| **Glitchless route, Anri** | 14 — the No-Hit glitchless route, via Anri |
| **All Bosses (main game)** | 19 |
| **All Bosses (with DLC)** | 25 |
| **Demo** | 3 — for trying things out without the game |

The **Anri** split fires when you receive Anri's Straight Sword. Dark Souls III
records picking an item up as an event flag of its own, so that is read directly
rather than guessed at from where you are or what you have lost.

### Building your own

**Edit selected** on the control panel opens the route editor: reorder splits
with the arrows, remove them with ✕, and add more from the boss list — which
brings that boss's event flag with it, so the split advances on its own. Anything
you type instead is a manual split and is marked as one. **Duplicate selected**
is the safe way to start from a route you like.

Routes are still plain JSON files in `appdata/routes/`, so hand-editing works
just as well — press **Reload from disk** afterwards. A route you delete stays
deleted; **Restore built-in routes** brings back any that are missing, including
ones added in a newer version.

**Renaming a route starts its personal bests over**, because they are filed under
its name; the editor warns you first. Renaming a *split* does not — that is what
custom names below are for.

If a split ever fails to advance on its own, the **Split** hotkey moves it along.

## Customising the look

The control panel's **Appearance** section changes size, colours and panel
transparency, with a live preview on a chequerboard so you can see what is
see-through. Changes apply immediately, including in OBS.

**Splits shown** is the number of boss names on screen at once — type it or step
it, 1 to 30. The list scrolls through longer routes, so this sets the overlay's
height rather than the route's length: set it to the route's full split count to
show everything with no scrolling.

**Clock size** is separate from **Size**, and multiplies on top of it. The clock
is read at a glance from across a room; the split list is detail. Making the
clock bigger should not cost the splits their space.

**Show the attempt count** puts "attempt 214" beside the challenge name. It is
counted per route and challenge, and goes up whenever a run starts. Set the
number by hand under **Run → Attempts** if you are carrying a tally over from
somewhere else.

### Split names

**Split names** on the control panel changes what the overlay calls each split:
"Cinder" instead of "Soul of Cinder", "Twin Princes" instead of "Lothric,
Younger Prince". **Use short names** fills in every boss at once, and any name
you set yourself outranks the preset.

Short names also earn their space back: when every name in the route is short,
the overlay narrows to match, so it covers less of your capture. One long name
anywhere in the route keeps the full width.

Only the display changes. Personal bests are filed under the name in the route
file, so renaming here never loses the history behind a boss — which is why this
exists instead of editing the route.

Every section of the control panel collapses, and remembers whether you left it
open.

## Hotkeys

Global, so they work without leaving the game.

| Default | Action |
|---|---|
| `Ctrl+Alt+S` | Start a run |
| `Ctrl+Alt+D` | Split |
| `Ctrl+Alt+R` | Reset the run |

Change them in `appdata/hotkeys.json` and restart. The control panel shows what
is actually bound — if another application already owns a combination, it says so.

## Command line

Mostly unnecessary, but available.

| Option | Meaning |
|---|---|
| `--fake` | Replay a scripted demo run; no game needed |
| `--port <n>` | Port to listen on (default 8777) |
| `--data <dir>` | Where settings and history live |
| `--no-hotkeys` | Do not register global hotkeys |
| `--no-tray` | No notification-area icon |

## If something is wrong

The log is the first place to look: **`appdata/overlaymod.log`**, next to the
executable, or **Open log file** on the tray icon.

| Problem | Try |
|---|---|
| Control panel says "not running" | Check DS3 is running and was launched directly rather than through the EAC launcher. Try running OverlayMod as administrator |
| Numbers are zero or nonsense | Note your game version (main menu, bottom corner) and open an issue with the log |
| Timer runs but nothing counts | Check **Health** under **Now** on the control panel. Real numbers while you play means the reading works; `0 / 0` while standing in a level means the memory offsets are wrong for your game version, and no amount of playing will move the counters. Tested against **1.15.2.0**; older patches move these offsets |
| A boss kill did not split | `http://127.0.0.1:8777/api/diagnostics?flag=<id>` reports every step of the lookup. Split manually with `Ctrl+Alt+D` meanwhile |
| Hotkeys do nothing | Another app may own the combination; the control panel marks unbound ones |
| A hit was counted as a fall or as poison, or missed | **What counts as a hit → Recent damage** on the control panel shows what was called and why, and what each cost. Only No Hit is affected. Put the number right with **Run → Hit corrections** |
| Poison still counts as hits | The line above the damage list says what the ceiling works out to in HP. Compare it against the `HP` column and raise **Bites no bigger than** past your ticks. Make sure you are on **0.2.3 or later** — in 0.2.2 this never worked at all |

Known limitations:

- **Falls and poison are detected by heuristics, not read from the game.** No Hit
  is only as good as those guesses. See [How falls and poison are told
  apart](#how-falls-and-poison-are-told-apart); No Damage is unaffected.
- **The approach-vs-boss breakdown is recorded but not displayed**, because
  detecting when a boss fight is active needs a memory offset that has not been
  found, which leaves it empty in a real game.
- **Auto-splitting has been confirmed to read boss flags correctly**, but has not
  yet been watched through a full run.

## More

- [CHANGELOG.md](CHANGELOG.md) — what changed, and what upgrading does to your
  existing personal bests
- [docs/LIVE-TESTING.md](docs/LIVE-TESTING.md) — launching offline without EAC,
  and what still needs checking against the real game
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — building, testing, architecture
- [docs/PLAN.md](docs/PLAN.md) — the implementation plan and design decisions

## Credits

DS3 memory layouts are documented by the reverse-engineering community. Pointer
*facts* — signatures, offsets, and the event-flag tables for boss defeats and
item pickups — were referenced from the open-source
[SoulSplitter](https://github.com/FrankvdStam/SoulSplitter),
[darksoulsiii-practice-tool](https://github.com/veeenu/darksoulsiii-practice-tool),
and community Cheat Engine tables, then re-implemented independently here. No
code was copied from those (GPL and other-licensed) projects.

## License

MIT — see [LICENSE](LICENSE).
