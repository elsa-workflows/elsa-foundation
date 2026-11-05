import { describe, expect, it } from "vitest";
import { applyBpmnJsCanvasChange, type BpmnJsCanvasChange } from "../bpmn/bpmnJsAuthoring";
import { resolveScope, type ScopeFrame } from "../workflowAdapter";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";

function leaf(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: "module-owned@exact", inputs: [{ referenceKey: "Connection", value: { connectionId: "retain-ref" } }], outputs: [], contributed: { retained: true } };
}
function composite(nodeId: string, kind: string, activities: ActivityNode[], extra: Record<string, unknown> = {}): ActivityNode {
  return { ...leaf(nodeId), structure: { kind, schemaVersion: "1.0.0", payload: { activities, ...extra } } };
}
function fixture() {
  const nested = composite("deleted-bpmn-body", "elsa.bpmn.structure", [leaf("deleted-leaf"), leaf("deleted-listener")], {
    elements: [{ elementId: "deleted-nested-element", elementType: "task", childNodeId: "deleted-leaf", listenerNodeId: "deleted-listener" }], sequenceFlows: []
  });
  const deleted = composite("deleted-composite", "elsa.sequence.structure", [nested]);
  const owner = composite("process", "elsa.bpmn.structure", [deleted, leaf("retained-activity")], {
    elements: [{ elementId: "deleted-outer-element", elementType: "subProcess", childNodeId: deleted.nodeId },
      { elementId: "retained-element", elementType: "task", childNodeId: "retained-activity" }], sequenceFlows: [],
    variables: [{ name: "typed", type: { alias: "Int32" }, default: { value: 42 } }], moduleMetadata: { retained: "exact" }
  });
  const root = composite("root-sequence", "elsa.sequence.structure", [owner, leaf("unrelated-sibling")]);
  const ids = ["root-sequence", "process", "deleted-outer-element", "deleted-composite", "deleted-bpmn-body", "deleted-nested-element",
    "deleted-leaf", "deleted-listener", "retained-element", "retained-activity", "unrelated-sibling"];
  const draft: WorkflowDraft = {
    id: "draft", definitionId: "definition", sourceVersionId: "saved-exact-version",
    state: { rootActivity: root, variables: [], inputs: [], outputs: [], strategyOptions: { retained: true } },
    layout: ids.map((nodeId, index) => ({ nodeId, x: index * 10, y: index, additionalProperties: { retained: nodeId } })),
    activityPresentation: ids.map(nodeId => ({ nodeId, displayName: `Presentation: ${nodeId}` })), validationErrors: []
  };
  const frames: ScopeFrame[] = [{ ownerNodeId: owner.nodeId, slotId: "elsa.bpmn.structure:activities", label: "Process" }];
  return { draft, frames, owner };
}
function deletion(owner: ActivityNode): ActivityNode {
  return { ...owner, structure: { ...owner.structure!, payload: { ...owner.structure!.payload,
    activities: (owner.structure!.payload.activities as ActivityNode[]).filter(activity => activity.nodeId === "retained-activity"),
    elements: (owner.structure!.payload.elements as { elementId: string }[]).filter(element => element.elementId === "retained-element") } } };
}
function changeFor(draft: WorkflowDraft, owner: ActivityNode): BpmnJsCanvasChange {
  return { owner: deletion(owner), baseOwner: structuredClone(owner), baseLayout: structuredClone(draft.layout),
    // The bridge removes its outer element, but stale descendant side tables still arrive here.
    layout: draft.layout.filter(record => record.nodeId !== "deleted-outer-element").map(record => record.nodeId === "retained-element" ? { ...record, x: 730 } : record) };
}

