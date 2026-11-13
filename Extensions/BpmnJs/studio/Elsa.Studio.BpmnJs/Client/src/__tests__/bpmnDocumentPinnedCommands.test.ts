import { describe, expect, it, vi } from "vitest";
import bpmnPackage from "bpmn-js/package.json";
import diagramPackage from "diagram-js/package.json";
import EventBus from "diagram-js/lib/core/EventBus";
import ElementRegistry from "diagram-js/lib/core/ElementRegistry";
import CommandStack from "diagram-js/lib/command/CommandStack";
import ChangeSupport from "diagram-js/lib/features/change-support/ChangeSupport";
import MoveShapeHandler from "diagram-js/lib/features/modeling/cmd/MoveShapeHandler";
import ResizeShapeHandler from "diagram-js/lib/features/modeling/cmd/ResizeShapeHandler";
import { create } from "diagram-js/lib/model";
import BpmnFactory from "bpmn-js/lib/features/modeling/BpmnFactory";
import ElementFactory from "bpmn-js/lib/features/modeling/ElementFactory";
import Modeling from "bpmn-js/lib/features/modeling/Modeling";
import BpmnUpdater from "bpmn-js/lib/features/modeling/BpmnUpdater";
import UpdateLabelHandler from "bpmn-js/lib/features/label-editing/cmd/UpdateLabelHandler";
import type { Shape } from "bpmn-js/lib/model/Types";
import { collectBpmnNativeCommandGroups, mapBpmnNativeCommandGroup, type NativeCommandEvents, type NativeCommandGroup } from "../bpmnDocumentCommands";
import { authoring, modeled, owner, rawXml } from "./bpmnDocumentFixture";
import { nativeModelingCommands } from "../bpmnDocumentDiff";

/** Actual pinned handlers/stack/updater; graphics are deliberately inert, with no rendered-import claim. */
async function commandCore() {
  expect(bpmnPackage.version).toBe("18.31.1");
  expect(diagramPackage.version).toBe("15.28.1");
  const source = owner(rawXml.replace('id="sub-di" bpmnElement="sub"', 'id="sub-di" bpmnElement="sub" isExpanded="true"'));
  const native = await modeled(source), eventBus = new EventBus();
  const stack = new CommandStack(eventBus, {} as ConstructorParameters<typeof CommandStack>[1]);
  const factory = new BpmnFactory(native.moddle), elementFactory = new ElementFactory(factory, native.moddle);
  const modeling = new Modeling(eventBus, elementFactory, stack, {} as ConstructorParameters<typeof Modeling>[3]);
  const textRenderer = { getExternalLabelBounds() { throw new Error("Explicit bounds must avoid text measurement"); } } as unknown as ConstructorParameters<typeof UpdateLabelHandler>[1];
  stack.register("element.updateLabel", new UpdateLabelHandler(modeling, textRenderer, factory));
  stack.register("shape.resize", new ResizeShapeHandler(modeling));
  stack.register("shape.move", new MoveShapeHandler(modeling));
  new BpmnUpdater(eventBus, factory, {} as ConstructorParameters<typeof BpmnUpdater>[2]);
  const plane = native.definitions.diagrams.find((diagram: { id: string }) => diagram.id === "main").plane;
  const root = create("root", { id: "process", type: "bpmn:Process", businessObject: plane.bpmnElement, di: plane });
  const registry = new ElementRegistry(eventBus), graphics = { update: vi.fn(), updateContainments: vi.fn() };
  new ChangeSupport(eventBus, { getRootElement: () => root } as unknown as ConstructorParameters<typeof ChangeSupport>[1], registry,
    graphics as unknown as ConstructorParameters<typeof ChangeSupport>[3]);
  const shape = (id: string, parent = root) => {
    const di = plane.planeElement.find((entry: { bpmnElement?: { id: string } }) => entry.bpmnElement?.id === id);
    const value = create("shape", { id, type: di.bpmnElement.$type, businessObject: di.bpmnElement, di, parent,
      x: di.bounds.x, y: di.bounds.y, width: di.bounds.width, height: di.bounds.height }) as Shape;
    registry.add(value, document.createElementNS("http://www.w3.org/2000/svg", "g"));
    return value;
  };
  const gateway = shape("gateway"), sub = shape("sub"), inner = shape("inner", sub);
  const label = create("label", { id: "gateway_label", type: "label", businessObject: gateway.businessObject,
    di: gateway.di, labelTarget: gateway, parent: root, ...gateway.di.label.bounds });
  registry.add(label, document.createElementNS("http://www.w3.org/2000/svg", "g"));
  const groups: NativeCommandGroup[] = [], refused = vi.fn();
  const detach = collectBpmnNativeCommandGroups(eventBus as unknown as NativeCommandEvents, group => groups.push(group), refused);
  return { ...native, source, modeling, gateway, sub, inner, groups, refused, detach, graphics };
}

