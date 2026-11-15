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

// R7: schema-declared reference roles, distinct from opaque namespace lookalikes.
function r7FormalOwner(processFragment = "", definitionsFragment = ""): ActivityNode {
  return owner(`<bpmn:definitions xmlns:bpmn="${bpmnNamespaces.model}" xmlns:tns="urn:test" xmlns:ext="urn:external" xmlns:vendor="urn:vendor" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" targetNamespace="urn:test">
    <bpmn:itemDefinition id="Item"/><bpmn:message id="FormalMessage"/>
    ${definitionsFragment}<bpmn:process id="Process"><bpmn:task id="Task"/>${processFragment}</bpmn:process>
  </bpmn:definitions>`);
}
function r7InputTransformationOwner(): ActivityNode {
  return r7FormalOwner('<bpmn:task id="FormalTask"><bpmn:ioSpecification><bpmn:dataInput id="FormalInput"/><bpmn:inputSet/><bpmn:outputSet/></bpmn:ioSpecification><bpmn:dataInputAssociation id="FormalAssociation"><bpmn:targetRef>FormalInput</bpmn:targetRef><bpmn:transformation id="Formal" evaluatesToTypeRef="tns:Item">value</bpmn:transformation></bpmn:dataInputAssociation></bpmn:task>');
}
function r7ExpectFormalDeletionRefused(original: ActivityNode, commands: BpmnDocumentCommand[] = [{ kind: "remove", target: address("Item") }]) {
  const before = structuredClone(original), parsed = readBpmnDocument(original), formal = parsed.byId.get("Formal")!;
  expect(formal.namespaceURI).toBe(bpmnNamespaces.model);
  expect(formal.hasAttributeNS("http://www.w3.org/2001/XMLSchema-instance", "type")).toBe(false);
  expect(resolveBpmnDocumentReference(parsed, formal, formal.getAttribute("evaluatesToTypeRef"))).toBe(parsed.byId.get("Item"));
  expect(() => {
    try { applyBpmnDocumentCommands(original, commands); }
    finally { expect(original).toEqual(before); }
  }).toThrow("deletion leaves reference 'evaluatesToTypeRef'");
}
function r7IoBindingOwner(): ActivityNode {
  return owner(`<bpmn:definitions xmlns:bpmn="${bpmnNamespaces.model}" xmlns:tns="urn:binding" xmlns:vendor="urn:vendor" targetNamespace="urn:binding">
    <bpmn:message id="BindingMessage"/>
    <bpmn:interface id="BindingInterface" name="Binding"><bpmn:operation id="BindingOperation" name="Invoke"><bpmn:inMessageRef>tns:BindingMessage</bpmn:inMessageRef></bpmn:operation></bpmn:interface>
    <bpmn:process id="Process"><bpmn:ioSpecification id="BindingIo"><bpmn:dataInput id="BindingInput"/><bpmn:dataOutput id="BindingOutput"/><bpmn:inputSet id="BindingInputSet"/><bpmn:outputSet id="BindingOutputSet"/></bpmn:ioSpecification>
      <bpmn:ioBinding id="Binding" operationRef="tns:BindingOperation" inputDataRef="BindingInput" outputDataRef="BindingOutput"/><bpmn:task id="Task"/>
    </bpmn:process></bpmn:definitions>`);
}
function r7ExpectIoDeletionRefused(target: string, attribute: string, atomic = false) {
  const original = r7IoBindingOwner(), before = structuredClone(original), parsed = readBpmnDocument(original), binding = parsed.byId.get("Binding")!;
  expect(binding.localName).toBe("ioBinding");
  expect(resolveBpmnDocumentReference(parsed, binding, binding.getAttribute(attribute))).toBe(parsed.byId.get(target));
  const commands: BpmnDocumentCommand[] = [...(atomic ? [{ kind: "attribute" as const, target: address("Task"), name: "name", value: "Uncommitted" }] : []), { kind: "remove", target: address(target) }];
  expect(() => {
    try { applyBpmnDocumentCommands(original, commands); }
    finally { expect(original).toEqual(before); }
  }).toThrow(`deletion leaves reference '${attribute}'`);
}

