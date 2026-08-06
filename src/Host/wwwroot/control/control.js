// Control page: pick the route and challenge, and drive the run manually.
//
// Selection lives on the host, not here, so this page is just a view over
// /api/routes. It re-reads that after every change rather than tracking state
// locally, which keeps it correct if something else changes the selection.

(() => {
  "use strict";

  const el = (id) => document.getElementById(id);

  const dom = {
    challenges: el("challenges"),
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
    overlayUrl: el("overlayUrl"),
    hotkeys: el("hotkeys"),
    quit: el("quit"),
    toast: el("toast"),
  };

  let catalogue = null;
  let toastTimer = 0;

  // --- appearance ---
  //
  // Each control maps to one field. Sliders carry a formatter for their readout;
  // colours need none. Wiring them from a table keeps adding a setting to one
  // line here rather than a block of near-identical handlers.
  const APPEARANCE = [
    { key: "scale", input: "apScale", out: "apScaleOut", number: true, format: (v) => `${(+v).toFixed(2)}×` },
    { key: "plateOpacity", input: "apPlateOpacity", out: "apPlateOpacityOut", number: true, format: (v) => `${Math.round(v * 100)}%` },
    { key: "shadowStrength", input: "apShadow", out: "apShadowOut", number: true, format: (v) => `${Math.round(v * 100)}%` },
    { key: "visibleSplits", input: "apSplits", out: "apSplitsOut", number: true, format: (v) => `${v}` },
    { key: "accent", input: "apAccent" },
    { key: "text", input: "apText" },
    { key: "dim", input: "apDim" },
    { key: "ahead", input: "apAhead" },
    { key: "behind", input: "apBehind" },
    { key: "plate", input: "apPlate" },
  ];

  let appearance = null;
  let appearanceTimer = 0;

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

  // --- appearance editing ---

  function showAppearance(settings) {
    appearance = settings;

    for (const f of APPEARANCE) {
      const input = el(f.input);
      if (!input) continue;
      input.value = settings[f.key];
      if (f.out && f.format) el(f.out).textContent = f.format(settings[f.key]);
    }
  }

  // Dragging a slider fires continuously; saving on every event would mean a
  // request per pixel. Show the change at once, persist once it settles.
  function onAppearanceInput(field, raw) {
    const value = field.number ? Number.parseFloat(raw) : raw;
    appearance = { ...appearance, [field.key]: field.key === "visibleSplits" ? Math.round(value) : value };

    if (field.out && field.format) el(field.out).textContent = field.format(value);

    clearTimeout(appearanceTimer);
    appearanceTimer = setTimeout(saveAppearance, 150);
  }

  async function saveAppearance() {
    try {
      const { settings } = await post("/api/appearance", appearance);
      appearance = settings;
    } catch (err) {
      toast(`Could not save appearance: ${err.message}`);
    }
  }

  async function loadAppearance() {
    const { settings } = await (await fetch("/api/appearance")).json();
    showAppearance(settings);
  }

  for (const field of APPEARANCE) {
    const input = el(field.input);
    if (input) input.addEventListener("input", () => onAppearanceInput(field, input.value));
  }

  el("apReset").addEventListener("click", async () => {
    try {
      const { settings } = await post("/api/appearance/reset");
      showAppearance(settings);
      toast("Appearance reset");
    } catch (err) {
      toast(`Reset failed: ${err.message}`);
    }
  });

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

  loadCatalogue().catch((err) => toast(`Could not load routes: ${err.message}`));
  loadHotkeys().catch(() => { /* hotkeys are optional; the buttons still work */ });
  loadAppearance().catch((err) => toast(`Could not load appearance: ${err.message}`));
})();
