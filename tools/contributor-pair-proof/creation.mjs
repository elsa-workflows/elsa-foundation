import assert from "node:assert/strict";

export const creationLimits = Object.freeze({ bytes: 1024 * 1024, catalogItems: 2000, responseReads: 6, responseMs: 1000 });
const record = value => value !== null && typeof value === "object" && !Array.isArray(value);
const identity = value => typeof value === "string" && value.length > 0 && value.length <= 256 ? value : null;
const boolean = value => typeof value === "boolean" ? value : null;
const network = value => ["unobserved", "requested", "received", "http-error", "unavailable"].includes(value) ? value : "unavailable";
const choicePhase = value => ["card-visible", "card-click", "selected"].includes(value) ? value : "unobserved";
const choiceCount = value => Number.isInteger(value) && value >= 0 ? Math.min(2, value) : null;

// These are observations of the explicitly selected fixture, never default-Create acceptance.
export function projectCreationState(value) {
  return {
    fixture: "explicit-flowchart", defaultCreate: "unverified",
    catalog: network(value?.catalog), candidatePresent: boolean(value?.candidatePresent), templatePresent: boolean(value?.templatePresent),
    catalogAtCreate: network(value?.catalogAtCreate), candidateAtCreate: boolean(value?.candidateAtCreate), templateAtCreate: boolean(value?.templateAtCreate),
    choiceEnabled: boolean(value?.choiceEnabled), choiceSelected: boolean(value?.choiceSelected),
    choicePhase: choicePhase(value?.choicePhase), choiceCount: choiceCount(value?.choiceCount), enabledCardCount: choiceCount(value?.enabledCardCount), choiceValuePresent: boolean(value?.choiceValuePresent),
    createRequests: Number.isInteger(value?.createRequests) ? Math.max(0, Math.min(2, value.createRequests)) : 0,
    requestRoot: boolean(value?.requestRoot), requestRootMatchesChoice: boolean(value?.requestRootMatchesChoice),
    createResponse: network(value?.createResponse), createRoot: boolean(value?.createRoot), createMatchesEditor: boolean(value?.createMatchesEditor),
    editorRead: network(value?.editorRead), editorRoot: boolean(value?.editorRoot), readMatchesEditor: boolean(value?.readMatchesEditor)
  };
}

export async function selectFlowchartRoot(dialog, onState) {
  const choice = dialog.getByRole("radio", { name: "Flowchart", exact: true });
  // Pinned Studio clips the native input. Its visible wrapping label is the user
  // target; require that exact enabled label/input association, never force a click.
  const card = dialog.locator('label.wf-root-card:not([aria-disabled="true"]):has(> input[type="radio"][name="wf-root-kind"][aria-label="Flowchart"])');
  const emit = value => { try { onState?.(value); } catch {} };
  async function observe(phase) {
    emit({ phase });
    if (typeof onState !== "function") return;
    try {
      const state = await choice.evaluateAll(nodes => ({
        count: Math.min(2, nodes.length), enabled: nodes.length === 1 ? !nodes[0].disabled : null,
        checked: nodes.length === 1 ? nodes[0].checked : null,
        version: nodes.length === 1 && nodes[0].value.length <= 256 ? nodes[0].value : null
      }));
      emit({ phase, ...state, enabledCardCount: await card.count() });
    } catch { /* Optional fixed-state observations cannot alter the journey. */ }
  }
  await observe("card-visible");
  await card.waitFor({ state: "visible" });
  assert.equal(await card.count(), 1, "the Create dialog must expose one enabled Flowchart label card");
  assert.equal(await choice.count(), 1, "the Create dialog must expose one Flowchart root choice");
  await observe("card-click");
  await card.click();
  assert.equal(await choice.isEnabled(), true, "the Flowchart root choice must be enabled");
  assert.equal(await choice.isChecked(), true, "the Flowchart root choice must be selected");
  const version = identity(await choice.inputValue());
  assert.ok(version, "the selected Flowchart root choice must identify an activity version");
  emit({ phase: "selected", count: 1, enabledCardCount: 1, enabled: true, checked: true, version });
  return version;
}

function parseJson(text) {
  if (typeof text !== "string" || Buffer.byteLength(text) > creationLimits.bytes) return null;
  try { return JSON.parse(text); } catch { return null; }
}

function rootOfState(state) {
  if (state === null) return false;
  if (!record(state)) return null;
  return state.rootActivity === null || state.rootActivity === undefined ? false : record(state.rootActivity) ? state.rootActivity : null;
}

function details(payload) {
  if (!record(payload) || !record(payload.definition) || !identity(payload.definition.id)) return null;
  const root = payload.draft === null ? false : record(payload.draft) ? rootOfState(payload.draft.state) : null;
  // Keep only identity for correlation and presence, never the authored root body.
  return { id: payload.definition.id, root: root === null ? null : root !== false };
}