describe("R7 standard BPMN reference deletion regressions", () => {
  it("refuses deletion through a schema-declared complex behavior condition", () => {
    r7ExpectFormalDeletionRefused(r7FormalOwner('<bpmn:task id="FormalTask"><bpmn:multiInstanceLoopCharacteristics><bpmn:complexBehaviorDefinition><bpmn:condition id="Formal" evaluatesToTypeRef="tns:Item">ready</bpmn:condition></bpmn:complexBehaviorDefinition></bpmn:multiInstanceLoopCharacteristics></bpmn:task>'));
  });
  it("refuses deletion through a schema-declared correlation data path", () => {
    r7ExpectFormalDeletionRefused(r7FormalOwner('<bpmn:correlationSubscription correlationKeyRef="ext:Key"><bpmn:correlationPropertyBinding correlationPropertyRef="ext:Property"><bpmn:dataPath id="Formal" evaluatesToTypeRef="tns:Item">value</bpmn:dataPath></bpmn:correlationPropertyBinding></bpmn:correlationSubscription>'));
  });
  it("refuses deletion through a schema-declared correlation message path", () => {
    r7ExpectFormalDeletionRefused(r7FormalOwner("", '<bpmn:correlationProperty id="Property"><bpmn:correlationPropertyRetrievalExpression messageRef="tns:FormalMessage"><bpmn:messagePath id="Formal" evaluatesToTypeRef="tns:Item">value</bpmn:messagePath></bpmn:correlationPropertyRetrievalExpression></bpmn:correlationProperty>'));
  });
  it("refuses deletion through the standard data association transformation type", () => {
    r7ExpectFormalDeletionRefused(r7FormalOwner('<bpmn:dataObject id="FormalData"/><bpmn:dataAssociation><bpmn:targetRef>FormalData</bpmn:targetRef><bpmn:transformation id="Formal" evaluatesToTypeRef="tns:Item">value</bpmn:transformation></bpmn:dataAssociation>'));
  });
  it("refuses deletion through a schema-declared input association transformation", () => {
    r7ExpectFormalDeletionRefused(r7InputTransformationOwner());
  });
  it("refuses deletion through a schema-declared output association transformation", () => {
    r7ExpectFormalDeletionRefused(r7FormalOwner('<bpmn:dataObject id="FormalData"/><bpmn:task id="FormalTask"><bpmn:ioSpecification><bpmn:dataOutput id="FormalOutput"/><bpmn:inputSet/><bpmn:outputSet/></bpmn:ioSpecification><bpmn:dataOutputAssociation><bpmn:sourceRef>FormalOutput</bpmn:sourceRef><bpmn:targetRef>FormalData</bpmn:targetRef><bpmn:transformation id="Formal" evaluatesToTypeRef="tns:Item">value</bpmn:transformation></bpmn:dataOutputAssociation></bpmn:task>'));
  });
  it("refuses deletion through the global formal expression declaration", () => {
    r7ExpectFormalDeletionRefused(r7FormalOwner("", '<bpmn:formalExpression id="Formal" evaluatesToTypeRef="tns:Item">value</bpmn:formalExpression>'));
  });
  it("refuses an implicit formal reference deletion atomically after an earlier edit", () => {
    r7ExpectFormalDeletionRefused(r7InputTransformationOwner(), [{ kind: "attribute", target: address("Task"), name: "name", value: "Uncommitted" }, { kind: "remove", target: address("Item") }]);
  });
  it("allows removing an implicit formal reference and its target in one transaction", () => {
    const original = r7InputTransformationOwner(), before = structuredClone(original);
    const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Formal") }, { kind: "remove", target: address("Item") }]);
    expect(readBpmnDocument(next).byId.has("Formal")).toBe(false);
    expect(readBpmnDocument(next).byId.has("Item")).toBe(false);
    expect(element(next, "FormalAssociation").localName).toBe("dataInputAssociation");
    expect(original).toEqual(before);
  });
  it("retains explicit formal typing on a conditional event as a reference control", () => {
    const original = r7FormalOwner('<bpmn:intermediateCatchEvent id="Conditional"><bpmn:conditionalEventDefinition><bpmn:condition id="Formal" xsi:type="bpmn:tFormalExpression" evaluatesToTypeRef="tns:Item">ready</bpmn:condition></bpmn:conditionalEventDefinition></bpmn:intermediateCatchEvent>'), before = structuredClone(original);
    expect(() => applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Item") }])).toThrow("deletion leaves reference 'evaluatesToTypeRef'");
    expect(original).toEqual(before);
  });
  it("does not infer formal typing from an untyped conditional event condition", () => {
    const original = r7FormalOwner('<bpmn:intermediateCatchEvent id="Conditional"><bpmn:conditionalEventDefinition><bpmn:condition id="Untyped" evaluatesToTypeRef="tns:Item">opaque draft</bpmn:condition></bpmn:conditionalEventDefinition></bpmn:intermediateCatchEvent>');
    const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Item") }]);
    expect(readBpmnDocument(next).byId.has("Item")).toBe(false);
    expect(element(next, "Untyped").getAttribute("evaluatesToTypeRef")).toBe("tns:Item");
  });
  it("preserves vendor and extension-wrapped formal expression lookalikes", () => {
    const original = r7FormalOwner('<bpmn:task id="OpaqueTask"><bpmn:extensionElements><vendor:formalExpression evaluatesToTypeRef="tns:Item">vendor</vendor:formalExpression><bpmn:formalExpression evaluatesToTypeRef="tns:Item">extension</bpmn:formalExpression></bpmn:extensionElements></bpmn:task>');
    const retained = element(original, "OpaqueTask").innerHTML, next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Item") }]);
    expect(element(next, "OpaqueTask").innerHTML).toBe(retained);
    expect(readBpmnDocument(next).byId.has("Item")).toBe(false);
  });
  it("does not interpret a vendor attribute on a standard formal expression", () => {
    const original = r7FormalOwner("", '<bpmn:formalExpression id="Formal" vendor:evaluatesToTypeRef="tns:Item">vendor attribute</bpmn:formalExpression>');
    const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Item") }]);
    expect(element(next, "Formal").getAttributeNS("urn:vendor", "evaluatesToTypeRef")).toBe("tns:Item");
    expect(readBpmnDocument(next).byId.has("Item")).toBe(false);
  });
  it("refuses deletion through the ioBinding input data reference", () => { r7ExpectIoDeletionRefused("BindingInput", "inputDataRef"); });
  it("refuses deletion through the ioBinding output data reference", () => { r7ExpectIoDeletionRefused("BindingOutput", "outputDataRef"); });
  it("refuses deletion through the ioBinding operation reference", () => { r7ExpectIoDeletionRefused("BindingOperation", "operationRef"); });
  it("refuses an ioBinding reference deletion atomically after an earlier edit", () => { r7ExpectIoDeletionRefused("BindingInput", "inputDataRef", true); });
  it("allows removing an ioBinding and its referenced input in one transaction", () => {
    const original = r7IoBindingOwner(), before = structuredClone(original);
    const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Binding") }, { kind: "remove", target: address("BindingInput") }]);
    expect(readBpmnDocument(next).byId.has("Binding")).toBe(false);
    expect(readBpmnDocument(next).byId.has("BindingInput")).toBe(false);
    expect(element(next, "BindingOutput").localName).toBe("dataOutput");
    expect(element(next, "BindingOperation").localName).toBe("operation");
    expect(original).toEqual(before);
  });
  it("preserves vendor ioBinding lookalikes without inventing references", () => {
    const original = r7IoBindingOwner();
    original.structure!.payload.documentXml = readBpmnDocument(original).payload.documentXml.replace('<bpmn:ioBinding id="Binding" operationRef="tns:BindingOperation" inputDataRef="BindingInput" outputDataRef="BindingOutput"/>', '<vendor:ioBinding id="VendorBinding" operationRef="tns:BindingOperation" inputDataRef="BindingInput" outputDataRef="BindingOutput"/>');
    const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("BindingInput") }]);
    expect(readBpmnDocument(next).byId.has("BindingInput")).toBe(false);
    const retained = readBpmnDocument(next).document.getElementsByTagNameNS("urn:vendor", "ioBinding")[0];
    expect(retained.getAttribute("inputDataRef")).toBe("BindingInput");
    expect(retained.getAttribute("operationRef")).toBe("tns:BindingOperation");
  });
});

