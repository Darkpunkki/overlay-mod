# Changelog

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