describe("pinned native command core", () => {
  it("accounts for the exact handler registry of the pinned modeling implementation", () => {
    expect([...nativeModelingCommands].sort()).toEqual(Object.keys(Modeling.prototype.getHandlers.call({} as Modeling)).sort());
  });

  it("collects a real nested label resize and reconciles actual moddle serialization without replacing source", async () => {
    const h = await commandCore(), before = structuredClone(h.source);
    try {
      h.modeling.updateLabel(h.gateway, "Native route", { x: 280.5, y: 165.25, width: 110, height: 24 });
      expect(h.refused).not.toHaveBeenCalled();
      expect(h.groups).toHaveLength(1);
      expect(h.groups[0].actions.map(action => action.command)).toEqual(["element.updateLabel", "shape.resize"]);
      expect(h.groups[0].actions[1]).toMatchObject({ label: true, elementId: "gateway", diId: "gateway-di" });
      const saved = await h.moddle.toXML(h.definitions), commands = mapBpmnNativeCommandGroup(authoring, h.baseline, h.groups[0], saved.xml);
      expect(commands).toEqual([
        { kind: "attribute", target: { elementId: "gateway" }, name: "name", value: "Native route" },
        { kind: "labelBounds", diId: "gateway-di", bounds: { x: 280.5, y: 165.25, width: 110, height: 24 } }
      ]);
      const candidate = authoring.applyBpmnDocumentCommands(h.source, commands), parsed = authoring.readBpmnDocument(candidate);
      expect(parsed.payload.activities).toEqual(authoring.readBpmnDocument(before).payload.activities);
      expect(parsed.payload.bindings).toEqual(authoring.readBpmnDocument(before).payload.bindings);
      expect(parsed.byId.get("work")!.getAttributeNS("urn:vendor", "flag")).toBe("qualified");
      const opaque = parsed.byId.get("work")!.getElementsByTagNameNS("urn:vendor", "configuration")[0];
      expect(opaque.textContent).toBe("before  after");
      expect(opaque.getAttribute("ref")).toBe("q:Inner");
      expect(opaque.lookupNamespaceURI("q")).toBe("urn:inner");
      expect(parsed.payload.documentXml).toContain("<?vendor retained?>");
      expect(h.source).toEqual(before);
    } finally { h.detach(); }
  });

  it("collects real recursive moves and refuses their serialized delta when a nested action is missing", async () => {
    const h = await commandCore();
    try {
      h.modeling.moveShape(h.sub, { x: 10.5, y: 20.25 }, undefined, undefined, { layout: false });
      expect(h.refused).not.toHaveBeenCalled();
      expect(h.groups).toHaveLength(1);
      expect(h.groups[0].actions.map(action => [action.command, action.elementId])).toEqual([["shape.move", "sub"], ["shape.move", "inner"]]);
      const saved = await h.moddle.toXML(h.definitions), group = h.groups[0];
      const commands = mapBpmnNativeCommandGroup(authoring, h.baseline, group, saved.xml);
      expect(commands).toEqual([
        { kind: "bounds", shapeId: "sub-di", bounds: { x: 610.5, y: 120.25, width: 120, height: 80 } },
        { kind: "bounds", shapeId: "inner-di", bounds: { x: 660.5, y: 320.25, width: 100, height: 80 } }
      ]);
      expect(() => mapBpmnNativeCommandGroup(authoring, h.baseline, { ...group, actions: group.actions.slice(0, 1) }, saved.xml)).toThrow(/unaccounted/);
      const candidate = authoring.applyBpmnDocumentCommands(h.source, commands);
      expect(authoring.readBpmnDocument(candidate).byId.get("inner-di")!.firstElementChild!.getAttribute("x")).toBe("660.5");
      expect(h.graphics.update).toHaveBeenCalled();
    } finally { h.detach(); }
  });
});
