# Testing against the real game

A walkthrough for the first live session, and a checklist of the things that
have never been confirmed against Dark Souls III. Everything here has been built
and tested against a scripted replay; none of it has met the actual game.

Work through it in order. Each check is quick, and doing them together means one
session rather than five.

---

## Before anything: back up your saves

Reading memory cannot corrupt a save, but you are about to change how the game
launches, and a backup costs nothing.

Copy this folder somewhere safe:

```
%APPDATA%\DarkSoulsIII\
```

Paste that into Explorer's address bar. Inside is a folder named after your Steam
ID containing `DS30000.sl2` — that is your character. Copy the whole
`DarkSoulsIII` folder.

---

## Step 1 — Launch the game offline, without EAC

**This is the one non-negotiable step.** Reading game memory while connected
online can get your account soft-banned. Easy Anti-Cheat must not be running.

Dark Souls III normally launches through `start_protected_game.exe`, which starts
EAC and then the game. Launching the game executable directly skips it — this is
the standard setup for practice tools and offline speedrunning.

1. **Put Steam in offline mode.** Steam menu → **Go Offline…** → **Restart in
   Offline Mode**. Belt and braces: even without EAC this keeps the game from
   reaching the servers.

2. **Find the game folder.** In Steam, right-click **DARK SOULS III** →
   **Manage** → **Browse local files**. Explorer opens at
   `...\steamapps\common\DARK SOULS III\Game\`.

3. **Launch `DarkSoulsIII.exe` directly** from that folder — *not*
   `start_protected_game.exe`, and not the Play button in Steam. Steam must still
   be running (offline is fine) or the game will not start.

4. **Confirm EAC is not running.** Ctrl+Shift+Esc → Details tab → look for
   `EasyAntiCheat.exe` or `start_protected_game.exe`. Neither should be there.
   If either is, you launched the wrong executable — close the game and retry.

> If the game refuses to start this way, stop and tell me what it says rather
> than falling back to the normal launcher. Running with EAC active is the one
> thing worth being strict about.

---

## Step 2 — Start OverlayMod

Run `OverlayMod.exe` (no `--fake` — that is the scripted replay). Order does not
matter; it retries attaching once a second and will pick the game up whenever it
appears.

Open the control page: <http://127.0.0.1:8777/control/>

Under **Now**, the **Game** row should read **in game** once you are loaded into
the world. If it says **not running**, OverlayMod has not attached — see
Troubleshooting.

---

## Step 3 — Choose the challenge and route

On the control page:

- **Challenge** — No Damage, No Hit, Deathless or Speedrun. This decides what the
  overlay shows and what runs are ranked by.
- **Route** — All Bosses (main game), All Bosses (with DLC), Quick route, or Demo.

Both are remembered for next time. Changing either abandons the run in progress,
so choose before you start playing.

---

## Step 4 — The verification checklist

These are the things that have never been observed on the real game. Each has a
clear pass/fail.

### 4.1 — Does it attach at all?

Load into any area. On the control page, **Game** should read **in game**, and
the **Timer** should start counting.

*If this fails nothing else can be tested — go to Troubleshooting.*

### 4.2 — Does the timer track in-game time?

- Play normally for a minute — the timer should advance in step with real time.
- **Walk into a loading screen** (a bonfire warp is easiest). The timer should
  **freeze** during the load and resume afterwards.
- **Quit to the main menu.** The timer should **stop**, not jump to a strange
  value.

The run timer counts in-game time since the run started, not your save's total
playtime, so it starts at zero and climbs.

*Why it matters: the tracker assumes menu and loading-screen readings are
meaningless and ignores them. If the timer jumps to something huge or negative,
say what you saw.*

### 4.3 — Do hits register?

Take a hit from any enemy. The **Hits** total should go up by one, and the active
split's hit count with it.

Then check the edges:
- **Take several hits quickly.** Each distinct hit should count once — a single
  hit must not register as three.
- **Drink an Estus.** Healing must not count as anything.
- **Take fall damage.** It will currently count as a hit — that is known and
  expected. Distinguishing damage sources needs work not yet done.

### 4.4 — Do boss splits fire? *(the big one)*

Kill any boss. The overlay should advance to the next split on its own, within a
second of the death.

Flag *reading* is confirmed working (2026-08-06): a save with Iudex dead reads
him as dead and six other bosses as alive. What has still not been watched is a
flag flipping **during** a run and the split advancing off the back of it.

> ⚠️ **Starting a run on a save with bosses already dead will skip through them
> immediately.** The tracker advances on a flag being set, and a flag set before
> the run began still counts. That is arguably correct — it catches up to where
> you actually are — but it is startling. To watch a split fire properly, start
> from a save where the next boss is still alive.

If a split does not fire, open the diagnostic with the boss dead:

```
http://127.0.0.1:8777/api/diagnostics?flag=14000800
```

Substitute the id for the boss you killed (they are in the route files under
`appdata/routes/`). The `flag` section reports every step of the lookup and, if
it gave up, a `failedAt` naming the step that did. **Paste that whole response
back** — it says far more than a true/false ever could.

The flag lookup walks a chain of pointers through structures whose layout was
reverse-engineered, so "which hop broke" is the only question worth asking.

You can always advance manually with **Ctrl+Alt+D**.

### 4.5 — Does a run survive quitting the game? *(the one I am least sure of)*

1. Start a run, take a hit or two, note the timer and hit count.
2. **Quit Dark Souls III completely.** The overlay should freeze where it was.
3. **Relaunch the game** (same way as Step 1) and load the same character.
4. The run should **resume** — same hit count, timer continuing from where it
   stopped, not restarted.

Then the same test **quitting only to the main menu** and continuing the same
character — that should also resume. And the opposite: quit to the menu and start
a **new character**, which *should* give a fresh run at zero.

The subtlety is that in-game time is written to the save periodically rather than
continuously, so reloading rewinds it to the last save point. The rule tolerates
a rewind of up to five minutes while still treating a barely-played save as a new
character. Every decision is logged with the actual numbers:

```
Back in play: save IGT 1234567ms, last seen 1240000ms, rewind 5433ms -> resuming this run
```

If a run is dropped when it should have resumed, or kept when it should have
restarted, that line from `appdata/overlaymod.log` is exactly what I need.

### 4.6 — Deaths

Switch to **Deathless** so the count is on screen, and die once. The split you
are on should go up by one, and stay at one however long you lie there.

Then die twice more in the same split and check it reads three. This is worth
doing deliberately: deaths used to be detected as a transition between two
consecutive readings, and the game raises its loading flag as the death fade
begins, so the reading that mattered was often not one the tracker was watching.
0.2.0 latches zero health instead. **If a death fails to register, that is a
regression and I want the log line.**

A run is never failed automatically, by design; it keeps counting.

### 4.7 — Fall damage *(the new heuristic — this is the one to watch)*

Select **No Hit**. Then, deliberately:

1. **Take a fall that hurts** — off a ledge you know you survive. The hit count
   should **not** move. The **damage** count under No Damage would have.
2. **Take an ordinary hit on level ground.** The hit count **should** move.
3. Open **What counts as a hit → Recent damage** on the control page. Each event
   shows the health it cost, the drop measured, and the call made. That list is
   the actual test — the counters only show the result.

Then play a normal segment of your route and read the list back. What matters is
whether any *real* hit was written off as a fall, which is the failure that
quietly flatters a run.

If it is misjudging: raise **Drop of at least**, or shorten **Within**. If it is
hopeless on your route, turn it off — No Hit then counts what No Damage counts,
which is honest rather than wrong.

*Why it matters: the detector measures player height, not what dealt the damage.
The game does record the latter, through a pointer chain that has not been found.
Until it is, this is a guess with its working shown.*

### 4.8 — Poison and toxic *(reworked in 0.2.3 — the size ceiling is still a guess)*

**0.2.2's version of this did nothing whatsoever.** It required at least 1.2 s
between ticks; the game ticks at 1 s, so every tick was thrown out, and the bound
was hard-coded so no setting could rescue it. If you are testing 0.2.2, stop —
the answer is already known.

Still on **No Hit**. Walk into the poison swamp below the Road of Sacrifices, or
take a Rotten Pine Resin to the face, and let the effect run its whole course.

1. **The hit count should not move at all** while it ticks. Damage under No
   Damage would have gone up once per second.
2. Open **Recent damage** again. Every tick should read **over time**, the `HP`
   column tells you what a tick actually costs on your character, and the line
   just above the list says what the ceiling currently works out to in HP.
3. **Get hit by something while poisoned.** That hit must still count. This is
   the failure worth hunting: a detector that swallows real hits alongside the
   poison is worse than one that never worked.

The comparison to make is **tick `HP` against the ceiling on that line**. The
default is **8% of maximum health**, which is a guess — if your ticks are bigger,
they will keep counting as hits, and the fix is to raise **Bites no bigger than**
until they stop. If a real hit is showing as **over time**, lower it instead.

The first three bites of any episode read **deciding…** for a couple of seconds
before settling. That is expected: four bites are what make a rhythm, and the
alternative — counting them and taking them back — would make the hit counter
flicker once a second for the whole minute.

*Why it matters: as with falls, this reads the shape of the damage rather than
its cause. The game knows you are poisoned; the pointer chain that says so has
not been found. Both classifiers are guesses with their working shown.*

### 4.9 — Approach vs. boss *(still blocked)*

Both segments are still recorded, and every hit still lands in Approach even
during a boss fight, because nothing sets the boss-fight flag — the memory offset
for boss health has not been found. As of 0.2.0 the breakdown is no longer
displayed, so there is nothing to check here; it returns when the offset does.

---

## Step 5 — Record something

With OBS: Browser Source at `http://127.0.0.1:8777/overlay/`, about **500×420**,
dragged **above** your game capture in the Sources list — the topmost source
draws in front.

