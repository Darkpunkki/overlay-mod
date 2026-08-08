// Control page: pick the route and challenge, and drive the run manually.
//
// Selection lives on the host, not here, so this page is just a view over
// /api/routes. It re-reads that after every change rather than tracking state
// locally, which keeps it correct if something else changes the selection.

(() => {
  "use strict";

  const el = (id) => document.getElementById(id);

  const dom = {
    version: el("version"),
    challenges: el("challenges"),
    challengeNote: el("challengeNote"),
    routes: el("routes"),
    reload: el("reload"),
    restore: el("restore"),
    start: el("start"),
    split: el("split"),
    reset: el("reset"),
    factGame: el("factGame"),
    factPhase: el("factPhase"),
    factTimer: el("factTimer"),
    factSplit: el("factSplit"),
    factHealth: el("factHealth"),
    overlayUrl: el("overlayUrl"),
    hotkeys: el("hotkeys"),
    fdEvents: el("fdEvents"),
    quit: el("quit"),
    toast: el("toast"),
  };

  let catalogue = null;
  let toastTimer = 0;

  // What each challenge actually measures. Four names on four buttons do not
  // explain themselves, and the difference between the first two is the whole
  // point of this release.
  const CHALLENGE_NOTES = {
    NoDamage: "Counts every drop in health, fall damage included. Nothing is guessed at.",
    NoHit: "Counts damage an enemy dealt you. Falls and poison are excluded — see What counts as a hit, below.",
    Deathless: "Counts deaths. Damage is still recorded underneath, it just is not shown.",
    Speedrun: "Ranked on time. No hit counter at all: the run clock, and each split against its best.",
  };

  // --- collapsible sections ---
  //
  // Which parts of this page matter depends on whether you are setting up or
  // mid-session, so the state is remembered rather than reset on every visit.

  const SECTIONS_KEY = "overlaymod.sections";

  function restoreSections() {
    let saved = {};
    try { saved = JSON.parse(localStorage.getItem(SECTIONS_KEY) ?? "{}"); } catch { /* defaults */ }

    for (const card of document.querySelectorAll("details.card")) {
      if (typeof saved[card.id] === "boolean") card.open = saved[card.id];

      card.addEventListener("toggle", () => {
        saved[card.id] = card.open;
        try { localStorage.setItem(SECTIONS_KEY, JSON.stringify(saved)); } catch { /* private mode */ }
        if (card.id === "cardFall" && card.open) loadDamageEvents();
      });
    }
  }

  // --- appearance ---
  //
  // Each control maps to one field. Sliders carry a formatter for their readout;
  // colours and the split count need none. Wiring them from a table keeps adding
  // a setting to one line here rather than a block of near-identical handlers.
  const APPEARANCE = [
    { key: "scale", input: "apScale", out: "apScaleOut", number: true, format: (v) => `${(+v).toFixed(2)}×` },
    { key: "plateOpacity", input: "apPlateOpacity", out: "apPlateOpacityOut", number: true, format: (v) => `${Math.round(v * 100)}%` },
    { key: "shadowStrength", input: "apShadow", out: "apShadowOut", number: true, format: (v) => `${Math.round(v * 100)}%` },
    { key: "visibleSplits", input: "apSplits", number: true, integer: true },
    { key: "accent", input: "apAccent" },
    { key: "text", input: "apText" },
    { key: "dim", input: "apDim" },
    { key: "ahead", input: "apAhead" },
    { key: "behind", input: "apBehind" },
    { key: "plate", input: "apPlate" },
  ];

  // Fall-damage thresholds. Same shape, different endpoint.
  const FALL = [
    { key: "enabled", input: "fdEnabled", boolean: true },
    { key: "descentMetres", input: "fdDescent", out: "fdDescentOut", number: true, format: (v) => `${(+v).toFixed(1)} m` },
    { key: "windowMs", input: "fdWindow", out: "fdWindowOut", number: true, integer: true, format: (v) => `${Math.round(v)} ms` },
  ];

  // Poison, toxic and anything else that ticks. Shares the endpoint with FALL,
  // but is posted on its own so editing one card cannot clobber the other.
  const OVER_TIME = [
    { key: "enabled", input: "dotEnabled", boolean: true },
    { key: "maxTickDamage", input: "dotMaxTick", out: "dotMaxTickOut", number: true, integer: true, format: (v) => `${Math.round(v)} HP` },
    { key: "maxIntervalMs", input: "dotInterval", out: "dotIntervalOut", number: true, integer: true, format: (v) => `${Math.round(v)} ms` },
  ];

  let appearance = null;
  let appearanceTimer = 0;
  let fallDamage = null;
  let fallTimer = 0;
  let overTime = null;
  let overTimeTimer = 0;

  function toast(message) {
    dom.toast.textContent = message;
    dom.toast.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { dom.toast.hidden = true; }, 2500);
  }

  function formatTime(ms) {
    if (!Number.isFinite(ms) || ms <= 0) return "0:00.000";
    const total = Math.floor(ms);
    const pad = (n, w) => String(n).padStart(w, "0");
    const millis = total % 1000;
    const secs = Math.floor(total / 1000) % 60;
    const mins = Math.floor(total / 60000) % 60;
    const hours = Math.floor(total / 3600000);
    return hours > 0
      ? `${hours}:${pad(mins, 2)}:${pad(secs, 2)}.${pad(millis, 3)}`
      : `${mins}:${pad(secs, 2)}.${pad(millis, 3)}`;
  }

  async function post(path, body) {
    const res = await fetch(path, {
      method: "POST",
      headers: body ? { "Content-Type": "application/json" } : {},
      body: body ? JSON.stringify(body) : undefined,
    });
    if (!res.ok) {
      let message = `${res.status}`;
      try { message = (await res.json()).error ?? message; } catch { /* keep status */ }
      throw new Error(message);
    }
    return res.json();
  }

  function choice({ label, meta, warn, selected, onPick }) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "choice";
    button.classList.toggle("is-selected", selected);

    const name = document.createElement("span");
    name.textContent = label;
    button.append(name);

    if (meta) {
      const m = document.createElement("span");
      m.className = "choice__meta";
      m.textContent = meta;
      button.append(m);
    }

    if (warn) {
      const w = document.createElement("span");
      w.className = "choice__warn";
      w.textContent = warn;
      button.append(w);
    }

    button.addEventListener("click", onPick);
    return button;
  }

  async function select(route, challenge) {
    try {
      await post("/api/routes/select", { route, challenge });
      await loadCatalogue();
      toast(`Now running ${route} as ${challenge}`);
    } catch (err) {
      toast(`Could not select: ${err.message}`);
    }
  }

  function render() {
    const { selected, challenges, routes } = catalogue;

    dom.challenges.replaceChildren(...challenges.map((c) =>
      choice({
        label: c.name,
        selected: c.type === selected.challenge,
        onPick: () => select(selected.route, c.type),
      })));

    dom.challengeNote.textContent = CHALLENGE_NOTES[selected.challenge] ?? "";

    dom.routes.replaceChildren(...routes.map((r) => {
      const manual = r.splits - r.autoSplits;
      return choice({
        label: r.name,
        meta: `${r.splits} splits · ${r.autoSplits} auto-advance`,
        // Unconfirmed flag ids fail silently, so this is worth saying plainly.
        warn: manual > 0
          ? `${manual} need a manual split — boss flags not confirmed yet`
          : (r.flagsVerified ? "" : "boss flags not verified against a live game"),
        selected: r.name === selected.route,
        onPick: () => select(r.name, selected.challenge),
      });
    }));
  }

  async function loadCatalogue() {
    catalogue = await (await fetch("/api/routes")).json();
    render();
  }

  async function loadAbout() {
    const { version } = await (await fetch("/api/about")).json();
    dom.version.textContent = `v${version}`;
  }

  async function loadHotkeys() {
    const { bindings } = await (await fetch("/api/hotkeys")).json();

    if (!bindings.length) {
      dom.hotkeys.replaceChildren(
        Object.assign(document.createElement("p"), {
          className: "hint",
          textContent: "Global hotkeys are off. Use the buttons above.",
        }));
      return;
    }

    dom.hotkeys.replaceChildren(...bindings.map((b) => {
      const chip = document.createElement("span");
      chip.className = "key";
      chip.classList.toggle("is-inactive", !b.active);

      const combo = document.createElement("span");
      combo.className = "key__combo";
      combo.textContent = b.key;

      const action = document.createElement("span");
      action.className = "key__action";
      action.textContent = b.active ? b.action : `${b.action} — not bound`;

      chip.append(combo, action);
      return chip;
    }));
  }

  // --- settings editing, shared by appearance and fall damage ---

  function readInput(field, input) {
    if (field.boolean) return input.checked;
    if (!field.number) return input.value;

    const value = Number.parseFloat(input.value);
    if (!Number.isFinite(value)) return null; // mid-edit, or emptied
    return field.integer ? Math.round(value) : value;
  }

  function show(fields, settings) {
    for (const f of fields) {
      const input = el(f.input);
      if (!input) continue;

      if (f.boolean) input.checked = !!settings[f.key];
      else input.value = settings[f.key];

      if (f.out && f.format) el(f.out).textContent = f.format(settings[f.key]);
    }
  }

  // Dragging a slider fires continuously; saving on every event would mean a
  // request per pixel. Show the change at once, persist once it settles.
  function onEdit(field, input, current, apply, schedule) {
    const base = current();
    const value = readInput(field, input);
    if (base === null || value === null) return; // not loaded yet, or mid-edit

    apply({ ...base, [field.key]: value });
    if (field.out && field.format) el(field.out).textContent = field.format(value);
    schedule();
  }

  // The server clamps what it is sent. Reflect anything it changed, so a field
  // can never show a value that is not the one in effect — but leave whatever
  // is being typed in alone until it is committed.
  function reconcile(fields, sent, applied) {
    for (const f of fields) {
      if (applied[f.key] === sent[f.key]) continue;

      const input = el(f.input);
      if (input && document.activeElement !== input) {
        if (f.boolean) input.checked = !!applied[f.key];
        else input.value = applied[f.key];
      }
      if (f.out && f.format) el(f.out).textContent = f.format(applied[f.key]);
    }
  }

  async function saveAppearance() {
    const sent = appearance;
    try {
      const { settings } = await post("/api/appearance", sent);
      appearance = settings;
      reconcile(APPEARANCE, sent, settings);
    } catch (err) {
      toast(`Could not save appearance: ${err.message}`);
    }
  }

  async function loadAppearance() {
    const { settings } = await (await fetch("/api/appearance")).json();
    appearance = settings;
    show(APPEARANCE, settings);
  }

  async function saveFallDamage() {
    const sent = fallDamage;
    try {
      const { fallDamage: applied } = await post("/api/tracking", { fallDamage: sent });
      fallDamage = applied;
      reconcile(FALL, sent, applied);
    } catch (err) {
      toast(`Could not save fall settings: ${err.message}`);
    }
  }

  async function saveOverTime() {
    const sent = overTime;
    try {
      const { damageOverTime: applied } = await post("/api/tracking", { damageOverTime: sent });
      overTime = applied;
      reconcile(OVER_TIME, sent, applied);
    } catch (err) {
      toast(`Could not save poison settings: ${err.message}`);
    }
  }

  async function loadTracking() {
    const { fallDamage: fall, damageOverTime: dot } = await (await fetch("/api/tracking")).json();
    fallDamage = fall;
    overTime = dot;
    show(FALL, fall);
    show(OVER_TIME, dot);
  }

  for (const field of APPEARANCE) {
    const input = el(field.input);
    if (!input) continue;
    input.addEventListener("input", () => {
      onEdit(field, input,
        () => appearance, (next) => { appearance = next; },
        () => { clearTimeout(appearanceTimer); appearanceTimer = setTimeout(saveAppearance, 150); });
    });
  }

  for (const field of FALL) {
    const input = el(field.input);
    if (!input) continue;
    input.addEventListener(field.boolean ? "change" : "input", () => {
      onEdit(field, input,
        () => fallDamage, (next) => { fallDamage = next; },
        () => { clearTimeout(fallTimer); fallTimer = setTimeout(saveFallDamage, 150); });
    });
  }

  for (const field of OVER_TIME) {
    const input = el(field.input);
    if (!input) continue;
    input.addEventListener(field.boolean ? "change" : "input", () => {
      onEdit(field, input,
        () => overTime, (next) => { overTime = next; },
        () => { clearTimeout(overTimeTimer); overTimeTimer = setTimeout(saveOverTime, 150); });
    });
  }

  // The split count is a number field, so it can be left half-typed or out of
  // range. Snap it to what is actually in effect once the edit is committed.
  el("apSplits").addEventListener("change", () => {
    if (appearance) el("apSplits").value = appearance.visibleSplits;
  });

  for (const [id, step] of [["apSplitsDown", -1], ["apSplitsUp", 1]]) {
    el(id).addEventListener("click", () => {
      const input = el("apSplits");
      const next = Math.min(30, Math.max(1, (Number.parseInt(input.value, 10) || 6) + step));
      input.value = next;
      input.dispatchEvent(new Event("input"));
    });
  }

  el("apReset").addEventListener("click", async () => {
    try {
      const { settings } = await post("/api/appearance/reset");
      appearance = settings;
      show(APPEARANCE, settings);
      toast("Appearance reset");
    } catch (err) {
      toast(`Reset failed: ${err.message}`);
    }
  });

  el("fdReset").addEventListener("click", async () => {
    try {
      const { fallDamage: fall, damageOverTime: dot } = await post("/api/tracking/reset");
      fallDamage = fall;
      overTime = dot;
      show(FALL, fall);
      show(OVER_TIME, dot);
      toast("Hit-detection settings reset");
    } catch (err) {
      toast(`Reset failed: ${err.message}`);
    }
  });

  // --- recent damage, for checking the detectors' calls ---

  // Kind comes from the engine's enum. "Pending" means small enough to be a
  // poison tick and still waiting on the bite after it, which is worth naming
  // rather than showing as a hit that may be about to disappear.
  const VERDICTS = {
    Fall: "fall",
    OverTime: "over time",
    Pending: "deciding…",
    Hit: "hit",
  };

  async function loadDamageEvents() {
    let events = [];
    try {
      ({ events } = await (await fetch("/api/hits")).json());
    } catch (err) {
      toast(`Could not read recent damage: ${err.message}`);
      return;
    }

    if (!events.length) {
      dom.fdEvents.replaceChildren(
        Object.assign(document.createElement("p"), {
          className: "hint",
          textContent: "Nothing yet. Take some damage and refresh.",
        }));
      return;
    }

    dom.fdEvents.replaceChildren(...events.map((e) => {
      const row = document.createElement("div");
      row.className = "event";
      row.classList.toggle("is-fall", e.kind === "Fall");
      row.classList.toggle("is-overtime", e.kind === "OverTime");
      row.classList.toggle("is-pending", e.kind === "Pending");
      row.classList.toggle("is-fatal", e.fatal);

      for (const [text, cls] of [
        [formatTime(e.igtMs), "event__time"],
        [e.split || "—", "event__split"],
        [`${e.damage} HP`, "event__size"],
        [`${e.descentMetres.toFixed(1)} m`, "event__drop"],
        [e.fatal ? "death" : (VERDICTS[e.kind] ?? "hit"), "event__verdict"],
      ]) {
        const cell = document.createElement("span");
        cell.className = cls;
        cell.textContent = text;
        row.append(cell);
      }

      return row;
    }));
  }

  el("fdRefresh").addEventListener("click", loadDamageEvents);

  // --- health, read straight from the game ---
  //
  // Deliberately not part of the state stream: the overlay must never show
  // health, so it has no business in the view model at thirty frames a second.
  // This is a diagnostic, polled slowly, and it answers the one question that
  // matters when nothing is counting — is the health read working at all?

  async function loadHealth() {
    let d;
    try {
      d = await (await fetch("/api/diagnostics")).json();
    } catch {
      dom.factHealth.textContent = "—";
      return;
    }

    if (!d.snapshot) {
      dom.factHealth.textContent = d.note ? "not applicable (demo source)" : "—";
      return;
    }

    if (!d.attached) {
      dom.factHealth.textContent = "—";
      return;
    }

    const { hp, maxHp } = d.snapshot;
    if (!d.snapshot.playerLoaded) {
      dom.factHealth.textContent = "not in a level";
    } else if (maxHp > 0) {
      dom.factHealth.textContent = `${hp} / ${maxHp}`;
    } else {
      // Damage, hits and deaths are all derived from this reading, so a live
      // character reading zero explains every counter sitting still at once.
      dom.factHealth.textContent =
        `${hp} / ${maxHp} — not reading correctly for this game build`;
    }
  }

  loadHealth();
  setInterval(loadHealth, 2000);

  // --- live status, from the same stream the overlay uses ---

  function renderLive(state) {
    dom.factGame.textContent = state.attached
      ? (state.player.loaded ? "in game" : (state.player.loading ? "loading" : "at menu"))
      : "not running";
    dom.factPhase.textContent = state.phase;
    dom.factTimer.textContent = formatTime(state.runIgtMs);

    const active = state.splits[state.activeIndex];
    dom.factSplit.textContent = active
      ? `${state.activeIndex + 1}/${state.splits.length} — ${active.name}`
      : "—";
  }

  dom.overlayUrl.textContent = `${window.location.origin}/overlay/`;

  dom.reload.addEventListener("click", async () => {
    try {
      const { routes } = await post("/api/routes/reload");
      await loadCatalogue();
      toast(`Reloaded ${routes} route${routes === 1 ? "" : "s"}`);
    } catch (err) {
      toast(`Reload failed: ${err.message}`);
    }
  });

  dom.restore.addEventListener("click", async () => {
    try {
      const { added } = await post("/api/routes/restore");
      await loadCatalogue();
      toast(added > 0
        ? `Added ${added} built-in route${added === 1 ? "" : "s"}`
        : "Nothing missing — all built-in routes are already here");
    } catch (err) {
      toast(`Restore failed: ${err.message}`);
    }
  });

  for (const [button, path, label] of [
    [dom.start, "/api/run/start", "Run started"],
    [dom.split, "/api/run/split", "Split"],
    [dom.reset, "/api/run/reset", "Run reset"],
  ]) {
    button.addEventListener("click", async () => {
      try {
        await post(path);
        toast(label);
      } catch (err) {
        toast(`Failed: ${err.message}`);
      }
    });
  }

  const stream = new EventSource("/events");
  stream.onmessage = (event) => {
    try { renderLive(JSON.parse(event.data)); } catch { /* ignore a bad frame */ }
  };

  dom.quit.addEventListener("click", async () => {
    if (!window.confirm("Stop OverlayMod? The overlay will go blank in OBS.")) return;
    try {
      await post("/api/quit");
      // The host is on its way down, so the stream is about to die; say so
      // rather than letting the page look broken.
      stream.close();
      toast("OverlayMod is shutting down. You can close this tab.");
    } catch {
      toast("Shutdown request failed — the host may already have stopped.");
    }
  });

  restoreSections();
  loadCatalogue().catch((err) => toast(`Could not load routes: ${err.message}`));
  loadAbout().catch(() => { /* the version is a nicety, not a requirement */ });
  loadHotkeys().catch(() => { /* hotkeys are optional; the buttons still work */ });
  loadAppearance().catch((err) => toast(`Could not load appearance: ${err.message}`));
  loadTracking().catch((err) => toast(`Could not load hit-detection settings: ${err.message}`));
})();