function r8DocumentOwner(documentXml: string): ActivityNode {
  return { nodeId: "r8-document-owner", activityVersionId: "r8-document-version", inputs: [], outputs: [], structure: {
    kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: { documentXml, processId: "Process", activities: [], bindings: [], variables: [] }
  } };
}
function r8ExpectNoRefusal<T>(action: () => T, assertImmutable: () => void): T {
  let value: T | undefined, failure: unknown;
  try { value = action(); } catch (error) { failure = error; }
  assertImmutable();
  expect(failure).toBeUndefined();
  return value!;
}
function r8ScopeSnapshot(): ActivityNode {
  return { nodeId: "opaque-scope-snapshot", activityVersionId: "snapshot-version", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.scope-view", schemaVersion: "1.0.0", payload: { retained: true } } };
}
function r8OpaqueNativeOwner(asArray = false): ActivityNode {
  const original = owner(), native = (original.structure!.payload.activities as ActivityNode[])[0];
  native.structure = { kind: "vendor.opaque", schemaVersion: "1", payload: { metadata: asArray ? [r8ScopeSnapshot()] : r8ScopeSnapshot(), retained: "native metadata" } };
  return original;
}
function r8ReconnectOwner(realOutgoing: boolean): ActivityNode {
  return r8DocumentOwner(`<bpmn:definitions xmlns:bpmn="${bpmnNamespaces.model}" xmlns:tns="urn:reconnect" xmlns:vendor="urn:vendor" targetNamespace="urn:reconnect"><bpmn:process id="Process">
    <bpmn:extensionElements><vendor:snapshot><bpmn:task id="OpaqueTask"><bpmn:outgoing>tns:Flow</bpmn:outgoing></bpmn:task></vendor:snapshot></bpmn:extensionElements>
    <bpmn:task id="Old">${realOutgoing ? "<bpmn:outgoing>Flow</bpmn:outgoing>" : ""}</bpmn:task><bpmn:task id="New"/><bpmn:task id="Target"><bpmn:incoming>Flow</bpmn:incoming></bpmn:task>
    <bpmn:sequenceFlow id="Flow" sourceRef="Old" targetRef="Target"/>
  </bpmn:process></bpmn:definitions>`);
}
function r8SnapshotXml(value: ActivityNode): string {
  const snapshot = readBpmnDocument(value).document.getElementsByTagNameNS("urn:vendor", "snapshot")[0];
  return new XMLSerializer().serializeToString(snapshot);
}

describe("R8 opaque native and XML boundaries", () => {
  it("opens node-shaped projected-scope metadata below an opaque native structure", () => {
    const original = r8OpaqueNativeOwner(), before = structuredClone(original);
    const parsed = r8ExpectNoRefusal(() => readBpmnDocument(original), () => expect(original).toEqual(before));
    expect(parsed.payload.documentXml).toBe(xml);
    expect((parsed.payload.activities[0].structure!.payload.metadata as ActivityNode).structure!.kind).toBe("elsa.bpmn.scope-view");
  });
  it("preserves opaque native snapshot arrays through an unrelated XML edit", () => {
    const original = r8OpaqueNativeOwner(true), before = structuredClone(original);
    const next = r8ExpectNoRefusal(() => applyBpmnDocumentCommands(original, [{ kind: "attribute", target: address("Task"), name: "name", value: "Edited" }]), () => expect(original).toEqual(before));
    expect(element(next, "Task").getAttribute("name")).toBe("Edited");
    expect(next.structure!.payload.activities).toBe(original.structure!.payload.activities);
    expect((next.structure!.payload.activities as ActivityNode[])[0].structure!.payload.metadata).toEqual([r8ScopeSnapshot()]);
  });
  it("still rejects projected views in declared canonical and legacy Sequence ownership", () => {
    for (const kind of ["elsa.sequence.structure", "elsa.sequence"]) {
      const original = owner(), before = structuredClone(original);
      (original.structure!.payload.activities as ActivityNode[])[0].structure = { kind, schemaVersion: "1.0.0", payload: { activities: [r8ScopeSnapshot()] } };
      const configured = structuredClone(original);
      expect(() => readBpmnDocument(original)).toThrow("projected scope structures");
      expect(original).toEqual(configured);
      expect(before.structure!.payload.documentXml).toBe(original.structure!.payload.documentXml);
    }
  });
  it("still rejects projected views in an independently owned complete-document forest", () => {
    const original = owner(), nested = r8DocumentOwner(`<bpmn:definitions xmlns:bpmn="${bpmnNamespaces.model}" targetNamespace="urn:nested"><bpmn:process id="Process"/></bpmn:definitions>`);
    nested.nodeId = "nested-document";
    nested.structure!.payload.activities = [r8ScopeSnapshot()];
    (original.structure!.payload.activities as ActivityNode[]).push(nested);
    const before = structuredClone(original);
    expect(() => readBpmnDocument(original)).toThrow("projected scope structures");
    expect(original).toEqual(before);
  });
  it("preserves an opaque-only outgoing snapshot while reconnecting the active flow", () => {
    const original = r8ReconnectOwner(false), before = structuredClone(original), opaque = r8SnapshotXml(original);
    const next = applyBpmnDocumentCommands(original, [{ kind: "reconnect", elementId: "Flow", sourceRef: "New", targetRef: "Target" }]);
    expect(original).toEqual(before);
    expect(r8SnapshotXml(next)).toBe(opaque);
    const parsed = readBpmnDocument(next), outgoing = element(next, "New").getElementsByTagNameNS(bpmnNamespaces.model, "outgoing")[0];
    expect(resolveBpmnDocumentReference(parsed, outgoing, outgoing.textContent)).toBe(parsed.byId.get("Flow"));
    expect(element(next, "Flow").getAttribute("sourceRef")).toBe("New");
    expect(element(next, "OpaqueTask").getElementsByTagNameNS(bpmnNamespaces.model, "outgoing")[0].textContent).toBe("tns:Flow");
  });
  it("does not treat an opaque snapshot as an ambiguous active outgoing reference", () => {
    const original = r8ReconnectOwner(true), before = structuredClone(original), opaque = r8SnapshotXml(original);
    const next = r8ExpectNoRefusal(() => applyBpmnDocumentCommands(original, [{ kind: "reconnect", elementId: "Flow", sourceRef: "New", targetRef: "Target" }]), () => expect(original).toEqual(before));
    expect(r8SnapshotXml(next)).toBe(opaque);
    expect(element(next, "Old").getElementsByTagNameNS(bpmnNamespaces.model, "outgoing")).toHaveLength(0);
    expect(element(next, "New").getElementsByTagNameNS(bpmnNamespaces.model, "outgoing")).toHaveLength(1);
  });
  it("still refuses a reconnect into an opaque snapshot outside the active standard scope", () => {
    const original = r8ReconnectOwner(true), before = structuredClone(original);
    expect(() => applyBpmnDocumentCommands(original, [{ kind: "reconnect", elementId: "Flow", sourceRef: "OpaqueTask", targetRef: "Target" }])).toThrow("one standard scope");
    expect(original).toEqual(before);
  });
});

interface R8ReferenceFixture {
  xml: string; processId: string; removeId: string;
  witnesses: { ownerId: string; property: string; targetId: string }[];
  external?: { ownerLocalName: string; property: string; value: string };
}
// Domain-correct representatives are pinned to bpmn-moddle 10.3.1 and the external audit companion.
const r8ReferenceFixtures: Record<string, R8ReferenceFixture> = {
  "domain-inputDataItem": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"RemovedItem\"/><bpmn:process id=\"Process\"><bpmn:task id=\"Owner\"><bpmn:multiInstanceLoopCharacteristics><bpmn:inputDataItem id=\"Typed\" itemSubjectRef=\"m:RemovedItem\"/></bpmn:multiInstanceLoopCharacteristics></bpmn:task></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedItem",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "itemSubjectRef",
        "targetId": "RemovedItem"
      }
    ]
  },
  "domain-outputDataItem": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"RemovedItem\"/><bpmn:process id=\"Process\"><bpmn:task id=\"Owner\"><bpmn:multiInstanceLoopCharacteristics><bpmn:outputDataItem id=\"Typed\" itemSubjectRef=\"m:RemovedItem\"/></bpmn:multiInstanceLoopCharacteristics></bpmn:task></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedItem",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "itemSubjectRef",
        "targetId": "RemovedItem"
      }
    ]
  },
  "domain-task-ServiceTask-operation": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:message id=\"KeepMessage\"/><bpmn:interface id=\"Interface\" name=\"Service\"><bpmn:operation id=\"RemovedOperation\" name=\"Operation\"><bpmn:inMessageRef>m:KeepMessage</bpmn:inMessageRef></bpmn:operation></bpmn:interface><bpmn:process id=\"Process\"><bpmn:task id=\"Typed\" xsi:type=\"bpmn:tServiceTask\" operationRef=\"m:RemovedOperation\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedOperation",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "operationRef",
        "targetId": "RemovedOperation"
      }
    ]
  },
  "domain-task-SendTask-operation": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:message id=\"KeepMessage\"/><bpmn:interface id=\"Interface\" name=\"Service\"><bpmn:operation id=\"RemovedOperation\" name=\"Operation\"><bpmn:inMessageRef>m:KeepMessage</bpmn:inMessageRef></bpmn:operation></bpmn:interface><bpmn:process id=\"Process\"><bpmn:task id=\"Typed\" xsi:type=\"bpmn:tSendTask\" operationRef=\"m:RemovedOperation\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedOperation",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "operationRef",
        "targetId": "RemovedOperation"
      }
    ]
  },
  "domain-task-ReceiveTask-operation": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:message id=\"KeepMessage\"/><bpmn:interface id=\"Interface\" name=\"Service\"><bpmn:operation id=\"RemovedOperation\" name=\"Operation\"><bpmn:inMessageRef>m:KeepMessage</bpmn:inMessageRef></bpmn:operation></bpmn:interface><bpmn:process id=\"Process\"><bpmn:task id=\"Typed\" xsi:type=\"bpmn:tReceiveTask\" operationRef=\"m:RemovedOperation\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedOperation",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "operationRef",
        "targetId": "RemovedOperation"
      }
    ]
  },
  "domain-task-SendTask-message": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:message id=\"RemovedMessage\"/><bpmn:process id=\"Process\"><bpmn:task id=\"Typed\" xsi:type=\"bpmn:tSendTask\" messageRef=\"m:RemovedMessage\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedMessage",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "messageRef",
        "targetId": "RemovedMessage"
      }
    ]
  },
  "domain-task-ReceiveTask-message": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:message id=\"RemovedMessage\"/><bpmn:process id=\"Process\"><bpmn:task id=\"Typed\" xsi:type=\"bpmn:tReceiveTask\" messageRef=\"m:RemovedMessage\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedMessage",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "messageRef",
        "targetId": "RemovedMessage"
      }
    ]
  },
  "domain-artifact-association": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"><bpmn:task id=\"RemovedTask\"/><bpmn:task id=\"KeepTask\"/><bpmn:artifact id=\"Typed\" xsi:type=\"bpmn:tAssociation\" sourceRef=\"m:RemovedTask\" targetRef=\"m:KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedTask",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "sourceRef",
        "targetId": "RemovedTask"
      }
    ]
  },
  "domain-choreography-global": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:choreography id=\"Typed\" xsi:type=\"bpmn:tGlobalChoreographyTask\" initiatingParticipantRef=\"m:RemovedParticipant\"><bpmn:participant id=\"RemovedParticipant\"/><bpmn:participant id=\"KeepParticipant\"/></bpmn:choreography><bpmn:process id=\"Process\"></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedParticipant",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "initiatingParticipantRef",
        "targetId": "RemovedParticipant"
      }
    ]
  },
  "domain-collaboration-global": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:collaboration id=\"Typed\" xsi:type=\"bpmn:tGlobalChoreographyTask\" initiatingParticipantRef=\"m:RemovedParticipant\"><bpmn:participant id=\"RemovedParticipant\"/><bpmn:participant id=\"KeepParticipant\"/></bpmn:collaboration><bpmn:process id=\"Process\"></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedParticipant",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "initiatingParticipantRef",
        "targetId": "RemovedParticipant"
      }
    ]
  },
  "domain-conversationNode-call": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:collaboration id=\"RemovedCollaboration\"/><bpmn:collaboration id=\"KeepCollaboration\"><bpmn:conversationNode id=\"Typed\" xsi:type=\"bpmn:tCallConversation\" calledCollaborationRef=\"m:RemovedCollaboration\"/></bpmn:collaboration><bpmn:process id=\"Process\"></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedCollaboration",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "calledCollaborationRef",
        "targetId": "RemovedCollaboration"
      }
    ]
  },
  "domain-event-boundary": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"><bpmn:task id=\"RemovedTask\"/><bpmn:event id=\"Typed\" xsi:type=\"bpmn:tBoundaryEvent\" attachedToRef=\"m:RemovedTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedTask",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "attachedToRef",
        "targetId": "RemovedTask"
      }
    ]
  },
  "domain-eventDefinition-error": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:error id=\"RemovedError\"/><bpmn:eventDefinition id=\"Typed\" xsi:type=\"bpmn:tErrorEventDefinition\" errorRef=\"m:RemovedError\"/><bpmn:process id=\"Process\"></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedError",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "errorRef",
        "targetId": "RemovedError"
      }
    ]
  },
  "domain-flowElement-service-operation": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:message id=\"KeepMessage\"/><bpmn:interface id=\"Interface\" name=\"Service\"><bpmn:operation id=\"RemovedOperation\" name=\"Operation\"><bpmn:inMessageRef>m:KeepMessage</bpmn:inMessageRef></bpmn:operation></bpmn:interface><bpmn:process id=\"Process\"><bpmn:flowElement id=\"Typed\" xsi:type=\"bpmn:tServiceTask\" operationRef=\"m:RemovedOperation\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedOperation",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "operationRef",
        "targetId": "RemovedOperation"
      }
    ]
  },
  "domain-flowElement-service-default": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"><bpmn:flowElement id=\"Typed\" xsi:type=\"bpmn:tServiceTask\" default=\"RemovedFlow\"/><bpmn:task id=\"KeepTask\"/><bpmn:sequenceFlow id=\"RemovedFlow\" sourceRef=\"Typed\" targetRef=\"KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedFlow",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "default",
        "targetId": "RemovedFlow"
      }
    ]
  },
  "domain-loopCharacteristics-multi": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:signalEventDefinition id=\"RemovedEventDefinition\"/><bpmn:process id=\"Process\"><bpmn:task id=\"Owner\"><bpmn:loopCharacteristics id=\"Typed\" xsi:type=\"bpmn:tMultiInstanceLoopCharacteristics\" oneBehaviorEventRef=\"m:RemovedEventDefinition\" noneBehaviorEventRef=\"m:RemovedEventDefinition\"/></bpmn:task></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedEventDefinition",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "oneBehaviorEventRef",
        "targetId": "RemovedEventDefinition"
      },
      {
        "ownerId": "Typed",
        "property": "noneBehaviorEventRef",
        "targetId": "RemovedEventDefinition"
      }
    ]
  },
  "domain-partitionElement-sequenceFlow": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"><bpmn:property id=\"KeepData\"/><bpmn:laneSet id=\"LaneSet\"><bpmn:lane id=\"Lane\"><bpmn:partitionElement id=\"Typed\" xsi:type=\"bpmn:tSequenceFlow\" sourceRef=\"RemovedTask\" targetRef=\"KeepTask\"/></bpmn:lane></bpmn:laneSet><bpmn:task id=\"RemovedTask\"/><bpmn:task id=\"KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedTask",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "sourceRef",
        "targetId": "RemovedTask"
      }
    ]
  },
  "domain-rootElement-message": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"RemovedItem\"/><bpmn:rootElement id=\"Typed\" xsi:type=\"bpmn:tMessage\" itemRef=\"m:RemovedItem\"/><bpmn:process id=\"Process\"></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedItem",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "itemRef",
        "targetId": "RemovedItem"
      }
    ]
  },
  "domain-DiagramElement-shape": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"><bpmn:task id=\"RemovedTask\"/></bpmn:process><bpmndi:BPMNDiagram id=\"Diagram\"><bpmndi:BPMNPlane id=\"Plane\" bpmnElement=\"m:Process\"><di:DiagramElement id=\"Typed\" xsi:type=\"bpmndi:BPMNShape\" bpmnElement=\"m:RemovedTask\"><dc:Bounds x=\"0\" y=\"0\" width=\"100\" height=\"80\"/></di:DiagramElement></bpmndi:BPMNPlane></bpmndi:BPMNDiagram></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedTask",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "bpmnElement",
        "targetId": "RemovedTask"
      }
    ]
  },
  "domain-DiagramElement-edge-model": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"><bpmn:task id=\"SourceTask\"/><bpmn:task id=\"TargetTask\"/><bpmn:sequenceFlow id=\"ModelFlow\" sourceRef=\"SourceTask\" targetRef=\"TargetTask\"/></bpmn:process><bpmndi:BPMNDiagram id=\"Diagram\"><bpmndi:BPMNPlane id=\"Plane\" bpmnElement=\"m:Process\"><bpmndi:BPMNShape id=\"SourceShape\" bpmnElement=\"m:SourceTask\"><dc:Bounds x=\"0\" y=\"0\" width=\"100\" height=\"80\"/></bpmndi:BPMNShape><bpmndi:BPMNShape id=\"TargetShape\" bpmnElement=\"m:TargetTask\"><dc:Bounds x=\"0\" y=\"0\" width=\"100\" height=\"80\"/></bpmndi:BPMNShape><di:DiagramElement id=\"Typed\" xsi:type=\"bpmndi:BPMNEdge\" bpmnElement=\"m:ModelFlow\" sourceElement=\"m:SourceShape\" targetElement=\"m:TargetShape\"><di:waypoint x=\"0\" y=\"0\"/><di:waypoint x=\"10\" y=\"10\"/></di:DiagramElement></bpmndi:BPMNPlane></bpmndi:BPMNDiagram></bpmn:definitions>",
    "processId": "Process",
    "removeId": "ModelFlow",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "bpmnElement",
        "targetId": "ModelFlow"
      }
    ]
  },
  "domain-DiagramElement-edge-source": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"><bpmn:task id=\"SourceTask\"/><bpmn:task id=\"TargetTask\"/><bpmn:sequenceFlow id=\"ModelFlow\" sourceRef=\"SourceTask\" targetRef=\"TargetTask\"/></bpmn:process><bpmndi:BPMNDiagram id=\"Diagram\"><bpmndi:BPMNPlane id=\"Plane\" bpmnElement=\"m:Process\"><bpmndi:BPMNShape id=\"SourceShape\" bpmnElement=\"m:SourceTask\"><dc:Bounds x=\"0\" y=\"0\" width=\"100\" height=\"80\"/></bpmndi:BPMNShape><bpmndi:BPMNShape id=\"TargetShape\" bpmnElement=\"m:TargetTask\"><dc:Bounds x=\"0\" y=\"0\" width=\"100\" height=\"80\"/></bpmndi:BPMNShape><di:DiagramElement id=\"Typed\" xsi:type=\"bpmndi:BPMNEdge\" bpmnElement=\"m:ModelFlow\" sourceElement=\"m:SourceShape\" targetElement=\"m:TargetShape\"><di:waypoint x=\"0\" y=\"0\"/><di:waypoint x=\"10\" y=\"10\"/></di:DiagramElement></bpmndi:BPMNPlane></bpmndi:BPMNDiagram></bpmn:definitions>",
    "processId": "Process",
    "removeId": "SourceShape",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "sourceElement",
        "targetId": "SourceShape"
      }
    ]
  },
  "domain-DiagramElement-edge-target": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"><bpmn:task id=\"SourceTask\"/><bpmn:task id=\"TargetTask\"/><bpmn:sequenceFlow id=\"ModelFlow\" sourceRef=\"SourceTask\" targetRef=\"TargetTask\"/></bpmn:process><bpmndi:BPMNDiagram id=\"Diagram\"><bpmndi:BPMNPlane id=\"Plane\" bpmnElement=\"m:Process\"><bpmndi:BPMNShape id=\"SourceShape\" bpmnElement=\"m:SourceTask\"><dc:Bounds x=\"0\" y=\"0\" width=\"100\" height=\"80\"/></bpmndi:BPMNShape><bpmndi:BPMNShape id=\"TargetShape\" bpmnElement=\"m:TargetTask\"><dc:Bounds x=\"0\" y=\"0\" width=\"100\" height=\"80\"/></bpmndi:BPMNShape><di:DiagramElement id=\"Typed\" xsi:type=\"bpmndi:BPMNEdge\" bpmnElement=\"m:ModelFlow\" sourceElement=\"m:SourceShape\" targetElement=\"m:TargetShape\"><di:waypoint x=\"0\" y=\"0\"/><di:waypoint x=\"10\" y=\"10\"/></di:DiagramElement></bpmndi:BPMNPlane></bpmndi:BPMNDiagram></bpmn:definitions>",
    "processId": "Process",
    "removeId": "TargetShape",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "targetElement",
        "targetId": "TargetShape"
      }
    ]
  },
  "domain-DiagramElement-plane": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"RemovedProcess\"/><bpmn:process id=\"Process\"></bpmn:process><bpmndi:BPMNDiagram id=\"Diagram\"><bpmndi:BPMNPlane id=\"Plane\" bpmnElement=\"m:Process\"><di:DiagramElement id=\"Typed\" xsi:type=\"bpmndi:BPMNPlane\" bpmnElement=\"m:RemovedProcess\"/></bpmndi:BPMNPlane></bpmndi:BPMNDiagram></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedProcess",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "bpmnElement",
        "targetId": "RemovedProcess"
      }
    ]
  },
  "domain-DiagramElement-label": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"></bpmn:process><bpmndi:BPMNDiagram id=\"Diagram\"><bpmndi:BPMNPlane id=\"Plane\" bpmnElement=\"m:Process\"><di:DiagramElement id=\"Typed\" xsi:type=\"bpmndi:BPMNLabel\" labelStyle=\"m:RemovedStyle\"/></bpmndi:BPMNPlane><bpmndi:BPMNLabelStyle id=\"RemovedStyle\"><dc:Font name=\"Arial\"/></bpmndi:BPMNLabelStyle></bpmndi:BPMNDiagram></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedStyle",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "labelStyle",
        "targetId": "RemovedStyle"
      }
    ]
  },
  "domain-effective-parent-tComplexBehaviorDefinition": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"RemovedItem\"/><bpmn:process id=\"Process\"><bpmn:property id=\"KeepData\"/><bpmn:laneSet id=\"LaneSet\"><bpmn:lane id=\"Lane\"><bpmn:partitionElement id=\"Typed\" xsi:type=\"bpmn:tComplexBehaviorDefinition\"><bpmn:condition id=\"Expression\" evaluatesToTypeRef=\"m:RemovedItem\">x</bpmn:condition></bpmn:partitionElement></bpmn:lane></bpmn:laneSet><bpmn:task id=\"KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedItem",
    "witnesses": [
      {
        "ownerId": "Expression",
        "property": "evaluatesToTypeRef",
        "targetId": "RemovedItem"
      }
    ]
  },
  "domain-effective-parent-tCorrelationPropertyBinding": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"RemovedItem\"/><bpmn:message id=\"KeepMessage\"/><bpmn:correlationProperty id=\"KeepCorrelationProperty\"><bpmn:correlationPropertyRetrievalExpression messageRef=\"m:KeepMessage\"><bpmn:messagePath>x</bpmn:messagePath></bpmn:correlationPropertyRetrievalExpression></bpmn:correlationProperty><bpmn:process id=\"Process\"><bpmn:property id=\"KeepData\"/><bpmn:laneSet id=\"LaneSet\"><bpmn:lane id=\"Lane\"><bpmn:partitionElement id=\"Typed\" xsi:type=\"bpmn:tCorrelationPropertyBinding\" correlationPropertyRef=\"m:KeepCorrelationProperty\"><bpmn:dataPath id=\"Expression\" evaluatesToTypeRef=\"m:RemovedItem\">x</bpmn:dataPath></bpmn:partitionElement></bpmn:lane></bpmn:laneSet><bpmn:task id=\"KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedItem",
    "witnesses": [
      {
        "ownerId": "Expression",
        "property": "evaluatesToTypeRef",
        "targetId": "RemovedItem"
      }
    ]
  },
  "domain-effective-parent-tCorrelationPropertyRetrievalExpression": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"RemovedItem\"/><bpmn:message id=\"KeepMessage\"/><bpmn:process id=\"Process\"><bpmn:property id=\"KeepData\"/><bpmn:laneSet id=\"LaneSet\"><bpmn:lane id=\"Lane\"><bpmn:partitionElement id=\"Typed\" xsi:type=\"bpmn:tCorrelationPropertyRetrievalExpression\" messageRef=\"m:KeepMessage\"><bpmn:messagePath id=\"Expression\" evaluatesToTypeRef=\"m:RemovedItem\">x</bpmn:messagePath></bpmn:partitionElement></bpmn:lane></bpmn:laneSet><bpmn:task id=\"KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedItem",
    "witnesses": [
      {
        "ownerId": "Expression",
        "property": "evaluatesToTypeRef",
        "targetId": "RemovedItem"
      }
    ]
  },
  "domain-effective-parent-tDataAssociation": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"RemovedItem\"/><bpmn:process id=\"Process\"><bpmn:property id=\"KeepData\"/><bpmn:laneSet id=\"LaneSet\"><bpmn:lane id=\"Lane\"><bpmn:partitionElement id=\"Typed\" xsi:type=\"bpmn:tDataAssociation\"><bpmn:targetRef>KeepData</bpmn:targetRef><bpmn:transformation id=\"Expression\" evaluatesToTypeRef=\"m:RemovedItem\">x</bpmn:transformation></bpmn:partitionElement></bpmn:lane></bpmn:laneSet><bpmn:task id=\"KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedItem",
    "witnesses": [
      {
        "ownerId": "Expression",
        "property": "evaluatesToTypeRef",
        "targetId": "RemovedItem"
      }
    ]
  },
  "domain-effective-parent-tDataInputAssociation": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"RemovedItem\"/><bpmn:process id=\"Process\"><bpmn:property id=\"KeepData\"/><bpmn:laneSet id=\"LaneSet\"><bpmn:lane id=\"Lane\"><bpmn:partitionElement id=\"Typed\" xsi:type=\"bpmn:tDataInputAssociation\"><bpmn:targetRef>KeepData</bpmn:targetRef><bpmn:transformation id=\"Expression\" evaluatesToTypeRef=\"m:RemovedItem\">x</bpmn:transformation></bpmn:partitionElement></bpmn:lane></bpmn:laneSet><bpmn:task id=\"KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedItem",
    "witnesses": [
      {
        "ownerId": "Expression",
        "property": "evaluatesToTypeRef",
        "targetId": "RemovedItem"
      }
    ]
  },
  "domain-effective-parent-tDataOutputAssociation": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"RemovedItem\"/><bpmn:process id=\"Process\"><bpmn:property id=\"KeepData\"/><bpmn:laneSet id=\"LaneSet\"><bpmn:lane id=\"Lane\"><bpmn:partitionElement id=\"Typed\" xsi:type=\"bpmn:tDataOutputAssociation\"><bpmn:targetRef>KeepData</bpmn:targetRef><bpmn:transformation id=\"Expression\" evaluatesToTypeRef=\"m:RemovedItem\">x</bpmn:transformation></bpmn:partitionElement></bpmn:lane></bpmn:laneSet><bpmn:task id=\"KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedItem",
    "witnesses": [
      {
        "ownerId": "Expression",
        "property": "evaluatesToTypeRef",
        "targetId": "RemovedItem"
      }
    ]
  },
  "domain-partitionElement-relationship-text": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:process id=\"Process\"><bpmn:property id=\"KeepData\"/><bpmn:laneSet id=\"LaneSet\"><bpmn:lane id=\"Lane\"><bpmn:partitionElement id=\"Typed\" xsi:type=\"bpmn:tRelationship\" type=\"audit\"><bpmn:source>m:RemovedTask</bpmn:source><bpmn:target>m:KeepTask</bpmn:target></bpmn:partitionElement></bpmn:lane></bpmn:laneSet><bpmn:task id=\"RemovedTask\"/><bpmn:task id=\"KeepTask\"/></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "RemovedTask",
    "witnesses": [
      {
        "ownerId": "Typed",
        "property": "source",
        "targetId": "RemovedTask"
      }
    ]
  },
  "external-itemDefinition-structure": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"Lookalike\"/><bpmn:itemDefinition id=\"OwnerItem\" structureRef=\"m:Lookalike\"/><bpmn:process id=\"Process\"></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "Lookalike",
    "witnesses": [],
    "external": {
      "ownerLocalName": "itemDefinition",
      "property": "structureRef",
      "value": "m:Lookalike"
    }
  },
  "external-interface-implementation": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"Lookalike\"/><bpmn:message id=\"KeepMessage\"/><bpmn:interface id=\"OwnerInterface\" name=\"Service\" implementationRef=\"m:Lookalike\"><bpmn:operation id=\"KeepOperation\" name=\"Keep\"><bpmn:inMessageRef>m:KeepMessage</bpmn:inMessageRef></bpmn:operation></bpmn:interface><bpmn:process id=\"Process\"></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "Lookalike",
    "witnesses": [],
    "external": {
      "ownerLocalName": "interface",
      "property": "implementationRef",
      "value": "m:Lookalike"
    }
  },
  "external-operation-implementation": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:itemDefinition id=\"Lookalike\"/><bpmn:message id=\"KeepMessage\"/><bpmn:interface id=\"KeepInterface\" name=\"Service\"><bpmn:operation id=\"OwnerOperation\" name=\"Keep\" implementationRef=\"m:Lookalike\"><bpmn:inMessageRef>m:KeepMessage</bpmn:inMessageRef></bpmn:operation></bpmn:interface><bpmn:process id=\"Process\"></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "Lookalike",
    "witnesses": [],
    "external": {
      "ownerLocalName": "operation",
      "property": "implementationRef",
      "value": "m:Lookalike"
    }
  },
  "external-extension-definition": {
    "xml": "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:m=\"urn:finite-audit\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:bpmndi=\"http://www.omg.org/spec/BPMN/20100524/DI\" xmlns:di=\"http://www.omg.org/spec/DD/20100524/DI\" xmlns:dc=\"http://www.omg.org/spec/DD/20100524/DC\" xmlns:v=\"urn:vendor\" targetNamespace=\"urn:finite-audit\"><bpmn:extension definition=\"m:Lookalike\"/><bpmn:itemDefinition id=\"Lookalike\"/><bpmn:process id=\"Process\"></bpmn:process></bpmn:definitions>",
    "processId": "Process",
    "removeId": "Lookalike",
    "witnesses": [],
    "external": {
      "ownerLocalName": "extension",
      "property": "definition",
      "value": "m:Lookalike"
    }
  }
};
function r8ReferenceOwner(id: string): ActivityNode {
  const fixture = r8ReferenceFixtures[id], original = r8DocumentOwner(fixture.xml);
  original.structure!.payload.processId = fixture.processId;
  const parsed = readBpmnDocument(original);
  expect(parsed.payload.documentXml).toBe(fixture.xml);
  expect(parsed.byId.has(fixture.removeId)).toBe(true);
  for (const witness of fixture.witnesses) {
    const site = parsed.byId.get(witness.ownerId)!;
    expect(site).toBeDefined();
    const reference = site.getAttribute(witness.property) ?? [...site.children].find(child => child.namespaceURI === bpmnNamespaces.model && child.localName === witness.property)?.textContent;
    expect(resolveBpmnDocumentReference(parsed, site, reference ?? null)).toBe(parsed.byId.get(witness.targetId));
  }
  return original;
}
function r8ExpectOwnedDeletionRefused(id: string) {
  const original = r8ReferenceOwner(id), before = structuredClone(original), fixture = r8ReferenceFixtures[id];
  expect(() => {
    try { applyBpmnDocumentCommands(original, [{ kind: "remove", target: address(fixture.removeId) }]); }
    finally { expect(original).toEqual(before); }
  }).toThrow(/deletion leaves/);
}
function r8ExpectExternalValuePreserved(id: string) {
  const original = r8ReferenceOwner(id), before = structuredClone(original), fixture = r8ReferenceFixtures[id], external = fixture.external!;
  const next = r8ExpectNoRefusal(() => applyBpmnDocumentCommands(original, [{ kind: "remove", target: address(fixture.removeId) }]), () => expect(original).toEqual(before));
  expect(readBpmnDocument(next).byId.has(fixture.removeId)).toBe(false);
  const retained = [...readBpmnDocument(next).document.getElementsByTagNameNS(bpmnNamespaces.model, external.ownerLocalName)].find(site => site.getAttribute(external.property) === external.value);
  expect(retained).toBeDefined();
  expect(retained!.getAttribute(external.property)).toBe(external.value);
}

