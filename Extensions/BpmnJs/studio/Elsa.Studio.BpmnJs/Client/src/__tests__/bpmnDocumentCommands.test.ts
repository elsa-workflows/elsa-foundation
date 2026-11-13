import { describe, expect, it, vi } from "vitest";
import { collectBpmnNativeCommandGroups, mapBpmnNativeCommandGroup, type NativeCommandEvents, type NativeCommandGroup } from "../bpmnDocumentCommands";
import { bindBpmnNativeBaseline, projectBpmnNativeDocument } from "../bpmnDocumentProjection";
import { authoring, edit, modeled, owner, rawXml, scope } from "./bpmnDocumentFixture";

const group = (actions: NativeCommandGroup["actions"]): NativeCommandGroup => ({ id: 1, trigger: "execute", actions });
describe("unenabled schema2 complete native command mapping", () => {
  it("maps one complete rename/default/DI group through owning commands and retains opaque raw source/native configuration", async () => {
    const { baseline } = await modeled();
    const next = edit(baseline.xml, (_document, byId) => {
      byId.get("gateway")!.setAttribute("name", "Edited route");
      byId.get("gateway")!.setAttribute("default", "high");
      byId.get("gateway-di")!.children[1].children[0].setAttribute("width", "110");
    });
    const commands = mapBpmnNativeCommandGroup(authoring, baseline, group([
      { command: "element.updateLabel", elementId: "gateway", diId: "gateway-di", newLabel: "Edited route" },
      { command: "shape.resize", elementId: "gateway", diId: "gateway-di", label: true },
      { command: "element.updateProperties", elementId: "gateway", properties: { default: "high" } }
    ]), next);
    expect(commands).toHaveLength(3);
    const previous = baseline.projection.owner;
    const candidate = authoring.applyBpmnDocumentCommands(previous, commands);
    const parsed = authoring.readBpmnDocument(candidate);
    expect(parsed.byId.get("gateway")!.getAttribute("default")).toBe("high");
    expect(parsed.byId.get("gateway")!.getAttribute("name")).toBe("Edited route");
    expect(candidate.structure!.payload.activities).toBe(previous.structure!.payload.activities);
    expect(candidate.structure!.payload.bindings).toBe(previous.structure!.payload.bindings);
    expect(parsed.payload.documentXml).toContain('v:flag="qualified" flag="unqualified"');
    expect(parsed.payload.documentXml).toContain('xmlns:q="urn:inner" ref="q:Inner"');
    expect(parsed.payload.documentXml).toContain('before <v:bold/> after');
    expect(parsed.payload.documentXml).toContain('<!-- retained source comment -->');
    expect(parsed.payload.documentXml).toContain('<?vendor retained?>');
    expect(parsed.payload.documentXml).toContain('q:flow');
  });

  it("maps a drag including nested edge and label effects, and refuses the same delta when one nested command is missing", async () => {
    const { baseline } = await modeled();
    const next = edit(baseline.xml, (_document, byId) => {
      byId.get("gateway-di")!.children[0].setAttribute("x", "320.25");
      byId.get("gateway-di")!.children[1].children[0].setAttribute("x", "310.25");
      byId.get("flow-di")!.children[1].setAttribute("x", "320.25");
    });
    const move = { command: "shape.move", elementId: "gateway", diId: "gateway-di", oldParentId: "process", parentId: "process" };
    const label = { command: "shape.move", elementId: "gateway", diId: "gateway-di", label: true };
    const route = { command: "connection.layout", elementId: "flow", diId: "flow-di" };
    const commands = mapBpmnNativeCommandGroup(authoring, baseline, group([move, label, route]), next);
    expect(commands.map(command => command.kind)).toEqual(["bounds", "labelBounds", "waypoints"]);
    const candidate = authoring.applyBpmnDocumentCommands(baseline.projection.owner, commands);
    expect(authoring.readBpmnDocument(candidate).payload.documentXml).toContain('v:pointKey="source"');
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, group([move, label]), next)).toThrow(/unaccounted XML\/DI/);
  });

  it("inverts recorded import geometry offsets instead of persisting auxiliary plane coordinates", async () => {
    const { projection, baseline } = await modeled();
    const imported = edit(baseline.xml, (_document, byId) => {
      const bounds = byId.get("inner-di")!.children[0]; bounds.setAttribute("x", "180"); bounds.setAttribute("y", "160");
    });
    const normalized = bindBpmnNativeBaseline(authoring, projection, imported);
    const next = edit(imported, (_document, byId) => byId.get("inner-di")!.children[0].setAttribute("x", "200"));
    expect(mapBpmnNativeCommandGroup(authoring, normalized, group([{ command: "shape.move", elementId: "inner", diId: "inner-di", parentId: "sub" }]), next))
      .toEqual([{ kind: "bounds", shapeId: "inner-di", bounds: { x: 670, y: 300, width: 100, height: 80 } }]);
    const candidate = authoring.applyBpmnDocumentCommands(projection.owner, [{ kind: "bounds", shapeId: "inner-di", bounds: { x: 670, y: 300, width: 100, height: 80 } }]);
    const nextProjection = projectBpmnNativeDocument(authoring, candidate, scope);
    expect(() => bindBpmnNativeBaseline(authoring, nextProjection, next)).toThrow(/normalization ledger/);
    expect(bindBpmnNativeBaseline(authoring, nextProjection, next, normalized).di.get("inner-di")!.boundsOffset).toEqual({ x: -470, y: -140 });
  });

  it("does not treat unchanged modeled serialization as permission to rewrite canonical XML", async () => {
    const { baseline } = await modeled();
    expect(mapBpmnNativeCommandGroup(authoring, baseline, group([{ command: "element.updateProperties", elementId: "work", properties: { name: "Original" } }]), baseline.xml)).toEqual([]);
  });

  it("refuses a retained semantic/DI target outside the selected view authority", async () => {
    const { baseline } = await modeled();
    const selected = bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, baseline.projection.owner,
      { ...scope, diagramId: "secondary", scopeElementId: "other" }), baseline.xml);
    const next = edit(baseline.xml, (_document, byId) => byId.get("work-di")!.children[0].setAttribute("x", "200"));
    expect(() => mapBpmnNativeCommandGroup(authoring, selected, group([{ command: "shape.move", diId: "work-di" }]), next)).toThrow(/outside the selected/);
    expect(() => mapBpmnNativeCommandGroup(authoring, selected, group([{ command: "element.updateProperties", elementId: "work", properties: { name: "Original" } }]), baseline.xml)).toThrow(/outside the selected/);
  });

  it.each(["shape.custom", "element.unsupported", "extension.command"])("refuses unmapped %s without an optimistic canonical edit", async command => {
    const { baseline } = await modeled();
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, group([{ command, elementId: "work" }]), baseline.xml)).toThrow(/unmapped native command/);
  });

  it("refuses an extra standard attribute, misleading property intent and unmapped route metadata", async () => {
    const { baseline } = await modeled();
    const extra = edit(baseline.xml, (_document, byId) => byId.get("process")!.setAttribute("isExecutable", "true"));
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, group([{ command: "shape.move", elementId: "work", diId: "work-di", parentId: "process" }]), extra)).toThrow(/unaccounted/);
    const name = edit(baseline.xml, (_document, byId) => byId.get("work")!.setAttribute("name", "Different"));
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, group([{ command: "element.updateProperties", elementId: "work", properties: { name: "Expected" } }]), name)).toThrow(/command intent/);
    const route = edit(baseline.xml, (_document, byId) => {
      const edge = byId.get("flow-di")!, point = edge.children[0].cloneNode(true) as Element;
      point.setAttributeNS("urn:vendor", "vendor:new", "unmapped"); edge.insertBefore(point, edge.children[1]);
    });
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, group([{ command: "connection.layout", diId: "flow-di" }]), route)).toThrow(/unmapped metadata/);
  });

  it("maps rerouted endpoint roles across inserted bends while preserving endpoint source metadata", async () => {
    const source = owner(rawXml.replace('<di:waypoint x="300" y="125"/>', '<di:waypoint x="300" y="125" v:pointKey="target"/>'));
    const { projection, baseline } = await modeled(source);
    const next = edit(baseline.xml, (document, byId) => {
      const edge = byId.get("flow-di")!, last = edge.children[1];
      for (const position of [{ x: 250, y: 140 }, { x: 250, y: 125 }]) {
        const point = document.createElementNS(authoring.bpmnNamespaces.coordinates, "di:waypoint");
        point.setAttribute("x", String(position.x)); point.setAttribute("y", String(position.y)); edge.insertBefore(point, last);
      }
    });
    const commands = mapBpmnNativeCommandGroup(authoring, baseline, group([{ command: "connection.layout", diId: "flow-di" }]), next);
    expect(commands).toEqual([{ kind: "waypoints", edgeId: "flow-di", points: [{ x: 220, y: 140 }, { x: 250, y: 140 }, { x: 250, y: 125 }, { x: 300, y: 125 }], sourceIndices: [0, null, null, 1] }]);
    const candidate = authoring.applyBpmnDocumentCommands(source, commands);
    const waypoints = authoring.readBpmnDocument(candidate).byId.get("flow-di")!.children;
    expect([...waypoints].map(point => point.getAttributeNS("urn:vendor", "pointKey"))).toEqual(["source", null, null, "target"]);
    expect(() => bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, candidate, projection.scope), next, baseline)).not.toThrow();
  });

  it("retains an opaque old bend until the native intent provides its explicit vertex correspondence", async () => {
    const source = owner(rawXml.replace('<di:waypoint x="300" y="125"/>', '<di:waypoint x="260" y="140" v:pointKey="opaque-bend"/><di:waypoint x="300" y="125"/>'));
    const { baseline } = await modeled(source);
    const next = edit(baseline.xml, (_document, byId) => byId.get("flow-di")!.children[1].remove());
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, group([{ command: "connection.layout", diId: "flow-di" }]), next)).toThrow(/unmapped opaque source bend/);
    expect(source.structure!.payload.documentXml).toContain('v:pointKey="opaque-bend"');
  });

  it("refuses unaccounted namespace rebinding even when QName value strings remain identical", async () => {
    const { baseline } = await modeled();
    const next = edit(baseline.xml, document => document.documentElement.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns:q", "urn:changed"));
    expect(() => mapBpmnNativeCommandGroup(authoring, baseline, group([{ command: "shape.move", diId: "work-di" }]), next)).toThrow(/unaccounted/);
  });

  it("collects a complete pinned event group, copies mutable contexts, and rejects native undo", () => {
    const listeners = new Map<string, (event: Parameters<Parameters<NativeCommandEvents["on"]>[2]>[0]) => void>();
    const events: NativeCommandEvents = { on: vi.fn((name, _priority, handler) => { listeners.set(name, handler); }), off: vi.fn() };
    const complete = vi.fn(), refuse = vi.fn();
    const detach = collectBpmnNativeCommandGroups(events, complete, refuse);
    const target = { id: "gateway", businessObject: { id: "gateway" }, di: { id: "gateway-di" }, parent: { id: "process" } };
    const label = { id: "gateway_label", businessObject: target.businessObject, di: target.di, labelTarget: target, parent: target.parent };
    const context = { element: label, newLabel: "Renamed" };
    listeners.get("commandStack.preExecute")!({ id: 7, command: "element.updateLabel", context });
    listeners.get("commandStack.preExecute")!({ id: 7, command: "shape.resize", context: { shape: label } });
    expect(complete).not.toHaveBeenCalled();
    listeners.get("commandStack.changed")!({ trigger: "execute" });
    context.newLabel = "Later";
    expect(complete).toHaveBeenCalledWith({ id: 7, trigger: "execute", actions: [
      expect.objectContaining({ command: "element.updateLabel", elementId: "gateway", diId: "gateway-di", label: true, newLabel: "Renamed" }),
      expect.objectContaining({ command: "shape.resize", elementId: "gateway", label: true, oldParentId: "process", parentId: "process" })
    ] });
    listeners.get("commandStack.changed")!({ trigger: "undo" });
    expect(refuse).toHaveBeenCalledOnce();
    detach(); expect(events.off).toHaveBeenCalledTimes(5);
  });
});
