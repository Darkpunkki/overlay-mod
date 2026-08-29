# Changelog

## 0.4.0

Manual hit corrections, and an overlay that narrows when every split name is
short.

Upgrading changes nothing about stored history: personal bests keep their
meaning, and a run parked by an earlier version resumes exactly as it was.

### Added

- **Manual hit corrections**, under **Run** on the control panel: every split
  the run has reached, with a **+** and **−** beside it. For when a detector
  called a real hit a fall, or a hit never registered at all — the player gets
  the last word. Correcting a split whose boss is already dead re-files that
  boss's banked best as if the count had been right when it died, so a miscount
  never stands as an unbeatable target. Hits only: damage is measured, not
  guessed, so there is nothing there to correct.

- **The overlay narrows when every split name is short.** Its width has a floor
  sized for the full boss names; once every name in the route is short — **Use
  short names**, or names of your own — that floor is mostly empty plate, so it
  drops and the overlay covers less of the capture. Judged on the whole route
  rather than the rows on screen, so the width never changes mid-run as the
  list scrolls.

## 0.3.0

Attempts, a route editor, custom split names, and a glitchless route that splits
on Anri.

### Added

- **An attempt counter**, on the overlay beside the challenge name and on the
  control panel. Counted per route *and* challenge — "my 300th No Hit attempt"
  and "my 4th Speedrun of the same route" are separate tallies — and it goes up
  whenever a run starts: loading into the world, resetting, or starting a fresh
  character. Nothing shows until the first attempt.

  **You can set it by hand.** Nobody starts using this on their first attempt,
  and a counter that insists on starting from one is a counter you would ignore.

- **A route editor**, on the control panel. Create a route, duplicate one,
  rename it, delete it; reorder splits, remove them, and add them from a list of
  every boss the build knows about — which brings the boss's event flag with it,
  so the split advances on its own. Anything you type instead is a manual split
  and says so.

  Route files are still plain JSON in `appdata/routes/` and still hand-editable.
  This is the same thing without the JSON.

- **Custom split names.** Call Soul of Cinder "Cinder" and Lothric, Younger
  Prince "Twin Princes". **Use short names** fills in every boss at once, and any
  name you set yourself outranks the preset.

  Only the display changes: personal bests are filed under the name in the route
  file, so renaming here never loses the history behind a boss. That is exactly
  why this exists instead of editing the route.

- **A separate size for the clock**, on top of the overall size. The clock is
  read from across a room and the split list is not, so scaling everything just
  to get a bigger clock cost the space the splits needed.

- **A new route: "Glitchless route, Anri"** — Gundyr, Vordt, **Anri**, Sage,
  Deacons, Abyss Watchers, Wolnir, Pontiff, Aldrich, Yhorm, Dancer, Dragonslayer
  Armour, Twin Princes, Soul of Cinder. All fourteen splits advance on their own.

  **The Anri split fires when you receive Anri's Straight Sword.** Dark Souls III
  records picking an item up as an event flag of its own — that is how the game
  knows not to offer it twice — so this reads the exact moment you were going to
  split on anyway, through the flag machinery that is already here and already
  confirmed against a live game. No new memory reads were needed for it.

  Existing installs get the route from **Restore built-in routes**, since routes
  are only seeded into an empty folder.

### Changed

- **Dying counts as a hit, whatever killed you.** Falls and poison were being set
  aside from the No Hit count wherever they landed — including when they landed
  fatally, so falling to your death or letting a poisoning finish you left the
  run reading as clean. The fatal blow now skips both classifiers and counts as
  one hit, once. A survivable fall is still not a hit, and the ticks before a
  fatal one are still poison; it is the killing blow that changes.

  This is the settled convention among No-Hit runners, and it is the one reading
  that cannot make an invalid run look valid.

### Fixed

- **`?scale=` and `?splits=` on the overlay URL work again.** The appearance
  settings arrive a fraction of a second after the page reads its query string
  and were overwriting it, which looks exactly like the parameter doing nothing.
  A setting asked for by hand in the URL now wins.

### Notes for anyone upgrading

Nothing to do. Settings, personal bests and existing routes are untouched, and
nothing is renamed until you ask for it.

Two things worth knowing:

- **Attempts start from zero**, because there was nothing to count before. Set
  the number by hand under **Run → Attempts** if you are carrying a tally over.
- **Renaming a route in the editor starts its personal bests over**, because
  those are filed under the route's name. The editor warns before it happens.
  Renaming a *split* does not — that is what custom split names are for.

## 0.2.4

Two ways No Hit charged a hit for damage the player did not take.

### Fixed

