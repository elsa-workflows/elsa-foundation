import { describe, expect, it } from "vitest";
// @ts-expect-error bpmn-moddle 10.3.1 publishes schema types but no declaration for its runtime export.
import { BpmnModdle } from "bpmn-moddle";
import SubprocessCompatibility from "bpmn-js/lib/features/drilldown/SubprocessCompatibility";
import type { ActivityNode, DesignMetadataRecord } from "@elsa-workflows/studio-workflows";
import { mergeBpmnJsDefinitions, projectBpmnJsXml } from "../bpmnJsBridge";

type ObjectValue = Record<string, unknown>;
interface Moddle {
  fromXML(xml: string): Promise<{ rootElement: ObjectValue; warnings: unknown[] }>;
  create(type: string, properties?: ObjectValue): ObjectValue;
  toXML(definitions: unknown): Promise<{ xml: string }>;
}
const moddle: Moddle = new BpmnModdle();
const values = (value: unknown) => value as ObjectValue[];
const record = (value: unknown) => value as ObjectValue;
const process = (definitions: ObjectValue) => values(definitions.rootElements)[0];
const flow = (definitions: ObjectValue, id: string) => values(process(definitions).flowElements).find(value => value.id === id)!;
const plane = (definitions: ObjectValue) => record(values(definitions.diagrams)[0].plane);
const di = (definitions: ObjectValue, id: string) => values(plane(definitions).planeElement).find(value => record(value.bpmnElement).id === id)!;
function replaceType(definitions: ObjectValue, id: string, type: string) {
  process(definitions).flowElements = values(process(definitions).flowElements).map(value => value.id === id ? moddle.create(type, { id, name: value.name }) : value);
}
function leaf(nodeId: string): ActivityNode { return { nodeId, activityVersionId: `exact-${nodeId}@1`, inputs: [], outputs: [] }; }
function scoped(nodeId: string, elements: unknown[], sequenceFlows: unknown[], activities: ActivityNode[] = []): ActivityNode {
  return { ...leaf(nodeId), structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: { elements, sequenceFlows, activities } } };
}
function richOwner(): ActivityNode {
  const listener = { ...leaf("listener-node"), inputs: [{ referenceKey: "Duration", value: { value: "00:03:00", expressionType: "Literal" } }], future: { retained: true } };
  const nestedBody = scoped("event-body", [{ elementId: "deep-start", elementType: "startEvent" }, { elementId: "deep-end", elementType: "endEvent" }],
    [{ flowId: "deep-flow", sourceRef: "deep-start", targetRef: "deep-end" }]);
  const nested = scoped("nested-node", [
    { elementId: "nested-start", elementType: "startEvent" }, { elementId: "nested-end", elementType: "endEvent" },
    { elementId: "event-subprocess", elementType: "subProcess", triggeredByEvent: true, childNodeId: nestedBody.nodeId, listenerNodeId: listener.nodeId }
  ], [{ flowId: "nested-flow", sourceRef: "nested-start", targetRef: "nested-end" }], [nestedBody, listener]);
  nested.structure!.payload.diagram = { shapes: { "nested-start": { x: 44, y: 55, width: 36, height: 36 } }, edges: {} };
  const decision = { ...leaf("decision-node"), inputs: [{ referenceKey: "Outcome", value: { expressionType: "JavaScript", value: 'getInput("amount") > 1000 ? "High" : "Low"' } }] };
  const task = { ...leaf("task-node"), inputs: [
    { referenceKey: "Connection", value: { expressionType: "Connection", value: { connectionId: "retained-reference" } }, conversion: { mode: "json" } },
    { referenceKey: "Credential", value: { expressionType: "Secret", value: { secretId: "retained-secret-reference" } } }
  ], outputs: [{ referenceKey: "result", target: { variable: "answer" } }], contributed: { module: "custom", nested: [1, 2] } };
  const owner = scoped("opaque:owner/identity", [
    { elementId: "start", elementType: "startEvent" },
    { elementId: "gateway", elementType: "exclusiveGateway", childNodeId: decision.nodeId, defaultFlowId: "default-flow", properties: { retained: "opaque" } },
    { elementId: "work", elementType: "serviceTask", childNodeId: task.nodeId, unknown: { keep: true } },
    { elementId: "sub", elementType: "subProcess", childNodeId: nested.nodeId },
    { elementId: "end", elementType: "endEvent", eventDefinitions: [{ type: "terminate", future: "keep" }] }
  ], [
    { flowId: "start-flow", sourceRef: "start", targetRef: "gateway" },
    { flowId: "high-flow", sourceRef: "gateway", targetRef: "work", conditionOutcome: "High", contributed: { keep: true } },
    { flowId: "default-flow", sourceRef: "gateway", targetRef: "sub", isDefault: true },
    { flowId: "work-end", sourceRef: "work", targetRef: "end" },
    { flowId: "sub-end", sourceRef: "sub", targetRef: "end" }
  ], [decision, task, nested]);
  owner.structure!.payload.variables = [{ name: "answer", type: { alias: "Int32" }, default: { value: 7 }, storageDriverType: "Json" }];
  owner.structure!.payload.future = { schema: "retained" };
  owner.structure!.payload.diagram = { vendor: "retained", shapes: { work: { x: 1, y: 2, width: 140, height: 90, label: { x: 10, y: 100, width: 120, height: 20 }, custom: "keep" } },
    edges: { "high-flow": { waypoints: [{ x: 9, y: 30 }, { x: 99, y: 30 }], custom: "keep" } } };
  return owner;
}
const layout: DesignMetadataRecord[] = [
  { nodeId: "work", x: 420, y: 180, additionalProperties: { color: "preserved" } },
  { nodeId: "another-scope", x: 9, y: 19, additionalProperties: { retained: true } }
];
async function projected(owner = richOwner(), positions = layout) {
  const result = await moddle.fromXML(projectBpmnJsXml(owner, positions));
  expect(result.warnings).toEqual([]);
  return result.rootElement;
}
function normalizeSubprocessPlanes(definitions: ObjectValue) {
  // Exercise the actual pinned modeler's import.render.start module, which creates child planes
  // even for the empty collapsed subprocess placeholders used for opaque Elsa bodies.
  let onImport: ((event: unknown, context: { definitions: ObjectValue }) => void) | undefined;
  const events = { on(name: string, _priority: number, handler: typeof onImport) {
    expect(name).toBe("import.render.start"); onImport = handler;
  } };
  new SubprocessCompatibility(events as unknown as ConstructorParameters<typeof SubprocessCompatibility>[0],
    moddle as unknown as ConstructorParameters<typeof SubprocessCompatibility>[1]);
  expect(onImport).toBeDefined();
  onImport!({}, { definitions });
}
function authoredElement(owner: ActivityNode, id: string) { return values(owner.structure!.payload.elements).find(value => value.elementId === id)!; }
function authoredFlow(owner: ActivityNode, id: string) { return values(owner.structure!.payload.sequenceFlows).find(value => value.flowId === id)!; }

