import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { useDraftHistory } from "../workflow-editor/useDraftHistory";
import { useWorkflowNativeBlockCommit } from "../workflow-editor/useWorkflowNativeBlockCommit";
import type { GraphAuthoringView } from "../graph-authoring/GraphAuthoringView";
import type { WorkflowDraft } from "../workflowTypes";
import { sequenceNode, leafNode } from "./fixtures";

let root: Root, container: HTMLDivElement;
let doc: WorkflowDocumentApi, history: ReturnType<typeof useDraftHistory>, commit: ReturnType<typeof useWorkflowNativeBlockCommit>;
let deferred: (() => void) | null, allowed: boolean;
let restored: GraphAuthoringView | undefined;
const base = (): WorkflowDraft => ({ id: "draft", definitionId: "definition", state: { rootActivity: sequenceNode("root", [leafNode("one"), leafNode("two")]), variables: [], inputs: [], outputs: [] }, layout: [], activityPresentation: [], validationErrors: [] });
function Probe() {
  doc = useWorkflowDocument();
  history = useDraftHistory({ draft: doc.state.draft,
    captureView: () => ({ frames: doc.state.frames, primaryNodeId: doc.state.selectedNodeId, selectedNodeIds: doc.state.selectedNodeId === "two" ? ["one", "two"] : doc.state.selectedNodeId ? [doc.state.selectedNodeId] : [] }),
    restoreDraft(snapshot, view) { restored = view; doc.editDraft(() => snapshot); doc.navigateToScope(view?.frames ?? [], view?.primaryNodeId ?? null); } });
  commit = useWorkflowNativeBlockCommit({ ...doc, compareAndEditDraft(...args) {
    const dispatch = doc.compareAndEditDraft;
    deferred = () => dispatch(...args);
  } }, history);
  return null;
}
beforeEach(() => {
  vi.useFakeTimers(); allowed = true; deferred = null; restored = undefined;
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<Probe />));
  const initial = base();
  flushSync(() => { doc.loadDraft(initial); doc.select("two"); history.resetHistory(initial); });
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.useRealTimers(); });
function proposed() { const value = structuredClone(doc.state.draft!); value.activityPresentation = [{ nodeId: "one", displayName: "Named step" }]; return value; }
async function settle() { await Promise.resolve(); flushSync(() => {}); await Promise.resolve(); }

it("waits for the deferred reducer receipt, records exactly one ordinary edit and restores its original selection", async () => {
  const original = doc.state.draft!, next = proposed();
  let outcome: boolean | undefined;
  const result = commit(original, [], "two", next, ["one"], () => allowed).then(value => { outcome = value; return value; });
  await settle();
  expect(outcome).toBeUndefined(); expect(doc.state.draft).toBe(original); expect(history.canUndoNow).toBe(false);
  flushSync(() => deferred!()); await settle();
  expect(await result).toBe(true); expect(doc.state.draft).toEqual(next); expect(history.lineageHold.held).toBe(false);
  flushSync(() => history.undo());
  expect(doc.state.draft).toEqual(original); expect(restored?.selectedNodeIds).toEqual(["one", "two"]); expect(history.canUndoNow).toBe(false);
  flushSync(() => history.redo()); expect(doc.state.draft).toEqual(next);
});

it.each(["source", "permission", "selection", "lifetime"])("refuses a late %s change without a proposal edit or history entry", async change => {
  const original = doc.state.draft!, next = proposed();
  const result = commit(original, [], "two", next, ["one"], () => allowed);
  if (change === "source") flushSync(() => doc.editDraft(({ draft }) => ({ ...draft!, activityPresentation: [{ nodeId: "two", displayName: "Other edit" }] })));
  if (change === "permission" || change === "lifetime") allowed = false;
  if (change === "selection") flushSync(() => doc.select("one"));
  const before = doc.state.draft;
  flushSync(() => deferred!()); await settle();
  expect(await result).toBe(false); expect(doc.state.draft).toBe(before);
  expect(doc.state.draft?.activityPresentation).not.toContainEqual({ nodeId: "one", displayName: "Named step" });
  if (change !== "source") expect(history.canUndoNow).toBe(false);
});

it("captures a pending ordinary edit before the transaction and does not merge the two history entries", async () => {
  flushSync(() => doc.editDraft(({ draft }) => ({ ...draft!, activityPresentation: [{ nodeId: "two", displayName: "Earlier edit" }] })));
  const original = doc.state.draft!, next = proposed();
  const result = commit(original, [], "two", next, ["one"], () => allowed);
  flushSync(() => deferred!()); await settle(); expect(await result).toBe(true);
  flushSync(() => history.undo()); expect(doc.state.draft).toEqual(original);
  flushSync(() => history.undo()); expect(doc.state.draft).toEqual(base());
});

it("keeps the published artifact association while editing the ordinary draft", async () => {
  flushSync(() => doc.setPublishedArtifact("existing-artifact"));
  const original = doc.state.draft!, next = proposed();
  const result = commit(original, [], "two", next, ["one"], () => allowed);
  flushSync(() => deferred!()); await settle(); expect(await result).toBe(true);
  expect(doc.state.publishedArtifactId).toBe("existing-artifact");
});

it("keeps an edit committed after the transaction as a separate following Undo position", async () => {
  const original = doc.state.draft!, next = proposed();
  const result = commit(original, [], "two", next, ["one"], () => allowed);
  flushSync(() => { deferred!(); doc.editDraft(({ draft }) => ({ ...draft!, activityPresentation: [{ nodeId: "one", displayName: "Later edit" }] })); });
  await settle(); expect(await result).toBe(true);
  expect(doc.state.draft?.activityPresentation?.[0].displayName).toBe("Later edit");
  flushSync(() => history.undo()); expect(doc.state.draft).toEqual(next);
  flushSync(() => history.undo()); expect(doc.state.draft).toEqual(original);
});