- **Resting at a bonfire while poisoned no longer costs a hit.** The rhythm that
  identifies poison was being demanded of *every* tick, not just used to
  recognise the effect — so the ordinary end of an ordinary poisoning orphaned a
  tick and billed it. Two ways that happened:

  - the last tick before the bonfire cures you lands off the beat, out of step
    with the ones before it;
  - the first tick after any heal is a bigger bite, because the bite is a share
    of your health — so **Estus mid-fight did this too**.

  Once poison has shown itself, it no longer has to keep proving it. Ticks are
  still bounded in size and spacing, so what continues an effect is still small
  and still slow.

  A short poisoning cured before it could be confirmed — poison procs as you
  reach the bonfire, three ticks, then you sit — is now resolved as poison too,
  but only when the ticks are identical to a precision combat cannot produce.
  Three ordinary light blows still count as three hits.

- **Dropping to a ledge and immediately sliding further is one fall, not a hit.**
  Fall damage was measured by looking back half a second, which sees the end of a
  long descent and not the drop that set it up: a two-stage fall read as under
  two metres and got charged as a hit. It is now measured from where the descent
  actually began, however long it took.

  Walking down a slope is still not a fall however far it goes, and a fall
  followed by a long trudge downhill stops counting as the fall.

- **The drop distance in the damage log is now the real one.** It was only ever
  the last half-second of it, which understated every fall — including the ones
  it correctly identified — and that column is what the thresholds get tuned
  against.

### Notes for anyone upgrading

Nothing to do. Settings and personal bests are untouched.

## 0.2.3

**0.2.2's poison detection did nothing at all.** If you installed it and poison
still counted as hits, this is the release you want.

### Fixed

- **Poison and toxic really are excluded from No Hit now.** 0.2.2 was built
  against two guesses about a game nobody had measured, and both were wrong:

  - It required at least **1.2 seconds** between ticks. Poison and toxic tick
    **once a second**, so every tick was thrown out before its size was even
    looked at. That bound was hard-coded, so no setting could rescue it — the
    feature was a silent no-op.
  - It capped a tick at **40 HP**. The bite scales with your health, so 40 is a
    fraction of one character's tick and more than another's whole tick.

  It now looks for a **metronome** instead: four or more small bites of about
  the same size, at gaps about the same as each other. Poison ticks at a fixed
  cadence and combat never does, so the evenness is what gives it away — not the
  speed, and not a guessed magnitude. **The size ceiling is a percentage of your
  maximum health** (8% by default), which covers a fresh character and a late one
  alike, and it degrades to using the highest health seen rather than switching
  off if the game build reads maximum health as zero.

- **The interval ceiling can now be set below 1.5 s.** It could not before,
  which is half of how the above went unnoticed.

### Added

- **The control page now shows what the percentage works out to** — "bites up to
  84 HP can be a tick right now (1050 max health)" — directly above the damage
  list, which is in HP. Comparing a percentage setting against a column of HP
  figures previously meant doing the arithmetic yourself, and that is exactly the
  comparison that tells a wrong setting from a broken detector.

### Notes for anyone upgrading

Nothing to do, and nothing is lost: 0.2.2's tick size was stored in the wrong
unit against a detector that did not work, so it is replaced with the new
default rather than converted. Fall settings and personal bests are untouched.

## 0.2.2

### Fixed

- **Poison and toxic no longer count as hits.** A No Hit run through a swamp
  used to pick up a hit every few seconds for as long as the effect lasted, and
  a dozen phantom hits is the difference between a run you keep and a run you
  throw away. Poison is still damage, so **No Damage counts it exactly as
  before** — only No Hit was ever meant to disagree.

  Like fall damage, this is worked out rather than read from the game: three or
  more small bites of about the same size, at least 1.2 seconds apart, is a
  status effect ticking. Anything that really hurts is over the size threshold
  and counts as a hit the moment it lands. The spacing rule is what keeps a
  melee combo out — several blows do arrive in a row, but they arrive in well
  under a second.

  Because it takes a third bite to be sure, the first two wait a few seconds
  before being called. A genuinely small hit therefore shows up a little late,
  rather than a poison tick showing up at all. It cannot tell poison from
  standing in a fire; both are called damage over time.

### Added

- **Both thresholds are on the control page**, under **What counts as a hit**
  (which is what the *Fall damage* card is now called, since it covers two
  things). Set the bite size and the spacing, or switch the whole thing off and
  have No Hit count poison again.

- **Recent damage now shows how much health each event cost**, alongside the
  drop already measured for it, and names the verdict reached: hit, fall, over
  time, or still deciding. That list is how these thresholds get set — take a
  run through a swamp, then read back what was actually called.

### Notes for anyone upgrading

Nothing to do. Settings tuned in 0.2.1 are carried over, personal bests are
untouched, and a run parked mid-session resumes with its counts intact — poison
already counted as a hit before this release stays counted as one, because
resuming does not rewrite history. Start a new run for a clean number.

## 0.2.1

Two counting bugs. **If 0.2.0 counted nothing for you while the timer kept
running, this is the release you want.**

### Fixed

- **Damage, hits and deaths could all stop together, on some machines only.**
  0.2.0 refused to read health at all unless maximum health also read as a
  positive number — a check meant to prove the reading was real. Nothing before
  it used maximum health for anything. On a build where that particular offset
  reads zero it silently switched off every counter at once, while the run
  timer, which needs no such reading, carried on as if nothing were wrong. Every
  challenge that counts something looked broken; Speedrun looked fine.

  Counting no longer depends on it. A death is now confirmed by lasting: zero
  health across several consecutive readings, which tells a body on the ground
  from a pointer briefly reading zero while the game rebuilds it — and needs no
  extra offset to be right.

