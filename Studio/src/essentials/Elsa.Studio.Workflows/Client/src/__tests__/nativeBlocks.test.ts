import { describe, expect, it } from "vitest";
import { createActivityNode, flowchartStructureKind, sequenceStructureKind } from "../workflowAdapter";
import { applyNativeBlockPlan, planCreateNativeBlock, planExtractNativeBlock, planUngroupNativeBlock, type NativeBlockContext, type NativeBlockPlan } from "../graph-authoring/nativeBlocks";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { flowchartActivity, sequenceActivity, writeLine, whileActivity, whileNode } from "./fixtures";
import { compensationScopeStructureKind } from "../compensationScope";
import { readFileSync } from "node:fs";

const action: ActivityCatalogItem = { ...writeLine, ports: [{ name: "Done", isBrowsable: true }] };
const outcomes = (item: ActivityCatalogItem, names: string[]) => ({ ...item, ports: names.map(name => ({ name })), designFacets: [...item.designFacets ?? [], { kind: "elsa.outcomes", schemaVersion: "1", payload: { ports: names.map(name => ({ name })) } }] });
const catalog = [action, outcomes(sequenceActivity, ["Done", "Break"]), outcomes(flowchartActivity, ["Done", "Break"]), whileActivity];
const node = (id: string): ActivityNode => ({ nodeId: id, activityVersionId: action.activityVersionId, inputs: [], outputs: [] });
const edge = (from: string, to: string, id = `${from}-${to}`, sourcePort?: string) => ({ id, source: { nodeId: from, ...(sourcePort === undefined ? {} : { port: sourcePort }) }, target: { nodeId: to }, vertices: [{ x: 180, y: 130, bend: "keep" }], custom: "keep" });
function container(kind: "sequence" | "flowchart", children: ActivityNode[], extra = {}) {
  const result = createActivityNode(kind === "sequence" ? sequenceActivity : flowchartActivity, "root");
  result.structure!.payload = { ...result.structure!.payload, activities: children, ...extra };
  return result;
}
function context(rootActivity = container("sequence", [node("a"), node("b"), node("c"), node("d")])): NativeBlockContext {
  return { document: { rootActivity, layout: [{ nodeId: "a", x: 400, y: 350 }, { nodeId: "b", x: 100, y: 80, additionalProperties: { preserve: true } }, { nodeId: "c", x: 300, y: 180 }], activityPresentation: [{ nodeId: "b", displayName: "Original", description: "Unchanged", extra: "keep" } as never] }, catalog: structuredClone(catalog), scope: [], canEdit: true, referenceContext: [], sourceKey: "workflow:1/revision:3" };
}
const request = { nodeId: "new-block", activityVersionId: sequenceActivity.activityVersionId, displayName: "  Review order  ", description: " Details " };
function accepted(plan: NativeBlockPlan) {
  expect(plan.status, JSON.stringify(plan)).toBe("ready");
  if (plan.status !== "ready") throw new Error(JSON.stringify(plan));
  return plan;
}
function refused(plan: NativeBlockPlan, code: string) {
  expect(plan).toMatchObject({ status: "refused", diagnostics: [{ code, path: expect.any(String), message: expect.any(String) }] });
}
const extract = (ctx: NativeBlockContext, ids = ["c", "b"]) => planExtractNativeBlock(ctx, { ...request, selectedNodeIds: ids, activityVersionId: ctx.document.rootActivity.structure?.kind === flowchartStructureKind ? flowchartActivity.activityVersionId : sequenceActivity.activityVersionId });
function flow() { return context(container("flowchart", [node("a"), node("b"), node("c"), node("d")], { connections: [edge("a", "b"), edge("b", "c"), edge("c", "d")], startNodeId: "a" })); }
const payload = (ctx: NativeBlockContext) => ctx.document.rootActivity.structure!.payload;
const planPayload = (plan: ReturnType<typeof accepted>) => plan.document.rootActivity.structure!.payload;

function compensationContext(kind: "sequence" | "flowchart", withHandler = true, editHandler = false) {
  const body = container(kind, [node("a"), node("b"), node("c"), node("d")], kind === "flowchart" ? {
    connections: [edge("a", "b"), edge("b", "c"), edge("c", "d")], startNodeId: "a"
  } : {});
  body.nodeId = "body";
  const scope: ActivityNode = {
    ...node("scope"), activityVersionId: "compensation-v1",
    structure: { kind: compensationScopeStructureKind, schemaVersion: "1.0.0", payload: { steps: [
      { name: "Attempt", activity: editHandler ? node("forward") : body,
        ...(withHandler ? { compensation: editHandler ? body : node("handler") } : {}), future: { keep: "row" } },
      { name: "Later", activity: node("later"), future: { keep: "later" } }
    ] } }
  };
  const ctx = context(container("sequence", [scope, node("outside")]));
  ctx.scope = [
    { ownerNodeId: "scope", slotId: `${compensationScopeStructureKind}:steps:steps:${editHandler ? "compensation" : "activity"}:0`, label: "Attempt" },
    { ownerNodeId: "body", slotId: `${body.structure!.kind}:activities`, label: "Body" }
  ];
  return { ctx, scope, body, steps: scope.structure!.payload.steps as Record<string, unknown>[] };
}

