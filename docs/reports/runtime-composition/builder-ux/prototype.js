/* Research interaction model only. No Elsa planner, file bridge, host, or network calls. */
const worker = [
  "Primitives", "Serialization", "Mediator", "Events", "Expressions",
  "ActivitiesRuntime", "ActivitiesPrimitives", "ActivitiesControlFlow", "ActivitiesSequence",
  "WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeResumption", "WorkflowsRuntimeTriggers",
  "ApiCapabilities", "WorkflowsRuntimeApi"
];
const diagnostics = [
  "DiagnosticsOpenTelemetry", "DiagnosticsOpenTelemetryEntityFrameworkCore",
  "DiagnosticsStructuredLogs", "DiagnosticsStructuredLogsEntityFrameworkCore"
];
const required = {
  WorkflowsRuntimeEntityFrameworkCore: ["WorkflowsRuntimeResumption"],
  WorkflowsRuntimeResumption: ["Tasks"],
  WorkflowsRuntimeTriggers: ["WorkflowsRuntimeApi"],
  WorkflowsRuntimeApi: ["ApiCapabilities"]
};
const unknown = "AcmeAuditSink";
const allIds = [...new Set([...worker, "Tasks", ...diagnostics])].sort();
const fresh = () => ({ source: "new draft", profile: false, group: false, primary: false, isolated: false, capacity: 256, explicit: {}, lastExport: null, refusal: null, search: "" });
const states = Object.fromEntries(["ledger", "workorder", "expert"].map(id => [id, fresh()]));

function resolve(state) {
  const selected = new Set(state.source === "sample shells.json" ? worker : []);
  if (state.profile) worker.forEach(id => selected.add(id));
  if (state.group) diagnostics.forEach(id => selected.add(id));
  Object.entries(state.explicit).forEach(([id, enabled]) => enabled ? selected.add(id) : selected.delete(id));
  let added = true;
  while (added) {
    added = false;
    for (const id of [...selected]) for (const dependency of required[id] || []) {
      if (!selected.has(dependency) && state.explicit[dependency] !== false) { selected.add(dependency); added = true; }
    }
  }
  return selected;
}

function origin(state, id) {
  if (Object.hasOwn(state.explicit, id)) return "explicit edit";
  if (id === "Tasks" && resolve(state).has(id)) return "required by WorkflowsRuntimeResumption";
  if (state.source === "sample shells.json" && worker.includes(id)) return "imported snapshot";
  if (state.group && diagnostics.includes(id)) return "provisional diagnostics group";
  if (state.profile && worker.includes(id)) return "provisional Worker profile";
  return "available in fixture";
}

function dependents(selected, id) {
  const affected = new Set([id]);
  let changed = true;
  while (changed) {
    changed = false;
    for (const feature of selected) {
      if (!affected.has(feature) && (required[feature] || []).some(dep => affected.has(dep))) {
        affected.add(feature); changed = true;
      }
    }
  }
  return [...affected].filter(feature => selected.has(feature));
}

function featureRows(view, state, selected) {
  const target = view.querySelector("[data-features]");
  const search = state.search.toLowerCase().trim();
  const visible = allIds.filter(id => !search || `${id} ${origin(state, id)}`.toLowerCase().includes(search));
  target.replaceChildren();
  const list = document.createElement("div");
  list.className = "feature-list";
  for (const id of visible) {
    const row = document.createElement("div");
    row.className = "feature-row";
    const label = document.createElement("div"); label.className = "name";
    label.textContent = id;
    const sub = document.createElement("small"); sub.textContent = `${selected.has(id) ? "Enabled" : "Off"} · ${origin(state, id)}`;
    label.append(sub);
    const button = document.createElement("button");
    button.type = "button"; button.dataset.feature = id;
    button.setAttribute("aria-label", `${selected.has(id) ? "Disable" : "Enable"} ${id}`);
    button.textContent = selected.has(id) ? "Disable" : "Enable";
    row.append(label, button); list.append(row);
  }
  if (state.source === "sample shells.json" && (!search || unknown.toLowerCase().includes(search) || "imported unchecked".includes(search))) {
    const row = document.createElement("div"); row.className = "feature-row";
    row.innerHTML = `<div class="name">${unknown}<small>Synthetic sample marker · unknown value is not round-tripped by this mock</small></div><span class="badge warn">Unchecked</span>`;
    list.append(row);
  }
  target.append(list);
}

