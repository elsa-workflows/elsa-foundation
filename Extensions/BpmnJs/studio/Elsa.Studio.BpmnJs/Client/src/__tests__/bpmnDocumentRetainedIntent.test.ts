import { expect, it, vi } from "vitest";
import EventBus from "diagram-js/lib/core/EventBus";
import CommandStack from "diagram-js/lib/command/CommandStack";
import MoveShapeHandler from "diagram-js/lib/features/modeling/cmd/MoveShapeHandler";
import MoveConnectionHandler from "diagram-js/lib/features/modeling/cmd/MoveConnectionHandler";
import ReconnectConnectionHandler from "diagram-js/lib/features/modeling/cmd/ReconnectConnectionHandler";
import { create } from "diagram-js/lib/model";
import BpmnFactory from "bpmn-js/lib/features/modeling/BpmnFactory";
import BpmnUpdater from "bpmn-js/lib/features/modeling/BpmnUpdater";
import { collectBpmnNativeCommandGroups, mapBpmnNativeCommandGroup, type NativeCommandEvents, type NativeCommandGroup } from "../bpmnDocumentCommands";
import { diffBpmnNativeCommandGroup } from "../bpmnDocumentDiff";
import { bindBpmnNativeBaseline, projectBpmnNativeDocument } from "../bpmnDocumentProjection";
import { authoring, modeled, owner, rawXml, scope } from "./bpmnDocumentFixture";

