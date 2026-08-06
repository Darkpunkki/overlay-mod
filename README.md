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

> **No release has been published yet.** Build it yourself, or get
> `OverlayMod.exe` from someone who has.

```powershell
git clone https://github.com/Darkpunkki/overlay-mod
cd overlay-mod
./scripts/publish.ps1
```

That produces `publish/OverlayMod.exe`. Put it in a folder of its own — it writes
its settings and history into an `appdata` folder beside itself.

It is a single file and needs **nothing else installed**: no .NET, no runtime, no
installer. Windows 64-bit only.

Windows will warn that the publisher is unknown, because the executable is
unsigned. Choose **More info → Run anyway**.

### Sharing it with someone

Send them the one `.exe`. That is genuinely all they need — verified on a machine
with no .NET available. Worth telling them:

- It is about **180 MB**, so use a file-sharing service rather than email.
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
| **No-Hit** | Total hits | Hits taken, against your best for that boss |
| **Deathless** | Deaths | Deaths, against your best for that boss |
| **Any%** | Time | Split time, against your best for that boss |
| **All Bosses** | Time | Split time, plus approach-vs-boss hit breakdown |

A run is never failed automatically. Take a hit in a No-Hit run and it keeps
counting — finish the attempt and see how it compares.

### Personal bests

Two kinds, and they are earned differently:

- **Per boss** — banked the moment that boss dies, whether or not you finish. An
  abandoned run still improves your best Iudex.
- **Whole run** — only from a run you actually completed.

## Routes

| Route | Splits |
|---|---|
| **Quick route** | 13 — a normal completion |
| **All Bosses (main game)** | 19 |
| **All Bosses (with DLC)** | 25 |
| **Demo** | 3 — for trying things out without the game |

Routes are JSON files in `appdata/routes/`. Edit them freely — add splits,
reorder, rename — then press **Reload from disk**. A route you delete stays
deleted; **Restore built-in routes** brings back any that are missing.

If a split ever fails to advance on its own, the **Split** hotkey moves it along.

## Customising the look

The control panel's **Appearance** section changes size, colours, panel
transparency and how many split rows show, with a live preview on a chequerboard
so you can see what is see-through. Changes apply immediately, including in OBS.

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
| A boss kill did not split | `http://127.0.0.1:8777/api/diagnostics?flag=<id>` reports every step of the lookup. Split manually with `Ctrl+Alt+D` meanwhile |
| Hotkeys do nothing | Another app may own the combination; the control panel marks unbound ones |

Known limitations:

- **Fall damage counts as a hit.** Telling damage sources apart is not built yet.
- **The approach-vs-boss breakdown is always empty**, because detecting when a
  boss fight is active needs a memory offset that has not been found. It only
  affects the All Bosses profile.
- **Auto-splitting has been confirmed to read boss flags correctly**, but has not
  yet been watched through a full run.

## More

- [docs/LIVE-TESTING.md](docs/LIVE-TESTING.md) — launching offline without EAC,
  and what still needs checking against the real game
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — building, testing, architecture
- [docs/PLAN.md](docs/PLAN.md) — the implementation plan and design decisions

## Credits

DS3 memory layouts are documented by the reverse-engineering community. Pointer
*facts* — signatures, offsets, and the boss event-flag table — were referenced
from the open-source [SoulSplitter](https://github.com/FrankvdStam/SoulSplitter),
[darksoulsiii-practice-tool](https://github.com/veeenu/darksoulsiii-practice-tool),
and community Cheat Engine tables, then re-implemented independently here. No
code was copied from those (GPL and other-licensed) projects.

## License

MIT — see [LICENSE](LICENSE).
