import { describe, expect, it, vi } from "vitest";
import EventBus from "diagram-js/lib/core/EventBus";
import BpmnFactory from "bpmn-js/lib/features/modeling/BpmnFactory";
import BaseModeler from "bpmn-js/lib/BaseModeler";
import BpmnCopyPaste from "bpmn-js/lib/features/copy-paste/BpmnCopyPaste";
import ModdleCopy from "bpmn-js/lib/features/copy-paste/ModdleCopy";
import type { ModdleElement } from "bpmn-js/lib/model/Types";
import { collectBpmnNativeCommandGroups, mapBpmnNativeCommandGroup, type NativeCommandAction, type NativeCommandEvents, type NativeCommandGroup } from "../bpmnDocumentCommands";
import { snapshotNativeNamespaces } from "../bpmnDocumentNamespaceContext";
import { authoring, edit, modeled, owner, rawXml } from "./bpmnDocumentFixture";
import { bindBpmnNativeBaseline } from "../bpmnDocumentProjection";

const ns = authoring.bpmnNamespaces, xmlns = "http://www.w3.org/2000/xmlns/";
function source(body = "q:Decision") {
  return owner(rawXml.replace('<bpmn:sequenceFlow id="flow" sourceRef="work" targetRef="gateway"/>',
    `<bpmn:sequenceFlow id="flow" sourceRef="work" targetRef="gateway"><bpmn:conditionExpression xmlns:q="urn:expression">${body}</bpmn:conditionExpression></bpmn:sequenceFlow>`)
    .replace('<bpmn:subProcess id="sub">', '<bpmn:scriptTask id="script" xmlns:q="urn:script"><bpmn:script>q:Script</bpmn:script></bpmn:scriptTask><bpmn:subProcess id="sub" xmlns:q="urn:sub">')
    .replace('<bpmndi:BPMNShape id="sub-di"', '<bpmndi:BPMNShape id="script-di" bpmnElement="script" xmlns:q="urn:script-di"><dc:Bounds x="700" y="500" width="100" height="80"/></bpmndi:BPMNShape><bpmndi:BPMNShape id="sub-di"'));
}
const bodyAction: NativeCommandAction = { command: "element.updateModdleProperties", moddleOwnerId: "flow", moddlePath: ["conditionExpression"], propertyNames: ["body"], properties: { body: "q:Changed" } };
const condition = (document: XMLDocument) => document.getElementsByTagNameNS(ns.model, "conditionExpression")[0];

