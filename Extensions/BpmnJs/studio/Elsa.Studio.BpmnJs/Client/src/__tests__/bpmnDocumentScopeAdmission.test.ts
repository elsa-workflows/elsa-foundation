import { expect, it, vi } from "vitest";
import EventBus from "diagram-js/lib/core/EventBus";
import CommandStack from "diagram-js/lib/command/CommandStack";
import ElementRegistry from "diagram-js/lib/core/ElementRegistry";
import CreateShapeHandler from "diagram-js/lib/features/modeling/cmd/CreateShapeHandler";
import CreateConnectionHandler from "diagram-js/lib/features/modeling/cmd/CreateConnectionHandler";
import { create } from "diagram-js/lib/model";
import BpmnFactory from "bpmn-js/lib/features/modeling/BpmnFactory";
import BpmnUpdater from "bpmn-js/lib/features/modeling/BpmnUpdater";
import UpdateModdlePropertiesHandler from "bpmn-js/lib/features/modeling/cmd/UpdateModdlePropertiesHandler";
import { collectBpmnNativeCommandGroups, mapBpmnNativeCommandGroup, type NativeCommandEvents, type NativeCommandGroup } from "../bpmnDocumentCommands";
import { diffBpmnNativeCommandGroup } from "../bpmnDocumentDiff";
import { bindBpmnNativeBaseline, projectBpmnNativeDocument } from "../bpmnDocumentProjection";
import { authoring, edit, modeled, owner, rawXml, scope } from "./bpmnDocumentFixture";

function atomic(native: Awaited<ReturnType<typeof modeled>>, group: NativeCommandGroup, next: string, message = /scope|plane|parent|intent|S10c/) {
  const before = structuredClone(native.projection.owner), apply = vi.spyOn(authoring, "applyBpmnDocumentCommands"), geometry = vi.fn(() => []);
  try {
    expect(() => mapBpmnNativeCommandGroup(authoring, native.baseline, group, next)).toThrow(message);
    expect(() => diffBpmnNativeCommandGroup(authoring, native.baseline, group, next, geometry)).toThrow(message);
    expect(apply).not.toHaveBeenCalled(); expect(geometry).not.toHaveBeenCalled(); expect(native.projection.owner).toEqual(before);
  } finally { apply.mockRestore(); }
}
async function createCore(alternate = false, collaboration = false, endpoints = false) {
  let xml = alternate ? rawXml.replace('</bpmn:definitions>', '<bpmndi:BPMNDiagram id="alternate"><bpmndi:BPMNPlane id="alternate-plane" bpmnElement="process"/></bpmndi:BPMNDiagram></bpmn:definitions>') : rawXml;
  if (endpoints) xml = xml.replace('<bpmn:process id="other"/>', '<bpmn:process id="other"><bpmn:task id="foreign"/></bpmn:process>').replace('<bpmndi:BPMNPlane id="other-plane" bpmnElement="other"/>', '<bpmndi:BPMNPlane id="other-plane" bpmnElement="other"><bpmndi:BPMNShape id="foreign-di" bpmnElement="foreign"><dc:Bounds x="500" y="100" width="100" height="80"/></bpmndi:BPMNShape></bpmndi:BPMNPlane>');
  if (collaboration) xml = xml.replace('<bpmndi:BPMNDiagram id="main">', '<bpmn:collaboration id="collaboration"><bpmn:participant id="participant-a" processRef="process"/><bpmn:participant id="participant-b" processRef="other"/></bpmn:collaboration><bpmndi:BPMNDiagram id="main">').replace('id="plane" bpmnElement="q:process"', 'id="plane" bpmnElement="collaboration"');
  const source = owner(xml);
  const native = await modeled(source), events = new EventBus(), stack = new CommandStack(events, {} as ConstructorParameters<typeof CommandStack>[1]);
  stack.register("shape.create", new CreateShapeHandler({ addShape(shape: { parent?: unknown }, parent: unknown) { shape.parent = parent; } } as ConstructorParameters<typeof CreateShapeHandler>[0]));
  new BpmnUpdater(events, new BpmnFactory(native.moddle), {} as ConstructorParameters<typeof BpmnUpdater>[2]);
  const groups: NativeCommandGroup[] = [], errors: unknown[] = [], detach = collectBpmnNativeCommandGroups(events as unknown as NativeCommandEvents, group => groups.push(group), error => errors.push(error));
  const root = (diagramId: string) => { const di = native.definitions.diagrams.find((entry: { id: string }) => entry.id === diagramId).plane; return create("root", { id: di.bpmnElement.id, type: di.bpmnElement.$type, businessObject: di.bpmnElement, di }); };
  const shape = (id: string, type = "bpmn:Task") => { const businessObject = native.moddle.create(type, { id }), di = native.moddle.create("bpmndi:BPMNShape", { id: `${id}-di`, bpmnElement: businessObject, bounds: native.moddle.create("dc:Bounds"), ...(type === "bpmn:SubProcess" ? { isExpanded: true } : {}) }); return create("shape", { id, type, businessObject, di, width: 100, height: 80 }); };
  const run = (element: ReturnType<typeof shape>, parent: ReturnType<typeof root>) => stack.execute("shape.create", { shape: element, parent, position: { x: 200, y: 200, width: 100, height: 80 } });
  return { ...native, stack, root, shape, run, groups, errors, detach };
}

