// Subscribes to the host's state stream and renders it.
//
// EventSource reconnects on its own, so a host restart recovers without
// touching OBS. We also watch for the stream going quiet, since a host that
// hangs without closing the socket would leave stale numbers on screen.
//
// What is shown is driven by state.display, which comes from the challenge
// profile. Adding a profile should not mean editing this file.
//
// Query parameters:
//   ?theme=<name>   load themes/<name>.css over the defaults
//   ?scale=<n>      scale the whole overlay (0.5 - 4)

(() => {
  "use strict";

  const el = (id) => document.getElementById(id);

  const dom = {
    overlay: el("overlay"),
    runTimer: el("runTimer"),
    routeName: el("routeName"),
    profileName: el("profileName"),
    splits: el("splits"),
    active: el("active"),
    activeName: el("activeName"),
    approachHits: el("approachHits"),
    bossHits: el("bossHits"),
    primaryLabel: el("primaryLabel"),
    primaryValue: el("primaryValue"),
    primaryPb: el("primaryPb"),
    deathsTotal: el("deathsTotal"),
    totalDeaths: el("totalDeaths"),
    status: el("status"),
  };

  // How many splits to show around the active one, LiveSplit style.
  const WINDOW_BEFORE = 2;
  const WINDOW_AFTER = 3;

  // Treat the stream as dead if nothing arrives for this long. The host ticks
  // at 30Hz, so this is a very generous margin.
  const STALE_MS = 2000;

  const EM_DASH = "–";

  let lastMessageAt = 0;

  // --- options ---

  function applyOptions() {
    const params = new URLSearchParams(window.location.search);

    const scale = Number.parseFloat(params.get("scale"));
    if (Number.isFinite(scale) && scale > 0) {
      document.documentElement.style.setProperty("--om-scale", Math.min(Math.max(scale, 0.5), 4));
    }

    // Only ever build a same-origin path from a restricted character set, so a
    // crafted URL cannot pull in a stylesheet from somewhere else.
    const theme = params.get("theme");
    if (theme && /^[a-z0-9-]{1,32}$/.test(theme)) {
      const link = document.createElement("link");
      link.rel = "stylesheet";
      link.href = `themes/${theme}.css`;
      document.head.append(link);
    }
  }

  // --- formatting ---

  function formatTime(ms) {
    if (!Number.isFinite(ms) || ms <= 0) return "0:00.000";
    const total = Math.floor(ms);
    const millis = total % 1000;
    const secs = Math.floor(total / 1000) % 60;
    const mins = Math.floor(total / 60000) % 60;
    const hours = Math.floor(total / 3600000);
    const pad = (n, w) => String(n).padStart(w, "0");
    return hours > 0
      ? `${hours}:${pad(mins, 2)}:${pad(secs, 2)}.${pad(millis, 3)}`
      : `${mins}:${pad(secs, 2)}.${pad(millis, 3)}`;
  }

  const has = (v) => v !== null && v !== undefined;

  // Lower is better for every metric we track: hits, deaths and time alike.
  function comparisonClass(value, best) {
    if (!has(best)) return "";
    if (value < best) return "is-ahead";
    if (value > best) return "is-behind";
    return "is-tied";
  }

  // --- rendering ---

  // Which slice of the split list to show, clamped to the ends so the list
  // stays a constant height instead of shrinking at the start and finish.
  function windowSplits(splits, activeIndex) {
    const size = WINDOW_BEFORE + WINDOW_AFTER + 1;
    if (splits.length <= size) return { from: 0, to: splits.length };
    let from = activeIndex - WINDOW_BEFORE;
    from = Math.max(0, Math.min(from, splits.length - size));
    return { from, to: from + size };
  }

  function renderSplits(state) {
    const { from, to } = windowSplits(state.splits, state.activeIndex);
    const showTimes = state.display.showSplitTimes;
    const rows = [];

    for (let i = from; i < to; i++) {
      const s = state.splits[i];
      const isActive = i === state.activeIndex && state.phase === "Running";
      const started = s.completed || isActive;

      const li = document.createElement("li");
      li.className = "split";
      li.classList.toggle("split--timed", showTimes);
      if (isActive) li.classList.add("is-active");
      else if (s.completed) li.classList.add("is-done");
      else li.classList.add("is-pending");

      const name = document.createElement("span");
      name.className = "split__name";
      name.textContent = s.name;

      const hits = document.createElement("span");
      hits.className = "split__hits";
      if (started) {
        hits.textContent = s.hits;
        const cls = comparisonClass(s.hits, s.pbHits);
        if (cls) hits.classList.add(cls);
      } else {
        hits.textContent = EM_DASH;
      }

      // The best this split has ever been, so progress is legible at a glance.
      const pb = document.createElement("span");
      pb.className = "split__pb";
      pb.textContent = has(s.pbHits) ? s.pbHits : EM_DASH;

      li.append(name, hits, pb);

      if (showTimes) {
        const time = document.createElement("span");
        time.className = "split__time";
        time.textContent = started ? formatTime(s.igtMs) : EM_DASH;
        li.append(time);
      }

      rows.push(li);
    }

    dom.splits.replaceChildren(...rows);
  }

  function renderActiveSegments(state) {
    const active = state.splits[state.activeIndex];
    const show = state.display.showSegmentBreakdown && active && state.phase === "Running";

    dom.active.hidden = !show;
    if (!show) return;

    dom.activeName.textContent = active.name;
    dom.approachHits.textContent = active.approach.hits;
    dom.bossHits.textContent = active.boss.hits;
  }

  function renderTotals(state) {
    const primary = state.primary;
    const isTime = primary.metric === "Time";
    const format = (v) => (isTime ? formatTime(v) : v);

    dom.primaryLabel.textContent = primary.metric;
    dom.primaryValue.textContent = format(primary.value);
    dom.primaryValue.className = "total__value";
    const cls = comparisonClass(primary.value, primary.best);
    if (cls) dom.primaryValue.classList.add(cls);

    dom.primaryPb.textContent = has(primary.best) ? `pb ${format(primary.best)}` : `pb ${EM_DASH}`;

    dom.deathsTotal.hidden = !state.display.showDeaths;
    dom.totalDeaths.textContent = state.totalDeaths;
  }

  function renderStatus(state) {
    // Healthy means: receiving data, and the game is actually there. Anything
    // else gets one quiet line rather than a banner in the middle of a take.
    if (!state.attached) {
      dom.overlay.classList.remove("is-healthy");
      dom.status.textContent = "waiting for game…";
      return;
    }
    dom.overlay.classList.add("is-healthy");
  }

  function render(state) {
    dom.overlay.classList.toggle("is-boss-active", !!state.bossFightActive);

    dom.runTimer.textContent = formatTime(state.runIgtMs);
    dom.routeName.textContent = state.routeName || EM_DASH;
    dom.profileName.textContent = state.profileName || "";

    renderSplits(state);
    renderActiveSegments(state);
    renderTotals(state);
    renderStatus(state);
  }

  function markDisconnected(message) {
    dom.overlay.classList.remove("is-healthy");
    dom.status.textContent = message;
  }

  // --- stream ---

  applyOptions();
  markDisconnected("connecting…");

  const stream = new EventSource("/events");

  stream.onmessage = (event) => {
    lastMessageAt = Date.now();
    try {
      render(JSON.parse(event.data));
    } catch (err) {
      console.error("[OverlayMod] bad payload", err);
    }
  };

  stream.onerror = () => markDisconnected("reconnecting…");

  setInterval(() => {
    if (lastMessageAt && Date.now() - lastMessageAt > STALE_MS) markDisconnected("no data");
  }, 500);
})();
