// @vitest-environment jsdom
import { describe, expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentCommands, bpmnNamespaces, projectBpmnDocumentScene, readBpmnDocument, type BpmnDocumentCommand } from "../bpmn/bpmnDocument";

const xml = `<?xml version="1.0"?>
<bpmn:definitions xmlns:bpmn="${bpmnNamespaces.model}" xmlns:bpmndi="${bpmnNamespaces.diagram}"
 xmlns:dc="${bpmnNamespaces.bounds}" xmlns:di="${bpmnNamespaces.coordinates}" xmlns:vendor="urn:vendor" id="Definitions" targetNamespace="urn:test">
 <bpmn:message id="Message" name="message"/>
 <bpmn:process id="Process" isExecutable="true" vendor:retained="yes">
  <bpmn:extensionElements><vendor:config token="original"><vendor:mixed>before <vendor:child/> after</vendor:mixed></vendor:config></bpmn:extensionElements>
  <bpmn:laneSet id="Lanes"><bpmn:lane id="Lane"><bpmn:flowNodeRef>Task</bpmn:flowNodeRef></bpmn:lane></bpmn:laneSet>
  <bpmn:startEvent id="Start"><bpmn:outgoing>Flow</bpmn:outgoing></bpmn:startEvent>
  <bpmn:serviceTask id="Task" name="Work" vendor:unknown="untouched"><bpmn:incoming vendor:note="keep">Flow</bpmn:incoming>
   <bpmn:multiInstanceLoopCharacteristics isSequential="true"><bpmn:loopCardinality>3</bpmn:loopCardinality></bpmn:multiInstanceLoopCharacteristics>
  </bpmn:serviceTask>
  <bpmn:boundaryEvent id="Boundary" attachedToRef="Task" cancelActivity="false"><bpmn:messageEventDefinition messageRef="Message"/></bpmn:boundaryEvent>
  <bpmn:subProcess id="Nested" triggeredByEvent="true"><bpmn:startEvent id="NestedStart" isInterrupting="false"><bpmn:signalEventDefinition/></bpmn:startEvent></bpmn:subProcess>
  <bpmn:sequenceFlow id="Flow" sourceRef="Start" targetRef="Task"><bpmn:conditionExpression language="urn:expression"><![CDATA[a < 4 && b > 1]]></bpmn:conditionExpression></bpmn:sequenceFlow>
  <bpmn:textAnnotation id="Note"><bpmn:text>Keep this note</bpmn:text></bpmn:textAnnotation>
  <bpmn:association id="Association" sourceRef="Task" targetRef="Note" associationDirection="One"/>
 </bpmn:process>
 <bpmn:process id="OtherProcess"><bpmn:receiveTask id="OtherTask"/></bpmn:process>
 <bpmn:collaboration id="Collaboration"><bpmn:participant id="Pool" processRef="Process"/><bpmn:participant id="OtherPool" processRef="OtherProcess"/>
  <bpmn:messageFlow id="MessageFlow" sourceRef="Task" targetRef="OtherTask" messageRef="Message"/>
 </bpmn:collaboration>
 <bpmndi:BPMNDiagram id="Diagram"><bpmndi:BPMNPlane id="Plane" bpmnElement="Collaboration">
  <bpmndi:BPMNShape id="PoolShape" bpmnElement="Pool" isHorizontal="true"><dc:Bounds x="0" y="0" width="700" height="300"/></bpmndi:BPMNShape>
  <bpmndi:BPMNShape id="TaskShape" bpmnElement="Task"><dc:Bounds x="200" y="80" width="100" height="80"/><bpmndi:BPMNLabel><dc:Bounds x="180" y="170" width="140" height="20"/></bpmndi:BPMNLabel></bpmndi:BPMNShape>
  <bpmndi:BPMNShape id="BoundaryShape" bpmnElement="Boundary"><dc:Bounds x="282" y="142" width="36" height="36"/></bpmndi:BPMNShape>
  <bpmndi:BPMNEdge id="FlowEdge" bpmnElement="Flow" vendor:routing="retain"><di:waypoint x="100" y="120" vendor:point="retain"/><di:waypoint x="200" y="120"/></bpmndi:BPMNEdge>
  <bpmndi:BPMNEdge id="MessageEdge" bpmnElement="MessageFlow"><di:waypoint x="250" y="160"/><di:waypoint x="250" y="400"/></bpmndi:BPMNEdge>
  <bpmndi:BPMNEdge id="AssociationEdge" bpmnElement="Association"><di:waypoint x="300" y="120"/><di:waypoint x="400" y="120"/></bpmndi:BPMNEdge>
 </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
</bpmn:definitions>`;