it.each(["active", "inactive", "mismatched-parent", "alternate-plane"])("qualifies pinned fresh shape creation against captured and active authority (%s)", async mode => {
  const h = await createCore(mode === "alternate-plane"), created = h.shape("created");
  try {
    h.run(created, h.root(mode === "inactive" ? "secondary" : mode === "alternate-plane" ? "alternate" : "main"));
    expect(h.errors).toEqual([]); expect(h.groups).toHaveLength(1);
    const intent = h.groups[0].actions[0].canvasTargets!.find(target => target.elementId === "created")!;
    expect(Object.isFrozen(intent)).toBe(true); expect(intent.semanticParentId).toBe(mode === "inactive" ? "other" : "process");
    if (mode === "mismatched-parent") {
      const previous = created.businessObject.$parent, other = h.root("secondary"); previous.flowElements = previous.flowElements.filter((entry: { id: string }) => entry.id !== "created"); other.businessObject.get("flowElements").push(created.businessObject); created.businessObject.$parent = other.businessObject;
      const plane = created.di.$parent; plane.planeElement = plane.planeElement.filter((entry: { id: string }) => entry.id !== "created-di"); other.di.get("planeElement").push(created.di); created.di.$parent = other.di;
    }
    const next = (await h.moddle.toXML(h.definitions)).xml;
    if (mode !== "active") atomic(h, h.groups[0], next);
    else {
      const candidate = authoring.applyBpmnDocumentCommands(h.projection.owner, mapBpmnNativeCommandGroup(authoring, h.baseline, h.groups[0], next));
      expect(authoring.readBpmnDocument(candidate).byId.get("created")!.parentElement!.getAttribute("id")).toBe("process");
      expect(() => bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, candidate, scope), next, h.baseline)).not.toThrow();
      atomic(h, { ...h.groups[0], actions: h.groups[0].actions.map(action => ({ ...action, parentId: undefined, canvasTargets: undefined })) }, next);
    }
  } finally { h.detach(); }
});

it.each(["semantic-id", "di-id", "inactive-alias"])("refuses fresh/renamed canvas aliases before source preparation (%s)", async alias => {
  const h = await createCore(), created = h.shape("created");
  try {
    h.run(created, h.root("main")); let group = h.groups[0];
    if (alias === "semantic-id") created.businessObject.id = "renamed-created";
    else if (alias === "di-id") created.di.id = "renamed-di";
    else group = { ...group, actions: [...group.actions, { command: "shape.replace", oldElementId: "other", newElementId: "created", parentId: "process" }] };
    atomic(h, group, (await h.moddle.toXML(h.definitions)).xml);
  } finally { h.detach(); }
});

