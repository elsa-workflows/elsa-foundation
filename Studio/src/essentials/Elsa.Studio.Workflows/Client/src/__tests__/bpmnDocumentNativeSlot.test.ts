import { describe, expect, it } from "vitest";
import { findActivityNode, getChildSlots, type ScopeFrame } from "../workflowAdapter";
import type { VariableDefinition, WorkflowDefinitionState } from "../workflowTypes";
import { readBpmnDocument } from "../bpmn/bpmnDocument";
import { resolveBpmnDocumentNativeSlotState, type BpmnDocumentNativeSlotContext } from "../bpmn/bpmnDocumentNativeSlot";
import { operationOwner } from "./bpmnDocumentOperationsFixture";
const variable = (name: string): VariableDefinition => ({ referenceKey: name, name, type: { alias: "String", collectionKind: "Single" } });
function fixture() {
  const owner = operationOwner(), payload = owner.structure!.payload, source = readBpmnDocument(owner);
  payload.documentXml = source.payload.documentXml.replace('<bd:BPMNDiagram id="Diagram">', '<b:process id="Other"><b:task id="OtherTask"/></b:process><bd:BPMNDiagram id="Diagram">');
  payload.variables = [variable("MainLocal")];
  const sequence = (nodeId: string, childId: string) => ({ nodeId, activityVersionId: "sequence@exact", inputs: [], outputs: [], structure: { kind: "elsa.sequence", schemaVersion: "1.0.0", payload: { activities: [{ nodeId: childId, activityVersionId: "leaf@exact", inputs: [], outputs: [] }], variables: [variable(`${nodeId}Local`)] } } });
  payload.activities = [...source.payload.activities, sequence("native-sub", "sub-leaf"), sequence("native-other", "other-leaf")];
  payload.bindings = [...source.payload.bindings, { elementId: "Child", childNodeId: "native-sub" }, { elementId: "OtherTask", childNodeId: "native-other" }];
  payload.scopeHeaders = [{ scopeElementId: "S", nodeId: "actual-S-header", activityVersionId: "bpmn@exact", inputs: [], outputs: [], variables: [variable("SubLocal")] },
    { scopeElementId: "Other", nodeId: "actual-Other-header", activityVersionId: "bpmn@exact", inputs: [], outputs: [], variables: [variable("OtherLocal")] }];
  const state: WorkflowDefinitionState = { variables: [variable("WorkflowGlobal")], inputs: [{ name: "Request" }], outputs: [{ name: "Result" }], rootActivity: {
    nodeId: "outer", activityVersionId: "sequence@exact", inputs: [], outputs: [], structure: { kind: "elsa.sequence", schemaVersion: "1.0.0", payload: { activities: [owner], variables: [variable("OuterLocal")] } }
  } };
  return { owner, state };
}
const frames = (nativeOwnerNodeId: string): ScopeFrame[] => [{ ownerNodeId: "owner", slotId: "activities", label: "Process" }, { ownerNodeId: nativeOwnerNodeId, slotId: "activities", label: "Body" }];
describe("source-owned lexical queries while an opaque native Slot is open", () => {
  it.each([['P', 'native', 'inline'], ['P', 'native-sub', 'sub-leaf'], ['Other', 'native-other', 'other-leaf']])("retains exact %s→%s scope for actual %s without persisting its query view", (processId, nativeOwnerNodeId, leafId) => {
    const { owner, state } = fixture(), before = structuredClone(state), context: BpmnDocumentNativeSlotContext = { documentOwnerNodeId: owner.nodeId, processId, nativeOwnerNodeId };
    const query = resolveBpmnDocumentNativeSlotState(state, frames(nativeOwnerNodeId), context)!;
    expect(findActivityNode(query.state.rootActivity!, leafId)).toBeTruthy();
    expect(query.state.variables).toEqual(state.variables); expect(query.state.inputs).toEqual(state.inputs); expect(query.state.outputs).toEqual(state.outputs);
    if (processId === "Other") {
      expect(query.state.rootActivity!.nodeId).toBe("actual-Other-header");
      expect(findActivityNode(query.state.rootActivity!, "native")).toBeNull();
      expect(query.state.rootActivity!.structure!.payload.variables).toEqual([variable("OtherLocal")]);
    } else {
      expect(query.state.rootActivity!.nodeId).toBe("outer");
      const scope = findActivityNode(query.state.rootActivity!, "actual-S-header")!;
      expect(scope.structure!.payload.variables).toEqual([variable("SubLocal")]);
      expect(getChildSlots(scope)[0].activities.map(node => node.nodeId)).toEqual(["native-sub"]);
    }
    const wire = JSON.parse(JSON.stringify(query.state));
    const view = processId === "Other" ? wire.rootActivity : wire.rootActivity.structure.payload.activities[0];
    expect(view.structure.payload.activities).toBeUndefined(); expect(view.structure.payload.owner).toEqual(owner);
    expect(state).toEqual(before); expect(state.rootActivity!.structure!.payload.activities).toEqual([owner]);
  });
  it("expires context when leaving the actual native owner frame chain", () => {
    const { owner, state } = fixture(), context = { documentOwnerNodeId: owner.nodeId, processId: "P", nativeOwnerNodeId: "native" };
    expect(resolveBpmnDocumentNativeSlotState(state, [], context)).toBeNull();
    expect(resolveBpmnDocumentNativeSlotState(state, frames("native-other"), context)).toBeNull();
  });
  it("refuses a stale context when a physical binding now belongs to another process", () => {
    const { owner, state } = fixture(), context = { documentOwnerNodeId: owner.nodeId, processId: "P", nativeOwnerNodeId: "native-other" };
    expect(resolveBpmnDocumentNativeSlotState(state, frames("native-other"), context)).toBeNull();
  });
});
