import { describe, expect, it } from "vitest";
import { mapBpmnNativeCommandGroup, mapBpmnNativeCommandPlan, type NativeCommandAction } from "../bpmnDocumentCommands";
import { bindBpmnNativeBaseline, projectBpmnNativeDocument } from "../bpmnDocumentProjection";
import { authoring, capturePropertyActions, edit, modeled, owner, rawXml, scope } from "./bpmnDocumentFixture";
// @ts-expect-error pinned bpmn-moddle has no declarations for its runtime entry.
import { BpmnModdle } from "bpmn-moddle";

const ns = authoring.bpmnNamespaces;
function append(parent: Element, xml: string) {
  xml = xml.replaceAll("b:", "bpmn:");
  const fragment = new DOMParser().parseFromString(`<bpmn:definitions xmlns:bpmn="${ns.model}" xmlns:bpmndi="${ns.diagram}" xmlns:dc="${ns.bounds}" xmlns:di="${ns.coordinates}">${xml}</bpmn:definitions>`, "application/xml");
  const value = parent.ownerDocument.importNode(fragment.documentElement.firstElementChild!, true);
  value.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns:bpmn", ns.model); parent.appendChild(value); return value;
}
async function capture(xml: string) {
  const moddle = new BpmnModdle(), parsed = await moddle.fromXML(xml);
  expect(parsed.warnings).toEqual([]);
  return (await moddle.toXML(parsed.rootElement)).xml as string;
}
async function change(actions: readonly NativeCommandAction[], editNative: (document: XMLDocument, index: Map<string, Element>) => void, source = owner()) {
  const { baseline } = await modeled(source);
  const intended = edit(baseline.xml, editNative), requested = await capturePropertyActions(baseline, actions, intended);
  const next = await capture(intended);
  const commands = mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: requested }, next);
  const plan = authoring.planBpmnDocumentSyntaxChange(source, commands);
  const candidate = authoring.applyBpmnDocumentTransaction(source, plan.transaction);
  return { baseline, next, commands, candidate, parsed: authoring.readBpmnDocument(candidate), plan };
}
describe("unenabled full-source modeled command differ", () => {
  it("creates a task and its DI with source-only insertion, preserving opaque configuration and other diagrams", async () => {
    const result = await change([{ command: "shape.create", elementId: "created", diId: "created-di", parentId: "process" }], (_document, byId) => {
      append(byId.get("process")!, '<b:userTask id="created" name="Created"/>');
      append(byId.get("plane")!, '<bpmndi:BPMNShape id="created-di" bpmnElement="created"><dc:Bounds x="180" y="240" width="100" height="80"/></bpmndi:BPMNShape>');
    });
    expect(result.parsed.byId.get("created")?.localName).toBe("userTask");
    expect(result.parsed.byId.get("created-di")?.firstElementChild?.getAttribute("x")).toBe("180");
    expect(result.parsed.payload.documentXml).toContain('v:flag="qualified" flag="unqualified"');
    expect(result.parsed.payload.documentXml).toContain('xmlns:q="urn:inner" ref="q:Inner"');
    expect(result.parsed.byId.has("other-plane")).toBe(true);
    expect(result.candidate.structure!.payload.activities).toBe(result.baseline.projection.owner.structure!.payload.activities);
  });

  it("creates and reconnects a flow with maintained endpoint references", async () => {
    const added = await change([{ command: "connection.create", elementId: "connected", diId: "connected-di", parentId: "process", sourceId: "work", targetId: "end" }], (_document, byId) => {
      append(byId.get("process")!, '<b:sequenceFlow id="connected" sourceRef="work" targetRef="end"/>');
      append(byId.get("work")!, '<b:outgoing>connected</b:outgoing>'); append(byId.get("end")!, '<b:incoming>connected</b:incoming>');
      append(byId.get("plane")!, '<bpmndi:BPMNEdge id="connected-di" bpmnElement="connected"><di:waypoint x="220" y="140"/><di:waypoint x="450" y="118"/></bpmndi:BPMNEdge>');
    });
    expect(added.parsed.byId.get("connected")?.getAttribute("sourceRef")).toBe("work");
    expect(added.parsed.byId.get("work")!.textContent).toContain("connected");
    const moved = await change([{ command: "connection.reconnect", elementId: "flow", diId: "flow-di", parentId: "process", sourceId: "work", targetId: "end" }], (_document, byId) => {
      byId.get("flow")!.setAttribute("targetRef", "end");
      [...byId.get("gateway")!.children].find(child => child.localName === "incoming")!.remove();
      append(byId.get("end")!, '<b:incoming>flow</b:incoming>');
    });
    expect(moved.parsed.byId.get("flow")!.getAttribute("targetRef")).toBe("end");
  });

  it("replaces a bound task type while retaining its exact native binding and opaque attributes", async () => {
    const result = await change([{ command: "shape.replace", oldElementId: "work", newElementId: "work", diId: "work-di", parentId: "process" }], (document, byId) => {
      const old = byId.get("work")!, replacement = document.createElementNS(ns.model, "bpmn:userTask");
      for (const attribute of [...old.attributes]) replacement.setAttributeNS(attribute.namespaceURI, attribute.name, attribute.value);
      while (old.firstChild) replacement.appendChild(old.firstChild); old.replaceWith(replacement);
    });
    expect(result.parsed.byId.get("work")!.localName).toBe("userTask");
    expect(result.candidate.structure!.payload.bindings).toBe(result.baseline.projection.owner.structure!.payload.bindings);
    expect(result.parsed.payload.documentXml).toContain('v:flag="qualified"');
  });

  it("deletes a bound element and incident connection through the owning atomic syntax policy", async () => {
    const result = await change([{ command: "shape.delete", elementId: "work", diId: "work-di" }, { command: "connection.delete", elementId: "flow", diId: "flow-di" }], (_document, byId) => {
      for (const id of ["work", "work-di", "flow", "flow-di"]) byId.get(id)!.remove();
      [...byId.get("gateway")!.children].find(child => child.localName === "incoming")!.remove();
    });
    expect(result.parsed.byId.has("work")).toBe(false);
    expect(result.parsed.payload.bindings).toEqual([]); expect(result.parsed.payload.activities).toEqual([]);
    expect(result.plan.removedNodeIds).toEqual(expect.arrayContaining(["work-native", "listener-native"]));
    expect(result.parsed.payload.documentXml).toContain('<!-- retained source comment -->');
  });

  it.each([
    ['<b:message id="new-message" name="Notice"/>', '<b:messageEventDefinition messageRef="new-message"/>'],
    ['<b:signal id="new-signal" name="Signal"/>', '<b:signalEventDefinition signalRef="new-signal"/>'],
    ["", '<b:timerEventDefinition><b:timeDuration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="b:tFormalExpression">PT5M</b:timeDuration></b:timerEventDefinition>']
  ])("edits idless event configuration and referenced globals (%s)", async (global, event) => {
    const result = await change([{ command: "element.updateModdleProperties", elementId: "end", propertyNames: ["eventDefinitions"] }], (document, byId) => {
      if (global) append(document.documentElement, global);
      append(byId.get("end")!, event);
    });
    expect(result.parsed.byId.get("end")!.children[0].localName).toContain("EventDefinition");
    if (global) expect(result.parsed.byId.size).toBeGreaterThan(result.baseline.projection.reservedIds.size);
  });

  it("edits loop configuration without replacing the owner or its disjoint native payload", async () => {
    const result = await change([{ command: "element.updateModdleProperties", elementId: "work", propertyNames: ["loopCharacteristics"] }], (_document, byId) => {
      append(byId.get("work")!, '<b:multiInstanceLoopCharacteristics isSequential="true"><b:loopCardinality xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="b:tFormalExpression">3</b:loopCardinality></b:multiInstanceLoopCharacteristics>');
    });
    expect(result.parsed.payload.documentXml).toContain('isSequential="true"');
    expect(result.parsed.payload.documentXml).toContain('<html:b>content</html:b>');
  });

  it("reparents an existing unbound node while retaining its source attributes and ordered anchor", async () => {
    const result = await change([{ command: "shape.move", elementId: "inner", diId: "inner-di", oldParentId: "sub", parentId: "process" }], (_document, byId) => {
      byId.get("process")!.insertBefore(byId.get("inner")!, byId.get("gateway")!);
    });
    expect(result.parsed.byId.get("inner")!.parentElement?.getAttribute("id")).toBe("process");
    expect(result.parsed.byId.get("inner")!.nextElementSibling?.getAttribute("id")).toBe("gateway");
    expect(result.commands).toContainEqual(expect.objectContaining({ kind: "reparent", target: { elementId: "inner" }, before: { elementId: "gateway" } }));
  });

  it("defers the exact collaboration/participant/lane plane-retargeting fixture to S10c", async () => {
    const source = owner(), original = structuredClone(source);
    await expect(change([{ command: "canvas.updateRoot", oldElementId: "process", newElementId: "collaboration" },
      { command: "shape.create", elementId: "participant", diId: "participant-di" }, { command: "shape.create", elementId: "lane", diId: "lane-di" },
      { command: "lane.updateRefs", elementIds: ["work", "lane"] }], (document, byId) => {
      append(document.documentElement, '<b:collaboration id="collaboration"><b:participant id="participant" processRef="process"/></b:collaboration>');
      const laneSet = append(byId.get("process")!, '<b:laneSet id="lanes"><b:lane id="lane"><b:flowNodeRef>work</b:flowNodeRef></b:lane></b:laneSet>');
      byId.get("process")!.insertBefore(laneSet, byId.get("work")!);
      byId.get("plane")!.setAttribute("bpmnElement", "collaboration");
      append(byId.get("plane")!, '<bpmndi:BPMNShape id="participant-di" bpmnElement="participant" isHorizontal="true"><dc:Bounds x="30" y="30" width="900" height="500"/></bpmndi:BPMNShape>');
      append(byId.get("plane")!, '<bpmndi:BPMNShape id="lane-di" bpmnElement="lane" isHorizontal="true"><dc:Bounds x="60" y="30" width="870" height="500"/></bpmndi:BPMNShape>');
    }, source)).rejects.toThrow(/S10c/);
    expect(source).toEqual(original);
  });

  it("creates data references/associations, text annotations, groups and category globals", async () => {
    const result = await change([{ command: "shape.create", elementId: "data", parentId: "process" }, { command: "connection.create", elementId: "data-in", canvasTargets: [{ elementId: "data-in", parentId: "process", semanticParentId: "work", sourceId: "data", targetId: "work", semanticSourceIds: ["data"], semanticTargetId: "input" }] },
      { command: "shape.create", elementId: "note", parentId: "process" }, { command: "connection.create", elementId: "association", parentId: "process", sourceId: "work", targetId: "note" }, { command: "shape.create", elementId: "group", parentId: "process" }], (document, byId) => {
      append(byId.get("process")!, '<b:dataObject id="data-object"/>');
      append(byId.get("process")!, '<b:dataObjectReference id="data" dataObjectRef="data-object"/>');
      append(byId.get("work")!, '<b:ioSpecification><b:dataInput id="input"/><b:inputSet><b:dataInputRefs>input</b:dataInputRefs></b:inputSet><b:outputSet/></b:ioSpecification>');
      append(byId.get("work")!, '<b:dataInputAssociation id="data-in"><b:sourceRef>data</b:sourceRef><b:targetRef>input</b:targetRef></b:dataInputAssociation>');
      append(byId.get("process")!, '<b:textAnnotation id="note"><b:text>Annotation</b:text></b:textAnnotation>');
      append(byId.get("process")!, '<b:association id="association" sourceRef="work" targetRef="note"/>');
      append(document.documentElement, '<b:category id="category"><b:categoryValue id="category-value" value="Category"/></b:category>');
      append(byId.get("process")!, '<b:group id="group" categoryValueRef="category-value"/>');
    });
    expect(result.parsed.byId.get("data")!.getAttribute("dataObjectRef")).toBe("data-object");
    expect(result.parsed.byId.get("data-in")!.children).toHaveLength(2);
    expect(result.parsed.byId.get("note")!.textContent).toBe("Annotation");
    expect(result.parsed.byId.get("category-value")!.getAttribute("value")).toBe("Category");
  });

  it("addresses idless definitions properties explicitly and rejects a neighboring unaccounted edit", async () => {
    const source = owner(rawXml.replace(' id="definitions"', ""));
    const result = await change([{ command: "element.updateModdleProperties", moddleDocumentRoot: true, propertyNames: ["name"] }], document => document.documentElement.setAttribute("name", "Definitions name"), source);
    expect(result.commands).toContainEqual({ kind: "attribute", target: { documentRoot: true }, name: "name", value: "Definitions name" });
    const { baseline } = await modeled();
    const next = edit(baseline.xml, (_document, byId) => { byId.get("end")!.setAttribute("name", "End"); byId.get("other")!.setAttribute("isExecutable", "true"); });
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{ command: "element.updateModdleProperties", elementId: "end", properties: { name: "End" } }] }, next)).toThrow(/unaccounted/);
  });

  it("retains structural edits as the next proved baseline after inserted/removed DI", async () => {
    const result = await change([{ command: "shape.create", elementId: "created", diId: "created-di", parentId: "process" }], (_document, byId) => {
      append(byId.get("process")!, '<b:task id="created"/>');
      append(byId.get("plane")!, '<bpmndi:BPMNShape id="created-di" bpmnElement="created"><dc:Bounds x="240" y="300" width="100" height="80"/></bpmndi:BPMNShape>');
    });
    expect(() => bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, result.candidate, scope), result.next, result.baseline)).not.toThrow();
  });

  it("retains canonical DI identity and opaque presentation when pinned replacement creates a different native DI ID", async () => {
    const { baseline } = await modeled();
    const next = await capture(edit(baseline.xml, (document, index) => {
      const old = index.get("work")!, replacement = document.createElementNS(ns.model, "bpmn:userTask");
      for (const attribute of [...old.attributes]) replacement.setAttributeNS(attribute.namespaceURI, attribute.name, attribute.value);
      while (old.firstChild) replacement.appendChild(old.firstChild); old.replaceWith(replacement);
      index.get("work-di")!.setAttribute("id", "replacement-di");
      index.get("work-di")!.firstElementChild!.setAttribute("x", "110");
    }));
    const mapped = mapBpmnNativeCommandPlan(authoring, baseline, { id: 1, trigger: "execute", actions: [
      { command: "shape.replace", oldElementId: "work", newElementId: "work", diId: "replacement-di", parentId: "process" }
    ] }, next);
    const plan = authoring.planBpmnDocumentSyntaxChange(baseline.projection.owner, mapped.commands);
    const candidate = authoring.applyBpmnDocumentTransaction(baseline.projection.owner, plan.transaction);
    const parsed = authoring.readBpmnDocument(candidate);
    expect(parsed.byId.has("replacement-di")).toBe(false);
    expect(parsed.byId.get("work-di")!.firstElementChild!.getAttributeNS("urn:vendor", "geometry")).toBe("retain");
    expect(parsed.byId.get("work-di")!.firstElementChild!.getAttribute("x")).toBe("110");
    const rebound = bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, candidate, scope), next, baseline, mapped.diAliases);
    expect(rebound.di.get("replacement-di")!.id).toBe("work-di");
    const dragged = edit(next, (_document, index) => index.get("replacement-di")!.firstElementChild!.setAttribute("x", "130"));
    expect(mapBpmnNativeCommandGroup(authoring, rebound, { id: 2, trigger: "execute", actions: [{ command: "shape.move", elementId: "work", diId: "replacement-di", parentId: "process" }] }, dragged))
      .toEqual([{ kind: "bounds", shapeId: "work-di", bounds: { x: 130, y: 100, width: 120, height: 80 } }]);
  });

  it("inverts an existing collapsed-plane translation for newly created child DI", async () => {
    const result = await change([{ command: "shape.create", elementId: "new-child", diId: "new-child-di", parentId: "sub" }], (_document, index) => {
      append(index.get("sub")!, '<b:task id="new-child"/>');
      const childPlane = index.get("inner-di")!.parentElement!;
      append(childPlane, '<bpmndi:BPMNShape id="new-child-di" bpmnElement="new-child"><dc:Bounds x="340" y="160" width="100" height="80"/></bpmndi:BPMNShape>');
    });
    const bounds = result.parsed.byId.get("new-child-di")!.firstElementChild!;
    expect(bounds.getAttribute("x")).toBe("810"); expect(bounds.getAttribute("y")).toBe("300");
    expect(result.parsed.byId.get("new-child-di")!.parentElement?.getAttribute("id")).toBe("plane");
    expect(() => bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, result.candidate, scope), result.next, result.baseline)).not.toThrow();
  });

  it("changes a QName property with an explicit fresh declaration while preserving retained opaque prefix meaning", async () => {
    const source = owner(rawXml.replace('<bpmn:serviceTask id="work"', '<bpmn:callActivity calledElement="q:Before" id="work"').replace('</bpmn:serviceTask>', '</bpmn:callActivity>'));
    const result = await change([{ command: "element.updateModdleProperties", elementId: "work", propertyNames: ["calledElement"] }], (document, index) => {
      document.documentElement.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns:newdef", "urn:new-definition");
      index.get("work")!.setAttribute("calledElement", "newdef:After");
    }, source);
    const task = result.parsed.byId.get("work")!;
    expect(task.getAttribute("calledElement")).toBe("newdef:After");
    expect(task.lookupNamespaceURI("newdef")).toBe("urn:new-definition");
    expect(result.commands).toContainEqual({ kind: "namespace", target: { elementId: "work" }, prefix: "newdef", namespaceURI: "urn:new-definition" });
    expect(result.parsed.payload.documentXml).toContain('xmlns:q="urn:inner" ref="q:Inner"');
  });

  it("creates an attached boundary event and replaces a gateway without changing its configured default", async () => {
    const result = await change([{ command: "shape.create", elementId: "boundary", parentId: "process" }, { command: "element.updateAttachment", elementId: "boundary" },
      { command: "shape.replace", oldElementId: "gateway", newElementId: "gateway", parentId: "process" }], (document, index) => {
      append(index.get("process")!, '<b:boundaryEvent id="boundary" attachedToRef="work" cancelActivity="false"><b:timerEventDefinition/></b:boundaryEvent>');
      const old = index.get("gateway")!, replacement = document.createElementNS(ns.model, "bpmn:inclusiveGateway");
      for (const attribute of [...old.attributes]) replacement.setAttributeNS(attribute.namespaceURI, attribute.name, attribute.value);
      while (old.firstChild) replacement.appendChild(old.firstChild); old.replaceWith(replacement);
    });
    expect(result.parsed.byId.get("boundary")!.getAttribute("attachedToRef")).toBe("work");
    expect(result.parsed.byId.get("gateway")!.localName).toBe("inclusiveGateway");
    expect(result.parsed.byId.get("gateway")!.getAttribute("default")).toBe("done");
    expect(result.parsed.payload.bindings).toEqual(result.baseline.projection.owner.structure!.payload.bindings);
  });

  it("edits text annotations and the referenced category value used by a group label", async () => {
    const source = owner(edit(rawXml, (document, index) => {
      append(index.get("process")!, '<b:textAnnotation id="note"><b:text>Before</b:text></b:textAnnotation>');
      const category = append(document.documentElement, '<b:category id="category"><b:categoryValue id="category-value" value="Before"/></b:category>');
      document.documentElement.insertBefore(category, index.get("main")!);
      append(index.get("process")!, '<b:group id="group" categoryValueRef="category-value"/>');
    }));
    const result = await change([{ command: "element.updateLabel", elementId: "note", newLabel: "After" }, { command: "element.updateLabel", elementId: "group", newLabel: "After" }], (_document, index) => {
      index.get("note")!.firstElementChild!.textContent = "After"; index.get("category-value")!.setAttribute("value", "After");
    }, source);
    expect(result.parsed.byId.get("note")!.textContent).toBe("After");
    expect(result.parsed.byId.get("category-value")!.getAttribute("value")).toBe("After");
  });

  it("records explicit authored paint as source attributes while retaining unknown presentation", async () => {
    const modern = "http://www.omg.org/spec/BPMN/non-normative/color/1.0";
    const result = await change([{ command: "element.setColor", elementId: "work", diId: "work-di" }], (document, index) => {
      document.documentElement.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns:color", modern);
      index.get("work-di")!.setAttributeNS(modern, "color:background-color", "#e8f3ff");
      index.get("work-di")!.setAttributeNS(modern, "color:border-color", "#183d5f");
    });
    expect(result.parsed.byId.get("work-di")!.getAttributeNS(modern, "background-color")).toBe("#e8f3ff");
    expect(result.parsed.byId.get("work-di")!.firstElementChild!.getAttributeNS("urn:vendor", "geometry")).toBe("retain");
  });

  it("preserves unchanged duplicate idless properties but refuses ambiguous opaque membership changes", async () => {
    const source = owner(rawXml.replace('<bpmn:outgoing>q:flow</bpmn:outgoing>', '<bpmn:documentation v:key="one">Same</bpmn:documentation><bpmn:documentation v:key="two">Same</bpmn:documentation><bpmn:outgoing>q:flow</bpmn:outgoing>'));
    const result = await change([{ command: "shape.create", elementId: "new-task", parentId: "process" }], (_document, index) => { append(index.get("process")!, '<b:task id="new-task"/>'); }, source);
    expect(result.parsed.payload.documentXml).toContain('v:key="one"'); expect(result.parsed.payload.documentXml).toContain('v:key="two"');
    const { baseline } = await modeled(source);
    const next = edit(baseline.xml, (_document, index) => {
      const docs = [...index.get("work")!.children].filter(child => child.localName === "documentation");
      docs[0].textContent = "Changed"; docs[1].remove();
    });
    const actions = await capturePropertyActions(baseline, [{ command: "element.updateModdleProperties", elementId: "work", propertyNames: ["documentation"] }], next);
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 2, trigger: "execute", actions }, next)).toThrow(/opaque source correspondence/);
  });

  it("refuses ordinal guesses when ambiguous idless members carry opaque metadata on standard descendants", async () => {
    const source = owner(rawXml.replace('<bpmn:endEvent id="end"/>', '<bpmn:intermediateCatchEvent id="end"><bpmn:timerEventDefinition><bpmn:timeDuration v:owner="first">PT1H</bpmn:timeDuration></bpmn:timerEventDefinition><bpmn:timerEventDefinition><bpmn:timeDuration v:owner="second">PT2H</bpmn:timeDuration></bpmn:timerEventDefinition></bpmn:intermediateCatchEvent>'));
    const { baseline } = await modeled(source);
    const next = edit(baseline.xml, (_document, index) => {
      const parent = index.get("end")!, [first, second] = [...parent.children];
      first.firstElementChild!.textContent = "PT3H"; second.firstElementChild!.textContent = "PT4H"; parent.insertBefore(second, first);
    });
    const actions = await capturePropertyActions(baseline, [{ command: "element.updateModdleProperties", elementId: "end", propertyNames: ["eventDefinitions"] }], next);
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions }, next)).toThrow(/opaque|correspondence/);
  });

  it("refuses an unrelated idless property change even when another property on the same activity was requested", async () => {
    const { baseline } = await modeled();
    const next = edit(baseline.xml, (_document, index) => {
      append(index.get("work")!, '<b:documentation>Requested</b:documentation>');
      append(index.get("work")!, '<b:standardLoopCharacteristics/>');
    });
    const intended = edit(baseline.xml, (_document, index) => { append(index.get("work")!, '<b:documentation>Requested</b:documentation>'); });
    const actions = await capturePropertyActions(baseline, [{ command: "element.updateModdleProperties", elementId: "work", propertyNames: ["documentation"] }], intended);
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions }, next)).toThrow(/unaccounted/);
  });

  it("refuses a semantic deletion reported only as a move and refuses off-scope canvas targets", async () => {
    const { baseline } = await modeled();
    const next = edit(baseline.xml, (_document, index) => index.get("work")!.remove());
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.move", elementId: "work", oldParentId: "process", parentId: "sub" }] }, next)).toThrow(/unaccounted/);
    const other = edit(baseline.xml, (_document, index) => index.get("other")!.setAttribute("isExecutable", "true"));
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 2, trigger: "execute", actions: [{ command: "element.updateModdleProperties", elementId: "other", properties: { isExecutable: true } }] }, other)).toThrow(/outside the selected/);
  });

  it("refuses ambiguous replacement DI aliases instead of choosing which source presentation to retarget", async () => {
    const { baseline } = await modeled();
    const next = edit(baseline.xml, (_document, index) => {
      const entry = index.get("work-di")!; entry.setAttribute("id", "one-new-di");
      const duplicate = entry.cloneNode(true) as Element; duplicate.setAttribute("id", "two-new-di"); entry.parentElement!.appendChild(duplicate);
    });
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.replace", oldElementId: "work", newElementId: "work" }] }, next)).toThrow(/ambiguous native\/source DI/);
  });

  it("does not treat vendor metadata's repeated id attribute as a standard identity collision", async () => {
    const source = owner(rawXml.replace('<v:configuration ', '<v:configuration id="work" '));
    const result = await change([{ command: "shape.create", elementId: "new-task", parentId: "process" }], (_document, index) => { append(index.get("process")!, '<b:task id="new-task"/>'); }, source);
    expect(result.parsed.payload.documentXml).toContain('<v:configuration id="work"');
    expect(result.parsed.byId.get("work")!.localName).toBe("serviceTask");
  });

  it("checks the entire modeled delta after mapping instead of accepting an extra unexplained node", async () => {
    const { baseline } = await modeled();
    const next = edit(baseline.xml, (document, index) => {
      append(index.get("process")!, '<b:task id="new-task"/>');
      document.documentElement.appendChild(document.createComment("Unexplained serializer side effect"));
    });
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.create", elementId: "new-task", parentId: "process" }] }, next)).toThrow(/unaccounted XML\/DI/);
  });

  it("projects pinned QName reference families without rewriting their raw lexical context", async () => {
    const source = owner(edit(rawXml, (document, index) => {
      for (const xml of ['<b:itemDefinition id="item"/>', '<b:dataStore id="store" itemSubjectRef="q:item"/>', '<b:category id="category"><b:categoryValue id="category-value" value="Category"/></b:category>']) {
        const value = append(document.documentElement, xml); document.documentElement.insertBefore(value, index.get("process")!);
      }
      index.get("message")!.setAttribute("itemRef", "q:item");
      append(index.get("process")!, '<b:dataStoreReference id="store-ref" dataStoreRef="q:store" itemSubjectRef="q:item"/>');
      append(index.get("process")!, '<b:group id="group" categoryValueRef="q:category-value"/>');
      append(index.get("end")!, '<b:compensateEventDefinition id="compensation" activityRef="q:work"/>');
      append(index.get("work")!, '<b:property id="property" itemSubjectRef="q:item"/>');
      append(index.get("work")!, '<b:multiInstanceLoopCharacteristics oneBehaviorEventRef="q:compensation"><b:loopDataInputRef>q:property</b:loopDataInputRef></b:multiInstanceLoopCharacteristics>');
      append(index.get("main")!, '<bpmndi:BPMNLabelStyle id="label-style"><dc:Font name="Arial" size="14"/></bpmndi:BPMNLabelStyle>');
      [...index.get("gateway-di")!.children].find(child => child.localName === "BPMNLabel")!.setAttribute("labelStyle", "q:label-style");
      index.get("flow-di")!.setAttribute("sourceElement", "q:work-di"); index.get("flow-di")!.setAttribute("targetElement", "q:gateway-di");
    }));
    const { projection } = await modeled(source);
    expect(projection.repairs.map(repair => repair.attribute ?? "text")).toEqual(expect.arrayContaining(["itemRef", "dataStoreRef", "itemSubjectRef", "categoryValueRef", "activityRef", "oneBehaviorEventRef", "text", "labelStyle", "sourceElement", "targetElement"]));
    expect(authoring.readBpmnDocument(source).payload.documentXml).toContain('dataStoreRef="q:store"');
    expect(authoring.readBpmnDocument(source).payload.documentXml).toContain('labelStyle="q:label-style"');
  });

  it("edits an existing idless DC Font through its declared modeled property path", async () => {
    const source = owner(edit(rawXml, (_document, index) => {
      append(index.get("main")!, '<bpmndi:BPMNLabelStyle id="label-style"><dc:Font name="Arial" size="14"/></bpmndi:BPMNLabelStyle>');
      [...index.get("gateway-di")!.children].find(child => child.localName === "BPMNLabel")!.setAttribute("labelStyle", "label-style");
    }));
    const result = await change([{ command: "element.updateModdleProperties", elementId: "gateway", moddleOwnerId: "label-style", moddlePath: ["font"], properties: { size: 18 }, propertyNames: ["size"] }], (_document, index) => {
      index.get("label-style")!.firstElementChild!.setAttribute("size", "18");
    }, source);
    expect(result.parsed.byId.get("label-style")!.firstElementChild!.getAttribute("size")).toBe("18");
    expect(result.parsed.byId.get("label-style")!.firstElementChild!.getAttribute("name")).toBe("Arial");
  });
});
