// @vitest-environment jsdom
import { describe, expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentCommands, bpmnNamespaces, projectBpmnDocumentScene, readBpmnDocument, resolveBpmnDocumentReference, type BpmnDocumentCommand } from "../bpmn/bpmnDocument";

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
function dataDocument() {
  return xml.replace('<bpmn:serviceTask id="Task"', '<bpmn:dataObject id="Data"/><bpmn:dataObjectReference id="DataRef" dataObjectRef="Data"/><bpmn:dataObjectReference id="DataRef2" dataObjectRef="Data"/><bpmn:serviceTask id="Task"')
    .replace('</bpmn:serviceTask>', '<bpmn:ioSpecification><bpmn:dataInput id="Input"/><bpmn:dataInput id="Input2"/></bpmn:ioSpecification><bpmn:dataInputAssociation id="InputAssociation"><bpmn:sourceRef>DataRef</bpmn:sourceRef><bpmn:sourceRef>DataRef2</bpmn:sourceRef><bpmn:targetRef>Input</bpmn:targetRef><bpmn:transformation language="urn:expression">keepTransformation()</bpmn:transformation></bpmn:dataInputAssociation></bpmn:serviceTask>')
    .replace('</bpmndi:BPMNPlane>', '<bpmndi:BPMNEdge id="InputEdge" bpmnElement="InputAssociation"><di:waypoint x="100" y="100"/><di:waypoint x="200" y="100"/></bpmndi:BPMNEdge></bpmndi:BPMNPlane>');
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
  it("does not interpret an unbound prefix as a local reference when the draft has no target namespace", () => {
    const original = owner(xml.replace(' targetNamespace="urn:test"', ""));
    const parsed = readBpmnDocument(original);
    const task = parsed.byId.get("Task")!;
    expect(resolveBpmnDocumentReference(parsed, task, "Task")).toBe(task);
    expect(resolveBpmnDocumentReference(parsed, task, "unbound:Task")).toBeUndefined();
    expect(original.structure!.payload.documentXml).toBe(parsed.payload.documentXml);
  });

  it.each([["missing:Loose", false], ["local:Loose:extra", true]])("does not infer a local deletion reference from opaque QName %s", (reference, hasNamespace) => {
    const source = xml.replace(' id="Definitions"', ' xmlns:local="urn:test" id="Definitions"')
      .replace('<bpmn:startEvent id="Start"', `<bpmn:callActivity id="Caller" calledElement="${reference}"/><bpmn:task id="Loose"/><bpmn:startEvent id="Start"`);
    const original = owner(hasNamespace ? source : source.replace(' targetNamespace="urn:test"', ""));
    const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Loose") }]);
    expect(readBpmnDocument(next).byId.has("Loose")).toBe(false);
    expect(element(next, "Caller").getAttribute("calledElement")).toBe(reference);
    expect(readBpmnDocument(original).byId.has("Loose")).toBe(true);
  });

  it.each(["Loose", "local:Loose"])("still refuses deletion through an actual local reference %s", reference => {
    const source = xml.replace(' id="Definitions"', ' xmlns:local="urn:test" id="Definitions"')
      .replace('<bpmn:startEvent id="Start"', `<bpmn:callActivity id="Caller" calledElement="${reference}"/><bpmn:task id="Loose"/><bpmn:startEvent id="Start"`);
    expect(() => applyBpmnDocumentCommands(owner(source), [{ kind: "remove", target: address("Loose") }])).toThrow("reference");
  });

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

  it("retains compact nested and independent scope headers with exact native bindings", () => {
    const original = owner();
    original.structure!.payload.scopeHeaders = [
      { scopeElementId: "Nested", nodeId: "nested-scope", activityVersionId: "exact-process-version", inputs: [], outputs: [], variables: [], futureMetadata: { keep: true } },
      { scopeElementId: "OtherProcess", nodeId: "other-scope", activityVersionId: "exact-process-version", inputs: [], outputs: [], variables: [] }
    ];
    (original.structure!.payload.activities as ActivityNode[]).push(
      { nodeId: "nested-listener", activityVersionId: "exact-listener", inputs: [], outputs: [] },
      { nodeId: "other-work", activityVersionId: "exact-receive", inputs: [], outputs: [] });
    (original.structure!.payload.bindings as unknown[]).push({ elementId: "NestedStart", listenerNodeId: "nested-listener" }, { elementId: "OtherTask", childNodeId: "other-work" });
    const next = applyBpmnDocumentCommands(original, [{ kind: "attribute", target: address("OtherTask"), name: "name", value: "Other process work" }]);
    expect(next.structure!.payload.scopeHeaders).toBe(original.structure!.payload.scopeHeaders);
    expect(next.structure!.payload.activities).toBe(original.structure!.payload.activities);
    expect(readBpmnDocument(next).payload.bindings).toEqual(original.structure!.payload.bindings);
    expect(element(next, "OtherTask").getAttribute("name")).toBe("Other process work");
  });

  it("requires complete native scope identities without persisting generated source views", () => {
    const original = owner();
    original.structure!.payload.scopeHeaders = [{ scopeElementId: "Nested", nodeId: "nested-scope", activityVersionId: "exact-process-version", inputs: [], outputs: [], variables: [], structure: { kind: "elsa.bpmn.scope-view" } }];
    expect(() => readBpmnDocument(original)).toThrow("second structure");
    const missing = owner();
    (missing.structure!.payload.activities as ActivityNode[]).push({ nodeId: "nested-listener", activityVersionId: "exact-listener", inputs: [], outputs: [] });
    (missing.structure!.payload.bindings as unknown[]).push({ elementId: "NestedStart", listenerNodeId: "nested-listener" });
    expect(() => readBpmnDocument(missing)).toThrow("scope header chain");
  });

  it.each([false, true])("rejects actual physical derived structures, including native descendants (nested=%s)", nested => {
    const original = owner();
    const body = (original.structure!.payload.activities as ActivityNode[])[0];
    const view = { nodeId: nested ? "leaf" : "body", activityVersionId: "exact-process", inputs: [], outputs: [],
      structure: { kind: "elsa.bpmn.scope-view", schemaVersion: "1.0.0", payload: {} } };
    if (nested) body.structure!.payload.activities = [view];
    else (original.structure!.payload.activities as ActivityNode[])[0] = view;
    const before = structuredClone(original);
    expect(() => readBpmnDocument(original)).toThrow("projected scope");
    expect(original).toEqual(before);
  });

  it("preserves reserved-looking literal inputs and native metadata without treating them as activity positions", () => {
    const original = owner();
    const body = (original.structure!.payload.activities as ActivityNode[])[0];
    const opaque = { kind: "elsa.bpmn.scope-view", structure: { kind: "elsa.bpmn.scope-view" } };
    body.inputs.push({ referenceKey: "literal", value: { expressionType: "Literal", value: opaque } });
    body.moduleMetadata = opaque;
    body.structure!.payload.opaque = opaque;
    const next = applyBpmnDocumentCommands(original, [{ kind: "attribute", target: address("Task"), name: "name", value: "Edited" }]);
    expect(next.structure!.payload.activities).toBe(original.structure!.payload.activities);
  });

  it.each(["vendor", "extension", "standard"])("rejects configured scope headers below %s wrappers while preserving unconfigured XML", wrapper => {
    const hidden = '<bpmn:subProcess id="Hidden"><bpmn:task id="HiddenTask"/></bpmn:subProcess>';
    const fragment = wrapper === "vendor" ? `<bpmn:extensionElements><vendor:opaque>${hidden}</vendor:opaque></bpmn:extensionElements>`
      : wrapper === "extension" ? `<bpmn:extensionElements>${hidden}</bpmn:extensionElements>` : `<bpmn:dataObject id="OpaqueData">${hidden}</bpmn:dataObject>`;
    const original = owner(xml.replace('<bpmn:startEvent id="Start">', `${fragment}<bpmn:startEvent id="Start">`));
    expect(readBpmnDocument(original).payload.documentXml).toBe(original.structure!.payload.documentXml);
    const unconfigured = applyBpmnDocumentCommands(original, [{ kind: "attribute", target: address("Task"), name: "name", value: "Edited" }]);
    expect(element(unconfigured, "Hidden").outerHTML).toBe(element(original, "Hidden").outerHTML);
    original.structure!.payload.scopeHeaders = [{ scopeElementId: "Hidden", nodeId: "hidden-scope", activityVersionId: "exact-process", inputs: [], outputs: [], variables: [] }];
    (original.structure!.payload.activities as ActivityNode[]).push({ nodeId: "hidden-native", activityVersionId: "exact-work", inputs: [], outputs: [] });
    (original.structure!.payload.bindings as unknown[]).push({ elementId: "HiddenTask", childNodeId: "hidden-native" });
    expect(() => readBpmnDocument(original)).toThrow("standard scope chain");
  });

  it.each(["documentXml", "processId", "activities", "bindings", "scopeHeaders"])("rejects a second %s authority in compact scope configuration", field => {
    const original = owner();
    original.structure!.payload.scopeHeaders = [{ scopeElementId: "Nested", nodeId: "nested-scope", activityVersionId: "exact-process", inputs: [], outputs: [], variables: [], [field]: [] }];
    expect(() => readBpmnDocument(original)).toThrow("second structure");
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

  it("creates new sequence references in the destination XML context instead of moving an orphan context", () => {
    const source = xml.replace('<bpmn:process id="Process"', '<bpmn:process xml:base="https://example.test/model/" xml:lang="nl" xml:space="preserve" id="Process"')
      .replace('<bpmn:outgoing>Flow</bpmn:outgoing>', '').replace('<bpmn:incoming vendor:note="keep">Flow</bpmn:incoming>', '');
    const original = owner(source);
    const next = applyBpmnDocumentCommands(original, [{ kind: "reconnect", elementId: "Flow", sourceRef: "Start", targetRef: "Nested" }]);
    for (const [id, name] of [["Start", "outgoing"], ["Nested", "incoming"]]) {
      const reference = element(next, id).getElementsByTagNameNS(bpmnNamespaces.model, name)[0];
      expect(reference.textContent).toBe("Flow");
      for (const localName of ["base", "lang", "space"]) expect(reference.hasAttributeNS("http://www.w3.org/XML/1998/namespace", localName)).toBe(false);
    }
    expect(original.structure!.payload.documentXml).toBe(source);
    expect(retainedXml(next)).toBe(retainedXml(original));
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
    expect(() => command({ kind: "attribute", target: address("Task"), name: "vendor:new", namespaceURI: "urn:changed", value: "new" })).toThrow("explicitly declared matching prefix");
  });

  it.each(['<!-- vendor marker -->', '<?vendor preserve?>', '<vendor:keep/>'])("refuses losing opaque data target content %s", retained => {
    const original = owner(dataDocument().replace('<bpmn:targetRef>Input</bpmn:targetRef>', `<bpmn:targetRef>${retained}Input</bpmn:targetRef>`));
    const before = structuredClone(original);
    expect(() => applyBpmnDocumentCommands(original, [{ kind: "dataReferences", elementId: "InputAssociation", sourceRefs: ["DataRef"], targetRef: "Input2" }])).toThrow("retained");
    expect(original).toEqual(before);
  });

  it("projects data-association child references and all semantic sources", () => {
    const association = projectBpmnDocumentScene(owner(dataDocument()), "Diagram").edges.find(edge => edge.elementId === "InputAssociation")!;
    expect(association.elementType).toBe("dataInputAssociation");
    expect(association.sourceRefs).toEqual(["DataRef", "DataRef2"]);
    expect(association.targetRefs).toEqual(["Input"]);
    expect(association.targetRef).toBe("Input");
  });

  it("edits every data-association source and the real I/O target while retaining transformations", () => {
    const original = owner(dataDocument());
    const next = applyBpmnDocumentCommands(original, [{ kind: "dataReferences", elementId: "InputAssociation", sourceRefs: ["DataRef2"], targetRef: "Input2" }]);
    const association = projectBpmnDocumentScene(next, "Diagram").edges.find(edge => edge.elementId === "InputAssociation")!;
    expect(association.sourceRefs).toEqual(["DataRef2"]);
    expect(association.targetRefs).toEqual(["Input2"]);
    expect(element(next, "InputAssociation").getElementsByTagNameNS(bpmnNamespaces.model, "transformation")[0].outerHTML)
      .toBe(element(original, "InputAssociation").getElementsByTagNameNS(bpmnNamespaces.model, "transformation")[0].outerHTML);
    expect(() => applyBpmnDocumentCommands(original, [{ kind: "dataReferences", elementId: "InputAssociation", sourceRefs: ["DataRef"], targetRef: "Task" }])).toThrow("item-aware");
    expect(applyBpmnDocumentCommands(original, [{ kind: "dataReferences", elementId: "InputAssociation", sourceRefs: ["DataRef", "DataRef2"], targetRef: "Input" }])).toBe(original);
  });

  it("moves and resets only label bounds while retaining label metadata and unrelated geometry", () => {
    const source = xml.replace('<bpmndi:BPMNLabel>', '<bpmndi:BPMNLabel vendor:retained="label">');
    const original = owner(source);
    const next = applyBpmnDocumentCommands(original, [
      { kind: "labelBounds", diId: "TaskShape", bounds: { x: 250, y: 300, width: 140, height: 30 } },
      { kind: "labelBounds", diId: "FlowEdge", bounds: { x: 150, y: 90, width: 80, height: 20 } }
    ]);
    expect(projectBpmnDocumentScene(next, "Diagram").shapes[1].labelBounds).toEqual({ x: 250, y: 300, width: 140, height: 30 });
    expect(element(next, "TaskShape").getElementsByTagNameNS(bpmnNamespaces.diagram, "BPMNLabel")[0].getAttributeNS("urn:vendor", "retained")).toBe("label");
    expect(element(next, "FlowEdge").getElementsByTagNameNS(bpmnNamespaces.coordinates, "waypoint")[0].outerHTML)
      .toBe(element(original, "FlowEdge").getElementsByTagNameNS(bpmnNamespaces.coordinates, "waypoint")[0].outerHTML);
    const reset = applyBpmnDocumentCommands(next, [{ kind: "labelBounds", diId: "TaskShape", bounds: null }]);
    expect(projectBpmnDocumentScene(reset, "Diagram").shapes[1].labelBounds).toBeUndefined();
    expect(element(reset, "TaskShape").getElementsByTagNameNS(bpmnNamespaces.diagram, "BPMNLabel")[0].getAttributeNS("urn:vendor", "retained")).toBe("label");
  });

  it("inserts a standard property before its declared sibling without rebuilding other children", () => {
    const next = command({ kind: "insert", parent: address("Task"), xml: `<bpmn:ioSpecification xmlns:bpmn="${bpmnNamespaces.model}" id="IO"/>`,
      before: { elementId: "Task", path: [{ namespaceURI: bpmnNamespaces.model, localName: "multiInstanceLoopCharacteristics" }] } });
    expect([...element(next, "Task").children].map(child => child.localName)).toEqual(["incoming", "ioSpecification", "multiInstanceLoopCharacteristics"]);
    expect(() => command({ kind: "insert", parent: address("Nested"), xml: `<bpmn:task xmlns:bpmn="${bpmnNamespaces.model}" id="New"/>`, before: address("Task") })).toThrow("direct child");
  });

  it("carries inherited absolute XML base, language and whitespace context across reparenting", () => {
    const source = xml.replace('<bpmn:process id="Process"', '<bpmn:process xml:base="https://source.example/models/" xml:lang="en" xml:space="preserve" id="Process"')
      .replace('<bpmn:subProcess id="Nested"', '<bpmn:subProcess xml:base="nested/" id="Nested"')
      .replace('<bpmn:process id="OtherProcess"', '<bpmn:process xml:base="https://other.example/" xml:lang="fr" id="OtherProcess"');
    const next = applyBpmnDocumentCommands(owner(source), [{ kind: "reparent", target: address("NestedStart"), parent: address("OtherProcess") }]);
    const moved = element(next, "NestedStart");
    expect(moved.getAttributeNS("http://www.w3.org/XML/1998/namespace", "base")).toBe("https://source.example/models/nested/");
    expect(moved.getAttributeNS("http://www.w3.org/XML/1998/namespace", "lang")).toBe("en");
    expect(moved.getAttributeNS("http://www.w3.org/XML/1998/namespace", "space")).toBe("preserve");
  });

  it("refuses a move whose inherited relative base cannot be retained without a source URI", () => {
    const original = owner(xml.replace('<bpmn:process id="OtherProcess"', '<bpmn:process xml:base="https://other.example/" id="OtherProcess"'));
    const before = structuredClone(original);
    expect(() => applyBpmnDocumentCommands(original, [{ kind: "reparent", target: address("NestedStart"), parent: address("OtherProcess") }])).toThrow("unresolved XML base");
    expect(original).toEqual(before);
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

  it.each(["A\u00b7B", "A\u203fB", "A\u2040B", "xmlns"])("retains valid NCName identity %s without renaming", id => {
    const source = xml.replace('id="Note"', `id="${id}"`).replace('targetRef="Note"', `targetRef="${id}"`);
    const original = owner(source);
    expect(readBpmnDocument(original).byId.get(id)?.getAttribute("id")).toBe(id);
    const next = applyBpmnDocumentCommands(original, [{ kind: "attribute", target: address("Task"), name: "name", value: "Edited" }]);
    expect(element(next, id).getAttribute("id")).toBe(id);
    expect(element(next, "Association").getAttribute("targetRef")).toBe(id);
    expect(original.structure!.payload.documentXml).toBe(source);
  });

  it.each(["bad:name", "bad name", "1invalid", "\u203fstart"])("rejects invalid NCName identity %s without mutation", id => {
    const original = owner(xml.replace('id="Note"', `id="${id}"`)), before = structuredClone(original);
    expect(() => readBpmnDocument(original)).toThrow("XML ID");
    expect(original).toEqual(before);
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

  it("does not mistake standard names or opaque vendor values for references when deleting an artifact", () => {
    const source = xml.replace('name="Work"', 'name="Note" vendor:targetRef="Note"')
      .replace('token="original"', 'token="Note"');
    const original = owner(source);
    const next = applyBpmnDocumentCommands(original, ["AssociationEdge", "Association", "Note"].map(elementId => ({ kind: "remove", target: address(elementId) })));
    expect(readBpmnDocument(next).byId.has("Note")).toBe(false);
    expect(element(next, "Task").getAttribute("name")).toBe("Note");
    expect(element(next, "Task").getAttributeNS("urn:vendor", "targetRef")).toBe("Note");
    expect(retainedXml(next)).toBe(retainedXml(original));
  });

  it("refuses deletion of a data input retained by a standard plural IDREF property", () => {
    const source = xml.replace('</bpmn:serviceTask>', '<bpmn:ioSpecification><bpmn:dataInput id="LooseInput"/><bpmn:inputSet><bpmn:dataInputRefs>LooseInput</bpmn:dataInputRefs></bpmn:inputSet></bpmn:ioSpecification></bpmn:serviceTask>');
    const original = owner(source);
    expect(() => applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("LooseInput") }])).toThrow("dataInputRefs");
    expect(original.structure!.payload.documentXml).toBe(source);
  });
});