function render(view) {
  const state = states[view.dataset.view];
  const selected = resolve(state);
  const bindings = state.isolated && state.group ? diagnostics.filter(id => id.endsWith("EntityFrameworkCore") && selected.has(id)) : [];
  view.querySelectorAll("[data-count]").forEach(el => { el.textContent = `${selected.size} selected`; });
  view.querySelectorAll("[data-setting]").forEach(el => { el.value = String(state.capacity); });
  view.querySelectorAll("[data-setting-origin]").forEach(el => { el.textContent = state.source === "sample shells.json" ? `Sample source value 128 · candidate ${state.capacity === 128 ? "unchanged" : `override ${state.capacity}`} · proposed reviewed mapping` : `Fixture default 256 · candidate value ${state.capacity} · proposed reviewed mapping`; });
  view.querySelectorAll("[data-unknown]").forEach(el => { el.textContent = state.source === "sample shells.json" ? "External AcmeAuditSink.FuturePolicy: the real file bridge should preserve its opaque value. This mock does not export or verify that value." : "No external setting in this new-draft fixture."; });
  view.querySelectorAll("[data-act]").forEach(button => {
    if (["profile", "diagnostics", "primary", "isolate"].includes(button.dataset.act)) {
      const selectedAction = { profile: state.profile, diagnostics: state.group, primary: state.primary, isolate: state.isolated }[button.dataset.act];
      button.setAttribute("aria-pressed", String(selectedAction));
    }
  });
  const resource = view.querySelector("[data-resources]");
  resource.replaceChildren();
  const primary = document.createElement("div");
  primary.textContent = state.primary ? "Default: primary → PostgreSQL / ConnectionStrings:Elsa" : "Default: no proposed resource selected";
  resource.append(primary);
  const diagnostic = document.createElement("div");
  diagnostic.textContent = bindings.length ? `Explicit diagnostics bindings (${bindings.length}): PostgreSQL / ConnectionStrings:Diagnostics` : "Diagnostics: inherits the default unless explicitly bound";
  resource.append(diagnostic);
  const summary = view.querySelector("[data-summary]");
  summary.innerHTML = `<span class="badge">${state.source}</span><span class="badge">${state.profile ? "Worker starter" : "Custom"}</span>${state.group ? '<span class="badge good">Diagnostics group</span>' : ''}${state.source === "sample shells.json" ? '<span class="badge warn">1 unknown unchecked</span>' : ''}<ul class="summary-list"><li>${selected.size} exact features in the proposed set</li><li>${state.primary ? "Primary PostgreSQL reference set" : "No central resource chosen"}</li><li>${bindings.length} explicit diagnostics bindings</li><li>Cache capacity: ${state.capacity}${state.source === "sample shells.json" ? " (source: 128)" : ""}</li><li>Host and database evidence: unchecked</li></ul>`;
  featureRows(view, state, selected);
  const feedback = view.querySelector("[data-feedback]");
  feedback.classList.toggle("blocked", Boolean(state.refusal));
  if (state.refusal) {
    feedback.replaceChildren();
    const p = document.createElement("p");
    p.textContent = `${state.refusal.id} is required by ${state.refusal.affected.slice(1).join(", ")}. The edit was refused; your draft is unchanged. Export waits for a recovery choice.`;
    feedback.append(p);
    for (const [action, label] of [["restore", "Keep required feature"], ["remove-dependent", `Remove ${state.refusal.affected.length} affected features`]]) {
      const button = document.createElement("button"); button.type = "button"; button.dataset.act = action; button.textContent = label;
      feedback.append(button);
    }
  } else if (!feedback.textContent) feedback.textContent = "Make a choice to see its impact. No host is contacted.";
}

function exportMock(view, state) {
  const selected = [...resolve(state)].sort();
  const effectiveBindings = Object.fromEntries((state.isolated && state.group ? diagnostics.filter(id => id.endsWith("EntityFrameworkCore")) : [])
    .filter(id => selected.includes(id)).map(id => [id, { resource: "diagnostics", origin: "explicit feature binding" }]));
  const candidate = {
    notice: "RESEARCH MOCK ONLY; not a deployable Elsa configuration or live-host result",
    source: { kind: state.source, evidence: "synthetic fixture; no file or host was read" },
    intent: { startingProfile: state.profile ? "provisional-worker@1" : null, groups: state.group ? ["provisional-diagnostics-ef@1"] : [], explicitFeatureEdits: state.explicit },
    effective: { featureIds: selected, resources: state.primary ? { primary: { provider: "PostgreSql", connectionReference: "ConnectionStrings:Elsa" }, ...(state.isolated ? { diagnostics: { provider: "PostgreSql", connectionReference: "ConnectionStrings:Diagnostics" } } : {}) } : {}, featureBindings: effectiveBindings, reviewedSettingCandidate: { featureId: "WorkflowsRuntimeEntityFrameworkCore", path: "WorkflowExecutableCacheCapacity", value: state.capacity } },
    unknownSourceFixtureFeature: state.source === "sample shells.json" ? { id: unknown, settingPath: "FuturePolicy", valuePresentInMockExport: false, expectedRealBridgeBehavior: "retain unchanged source content" } : null,
    unchecked: ["running host", "package availability", "process overrides", "connection values", "provider connectivity", "database topology"]
  };
  state.lastExport = JSON.parse(JSON.stringify(state));
  state.lastExport.lastExport = null;
  const blob = new Blob([JSON.stringify(candidate, null, 2)], { type: "application/json" });
  const link = document.createElement("a"); link.href = URL.createObjectURL(blob); link.download = "runtime-builder-research-candidate.json";
  document.body.append(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(link.href), 1000);
  const feedback = view.querySelector("[data-feedback]");
  feedback.textContent = "Downloaded a research JSON illustration. No CShells file was generated, no connection value was exported, and no host was changed. Reopen restores this mock draft.";
  render(view);
}