describe("BPMN canvas application to the live workflow draft", () => {
  it("removes deleted descendant activity, listener and nested BPMN element side tables atomically", () => {
    const { draft, frames, owner } = fixture(); const before = structuredClone(draft);
    const change = changeFor(draft, owner);
    const next = applyBpmnJsCanvasChange(draft, frames, change, undefined)!;
    expect(next).not.toBeNull();
    const removed = ["deleted-outer-element", "deleted-composite", "deleted-bpmn-body", "deleted-nested-element", "deleted-leaf", "deleted-listener"];
    for (const id of removed) {
      expect(next.layout.some(record => record.nodeId === id)).toBe(false);
      expect(next.activityPresentation!.some(record => record.nodeId === id)).toBe(false);
    }
    expect(resolveScope(next.state.rootActivity, frames)?.owner).toBe(change.owner);
    expect(next.layout.find(record => record.nodeId === "retained-element")).toMatchObject({ x: 730, additionalProperties: { retained: "retained-element" } });
    expect(next.activityPresentation!.find(record => record.nodeId === "retained-activity")).toBe(draft.activityPresentation!.find(record => record.nodeId === "retained-activity"));
    expect(next.state.strategyOptions).toBe(draft.state.strategyOptions);
    expect(next.id).toBe(draft.id);
    expect(next.definitionId).toBe(draft.definitionId);
    expect(next.sourceVersionId).toBe(draft.sourceVersionId);
    expect(draft).toEqual(before);
  });

  it("keeps unrelated graph data and live layout despite omission or attempted outside-scope changes", () => {
    const { draft, frames, owner } = fixture();
    const change = changeFor(draft, owner);
    change.layout = change.layout.filter(record => record.nodeId !== "unrelated-sibling").map(record => record.nodeId === "root-sequence" ? { ...record, x: 9999 } : record);
    const next = applyBpmnJsCanvasChange(draft, frames, change, undefined)!;
    for (const id of ["root-sequence", "unrelated-sibling"]) {
      expect(next.layout.find(record => record.nodeId === id)).toBe(draft.layout.find(record => record.nodeId === id));
      expect(next.activityPresentation!.find(record => record.nodeId === id)).toBe(draft.activityPresentation!.find(record => record.nodeId === id));
    }
    const beforeChildren = draft.state.rootActivity!.structure!.payload.activities as ActivityNode[];
    const afterChildren = next.state.rootActivity!.structure!.payload.activities as ActivityNode[];
    expect(afterChildren[1]).toBe(beforeChildren[1]);
    expect(resolveScope(next.state.rootActivity, frames)!.owner.structure!.payload.variables).toBe(owner.structure!.payload.variables);
    expect(resolveScope(next.state.rootActivity, frames)!.owner.structure!.payload.moduleMetadata).toBe(owner.structure!.payload.moduleMetadata);
  });

  it("supports the same deletion at a root BPMN scope without changing the process identity", () => {
    const { draft, owner } = fixture();
    draft.state.rootActivity = owner;
    const change = changeFor(draft, owner);
    const next = applyBpmnJsCanvasChange(draft, [], change, undefined)!;
    expect(next.state.rootActivity).toBe(change.owner);
    expect(next.layout.some(record => record.nodeId === "deleted-nested-element")).toBe(false);
    expect(next.activityPresentation!.some(record => record.nodeId === "deleted-listener")).toBe(false);
  });

  it.each([
    ["stale bound properties", (_draft: WorkflowDraft, _frames: ScopeFrame[], _change: BpmnJsCanvasChange, owner: ActivityNode) => {
      (owner.structure!.payload.activities as ActivityNode[])[1].inputs = [{ referenceKey: "Updated", value: { expressionType: "JavaScript", value: "latest" } }];
    }],
    ["stale layout", (draft: WorkflowDraft) => { draft.layout[0] = { ...draft.layout[0], x: 42 }; }],
    ["wrong node identity", (_draft: WorkflowDraft, _frames: ScopeFrame[], change: BpmnJsCanvasChange) => { change.owner.nodeId = "another-process"; }],
    ["wrong base identity", (_draft: WorkflowDraft, _frames: ScopeFrame[], change: BpmnJsCanvasChange) => { change.baseOwner.nodeId = "another-process"; }],
    ["departed scope", (_draft: WorkflowDraft, frames: ScopeFrame[]) => { frames[0] = { ...frames[0], ownerNodeId: "departed-process" }; }],
    ["wrong slot", (_draft: WorkflowDraft, frames: ScopeFrame[]) => { frames[0] = { ...frames[0], slotId: "elsa.bpmn.structure:missing" }; }],
    ["scope changed to another mode", (_draft: WorkflowDraft, frames: ScopeFrame[]) => { frames.splice(0); }],
    ["owner kind changed", (_draft: WorkflowDraft, _frames: ScopeFrame[], change: BpmnJsCanvasChange) => { change.owner.structure!.kind = "elsa.flowchart.structure"; }],
    ["activity version changed", (_draft: WorkflowDraft, _frames: ScopeFrame[], change: BpmnJsCanvasChange) => { change.owner.activityVersionId = "different-owner-version"; }],
    ["missing root", (draft: WorkflowDraft) => { draft.state.rootActivity = null; }],
    ["duplicate layout", (_draft: WorkflowDraft, _frames: ScopeFrame[], change: BpmnJsCanvasChange) => { change.layout.push({ ...change.layout.find(record => record.nodeId === "retained-element")! }); }]
  ])("rejects %s without changing the live draft", (_label, mutate) => {
    const { draft, frames, owner } = fixture();
    const change = changeFor(draft, owner);
    mutate(draft, frames, change, owner);
    const before = structuredClone(draft);
    expect(applyBpmnJsCanvasChange(draft, frames, change, undefined)).toBeNull();
    expect(draft).toEqual(before);
  });
});