it("qualifies a real same-group expanded container and child through their active root", async () => {
  const h = await createCore(), container = h.shape("container", "bpmn:SubProcess"), child = h.shape("child"), root = h.root("main");
  try {
    h.stack.register("elements.create", { preExecute() { h.run(container, root); h.run(child, container); }, execute() { return [container, child]; } });
    h.stack.execute("elements.create", { elements: [container, child], parent: root });
    expect(h.errors).toEqual([]); expect(h.groups).toHaveLength(1);
    const next = (await h.moddle.toXML(h.definitions)).xml, candidate = authoring.applyBpmnDocumentCommands(h.projection.owner, mapBpmnNativeCommandGroup(authoring, h.baseline, h.groups[0], next));
    expect(authoring.readBpmnDocument(candidate).byId.get("child")!.parentElement!.getAttribute("id")).toBe("container");
    expect(() => bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, candidate, scope), next, h.baseline)).not.toThrow();
  } finally { h.detach(); }
});

it.each(["active", "inactive", "mismatch", "collaboration"])("qualifies pinned connection endpoint intent and active-view authority (%s)", async mode => {
  const h = await createCore(false, mode === "collaboration", true), root = h.root("main");
  try {
    const selected = mode === "collaboration" ? { ...scope, scopeElementId: "collaboration" } : scope;
    const projection = projectBpmnNativeDocument(authoring, h.projection.owner, selected), baseline = bindBpmnNativeBaseline(authoring, projection, h.baseline.xml);
    const native = { ...h, projection, baseline }, all = h.definitions.rootElements.flatMap((element: { flowElements?: { id: string }[] }) => element.flowElements ?? []);
    const endpoint = (id: string) => create("shape", { id, businessObject: all.find((entry: { id: string }) => entry.id === id), di: h.definitions.diagrams.flatMap((diagram: { plane: { planeElement?: { bpmnElement: { id: string } }[] } }) => diagram.plane.planeElement ?? []).find((entry: { bpmnElement: { id: string } }) => entry.bpmnElement.id === id) });
    const businessObject = h.moddle.create(mode === "collaboration" ? "bpmn:MessageFlow" : "bpmn:SequenceFlow", { id: "new-flow" }), connection = create("connection", { id: "new-flow", type: businessObject.$type, businessObject, di: h.moddle.create("bpmndi:BPMNEdge", { id: "new-flow-di", bpmnElement: businessObject }), waypoints: [{ x: 220, y: 140 }, { x: 450, y: 118 }] });
    h.stack.register("connection.create", new CreateConnectionHandler({ addConnection(element: { parent?: unknown }, parent: unknown) { element.parent = parent; } } as ConstructorParameters<typeof CreateConnectionHandler>[0], {} as ConstructorParameters<typeof CreateConnectionHandler>[1]));
    h.stack.execute("connection.create", { connection, source: endpoint("work"), target: endpoint(mode === "inactive" || mode === "collaboration" ? "foreign" : "end"), parent: root, cropped: true });
    expect(h.errors).toEqual([]); expect(h.groups).toHaveLength(1);
    if (mode === "mismatch") {
      const previous = businessObject.targetRef, work = all.find((entry: { id: string }) => entry.id === "work"); previous.incoming = previous.incoming.filter((entry: { id: string }) => entry.id !== "new-flow"); work.get("incoming").push(businessObject); businessObject.targetRef = work;
    }
    const next = (await h.moddle.toXML(h.definitions)).xml;
    if (mode === "inactive" || mode === "mismatch") atomic(native, h.groups[0], next);
    else {
      const candidate = authoring.applyBpmnDocumentCommands(h.projection.owner, mapBpmnNativeCommandGroup(authoring, baseline, h.groups[0], next));
      expect(authoring.readBpmnDocument(candidate).byId.get("new-flow")!.getAttribute("targetRef")).toBe(mode === "collaboration" ? "foreign" : "end");
      expect(() => bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, candidate, selected), next, baseline)).not.toThrow();
    }
  } finally { h.detach(); }
});