describe("R8 pinned standard reference role and type domains", () => {
  it("protects the owned reference for inputDataItem", () => { r8ExpectOwnedDeletionRefused("domain-inputDataItem"); });
  it("protects the owned reference for outputDataItem", () => { r8ExpectOwnedDeletionRefused("domain-outputDataItem"); });
  it("protects the owned reference for task ServiceTask operation", () => { r8ExpectOwnedDeletionRefused("domain-task-ServiceTask-operation"); });
  it("protects the owned reference for task SendTask operation", () => { r8ExpectOwnedDeletionRefused("domain-task-SendTask-operation"); });
  it("protects the owned reference for task ReceiveTask operation", () => { r8ExpectOwnedDeletionRefused("domain-task-ReceiveTask-operation"); });
  it("protects the owned reference for task SendTask message", () => { r8ExpectOwnedDeletionRefused("domain-task-SendTask-message"); });
  it("protects the owned reference for task ReceiveTask message", () => { r8ExpectOwnedDeletionRefused("domain-task-ReceiveTask-message"); });
  it("protects the owned reference for artifact association", () => { r8ExpectOwnedDeletionRefused("domain-artifact-association"); });
  it("protects the owned reference for choreography global", () => { r8ExpectOwnedDeletionRefused("domain-choreography-global"); });
  it("protects the owned reference for collaboration global", () => { r8ExpectOwnedDeletionRefused("domain-collaboration-global"); });
  it("protects the owned reference for conversationNode call", () => { r8ExpectOwnedDeletionRefused("domain-conversationNode-call"); });
  it("protects the owned reference for event boundary", () => { r8ExpectOwnedDeletionRefused("domain-event-boundary"); });
  it("protects the owned reference for eventDefinition error", () => { r8ExpectOwnedDeletionRefused("domain-eventDefinition-error"); });
  it("protects the owned reference for flowElement service operation", () => { r8ExpectOwnedDeletionRefused("domain-flowElement-service-operation"); });
  it("protects the owned reference for flowElement service default", () => { r8ExpectOwnedDeletionRefused("domain-flowElement-service-default"); });
  it("protects the owned reference for loopCharacteristics multi", () => { r8ExpectOwnedDeletionRefused("domain-loopCharacteristics-multi"); });
  it("protects the owned reference for partitionElement sequenceFlow", () => { r8ExpectOwnedDeletionRefused("domain-partitionElement-sequenceFlow"); });
  it("protects the owned reference for rootElement message", () => { r8ExpectOwnedDeletionRefused("domain-rootElement-message"); });
  it("protects the owned reference for DiagramElement shape", () => { r8ExpectOwnedDeletionRefused("domain-DiagramElement-shape"); });
  it("protects the owned reference for DiagramElement edge model", () => { r8ExpectOwnedDeletionRefused("domain-DiagramElement-edge-model"); });
  it("protects the owned reference for DiagramElement edge source", () => { r8ExpectOwnedDeletionRefused("domain-DiagramElement-edge-source"); });
  it("protects the owned reference for DiagramElement edge target", () => { r8ExpectOwnedDeletionRefused("domain-DiagramElement-edge-target"); });
  it("protects the owned reference for DiagramElement plane", () => { r8ExpectOwnedDeletionRefused("domain-DiagramElement-plane"); });
  it("protects the owned reference for DiagramElement label", () => { r8ExpectOwnedDeletionRefused("domain-DiagramElement-label"); });
  it("protects the owned reference for effective parent tComplexBehaviorDefinition", () => { r8ExpectOwnedDeletionRefused("domain-effective-parent-tComplexBehaviorDefinition"); });
  it("protects the owned reference for effective parent tCorrelationPropertyBinding", () => { r8ExpectOwnedDeletionRefused("domain-effective-parent-tCorrelationPropertyBinding"); });
  it("protects the owned reference for effective parent tCorrelationPropertyRetrievalExpression", () => { r8ExpectOwnedDeletionRefused("domain-effective-parent-tCorrelationPropertyRetrievalExpression"); });
  it("protects the owned reference for effective parent tDataAssociation", () => { r8ExpectOwnedDeletionRefused("domain-effective-parent-tDataAssociation"); });
  it("protects the owned reference for effective parent tDataInputAssociation", () => { r8ExpectOwnedDeletionRefused("domain-effective-parent-tDataInputAssociation"); });
  it("protects the owned reference for effective parent tDataOutputAssociation", () => { r8ExpectOwnedDeletionRefused("domain-effective-parent-tDataOutputAssociation"); });
  it("protects the owned reference for partitionElement relationship text", () => { r8ExpectOwnedDeletionRefused("domain-partitionElement-relationship-text"); });
  it("preserves the external QName domain for itemDefinition structure", () => { r8ExpectExternalValuePreserved("external-itemDefinition-structure"); });
  it("preserves the external QName domain for interface implementation", () => { r8ExpectExternalValuePreserved("external-interface-implementation"); });
  it("preserves the external QName domain for operation implementation", () => { r8ExpectExternalValuePreserved("external-operation-implementation"); });
  it("preserves the external QName domain for extension definition", () => { r8ExpectExternalValuePreserved("external-extension-definition"); });
});