function owner(source = xml): ActivityNode {
  return { nodeId: "opaque:process/1", activityVersionId: "exact-bpmn-v1", inputs: [], outputs: [], custom: { retained: true }, structure: {
    kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: { documentXml: source, processId: "Process", variables: [], bindings: [
      { elementId: "Task", childNodeId: "body", listenerNodeId: "listener" }
    ], activities: [
      { nodeId: "body", activityVersionId: "exact-sequence", inputs: [{ referenceKey: "secret", value: { expressionType: "Secret", value: { secretId: "reference-only" } } }], outputs: [], structure: {
        kind: "elsa.sequence", schemaVersion: "1.0.0", payload: { activities: [{ nodeId: "leaf", activityVersionId: "exact-inline", inputs: [], outputs: [] }] }
      } },
      { nodeId: "listener", activityVersionId: "exact-listener", inputs: [], outputs: [], moduleData: { keep: [1, 2] } }
    ], futureMetadata: { retained: true } }
  } };
}
function element(value: ActivityNode, id: string) { return readBpmnDocument(value).byId.get(id)!; }
function command(value: BpmnDocumentCommand) { return applyBpmnDocumentCommands(owner(), [value]); }
const address = (elementId: string) => ({ elementId });
function retainedXml(value: ActivityNode) {
  const parsed = readBpmnDocument(value);
  const extension = parsed.document.getElementsByTagNameNS("urn:vendor", "config")[0];
  expect(extension, "untouched vendor configuration remains present").toBeDefined();
  return new XMLSerializer().serializeToString(extension);
}