function act(view, action) {
  const state = states[view.dataset.view];
  const feedback = view.querySelector("[data-feedback]");
  const refusedFeature = state.refusal?.id;
  if (action === "export" && state.refusal) {
    feedback.textContent = "Resolve the refused feature edit before exporting.";
    render(view);
    return;
  }
  state.refusal = action === "restore" || action === "remove-dependent" ? state.refusal : null;
  if (action === "reset") { const keep = state.lastExport; states[view.dataset.view] = fresh(); states[view.dataset.view].lastExport = keep; feedback.textContent = "New empty draft. This is a mock reset."; }
  else if (action === "import") { Object.assign(state, { source: "sample shells.json", profile: false, group: false, primary: false, isolated: false, capacity: 128, explicit: {} }); feedback.textContent = "Loaded a synthetic sample snapshot with an unknown AcmeAuditSink marker. Its value is not exported by this mock; no real file or host was read."; }
  else if (action === "profile") { state.profile = !state.profile; feedback.textContent = state.profile ? "Provisional Worker starter expanded; inspect its exact IDs and required Tasks." : "Worker starter removed. Explicit edits and imported selections remain."; }
  else if (action === "diagnostics") { state.group = !state.group; if (!state.group) state.isolated = false; feedback.textContent = state.group ? "Four diagnostics IDs added from a provisional flat group." : "Diagnostics group removed; no group-level resource rule remains."; }
  else if (action === "primary") { state.primary = !state.primary; if (!state.primary) state.isolated = false; feedback.textContent = state.primary ? "Named PostgreSQL primary reference selected; its value is not present here." : "Primary resource removed from mock draft."; }
  else if (action === "isolate") {
    if (!state.primary || !state.group) feedback.textContent = "Choose the primary resource and diagnostics group first; no binding was changed.";
    else { state.isolated = !state.isolated; feedback.textContent = state.isolated ? "Two diagnostics EF consumers now have explicit bindings. This is not a group inheritance rule." : "Explicit diagnostics bindings removed; both inherit the default."; }
  }
  else if (action === "restore") { state.refusal = null; feedback.textContent = "Kept the required feature. The refused edit was not applied."; }
  else if (action === "remove-dependent" && state.refusal) { state.refusal.affected.forEach(id => { state.explicit[id] = false; }); state.refusal = null; feedback.textContent = "Created explicit removals for the affected features. Review the changed set before export."; }
  else if (action === "export") { state.refusal = null; exportMock(view, state); return; }
  else if (action === "reopen") {
    if (!state.lastExport) feedback.textContent = "Nothing exported in this concept yet.";
    else { const saved = state.lastExport; states[view.dataset.view] = { ...saved, lastExport: saved, refusal: null }; feedback.textContent = "Reopened the last mock draft, including its synthetic source marker. The unknown value was not round-tripped; no host was queried or changed."; }
  }
  render(view);
  if ((action === "restore" || action === "remove-dependent") && refusedFeature)
    view.querySelector(`[data-feature="${refusedFeature}"]`)?.focus({ preventScroll: true });
}

document.querySelectorAll("[data-show]").forEach(button => button.addEventListener("click", () => {
  const id = button.dataset.show;
  document.querySelectorAll(".concept").forEach(view => { view.hidden = view.id !== id; });
  document.querySelectorAll("[data-show]").forEach(tab => tab.setAttribute("aria-current", tab === button ? "page" : "false"));
  document.getElementById(id).querySelector("h2").focus({ preventScroll: true });
}));
document.querySelectorAll(".concept h2").forEach(heading => heading.tabIndex = -1);
document.querySelectorAll("[data-view]").forEach(view => {
  view.addEventListener("click", event => {
    const button = event.target.closest("button");
    if (!button || !view.contains(button)) return;
    if (button.dataset.act) { act(view, button.dataset.act); return; }
    if (button.dataset.feature) {
      const state = states[view.dataset.view];
      const id = button.dataset.feature;
      const selected = resolve(state);
      const feedback = view.querySelector("[data-feedback]");
      state.refusal = null;
      if (selected.has(id)) {
        const affected = dependents(selected, id);
        if (affected.length > 1) state.refusal = { id, affected };
        else { state.explicit[id] = false; feedback.textContent = `${id} explicitly disabled in the mock draft.`; }
      } else { state.explicit[id] = true; feedback.textContent = `${id} explicitly enabled in the mock draft.`; }
      render(view);
      view.querySelector(`[data-feature="${id}"]`)?.focus({ preventScroll: true });
    }
  });
  view.querySelector("[data-setting]").addEventListener("change", event => {
    const state = states[view.dataset.view]; state.capacity = Number(event.target.value);
    view.querySelector("[data-feedback]").textContent = "Reviewed setting candidate changed; source value remains visible in the draft.";
    render(view);
  });
  const search = view.querySelector("[data-search]");
  if (search) search.addEventListener("input", event => { states[view.dataset.view].search = event.target.value; render(view); });
  render(view);
});
