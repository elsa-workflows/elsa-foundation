import { expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import type { BlockPromotionContext, ReadyBlockPromotion } from "../graph-authoring/blockPromotionTypes";
import { bindBlockPromotionVersion, planBlockPromotion } from "../graph-authoring/blockPromotion";
import { context, literal, argument, node, descriptor } from "./blockPromotionFixtures";
const children = (node: ActivityNode) => node.structure!.payload.activities as ActivityNode[];
const selected = (ctx: BlockPromotionContext) => children(ctx.document.rootActivity)[0];
const ready = (ctx: BlockPromotionContext): ReadyBlockPromotion => { const plan = planBlockPromotion(ctx, "block"); expect(plan.status).toBe("ready"); if (plan.status !== "ready") throw new Error(JSON.stringify(plan)); return plan; };
const root = (plan: ReadyBlockPromotion) => (plan.candidate.provider.payload as { rootActivity: ActivityNode }).rootActivity;
it("proposes explicit immutable inputs and exact advertised outcomes without changing source", () => {
  const ctx = context(), before = structuredClone(ctx), plan = ready(ctx);
  expect(ctx).toEqual(before); expect(plan.candidate.provider).toMatchObject({ providerKey: "elsa.activity-graph", schemaVersion: "2" });
  expect(plan.candidate.contract.inputs).toMatchObject([{ referenceKey: "input-1", displayName: "Greeting", type: { alias: "String", collectionKind: "Single" } }]);
  expect(plan.candidate.contract.outcomes).toEqual([{ referenceKey: "done-key", name: "Done", isEmitted: true }]);
  expect(children(root(plan))[0].inputs).toEqual([argument({ expressionType: "Input", value: { referenceKey: "input-1" } })]);
  expect(plan.callSite.inputs).toEqual([{ referenceKey: "input-1", value: { expressionType: "Input", value: { referenceKey: "greeting" } } }]);
});
it("replaces through the owning slots and keeps call-site layout and names", () => {
  const ctx = context(), result = bindBlockPromotionVersion(ctx, ready(ctx), "published-version");
  expect(result.status).toBe("ready"); if (result.status !== "ready") return;
  expect(children(result.document.rootActivity)[0]).toMatchObject({ nodeId: "block", activityVersionId: "published-version", structure: null });
  expect(children(result.document.rootActivity)[1].nodeId).toBe("after"); expect(result.document.layout).toEqual([{ nodeId: "block", x: 400, y: 200 }]);
  expect(result.document.activityPresentation).toEqual([{ nodeId: "block", displayName: "Prepare shipment" }]); expect(selected(ctx).activityVersionId).toBe("sequence");
});
it("keeps native local scopes and exposes a literal-initialized local with Set Output", () => {
  const ctx = context(), block = selected(ctx); block.structure!.payload.variables = [{ referenceKey: "local", name: "Shipment code", storageDriverType: null, type: { alias: "String", collectionKind: "Single" }, default: literal("") }];
  const assign: ActivityNode = { nodeId: "assign", activityVersionId: "set", inputs: [{ referenceKey: "value", value: { expressionType: "Input", value: { referenceKey: "greeting" } } }], outputs: [], intrinsic: { kind: "set", valueType: { alias: "String", collectionKind: "Single" }, variable: { referenceKey: "local", declaringScopeId: "block" } } }; block.structure!.payload.activities = [assign];
  const plan = ready(ctx), candidate = root(plan);
  expect(candidate.structure!.payload.variables).toEqual(block.structure!.payload.variables); expect(children(candidate)[0].intrinsic!.variable).toEqual({ referenceKey: "local", declaringScopeId: "block" });
  expect(children(candidate)[1].intrinsic!.kind).toBe("setoutput"); expect(children(candidate)[1].inputs).toContainEqual({ referenceKey: "value", value: { expressionType: "Variable", value: { referenceKey: "local", declaringScopeId: "block" } } });
  expect(plan.candidate.contract.outputs).toMatchObject([{ displayName: "Shipment code", referenceKey: "output-1" }]); expect((plan.candidate.provider.payload as { variables: unknown[] }).variables).toEqual([]);
});
it("captures a child result and rewrites its following caller while retaining declared scope", () => {
  const ctx = context(); selected(ctx).structure!.payload.activities = [node("producer", "read")]; children(ctx.document.rootActivity)[1].inputs = [argument({ expressionType: "ActivityResult", value: { producerNodeId: "producer", projectionKey: "Result", producerScopeId: "scope:root" } })];
  const before = structuredClone(ctx), plan = ready(ctx); expect(children(root(plan)).map(child => child.activityVersionId)).toEqual(["read", "set-output"]);
  expect(children(root(plan))[1].inputs).toContainEqual({ referenceKey: "value", value: { expressionType: "ActivityResult", value: { producerNodeId: "producer", projectionKey: "Result", producerScopeId: "scope:root" } } });
  expect(children(plan.rewrittenSource.rootActivity)[1].inputs).toEqual([argument({ expressionType: "ActivityResult", value: { producerNodeId: "block", projectionKey: "output-1", producerScopeId: "scope:root" } })]); expect(ctx).toEqual(before);
});
it("refuses a result read moved before guaranteed producer completion", () => {
  const ctx = context(), block = selected(ctx); block.structure!.payload.activities = [node("inside", "write", { expressionType: "ActivityResult", value: { producerNodeId: "producer", projectionKey: "Result" } })]; ctx.document.rootActivity.structure!.payload.activities = [block, node("producer", "read")];
  expect(planBlockPromotion(ctx, "block")).toMatchObject({ status: "refused", diagnostics: [{ code: "unpreserved-result-timing" }] }); ctx.document.rootActivity.structure!.payload.activities = [node("producer", "read"), block]; expect(planBlockPromotion(ctx, "block").status).toBe("ready");
});
it("refuses external mutable reads and writes with located diagnostics", () => {
  const ctx = context(), block = selected(ctx); block.structure!.payload.activities = [node("inside", "write", { expressionType: "Variable", value: { referenceKey: "outer-value", declaringScopeId: "workflow" } })];
  expect(planBlockPromotion(ctx, "block")).toMatchObject({ status: "refused", diagnostics: [{ code: "unpreserved-read-timing", path: "nodes.inside.inputs[0]" }] }); const assign = node("assign", "set"); assign.intrinsic = { kind: "set", variable: { referenceKey: "outer-value", declaringScopeId: "outer" } }; block.structure!.payload.activities = [assign]; expect(planBlockPromotion(ctx, "block")).toMatchObject({ status: "refused", diagnostics: [{ code: "external-write", path: "nodes.assign.intrinsic.variable" }] });
});
it("retains unrelated literal resource data", () => { const ctx = context(); ctx.referenceContext = [{ value: literal({ note: "producer", variableKey: "text" }) }]; expect(ready(ctx).referenceContext).toEqual(ctx.referenceContext); });
it("rejects source/capability/proposal changes before replacing anything", () => {
  for (const change of ["source", "capabilities", "proposal"] as const) { const ctx = context(), plan = ready(ctx); if (change === "source") ctx.sourceKey = "revision:3"; if (change === "capabilities") ctx.capabilities.snapshotFingerprint = "caps:2"; if (change === "proposal") plan.blockNodeId = "after"; expect(bindBlockPromotionVersion(ctx, plan, "published-version")).toMatchObject({ status: "refused", diagnostics: [{ code: "stale-promotion" }] }); expect(selected(ctx).activityVersionId).toBe("sequence"); }
});

it("retains BPMN element layout in the reusable body and removes it from the replaced source", () => {
  const ctx = context(), block = selected(ctx);
  ctx.catalog.push(descriptor("bpmn", "Elsa.Activities.Bpmn.Activities.BpmnProcess"));
  block.activityVersionId = "bpmn";
  block.structure = { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: {
    activities: children(block), elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "task", elementType: "task", childNodeId: "inside" }, { elementId: "end", elementType: "endEvent" }],
    sequenceFlows: [{ flowId: "in", sourceRef: "start", targetRef: "task" }, { flowId: "out", sourceRef: "task", targetRef: "end" }]
  } };
  ctx.document.layout.push({ nodeId: "task", x: 180, y: 80 });
  const plan = ready(ctx); expect(plan.candidate.layout).toContainEqual({ nodeId: "task", data: { x: 180, y: 80 } });
  const result = bindBlockPromotionVersion(ctx, plan, "published-version"); expect(result.status).toBe("ready");
  if (result.status === "ready") expect(result.document.layout.map(item => item.nodeId)).toEqual(["block"]);
});
it("refuses Complete and a Break whose original loop is outside the block", () => {
  for (const [version, key, code] of [["complete", "Elsa.Activities.Graph.Runtime.Activities.Complete", "changed-complete-target"], ["break", "Elsa.Activities.Primitives.Activities.Break", "changed-break-target"]]) {
    const ctx = context(); ctx.catalog.push(descriptor(version, key)); selected(ctx).structure!.payload.activities = [node("control", version)];
    expect(planBlockPromotion(ctx, "block")).toMatchObject({ status: "refused", diagnostics: [{ code, path: "nodes.control" }] });
  }
});
