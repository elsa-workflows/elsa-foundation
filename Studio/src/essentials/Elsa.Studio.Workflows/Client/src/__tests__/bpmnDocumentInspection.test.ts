import { describe, expect, it } from "vitest";
import { findActivityNode, getChildSlots } from "../workflowAdapter";
import type { VariableDefinition, WorkflowDefinitionState } from "../workflowTypes";
import { applyBpmnDocumentTransaction, readBpmnDocument } from "../bpmn/bpmnDocument";
import { bpmnDocumentInspectionState, planBpmnDocumentInspectionChange, projectBpmnDocumentScopeView, readBpmnDocumentInspection } from "../bpmn/bpmnDocumentInspection";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

const variable = (id: string): VariableDefinition => ({ referenceKey: id, name: id, type: { alias: "String", collectionKind: "Single" } });
function owner() {
  const result = operationOwner(), original = readBpmnDocument(result), payload = result.structure!.payload;
  payload.documentXml = (payload.documentXml as string).replace('<bd:BPMNDiagram id="Diagram">', '<b:process id="P2"><b:task id="Other"/></b:process><bd:BPMNDiagram id="Diagram">');
  payload.variables = [variable("main")];
  payload.activities = [...original.payload.activities, { nodeId: "nested-native", activityVersionId: "leaf@exact", inputs: [], outputs: [], future: { keep: true } }, { nodeId: "other-native", activityVersionId: "leaf@exact", inputs: [], outputs: [] }];
  payload.bindings = [...original.payload.bindings, { elementId: "Child", childNodeId: "nested-native" }, { elementId: "Other", childNodeId: "other-native" }];
  payload.scopeHeaders = [
    { scopeElementId: "S", nodeId: "header-S", activityVersionId: "bpmn@exact", inputs: [], outputs: [], executionPlacement: "background", variables: [variable("nested")], future: { exact: true } },
    { scopeElementId: "P2", nodeId: "header-P2", activityVersionId: "bpmn@exact", inputs: [], outputs: [], variables: [variable("independent")] }
  ];
  return result;
}
describe("module-owned BPMN inspector/query projection", () => {
  it("uses real invocation/header IDs and exposes derived client slots without serializing another graph", () => {
    const source = owner(), before = structuredClone(source), view = projectBpmnDocumentScopeView(source, "P");
    expect(view.nodeId).toBe(source.nodeId); expect(view.activityVersionId).toBe(source.activityVersionId);
    expect(getChildSlots(view)[0].activities.map(node => node.nodeId)).toEqual(["native", "header-S"]);
    expect(findActivityNode(view, "nested-native")?.nodeId).toBe("nested-native");
    expect(findActivityNode(view, "other-native")).toBeNull();
    const wire = JSON.parse(JSON.stringify(view));
    expect(wire.structure.payload.activities).toBeUndefined();
    expect(wire.structure.payload.owner).toEqual(source);
    expect(wire.structure.payload.variables).toEqual([variable("main")]);
    expect(source).toEqual(before);
  });

  it("keeps main-process lexical parents and isolates an independent process while preserving global contracts", () => {
    const source = owner(), outer = { nodeId: "outer", activityVersionId: "sequence@exact", inputs: [], outputs: [], structure: { kind: "elsa.sequence", schemaVersion: "1.0.0", payload: { activities: [source], variables: [variable("outer")] } } };
    const state: WorkflowDefinitionState = { rootActivity: outer, variables: [variable("global")], inputs: [{ name: "request" }], outputs: [{ name: "result" }] };
    const main = bpmnDocumentInspectionState(state, source, "P");
    expect(main.rootActivity!.nodeId).toBe("outer");
    expect(findActivityNode(main.rootActivity!, "header-S")).toBeTruthy();
    const independent = bpmnDocumentInspectionState(state, source, "P2");
    expect(independent.rootActivity!.nodeId).toBe("header-P2");
    expect(independent.variables).toEqual(state.variables); expect(independent.inputs).toEqual(state.inputs); expect(independent.outputs).toEqual(state.outputs);
    expect(getChildSlots(independent.rootActivity!)[0].activities.map(node => node.nodeId)).toEqual(["other-native"]);
    expect(findActivityNode(independent.rootActivity!, "native")).toBeNull();
    expect(independent.rootActivity!.structure!.payload.variables).toEqual([variable("independent")]);
  });

  it("folds actual header config/variables only, preserving full XML, native bodies and unrelated headers", () => {
    const source = owner(), before = readBpmnDocument(source), inspection = readBpmnDocumentInspection(source, operationScope, { elementId: "S", diId: "SDI" });
    expect(inspection.activity!.nodeId).toBe("header-S");
    const next = { ...inspection.activity!, inputs: [{ referenceKey: "start-new", value: { expressionType: "Literal", value: false } }], executionPlacement: undefined,
      structure: { ...inspection.activity!.structure!, payload: { ...inspection.activity!.structure!.payload, variables: [variable("edited")] } } };
    const changed = planBpmnDocumentInspectionChange(source, inspection, next), after = readBpmnDocument(changed.owner);
    expect(after.payload.documentXml).toBe(before.payload.documentXml); expect(after.payload.activities).toEqual(before.payload.activities);
    expect(after.payload.scopeHeaders![0]).toMatchObject({ variables: [variable("edited")], future: { exact: true }, inputs: next.inputs });
    expect(after.payload.scopeHeaders![0].executionPlacement).toBeUndefined();
    expect(after.payload.scopeHeaders![1]).toEqual(before.payload.scopeHeaders![1]);
    expect(source).toEqual(owner());
  });

  it("folds the selected process invocation fields at its actual owner identity without persisting the view", () => {
    const source = owner(), inspection = readBpmnDocumentInspection(source, operationScope, null);
    const next = { ...inspection.activity!, inputs: [{ referenceKey: "actual-root", value: { expressionType: "Literal", value: "configured" } }], structure: { ...inspection.activity!.structure!, payload: { ...inspection.activity!.structure!.payload, variables: [variable("root-edited")] } } };
    const changed = planBpmnDocumentInspectionChange(source, inspection, next);
    expect(changed.owner.inputs).toEqual(next.inputs);
    expect(changed.owner.structure!.kind).toBe("elsa.bpmn.structure");
    expect(readBpmnDocument(changed.owner).payload.variables).toEqual([variable("root-edited")]);
    expect(readBpmnDocument(changed.owner).payload.documentXml).toBe(readBpmnDocument(source).payload.documentXml);
  });

  it("retains child/listener roles and rejects replacing standard XML children through the native header editor", () => {
    const source = owner(), original = readBpmnDocument(source), payload = source.structure!.payload;
    payload.activities = [...original.payload.activities, { nodeId: "listener", activityVersionId: "listener@exact", inputs: [], outputs: [] }];
    payload.bindings = original.payload.bindings.map(binding => binding.elementId === "A" ? { ...binding, listenerNodeId: "listener" } : binding);
    expect(readBpmnDocumentInspection(source, operationScope, { elementId: "A" }, "listener").activity!.nodeId).toBe("listener");
    const inspection = readBpmnDocumentInspection(source, operationScope, { elementId: "S" });
    const next = { ...inspection.activity!, structure: { ...inspection.activity!.structure!, payload: { ...inspection.activity!.structure!.payload, activities: [] } } };
    expect(() => planBpmnDocumentInspectionChange(source, inspection, next)).toThrow("XML canvas");
  });

  it("refuses a late whole-node edit rather than replacing a newer native input with its stale snapshot", () => {
    const source = owner(), inspection = readBpmnDocumentInspection(source, operationScope, { elementId: "A" }), next = { ...inspection.activity!, outputs: [{ name: "late-output" }] };
    const current = applyBpmnDocumentTransaction(source, { configuration: { activities: readBpmnDocument(source).payload.activities.map(activity => activity.nodeId === "native" ? { ...activity, inputs: [{ name: "newer-input", expression: { type: "WorkflowRequest", value: { memberKey: "current", metadata: { keep: true } } } }] } : activity) } });
    expect(() => planBpmnDocumentInspectionChange(current, inspection, next)).toThrow("baseline is stale");
    expect(readBpmnDocument(current).payload.activities.find(activity => activity.nodeId === "native")!.inputs).toEqual([{ name: "newer-input", expression: { type: "WorkflowRequest", value: { memberKey: "current", metadata: { keep: true } } } }]);
  });
  it("refuses a retained native node when its original XML binding changed roles or owners", () => {
    const source = owner(), inspection = readBpmnDocumentInspection(source, operationScope, { elementId: "A" });
    const current = applyBpmnDocumentTransaction(source, { configuration: { bindings: readBpmnDocument(source).payload.bindings.map(binding => binding.elementId === "A" ? { elementId: "A", listenerNodeId: binding.childNodeId } : binding) } });
    expect(() => planBpmnDocumentInspectionChange(current, inspection, { ...inspection.activity!, outputs: [{ name: "late" }] })).toThrow("binding ownership changed");
  });

});