describe("complete BPMN document authority", () => {
  it("opens full collaboration, nested events, loops and vendor syntax without rewriting source", () => {
    const original = owner();
    const before = structuredClone(original);
    const parsed = readBpmnDocument(original);
    expect(parsed.payload.documentXml).toBe(xml);
    expect(parsed.byId.get("NestedStart")?.localName).toBe("startEvent");
    expect(parsed.byId.get("OtherTask")?.parentElement?.getAttribute("id")).toBe("OtherProcess");
    expect(parsed.process.getAttributeNS("urn:vendor", "retained")).toBe("yes");
    expect(original).toEqual(before);
    expect(applyBpmnDocumentCommands(original, [])).toBe(original);
  });

  it("keeps original XML bytes and draft identity for no-op property/geometry commands", () => {
    const original = owner();
    expect(applyBpmnDocumentCommands(original, [
      { kind: "attribute", target: address("Task"), name: "name", value: "Work" },
      { kind: "text", target: { elementId: "Flow", path: [{ namespaceURI: bpmnNamespaces.model, localName: "conditionExpression" }] }, value: "a < 4 && b > 1" },
      { kind: "bounds", shapeId: "TaskShape", bounds: { x: 200, y: 80, width: 100, height: 80 } },
      { kind: "waypoints", edgeId: "FlowEdge", points: [{ x: 100, y: 120 }, { x: 200, y: 120 }] },
      { kind: "reconnect", elementId: "Flow", sourceRef: "Start", targetRef: "Task" }
    ])).toBe(original);
    expect(original.structure!.payload.documentXml).toBe(xml);
  });

  it("retains declaration-like text inside scripts and comments as ordinary source content", () => {
    const source = xml.replace("a < 4 && b > 1", 'scriptText == "<!DOCTYPE sample>"')
      .replace('<bpmn:process id="Process"', '<!-- documentation about <!ENTITY sample> --> <bpmn:process id="Process"');
    const original = owner(source);
    expect(readBpmnDocument(original).payload.documentXml).toBe(source);
    const next = applyBpmnDocumentCommands(original, [{ kind: "attribute", target: address("Task"), name: "name", value: "Edited" }]);
    expect(element(next, "Flow").textContent).toBe('scriptText == "<!DOCTYPE sample>"');
  });

  it("edits a standard property while retaining exact native trees, references, metadata and other syntax", () => {
    const original = owner();
    const next = applyBpmnDocumentCommands(original, [{ kind: "attribute", target: address("Task"), name: "name", value: "Edited" }]);
    expect(element(next, "Task").getAttribute("name")).toBe("Edited");
    expect(element(original, "Task").getAttribute("name")).toBe("Work");
    expect(next.structure!.payload.activities).toBe(original.structure!.payload.activities);
    expect(next.structure!.payload.bindings).toBe(original.structure!.payload.bindings);
    expect(next.structure!.payload.futureMetadata).toEqual({ retained: true });
    expect(retainedXml(next)).toBe(retainedXml(original));
    expect(element(next, "Task").getAttributeNS("urn:vendor", "unknown")).toBe("untouched");
    expect(element(next, "Flow").textContent).toBe("a < 4 && b > 1");
    expect(element(next, "Nested").outerHTML).toBe(element(original, "Nested").outerHTML);
    expect(element(next, "OtherProcess").outerHTML).toBe(element(original, "OtherProcess").outerHTML);
  });

  it("addresses idless standard property text without duplicating semantic state", () => {
    const next = command({ kind: "text", target: { elementId: "Flow", path: [{ namespaceURI: bpmnNamespaces.model, localName: "conditionExpression" }] }, value: "amount > 500" });
    expect(element(next, "Flow").textContent).toBe("amount > 500");
    expect(next.structure!.payload.elements).toBeUndefined();
    expect(next.structure!.payload.sequenceFlows).toBeUndefined();
    expect(next.structure!.payload.diagram).toBeUndefined();
  });

  it("preserves binding intent and complete child content on notation replacement", () => {
    const next = command({ kind: "replaceType", target: address("Task"), localName: "userTask" });
    expect(element(next, "Task").localName).toBe("userTask");
    expect(element(next, "Task").getElementsByTagNameNS(bpmnNamespaces.model, "multiInstanceLoopCharacteristics")).toHaveLength(1);
    expect(next.structure!.payload.bindings).toEqual(owner().structure!.payload.bindings);
    expect(element(next, "Task").getAttributeNS("urn:vendor", "unknown")).toBe("untouched");
  });

  it("reconnects sequence refs together and carries reference metadata to the new endpoint", () => {
    const next = command({ kind: "reconnect", elementId: "Flow", sourceRef: "Start", targetRef: "Nested" });
    expect(element(next, "Flow").getAttribute("targetRef")).toBe("Nested");
    expect(element(next, "Task").getElementsByTagNameNS(bpmnNamespaces.model, "incoming")).toHaveLength(0);
    const incoming = element(next, "Nested").getElementsByTagNameNS(bpmnNamespaces.model, "incoming")[0];
    expect(incoming.textContent).toBe("Flow");
    expect(incoming.getAttributeNS("urn:vendor", "note")).toBe("keep");
    expect(element(next, "Start").getElementsByTagNameNS(bpmnNamespaces.model, "outgoing")).toHaveLength(1);
  });

  it("keeps cross-pool message flows distinct from sequence flows", () => {
    const next = command({ kind: "reconnect", elementId: "MessageFlow", sourceRef: "Pool", targetRef: "OtherPool" });
    expect(element(next, "MessageFlow").getAttribute("sourceRef")).toBe("Pool");
    expect(element(next, "Pool").children).toHaveLength(0);
    expect(() => command({ kind: "reconnect", elementId: "Flow", sourceRef: "Task", targetRef: "OtherTask" })).toThrow("one standard scope");
  });

  it("projects complete DI with exact geometry and typed artifact/message connections", () => {
    const scene = projectBpmnDocumentScene(owner(), "Diagram");
    expect(scene.scopeId).toBe("Collaboration");
    expect(scene.shapes.map(shape => shape.elementType)).toEqual(["participant", "serviceTask", "boundaryEvent"]);
    expect(scene.shapes[1].bounds).toEqual({ x: 200, y: 80, width: 100, height: 80 });
    expect(scene.shapes[1].labelBounds).toEqual({ x: 180, y: 170, width: 140, height: 20 });
    expect(scene.edges.map(edge => edge.elementType)).toEqual(["sequenceFlow", "messageFlow", "association"]);
    expect(scene.edges[1].points).toEqual([{ x: 250, y: 160 }, { x: 250, y: 400 }]);
    expect(scene.shapes[2].element.getAttribute("attachedToRef")).toBe("Task");
  });

  it("resolves local target-namespace QNames while preserving their lexical source", () => {
    const source = xml.replace('xmlns:vendor="urn:vendor"', 'xmlns:vendor="urn:vendor" xmlns:tns="urn:test"')
      .replaceAll('bpmnElement="Task"', 'bpmnElement="tns:Task"')
      .replace('bpmnElement="Collaboration"', 'bpmnElement="tns:Collaboration"')
      .replace('sourceRef="Task" targetRef="Note"', 'sourceRef="tns:Task" targetRef="tns:Note"');
    const scene = projectBpmnDocumentScene(owner(source), "Diagram");
    expect(scene.scopeId).toBe("Collaboration");
    expect(scene.shapes[1].elementId).toBe("Task");
    expect(scene.edges[2].sourceRef).toBe("Task");
    expect(readBpmnDocument(owner(source)).payload.documentXml).toBe(source);
    expect(() => applyBpmnDocumentCommands(owner(source), [{ kind: "remove", target: address("Note") }])).toThrow("reference");
  });

  it("moves qualified incoming/outgoing references on reconnect and refuses dangling QName text on deletion", () => {
    const source = xml.replace('xmlns:vendor="urn:vendor"', 'xmlns:vendor="urn:vendor" xmlns:tns="urn:test"').replaceAll(">Flow<", ">tns:Flow<");
    const next = applyBpmnDocumentCommands(owner(source), [{ kind: "reconnect", elementId: "Flow", sourceRef: "Start", targetRef: "Nested" }]);
    expect(element(next, "Task").getElementsByTagNameNS(bpmnNamespaces.model, "incoming")).toHaveLength(0);
    const incoming = element(next, "Nested").getElementsByTagNameNS(bpmnNamespaces.model, "incoming")[0];
    expect(incoming.textContent).toBe("tns:Flow");
    expect(incoming.lookupNamespaceURI("tns")).toBe("urn:test");
    expect(() => applyBpmnDocumentCommands(owner(source), ["FlowEdge", "Flow"].map(elementId => ({ kind: "remove", target: address(elementId) })))).toThrow("outgoing");
  });

  it.each(["Flow", "tns:Flow"])("clears the retained default link %s when reconnecting away from its owner", defaultFlow => {
    const source = xml.replace('xmlns:vendor="urn:vendor"', 'xmlns:vendor="urn:vendor" xmlns:tns="urn:test"')
      .replace('<bpmn:serviceTask id="Task"', `<bpmn:serviceTask default="${defaultFlow}" id="Task"`)
      .replace('sourceRef="Start" targetRef="Task"', 'sourceRef="Task" targetRef="Nested"')
      .replace('<bpmn:incoming vendor:note="keep">Flow</bpmn:incoming>', '<bpmn:outgoing vendor:note="keep">Flow</bpmn:outgoing>')
      .replace('<bpmn:outgoing>Flow</bpmn:outgoing>', '');
    const next = applyBpmnDocumentCommands(owner(source), [{ kind: "reconnect", elementId: "Flow", sourceRef: "Start", targetRef: "Nested" }]);
    expect(element(next, "Task").hasAttribute("default")).toBe(false);
  });

  it("preserves inherited namespace meaning when moving a subtree across conflicting declarations", () => {
    const source = xml.replace('<bpmn:process id="OtherProcess">', '<bpmn:process xmlns:types="urn:new" id="OtherProcess">')
      .replace('<bpmn:subProcess id="Nested"', '<bpmn:subProcess xmlns:types="urn:old" id="Nested"')
      .replace('<bpmn:startEvent id="NestedStart"', '<bpmn:startEvent implementation="types:Service" id="NestedStart"');
    const next = applyBpmnDocumentCommands(owner(source), [{ kind: "reparent", target: address("NestedStart"), parent: address("OtherProcess") }]);
    const moved = element(next, "NestedStart");
    expect(moved.parentElement?.getAttribute("id")).toBe("OtherProcess");
    expect(moved.getAttribute("implementation")).toBe("types:Service");
    expect(moved.lookupNamespaceURI("types")).toBe("urn:old");
    expect(element(next, "OtherProcess").lookupNamespaceURI("types")).toBe("urn:new");
  });

  it("preserves an absent default namespace for unprefixed vendor QNames after reparenting", () => {
    const source = xml.replace('<bpmn:process id="OtherProcess">', '<bpmn:process xmlns="urn:new" id="OtherProcess">')
      .replace('<bpmn:startEvent id="NestedStart"', '<bpmn:startEvent vendor:typeRef="Service" id="NestedStart"');
    expect(element(owner(source), "NestedStart").lookupNamespaceURI(null)).toBeNull();
    const next = applyBpmnDocumentCommands(owner(source), [{ kind: "reparent", target: address("NestedStart"), parent: address("OtherProcess") }]);
    expect(element(next, "NestedStart").lookupNamespaceURI(null)).toBeNull();
    expect(element(next, "NestedStart").getAttributeNS("urn:vendor", "typeRef")).toBe("Service");
  });

  it("keeps the original BPMN prefix on type replacement and refuses a conflicting new attribute prefix", () => {
    const source = xml.replaceAll("bpmn:", "m:").replace("xmlns:bpmn=", "xmlns:m=")
      .replace('xmlns:vendor="urn:vendor"', 'xmlns:vendor="urn:vendor" xmlns:bpmn="urn:foreign"');
    const next = applyBpmnDocumentCommands(owner(source), [{ kind: "replaceType", target: address("Task"), localName: "userTask" }]);
    expect(element(next, "Task").prefix).toBe("m");
    expect(element(next, "Task").lookupNamespaceURI("bpmn")).toBe("urn:foreign");
    expect(() => command({ kind: "attribute", target: address("Task"), name: "vendor:new", namespaceURI: "urn:changed", value: "new" })).toThrow("QName namespace context");
  });

  it("projects data-association child references and all semantic sources", () => {
    const source = xml.replace('<bpmn:serviceTask id="Task"', '<bpmn:dataObject id="Data"/><bpmn:dataObjectReference id="DataRef" dataObjectRef="Data"/><bpmn:dataObjectReference id="DataRef2" dataObjectRef="Data"/><bpmn:serviceTask id="Task"')
      .replace('</bpmn:serviceTask>', '<bpmn:ioSpecification><bpmn:dataInput id="Input"/></bpmn:ioSpecification><bpmn:dataInputAssociation id="InputAssociation"><bpmn:sourceRef>DataRef</bpmn:sourceRef><bpmn:sourceRef>DataRef2</bpmn:sourceRef><bpmn:targetRef>Input</bpmn:targetRef></bpmn:dataInputAssociation></bpmn:serviceTask>')
      .replace('</bpmndi:BPMNPlane>', '<bpmndi:BPMNEdge id="InputEdge" bpmnElement="InputAssociation"><di:waypoint x="100" y="100"/><di:waypoint x="200" y="100"/></bpmndi:BPMNEdge></bpmndi:BPMNPlane>');
    const association = projectBpmnDocumentScene(owner(source), "Diagram").edges.find(edge => edge.elementId === "InputAssociation")!;
    expect(association.elementType).toBe("dataInputAssociation");
    expect(association.sourceRefs).toEqual(["DataRef", "DataRef2"]);
    expect(association.targetRefs).toEqual(["Input"]);
    expect(association.targetRef).toBe("Input");
  });

  it("writes only selected geometry, preserving waypoint metadata and unrelated labels/routes", () => {
    const original = owner();
    const next = applyBpmnDocumentCommands(original, [
      { kind: "bounds", shapeId: "TaskShape", bounds: { x: 300, y: 90, width: 120, height: 100 } },
      { kind: "waypoints", edgeId: "FlowEdge", points: [{ x: 110, y: 130 }, { x: 200, y: 130 }, { x: 300, y: 140 }] }
    ]);
    const scene = projectBpmnDocumentScene(next, "Diagram");
    expect(scene.shapes[1].bounds.x).toBe(300);
    expect(scene.shapes[1].labelBounds).toEqual(projectBpmnDocumentScene(original, "Diagram").shapes[1].labelBounds);
    expect(element(next, "MessageEdge").outerHTML).toBe(element(original, "MessageEdge").outerHTML);
    expect(element(next, "FlowEdge").getAttributeNS("urn:vendor", "routing")).toBe("retain");
    expect(element(next, "FlowEdge").children[0].getAttributeNS("urn:vendor", "point")).toBe("retain");
    expect(scene.edges[0].points).toHaveLength(3);
  });

  it("creates and reparents standard syntax in one transaction with identity indexing refreshed", () => {
    const next = applyBpmnDocumentCommands(owner(), [
      { kind: "append", parent: address("Process"), xml: `<bpmn:manualTask xmlns:bpmn="${bpmnNamespaces.model}" id="NewTask"/>` },
      { kind: "attribute", target: address("NewTask"), name: "name", value: "Manual work" },
      { kind: "reparent", target: address("NewTask"), parent: address("Nested") }
    ]);
    expect(element(next, "NewTask").parentElement?.getAttribute("id")).toBe("Nested");
    expect(element(next, "NewTask").getAttribute("name")).toBe("Manual work");
  });

  it.each(["elements", "sequenceFlows", "diagram", "messageFlows"])("rejects competing persisted %s authority", field => {
    const original = owner(); original.structure!.payload[field] = [];
    expect(() => readBpmnDocument(original)).toThrow("competing graph or diagram");
  });

  it.each([
    ["duplicate XML identity", xml.replace('id="OtherTask"', 'id="Task"'), "duplicated"],
    ["missing selected process", xml.replace('id="Process"', 'id="Missing"'), "selected process"],
    ["malformed XML", "<definitions", "malformed"],
    ["DTD", '<!DOCTYPE definitions SYSTEM "https://example.invalid/external">' + xml, "DTD"],
    ["non-BPMN root", '<definitions xmlns="urn:wrong"/>', "BPMN definitions"]
  ])("refuses %s without mutation", (_label, source, message) => {
    const original = owner(source); const before = structuredClone(original);
    expect(() => readBpmnDocument(original)).toThrow(message);
    expect(original).toEqual(before);
  });

  it.each([
    { kind: "attribute", target: address("Task"), name: "id", value: "Changed" },
    { kind: "text", target: address("Task"), value: "would drop configuration" },
    { kind: "remove", target: address("Task") },
    { kind: "remove", target: address("Note") },
    { kind: "reparent", target: address("Nested"), parent: address("NestedStart") },
    { kind: "bounds", shapeId: "TaskShape", bounds: { x: NaN, y: 0, width: 1, height: 1 } },
    { kind: "waypoints", edgeId: "FlowEdge", points: [] },
    { kind: "append", parent: address("Process"), xml: `<bpmn:task xmlns:bpmn="${bpmnNamespaces.model}" id="Task"/>` }
  ] satisfies BpmnDocumentCommand[])("rejects unsafe $kind transactions atomically", unsafe => {
    const original = owner(); const before = structuredClone(original);
    expect(() => applyBpmnDocumentCommands(original, [
      { kind: "attribute", target: address("Task"), name: "name", value: "Transient" }, unsafe
    ])).toThrow();
    expect(original).toEqual(before);
  });

  it("allows explicit deletion of an artifact and its referencing association in one transaction", () => {
    const next = applyBpmnDocumentCommands(owner(), ["AssociationEdge", "Association", "Note"].map(elementId => ({ kind: "remove", target: address(elementId) })));
    expect(readBpmnDocument(next).byId.has("Note")).toBe(false);
    expect(readBpmnDocument(next).byId.has("Association")).toBe(false);
  });
});
