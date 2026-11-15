// @vitest-environment node
import { nativeBlockEditorDocument, nativeBlockPlannerContext } from "../graph-authoring/nativeBlockEditorContext";
import { describe, expect, it } from "vitest";
import { createActivityNode } from "../workflowAdapter";
import { applyNativeBlockPlan, type NativeBlockContext, type NativeBlockPlan } from "../graph-authoring/nativeBlocks";
import { planCreateBpmnBlock, planExtractBpmnBlock, planUngroupBpmnBlock } from "../graph-authoring/bpmnBlocks";
import { describeNativeBlockChange } from "../graph-authoring/nativeBlockPreview";
import { bpmnActivity, writeLine, setVariableActivity } from "./fixtures";
import type { ActivityNode } from "../workflowTypes";
import { readFileSync } from "node:fs";
import { preservesJavaScriptWithAddedVariable } from "../../../../../../../Extensions/ExpressionEditors.JavaScript/studio/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/variableAdditionPreservation";
const request = { nodeId: "block", activityVersionId: bpmnActivity.activityVersionId, displayName: "Shipping", selectedNodeIds: ["ea", "eb"] };
function context(): NativeBlockContext {
  const root = createActivityNode(bpmnActivity, "root");
  root.structure!.payload = { activities: ["a", "b", "c"].map(id => createActivityNode(writeLine, id)),
    elements: [{ elementId: "start", elementType: "startEvent" }, ...["a", "b", "c"].map(id => ({ elementId: `e${id}`, elementType: "task", childNodeId: id })), { elementId: "end", elementType: "endEvent" }],
    sequenceFlows: ["start", "ea", "eb", "ec"].map((id, index) => ({ flowId: `f${index}`, sourceRef: id, targetRef: ["ea", "eb", "ec", "end"][index] })) };
  return { document: { rootActivity: root, layout: ["start", "ea", "eb", "ec", "end"].map((id, index) => ({ nodeId: id, x: index * 160.25, y: 80.5 })),
    activityPresentation: [{ nodeId: "a", displayName: "Original" }] }, catalog: [bpmnActivity, writeLine, setVariableActivity], scope: [], canEdit: true, sourceKey: "workflow:1/rev:1", referenceContext: [] };
}
function accepted(plan: NativeBlockPlan) {
  expect(plan.status, JSON.stringify(plan)).toBe("ready");
  if (plan.status !== "ready") throw new Error(JSON.stringify(plan));
  return plan;
}
function children(root: ActivityNode) { return root.structure!.payload.activities as ActivityNode[]; }
function refuse(plan: NativeBlockPlan, code: string) { expect(plan).toMatchObject({ status: "refused", diagnostics: [{ code }] }); }