describe("identity-preserving bpmn-js authoring bridge", () => {
  it("parses projected XML using released moddle and keeps rich Elsa configuration in the same tree", async () => {
    const owner = richOwner(); const before = structuredClone(owner);
    const definitions = await projected(owner);
    const merged = mergeBpmnJsDefinitions(owner, definitions, layout);
    expect(merged.owner.nodeId).toBe(owner.nodeId);
    expect(merged.owner.activityVersionId).toBe(owner.activityVersionId);
    expect(merged.owner.structure!.payload.activities).toEqual(owner.structure!.payload.activities);
    expect(values(merged.owner.structure!.payload.activities)[2]).toBe(values(owner.structure!.payload.activities)[2]);
    expect(merged.owner.structure!.payload.variables).toBe(owner.structure!.payload.variables);
    expect(merged.owner.structure!.payload.future).toBe(owner.structure!.payload.future);
    expect(authoredElement(merged.owner, "gateway").childNodeId).toBe("decision-node");
    expect(authoredFlow(merged.owner, "high-flow").conditionOutcome).toBe("High");
    expect(authoredFlow(merged.owner, "high-flow").contributed).toEqual({ keep: true });
    expect(authoredElement(merged.owner, "end").eventDefinitions).toEqual([{ type: "terminate", future: "keep" }]);
    expect(merged.layout.find(value => value.nodeId === "another-scope")).toBe(layout[1]);
    expect(owner).toEqual(before);
    const xml = projectBpmnJsXml(owner, layout);
    expect(xml).not.toContain("retained-secret-reference");
    expect(xml).not.toContain("getInput");
    expect(xml).not.toContain("listener-node");
  });

  it("applies gateway defaults, names, positions and waypoints without replacing bound work", async () => {
    const owner = richOwner();
    const definitions = await projected(owner);
    flow(definitions, "gateway").default = flow(definitions, "high-flow");
    flow(definitions, "work").name = "Configured Elsa work";
    record(di(definitions, "work").bounds).x = 520;
    values(di(definitions, "high-flow").waypoint)[0].y = 99;
    const merged = mergeBpmnJsDefinitions(owner, definitions, layout);
    expect(authoredElement(merged.owner, "gateway").defaultFlowId).toBe("high-flow");
    expect(authoredFlow(merged.owner, "high-flow")).toMatchObject({ isDefault: true, conditionOutcome: null });
    expect(authoredFlow(merged.owner, "default-flow").isDefault).toBe(false);
    expect(authoredElement(merged.owner, "work")).toMatchObject({ name: "Configured Elsa work", childNodeId: "task-node", unknown: { keep: true } });
    expect(merged.owner.structure!.payload.activities).toEqual(owner.structure!.payload.activities);
    expect(merged.layout.find(value => value.nodeId === "work")).toMatchObject({ x: 520, y: 180, width: 140, height: 90, additionalProperties: { color: "preserved" } });
    const diagram = record(merged.owner.structure!.payload.diagram);
    expect(record(record(diagram.shapes).work)).toMatchObject({ x: 520, custom: "keep", label: { x: 10, y: 100, width: 120, height: 20 } });
    expect(record(record(diagram.edges)["high-flow"])).toMatchObject({ custom: "keep", waypoints: [{ x: 9, y: 99 }, { x: 99, y: 30 }] });
    const next = await projected(merged.owner, merged.layout);
    expect(flow(next, "gateway").default).toBe(flow(next, "high-flow"));
    expect(record(di(next, "work").bounds).x).toBe(520);
  });

  it("retains outcomes and diagram data through actual moddle XML serialization", async () => {
    const owner = richOwner();
    const definitions = await projected(owner);
    flow(definitions, "work").name = "From bpmn-js & back";
    const saved = await moddle.toXML(definitions);
    const reread = await moddle.fromXML(saved.xml);
    expect(reread.warnings).toEqual([]);
    const merged = mergeBpmnJsDefinitions(owner, reread.rootElement, layout);
    expect(authoredFlow(merged.owner, "high-flow").conditionOutcome).toBe("High");
    expect(authoredElement(merged.owner, "work").name).toBe("From bpmn-js & back");
    expect(merged.owner.structure!.payload.activities).toEqual(owner.structure!.payload.activities);
  });

  it.each([true, false])("preserves native modeler gateway marker visibility (%s) across a drag and XML round trip", async isMarkerVisible => {
    const owner = richOwner();
    const definitions = await projected(owner);
    const gateway = di(definitions, "gateway");
    gateway.isMarkerVisible = isMarkerVisible;
    record(gateway.bounds).x = 730;
    const merged = mergeBpmnJsDefinitions(owner, definitions, layout);
    expect(record(record(record(merged.owner.structure!.payload.diagram).shapes).gateway)).toMatchObject({ x: 730, isMarkerVisible });
    const reread = await projected(merged.owner, merged.layout);
    expect(di(reread, "gateway").isMarkerVisible).toBe(isMarkerVisible);
    expect(record(di(reread, "gateway").bounds).x).toBe(730);
    expect(merged.owner.structure!.payload.activities).toEqual(owner.structure!.payload.activities);
  });

  it.each(["nested-bpmn", "native-sequence"])("accepts real modeler normalization of a collapsed %s body without replacing it", async bodyKind => {
    const owner = richOwner();
    if (bodyKind === "native-sequence") {
      const activities = values(owner.structure!.payload.activities);
      activities[2] = { ...leaf("nested-node"), structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [leaf("native-child")] } } };
    }
    const before = structuredClone(owner);
    const definitions = await projected(owner);
    normalizeSubprocessPlanes(definitions);
    expect(values(definitions.diagrams)).toHaveLength(2);
    const childPlane = record(values(definitions.diagrams)[1].plane);
    expect(record(childPlane.bpmnElement)).toBe(flow(definitions, "sub"));
    expect(values(childPlane.planeElement)).toEqual([]);
    flow(definitions, "gateway").name = "Edited after real normalization";
    record(di(definitions, "gateway").bounds).x = 730;
    const merged = mergeBpmnJsDefinitions(owner, definitions, layout);
    expect(authoredElement(merged.owner, "gateway").name).toBe("Edited after real normalization");
    expect(merged.layout.find(value => value.nodeId === "gateway")).toMatchObject({ x: 730 });
    expect(merged.owner.structure!.payload.activities).toEqual(owner.structure!.payload.activities);
    expect(values(merged.owner.structure!.payload.activities)[2]).toBe(values(owner.structure!.payload.activities)[2]);
    expect(owner).toEqual(before);
  });

  it.each([
    ["nonempty child plane", (definitions: ObjectValue) => { record(values(definitions.diagrams)[1].plane).planeElement = [di(definitions, "gateway")]; }],
    ["unrelated child plane", (definitions: ObjectValue) => { record(values(definitions.diagrams)[1].plane).bpmnElement = flow(definitions, "work"); }],
    ["identified child diagram", (definitions: ObjectValue) => { values(definitions.diagrams)[1].id = "authored-diagram"; }],
    ["identified child plane", (definitions: ObjectValue) => { record(values(definitions.diagrams)[1].plane).id = "authored-plane"; }],
    ["annotated child plane", (definitions: ObjectValue) => { record(record(values(definitions.diagrams)[1].plane).$attrs)["vendor:keep"] = "content"; }],
    ["named child diagram", (definitions: ObjectValue) => { values(definitions.diagrams)[1].name = "Authored name"; }],
    ["duplicate child plane", (definitions: ObjectValue) => { values(definitions.diagrams).push(values(definitions.diagrams)[1]); }],
    ["duplicate root diagram", (definitions: ObjectValue) => { values(definitions.diagrams).push(values(definitions.diagrams)[0]); }]
  ])("refuses %s rather than silently ignoring authored diagram content", async (_name, mutate) => {
    const owner = richOwner(); const before = structuredClone(owner);
    const definitions = await projected(owner);
    normalizeSubprocessPlanes(definitions); mutate(definitions);
    expect(() => mergeBpmnJsDefinitions(owner, definitions, layout)).toThrow("The Elsa process is unchanged");
    expect(owner).toEqual(before);
  });

  it("retains a newly edited shared-inspector binding on subsequent canvas edits", async () => {
    const owner = richOwner();
    values(owner.structure!.payload.activities)[1].inputs = [{ referenceKey: "Text", value: { expressionType: "JavaScript", value: "variables.answer" } }];
    const definitions = await projected(owner);
    flow(definitions, "work").name = "Changed on canvas";
    const merged = mergeBpmnJsDefinitions(owner, definitions, layout);
    expect(values(merged.owner.structure!.payload.activities)[1].inputs).toEqual(values(owner.structure!.payload.activities)[1].inputs);
  });

  it("removes only the explicitly deleted element's work and retains nested listener configuration", async () => {
    const owner = richOwner(); const before = structuredClone(owner);
    const definitions = await projected(owner);
    const removed = new Set(["work", "high-flow", "work-end"]);
    process(definitions).flowElements = values(process(definitions).flowElements).filter(value => !removed.has(String(value.id)));
    plane(definitions).planeElement = values(plane(definitions).planeElement).filter(value => !removed.has(String(record(value.bpmnElement).id)));
    const merged = mergeBpmnJsDefinitions(owner, definitions, layout);
    expect(values(merged.owner.structure!.payload.activities).map(value => value.nodeId)).toEqual(["decision-node", "nested-node"]);
    expect(values(merged.owner.structure!.payload.activities)[1]).toEqual(values(owner.structure!.payload.activities)[2]);
    expect(merged.layout.some(value => value.nodeId === "work")).toBe(false);
    expect(owner).toEqual(before);
  });

  it("accepts supported unbound shape creation without binding it to unrelated Elsa work", async () => {
    const owner = scoped("root", [], []); const definitions = await projected(owner, []);
    const task = moddle.create("bpmn:Task", { id: "new-task", name: "New work" });
    process(definitions).flowElements = [task];
    plane(definitions).planeElement = [moddle.create("bpmndi:BPMNShape", { id: "new-shape", bpmnElement: task, bounds: moddle.create("dc:Bounds", { x: 5, y: 7, width: 100, height: 80 }) })];
    const merged = mergeBpmnJsDefinitions(owner, definitions, []);
    expect(authoredElement(merged.owner, "new-task")).toMatchObject({ elementType: "task", name: "New work" });
    expect(merged.owner.structure!.payload.activities).toEqual([]);
  });

  it.each([
    ["loop", (owner: ActivityNode) => { authoredElement(owner, "work").loopCharacteristics = { cardinality: 3 }; }],
    ["event", (owner: ActivityNode) => { authoredElement(owner, "start").eventDefinitions = [{ type: "message", properties: { name: "arrived" } }]; }],
    ["lane", (owner: ActivityNode) => { authoredElement(owner, "work").laneId = "lane"; }],
    ["condition", (owner: ActivityNode) => { record(authoredElement(owner, "gateway").properties)["elsa.bpmn.retained-condition:high-flow"] = '{"body":"x > 1"}'; }],
    ["unsupported type", (owner: ActivityNode) => { authoredElement(owner, "gateway").elementType = "complexGateway"; }],
    ["inherited object type", (owner: ActivityNode) => { authoredElement(owner, "gateway").elementType = "toString"; }],
    ["malformed lane reference", (owner: ActivityNode) => { authoredElement(owner, "work").laneId = false; }],
    ["blank default identity", (owner: ActivityNode) => { authoredElement(owner, "gateway").defaultFlowId = ""; }],
    ["malformed elements", (owner: ActivityNode) => { owner.structure!.payload.elements = [null]; }],
    ["malformed flow", (owner: ActivityNode) => { values(owner.structure!.payload.sequenceFlows).push({}); }],
    ["orphan work", (owner: ActivityNode) => { values(owner.structure!.payload.activities).push(leaf("orphan")); }],
    ["duplicate nested id", (owner: ActivityNode) => { authoredElement(owner, "work").elementId = "deep-start"; }],
    ["unsupported schema", (owner: ActivityNode) => { owner.structure!.schemaVersion = "2.0.0"; }]
  ])("refuses %s before projection without filtering or altering the source", (_label, mutate) => {
    const owner = richOwner(); mutate(owner); const before = structuredClone(owner);
    expect(() => projectBpmnJsXml(owner, layout)).toThrow("The Elsa process is unchanged");
    expect(owner).toEqual(before);
  });

  it.each([
    ["condition expressions", (definitions: ObjectValue) => { flow(definitions, "high-flow").conditionExpression = moddle.create("bpmn:FormalExpression", { body: "amount > 1" }); }],
    ["bound type change", (definitions: ObjectValue) => { replaceType(definitions, "work", "bpmn:UserTask"); }],
    ["unsupported type", (definitions: ObjectValue) => { replaceType(definitions, "work", "bpmn:ComplexGateway"); }],
    ["extensions", (definitions: ObjectValue) => { flow(definitions, "work").extensionElements = moddle.create("bpmn:ExtensionElements", {}); }],
    ["unknown attribute", (definitions: ObjectValue) => { record(flow(definitions, "work").$attrs)["vendor:execution"] = "changed"; }],
    ["expanded body", (definitions: ObjectValue) => { di(definitions, "sub").isExpanded = true; }],
    ["invalid source", (definitions: ObjectValue) => { flow(definitions, "high-flow").sourceRef = { id: "missing" }; }],
    ["default on a start event", (definitions: ObjectValue) => { flow(definitions, "start").default = flow(definitions, "start-flow"); }],
    ["duplicate identity", (definitions: ObjectValue) => { flow(definitions, "work").id = "gateway"; }],
    ["duplicate diagram identity", (definitions: ObjectValue) => { di(definitions, "work").id = "gateway"; }],
    ["duplicate event identity", (definitions: ObjectValue) => { values(flow(definitions, "end").eventDefinitions)[0].id = "gateway"; }],
    ["inconsistent flow references", (definitions: ObjectValue) => { flow(definitions, "work").incoming = [flow(definitions, "start-flow")]; }],
    ["missing diagram", (definitions: ObjectValue) => { definitions.diagrams = []; }],
    ["missing shape", (definitions: ObjectValue) => { plane(definitions).planeElement = values(plane(definitions).planeElement).filter(value => record(value.bpmnElement).id !== "work"); }],
    ["multiple process", (definitions: ObjectValue) => { values(definitions.rootElements).push(moddle.create("bpmn:Process", { id: "another" })); }],
    ["nested id collision", (definitions: ObjectValue) => { flow(definitions, "work").id = "deep-start"; }],
    ["nonfinite layout", (definitions: ObjectValue) => { record(di(definitions, "work").bounds).x = Number.POSITIVE_INFINITY; }]
  ])("refuses %s atomically when merging a modeler edit", async (_label, mutate) => {
    const owner = richOwner(); const before = structuredClone(owner);
    const definitions = await projected(owner); mutate(definitions);
    expect(() => mergeBpmnJsDefinitions(owner, definitions, layout)).toThrow("The Elsa process is unchanged");
    expect(owner).toEqual(before);
  });
});
