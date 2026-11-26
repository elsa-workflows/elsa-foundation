import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { useActivityClipboard } from "../workflow-editor/useActivityClipboard";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { useDraftHistory } from "../workflow-editor/useDraftHistory";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";
import { leafNode, sequenceActivity, sequenceNode, writeLine } from "./fixtures";

const catalog = new Map([[sequenceActivity.activityVersionId, sequenceActivity], [writeLine.activityVersionId, { ...writeLine,
  inputs: [{ name: "Text", referenceKey: "text", typeName: "String", isWrapped: true }] }]]);
const binding = { expressionType: "ActivityResultMember", value: { producerNodeId: "producer", projectionKey: "$result", memberPath: ["a.b", ""] } };
function initial(definitionId = "definition-a"): WorkflowDraft {
  const consumer = leafNode("consumer"); consumer.inputs = [{ referenceKey: "text", value: binding }];
  return { id: `${definitionId}-draft`, definitionId, state: { rootActivity: sequenceNode("root", [leafNode("producer"), consumer]) },
    layout: [], activityPresentation: [], validationErrors: [] };
}
let doc: WorkflowDocumentApi, history: ReturnType<typeof useDraftHistory>, clipboard: ReturnType<typeof useActivityClipboard>;
let root: Root, container: HTMLDivElement, editCount: number, status: string;
let disable: (value: boolean) => void;
// The copy origin is page-level state, so every test gets a fresh hook module; a fresh import also stands in for a full page load.
let useClipboard: typeof useActivityClipboard;
async function freshClipboardModule() { vi.resetModules(); useClipboard = (await import("../workflow-editor/useActivityClipboard")).useActivityClipboard; }
function remount() { flushSync(() => root.unmount()); root = createRoot(container); flushSync(() => root.render(<Probe />)); }
function Probe() {
  const [disabled, setDisabled] = useState(false); disable = setDisabled;
  doc = useWorkflowDocument();
  history = useDraftHistory({ draft: doc.state.draft, restoreDraft: snapshot => doc.editDraft(() => snapshot) });
  clipboard = useClipboard({ resourceKind: "workflow", draft: doc.state.draft, frames: doc.state.frames, catalogByVersion: catalog,
    selectedNodeIds: ["producer", "consumer"], disabled, definitionId: doc.state.draft?.definitionId ?? "",
    editDraft: recipe => { editCount++; doc.editDraft(recipe); }, select: doc.select, setStatus: value => { status = value; } });
  return null;
}
function nodes() { return doc.state.draft!.state.rootActivity!.structure!.payload.activities as ActivityNode[]; }
function load(draft: WorkflowDraft) { flushSync(() => { history.resetHistory(draft); doc.loadDraft(draft); }); }
beforeEach(async () => {
  await freshClipboardModule();
  editCount = 0; status = ""; sessionStorage.clear(); container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  flushSync(() => root.render(<Probe />)); load(initial());
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });

describe("actual resource-local clipboard and history", () => {
  it.each(["paste", "duplicate"] as const)("commits %s reference identities in one edit, restores them with Undo/Redo and reopens losslessly", operation => {
    const before = structuredClone(doc.state.draft);
    expect(clipboard.copy()).toBe(true);
    expect(clipboard[operation]()).toBe(true);
    expect(editCount).toBe(1);
    const producerId = nodes()[2].nodeId;
    expect(nodes()[3].inputs).toEqual([{ referenceKey: "text", value: { ...binding, value: { ...binding.value, producerNodeId: producerId } } }]);
    flushSync(() => history.undo());
    expect(doc.state.draft).toEqual(before);
    expect(history.canUndoNow).toBe(false);
    flushSync(() => history.redo());
    expect(nodes()[2].nodeId).toBe(producerId);
    const applied = canonicalizeStateForWire(doc.state.draft!.state);
    load({ ...doc.state.draft!, state: expandStateFromWire(JSON.parse(JSON.stringify(applied))) });
    expect(canonicalizeStateForWire(doc.state.draft!.state)).toEqual(applied);
    expect((nodes()[3].text as { expression: { value: unknown } }).expression.value).toEqual({ ...binding.value, producerNodeId: producerId });
  });

  it("clears the clipboard on a resource change even when the destination has identical node IDs", () => {
    expect(clipboard.copy()).toBe(true);
    load(initial("definition-b"));
    const before = structuredClone(doc.state.draft);
    expect(clipboard.paste()).toBe(false);
    expect(doc.state.draft).toEqual(before);
    expect(editCount).toBe(0);
    expect(status).toBe("The copied activities came from another workflow, so they can't be pasted here yet.");
  });

  it("explains a refused paste after the editor reopens on another workflow", () => {
    expect(clipboard.copy()).toBe(true);
    // Studio keys the editor by definition ID, so opening another workflow mounts a fresh editor.
    remount(); load(initial("definition-c"));
    const before = structuredClone(doc.state.draft);
    expect(clipboard.paste()).toBe(false);
    expect(doc.state.draft).toEqual(before);
    expect(editCount).toBe(0);
    expect(status).toBe("The copied activities came from another workflow, so they can't be pasted here yet.");
  });

  it("explains a refused paste after a full page load in the same tab", async () => {
    expect(clipboard.copy()).toBe(true);
    await freshClipboardModule(); remount(); load(initial("definition-d"));
    expect(clipboard.paste()).toBe(false);
    expect(editCount).toBe(0);
    expect(status).toBe("The copied activities came from another workflow, so they can't be pasted here yet.");
  });

  it("uses the singular form for one copied activity", () => {
    flushSync(() => clipboard.duplicate(["producer"]));
    remount(); load(initial("definition-e"));
    expect(clipboard.paste()).toBe(false);
    expect(status).toBe("The copied activity came from another workflow, so it can't be pasted here yet.");
  });

  it("says nothing on paste when nothing was copied", () => {
    expect(clipboard.paste()).toBe(false);
    remount(); load(initial("definition-b"));
    expect(clipboard.paste()).toBe(false);
    expect(status).toBe("");
  });

  it("says nothing when the editor reopens on the workflow the activities came from", () => {
    expect(clipboard.copy()).toBe(true); status = "";
    remount(); load(initial());
    expect(clipboard.paste()).toBe(false);
    expect(status).toBe("");
  });

  it("says nothing while the document is read-only, even after a copy elsewhere", () => {
    expect(clipboard.copy()).toBe(true); status = "";
    remount(); load(initial("definition-b")); flushSync(() => disable(true));
    expect(clipboard.paste()).toBe(false);
    expect(status).toBe("");
  });

  it("ignores an unreadable stored origin after a full page load", async () => {
    sessionStorage.setItem("elsa.studio.activityClipboard.copyOrigin", "{not json");
    await freshClipboardModule(); remount(); load(initial("definition-b"));
    expect(clipboard.paste()).toBe(false);
    expect(status).toBe("");
  });

  it("preserves but cannot apply the clipboard while the document is read-only", () => {
    expect(clipboard.copy()).toBe(true);
    flushSync(() => disable(true));
    expect(clipboard.copy()).toBe(false);
    expect(clipboard.duplicate()).toBe(false);
    expect(clipboard.paste()).toBe(false);
    expect(nodes()).toHaveLength(2); expect(editCount).toBe(0);
    flushSync(() => disable(false));
    expect(clipboard.paste()).toBe(true);
    expect(nodes()).toHaveLength(4); expect(editCount).toBe(1);
  });
});