describe("BPMN resource proposals", () => {
  it("matches all six production-planner documents consumed by the actual native runtime proof", () => {
    const moduleUrl = import.meta.url;
    const fixture = JSON.parse(readFileSync(new URL("../../../../../../../specs/1204-named-embedded-blocks/contracts/bpmn-result-transfer-cases.json", moduleUrl), "utf8")) as {
      catalog: NativeBlockContext["catalog"]; cases: { context: Omit<NativeBlockContext, "catalog">; request: Parameters<typeof planExtractBpmnBlock>[1]; selection: string[]; grouped: unknown; ungrouped: unknown }[];
    };
    expect(fixture.cases).toHaveLength(6);
    for (const test of fixture.cases) {
      const source = { ...test.context, catalog: fixture.catalog, expressionScopePreservation: { revision: "javascript-variable-addition-v1", allowsVariableAddition: preservesJavaScriptWithAddedVariable } };
      const plan = accepted(planExtractBpmnBlock(source, test.request));
      expect(plan.document).toEqual(test.grouped); expect(plan.selection).toEqual(test.selection);
      expect(accepted(planUngroupBpmnBlock({ ...source, document: plan.document }, plan.selection[0])).document).toEqual(test.ungrouped);
    }
  });
  it.each(["create", "extract"])("can Ungroup an untouched %s subprocess after the normal editor/wire round trip", action => {
    const source = context();
    const proposal = accepted(action === "create" ? planCreateBpmnBlock(source, { ...request, position: { x: 0, y: 0 } }) : planExtractBpmnBlock(source, request));
    const editor = nativeBlockEditorDocument(proposal.document, source.catalog);
    const persisted = nativeBlockPlannerContext({ ...source, document: editor }, { x: 0, y: 0 });
    const block = children(persisted.document.rootActivity).find(child => child.nodeId === request.nodeId)!;
    expect(block.inputs).toContainEqual({ referenceKey: "CanStartWorkflow", value: { value: "false", expressionType: "Literal" } });
    const result = accepted(planUngroupBpmnBlock(persisted, proposal.selection[0]));
    expect(children(result.document.rootActivity).some(child => child.nodeId === request.nodeId)).toBe(false);
  });
  it("refuses authored subprocess properties that would disappear during Ungroup", () => {
    const source = context(), proposal = accepted(planExtractBpmnBlock(source, request));
    (proposal.document.rootActivity.structure!.payload.elements as Record<string, unknown>[]).find(element => element.elementId === proposal.selection[0])!.properties = { reviewTag: "keep" };
    const original = structuredClone(proposal.document);
    refuse(planUngroupBpmnBlock({ ...source, document: proposal.document }, proposal.selection[0]), "boundary-metadata");
    expect(proposal.document).toEqual(original);
  });
  it("keeps refusal of a real invocation setting after canonical wire comparison", () => {
    const source = context(), proposal = accepted(planExtractBpmnBlock(source, request));
    const persisted = nativeBlockPlannerContext({ ...source, document: nativeBlockEditorDocument(proposal.document, source.catalog) }, { x: 0, y: 0 });
    const input = children(persisted.document.rootActivity).find(child => child.nodeId === request.nodeId)!.inputs[0] as { autoEvaluate?: boolean };
    input.autoEvaluate = true;
    refuse(planUngroupBpmnBlock(persisted, proposal.selection[0]), "boundary-settings");
  });
  it("does not normalize away a duplicate invocation binding before Ungroup", () => {
    const source = context(), proposal = accepted(planExtractBpmnBlock(source, request));
    const block = children(proposal.document.rootActivity).find(child => child.nodeId === request.nodeId)!;
    block.inputs.unshift({ referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: true } });
    refuse(planUngroupBpmnBlock({ ...source, document: proposal.document }, proposal.selection[0]), "boundary-settings");
  });
  it("inserts an empty ordinary subprocess on the selected task's existing route", () => {
    const source = context(), before = structuredClone(source);
    const created = accepted(planCreateBpmnBlock(source, { ...request, beforeElementId: "eb", position: { x: 200.5, y: 100.25 } }));
    const block = children(created.document.rootActivity).find(child => child.nodeId === "block")!;
    expect(children(block)).toEqual([]);
    expect(block.inputs).toContainEqual({ referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: false } });
    const flows = created.document.rootActivity.structure!.payload.sequenceFlows;
    expect(flows).toContainEqual({ flowId: "f1", sourceRef: "ea", targetRef: created.selection[0] });
    expect(flows).toContainEqual(expect.objectContaining({ sourceRef: created.selection[0], targetRef: "eb" }));
    const ungrouped = accepted(planUngroupBpmnBlock({ ...source, document: created.document }, created.selection[0]));
    expect(children(ungrouped.document.rootActivity)).toEqual(children(source.document.rootActivity));
    expect(ungrouped.document.rootActivity.structure!.payload.sequenceFlows).toEqual(source.document.rootActivity.structure!.payload.sequenceFlows);
    expect(source).toEqual(before);
  });
  it("creates real ordinary start/subprocess/end routing in a previously empty BPMN process", () => {
    const source = context(); source.document.layout = [];
    source.document.rootActivity.structure!.payload = { activities: [], elements: [], sequenceFlows: [] };
    const created = accepted(planCreateBpmnBlock(source, { ...request, position: { x: 0, y: 0 } }));
    expect(created.document.rootActivity.structure!.payload.elements).toMatchObject([
      { elementType: "startEvent" }, { elementType: "subProcess", childNodeId: "block" }, { elementType: "endEvent" }]);
    expect(created.document.rootActivity.structure!.payload.sequenceFlows).toHaveLength(2);
    expect(source.document.rootActivity.structure!.payload.elements).toEqual([]);
  });
  it("refuses insertion at a missing element or a plain start", () => {
    for (const beforeElementId of ["missing", "start"])
      refuse(planCreateBpmnBlock(context(), { ...request, beforeElementId, position: { x: 0, y: 0 } }), "unsupported-bpmn-insertion");
  });
  it("previews the native subprocess, moved work, synthetic events and rewired BPMN flows", () => {
    const source = context(), result = accepted(planExtractBpmnBlock(source, request));
    const preview = describeNativeBlockChange(source.document, result.document, source.catalog);
    expect(preview.containers).toContainEqual({ id: "block", name: "Shipping", kind: "BPMN subprocess", children: 2, change: "add" });
    expect(preview.movedSteps).toContain("Original → Shipping");
    expect(preview.addedElements).toContain("Start event in Shipping");
    expect(preview.addedElements).toContain("End event in Shipping");
    expect(preview.addedConnections).toContain("Start event → Shipping in BPMN Process");
    expect(preview.removedConnections).toContain("Start event → Original in BPMN Process");
  });
  it("preserves the source, all exact bindings and typed names through extract and Ungroup", () => {
    const source = context(), original = structuredClone(source);
    const extracted = accepted(planExtractBpmnBlock(source, request));
    expect(extracted.selection).toHaveLength(1);
    expect(extracted.selection[0]).not.toBe("block");
    expect(extracted.document.activityPresentation).toContainEqual({ nodeId: "block", displayName: "Shipping" });
    const ungrouped = accepted(planUngroupBpmnBlock({ ...source, document: extracted.document }, extracted.selection[0]));
    expect(children(ungrouped.document.rootActivity)).toEqual(children(source.document.rootActivity));
    expect(ungrouped.document.rootActivity.structure!.payload.sequenceFlows).toEqual(expect.arrayContaining(source.document.rootActivity.structure!.payload.sequenceFlows as unknown[]));
    expect(ungrouped.document.rootActivity.structure!.payload.sequenceFlows).toHaveLength(4);
    expect(ungrouped.selection).toEqual(["ea", "eb"]);
    expect(ungrouped.document.layout).toEqual(source.document.layout);
    expect(ungrouped.document.activityPresentation).toEqual(source.document.activityPresentation);
    expect(source).toEqual(original);
  });
  it.each(["permission", "revision", "catalog", "references", "scope", "document"])("rejects a stale %s at apply", change => {
    const source = context(), plan = accepted(planExtractBpmnBlock(source, request));
    if (change === "permission") source.canEdit = false;
    if (change === "revision") source.sourceKey = "workflow:1/rev:2";
    if (change === "catalog") source.catalog = source.catalog.filter(item => item !== bpmnActivity);
    if (change === "references") source.referenceContext.push({ binding: "changed" });
    if (change === "scope") source.scope = [{ ownerNodeId: "root", slotId: "Bpmn.Activities", label: "Changed" }];
    if (change === "document") source.document.layout[0].x++;
    refuse(applyNativeBlockPlan(source, plan), "stale-source");
  });
  it("rejects mutations of a validated candidate", () => {
    const source = context(), plan = accepted(planExtractBpmnBlock(source, request));
    plan.document.activityPresentation[0].displayName = "changed";
    refuse(applyNativeBlockPlan(source, plan), "changed-preview");
  });
  it("refuses unavailable versions, read-only documents, and reserved BPMN identities", () => {
    const source = context();
    refuse(planExtractBpmnBlock({ ...source, canEdit: false }, request), "read-only");
    refuse(planExtractBpmnBlock(source, { ...request, activityVersionId: "missing" }), "unavailable-bpmn-composite");
    refuse(planExtractBpmnBlock(source, { ...request, nodeId: "f0" }), "occupied-identity");
  });
  it("schedules ordinary Set result captures on real BPMN elements/flows and preserves them through Ungroup", () => {
    const source = context();
    children(source.document.rootActivity)[2].inputs = [{ referenceKey: "Text", value: { expressionType: "ActivityResult", value: { producerNodeId: "a", projectionKey: "Result", producerScopeId: "root" } } }];
    const extracted = accepted(planExtractBpmnBlock(source, request));
    const root = extracted.document.rootActivity, block = children(root)[0], body = block.structure!.payload;
    const capture = children(block).find(node => node.activityVersionId === "elsa.intrinsic.set@1")!;
    expect(capture).toBeDefined();
    const elements = body.elements as { elementId: string; childNodeId?: string }[];
    const captureElement = elements.find(element => element.childNodeId === capture.nodeId)!;
    const flows = body.sequenceFlows as { sourceRef: string; targetRef: string }[];
    expect(flows).toContainEqual(expect.objectContaining({ sourceRef: "ea", targetRef: captureElement.elementId }));
    expect(flows).toContainEqual(expect.objectContaining({ sourceRef: captureElement.elementId, targetRef: "eb" }));
    expect(children(root)[1].inputs[0]).toMatchObject({ value: { expressionType: "Variable" } });
    const ungrouped = accepted(planUngroupBpmnBlock({ ...source, document: extracted.document }, extracted.selection[0]));
    expect(children(ungrouped.document.rootActivity).map(node => node.nodeId)).toEqual(["a", capture.nodeId, "b", "c"]);
    expect(ungrouped.document.rootActivity.structure!.payload.variables).toEqual(root.structure!.payload.variables);
  });
  it("refuses a result reader preceding the extracted producer", () => {
    const source = context();
    children(source.document.rootActivity)[0].inputs = [{ referenceKey: "Text", value: { expressionType: "ActivityResult", value: { producerNodeId: "b", projectionKey: "Result", producerScopeId: "root" } } }];
    refuse(planExtractBpmnBlock(source, { ...request, selectedNodeIds: ["eb"] }), "unproven-result-transfer");
  });
  it.each(["variable", "event-name", "flow-name", "settings", "reference"])("refuses Ungroup that would lose %s", change => {
    const source = context(), extracted = accepted(planExtractBpmnBlock(source, request));
    const next = { ...source, document: extracted.document }, block = children(next.document.rootActivity)[0];
    if (change === "variable") block.structure!.payload.variables = [{ referenceKey: "local", name: "Local" }];
    if (change === "event-name") (block.structure!.payload.elements as Record<string, unknown>[])[0].name = "Authored start";
    if (change === "flow-name") (block.structure!.payload.sequenceFlows as Record<string, unknown>[])[0].name = "Authored entry";
    if (change === "settings") block.inputs.push({ referenceKey: "Custom", value: { expressionType: "Literal", value: 1 } });
    if (change === "reference") next.referenceContext = [{ producerNodeId: "block" }];
    const result = planUngroupBpmnBlock(next, extracted.selection[0]);
    expect(result.status).toBe("refused");
    expect(children(next.document.rootActivity)[0].nodeId).toBe("block");
  });
});