- **Starting a new game did not reset the counters** if the previous session had
  been short. The check for "this is a different character" asked whether the
  *previous* run was more than a minute long, which is the wrong side of the
  comparison: after a couple of minutes of testing, New Game looked like an
  ordinary save-point rewind and the old run's hits carried straight over. It
  now asks about the save you just loaded, not the one you left. This bug was in
  0.1.0 too.

- **Hits are no longer counted during loading screens.** 0.2.0 read health
  through them; two readings either side of a load describe different worlds.
  Deaths still watch through the load, which is what stops the death fade hiding
  them.

### Added

- **Health on the control panel**, under **Now**. It is still never shown on the
  overlay — the game already shows it. But damage, hits and deaths are all
  derived from that one reading, so when nothing is counting it is the fact
  worth seeing: a live character reading `0 / 0` means the memory offsets are
  wrong for that build, and no amount of playing will move the counters.

  This is not hypothetical: 0.2.0 counted nothing on an **older Dark Souls III
  patch**, where that reading does not land where OverlayMod expects. Everything
  here is tested against **1.15.2.0**. Older patches move some of these offsets,
  and only some of the moves are known — the Health row is how you find out
  which side of that line your game is on.

## 0.2.0

### Challenges

The four challenges are now **No Damage**, **No Hit**, **Deathless** and
**Speedrun**. Any% and All Bosses are gone as *challenges* — the All Bosses
*routes* are untouched, and can be run under any of the four.

- **No Damage** is what No-Hit used to be: every drop in health counts, fall
  damage included.
- **No Hit** is new. It counts only damage the game dealt you, ignoring what you
  did to yourself landing. See the caveat below — this is a heuristic.
- **Speedrun** shows no hit counter at all: the run clock, and each split's time
  against its own best.

### Fall damage

No Hit tells landing damage apart by watching how far you dropped in the moment
before you lost health. That is a measurement of player height, **not a reading
of what damaged you** — Dark Souls III does record the latter, but through a
pointer chain this project has not found or verified.

So it will get things wrong at the edges, most obviously a real hit taken within
a few frames of touching down. Two things follow from that:

- The thresholds are yours to move, under **Fall damage** on the control panel.
- **Recent damage** on the same panel lists what the detector actually called,
  with the drop it measured for each, so you can check it rather than trust it.

Turning the detector off makes No Hit count exactly what No Damage counts, which
is the honest setting if it is misjudging your route.

### Fixed

- **Deaths were being missed.** A death was detected as a transition from a
  positive health reading to zero, which needs both of the ticks either side of
  it. The game raises its loading flag as the death fade starts, so the tick
  where health first reads zero was often not one the tracker was watching — and
  the death vanished. Zero health is now latched instead, so it counts however
  the readings fall around it, and still only once however long the body lies
  there. Deathless was the challenge this hurt most.

### Overlay

- The route name is gone from the header. It is chosen once and never changes
  mid-run, so on screen it was a label nobody read.
- **Personal-best colouring is mirrored.** Beating a split's best paints the live
  value green and the best red; falling behind swaps them. One green and one red
  per row means the better number is findable without reading the header.
- The approach-versus-boss breakdown is no longer displayed. All Bosses was the
  only challenge that showed it and it is always empty in a real game, because
  detecting an active boss fight needs an offset that has not been found. Both
  segments are still recorded.

### Control panel

- **Every section collapses**, and remembers whether it was open.
- **Splits shown** is now a number you type or step, from 1 to 30, instead of a
  slider capped at 20. (It was always there — it was a slider labelled "Split
  rows" in Appearance.)
- New **Fall damage** section: thresholds, and the recent-damage list.
- The version is shown next to the title, and in the log banner and tray
  tooltip. 0.1.0 printed it nowhere, which made every bug report a round trip.

### Upgrading from 0.1.0

Your `appdata` carries over. Two things to know:

- **Your existing per-boss and whole-run bests become No Damage bests.** They
  were recorded under a counter that included fall damage, which is exactly what
  No Damage means now, so the numbers move across unchanged. **No Hit starts with
  no bests**, because nothing in an old file says which of those hits was the
  ground — inventing one would put an unbeatable target on screen.
- **If you had Any% or All Bosses selected**, you will come back on Speedrun. If
  you had No-Hit selected you will come back on No Hit, which is now the stricter
  reading of the two; pick **No Damage** for the comparison you had before.

Route files naming a removed challenge still load. Backing up
`appdata/records.json` before the first launch costs nothing and the migration
is one-way.

## 0.1.0

First release. Attaching, in-game timer, hit and death tracking, auto-splitting
on boss-defeat flags, routes and challenges, personal bests, appearance
controls, global hotkeys, and a single self-contained executable.