Record a couple of minutes including a boss. Then watch it back and check the
overlay is legible over actual gameplay rather than a black background.

---

## Troubleshooting

**"not running" on the control page.**
- Is the game actually running? Check Task Manager for `DarkSoulsIII.exe`.
- Did you launch `DarkSoulsIII.exe` directly? The EAC launcher can prevent
  attaching.
- Try running OverlayMod as administrator — reading another process's memory can
  need it depending on your setup.
- Check `appdata/overlaymod.log` next to the executable. It records every attach
  attempt and why one failed.

**Everything attaches but all the numbers are zero or nonsense.**
The pointer offsets are version-specific. Note your game version (main menu,
bottom corner) and the log contents.

**Hotkeys do nothing.**
Another application may already own `Ctrl+Alt+D`. The control page marks a
binding it could not register. Change them in `appdata/hotkeys.json` and restart.

**It will not close.**
Use **Quit OverlayMod** on the control page, or right-click the notification-area
icon (behind the **^** arrow) → **Exit**. `Ctrl+C` and closing the terminal will
not work — it is a background process.

---

## What to report back

For anything that fails, the useful details are:

1. Which check number.
2. What you expected and what happened.
3. The last twenty lines of `appdata/overlaymod.log`.
4. Your game version, for anything involving wrong numbers.
