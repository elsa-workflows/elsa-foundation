import React, { useEffect, useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { requestStudioNavigation, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { useWorkflowPersistence } from "../workflow-editor/useWorkflowPersistence";
import { useDefinitionMetadata } from "../workflow-editor/useDefinitionMetadata";
import { useWorkflowDocument } from "../workflow-editor/workflowDocument";

const api = vi.hoisted(() => ({ save: vi.fn(), metadata: vi.fn() }));
vi.mock("../api/workflowDesign", () => ({ updateDraft: api.save, updateDefinitionMetadata: api.metadata }));
const context = { baseUrl: "https://backend.example/full-shell/" } as StudioEndpointContext;
const initial: WorkflowDraft = {
  id: "draft", definitionId: "definition", layout: [], validationErrors: [],
  state: { rootActivity: { nodeId: "switch", activityVersionId: "switch-v1", inputs: [], outputs: [], structure: {
    kind: "Switch", schemaVersion: "1", payload: { cases: [{ caseId: "first", match: "First", condition: { referenceKey: "case:first", value: { expressionType: "Literal", value: true } }, activity: null }] }
  } } }
};
const details: WorkflowDefinitionDetails = { definition: { id: "definition", name: "Original", description: "Keep me", createdAt: "", lastModifiedAt: "", versionCount: 0 }, versions: [] };
let root: Root, container: HTMLDivElement;
let document: ReturnType<typeof useWorkflowDocument>;
let persistence: ReturnType<typeof useWorkflowPersistence>;
let metadata: ReturnType<typeof useDefinitionMetadata>;
let autosave = false;
const noop = () => {};

function Probe() {
  document = useWorkflowDocument();
  persistence = useWorkflowPersistence({ context, draft: document.state.draft, autosaveEnabledByDefault: autosave, editDraft: document.editDraft, setStatus: noop, setError: noop });
  const { markSaved } = persistence, { loadDraft } = document;
  useEffect(() => { markSaved(initial); loadDraft(initial); }, [markSaved, loadDraft]);
  const [currentDetails, setDetails] = useState<WorkflowDefinitionDetails | null>(details);
  metadata = useDefinitionMetadata({ context, details: currentDetails, setDetails, setStatus: noop });
  return null;
}
function navigationAllowed() { return requestStudioNavigation("/workflows/editor?backend=first", "/settings/backends"); }
function unloadAllowed() { return window.dispatchEvent(new Event("beforeunload", { cancelable: true })); }
async function update(run: () => unknown) {
  let result: unknown;
  flushSync(() => { result = run(); });
  await result;
  await vi.advanceTimersByTimeAsync(0);
  flushSync(() => root.render(<Probe />));
}
async function mount() { await update(() => root.render(<Probe />)); }
async function editCondition() {
  await update(async () => document.editDraft(({ draft }) => ({ ...draft!, state: { ...draft!.state, rootActivity: {
    ...draft!.state.rootActivity!, structure: { kind: "Switch", schemaVersion: "1", payload: { cases: [{ caseId: "first", match: "First", condition: { referenceKey: "case:first", value: { expressionType: "JavaScript", value: "1 + 1 === 2" } }, activity: null }] } }
  } } })));
}

beforeEach(() => {
  vi.useFakeTimers(); vi.resetAllMocks(); autosave = false;
  api.save.mockImplementation(async (_context, draft) => draft);
  api.metadata.mockImplementation(async (_context, _id, patch) => ({ ...details, definition: { ...details.definition, ...patch } }));
  container = window.document.createElement("div"); window.document.body.append(container); root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.useRealTimers(); });

describe("workflow navigation guards", () => {
  it("allows a saved workflow, blocks an unsaved Switch condition with autosave disabled, and releases after undo", async () => {
    await mount(); expect(navigationAllowed()).toBe(true);
    await editCondition(); expect(navigationAllowed()).toBe(false); expect(unloadAllowed()).toBe(false); expect(api.save).not.toHaveBeenCalled();
    await update(async () => document.editDraft(() => initial));
    expect(navigationAllowed()).toBe(true); expect(unloadAllowed()).toBe(true);
  });

  it("blocks during debounce and a failed autosave, then releases only after a successful save", async () => {
    autosave = true; await mount(); await editCondition();
    expect(navigationAllowed()).toBe(false);
    api.save.mockRejectedValueOnce(new Error("offline"));
    await update(async () => vi.advanceTimersByTimeAsync(1200));
    expect(api.save).toHaveBeenCalledTimes(1); expect(navigationAllowed()).toBe(false);
    await update(async () => { await persistence.flushPendingSave(); });
    expect(navigationAllowed()).toBe(true);
  });

  it("blocks queued writes even when the current signature already matches the saved draft", async () => {
    await mount(); let finish!: (draft: WorkflowDraft) => void;
    api.save.mockImplementation(() => new Promise<WorkflowDraft>(resolve => { finish = resolve; }));
    let saved!: Promise<WorkflowDraft>;
    await update(() => { saved = persistence.saveDraft(initial, "Saved"); expect(navigationAllowed()).toBe(false); });
    await update(async () => { await Promise.resolve(); });
    expect(navigationAllowed()).toBe(false);
    await update(async () => { finish(initial); await saved; });
    expect(navigationAllowed()).toBe(true);
  });

  it("keeps a newer edit guarded when an older save completes and removes listeners on unmount", async () => {
    await mount(); await editCondition();
    const old = document.state.draft!; let finish!: (draft: WorkflowDraft) => void;
    api.save.mockImplementation(() => new Promise<WorkflowDraft>(resolve => { finish = resolve; }));
    let saved!: Promise<WorkflowDraft>;
    await update(async () => { saved = persistence.saveDraft(old, "Saved"); });
    await update(async () => document.editDraft(({ draft }) => ({ ...draft!, layout: [{ nodeId: "switch", x: 40, y: 0 }] })));
    await update(async () => { finish(old); await saved; });
    expect(navigationAllowed()).toBe(false);
    flushSync(() => root.unmount()); expect(navigationAllowed()).toBe(true); expect(unloadAllowed()).toBe(true);
    root = createRoot(container);
  });

  it("guards debounced and failed metadata until retry succeeds without dropping the other field", async () => {
    await mount(); await update(async () => metadata.updateDefinitionMeta({ name: "Renamed" }));
    expect(navigationAllowed()).toBe(false); expect(unloadAllowed()).toBe(false);
    api.metadata.mockRejectedValueOnce(new Error("offline"));
    await update(async () => vi.advanceTimersByTimeAsync(800));
    expect(navigationAllowed()).toBe(false);
    await update(async () => { expect(await metadata.flushDefinitionMeta()).toBe(true); });
    expect(api.metadata).toHaveBeenLastCalledWith(context, "definition", { name: "Renamed", description: "Keep me" });
    expect(navigationAllowed()).toBe(true);
  });

  it("keeps a metadata edit made during a save guarded until that edit is also persisted", async () => {
    await mount(); let finish!: (saved: WorkflowDefinitionDetails) => void;
    api.metadata.mockImplementationOnce(() => new Promise<WorkflowDefinitionDetails>(resolve => { finish = resolve; }));
    await update(async () => metadata.updateDefinitionMeta({ name: "Renamed" }));
    let saved!: Promise<boolean>;
    await update(async () => { saved = metadata.flushDefinitionMeta(); });
    await update(async () => metadata.updateDefinitionMeta({ description: "New description" }));
    await update(async () => { finish({ ...details, definition: { ...details.definition, name: "Renamed" } }); await saved; });
    expect(navigationAllowed()).toBe(false);
    await update(async () => { await metadata.flushDefinitionMeta(); });
    expect(navigationAllowed()).toBe(true);
  });
});
