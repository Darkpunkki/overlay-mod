// Subscribes to the host's state stream and renders it.
//
// EventSource reconnects on its own, so a host restart recovers without
// touching OBS. We only track whether data is currently arriving, so the
// overlay can say "disconnected" rather than silently freezing on stale numbers.

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
    totalHits: el("totalHits"),
    totalDeaths: el("totalDeaths"),
    hp: el("hp"),
    status: el("status"),
  };

  // How many splits to show around the active one, LiveSplit style.
  const WINDOW_BEFORE = 2;
  const WINDOW_AFTER = 3;

  // Treat the stream as dead if nothing arrives for this long. The host ticks
  // at 30Hz, so this is a very generous margin.
  const STALE_MS = 2000;

  let lastMessageAt = 0;

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
    const rows = [];

    for (let i = from; i < to; i++) {
      const s = state.splits[i];
      const li = document.createElement("li");
      li.className = "split";
      if (i === state.activeIndex && state.phase === "Running") li.classList.add("is-active");
      else if (s.completed) li.classList.add("is-done");
      else li.classList.add("is-pending");

      const name = document.createElement("span");
      name.className = "split__name";
      name.textContent = s.name;

      const hits = document.createElement("span");
      hits.className = "split__hits";
      hits.textContent = s.hits > 0 ? `${s.hits}✕` : "";

      const time = document.createElement("span");
      time.className = "split__time";
      time.textContent = s.completed || i === state.activeIndex ? formatTime(s.igtMs) : "—";

      li.append(name, hits, time);
      rows.push(li);
    }

    dom.splits.replaceChildren(...rows);
  }

  function render(state) {
    dom.overlay.classList.remove("is-disconnected");
    dom.overlay.classList.toggle("is-boss-active", !!state.bossFightActive);

    dom.runTimer.textContent = formatTime(state.runIgtMs);
    dom.routeName.textContent = state.routeName || "—";
    dom.profileName.textContent = state.profileName || "";

    renderSplits(state);

    const active = state.splits[state.activeIndex];
    if (active && state.phase === "Running") {
      dom.active.hidden = false;
      dom.activeName.textContent = active.name;
      dom.approachHits.textContent = active.approach.hits;
      dom.bossHits.textContent = active.boss.hits;
    } else {
      dom.active.hidden = true;
    }

    dom.totalHits.textContent = state.totalHits;
    dom.totalDeaths.textContent = state.totalDeaths;

    const p = state.player;
    if (state.attached && p.loaded) {
      dom.hp.textContent = `${p.hp}/${p.maxHp}`;
      dom.hp.classList.toggle("is-low", p.maxHp > 0 && p.hp / p.maxHp < 0.3);
    } else {
      dom.hp.textContent = state.attached ? "—" : "no game";
      dom.hp.classList.remove("is-low");
    }
  }

  function markDisconnected(message) {
    dom.overlay.classList.add("is-disconnected");
    dom.status.textContent = message;
  }

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

  // EventSource fires onerror when the connection drops, but a host that hangs
  // without closing the socket would leave the overlay showing stale numbers.
  setInterval(() => {
    if (lastMessageAt && Date.now() - lastMessageAt > STALE_MS) markDisconnected("no data");
  }, 500);

  markDisconnected("connecting…");
})();
