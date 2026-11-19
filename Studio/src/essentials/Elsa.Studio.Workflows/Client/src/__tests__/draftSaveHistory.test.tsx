import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft } from "../workflowTypes";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { updateDraft } from "../api/workflowDesign";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { useDraftHistory } from "../workflow-editor/useDraftHistory";
import { useWorkflowPersistence } from "../workflow-editor/useWorkflowPersistence";
import { historyCaptureDelayMs, autosaveDelayMs } from "../workflow-editor/constants";

vi.mock("../api/workflowDesign", async importOriginal => ({ ...await importOriginal<typeof import("../api/workflowDesign")>(), updateDraft: vi.fn() }));
const context = { baseUrl: "https://owned-persistence.example/", http: {} } as unknown as StudioEndpointContext;
const noop = () => {};
let doc: WorkflowDocumentApi, history: ReturnType<typeof useDraftHistory>, persistence: ReturnType<typeof useWorkflowPersistence>;
let root: Root, container: HTMLDivElement;
function Probe() {
  doc = useWorkflowDocument();
  history = useDraftHistory({ draft: doc.state.draft, restoreDraft: snapshot => doc.editDraft(() => snapshot) });
  persistence = useWorkflowPersistence({ context, draft: doc.state.draft, editDraft: doc.editDraft, onSaveReconciled: history.acceptSavedDraft, setStatus: noop, setError: noop });
  return null;
}
function baseline(): WorkflowDraft {
  return { id: "draft", definitionId: "definition", sourceVersionId: null, validationErrors: [], layout: [], activityPresentation: [],
    state: { variables: [], inputs: [], outputs: [], rootActivity: { nodeId: "root", activityVersionId: "sequence", inputs: [], outputs: [],
      structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [{ nodeId: "child", activityVersionId: "writer", inputs: [], outputs: [], text: { expression: { type: "Literal", value: "shipment" } } }] } } } } };
}
// The actual owned-host response adds these optional null layout fields; input wrappers also take the normal API round trip.
function serverCopy(draft: WorkflowDraft): WorkflowDraft {
  return { ...structuredClone(draft), state: expandStateFromWire(canonicalizeStateForWire(draft.state)),
    layout: draft.layout.map(record => ({ ...record, width: record.width ?? null, height: record.height ?? null, additionalProperties: record.additionalProperties ?? null })) };
}
function extract() {
  flushSync(() => { history.flushPendingHistory(); doc.editDraft(({ draft }) => {
    const old = draft!.state.rootActivity!;
    return { ...draft!, state: { ...draft!.state, rootActivity: { ...old, structure: { ...old.structure!, payload: { activities: [
      { nodeId: "block", activityVersionId: "sequence", inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: old.structure!.payload.activities } } }
    ] } } } }, layout: [{ nodeId: "block", x: 0, y: 0 }], activityPresentation: [{ nodeId: "block", displayName: "Prepare shipment" }] };
  }); });
}
function firstStep() { return (doc.state.draft!.state.rootActivity!.structure!.payload.activities as Array<{ nodeId: string }>)[0].nodeId; }
async function settle() { await vi.advanceTimersByTimeAsync(autosaveDelayMs + historyCaptureDelayMs + 20); flushSync(() => {}); }
beforeEach(() => {
  vi.useFakeTimers(); vi.mocked(updateDraft).mockReset().mockImplementation(async (_context, draft) => serverCopy(draft));
  container = document.createElement("div"); document.body.append(container); root = createRoot(container); flushSync(() => root.render(<Probe />));
  const initial = serverCopy(baseline()); flushSync(() => { history.resetHistory(initial); doc.loadDraft(initial); persistence.markSaved(initial); });
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.clearAllMocks(); vi.useRealTimers(); });

it.each([false, true])("save normalization is not another Undo entry (capture already fired: %s)", async captured => {
  extract();
  if (captured) await vi.advanceTimersByTimeAsync(historyCaptureDelayMs + 1);
  await persistence.saveDraft(doc.state.draft!, "Saved"); await settle();
  expect(firstStep()).toBe("block");
  expect(doc.state.draft!.activityPresentation).toEqual([{ nodeId: "block", displayName: "Prepare shipment" }]);
  flushSync(() => history.undo());
  expect(firstStep()).toBe("child");
  await settle(); // Undo's own autosave must not clear its Redo.
  expect(history.canRedoNow).toBe(true);
  flushSync(() => history.redo());
  expect(firstStep()).toBe("block");
  await settle();
  expect(doc.state.draft!.layout).toEqual([{ nodeId: "block", x: 0, y: 0, width: null, height: null, additionalProperties: null }]);
  flushSync(() => history.undo()); expect(firstStep()).toBe("child");
  expect(history.canUndoNow).toBe(false);
});

it("a stale save response cannot replace a newer edit or rebase its history", async () => {
  extract(); await vi.advanceTimersByTimeAsync(historyCaptureDelayMs + 1);
  const extracted = structuredClone(doc.state.draft!);
  let acknowledge!: (saved: WorkflowDraft) => void;
  vi.mocked(updateDraft).mockImplementationOnce(() => new Promise(resolve => { acknowledge = resolve; }));
  const saving = persistence.saveDraft(extracted, "Saved"); await Promise.resolve(); await Promise.resolve();
  flushSync(() => doc.editDraft(({ draft }) => ({ ...draft!, activityPresentation: [{ nodeId: "block", displayName: "Later name" }], layout: [{ nodeId: "block", x: 50, y: 60 }] })));
  await vi.advanceTimersByTimeAsync(historyCaptureDelayMs + 1);
  acknowledge(serverCopy(extracted)); await saving; flushSync(() => {});
  expect(doc.state.draft!.activityPresentation![0].displayName).toBe("Later name");
  expect(doc.state.draft!.layout[0]).toMatchObject({ x: 50, y: 60 });
  flushSync(() => history.undo());
  expect(doc.state.draft!.activityPresentation![0].displayName).toBe("Prepare shipment");
  flushSync(() => history.undo()); expect(firstStep()).toBe("child");
});

it("a validation-only save response does not add an author edit", async () => {
  vi.mocked(updateDraft).mockImplementationOnce(async (_context, draft) => ({ ...serverCopy(draft), validationErrors: [{ type: "test-warning", message: "Review this draft", severity: "Warning" }] }));
  await persistence.saveDraft(doc.state.draft!, "Saved"); await settle();
  expect(doc.state.draft!.validationErrors[0].message).toBe("Review this draft");
  expect(history.canUndoNow).toBe(false);
});

it("two queued acknowledgements of one snapshot do not create another edit", async () => {
  extract(); await vi.advanceTimersByTimeAsync(historyCaptureDelayMs + 1);
  const snapshot = doc.state.draft!;
  await Promise.all([persistence.saveDraft(snapshot, "Saved"), persistence.saveDraft(snapshot, "Saved")]);
  await settle();
  flushSync(() => history.undo()); expect(firstStep()).toBe("child");
  const undone = doc.state.draft!;
  await Promise.all([persistence.saveDraft(undone, "Autosaved"), persistence.saveDraft(undone, "Saved")]);
  await settle(); expect(history.canRedoNow).toBe(true);
  flushSync(() => history.redo()); expect(firstStep()).toBe("block");
});
