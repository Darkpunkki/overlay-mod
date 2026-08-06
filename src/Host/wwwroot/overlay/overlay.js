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
//   ?splits=<n>     how many split rows to show at once (3 - 30)

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

  // How many split rows to show at once, LiveSplit style. The list stays this
  // tall no matter how long the route is, so a 25-boss route needs no more room
  // than a 3-boss one. Override with ?splits=N.
  let visibleSplits = 6;

  // Treat the stream as dead if nothing arrives for this long. The host ticks
  // at 30Hz, so this is a very generous margin.
  const STALE_MS = 2000;

  const EM_DASH = "–";

  let lastMessageAt = 0;
  let appearanceVersion = -1;

  // --- appearance ---

  function rgba(hex, alpha) {
    const m = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex ?? "");
    if (!m) return hex;
    const [r, g, b] = [1, 2, 3].map((i) => Number.parseInt(m[i], 16));
    return `rgb(${r} ${g} ${b} / ${Math.round(alpha * 100)}%)`;
  }

  // Everything the stylesheet reads is a custom property, so restyling is just
  // setting them — no layout code needs to know a theme changed.
  function applyAppearance(s) {
    const root = document.documentElement.style;
    root.setProperty("--om-scale", s.scale);
    root.setProperty("--om-text", s.text);
    root.setProperty("--om-text-dim", s.dim);
    root.setProperty("--om-text-faint", rgba(s.dim, 0.72));
    root.setProperty("--om-accent", s.accent);
    root.setProperty("--om-ahead", s.ahead);
    root.setProperty("--om-behind", s.behind);
    root.setProperty("--om-plate", rgba(s.plate, s.plateOpacity));
    root.setProperty("--om-shadow", `0 1px 3px rgb(0 0 0 / ${Math.round(s.shadowStrength * 100)}%)`);

    if (Number.isFinite(s.visibleSplits)) visibleSplits = s.visibleSplits;
  }

  async function refreshAppearance(version) {
    if (version === appearanceVersion) return;
    appearanceVersion = version;

    try {
      const { settings } = await (await fetch("/api/appearance")).json();
      applyAppearance(settings);
    } catch (err) {
      console.error("[OverlayMod] could not load appearance", err);
    }
  }

  // --- options ---

  function applyOptions() {
    const params = new URLSearchParams(window.location.search);

    const scale = Number.parseFloat(params.get("scale"));
    if (Number.isFinite(scale) && scale > 0) {
      document.documentElement.style.setProperty("--om-scale", Math.min(Math.max(scale, 0.5), 4));
    }

    const splits = Number.parseInt(params.get("splits") ?? "", 10);
    if (Number.isFinite(splits)) visibleSplits = Math.min(Math.max(splits, 3), 30);

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
    const size = visibleSplits;
    if (splits.length <= size) return { from: 0, to: splits.length };

    // Sit the active split about a third of the way down, so there is more
    // room ahead of it than behind — what is coming matters more.
    const before = Math.floor((size - 1) / 3);
    const from = Math.max(0, Math.min(activeIndex - before, splits.length - size));
    return { from, to: from + size };
  }

  // A split shows whichever metric its profile is ranked by, against that
  // split's own best. Comparing hits on a time-ranked run tells you nothing.
  function splitMetric(split, metric) {
    switch (metric) {
      case "Time": return { value: split.igtMs, best: split.pbIgtMs, format: formatTime };
      case "Deaths": return { value: split.deaths, best: split.pbDeaths, format: String };
      default: return { value: split.hits, best: split.pbHits, format: String };
    }
  }

  function renderSplits(state) {
    const { from, to } = windowSplits(state.splits, state.activeIndex);
    const metric = state.display.splitMetric;
    const isTime = metric === "Time";
    const rows = [];

    // A header, because two bare numbers per row are ambiguous otherwise.
    const header = document.createElement("li");
    header.className = "split split--head";
    header.classList.toggle("split--wide", isTime);
    for (const [text, cls] of [["", "split__name"], [metric, "split__value"], ["PB", "split__pb"]]) {
      const cell = document.createElement("span");
      cell.className = cls;
      cell.textContent = text;
      header.append(cell);
    }
    rows.push(header);

    for (let i = from; i < to; i++) {
      const s = state.splits[i];
      const isActive = i === state.activeIndex && state.phase === "Running";
      const started = s.completed || isActive;
      const { value, best, format } = splitMetric(s, metric);

      const li = document.createElement("li");
      li.className = "split";
      li.classList.toggle("split--wide", isTime);
      if (isActive) li.classList.add("is-active");
      else if (s.completed) li.classList.add("is-done");
      else li.classList.add("is-pending");

      const name = document.createElement("span");
      name.className = "split__name";
      name.textContent = s.name;

      const current = document.createElement("span");
      current.className = "split__value";
      if (started) {
        current.textContent = format(value);
        const cls = comparisonClass(value, best);
        if (cls) current.classList.add(cls);
      } else {
        current.textContent = EM_DASH;
      }

      // The best this split has ever been, so progress is legible at a glance.
      const pb = document.createElement("span");
      pb.className = "split__pb";
      pb.textContent = has(best) ? format(best) : EM_DASH;

      li.append(name, current, pb);
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
    // Cheap: only actually fetches when the version has moved.
    refreshAppearance(state.appearanceVersion);

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