function r8UnqualifiedTaskType(type: string): ActivityNode {
  const fixture = r8ReferenceFixtures["domain-task-ServiceTask-operation"];
  return r8DocumentOwner(fixture.xml.replace('xsi:type="bpmn:tServiceTask"', `xsi:type="${type}"`));
}
function r8ExpectUnqualifiedTypeOpaque(type: string) {
  const original = r8UnqualifiedTaskType(type), before = structuredClone(original);
  const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("RemovedOperation") }]);
  expect(readBpmnDocument(next).byId.has("RemovedOperation")).toBe(false);
  expect(element(next, "Typed").getAttribute("operationRef")).toBe("m:RemovedOperation");
  expect(original).toEqual(before);
}

describe("R8 reference qualification controls", () => {
  it("does not grant standard semantics to a foreign type with a known local spelling", () => { r8ExpectUnqualifiedTypeOpaque("v:tServiceTask"); });
  it("does not grant standard semantics to an unknown standard type", () => { r8ExpectUnqualifiedTypeOpaque("bpmn:tFutureTask"); });
  it("does not grant standard semantics to an unbound type prefix", () => { r8ExpectUnqualifiedTypeOpaque("unbound:tServiceTask"); });
  it("does not grant standard semantics to malformed type QNames", () => {
    for (const type of ["bpmn:tServiceTask:extra", "bpmn: tServiceTask", ":tServiceTask", "bpmn:", "bpmn:1Task"]) r8ExpectUnqualifiedTypeOpaque(type);
  });
  it("does not grant a known non-derived type its reference fields on a task", () => {
    const fixture = r8ReferenceFixtures["domain-task-ServiceTask-operation"], original = r8DocumentOwner(fixture.xml.replace('<bpmn:message id="KeepMessage"/>', '<bpmn:itemDefinition id="RemovedItem"/><bpmn:message id="KeepMessage"/>').replace('xsi:type="bpmn:tServiceTask" operationRef="m:RemovedOperation"', 'xsi:type="bpmn:tMessage" itemRef="m:RemovedItem"'));
    const before = structuredClone(original), next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("RemovedItem") }]);
    expect(element(next, "Typed").getAttribute("itemRef")).toBe("m:RemovedItem");
    expect(readBpmnDocument(next).byId.has("RemovedItem")).toBe(false);
    expect(original).toEqual(before);
  });
  it("does not grant an abstract element a concrete gateway type contract", () => {
    const original = r8DocumentOwner(`<bpmn:definitions xmlns:bpmn="${bpmnNamespaces.model}" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" targetNamespace="urn:abstract"><bpmn:process id="Process"><bpmn:task id="Source"/><bpmn:task id="Target"/><bpmn:gateway id="Typed" xsi:type="bpmn:tExclusiveGateway" default="RemovedFlow"/><bpmn:sequenceFlow id="RemovedFlow" sourceRef="Source" targetRef="Target"/></bpmn:process></bpmn:definitions>`);
    const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("RemovedFlow") }]);
    expect(element(next, "Typed").getAttribute("default")).toBe("RemovedFlow");
    expect(readBpmnDocument(next).byId.has("RemovedFlow")).toBe(false);
  });
  it("does not infer a multi-instance local data-item role under the wrong parent", () => {
    const fixture = r8ReferenceFixtures["domain-inputDataItem"], original = r8DocumentOwner(fixture.xml.replace("<bpmn:multiInstanceLoopCharacteristics>", "").replace("</bpmn:multiInstanceLoopCharacteristics>", ""));
    const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("RemovedItem") }]);
    expect(element(next, "Typed").getAttribute("itemSubjectRef")).toBe("m:RemovedItem");
    expect(readBpmnDocument(next).byId.has("RemovedItem")).toBe(false);
  });
  it("allows atomic removal of each declared multi-instance data item and its reference target", () => {
    for (const id of ["domain-inputDataItem", "domain-outputDataItem"]) {
      const original = r8ReferenceOwner(id), before = structuredClone(original);
      const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Typed") }, { kind: "remove", target: address("RemovedItem") }]);
      expect(readBpmnDocument(next).byId.has("Typed")).toBe(false);
      expect(readBpmnDocument(next).byId.has("RemovedItem")).toBe(false);
      expect(original).toEqual(before);
    }
  });
  it("allows atomic removal of a specialized task and its referenced operation owner", () => {
    const original = r8ReferenceOwner("domain-task-ServiceTask-operation"), before = structuredClone(original);
    const next = applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Typed") }, { kind: "remove", target: address("Interface") }]);
    expect(readBpmnDocument(next).byId.has("Typed")).toBe(false);
    expect(readBpmnDocument(next).byId.has("RemovedOperation")).toBe(false);
    expect(original).toEqual(before);
  });
  it("keeps true ItemDefinition references distinct from external structure and implementation QNames", () => {
    const prefix = `<bpmn:definitions xmlns:bpmn="${bpmnNamespaces.model}" xmlns:m="urn:item" targetNamespace="urn:item"><bpmn:itemDefinition id="Item"/>`;
    const suffix = '<bpmn:process id="Process"/></bpmn:definitions>';
    for (const declaration of ['<bpmn:error id="Error" structureRef="m:Item"/>', '<bpmn:resource id="Resource"><bpmn:resourceParameter id="Parameter" name="Value" type="m:Item"/></bpmn:resource>']) {
      const original = r8DocumentOwner(prefix + declaration + suffix), before = structuredClone(original);
      expect(() => applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("Item") }])).toThrow("deletion leaves reference");
      expect(original).toEqual(before);
    }
    expect(() => applyBpmnDocumentCommands(r7InputTransformationOwner(), [{ kind: "remove", target: address("Item") }])).toThrow("deletion leaves reference");
  });
  it("keeps the native CallableElement domain of calledElement despite its descriptor String encoding", () => {
    const original = r8DocumentOwner(`<bpmn:definitions xmlns:bpmn="${bpmnNamespaces.model}" xmlns:m="urn:call" targetNamespace="urn:call"><bpmn:process id="RemovedProcess"/><bpmn:process id="Process"><bpmn:callActivity id="Caller" calledElement="m:RemovedProcess"/></bpmn:process></bpmn:definitions>`), before = structuredClone(original);
    expect(() => applyBpmnDocumentCommands(original, [{ kind: "remove", target: address("RemovedProcess") }])).toThrow("deletion leaves reference");
    expect(original).toEqual(before);
  });
});