describe("pure native block planning", () => {
  it("matches the exact planner documents consumed by real publication and runtime regressions", () => {
    const moduleUrl = import.meta.url;
    const fixture = JSON.parse(readFileSync(new URL("../../../../../../../specs/1204-named-embedded-blocks/contracts/native-result-transfer-cases.json", moduleUrl), "utf8")) as {
      catalog: NativeBlockContext["catalog"];
      cases: { context: Omit<NativeBlockContext, "catalog">; request: Parameters<typeof planExtractNativeBlock>[1]; grouped: unknown; ungrouped: unknown }[];
    };
    expect(fixture.cases).toHaveLength(6);
    for (const test of fixture.cases) {
      const context = { ...test.context, catalog: fixture.catalog };
      const plan = accepted(planExtractNativeBlock(context, test.request));
      expect(plan.document).toEqual(test.grouped);
      expect(accepted(planUngroupNativeBlock({ ...context, document: plan.document }, "block")).document).toEqual(test.ungrouped);
    }
  });

  it.each(["sequence", "flowchart"] as const)("transfers an outside ordinary result read explicitly through the enclosing %s frame", kind => {
    const ctx = kind === "sequence" ? context() : flow();
    ctx.catalog.push({ ...action, activityVersionId: "elsa.intrinsic.set@1", intrinsic: { kind: "Set", valueInputKey: "value" } });
    const children = payload(ctx).activities as ActivityNode[];
    children[1].outputs = [{ referenceKey: "existing", value: { expressionType: "Variable", value: { referenceKey: "retained" } } }];
    children[3].inputs = [{ referenceKey: "text", future: "retain", value: { expressionType: "ActivityResult", value: { producerNodeId: "b", projectionKey: "$result", producerScopeId: "scope:root" } } }];
    payload(ctx).variables = [{ referenceKey: "new-block-result-1", name: "Existing", type: { alias: "Int32" } }];
    const before = structuredClone(ctx);
    const plan = accepted(extract(ctx));
    expect(ctx).toEqual(before);
    const root = plan.document.rootActivity.structure!.payload;
    const body = (root.activities as ActivityNode[])[1].structure!.payload;
    const capture = (body.activities as ActivityNode[])[1];
    expect((body.activities as ActivityNode[]).map(node => node.nodeId)).toEqual(["b", capture.nodeId, "c"]);
    expect(capture.intrinsic).toMatchObject({ kind: "set", variable: { referenceKey: "new-block-result-2", declaringScopeId: "root" } });
    expect((body.activities as ActivityNode[])[0].outputs).toEqual(children[1].outputs);
    expect((root.activities as ActivityNode[])[2].inputs).toEqual([{ referenceKey: "text", future: "retain", value: { expressionType: "Variable", value: { referenceKey: "new-block-result-2", declaringScopeId: "root" } } }]);
    expect((root.variables as unknown[])[0]).toEqual((payload(ctx).variables as unknown[])[0]);
    const inverse = accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"));
    expect(inverse.document.rootActivity.structure!.payload.variables).toEqual(root.variables);
    expect((inverse.document.rootActivity.structure!.payload.activities as ActivityNode[]).map(node => node.nodeId)).toEqual(["a", "b", capture.nodeId, "c", "d"]);
    if (kind === "flowchart") expect(body.connections).toEqual([
      { ...edge("b", "c"), source: { nodeId: capture.nodeId }, vertices: [{ x: 80, y: 50, bend: "keep" }] },
      { id: capture.nodeId, source: { nodeId: "b", port: "Done" }, target: { nodeId: capture.nodeId } }
    ]);
  });

  it("rewrites explicit expression parameters without editing expression source or literal payloads", () => {
    const ctx = context();
    ctx.catalog.push({ ...action, activityVersionId: "elsa.intrinsic.set@1", intrinsic: { kind: "Set", valueInputKey: "value" } });
    const children = payload(ctx).activities as ActivityNode[];
    children[3].inputs = [{ referenceKey: "text", value: { expressionType: "JavaScript", value: {
      language: "JavaScript", source: "prior + 1", resultType: { alias: "Int32", collectionKind: "single" }, options: {}, capabilityProfile: "binding-pure-v1", parameters: {
        prior: { kind: "ActivityResult", producerNodeId: "b", projectionKey: "$result" },
        literal: { kind: "Literal", value: { producerNodeId: "b", projectionKey: "$result" } }
      }
    } } }];
    const plan = accepted(extract(ctx));
    const input = ((planPayload(plan).activities as ActivityNode[])[2].inputs[0] as { value: { value: { source: string; parameters: unknown } } }).value.value;
    expect(input.source).toBe("prior + 1");
    expect(input.parameters).toEqual({ prior: { kind: "Variable", declaringScopeNodeId: "root", variableKey: "new-block-result-1" }, literal: { kind: "Literal", value: { producerNodeId: "b", projectionKey: "$result" } } });
    accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"));
  });

  it.each(["missing-set", "optional", "nested-producer", "resource"])("refuses an unproved ordinary result transfer before mutation: %s", fault => {
    const ctx = context();
    if (fault !== "missing-set") ctx.catalog.push({ ...action, activityVersionId: "elsa.intrinsic.set@1", intrinsic: { kind: "Set", valueInputKey: "value" } });
    const binding = { expressionType: "ActivityResult", value: { producerNodeId: fault === "nested-producer" ? "inner" : "b", projectionKey: "$result", ...(fault === "optional" ? { isOptional: true } : {}) } };
    const children = payload(ctx).activities as ActivityNode[];
    if (fault === "nested-producer") { children[1] = container("sequence", [node("inner")]); children[1].nodeId = "b"; }
    if (fault === "resource") ctx.referenceContext = [{ outputMappings: [{ source: binding }] }];
    else children[3].inputs = [{ referenceKey: "text", value: binding }];
    const before = structuredClone(ctx);
    refused(extract(ctx), fault === "missing-set" ? "unavailable-result-transfer" : fault === "resource" ? "resource-result-crossing" : "unproven-result-transfer");
    expect(ctx).toEqual(before);
  });

  it("reserves existing connection and orphan metadata identities through extraction and Ungroup", () => {
    const ctx = flow();
    ctx.catalog.push({ ...action, activityVersionId: "elsa.intrinsic.set@1", intrinsic: { kind: "Set", valueInputKey: "value" } });
    const edges = payload(ctx).connections as ReturnType<typeof edge>[];
    edges[1].id = "new-block-capture-1";
    payload(ctx).nodeMetadata = { "new-block-capture-2": { policyKind: "missing-policy", configuration: {} } };
    payload(ctx).connectionMetadata = { "new-block-capture-3": { retain: true } };
    (payload(ctx).activities as ActivityNode[])[3].inputs = [{ referenceKey: "text", value: { expressionType: "ActivityResult", value: { producerNodeId: "b", projectionKey: "$result" } } }];
    const plan = accepted(extract(ctx));
    const body = (planPayload(plan).activities as ActivityNode[])[1].structure!.payload;
    expect((body.activities as ActivityNode[])[1].nodeId).toBe("new-block-capture-4");
    expect((body.connections as ReturnType<typeof edge>[]).map(connection => connection.id)).toEqual(["new-block-capture-1", "new-block-capture-4"]);
    const inverse = accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"));
    const restored = inverse.document.rootActivity.structure!.payload;
    expect(restored.nodeMetadata).toEqual(payload(ctx).nodeMetadata);
    expect(restored.connectionMetadata).toEqual(payload(ctx).connectionMetadata);
    expect((restored.activities as ActivityNode[]).map(node => node.nodeId)).not.toContain("new-block-capture-2");
  });

  it.each([null, "", "   ", " a "])("uses the canonical Flowchart entry with non-topological array order: %j", start => {
    const ctx = flow();
    ctx.catalog.push({ ...action, activityVersionId: "elsa.intrinsic.set@1", intrinsic: { kind: "Set", valueInputKey: "value" } });
    const children = payload(ctx).activities as ActivityNode[];
    children[3].inputs = [{ referenceKey: "text", value: { expressionType: "ActivityResult", value: { producerNodeId: "b", projectionKey: "$result" } } }];
    payload(ctx).activities = [children[3], children[1], children[0], children[2]];
    payload(ctx).startNodeId = start;
    const plan = accepted(extract(ctx));
    expect(planPayload(plan).startNodeId).toBe("a");
    accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"));
  });

  it.each(["Xml", "None", "Explicit", "Auto"])("refuses an unproved authored conversion instead of losing it: %s", mode => {
    const ctx = context();
    ctx.catalog.push({ ...action, activityVersionId: "elsa.intrinsic.set@1", intrinsic: { kind: "Set", valueInputKey: "value" } });
    (payload(ctx).activities as ActivityNode[])[3].inputs = [{ referenceKey: "text", conversion: { mode }, value: { expressionType: "ActivityResult", value: { producerNodeId: "b", projectionKey: "$result" } } }];
    const before = structuredClone(ctx);
    refused(extract(ctx), "unproven-result-conversion");
    expect(ctx).toEqual(before);
  });

  it.each(["sequence", "flowchart"] as const)("refuses an outside member reader before extracting its %s producer", kind => {
    const ctx = kind === "sequence" ? context() : flow();
    const consumer = (payload(ctx).activities as ActivityNode[])[3];
    consumer.inputs = [{ referenceKey: "CustomerId", value: { expressionType: "ActivityResultMember", value: { producerNodeId: "b", projectionKey: "$result", memberPath: ["customerId"] } } }];
    const before = structuredClone(ctx);
    const plan = extract(ctx);
    refused(plan, "member-result-scope-crossing");
    expect(plan).toMatchObject({ diagnostics: [{ path: "nodes.d.inputs[0].value" }] });
    expect(applyNativeBlockPlan(ctx, plan)).toEqual(plan);
    expect(ctx).toEqual(before);
  });

  it("keeps member readers with their producer and retains reads from an outside producer", () => {
    const ctx = context();
    const children = payload(ctx).activities as ActivityNode[];
    const member = (producerNodeId: string) => ({ referenceKey: "CustomerId", value: { expressionType: "ActivityResultMember", value: { producerNodeId, projectionKey: "$result", memberPath: ["customerId"] } } });
    children[1].inputs = [member("a")];
    children[2].inputs = [member("b")];
    const plan = accepted(extract(ctx));
    expect((planPayload(plan).activities as ActivityNode[])[1].structure!.payload.activities).toEqual(children.slice(1, 3));
    expect(accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block")).document.rootActivity).toEqual(ctx.document.rootActivity);
  });

  it("checks descendant producers and resource-owned member readers without treating literal objects as bindings", () => {
    const nested = container("sequence", [node("producer")]); nested.nodeId = "b";
    const ctx = context(container("sequence", [node("a"), nested, node("c")]));
    const binding = { expressionType: "activityresultmember", value: { producerNodeId: "producer", projectionKey: "$result", memberPath: ["customerId"] } };
    ctx.referenceContext = [{ outputMappings: [{ source: binding }] }];
    const before = structuredClone(ctx);
    refused(extract(ctx), "member-result-scope-crossing");
    expect(ctx).toEqual(before);
    ctx.referenceContext = [{ value: { expressionType: "Literal", value: binding } }];
    accepted(extract(ctx));
  });

  it("extracts authored Sequence order and preserves every child value and unrelated sidecar", () => {
    const ctx = context();
    const b = (payload(ctx).activities as ActivityNode[])[1];
    b.inputs = [{ referenceKey: "value", value: { expressionType: "Variable", value: { declaringScopeId: "root", referenceKey: "total" } } }];
    b.outputs = [{ referenceKey: "result", target: { nodeId: "external", key: "result" } }];
    b.executionSettings = { retry: 2 };
    payload(ctx).variables = [{ referenceKey: "total", initialValue: 0 }];
    const before = structuredClone(ctx);
    const plan = accepted(extract(ctx));
    const children = planPayload(plan).activities as ActivityNode[];
    expect(children.map(x => x.nodeId)).toEqual(["a", "new-block", "d"]);
    expect(children[1].structure!.payload.activities).toEqual((payload(ctx).activities as ActivityNode[]).slice(1, 3));
    expect(planPayload(plan).variables).toEqual(payload(ctx).variables);
    expect(children[1]).toMatchObject({ activityVersionId: sequenceActivity.activityVersionId, inputs: [], outputs: [], structure: { kind: sequenceStructureKind } });
    expect(plan.document.activityPresentation).toEqual([...ctx.document.activityPresentation, { nodeId: "new-block", displayName: "Review order", description: "Details" }]);
    expect(plan.document.layout).toEqual([{ nodeId: "a", x: 400, y: 350 }, { nodeId: "b", x: 0, y: 0, additionalProperties: { preserve: true } }, { nodeId: "c", x: 200, y: 100 }, { nodeId: "new-block", x: 100, y: 80 }]);
    expect(ctx).toEqual(before);
    expect(plan.selection).toEqual(["new-block"]);
  });

  it("translates only immediate saved coordinates and uses the current wrapper position on Ungroup", () => {
    const nested = container("sequence", [node("descendant")]); nested.nodeId = "b";
    const ctx = context(container("sequence", [node("a"), nested, node("c")]));
    ctx.document.layout.push({ nodeId: "descendant", x: 20, y: 30 });
    const plan = accepted(extract(ctx));
    expect(plan.document.layout.find(x => x.nodeId === "descendant")).toEqual({ nodeId: "descendant", x: 20, y: 30 });
    plan.document.layout.find(x => x.nodeId === "new-block")!.x = 450;
    const ungroup = accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"));
    expect(ungroup.document.layout.find(x => x.nodeId === "b")).toMatchObject({ x: 450, y: 80 });
    expect(ungroup.document.layout.find(x => x.nodeId === "descendant")).toEqual({ nodeId: "descendant", x: 20, y: 30 });
    expect(ungroup.document.rootActivity).toEqual(ctx.document.rootActivity);
    expect(ungroup.document.activityPresentation).toEqual(ctx.document.activityPresentation);
  });

  it("does not invent missing child coordinates", () => {
    const ctx = context(); ctx.document.layout = [];
    const plan = accepted(extract(ctx));
    expect(plan.document.layout).toEqual([{ nodeId: "new-block", x: 0, y: 0 }]);
    expect(accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block")).document.layout).toEqual([]);
  });

  it.each([["a", "c"], [], ["b", "b"], ["gone"]])("refuses an invalid/noncontiguous Sequence selection %j without mutation", (...ids) => {
    const ctx = context(); const before = structuredClone(ctx);
    refused(extract(ctx, ids), ids.join() === "a,c" ? "noncontiguous-sequence" : "stale-selection");
    expect(ctx).toEqual(before);
  });

  it("preserves Break and Complete nodes and enclosing loop identity without introducing a reusable boundary", () => {
    const sequence = container("sequence", [{ ...node("break"), activityVersionId: "break-v1" }, { ...node("complete"), activityVersionId: "complete-v1" }]);
    sequence.nodeId = "body";
    const ctx = context(whileNode("loop", sequence));
    ctx.scope = [{ ownerNodeId: "body", slotId: `${sequenceStructureKind}:activities`, label: "Body" }];
    const plan = accepted(planExtractNativeBlock(ctx, { ...request, selectedNodeIds: ["complete", "break"] }));
    expect(plan.document.rootActivity.nodeId).toBe("loop");
    expect(JSON.stringify(plan.document.rootActivity)).toContain('"complete-v1"');
    expect(JSON.stringify(plan.document.rootActivity)).not.toContain("elsa.graph-activity");
    expect(accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block")).document.rootActivity).toEqual(ctx.document.rootActivity);
  });

  it("creates a named empty native block of an explicitly chosen available kind", () => {
    const ctx = context();
    const plan = accepted(planCreateNativeBlock(ctx, { ...request, kind: "flowchart", activityVersionId: flowchartActivity.activityVersionId, beforeNodeId: "b", position: { x: 10, y: 20 } }));
    expect((planPayload(plan).activities as ActivityNode[]).map(x => x.nodeId)).toEqual(["a", "new-block", "b", "c", "d"]);
    expect((planPayload(plan).activities as ActivityNode[])[1].structure).toMatchObject({ kind: flowchartStructureKind, payload: { activities: [], connections: [], startNodeId: null } });
  });

  it("preserves an implicit Flowchart start when adding a disconnected block before it", () => {
    const ctx = flow(); payload(ctx).startNodeId = null;
    const plan = accepted(planCreateNativeBlock(ctx, { ...request, kind: "sequence", beforeNodeId: "a", position: { x: 0, y: 0 } }));
    expect(planPayload(plan).startNodeId).toBe("a");
  });

  it.each(["", " ", "a".repeat(201)])("refuses an invalid name", displayName => refused(planCreateNativeBlock(context(), { ...request, displayName, kind: "sequence", position: { x: 0, y: 0 } }), "invalid-name"));
  it("refuses an overlong description instead of truncating it", () => refused(planExtractNativeBlock(context(), { ...request, description: "a".repeat(2001), selectedNodeIds: ["a"] }), "invalid-name"));
  it("refuses a globally occupied node or orphan sidecar identity", () => {
    refused(planExtractNativeBlock(context(), { ...request, nodeId: "a", selectedNodeIds: ["b"] }), "occupied-identity");
    const ctx = context(); ctx.document.activityPresentation.push({ nodeId: "new-block", displayName: "orphan" });
    refused(extract(ctx), "occupied-identity");
  });

  it.each(["create", "extract"])("reserves orphan native policy identities during %s", operation => {
    const ctx = flow();
    payload(ctx).nodeMetadata = { "new-block": { policyKind: "missing-policy", configuration: {} } };
    const plan = operation === "extract" ? extract(ctx) : planCreateNativeBlock(ctx, { ...request, kind: "sequence", position: { x: 0, y: 0 } });
    refused(plan, "occupied-identity");
  });

  it("preserves an unrelated ancestor's implicit start through a nested edit", () => {
    const body = container("sequence", [node("inside")]); body.nodeId = "first";
    const ctx = context(container("flowchart", [node("later"), body], { startNodeId: null, connections: [edge("first", "later")] }));
    ctx.scope = [{ ownerNodeId: "first", slotId: `${sequenceStructureKind}:activities`, label: "First" }];
    const plan = accepted(planExtractNativeBlock(ctx, { ...request, selectedNodeIds: ["inside"] }));
    expect(planPayload(plan).startNodeId).toBeNull();
    const inverse = accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"));
    expect(inverse.document.rootActivity).toEqual(ctx.document.rootActivity);
  });

  it.each(["unavailable", "wrong-type", "duplicate-version", "unsupported-schema"]) ("requires an exact authorized native catalog version: %s", fault => {
    const ctx = context(); const native = ctx.catalog[1];
    if (fault === "unavailable") native.available = false;
    if (fault === "wrong-type") native.activityTypeKey = "My.Custom.Sequence";
    if (fault === "duplicate-version") ctx.catalog.push(structuredClone(native));
    if (fault === "unsupported-schema") (native.designFacets![0] as { schemaVersion: string }).schemaVersion = "2";
    refused(extract(ctx), fault === "unsupported-schema" ? "unsupported-composite" : "unavailable-composite");
  });

  it("refuses stale scope and read-only sources before a mutation", () => {
    const ctx = context(); ctx.scope = [{ ownerNodeId: "deleted", slotId: "gone", label: "Gone" }];
    refused(extract(ctx), "unsupported-scope");
    refused(extract({ ...context(), canEdit: false }), "read-only");
  });

  it.each(["source", "catalog", "authority", "resource", "scope", "preview"]) ("fences %s changes between review and apply", change => {
    const ctx = context(); const plan = accepted(extract(ctx));
    if (change === "source") ctx.document.activityPresentation.push({ nodeId: "a", displayName: "Changed" });
    if (change === "catalog") ctx.catalog[1].available = false;
    if (change === "authority") ctx.canEdit = false;
    if (change === "resource") ctx.sourceKey = "workflow:2/revision:3";
    if (change === "scope") ctx.scope.push({ kind: "root", ownerNodeId: "root", slotId: "other", label: "Other" });
    if (change === "preview") plan.document.layout = [];
    refused(applyNativeBlockPlan(ctx, plan), change === "preview" ? "changed-preview" : "stale-source");
  });

  it("detaches successful apply and original proposal from subsequent caller mutations", () => {
    const ctx = context(); const plan = accepted(extract(ctx));
    const applied = accepted(applyNativeBlockPlan(ctx, plan));
    applied.document.layout[0].x = 9;
    expect(plan.document.layout[0].x).toBe(400);
    (payload(ctx).activities as ActivityNode[])[1].inputs.push("later");
    expect(((planPayload(plan).activities as ActivityNode[])[1].structure!.payload.activities as ActivityNode[])[0].inputs).toEqual([]);
  });

  it.each(["bad-child", "duplicate-id", "duplicate-layout", "invalid-position"]) ("refuses malformed input: %s", fault => {
    const ctx = context();
    if (fault === "bad-child") (payload(ctx).activities as unknown[]).push({ bad: true });
    if (fault === "duplicate-id") (payload(ctx).activities as ActivityNode[]).push(node("a"));
    if (fault === "duplicate-layout") ctx.document.layout.push({ nodeId: "a", x: 0, y: 0 });
    if (fault === "invalid-position") ctx.document.layout[0].x = Number.NaN;
    refused(extract(ctx), { "bad-child": "malformed-children", "duplicate-id": "duplicate-node", "duplicate-layout": "duplicate-sidecar", "invalid-position": "invalid-position" }[fault]!);
  });
});

describe("linear Flowchart transformation", () => {
  it("preserves connection identities, endpoint fields, metadata and internal geometry through extraction and Ungroup", () => {
    const ctx = flow();
    const connections = payload(ctx).connections as ReturnType<typeof edge>[];
    Object.assign(connections[0].target, { extra: "inbound" });
    payload(ctx).connectionMetadata = { "a-b": { label: "Entry" }, "b-c": { label: "Internal", configuration: {}, isDefault: false }, "c-d": { label: "Exit" } };
    payload(ctx).nodeMetadata = { b: { policyKind: null, configuration: {} } };
    const before = structuredClone(ctx);
    const plan = accepted(extract(ctx));
    const outer = planPayload(plan);
    const block = (outer.activities as ActivityNode[])[1];
    expect(outer.connections).toEqual([{ ...connections[0], target: { ...connections[0].target, nodeId: "new-block" } }, { ...connections[2], source: { ...connections[2].source, nodeId: "new-block" } }]);
    expect(outer.connectionMetadata).toEqual({ "a-b": { label: "Entry" }, "c-d": { label: "Exit" } });
    expect(block.structure!.payload).toMatchObject({ startNodeId: "b", nodeMetadata: { b: { policyKind: null, configuration: {} } }, connections: [{ ...connections[1], vertices: [{ x: 80, y: 50, bend: "keep" }] }] });
    const ungroup = accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"));
    const restored = ungroup.document.rootActivity.structure!.payload;
    expect(restored.activities).toEqual(payload(ctx).activities);
    expect(restored.connectionMetadata).toEqual(payload(ctx).connectionMetadata);
    expect(restored.nodeMetadata).toEqual(payload(ctx).nodeMetadata);
    expect([...(restored.connections as ReturnType<typeof edge>[])].sort((a, b) => a.id.localeCompare(b.id))).toEqual([...connections].sort((a, b) => a.id.localeCompare(b.id)));
    expect(ungroup.document.layout).toEqual(ctx.document.layout);
    expect(ctx).toEqual(before);
  });

  it("pins implicit boundary connection identities so native metadata remains attached after redirection", () => {
    const ctx = flow(); const connections = payload(ctx).connections as Record<string, unknown>[];
    delete connections[0].id;
    payload(ctx).connectionMetadata = { "a:Done->b": { label: "Preserve" } };
    const plan = accepted(extract(ctx));
    expect((planPayload(plan).connections as { id: string }[])[0].id).toBe("a:Done->b");
    expect(planPayload(plan).connectionMetadata).toEqual(payload(ctx).connectionMetadata);
  });

  it("redirects a selected start using graph order even when array order differs", () => {
    const ctx = flow(); const activities = payload(ctx).activities as ActivityNode[];
    payload(ctx).activities = [activities[2], activities[1], activities[3], activities[0]];
    payload(ctx).startNodeId = null;
    const plan = accepted(extract(ctx, ["b", "a"]));
    expect(planPayload(plan).startNodeId).toBe("new-block");
    expect((((planPayload(plan).activities as ActivityNode[]).find(x => x.nodeId === "new-block"))!.structure!.payload.activities as ActivityNode[]).map(x => x.nodeId)).toEqual(["a", "b"]);
  });

  it("refuses to pin an opaque identity that native start normalization would trim", () => {
    const ctx = flow();
    (payload(ctx).activities as ActivityNode[])[0].nodeId = " a ";
    (payload(ctx).connections as ReturnType<typeof edge>[])[0].source.nodeId = " a ";
    payload(ctx).startNodeId = null;
    refused(extract(ctx), "unsafe-start-identity");
  });

  it.each(["conditional", "hidden-outcome", "dynamic", "missing-metadata", "policy", "join", "alternate-trigger", "middle-start", "cycle", "multiple-entry", "bad-endpoint", "bad-vertices"])("refuses unsafe Flowchart %s before any mutation", fault => {
    const ctx = flow(); const connections = payload(ctx).connections as ReturnType<typeof edge>[];
    if (fault === "conditional") connections[1].source.port = "True";
    if (fault === "hidden-outcome") ctx.catalog[0].ports!.push({ name: "False", isBrowsable: false });
    if (fault === "dynamic") ctx.catalog[0].designFacets!.push({ kind: "elsa.outcomes", payload: { ports: [{ name: "Done" }], dynamicOutcomes: { kind: "valuePerItem", source: "Actions" } } });
    if (fault === "missing-metadata") ctx.catalog[0].ports = [];
    if (fault === "policy") payload(ctx).connectionMetadata = { "b-c": { policyKind: "FirstWins", configuration: {} } };
    if (fault === "join") connections.push(edge("a", "d"));
    if (fault === "alternate-trigger") ctx.catalog[0].executionType = "Trigger";
    if (fault === "middle-start") payload(ctx).startNodeId = "c";
    if (fault === "cycle") connections.push(edge("c", "b"));
    if (fault === "multiple-entry") connections.push(edge("a", "c"));
    if (fault === "bad-endpoint") connections[0].target.nodeId = "gone";
    if (fault === "bad-vertices") connections[0].vertices[0].x = Infinity;
    const before = structuredClone(ctx);
    const plan = extract(ctx);
    expect(plan.status, fault).toBe("refused");
    expect(ctx).toEqual(before);
  });

  it("rejects the If(False) early-completion counterexample even with a single True edge", () => {
    const ctx = flow();
    const decision = outcomes({ ...action, activityVersionId: "if-v1" }, ["True", "False"]);
    ctx.catalog.push(decision);
    (payload(ctx).activities as ActivityNode[])[1].activityVersionId = decision.activityVersionId;
    (payload(ctx).connections as ReturnType<typeof edge>[])[1].source.port = "True";
    refused(extract(ctx), "unproven-outcomes");
  });

  it("supports a trigger entry only when it is also the unchanged static entry", () => {
    const ctx = flow();
    const trigger = { ...action, activityVersionId: "trigger-v1", executionType: "Trigger" };
    ctx.catalog.push(trigger);
    (payload(ctx).activities as ActivityNode[])[0].activityVersionId = trigger.activityVersionId;
    const plan = accepted(extract(ctx, ["b", "a"]));
    expect(planPayload(plan).startNodeId).toBe("new-block");
    expect(accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block")).document.rootActivity.structure!.payload.startNodeId).toBe("a");
  });

  it("retains native Break propagation rather than treating it as an ordinary unmatched exit", () => {
    const ctx = flow();
    ctx.catalog[0] = outcomes(action, ["Done", "Break"]);
    accepted(extract(ctx));
  });

  it("refuses malformed routing and policy data instead of losing it", () => {
    const ctx = flow();
    payload(ctx).nodeMetadata = { b: { configuration: { target: "c" } } };
    refused(extract(ctx), "policy-boundary");
    payload(ctx).nodeMetadata = [];
    refused(extract(ctx), "malformed-metadata");
  });
});

describe("no-loss Ungroup", () => {
  it.each(["canonical", "editor", "graph-mapping"].flatMap(form => ["Literal", "literal", "lItErAl"].map(syntax => [form, syntax])))("preserves literal data mentioning a wrapper identity in %s / %s form", (form, syntax) => {
    const ctx = context();
    const producer = (payload(ctx).activities as ActivityNode[])[1];
    const literal = form === "graph-mapping" ? "new-block" : "Keep new-block as ordinary text";
    if (form === "canonical") producer.inputs = [{ referenceKey: "text", value: { expressionType: syntax, value: literal } }];
    if (form === "editor") producer.text = { expression: { type: syntax, value: literal } };
    const plan = accepted(extract(ctx));
    const referenceContext = form === "graph-mapping" ? [{ outputMappings: [{ outputReferenceKey: "text", source: { syntax, value: literal } }] }] : [];
    const inverse = accepted(planUngroupNativeBlock({ ...ctx, document: plan.document, referenceContext }, "new-block"));
    expect(inverse.document.rootActivity).toEqual(ctx.document.rootActivity);
  });

  it("refuses a boundary result used by resource-owned mappings outside rootActivity", () => {
    const original = context(); const plan = accepted(extract(original));
    const ctx = { ...original, document: plan.document, referenceContext: [{ outputMappings: [{ outputReferenceKey: "result", source: { expressionType: "ActivityResult", value: { producerNodeId: "new-block", projectionKey: "$result", producerScopeId: "scope:root" } } }] }] };
    refused(planUngroupNativeBlock(ctx, "new-block"), "referenced-boundary");
  });

  it.each(["Literal", "Object"])("keeps structured reference fields conservative inside %s payloads", expressionType => {
    const original = context(); const plan = accepted(extract(original));
    const referenceContext = [{ value: { expressionType, value: { declaringScopeId: "new-block", referenceKey: "target" } } }];
    refused(planUngroupNativeBlock({ ...original, document: plan.document, referenceContext }, "new-block"), "referenced-boundary");
  });

  it("fences changes to resource-owned bindings even when the resource revision has not changed", () => {
    const ctx = { ...context(), referenceContext: [] as unknown[] };
    const plan = accepted(extract(ctx));
    ctx.referenceContext.push({ outputMappings: [{ outputReferenceKey: "later" }] });
    refused(applyNativeBlockPlan(ctx, plan), "stale-source");
  });

  it("requires the host to disclose its reference context before planning", () => {
    const ctx = { ...context(), referenceContext: undefined } as unknown as NativeBlockContext;
    refused(extract(ctx), "missing-reference-context");
  });

  it.each(["local", "input", "output", "setting", "structured-reference", "opaque-reference"]) ("refuses a used/configured boundary: %s", fault => {
    const ctx = context(); const plan = accepted(extract(ctx));
    const block = (planPayload(plan).activities as ActivityNode[])[1];
    if (fault === "local") block.structure!.payload.variables = [{ referenceKey: "local" }];
    if (fault === "input") block.inputs.push({ referenceKey: "value", value: 2 });
    if (fault === "output") block.outputs.push({ referenceKey: "result" });
    if (fault === "setting") block.executionSettings = { retry: 2 };
    if (fault === "structured-reference") (planPayload(plan).activities as ActivityNode[])[0].inputs.push({ referenceKey: "value", value: { activityNodeId: "new-block" } });
    if (fault === "opaque-reference") (planPayload(plan).activities as ActivityNode[])[0].inputs.push({ value: { expressionType: "JavaScript", value: 'activities["new-block"].result' } });
    refused(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"), fault === "local" ? "boundary-declarations" : fault.includes("reference") ? "referenced-boundary" : "boundary-settings");
  });

  it("removes an empty Sequence without changing adjacent activities", () => {
    const ctx = context();
    const plan = accepted(planCreateNativeBlock(ctx, { ...request, kind: "sequence", beforeNodeId: "b", position: { x: 0, y: 0 } }));
    expect(accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block")).document).toEqual(ctx.document);
  });

  it("refuses to flatten different native structure kinds", () => {
    const ctx = context();
    const plan = accepted(planCreateNativeBlock(ctx, { ...request, kind: "flowchart", activityVersionId: flowchartActivity.activityVersionId, position: { x: 0, y: 0 } }));
    refused(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"), "unsupported-block");
  });

  it("rejects references in connection extensions rather than skipping whole routing records", () => {
    const ctx = flow(); const plan = accepted(extract(ctx));
    (planPayload(plan).connections as Record<string, unknown>[])[0].customReference = { activityNodeId: "new-block" };
    refused(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"), "referenced-boundary");
  });

  it("allows removing the sole empty Flowchart but refuses a connected empty boundary", () => {
    const ctx = context(container("flowchart", []));
    const created = accepted(planCreateNativeBlock(ctx, { ...request, kind: "flowchart", activityVersionId: flowchartActivity.activityVersionId, position: { x: 0, y: 0 } }));
    const ungrouped = accepted(planUngroupNativeBlock({ ...ctx, document: created.document }, "new-block"));
    expect(planPayload(ungrouped)).toMatchObject({ activities: [], connections: [], startNodeId: null });
    const connected = flow(); const plan = accepted(extract(connected));
    ((planPayload(plan).activities as ActivityNode[])[1].structure!.payload.activities as ActivityNode[]).splice(0);
    (planPayload(plan).activities as ActivityNode[])[1].structure!.payload.connections = [];
    (planPayload(plan).activities as ActivityNode[])[1].structure!.payload.startNodeId = null;
    refused(planUngroupNativeBlock({ ...connected, document: plan.document }, "new-block"), "empty-flowchart-boundary");
  });

  it.each(["nodeMetadata", "connectionMetadata"])("does not lose retained %s when ungrouping an empty Flowchart", property => {
    const ctx = context(container("flowchart", []));
    const plan = accepted(planCreateNativeBlock(ctx, { ...request, kind: "flowchart", activityVersionId: flowchartActivity.activityVersionId, position: { x: 0, y: 0 } }));
    (planPayload(plan).activities as ActivityNode[])[0].structure!.payload[property] = { orphan: { configuration: { note: "keep" } } };
    refused(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"), "boundary-metadata");
  });

  it("removes a disconnected empty Flowchart without changing the explicit parent entry", () => {
    const ctx = context(container("flowchart", [node("a")], { connections: [], startNodeId: "a" }));
    const plan = accepted(planCreateNativeBlock(ctx, { ...request, kind: "flowchart", activityVersionId: flowchartActivity.activityVersionId, beforeNodeId: "a", position: { x: 0, y: 0 } }));
    const inverse = accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"));
    expect(inverse.document).toEqual(ctx.document);
  });

  it("does not activate orphan inner connection metadata on an existing outer connection", () => {
    const ctx = flow(); const plan = accepted(extract(ctx));
    (planPayload(plan).activities as ActivityNode[])[1].structure!.payload.connectionMetadata = { "a-b": { policyKind: "missing-policy" } };
    refused(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"), "metadata-collision");
  });

  it.each(["connections", "startNodeId"])("refuses malformed %s on an empty Flowchart boundary", property => {
    const ctx = context(container("flowchart", []));
    const plan = accepted(planCreateNativeBlock(ctx, { ...request, kind: "flowchart", activityVersionId: flowchartActivity.activityVersionId, position: { x: 0, y: 0 } }));
    (planPayload(plan).activities as ActivityNode[])[0].structure!.payload[property] = property === "connections" ? [{ bad: true }] : "gone";
    refused(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block"), property === "connections" ? "malformed-connection" : "invalid-start");
  });
});

describe("native blocks alongside compensation steps", () => {
  it.each([
    ["sequence", false], ["sequence", true], ["flowchart", false], ["flowchart", true]
  ] as const)("extracts and ungroups a nested %s with handler=%s without changing step records", (kind, withHandler) => {
    const { ctx } = compensationContext(kind, withHandler);
    const before = structuredClone(ctx);
    const plan = accepted(planExtractNativeBlock(ctx, {
      ...request, selectedNodeIds: ["b", "c"],
      activityVersionId: (kind === "sequence" ? sequenceActivity : flowchartActivity).activityVersionId
    }));
    const outer = plan.document.rootActivity.structure!.payload.activities as ActivityNode[];
    const steps = outer[0].structure!.payload.steps as Record<string, unknown>[];
    const beforeSteps = (before.document.rootActivity.structure!.payload.activities as ActivityNode[])[0].structure!.payload.steps as Record<string, unknown>[];
    expect(steps[0].future).toEqual({ keep: "row" });
    expect(steps[1]).toEqual(beforeSteps[1]);
    const body = steps[0].activity as ActivityNode;
    expect((body.structure!.payload.activities as ActivityNode[]).map(child => child.nodeId)).toEqual(["a", "new-block", "d"]);
    const restored = accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block")).document;
    if (kind === "flowchart") {
      const restoredScope = (restored.rootActivity.structure!.payload.activities as ActivityNode[])[0];
      const restoredBody = (restoredScope.structure!.payload.steps as Record<string, unknown>[])[0].activity as ActivityNode;
      const originalBody = beforeSteps[0].activity as ActivityNode;
      const actualConnections = restoredBody.structure!.payload.connections as ReturnType<typeof edge>[];
      const originalConnections = originalBody.structure!.payload.connections as ReturnType<typeof edge>[];
      // Flowchart routing is independent of connection-array order; retain every connection field.
      expect([...actualConnections].sort((a, b) => a.id.localeCompare(b.id))).toEqual([...originalConnections].sort((a, b) => a.id.localeCompare(b.id)));
      restoredBody.structure!.payload.connections = originalConnections;
    }
    expect(restored).toEqual(before.document);
    expect(ctx).toEqual(before);
  });

  it("creates and ungroups a block in a compensation handler without rewriting forward steps", () => {
    const { ctx } = compensationContext("sequence", true, true);
    const before = structuredClone(ctx.document);
    const plan = accepted(planCreateNativeBlock(ctx, { ...request, kind: "sequence", beforeNodeId: "b", position: { x: 20, y: 30 } }));
    expect(accepted(planUngroupNativeBlock({ ...ctx, document: plan.document }, "new-block")).document).toEqual(before);
  });

  it("retains an unrelated compensation scope when extracting outside it", () => {
    const { scope } = compensationContext("sequence");
    const ctx = context(container("sequence", [scope, node("outer-b"), node("outer-c")]));
    const plan = accepted(extract(ctx, ["outer-b", "outer-c"]));
    expect((planPayload(plan).activities as ActivityNode[])[0]).toEqual(scope);
  });

  it("does not offer extraction of the compensation overview itself", () => {
    const { scope } = compensationContext("sequence");
    refused(extract(context(scope)), "unsupported-scope");
  });

  it("reserves real compensation handler identities without counting the overview twice", () => {
    const { ctx } = compensationContext("sequence");
    refused(planExtractNativeBlock(ctx, { ...request, nodeId: "handler", selectedNodeIds: ["b", "c"] }), "occupied-identity");
  });

  it("still rejects an identity authored in both forward and compensation slots", () => {
    const { ctx, steps } = compensationContext("sequence");
    steps[0].compensation = node("b");
    refused(extract(ctx), "duplicate-node");
  });

  it.each(["not-array", "null-row", "array-row", "bad-forward", "bad-handler"])("refuses malformed compensation children: %s", fault => {
    const { ctx, scope, steps } = compensationContext("sequence");
    if (fault === "not-array") scope.structure!.payload.steps = {};
    if (fault === "null-row") steps.push(null as never);
    if (fault === "array-row") steps.push([] as never);
    if (fault === "bad-forward") steps[0].activity = { nodeId: "invalid" };
    if (fault === "bad-handler") steps[0].compensation = { nodeId: "invalid" };
    const before = structuredClone(ctx);
    refused(extract(ctx), "malformed-children");
    expect(ctx).toEqual(before);
  });
});
