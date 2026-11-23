import React, { StrictMode } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { useWorkflowDocument, type WorkflowDocumentApi, type OrdinaryWorkflowEditHistory } from "../workflow-editor/workflowDocument";
import { useDraftHistory } from "../workflow-editor/useDraftHistory";
import type { WorkflowDraft } from "../workflowTypes";
import { historyCaptureDelayMs } from "../workflow-editor/constants";

let doc: WorkflowDocumentApi, history: ReturnType<typeof useDraftHistory>, root: Root, container: HTMLDivElement;
function Probe() {
  doc = useWorkflowDocument();
  history = useDraftHistory({ draft: doc.state.draft, restoreDraft: snapshot => doc.editDraft(() => snapshot),
    transitions: doc.state.historyTransitions, onTransitionsCaptured: doc.acknowledgeHistoryTransitions });
  return null;
}
const empty = { source: "a", parameters: {} };
const inserted = { source: "a+args.customer", parameters: { customer: { kind: "ActivityResult", producerNodeId: "fetch", projectionKey: "$result" } } };
function baseline(): WorkflowDraft {
  return { id: "draft", definitionId: "definition", sourceVersionId: null, validationErrors: [], layout: [],
    state: { variables: [], inputs: [], outputs: [], rootActivity: { nodeId: "root", activityVersionId: "writer", inputs: [], outputs: [], text: empty } } };
}
function edit(value: unknown, boundary?: OrdinaryWorkflowEditHistory) {
  doc.editDraft(({ draft }) => ({ ...draft!, state: { ...draft!.state, rootActivity: { ...draft!.state.rootActivity!, text: value } } }), boundary);
}
function value() { return doc.state.draft!.state.rootActivity!.text; }
const bookmarks = () => ({ before: { restore: vi.fn() }, after: { restore: vi.fn() } });
beforeEach(() => {
  vi.useFakeTimers(); container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<StrictMode><Probe /></StrictMode>));
  const initial = baseline(); flushSync(() => { history.resetHistory(initial); doc.loadDraft(initial); });
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.useRealTimers(); });
it("captures an atomic insertion exactly once among batched typing, with cursor restoration", () => {
  const before = { ...empty, source: "ab" }, after = { ...inserted, source: "ab+args.customer" }, bookmark = bookmarks();
  flushSync(() => { edit(before); edit(after, bookmark); edit({ ...after, source: after.source + "!" }); });
  expect(JSON.stringify(doc.state.draft)).not.toContain("restore");
  flushSync(() => history.undo()); expect(value()).toEqual(after);
  expect(doc.state.historyTransitions).toEqual([]);
  flushSync(() => history.undo()); expect(value()).toEqual(before); expect(bookmark.before.restore).toHaveBeenCalledOnce();
  flushSync(() => history.undo()); expect(value()).toEqual(empty); expect(history.canUndoNow).toBe(false);
  flushSync(() => history.redo()); expect(value()).toEqual(before);
  flushSync(() => history.redo()); expect(value()).toEqual(after); expect(bookmark.after.restore).toHaveBeenCalled();
  flushSync(() => history.redo()); expect(value()).toEqual({ ...after, source: after.source + "!" });
});
it("preserves reused parameters, same-source parameter edits and consecutive boundaries", () => {
  const first = bookmarks(), second = bookmarks();
  const sameSource = { ...inserted, parameters: { customer: { ...inserted.parameters.customer, producerNodeId: "other" } } };
  flushSync(() => { edit(inserted, first); edit(sameSource, second); });
  flushSync(() => history.undo()); expect(value()).toEqual(inserted);
  flushSync(() => history.undo()); expect(value()).toEqual(empty); expect(history.canUndoNow).toBe(false);
  flushSync(() => { history.redo(); }); expect(value()).toEqual(inserted);
  flushSync(() => { history.redo(); }); expect(value()).toEqual(sameSource);
});
it("save acknowledgement does not duplicate the insertion and later typing keeps its own history", async () => {
  flushSync(() => edit(inserted, bookmarks()));
  const current = doc.state.draft!, saved = { ...structuredClone(current), validationErrors: [] };
  flushSync(() => { history.acceptSavedDraft(current, saved); doc.editDraft(() => saved); });
  await vi.advanceTimersByTimeAsync(historyCaptureDelayMs + 1);
  flushSync(() => history.undo()); expect(value()).toEqual(empty); expect(history.canUndoNow).toBe(false);
  flushSync(() => history.redo()); expect(value()).toEqual(inserted);
  flushSync(() => edit({ ...inserted, source: inserted.source + "!" }));
  flushSync(() => history.undo()); expect(value()).toEqual(inserted);
  flushSync(() => history.undo()); expect(value()).toEqual(empty);
});
it("ordinary insertion inherits Weaver lineage, clears batch origin and adds no new hold", () => {
  const applied = { ...baseline(), state: { ...baseline().state, rootActivity: { ...baseline().state.rootActivity!, text: { ...empty, source: "batch" } } } };
  flushSync(() => { history.batchHistory.flushFromLatest(); doc.editDraft(() => applied); history.batchHistory.recordBatch("reviewed", applied); });
  expect(history.batchHistory.undoState("reviewed").status).toBe("available");
  const line = [...history.lineageHold.lineage()];
  flushSync(() => edit(inserted, bookmarks()));
  expect([...history.lineageHold.lineage()]).toEqual(line);
  expect(history.batchHistory.undoState("reviewed").status).toBe("unavailable");
  flushSync(() => history.undo()); expect(value()).toEqual({ ...empty, source: "batch" });
  expect(history.batchHistory.undoState("reviewed").status).toBe("available");
});

it("does not carry a batched insertion receipt into a different loaded draft", () => {
  const next = { ...baseline(), id: "new-draft", definitionId: "new-definition" };
  flushSync(() => { edit(inserted, bookmarks()); doc.loadDraft(next); });
  expect(doc.state.historyTransitions).toEqual([]);
  flushSync(() => history.resetHistory(next));
  flushSync(() => edit({ ...empty, source: "new-resource-edit" }));
  flushSync(() => history.undo()); expect(value()).toEqual(empty); expect(history.canUndoNow).toBe(false);
  expect(doc.state.draft!.definitionId).toBe("new-definition");
});