describe("projection-owned namespace contexts", () => {
  it("accepts actual writer-only namespace loss and retains raw native bytes separately from canonical source", async () => {
    const canonical = source(), before = structuredClone(canonical), result = await modeled(canonical);
    expect(condition(new DOMParser().parseFromString(result.baseline.xml, "application/xml")).lookupNamespaceURI("q")).not.toBe("urn:expression");
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [{ command: "element.updateProperties", elementId: "gateway", properties: { name: "Route" } }] }, result.baseline.xml);
    expect(commands).toEqual([]);
    expect(canonical).toEqual(before);
    expect(result.baseline.xml).not.toBe(result.projection.sourceXml);
  });

  it("edits a retained condition in its original shadowed namespace through a narrow source command", async () => {
    const result = await modeled(source()), before = structuredClone(result.projection.owner);
    const next = edit(result.baseline.xml, document => { condition(document).textContent = "q:Changed"; });
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [bodyAction] }, next);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(result.projection.owner, commands));
    expect(commands.map(command => command.kind)).toEqual(["text"]);
    expect(condition(retained.document).textContent).toBe("q:Changed");
    expect(condition(retained.document).lookupNamespaceURI("q")).toBe("urn:expression");
    expect(retained.payload.activities).toEqual(authoring.readBpmnDocument(before).payload.activities);
    expect(result.projection.owner).toEqual(before);
  });

  it("retains moved scopes and gives new source constructs the accepted parent context", async () => {
    const result = await modeled(source());
    const moved = edit(result.baseline.xml, (_document, index) => index.get("sub")!.appendChild(index.get("script")!));
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [{ command: "shape.move", elementId: "script", oldParentId: "process", parentId: "sub" }] }, moved);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(result.projection.owner, commands));
    expect(retained.byId.get("script")!.parentElement!.getAttribute("id")).toBe("sub");
    expect(retained.byId.get("script")!.firstElementChild!.lookupNamespaceURI("q")).toBe("urn:script");
    const added = edit(result.baseline.xml, (document, index) => {
      const task = document.createElementNS(ns.model, "bpmn:scriptTask"), script = document.createElementNS(ns.model, "bpmn:script");
      task.setAttribute("id", "new-script"); script.textContent = "q:New"; task.append(script); index.get("sub")!.append(task);
    });
    const create = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 2, trigger: "execute", actions: [{ command: "shape.create", elementId: "new-script", parentId: "sub" }] }, added);
    const next = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(result.projection.owner, create));
    expect(next.byId.get("new-script")!.firstElementChild!.lookupNamespaceURI("q")).toBe("urn:sub");
  });

  it("preserves a default namespace used by an unprefixed expression value", async () => {
    const canonical = source("Decision");
    canonical.structure!.payload.documentXml = (canonical.structure!.payload.documentXml as string).replace('conditionExpression xmlns:q="urn:expression"', 'conditionExpression xmlns:q="urn:expression" xmlns="urn:default-expression"');
    const result = await modeled(canonical), next = edit(result.baseline.xml, document => { condition(document).textContent = "Changed"; });
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [{ ...bodyAction, properties: { body: "Changed" } }] }, next);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(canonical, commands));
    expect(condition(retained.document).lookupNamespaceURI(null)).toBe("urn:default-expression");
    expect(condition(retained.document).textContent).toBe("Changed");
  });

  it("retains shadowed call-reference meaning while editing its QName value", async () => {
    const canonical = source();
    canonical.structure!.payload.documentXml = (canonical.structure!.payload.documentXml as string).replace('<bpmn:subProcess id="sub"', '<bpmn:callActivity id="call" xmlns:v="urn:local-call" calledElement="v:Original"/><bpmn:subProcess id="sub"');
    const result = await modeled(canonical), next = edit(result.baseline.xml, (_document, index) => index.get("call")!.setAttribute("calledElement", "v:Changed"));
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [{ command: "element.updateProperties", elementId: "call", properties: { calledElement: "v:Changed" } }] }, next);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(canonical, commands)).byId.get("call")!;
    expect(retained.getAttribute("calledElement")).toBe("v:Changed");
    expect(retained.lookupNamespaceURI("v")).toBe("urn:local-call");
    expect(commands.map(command => command.kind)).toEqual(["attribute"]);
  });

  it("does not bind a previously unbound opaque token split across source text and CDATA", async () => {
    const canonical = owner(rawXml.replace('<bpmn:serviceTask id="work"', '<bpmn:callActivity calledElement="q:Before" id="work"').replace('</bpmn:serviceTask>', '</bpmn:callActivity>')
      .replace('before <v:bold/> after', 'new<![CDATA[def:Kept]]>'));
    const result = await modeled(canonical), next = edit(result.baseline.xml, (document, index) => {
      document.documentElement.setAttributeNS(xmlns, "xmlns:newdef", "urn:new-definition"); index.get("work")!.setAttribute("calledElement", "newdef:After");
    });
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [{ command: "element.updateModdleProperties", elementId: "work", propertyNames: ["calledElement"] }] }, next);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(canonical, commands)), work = retained.byId.get("work")!;
    const opaque = work.getElementsByTagNameNS("urn:vendor", "configuration")[0];
    expect(opaque.textContent).toBe("newdef:Kept"); expect(opaque.lookupNamespaceURI("newdef")).toBeNull();
    const prefix = work.getAttribute("calledElement")!.split(":")[0];
    expect(work.lookupNamespaceURI(prefix)).toBe("urn:new-definition");
  });

  it("preserves exact copied property contexts across a different target shadow", async () => {
    const result = await modeled(source()), snapshot = snapshotNativeNamespaces(authoring, result.projection, "script");
    const next = edit(result.baseline.xml, (_document, index) => { const copy = index.get("script")!.cloneNode(true) as Element; copy.setAttribute("id", "copy"); index.get("sub")!.append(copy); });
    const group = { id: 1, trigger: "execute" as const, actions: [{ command: "shape.create", elementId: "copy", parentId: "sub" }], namespaceOrigins: [{ elementId: "copy", snapshot }] };
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, group, next);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(result.projection.owner, commands));
    expect(retained.byId.get("copy")!.firstElementChild!.textContent).toBe("q:Script");
    expect(retained.byId.get("copy")!.firstElementChild!.lookupNamespaceURI("q")).toBe("urn:script");
    expect(() => mapBpmnNativeCommandGroup(authoring, result.baseline, { ...group, namespaceOrigins: [{ elementId: "copy" }] }, next)).toThrow("correspondence");
  });

  it("keeps repeated idless values attached to their exact namespace context when reordered", async () => {
    for (const text of [["q:First", "q:Second"], ["q:Same", "q:Same"]]) {
      const canonical = source();
      canonical.structure!.payload.documentXml = (canonical.structure!.payload.documentXml as string).replace('<bpmn:script>q:Script', `<bpmn:documentation xmlns:q="urn:first">${text[0]}</bpmn:documentation><bpmn:documentation xmlns:q="urn:second">${text[1]}</bpmn:documentation><bpmn:script>q:Script`);
      const result = await modeled(canonical), reorder = (xml: string, erase = false) => edit(xml, (_document, index) => {
        const task = index.get("script")!, docs = [...task.children].filter(child => child.localName === "documentation");
        if (erase) for (const doc of docs) for (const attribute of [...doc.attributes]) if (attribute.namespaceURI === xmlns) doc.removeAttributeNode(attribute);
        task.insertBefore(docs[1], docs[0]);
      });
      const group = { id: 1, trigger: "execute" as const, actions: [{ command: "element.updateModdleProperties", elementId: "script", propertyNames: ["documentation"] }] };
      const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, group, reorder(result.baseline.xml));
      const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(canonical, commands)), docs = [...retained.byId.get("script")!.children].filter(child => child.localName === "documentation");
      expect(docs.map(doc => [doc.textContent, doc.lookupNamespaceURI("q")])).toEqual([[text[1], "urn:second"], [text[0], "urn:first"]]);
      if (text[0] === text[1]) expect(() => mapBpmnNativeCommandGroup(authoring, result.baseline, group, reorder(result.baseline.xml, true))).toThrow("ambiguous");
    }
  });

  it("captures immutable clone provenance through the actual pinned copy handlers", async () => {
    const result = await modeled(source()), events = new EventBus();
    const aliased = edit(result.baseline.xml, (_document, index) => index.get("script-di")!.setAttribute("id", "native-script-di"));
    const baseline = bindBpmnNativeBaseline(authoring, result.projection, aliased, undefined, new Map([["native-script-di", "script-di"]]));
    // Use the actual modeler factory/ID collector without constructing a rendered SVG host.
    const modeler = BaseModeler.prototype as unknown as { _createModdle(options: object): typeof result.moddle; _collectIds(definitions: unknown, elements: unknown): void };
    const moddle = modeler._createModdle({}), parsed = await moddle.fromXML(baseline.xml);
    modeler._collectIds(parsed.rootElement, parsed.elementsById);
    const factory = new BpmnFactory(moddle);
    new BpmnCopyPaste(factory, events, new ModdleCopy(events, factory, moddle));
    const process = parsed.rootElement.rootElements.find((entry: { id: string }) => entry.id === "process");
    const script = process.flowElements.find((entry: { id: string }) => entry.id === "script") as ModdleElement;
    const descriptor: Record<string, unknown> = { id: "script" }, groups: NativeCommandGroup[] = [], refused = vi.fn();
    const detach = collectBpmnNativeCommandGroups(events as unknown as NativeCommandEvents, group => groups.push(group), refused,
      id => snapshotNativeNamespaces(authoring, result.projection, id, baseline.di));
    try {
      const plane = parsed.rootElement.diagrams.find((diagram: { id: string }) => diagram.id === "main").plane;
      const originalDi = plane.planeElement.find((entry: { id: string }) => entry.id === "native-script-di");
      events.fire("copyPaste.copyElement", { descriptor, element: { id: "script", businessObject: script, di: originalDi } });
      const pasted = { ...descriptor };
      events.fire("copyPaste.pasteElement", { descriptor: pasted, cache: {} });
      const created = pasted.businessObject as ModdleElement;
      expect(created.id).not.toBe(script.id);
      process.flowElements.push(created); created.$parent = process;
      const createdDi = pasted.di as ModdleElement; plane.planeElement.push(createdDi); createdDi.$parent = plane;
      events.fire("commandStack.preExecute", { id: 1, command: "shape.create", context: { shape: { businessObject: created, di: createdDi }, parent: { id: "process" } } });
      events.fire("commandStack.changed", { trigger: "execute" });
      expect(refused).not.toHaveBeenCalled(); expect(groups).toHaveLength(1);
      expect(groups[0].namespaceOrigins![0].snapshot!.xml).toContain('xmlns:q="urn:script"');
      const saved = await moddle.toXML(parsed.rootElement), commands = mapBpmnNativeCommandGroup(authoring, baseline, groups[0], saved.xml);
      const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(result.projection.owner, commands));
      expect(retained.byId.get(created.id)!.firstElementChild!.lookupNamespaceURI("q")).toBe("urn:script");
      expect(retained.byId.get(createdDi.id)!.lookupNamespaceURI("q")).toBe("urn:script-di");
    } finally { detach(); }
  });

  it("keeps originally unbound text unbound across generated XML-name prefix normalization", async () => {
    const result = await modeled(source("generated:Decision")), before = structuredClone(result.projection.owner);
    const next = edit(result.baseline.xml, (document, index) => {
      const current = condition(document), replacement = document.createElementNS(ns.model, "generated:conditionExpression");
      replacement.setAttributeNS(xmlns, "xmlns:generated", ns.model); replacement.textContent = current.textContent;
      for (const attribute of [...current.attributes]) replacement.setAttributeNS(attribute.namespaceURI, attribute.name, attribute.value);
      current.replaceWith(replacement); index.get("gateway")!.setAttribute("name", "Renamed");
    });
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [{ command: "element.updateLabel", elementId: "gateway", newLabel: "Renamed" }] }, next);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(result.projection.owner, commands));
    expect(condition(retained.document).lookupNamespaceURI("generated")).toBeNull();
    expect(condition(retained.document).textContent).toBe("generated:Decision");
    expect(result.projection.owner).toEqual(before);
  });

  it("does not acquire a binding from the actual writer's generated shadow alias", async () => {
    const canonical = source("q_1:Decision"), result = await modeled(canonical);
    expect(condition(new DOMParser().parseFromString(result.baseline.xml, "application/xml")).lookupNamespaceURI("q_1")).toBe("urn:expression");
    const next = edit(result.baseline.xml, (_document, index) => index.get("gateway")!.setAttribute("name", "Renamed"));
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [{ command: "element.updateLabel", elementId: "gateway", newLabel: "Renamed" }] }, next);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(canonical, commands));
    expect(condition(retained.document).lookupNamespaceURI("q_1")).toBeNull();
    expect(condition(retained.document).textContent).toBe("q_1:Decision");
  });

  it("does not acquire a default QName binding when only generated XML names change", async () => {
    const result = await modeled(source("Decision")), next = edit(result.baseline.xml, (document, index) => {
      const current = condition(document), replacement = document.createElementNS(ns.model, "conditionExpression");
      replacement.setAttributeNS(xmlns, "xmlns", ns.model); replacement.textContent = current.textContent;
      for (const attribute of [...current.attributes]) if (attribute.namespaceURI !== xmlns) replacement.setAttributeNS(attribute.namespaceURI, attribute.name, attribute.value);
      current.replaceWith(replacement); index.get("gateway")!.setAttribute("name", "Renamed");
    });
    const commands = mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [{ command: "element.updateLabel", elementId: "gateway", newLabel: "Renamed" }] }, next);
    const retained = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(result.projection.owner, commands));
    expect(condition(retained.document).lookupNamespaceURI(null)).toBeNull();
    expect(condition(retained.document).textContent).toBe("Decision");
  });

  it("refuses a genuine opaque binding mutation and an unrepresentable unbound move atomically", async () => {
    const result = await modeled(source()), before = structuredClone(result.projection.owner);
    const changed = edit(result.baseline.xml, document => { condition(document).setAttributeNS(xmlns, "xmlns:q", "urn:unproven"); condition(document).textContent = "q:Changed"; });
    expect(() => mapBpmnNativeCommandGroup(authoring, result.baseline, { id: 1, trigger: "execute", actions: [bodyAction] }, changed)).toThrow("namespace");
    expect(result.projection.owner).toEqual(before);
    const unbound = source().structure!.payload.documentXml as string;
    const canonical = owner(unbound.replace('xmlns:q="urn:script"', 'xmlns:missing="urn:other"').replace('q:Script', 'absent:Script').replace('xmlns:q="urn:sub"', 'xmlns:q="urn:sub" xmlns:absent="urn:bound"'));
    const move = await modeled(canonical), next = edit(move.baseline.xml, (_document, index) => index.get("sub")!.append(index.get("script")!));
    expect(() => mapBpmnNativeCommandGroup(authoring, move.baseline, { id: 2, trigger: "execute", actions: [{ command: "shape.move", elementId: "script", oldParentId: "process", parentId: "sub" }] }, next)).toThrow("unbound source QName");
    const clone = edit(move.baseline.xml, (_document, index) => { const copied = index.get("script")!.cloneNode(true) as Element; copied.setAttribute("id", "unbound-copy"); index.get("sub")!.append(copied); });
    expect(() => mapBpmnNativeCommandGroup(authoring, move.baseline, { id: 3, trigger: "execute", actions: [{ command: "shape.create", elementId: "unbound-copy", parentId: "sub" }], namespaceOrigins: [{ elementId: "unbound-copy", snapshot: snapshotNativeNamespaces(authoring, move.projection, "script") }] }, clone)).toThrow("unbound source QName");
  });
});
