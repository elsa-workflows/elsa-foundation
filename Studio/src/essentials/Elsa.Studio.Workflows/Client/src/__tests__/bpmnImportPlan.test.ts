// @vitest-environment node
import { expect, it } from "vitest";
import { createActivityNode } from "../workflowAdapter";
import { bpmnActivity, writeLine } from "./fixtures";
import { planBpmnBodyImport } from "../graph-authoring/bpmnImportPlan";
import { applyNativeBlockPlan, type NativeBlockContext } from "../graph-authoring/nativeBlocks";
import type { BpmnImportResult } from "../api/bpmnInterchange";

function example() {
  const owner = createActivityNode(bpmnActivity, "owner");
  owner.structure!.payload = { activities: [createActivityNode(writeLine, "old-child")], elements: [{ elementId: "old-task", elementType: "task", childNodeId: "old-child" }], sequenceFlows: [] };
  const context: NativeBlockContext = { document: { rootActivity: owner, layout: [{ nodeId: "old-task", x: 0, y: 0 }], activityPresentation: [{ nodeId: "owner", displayName: "Shipping" }, { nodeId: "old-child", displayName: "Old step" }] },
    scope: [], catalog: [bpmnActivity, writeLine], canEdit: true, referenceContext: [], sourceKey: "draft/1/rev/1" };
  const imported = createActivityNode(bpmnActivity, "imported-root");
  imported.structure!.payload = { activities: [createActivityNode(writeLine, "new-child")], elements: [{ elementId: "new-task", elementType: "task", childNodeId: "new-child", name: "Deliver" }], sequenceFlows: [], diagram: { shapes: { "new-task": { x: 10.25, y: 20.5 } }, edges: {} } };
  const result: BpmnImportResult = { processNode: imported, analysis: { processIds: ["process"], elementCounts: { task: 1 }, issues: [], capabilities: [{ elementId: "new-task", capability: "Retained", availability: "Available", reason: "Confirmed" }], hostInventoryVerified: true, canExecute: true } };
  return { context, result };
}
it("preserves the owning exact identity, invocation, contract, name and source while replacing one native body", () => {
  const { context, result } = example();
  context.document.rootActivity.inputs = [{ referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: false } }];
  context.document.rootActivity.outputs = [{ referenceKey: "Result", target: { referenceKey: "shipment" } }];
  const source = structuredClone(context), proposal = planBpmnBodyImport(context, result);
  expect(proposal.status).toBe("ready"); if (proposal.status !== "ready") return;
  expect(proposal.document.rootActivity).toEqual({ ...context.document.rootActivity, structure: result.processNode.structure });
  expect(proposal.document.layout).toEqual([{ nodeId: "new-task", x: 10.25, y: 20.5 }]);
  expect(proposal.document.activityPresentation).toEqual([{ nodeId: "owner", displayName: "Shipping" }, { nodeId: "new-child", displayName: "Deliver" }]);
  expect(context).toEqual(source);
  expect(applyNativeBlockPlan({ ...context, canEdit: false }, proposal).status).toBe("refused");
  expect(applyNativeBlockPlan({ ...context, sourceKey: "draft/1/rev/2" }, proposal).status).toBe("refused");
});
it.each(["degraded", "dropped"])("refuses %s conversion without changing the source", severity => {
  const { context, result } = example(); const original = structuredClone(context);
  result.analysis.issues = [{ severity, message: "Lost binding", elementId: "new-task" }];
  expect(planBpmnBodyImport(context, result)).toMatchObject({ status: "refused", diagnostics: [{ code: "lossy-bpmn-import" }] });
  expect(context).toEqual(original);
});
it.each(["host", "execution", "confirmation", "inventory"])("refuses an unverified %s", missing => {
  const { context, result } = example();
  if (missing === "host") result.analysis.hostInventoryVerified = false;
  if (missing === "execution") result.analysis.canExecute = false;
  if (missing === "confirmation") result.analysis.capabilities![0].availability = "RequiresBinding";
  if (missing === "inventory") result.analysis.capabilities = undefined;
  expect(planBpmnBodyImport(context, result)).toMatchObject({ status: "refused", diagnostics: [{ code: "unverified-bpmn-import" }] });
});
it("refuses hidden resource references to a replaced body, including opaque references", () => {
  for (const value of [{ producerNodeId: "old-child" }, { expressionType: "JavaScript", value: "activities['old-child'].output" }]) {
    const { context, result } = example(); context.referenceContext = [value];
    expect(planBpmnBodyImport(context, result)).toMatchObject({ status: "refused", diagnostics: [{ code: "referenced-import-body" }] });
  }
});
it("keeps literal scalar data from masquerading as a node reference", () => {
  const { context, result } = example(); context.referenceContext = [{ expressionType: "Literal", value: "old-child" }];
  expect(planBpmnBodyImport(context, result).status).toBe("ready");
});
it("refuses collisions with retained sidecars and the owning activity", () => {
  for (const id of ["owner", "orphan-position"]) {
    const { context, result } = example(); context.document.layout.push({ nodeId: "orphan-position", x: 1, y: 2 });
    (result.processNode.structure!.payload.elements as { elementId: string }[])[0].elementId = id;
    expect(planBpmnBodyImport(context, result).status).toBe("refused");
  }
});
it("refuses an exact child version absent from the authorized editor catalog", () => {
  const { context, result } = example(); context.catalog = [bpmnActivity];
  expect(planBpmnBodyImport(context, result)).toMatchObject({ status: "refused", diagnostics: [{ code: "unavailable-import-version" }] });
});
it("rejects missing structures and duplicate imported identities", () => {
  const { context, result } = example();
  result.processNode.structure = null;
  expect(planBpmnBodyImport(context, result).status).toBe("refused");
});