export function observeWorkflowCreation(page, { backendOrigin }) {
  let disposed = false;
  let selected = null;
  let editor = null;
  let catalog = null;
  let created = null;
  let createRequest = null;
  let createResponseSeen = false;
  let reads = 0;
  const editorReads = new Map();
  const editorReadsSeen = new Set();
  const cancellations = new Set();
  const state = { catalog: "unobserved", createResponse: "unobserved", createRequests: 0 };
  const path = request => {
    const url = new URL(request.url());
    return url.origin === backendOrigin ? url.pathname : null;
  };
  const rootPresent = root => root === null ? null : root !== false;

  // Only known creation/catalog JSON is inspected in memory. No payload, identity,
  // URL, header or arbitrary message is retained. Parsing/output are size-bounded;
  // a late Playwright body read is discarded, not claimed forcibly canceled.
  function readResponse(response) {
    return new Promise(resolve => {
      let done = false;
      let timer;
      const finish = value => {
        if (done) return;
        done = true;
        clearTimeout(timer);
        cancellations.delete(cancel);
        resolve(value);
      };
      const cancel = () => finish(null);
      cancellations.add(cancel);
      timer = setTimeout(cancel, creationLimits.responseMs);
      Promise.resolve().then(() => response.text()).then(text => {
        if (!done) finish(parseJson(text));
      }, cancel);
    });
  }

  const onRequest = request => {
    if (disposed) return;
    try {
      const route = path(request);
      if (route === "/design/activities/catalog" && request.method() === "GET") state.catalog = "requested";
      if (route !== "/design/workflows/definitions" || request.method() !== "POST") return;
      state.createRequests = Math.min(2, state.createRequests + 1);
      if (state.createRequests !== 1) return;
      createRequest = request;
      const candidates = selected && catalog ? catalog.filter(item => item.id === selected) : null;
      state.catalogAtCreate = state.catalog;
      state.candidateAtCreate = candidates ? candidates.length > 0 : null;
      state.templateAtCreate = candidates?.length === 1 ? candidates[0].template : null;
      const payload = parseJson(request.postData());
      const root = record(payload) ? payload.initialState === undefined || payload.initialState === null ? false : rootOfState(payload.initialState) : null;
      state.requestRoot = rootPresent(root);
      state.requestRootMatchesChoice = record(root) && selected ? root.activityVersionId === selected : root === false ? false : null;
    } catch { /* Optional observation never changes the original journey failure. */ }
  };
  const onResponse = async response => {
    if (disposed || reads >= creationLimits.responseReads) return;
    let kind;
    let readId;
    const setResponseState = (status, detail = null) => {
      if (kind === "editorRead") editorReads.set(readId, { status, detail });
      else state[kind] = status;
    };
    try {
      const request = response.request();
      const route = path(request);
      if (route === "/design/activities/catalog" && request.method() === "GET") kind = "catalog";
      else if (route === "/design/workflows/definitions" && request.method() === "POST" && request === createRequest && !createResponseSeen) {
        createResponseSeen = true;
        kind = "createResponse";
      }
      else if (route?.startsWith("/design/workflows/definitions/") && request.method() === "GET") {
        readId = identity(decodeURIComponent(route.slice("/design/workflows/definitions/".length)));
        if (!readId || readId.includes("/") || editorReadsSeen.has(readId) || editorReadsSeen.size >= 3) return;
        editorReadsSeen.add(readId);
        kind = "editorRead";
      } else return;
      reads++;
      if (!response.ok()) { setResponseState("http-error"); return; }
      const payload = await readResponse(response);
      if (disposed) return;
      if (kind === "catalog") {
        if (!Array.isArray(payload?.activities) || payload.activities.length > creationLimits.catalogItems) {
          state.catalog = "unavailable"; return;
        }
        catalog = payload.activities.flatMap(item => identity(item?.activityVersionId)
          ? [{ id: item.activityVersionId, template: record(item.authoringTemplate) }] : []);
        state.catalog = "received";
      } else {
        const projected = details(payload);
        setResponseState(projected ? "received" : "unavailable", projected);
        if (kind === "createResponse") created = projected;
      }
    } catch {
      if (!disposed && kind) setResponseState("unavailable");
    }
  };
  try { page.on("request", onRequest); } catch { state.catalog = "unavailable"; }
  try { page.on("response", onResponse); } catch { state.createResponse = "unavailable"; }
  return {
    observeChoice(value) {
      selected = identity(value?.version);
      state.choicePhase = choicePhase(value?.phase);
      state.choiceCount = choiceCount(value?.count);
      state.enabledCardCount = choiceCount(value?.enabledCardCount);
      state.choiceEnabled = boolean(value?.enabled);
      state.choiceSelected = boolean(value?.checked);
      state.choiceValuePresent = value?.version === undefined ? null : selected !== null;
    },
    select(version) {
      selected = identity(version);
      assert.ok(selected, "the observed Flowchart choice must identify an activity version");
      state.choiceEnabled = true;
      state.choiceSelected = true;
      state.choicePhase = "selected";
      state.choiceValuePresent = true;
    },
    bindEditor(definitionId) { editor = identity(definitionId); },
    snapshot() {
      const candidates = selected && catalog ? catalog.filter(item => item.id === selected) : null;
      const read = editor ? editorReads.get(editor) : null;
      const detail = read?.detail;
      return projectCreationState({ ...state,
        candidatePresent: candidates ? candidates.length > 0 : null,
        templatePresent: candidates?.length === 1 ? candidates[0].template : null,
        createRoot: created ? created.root : null,
        createMatchesEditor: created && editor ? created.id === editor : null,
        editorRead: read?.status ?? "unobserved",
        editorRoot: detail && detail.id === editor ? detail.root : null,
        readMatchesEditor: detail && editor ? detail.id === editor : null
      });
    },
    dispose() {
      disposed = true;
      try { page.off("request", onRequest); } catch {}
      try { page.off("response", onResponse); } catch {}
      for (const cancel of cancellations) cancel();
    }
  };
}
