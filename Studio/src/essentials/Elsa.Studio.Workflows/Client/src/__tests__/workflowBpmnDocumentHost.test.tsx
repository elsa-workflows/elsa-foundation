import React from "react";
import { createRoot } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createWorkflowBpmnDocumentHost } from "../workflow-editor/workflowBpmnDocumentHost";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { createAuthoringCheckpoint, type AuthoringCheckpointToken } from "../graph-authoring/authoringCheckpoint";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";
import type { BpmnDocumentSyntaxPlan } from "../bpmn/bpmnDocumentSyntaxChange";

const cleanups: (() => void)[] = [];
afterEach(() => cleanups.splice(0).forEach(cleanup => cleanup()));
function activity(nodeId: string): ActivityNode { return { nodeId, activityVersionId: "exact@1", inputs: [], outputs: [] }; }
function source(): ActivityNode {
  return { ...activity("owner"), structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    documentXml: "retained source", activities: [activity("native"), activity("deleted")], bindings: [], variables: [] } } };
}
const plan = (removedNodeIds: string[] = []): BpmnDocumentSyntaxPlan => ({ transaction: {}, removedNodeIds, removedElementIds: [], unqualifiedOwnershipIds: [] });
function harness(owner = source()) {
  const element = document.createElement("div"); document.body.append(element); const root = createRoot(element);
  let doc!: WorkflowDocumentApi, authorized = true, authorityId = "author", scopeId = "root", resourceId = "workflow";
  function Harness() { doc = useWorkflowDocument(); return <output>{doc.state.draft?.state.rootActivity?.nodeId}</output>; }
  flushSync(() => root.render(<Harness />));
  const draft: WorkflowDraft = { id: "draft", definitionId: "definition", state: { rootActivity: owner }, validationErrors: [],
    layout: [{ nodeId: "native", x: 10, y: 20 }, { nodeId: "deleted", x: 30, y: 40 }, { nodeId: "unrelated", x: 50, y: 60 }],
    activityPresentation: [{ nodeId: "deleted", displayName: "Remove with owned node" }, { nodeId: "unrelated", displayName: "Retain" }] };
  flushSync(() => doc.loadDraft(draft));
  const readLive = () => { const live = doc.readCommitted(); return { snapshot: live.state.draft!, lineage: ["parent-line"],
    token: { authorityId, resourceId, scopeId, revision: live.revision } satisfies AuthoringCheckpointToken }; };
  const coordinator = createAuthoringCheckpoint({ readLive });
  const edit = vi.fn((recipe: Parameters<WorkflowDocumentApi["editDraft"]>[0]) => flushSync(() => doc.editDraft(recipe)));
  const host = createWorkflowBpmnDocumentHost({ id: "draft:owner", ownerId: "owner", initial: readLive(), coordinator, readLive,
    canEdit: () => authorized, catalog: () => new Map(), editDraft: edit, notifyWorkChanged: vi.fn(),
    transitionScope: async action => { const result = await coordinator.transition("scope-change", () => { flushSync(() => { doc.invalidateAuthoringScope(); action(); }); return undefined; }); if (result.cleanupFailure) throw result.cleanupFailure; } });
  cleanups.push(() => { flushSync(() => root.unmount()); element.remove(); });
  return { host, edit, readLive, document: () => doc,
    revoke: () => { authorized = false; }, changeAuthority: () => { authorityId = "other-author"; },
    changeResource: () => { resourceId = "other-resource"; }, changeScope: () => { scopeId = "other-scope"; } };
}
describe("actual Workflow document adapter for the shared BPMN Surface", () => {
  it("acknowledges through the committed reducer and removes only independently planned native side tables", () => {
    const h = harness(), expected = h.host.readCanonical(), candidate = structuredClone(expected.owner);
    candidate.structure!.payload.activities = [activity("native")];
    expect(h.host.admit(expected, candidate, plan(["deleted"]), { preparation: null })).toBe(true);
    const live = h.readLive();
    expect(live.snapshot.state.rootActivity).toEqual(candidate);
    expect(live.snapshot.layout.map(record => record.nodeId)).toEqual(["native", "unrelated"]);
    expect(live.snapshot.activityPresentation).toEqual([{ nodeId: "unrelated", displayName: "Retain" }]);
    expect(live.token.revision).toBe(expected.token.revision + 1);
    expect(h.edit).toHaveBeenCalledOnce();
  });

  it.each(["owner", "layout", "epoch"] as const)("refuses a stale %s baseline without replacing canonical source", field => {
    const h = harness(); let expected = structuredClone(h.host.readCanonical());
    if (field === "owner") expected.owner.displayName = "stale";
    if (field === "layout") expected.layout[0].x++;
    if (field === "epoch") expected = { ...expected, token: { ...expected.token, revision: expected.token.revision - 1 } };
    const original = h.readLive().snapshot;
    expect(h.host.admit(expected, { ...expected.owner, displayName: "native" }, plan(), { preparation: null })).toBe(false);
    expect(h.readLive().snapshot).toBe(original); expect(h.edit).not.toHaveBeenCalled();
  });

  it.each(["revoke", "changeAuthority", "changeResource", "changeScope"] as const)("refuses retained admission after %s but keeps committed source readable", change => {
    const h = harness(), expected = h.host.readCanonical(); h[change]();
    expect(h.host.canEdit()).toBe(false);
    expect(h.host.readCanonical().owner).toBe(expected.owner);
    expect(h.host.admit(expected, { ...expected.owner, displayName: "late" }, plan(), { preparation: null })).toBe(false);
    expect(h.edit).not.toHaveBeenCalled();
  });

  it("keeps a no-op prepared legacy source exact and migrates only with a real edit", () => {
    const unrelatedBpmn = { ...activity("nested"), structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: { elements: [{ elementId: "unrelated-element", elementType: "task" }], activities: [], sequenceFlows: [] } } };
    const legacy = { ...activity("owner"), structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: {
      elements: [{ elementId: "migrated-element", elementType: "task" }], sequenceFlows: [], activities: [activity("native"), unrelatedBpmn] } } };
    const h = harness(legacy);
    flushSync(() => h.document().editDraft(({ draft: current }) => ({ ...current!, layout: [...current!.layout,
      { nodeId: "migrated-element", x: 100, y: 200 }, { nodeId: "unrelated-element", x: 300, y: 400 }] })));
    const expected = h.host.readCanonical(), prepared = source(); prepared.structure!.payload.activities = [activity("native"), unrelatedBpmn];
    const preparation = { canonicalBaseOwner: expected.owner, canonicalBaseLayout: expected.layout, preparedOwner: prepared };
    const original = h.readLive().snapshot;
    expect(h.host.admit(expected, prepared, plan(), { preparation })).toBe(true);
    expect(h.readLive().snapshot).toBe(original); expect(h.edit).not.toHaveBeenCalled();
    const candidate = { ...prepared, displayName: "First actual edit" };
    expect(h.host.admit(expected, candidate, plan(), { preparation })).toBe(true);
    expect(h.readLive().snapshot.state.rootActivity).toEqual(candidate);
    expect(h.readLive().snapshot.layout.map(record => record.nodeId)).toEqual(["native", "deleted", "unrelated", "unrelated-element"]);
    expect(h.edit).toHaveBeenCalledOnce();
  });

  it("refuses a prepared projection whose layout no longer matches and preserves native history inputs", () => {
    const h = harness(), expected = h.host.readCanonical(), original = h.readLive().snapshot;
    const preparation = { canonicalBaseOwner: expected.owner, canonicalBaseLayout: [], preparedOwner: source() };
    expect(h.host.admit(expected, { ...source(), displayName: "edit" }, plan(), { preparation })).toBe(false);
    expect(h.readLive().snapshot).toBe(original); expect(h.edit).not.toHaveBeenCalled();
  });

  it("invalidates a standard scope epoch inside the shared fence without changing persisted source", async () => {
    const h = harness(), original = h.readLive(); const action = vi.fn(() => undefined);
    await h.host.transitionScope(action);
    expect(action).toHaveBeenCalledOnce();
    expect(h.readLive().token.revision).toBe(original.token.revision + 1);
    expect(h.readLive().snapshot).toBe(original.snapshot);
  });
});
