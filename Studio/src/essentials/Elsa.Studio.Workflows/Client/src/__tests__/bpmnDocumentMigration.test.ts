import { describe, expect, it, vi } from "vitest";
import type { ActivityNode, DesignMetadataRecord } from "../workflowTypes";
import { applyBpmnDocumentCommands, bpmnNamespaces as ns, readBpmnDocument } from "../bpmn/bpmnDocument";
import { planLegacyBpmnDocumentMigration, prepareLegacyBpmnDocument } from "../bpmn/bpmnDocumentMigration";
import { projectBpmnDocumentCanvas } from "../bpmn/bpmnDocumentCanvas";
import { withDiagramFromLayout } from "../api/bpmnInterchange";

const vendor = "https://elsa-workflows.io/schemas/bpmn";
const variable = (referenceKey: string) => ({ referenceKey, name: referenceKey, type: { alias: "Elsa.String", collectionKind: "single" } });
function leaf(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: "exact-native@version", executionPlacement: "background", inputs: [{ referenceKey: "Expression",
    value: { expressionType: "Variable", value: { containerId: "body-native", referenceKey: "local" } }, storageDriverType: "retained" }],
  outputs: [{ referenceKey: "$result", value: { expressionType: "ActivityResult", value: { producerNodeId: "other", producerScopeId: "root", projectionKey: "value" } } }],
  vendorMetadata: { literal: "do not remap or export" } };
}
function legacy(): ActivityNode {
  const nested: ActivityNode = { nodeId: "body-native", activityVersionId: "exact-bpmn@nested", executionPlacement: "inline",
    inputs: [{ referenceKey: "CanStart", value: { expressionType: "Literal", value: false } }], outputs: [], headerMetadata: { retained: true },
    structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: {
      activities: [leaf("inside-native")], elements: [{ elementId: "Inside", elementType: "task", childNodeId: "inside-native" }], sequenceFlows: [],
      variables: [variable("local")], diagram: { shapes: { Inside: { x: 12.25, y: 18.75, width: 100, height: 80 } }, edges: {} }
    } } };
  return { nodeId: "root/native:opaque", activityVersionId: "exact-bpmn@root", inputs: [{ referenceKey: "CanStart", value: { expressionType: "Literal", value: true } }], outputs: [],
    vendorMetadata: { retained: "root" }, structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: {
      activities: [nested, leaf("task-native")], variables: [variable("outer")],
      elements: [{ elementId: "Start", elementType: "startEvent" }, { elementId: "Body", elementType: "subProcess", name: "  Scope  ", childNodeId: "body-native" },
        { elementId: "Task", elementType: "task", name: "  exact task  ", childNodeId: "task-native" }, { elementId: "End", elementType: "endEvent" }],
      sequenceFlows: [{ flowId: "F1", sourceRef: "Start", targetRef: "Body" }, { flowId: "F2", sourceRef: "Body", targetRef: "Task", name: "  route  " }, { flowId: "F3", sourceRef: "Task", targetRef: "End" }],
      diagram: { shapes: { Task: { x: 390.125, y: 42.875, width: 100.5, height: 80.25, label: { x: 391.25, y: 125.5, width: 70.5, height: 18.25 }, isMarkerVisible: true } },
        edges: { F2: { waypoints: [{ x: 300.25, y: 60.125 }, { x: 390.125, y: 60.125 }], label: { x: 305.5, y: 42.125, width: 50.25, height: 15.5 } } } }
    } } };
}
// A deliberately lossy legacy writer fixture: typed names are trimmed, only root DI is emitted,
// variables become name-only vendor elements, and portable bindings can be present. The upgrade
// must restore raw text/DI and retain native configuration independently of this representation.
function writer(owner: ActivityNode, processId: string): string {
  const escape = (value: unknown) => String(value).replaceAll("&", "&amp;").replaceAll('"', "&quot;");
  const scope = (node: ActivityNode): string => {
    const payload = node.structure!.payload;
    const children = payload.activities as ActivityNode[];
    return `<b:extensionElements><elsa:variable name="name-only"/></b:extensionElements>` + (payload.elements as { elementId: string; elementType: string; name?: string; childNodeId?: string; properties?: Record<string, string> }[]).map(element => {
      const child = children.find(child => child.nodeId === element.childNodeId);
      return `<b:${element.elementType} id="${element.elementId}"${element.name ? ` name="${escape(element.name.trim())}"` : ""}${element.childNodeId ? ' elsa:taskBinding="portable"' : ""}${element.properties?.["elsa.direct.binding"] ? ` elsa:directBinding="${escape(element.properties["elsa.direct.binding"])}"` : ""}>${child?.structure?.kind === "elsa.bpmn.structure" ? scope(child) : ""}</b:${element.elementType}>`;
    }).join("") + (payload.sequenceFlows as { flowId: string; sourceRef: string; targetRef: string; name?: string }[]).map(flow =>
      `<b:sequenceFlow id="${flow.flowId}" sourceRef="${flow.sourceRef}" targetRef="${flow.targetRef}"${flow.name ? ` name="${escape(flow.name.trim())}"` : ""}/>`).join("")
      + ((payload.pools ?? []) as { poolId: string }[]).map(pool => `<b:participant id="${pool.poolId}" processRef="${processId}"/>`).join("");
  };
  return `<b:definitions xmlns:b="${ns.model}" xmlns:elsa="${vendor}" xmlns:bd="${ns.diagram}" targetNamespace="urn:migration"><b:process id="${processId}" isExecutable="true">${scope(owner)}</b:process><bd:BPMNDiagram id="OldDiagram"><bd:BPMNPlane id="OldPlane" bpmnElement="${processId}"/></bd:BPMNDiagram></b:definitions>`;
}
function migrate(owner = legacy(), layout: DesignMetadataRecord[] = []) {
  const plan = planLegacyBpmnDocumentMigration(owner, layout);
  return plan.complete(writer(plan.exportOwner, plan.processId));
}