async function core() {
  const xml = rawXml.replace('id="sub-di" bpmnElement="sub"', 'id="sub-di" bpmnElement="sub" isExpanded="true"')
    .replace('<bpmn:process id="other"/>', '<bpmn:process id="other"><bpmn:task id="foreign"/></bpmn:process>')
    .replace('<bpmndi:BPMNPlane id="other-plane" bpmnElement="other"/>', '<bpmndi:BPMNPlane id="other-plane" bpmnElement="other"><bpmndi:BPMNShape id="foreign-di" bpmnElement="foreign"><dc:Bounds x="500" y="100" width="100" height="80"/></bpmndi:BPMNShape></bpmndi:BPMNPlane>');
  const h = await modeled(owner(xml)), events = new EventBus(), stack = new CommandStack(events, {} as ConstructorParameters<typeof CommandStack>[1]);
  const plane = h.definitions.diagrams.find((entry: { id: string }) => entry.id === "main").plane, process = plane.bpmnElement;
  const root = create("root", { id: "process", type: process.$type, businessObject: process, di: plane });
  const shape = (id: string, parent = root) => {
    const di = h.definitions.diagrams.flatMap((entry: { plane: { planeElement?: { bpmnElement: { id: string } }[] } }) => entry.plane.planeElement ?? []).find((entry: { bpmnElement: { id: string } }) => entry.bpmnElement.id === id);
    return create("shape", { id, type: di.bpmnElement.$type, businessObject: di.bpmnElement, di, parent, ...di.bounds });
  };
  const work = shape("work"), gateway = shape("gateway"), end = shape("end"), sub = shape("sub"), inner = shape("inner", sub), foreign = shape("foreign");
  const di = plane.planeElement.find((entry: { id: string }) => entry.id === "flow-di"), businessObject = di.bpmnElement;
  const flow = create("connection", { id: "flow", type: businessObject.$type, businessObject, di, parent: root, source: work, target: gateway, waypoints: di.waypoint.map((point: { x: number; y: number }) => ({ x: point.x, y: point.y })) });
  stack.register("shape.move", new MoveShapeHandler({} as ConstructorParameters<typeof MoveShapeHandler>[0]));
  stack.register("connection.move", new MoveConnectionHandler());
  stack.register("connection.reconnect", new ReconnectConnectionHandler({} as ConstructorParameters<typeof ReconnectConnectionHandler>[0]));
  const updater = new BpmnUpdater(events, new BpmnFactory(h.moddle), {} as ConstructorParameters<typeof BpmnUpdater>[2]);
  const groups: NativeCommandGroup[] = [], errors: unknown[] = [], detach = collectBpmnNativeCommandGroups(events as unknown as NativeCommandEvents, group => groups.push(group), error => errors.push(error));
  const move = (parent = root) => stack.execute("shape.move", { shape: inner, newParent: parent, delta: { x: 20, y: 20 }, hints: { layout: false } });
  const reconnect = (target = end) => stack.execute("connection.reconnect", { connection: flow, newTarget: target, dockingOrPoints: flow.waypoints, hints: { layoutConnection: false } });
  return { ...h, events, updater, stack, root, work, end, sub, inner, foreign, flow, move, reconnect, groups, errors, detach };
}
function atomic(h: Awaited<ReturnType<typeof core>>, group: NativeCommandGroup, next: string) {
  const previous = structuredClone(h.projection.owner), apply = vi.spyOn(authoring, "applyBpmnDocumentCommands"), geometry = vi.fn(() => []);
  try {
    expect(() => mapBpmnNativeCommandGroup(authoring, h.baseline, group, next)).toThrow(/intent|scope|parent|endpoint/);
    expect(() => diffBpmnNativeCommandGroup(authoring, h.baseline, group, next, geometry)).toThrow(/intent|scope|parent|endpoint/);
    expect(geometry).not.toHaveBeenCalled(); expect(apply).not.toHaveBeenCalled(); expect(h.projection.owner).toEqual(previous);
  } finally { apply.mockRestore(); }
}
function apply(h: Awaited<ReturnType<typeof core>>, group: NativeCommandGroup, next: string) {
  const candidate = authoring.applyBpmnDocumentCommands(h.projection.owner, mapBpmnNativeCommandGroup(authoring, h.baseline, group, next));
  expect(() => bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, candidate, scope), next, h.baseline)).not.toThrow();
  return authoring.readBpmnDocument(candidate);
}
it.each(["valid", "inactive", "semantic-mismatch", "visual-mismatch", "missing"])("qualifies retained pinned reconnect intent before source preparation (%s)", async mode => {
  const h = await core();
  try {
    h.reconnect(mode === "inactive" ? h.foreign : h.end);
    expect(h.errors).toEqual([]); expect(h.groups).toHaveLength(1);
    const target = h.groups[0].actions[0].canvasTargets![0]; expect(target.semanticTargetId).toBe(mode === "inactive" ? "foreign" : "end"); expect(Object.isFrozen(target)).toBe(true);
    if (mode === "semantic-mismatch") {
      h.end.businessObject.incoming = h.end.businessObject.incoming.filter((entry: { id: string }) => entry.id !== "flow");
      h.work.businessObject.get("incoming").push(h.flow.businessObject); h.flow.businessObject.targetRef = h.work.businessObject;
    }
    const group = mode === "missing" ? { ...h.groups[0], actions: [{ command: "connection.reconnect", elementId: "flow", diId: "flow-di", parentId: "process" }] }
      : mode === "visual-mismatch" ? { ...h.groups[0], actions: h.groups[0].actions.map(action => ({ ...action, requestedTargetId: undefined, canvasTargets: action.canvasTargets!.map(target => ({ ...target, targetId: "work" })) })) } : h.groups[0];
    const next = (await h.moddle.toXML(h.definitions)).xml;
    if (mode === "valid") expect(apply(h, group, next).byId.get("flow")!.getAttribute("targetRef")).toBe("end");
    else atomic(h, group, next);
  } finally { h.detach(); }
});
it.each(["valid", "lost-reparent", "missing", "in-place-missing"])("qualifies retained pinned move intent including unchanged serialized parents (%s)", async mode => {
  const h = await core();
  try {
    h.move(mode === "in-place-missing" ? h.sub : h.root);
    expect(h.errors).toEqual([]); expect(h.groups).toHaveLength(1);
    expect(h.groups[0].actions[0].canvasTargets![0].semanticParentId).toBe(mode === "in-place-missing" ? "sub" : "process");
    if (mode === "lost-reparent") {
      const business = h.inner.businessObject; business.$parent.flowElements = business.$parent.flowElements.filter((entry: { id: string }) => entry.id !== "inner");
      h.sub.businessObject.get("flowElements").push(business); business.$parent = h.sub.businessObject;
    }
    const group = mode.endsWith("missing") ? { ...h.groups[0], actions: [{ command: "shape.move", elementId: "inner", diId: "inner-di" }] } : h.groups[0], next = (await h.moddle.toXML(h.definitions)).xml;
    if (mode === "valid") expect(apply(h, group, next).byId.get("inner")!.parentElement!.getAttribute("id")).toBe("process");
    else atomic(h, group, next);
  } finally { h.detach(); }
});
it("qualifies retained connection movement without discarding its complete endpoint intent", async () => {
  const h = await core();
  try {
    h.stack.execute("connection.move", { connection: h.flow, delta: { x: 20, y: 20 } });
    expect(h.errors).toEqual([]); const next = (await h.moddle.toXML(h.definitions)).xml;
    expect(apply(h, h.groups[0], next).byId.get("flow")!.getAttribute("targetRef")).toBe("gateway");
    atomic(h, { ...h.groups[0], actions: [{ command: "connection.move", elementId: "flow", diId: "flow-di" }] }, next);
  } finally { h.detach(); }
});
it("uses final coalesced structural intent across repeated pinned moves/reconnects and later label metadata", async () => {
  const h = await core();
  try {
    h.stack.register("elements.move", { preExecute() { h.move(h.root); h.move(h.sub); h.reconnect(h.work); h.reconnect(h.end); }, execute() { return [h.inner, h.flow]; } });
    h.stack.execute("elements.move", { elements: [h.inner, h.flow], newParent: h.root });
    expect(h.errors).toEqual([]); expect(h.groups).toHaveLength(1);
    const next = (await h.moddle.toXML(h.definitions)).xml, result = apply(h, { ...h.groups[0], actions: [...h.groups[0].actions, { command: "element.updateLabel", elementId: "flow", newLabel: null }] }, next);
    expect(result.byId.get("inner")!.parentElement!.getAttribute("id")).toBe("sub"); expect(result.byId.get("flow")!.getAttribute("targetRef")).toBe("end");
  } finally { h.detach(); }
});

it.each(["parent", "endpoint"])("retains explicit pinned context requests if their effect is lost before complete capture (%s)", async field => {
  const h = await core();
  try {
    h.events.on("commandStack.changed", 1000, () => {
      if (field === "endpoint") { h.flow.target = h.work; h.updater.updateConnection({ connection: h.flow } as Parameters<typeof h.updater.updateConnection>[0]); }
      else {
        const business = h.inner.businessObject; business.$parent.flowElements = business.$parent.flowElements.filter((entry: { id: string }) => entry.id !== "inner");
        h.sub.businessObject.get("flowElements").push(business); business.$parent = h.sub.businessObject; h.inner.parent = h.sub;
      }
    });
    if (field === "endpoint") h.reconnect(); else h.move();
    expect(h.errors).toEqual([]); expect(h.groups).toHaveLength(1);
    expect(field === "endpoint" ? h.groups[0].actions[0].requestedTargetId : h.groups[0].actions[0].requestedParentId).toBe(field === "endpoint" ? "end" : "process");
    atomic(h, h.groups[0], (await h.moddle.toXML(h.definitions)).xml);
  } finally { h.detach(); }
});
