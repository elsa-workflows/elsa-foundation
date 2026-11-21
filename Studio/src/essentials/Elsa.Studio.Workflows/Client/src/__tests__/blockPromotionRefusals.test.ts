import { expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import type { BlockPromotionContext } from "../graph-authoring/blockPromotionTypes";
import { bindBlockPromotionVersion, planBlockPromotion } from "../graph-authoring/blockPromotion";
import { argument, context, descriptor, node } from "./blockPromotionFixtures";

const children = (owner: ActivityNode) => owner.structure!.payload.activities as ActivityNode[];
const block = (ctx: BlockPromotionContext) => children(ctx.document.rootActivity)[0];
const resultRead = (producerNodeId: string, projectionKey = "Result") => ({ expressionType: "ActivityResult", value: { producerNodeId, projectionKey } });

// Each case changes one fact of an otherwise promotable block; the refusal is located and leaves the source alone.
const cases: [string, (ctx: BlockPromotionContext) => string | void][] = [
  ["read-only", ctx => { ctx.canEdit = false; }],
  ["missing-resource-kind", ctx => { (ctx as { resourceKind: string }).resourceKind = "workflow-template"; }],
  ["graph-authoring-unavailable", ctx => { ctx.capabilities.providers = []; }],
  ["missing-block", () => "not-a-block"],
  ["invalid-name", ctx => { ctx.document.activityPresentation = ctx.document.activityPresentation.filter(item => item.nodeId !== "block"); }],
  ["boundary-output-capture", ctx => { block(ctx).outputs = [{ referenceKey: "Result", target: { kind: "Variable", referenceKey: "captured" } }]; }],
  ["unavailable-activity", ctx => { ctx.catalog = ctx.catalog.map(item => item.activityVersionId === "write" ? { ...item, available: false } : item); }],
  ["unsupported-activity-effects", ctx => { ctx.catalog.push(descriptor("custom", "Acme.Custom")); children(block(ctx))[0].activityVersionId = "custom"; }],
  ["unpreserved-input-availability", ctx => { ctx.publicInputs = ctx.publicInputs.map(input => ({ ...input, isRequired: false })); }],
  ["unsupported-contract-type", ctx => { ctx.capabilities.types = ctx.capabilities.types.filter(type => type.alias !== "String"); }],
  ["changed-container-result", ctx => { children(ctx.document.rootActivity)[1].inputs = [argument(resultRead("block"))]; }],
  ["escaping-local-scope", ctx => { children(ctx.document.rootActivity)[1].inputs = [argument({ expressionType: "Variable", value: { referenceKey: "local", declaringScopeId: "block" } })]; }],
  ["unproven-caller-timing", ctx => {
    // A reader placed before the block cannot see a result the promoted call produces later.
    children(block(ctx)).push(node("producer", "read"));
    children(ctx.document.rootActivity).unshift(node("early", "write", resultRead("producer")));
  }],
  ["changed-write-target", ctx => {
    children(block(ctx))[0] = { nodeId: "rename", activityVersionId: "set", inputs: [argument({ expressionType: "Literal", value: "x" })], outputs: [],
      intrinsic: { kind: "setname", valueType: { alias: "String", collectionKind: "Single" } } } as ActivityNode;
  }],
  ["unknown-outcomes", ctx => { ctx.catalog = ctx.catalog.map(item => item.activityVersionId === "sequence" ? { ...item, ports: [] } : item); }],
  ["unknown-projection", ctx => {
    // The block reads an outside producer's result through a projection that producer does not advertise.
    const promoted = block(ctx);
    children(ctx.document.rootActivity).unshift(node("producer", "read"));
    children(promoted)[0].inputs = [argument(resultRead("producer", "NotAdvertised"))];
  }],
  ["unpreserved-result-timing", ctx => {
    // The producer runs after the block, so its result does not exist when the call snapshots its inputs.
    children(ctx.document.rootActivity).push(node("producer", "read"));
    children(block(ctx))[0].inputs = [argument(resultRead("producer"))];
  }],
  ["unknown-producer", ctx => { children(block(ctx))[0].inputs = [argument(resultRead("missing-producer"))]; }]
];

it.each(cases)("refuses %s before proposing anything", (code, change) => {
  const ctx = context();
  const blockNodeId = change(ctx) ?? "block";
  const before = structuredClone(ctx);
  const plan = planBlockPromotion(ctx, blockNodeId);
  expect(plan.status).toBe("refused");
  expect(plan.status === "refused" && plan.diagnostics.map(item => item.code)).toEqual([code]);
  expect(ctx).toEqual(before);
});

it("refuses to bind a version that is not one exact available catalog entry", () => {
  const ctx = context(), plan = planBlockPromotion(ctx, "block");
  if (plan.status !== "ready") throw new Error(JSON.stringify(plan));
  const result = bindBlockPromotionVersion(ctx, plan, "missing-version");
  expect(result.status === "refused" && result.diagnostics.map(item => item.code)).toEqual(["unavailable-target"]);
});