describe("legacy BPMN upgrade at the first authored transaction", () => {
  it("retains exact native identities, versions, bindings, typed references, variables and invocation headers", () => {
    const before = legacy(), original = structuredClone(before);
    const result = migrate(before), parsed = readBpmnDocument(result);
    expect(before).toEqual(original);
    expect(result.nodeId).toBe(before.nodeId); expect(result.activityVersionId).toBe(before.activityVersionId);
    expect(result.inputs).toEqual(before.inputs); expect(result.vendorMetadata).toEqual(before.vendorMetadata);
    const nested = (before.structure!.payload.activities as ActivityNode[])[0];
    const { structure: _, ...header } = nested;
    expect(parsed.payload.scopeHeaders).toEqual([{ ...header, scopeElementId: "Body", variables: nested.structure!.payload.variables }]);
    expect(parsed.payload.activities).toEqual([(nested.structure!.payload.activities as ActivityNode[])[0], (before.structure!.payload.activities as ActivityNode[])[1]]);
    expect(parsed.payload.bindings).toEqual([{ elementId: "Inside", childNodeId: "inside-native" }, { elementId: "Task", childNodeId: "task-native" }]);
    expect(parsed.payload.variables).toEqual([variable("outer")]);
    expect([...parsed.byId.values()].filter(element => ["Start", "Body", "Task", "End", "Inside", "F1", "F2", "F3"].includes(element.id))).toHaveLength(8);
    expect(parsed.byId.get("Inside")!.parentElement).toBe(parsed.byId.get("Body"));
  });

  it("uses a detached preview, leaves opening/switching clean, and admits migration with an actual edit", async () => {
    const before = legacy(), original = structuredClone(before);
    let release!: (value: string) => void;
    const exporting = vi.fn((owner: ActivityNode, processId: string) => new Promise<string>(resolve => { release = () => resolve(writer(owner, processId)); }));
    const pending = prepareLegacyBpmnDocument(before, [], exporting);
    expect(before).toEqual(original); expect(before.structure!.schemaVersion).toBe("1.0.0");
    release(""); const preview = await pending;
    expect(before).toEqual(original); expect(exporting).toHaveBeenCalledTimes(1);
    const changed = applyBpmnDocumentCommands(preview, [{ kind: "attribute", target: { elementId: "Task" }, name: "name", value: "Authored edit" }]);
    expect(readBpmnDocument(changed).byId.get("Task")!.getAttribute("name")).toBe("Authored edit");
    expect(before).toEqual(original);
  });

  it("keeps ordinary task-bound BPMN and Sequence native bodies intact instead of inlining or emptying them", () => {
    const before = legacy();
    const nested = (before.structure!.payload.activities as ActivityNode[])[0];
    (before.structure!.payload.elements as { elementId: string; elementType: string }[]).find(element => element.elementId === "Body")!.elementType = "task";
    const task = (before.structure!.payload.activities as ActivityNode[])[1];
    task.structure = { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [leaf("native-in-sequence")] } };
    const plan = planLegacyBpmnDocumentMigration(before);
    expect((plan.exportOwner.structure!.payload.activities as ActivityNode[]).every(node => !node.structure)).toBe(true);
    const parsed = readBpmnDocument(plan.complete(writer(plan.exportOwner, plan.processId)));
    expect(parsed.payload.scopeHeaders).toEqual([]);
    expect(parsed.payload.activities).toEqual([nested, task]);
    expect(parsed.byId.has("Inside")).toBe(false);
    expect(parsed.payload.bindings).toContainEqual({ elementId: "Body", childNodeId: "body-native" });
  });

  it("preserves a separate listener channel on a standard subprocess body", () => {
    const before = legacy();
    (before.structure!.payload.activities as ActivityNode[]).push(leaf("listener"));
    (before.structure!.payload.elements as Record<string, unknown>[]).find(element => element.elementId === "Body")!.listenerNodeId = "listener";
    const parsed = readBpmnDocument(migrate(before));
    expect(parsed.payload.bindings).toContainEqual({ elementId: "Body", listenerNodeId: "listener" });
    expect(parsed.payload.activities.find(node => node.nodeId === "listener")).toEqual(leaf("listener"));
    expect(parsed.payload.scopeHeaders?.[0].nodeId).toBe("body-native");
  });

  it("preserves exact labels, fractional geometry, markers and nested planes while applying authoritative layout", () => {
    const result = readBpmnDocument(migrate(legacy(), [{ nodeId: "Task", x: 401.25, y: 55.125 }]));
    const shapes = [...result.document.getElementsByTagNameNS(ns.diagram, "BPMNShape")];
    const task = shapes.find(shape => shape.getAttribute("bpmnElement") === "Task")!;
    expect(task.getAttribute("isMarkerVisible")).toBe("true");
    const bounds = task.getElementsByTagNameNS(ns.bounds, "Bounds");
    expect([...bounds].map(element => ["x", "y", "width", "height"].map(name => element.getAttribute(name)))).toEqual([
      ["401.25", "55.125", "100.5", "80.25"], ["391.25", "125.5", "70.5", "18.25"]
    ]);
    const edge = [...result.document.getElementsByTagNameNS(ns.diagram, "BPMNEdge")].find(edge => edge.getAttribute("bpmnElement") === "F2")!;
    expect([...edge.getElementsByTagNameNS(ns.coordinates, "waypoint")].map(point => [point.getAttribute("x"), point.getAttribute("y")])).toEqual([["300.25", "60.125"], ["390.125", "60.125"]]);
    expect([...result.document.getElementsByTagNameNS(ns.diagram, "BPMNPlane")].map(plane => plane.getAttribute("bpmnElement"))).toEqual([result.payload.processId, "Body"]);
    expect(result.byId.get("Task")!.getAttribute("name")).toBe("  exact task  ");
    expect(result.byId.get("F2")!.getAttribute("name")).toBe("  route  ");
  });

  it("does not duplicate native variables or task bindings into public XML", () => {
    const result = readBpmnDocument(migrate());
    expect(result.document.getElementsByTagNameNS(vendor, "variable")).toHaveLength(0);
    expect([...result.document.getElementsByTagNameNS(ns.model, "*")].some(element => element.hasAttributeNS(vendor, "taskBinding"))).toBe(false);
    expect(result.payload.documentXml).not.toContain("do not remap or export");
    expect(result.payload.variables).toHaveLength(1); expect(result.payload.scopeHeaders?.[0].variables).toHaveLength(1);
  });

  it("keeps an original direct human binding while removing generated portable task metadata", () => {
    const before = legacy(), elements = before.structure!.payload.elements as Record<string, unknown>[];
    const direct = JSON.stringify({ kind: "human", version: "future", form: { exact: true } });
    elements.push({ elementId: "Human", elementType: "userTask", properties: { "elsa.direct.binding": direct } });
    const parsed = readBpmnDocument(migrate(before));
    expect(parsed.byId.get("Human")!.getAttributeNS(vendor, "directBinding")).toBe(direct);
    expect(parsed.byId.get("Human")!.hasAttributeNS(vendor, "taskBinding")).toBe(false);
    expect(parsed.payload.bindings.some(binding => binding.elementId === "Human")).toBe(false);
  });

  it("removes only the library's redundant outcome predicate while preserving native routing intent", () => {
    const before = legacy(), flow = (before.structure!.payload.sequenceFlows as Record<string, unknown>[])[1];
    flow.conditionOutcome = "A'B\\C";
    const plan = planLegacyBpmnDocumentMigration(before);
    const xml = writer(plan.exportOwner, plan.processId).replace(/(<b:sequenceFlow id="F2"[^>]*)\/>/, "$1><b:conditionExpression>outcome == 'A'B\\C'</b:conditionExpression></b:sequenceFlow>");
    const parsed = readBpmnDocument(plan.complete(xml));
    expect(parsed.byId.get("F2")!.getAttributeNS(vendor, "conditionOutcome")).toBe("A'B\\C");
    expect(parsed.byId.get("F2")!.getElementsByTagNameNS(ns.model, "conditionExpression")).toHaveLength(0);
  });

  it.each(["retained-author", "different-expression", "comment", "instruction", "attribute"])("retains an authored or opaque condition %s", kind => {
    const before = legacy(), payload = before.structure!.payload;
    (payload.sequenceFlows as Record<string, unknown>[])[1].conditionOutcome = "High";
    if (kind === "retained-author") (payload.elements as Record<string, unknown>[])[0].properties = { "elsa.bpmn.retained-condition:F2": '{"body":"outcome == \'High\'"}' };
    const plan = planLegacyBpmnDocumentMigration(before);
    const condition = `<b:conditionExpression${kind === "attribute" ? ' language="urn:explicit"' : ""}>${kind === "different-expression" ? "custom()" : "outcome == 'High'"}${kind === "comment" ? "<!-- opaque -->" : kind === "instruction" ? "<?vendor exact?>" : ""}</b:conditionExpression>`;
    const xml = writer(plan.exportOwner, plan.processId).replace(/(<b:sequenceFlow id="F2"[^>]*)\/>/, `$1>${condition}</b:sequenceFlow>`);
    const parsed = readBpmnDocument(plan.complete(xml));
    expect(parsed.byId.get("F2")!.getElementsByTagNameNS(ns.model, "conditionExpression")).toHaveLength(1);
    expect(parsed.payload.documentXml).toContain(kind === "comment" ? "<!-- opaque -->" : kind === "instruction" ? "<?vendor exact?>" : kind === "different-expression" ? "custom()" : "outcome == 'High'");
  });

  it("keeps pools and message flows on their actual collaboration plane with legacy relationship metadata", () => {
    const before = legacy(), payload = before.structure!.payload;
    payload.pools = [{ poolId: "White", processRef: "Process", name: "  White pool  ", isExecutable: true }, { poolId: "Black", processRef: null, isExecutable: false }];
    payload.lanes = [{ laneId: "Lane", poolId: "White", name: "  Lane  " }];
    payload.messageFlows = [{ flowId: "Message", sourceElementId: "Task", sourcePoolId: "White", targetElementId: null, targetPoolId: "Black", name: "  Message  " }];
    const diagram = payload.diagram as { edges: Record<string, unknown> };
    diagram.edges.Message = { waypoints: [{ x: 490.125, y: 80 }, { x: 600, y: 80 }] };
    const plan = planLegacyBpmnDocumentMigration(before);
    const xml = writer(plan.exportOwner, plan.processId).replace(/<b:participant[^>]*\/>/g, "")
      .replace('<b:process id="Process" isExecutable="true">', '<b:process id="Process" isExecutable="true"><b:laneSet id="LaneSet"><b:lane id="Lane"><b:flowNodeRef>Task</b:flowNodeRef></b:lane></b:laneSet>')
      .replace('<bd:BPMNDiagram id="OldDiagram">', '<b:collaboration id="Collaboration"><b:participant id="White" processRef="Process"/><b:participant id="Black"/><b:messageFlow id="Message" sourceRef="Task" targetRef="Black"/></b:collaboration><bd:BPMNDiagram id="OldDiagram">');
    const result = plan.complete(xml), parsed = readBpmnDocument(result);
    const rootPlane = parsed.document.getElementsByTagNameNS(ns.diagram, "BPMNPlane")[0];
    expect(rootPlane.getAttribute("bpmnElement")).toBe("Collaboration");
    const canvas = projectBpmnDocumentCanvas(result, { documentOwnerNodeId: result.nodeId, scopeElementId: "Collaboration", diagramId: rootPlane.parentElement!.id });
    expect(canvas.nodes.map(node => node.data.semanticElementId)).toEqual(expect.arrayContaining(["White", "Black", "Task", "Lane"]));
    expect(canvas.edges.map(edge => edge.data!.semanticElementId)).toContain("Message");
    expect(parsed.byId.get("White")!.getAttribute("name")).toBe("  White pool  ");
    expect(parsed.byId.get("Black")!.hasAttribute("processRef")).toBe(false);
    expect(parsed.byId.get("Black")!.getAttributeNS(vendor, "isExecutable")).toBe("false");
    expect(parsed.byId.get("Lane")!.getAttributeNS(vendor, "poolId")).toBe("White");
    expect(parsed.byId.get("Message")!.getAttributeNS(vendor, "sourcePoolId")).toBe("White");
    expect(parsed.byId.get("Message")!.getAttributeNS(vendor, "targetPoolId")).toBe("Black");
    expect(before.structure!.schemaVersion).toBe("1.0.0");
  });

  it("makes completion deterministic and independent from later mutations of the export request", () => {
    const before = legacy(), plan = planLegacyBpmnDocumentMigration(before);
    const xml = writer(plan.exportOwner, plan.processId);
    plan.exportOwner.inputs.push("external mutation");
    (plan.exportOwner.structure!.payload.activities as ActivityNode[])[0].inputs.push("request-only mutation");
    const first = plan.complete(xml), second = plan.complete(xml);
    expect(first).toEqual(second); expect(first.inputs).toEqual(before.inputs);
    expect(readBpmnDocument(first).payload.scopeHeaders?.[0].inputs).toEqual((before.structure!.payload.activities as ActivityNode[])[0].inputs);
  });

  it("does not reconvert or rewrite a complete-source document on open or no-op save", async () => {
    const complete = migrate(), exporting = vi.fn();
    expect(await prepareLegacyBpmnDocument(complete, [], exporting)).toBe(complete);
    expect(exporting).not.toHaveBeenCalled();
    expect(withDiagramFromLayout(complete, [{ nodeId: "Task", x: 999, y: 999 }])).toBe(complete);
    expect(Object.hasOwn(complete.structure!.payload, "diagram")).toBe(false);
  });

  it.each(["payload", "element", "flow", "event", "diagram", "shape", "waypoint", "label"])("refuses unmapped retained %s metadata atomically", kind => {
    const before = legacy(), payload = before.structure!.payload;
    const element = (payload.elements as Record<string, unknown>[])[0];
    if (kind === "payload") payload.retainedVendor = { keep: true };
    if (kind === "element") element.retainedVendor = { keep: true };
    if (kind === "flow") (payload.sequenceFlows as Record<string, unknown>[])[0].retainedVendor = true;
    if (kind === "event") element.eventDefinitions = [{ type: "message", properties: { name: "Order", retainedVendor: "exact" } }];
    const diagram = payload.diagram as { shapes: Record<string, Record<string, unknown>>; edges: Record<string, Record<string, unknown>>; [key: string]: unknown };
    if (kind === "diagram") diagram.retainedVendor = "exact";
    if (kind === "shape") diagram.shapes.Task.retainedVendor = "exact";
    if (kind === "waypoint") (diagram.edges.F2.waypoints as Record<string, unknown>[])[0].retainedVendor = "exact";
    if (kind === "label") (diagram.shapes.Task.label as Record<string, unknown>).retainedVendor = "exact";
    const original = structuredClone(before);
    expect(() => migrate(before)).toThrow(/retained.*no qualified XML mapping/);
    expect(before).toEqual(original);
  });

  it.each(["duplicate-standard-id", "invalid-standard-id", "duplicate-native-id", "unbound-native", "shared-binding"])("refuses ambiguous identity or ownership %s without a partial rewrite", kind => {
    const before = legacy(), payload = before.structure!.payload;
    const children = payload.activities as ActivityNode[], elements = payload.elements as Record<string, unknown>[];
    if (kind === "duplicate-standard-id") (children[0].structure!.payload.elements as Record<string, unknown>[])[0].elementId = "Task";
    if (kind === "invalid-standard-id") elements[0].elementId = "0bad";
    if (kind === "duplicate-native-id") (children[0].structure!.payload.activities as ActivityNode[])[0].nodeId = "task-native";
    if (kind === "unbound-native") children.push(leaf("orphan"));
    if (kind === "shared-binding") elements[0].childNodeId = "task-native";
    const original = structuredClone(before);
    expect(() => migrate(before)).toThrow(); expect(before).toEqual(original);
  });

  it("refuses omitted or relocated writer elements before handing a candidate to its parent", () => {
    const before = legacy(), plan = planLegacyBpmnDocumentMigration(before), xml = writer(plan.exportOwner, plan.processId);
    expect(() => plan.complete(xml.replace(/<b:task id="Task"[^>]*>.*?<\/b:task>/, ""))).toThrow();
    expect(before.structure!.schemaVersion).toBe("1.0.0");
  });
});