it.each([false, true])("refuses actual empty-plane identity replacement before any reconciliation (retarget=%s)", async retarget => {
  const native = await modeled(), selected = { ...scope, diagramId: "secondary", scopeElementId: "other" }, projection = projectBpmnNativeDocument(authoring, native.projection.owner, selected), baseline = bindBpmnNativeBaseline(authoring, projection, native.baseline.xml);
  const events = new EventBus(), stack = new CommandStack(events, {} as ConstructorParameters<typeof CommandStack>[1]), groups: NativeCommandGroup[] = [], errors: unknown[] = [];
  stack.register("element.updateModdleProperties", new UpdateModdlePropertiesHandler(new ElementRegistry(events)));
  const plane = native.definitions.diagrams.find((entry: { id: string }) => entry.id === "secondary").plane, other = plane.bpmnElement, process = native.definitions.rootElements.find((entry: { id: string }) => entry.id === "process");
  const detach = collectBpmnNativeCommandGroups(events as unknown as NativeCommandEvents, group => groups.push(group), error => errors.push(error));
  try {
    stack.execute("element.updateModdleProperties", { element: { id: "other", businessObject: other, di: plane }, moddleElement: plane, properties: { id: "replacement-plane", bpmnElement: retarget ? process : other } });
    expect(errors).toEqual([]); expect(groups).toHaveLength(1);
    const group: NativeCommandGroup = { ...groups[0], actions: [{ command: "canvas.updateRoot", oldElementId: "other", newElementId: retarget ? "process" : "other" }, ...groups[0].actions] };
    atomic({ ...native, projection, baseline }, group, (await native.moddle.toXML(native.definitions)).xml, /S10c/);
  } finally { detach(); }
});

it.each(["create", "remove"])("defers complete empty-plane membership changes atomically (%s)", async operation => {
  const native = await modeled(), next = edit(native.baseline.xml, (document, index) => { if (operation === "remove") index.get("other-plane")!.remove(); else { const diagram = document.createElementNS(authoring.bpmnNamespaces.diagram, "bpmndi:BPMNDiagram"), plane = document.createElementNS(authoring.bpmnNamespaces.diagram, "bpmndi:BPMNPlane"); diagram.setAttribute("id", "new-diagram"); plane.setAttribute("id", "new-plane"); plane.setAttribute("bpmnElement", "process"); diagram.append(plane); document.documentElement.append(diagram); } });
  atomic(native, { id: 1, trigger: "execute", actions: [{ command: "canvas.updateRoot", oldElementId: "process", newElementId: "process" }] }, next, /S10c/);
});

it.each(["main", "secondary"])("protects the owning authored diagram identity before reconciliation (%s)", async diagramId => {
  const native = await modeled(), selected = { ...scope, diagramId, scopeElementId: diagramId === "main" ? "process" : "other" }, projection = projectBpmnNativeDocument(authoring, native.projection.owner, selected), baseline = bindBpmnNativeBaseline(authoring, projection, native.baseline.xml);
  const diagram = native.definitions.diagrams.find((entry: { id: string }) => entry.id === diagramId), businessObject = diagram.plane.bpmnElement, events = new EventBus(), stack = new CommandStack(events, {} as ConstructorParameters<typeof CommandStack>[1]);
  const groups: NativeCommandGroup[] = [], errors: unknown[] = [], detach = collectBpmnNativeCommandGroups(events as unknown as NativeCommandEvents, group => groups.push(group), error => errors.push(error));
  stack.register("element.updateModdleProperties", new UpdateModdlePropertiesHandler(new ElementRegistry(events)));
  try {
    stack.execute("element.updateModdleProperties", { element: { id: businessObject.id, businessObject, di: diagram.plane }, moddleElement: diagram, properties: { id: "replacement-diagram" } });
    expect(errors).toEqual([]); expect(groups).toHaveLength(1);
    atomic({ ...native, projection, baseline }, { ...groups[0], actions: [{ command: "canvas.updateRoot", oldElementId: businessObject.id, newElementId: businessObject.id }, ...groups[0].actions] }, (await native.moddle.toXML(native.definitions)).xml, /S10c/);
  } finally { detach(); }
});
