import { describe, expect, it, vi } from "vitest";
import type { AuthoringCheckpointToken, BpmnDocumentCanvasEditorContext } from "@elsa-workflows/studio-workflows";
import { createBpmnDocumentController } from "../bpmnDocumentController";
import { authoring, deferred, edit, owner, rawXml, scope } from "./bpmnDocumentFixture";
import { createBpmnDocumentSessionHost } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnDocumentSessionHost";
import { createAuthoringCheckpoint } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/authoringCheckpoint";

function control() {
  let source = owner(rawXml.replace('id="sub-di" bpmnElement="sub"', 'id="sub-di" bpmnElement="sub" isExpanded="true"'));
  let token: AuthoringCheckpointToken = { authorityId: "authority", resourceId: "draft", scopeId: "root", revision: 0 };
  let native = "", label = "", active = false, commandId = 0, drag: object | undefined;
  const listeners = new Map<string, { priority: number; handler: (event: Record<string, unknown>) => unknown }[]>();
  const events = { on(name: string, priority: number, handler: (event: Record<string, unknown>) => unknown) {
    listeners.set(name, [...listeners.get(name) ?? [], { priority, handler }].sort((a, b) => b.priority - a.priority));
  }, off() {}, fire(name: string, value: Record<string, unknown> = {}) {
    for (const listener of listeners.get(name) ?? []) if (listener.handler(value) === false) return false;
    return true;
  } };
  const process = { id: "process", businessObject: { id: "process" } }, sub = { id: "sub_plane", businessObject: { id: "sub" } };
  let root = process;
  const canvas = { getRootElements: () => [process, sub], getRootElement: () => root,
    setRootElement: vi.fn((next: typeof process) => { root = next; return root; }),
    viewbox: vi.fn(() => ({})), zoom: vi.fn(), resized: vi.fn() };
  const commands = { execute: vi.fn((command: string, context: Record<string, unknown>) => {
    const id = ++commandId;
    events.fire("commandStack.preExecute", { command, context, id });
    if (context.nextRoot) canvas.setRootElement(context.nextRoot as typeof process);
    native = edit(native, (_document, byId) => byId.get("work")!.setAttribute("name", String(context.newLabel)));
    events.fire("commandStack.changed", { trigger: "execute" });
  }), undo: vi.fn(), redo: vi.fn(), clear: vi.fn() };
  const shape = { id: "work", businessObject: { id: "work" }, di: { id: "work-di" } };
  const editing = { isActive: () => active, complete: vi.fn(() => {
    if (!active) return;
    if (label) commands.execute("element.updateLabel", { shape, newLabel: label });
    events.fire("directEditing.deactivate"); active = false;
  }) };
  const dragging = { context: () => drag, end: vi.fn(() => { drag = undefined; events.fire("drag.ended"); }) };
  const selection = { select: vi.fn() };
  const services: Record<string, unknown> = { canvas, commandStack: commands, eventBus: events, directEditing: editing, dragging,
    elementRegistry: { get: () => shape }, selection };
  const modeler = { importXML: vi.fn(async (xml: string) => { native = xml; return { warnings: [] }; }),
    saveXML: vi.fn(() => Promise.resolve({ xml: native })), get: <T>(name: string) => services[name] as T,
    attachTo: vi.fn(), detach: vi.fn(), destroy: vi.fn() };
  const readLive = () => ({ owner: source, token });
  const coordinator = createAuthoringCheckpoint({ readLive: () => ({ token, snapshot: source, lineage: [] }) });
  const admit = vi.fn((_expected, candidate) => { source = candidate; token = { ...token, revision: token.revision + 1 }; return true; });
  const state = vi.fn(), history = vi.fn();
  const context: BpmnDocumentCanvasEditorContext = { protocol: { id: "elsa.workflows.bpmn-document-canvas", version: 1 },
    resourceKind: "workflow-definition", documentId: "draft", scope, owner: source, preparation: null, selection: null, ready: true, readOnly: false, readLive,
    createSessionHost: (id, participant) => createBpmnDocumentSessionHost({ id, participant, readLive, coordinator, canEdit: () => true, admit }),
    retainCanvasController: (_id, create) => create(), onSelect: vi.fn(), onNavigateScope: vi.fn(), resolveStandardScope: id => id === "sub" ? { ...scope, scopeElementId: id } : null,
    onUndo: history, onRedo: history, onStateChange: state };
  const factory = vi.fn(async () => modeler), controller = createBpmnDocumentController({ context, authoring, createModeler: factory });
  const container = document.createElement("div");
  let detach = controller.attach(container);
  return { controller, context, coordinator, admit, state, factory, modeler, editing, commands, canvas, sub, readLive, selection,
    detach: () => detach(), reattach: () => { detach = controller.attach(container); },
    beginEditing(text: string) { active = true; label = text; events.fire("directEditing.activate"); },
    beginDrag() { drag = {}; events.fire("drag.init"); },
    fire: events.fire,
    replaceSource() { source = authoring.applyBpmnDocumentCommands(source, [{ kind: "attribute", target: { elementId: "work" }, name: "name", value: "Inspector" }]); token = { ...token, revision: token.revision + 1 }; }
  };
}
const ready = async (h: ReturnType<typeof control>) => vi.waitFor(() => expect(h.controller.read().status).toBe("ready"));

describe("source2 native retained controller using controlled ports (no rendered Modeler acceptance)", () => {
  it("opens one private Modeler without changing source and reattaches it after view detachment", async () => {
    const h = control(), before = h.readLive().owner; await ready(h);
    expect(h.admit).not.toHaveBeenCalled(); expect(h.readLive().owner).toBe(before);
    h.detach(); h.reattach(); await ready(h);
    expect(h.factory).toHaveBeenCalledOnce(); expect(h.modeler.importXML).toHaveBeenCalledOnce();
    expect(h.modeler.detach).toHaveBeenCalledOnce(); expect(h.modeler.attachTo).toHaveBeenCalledTimes(2);
  });

  it("captures an active label before completion and retains it across detach until exact parent acknowledgement", async () => {
    const h = control(); await ready(h);
    h.beginEditing("Saved native label");
    expect(h.coordinator.probe()).toBe(true); expect(h.controller.read().pending).toBe(true);
    h.detach();
    const serialized = deferred<{ xml: string }>(), original = h.modeler.saveXML.getMockImplementation()!;
    h.modeler.saveXML.mockImplementationOnce(() => { void original().then(value => serialized.resolve(value)); return serialized.promise; });
    const captured = await h.coordinator.capture("save");
    expect(authoring.readBpmnDocument(captured.snapshot).byId.get("work")!.getAttribute("name")).toBe("Saved native label");
    expect(h.admit).toHaveBeenCalledOnce(); expect(h.coordinator.probe()).toBe(false);
    h.reattach(); expect(h.factory).toHaveBeenCalledOnce();
  });

  it("holds Save during a native serialization and does not reimport away its pending view", async () => {
    const h = control(); await ready(h); h.beginEditing("Delayed native label");
    const delayed = deferred<{ xml: string }>();
    const original = h.modeler.saveXML.getMockImplementation()!;
    let capturedXml = "";
    h.modeler.saveXML.mockImplementationOnce(() => { void original().then(value => { capturedXml = value.xml; }); return delayed.promise; });
    let settled = false;
    const captured = h.coordinator.capture("save").then(value => { settled = true; return value; });
    await vi.waitFor(() => expect(h.editing.complete).toHaveBeenCalled());
    expect(h.admit).not.toHaveBeenCalled(); expect(settled).toBe(false);
    h.controller.update({ ...h.context, readOnly: true }); h.detach();
    expect(h.modeler.importXML).toHaveBeenCalledOnce();
    delayed.resolve({ xml: capturedXml });
    expect(authoring.readBpmnDocument((await captured).snapshot).byId.get("work")!.getAttribute("name")).toBe("Delayed native label");
    expect(h.admit).toHaveBeenCalledOnce();
  });

  it("retains a refused native command, reports failure, and retries without another gesture", async () => {
    const h = control(); await ready(h); h.admit.mockReturnValueOnce(false);
    h.beginEditing("Retained refused label"); h.editing.complete();
    await vi.waitFor(() => expect(h.controller.read().status).toBe("failed"));
    expect(h.coordinator.probe()).toBe(true);
    expect(authoring.readBpmnDocument(h.readLive().owner).byId.get("work")!.getAttribute("name")).toBe("Original");
    await h.controller.retry();
    expect(authoring.readBpmnDocument(h.readLive().owner).byId.get("work")!.getAttribute("name")).toBe("Retained refused label");
    expect(h.admit).toHaveBeenCalledTimes(2); expect(h.coordinator.probe()).toBe(false);
  });

  it("delegates native drilldown to the parent scope barrier instead of changing a private root", async () => {
    const h = control(); await ready(h);
    const before = h.canvas.getRootElement(); h.canvas.setRootElement(h.sub);
    expect(h.context.onNavigateScope).toHaveBeenCalledWith({ ...scope, scopeElementId: "sub" });
    expect(h.canvas.getRootElement()).toBe(before);
  });

  it("lets a command's root setter finish and delegates scope only after canonical acknowledgement", async () => {
    const h = control(); await ready(h);
    const saved = deferred<{ xml: string }>(), original = h.modeler.saveXML.getMockImplementation()!;
    h.modeler.saveXML.mockImplementationOnce(() => { void original().then(saved.resolve); return saved.promise; });
    const shape = { id: "work", businessObject: { id: "work" }, di: { id: "work-di" } };
    h.commands.execute("element.updateLabel", { shape, newLabel: "Root command companion", nextRoot: h.sub });
    expect(h.canvas.getRootElement()).toBe(h.sub);
    expect(h.context.onNavigateScope).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(h.context.onNavigateScope).toHaveBeenCalledWith({ ...scope, scopeElementId: "sub" }));
    expect(h.admit).toHaveBeenCalledOnce();
  });

  it("checks cached cross-editor permission before a read-only prop render arrives", async () => {
    const h = control(); await ready(h); let allowed = true;
    h.controller.update({ ...h.context, canMutate: () => allowed }); allowed = false;
    expect(() => h.commands.execute("element.updateLabel", { newLabel: "late" })).toThrow(/unavailable/);
    expect(h.admit).not.toHaveBeenCalled();
  });

  it("restores the selected process root after a canonical history change and clean hidden checkpoint", async () => {
    const h = control(); await ready(h);
    h.commands.execute("element.updateLabel", { shape: { id: "work", di: { id: "work-di" } }, newLabel: "Before history", nextRoot: h.sub });
    await vi.waitFor(() => expect(h.context.onNavigateScope).toHaveBeenCalledOnce());
    h.detach(); h.replaceSource();
    await h.coordinator.capture("undo");
    const navigations = vi.mocked(h.context.onNavigateScope).mock.calls.length;
    h.reattach(); await vi.waitFor(() => expect(h.modeler.importXML).toHaveBeenCalledTimes(2)); await ready(h);
    expect(h.canvas.getRootElement().id).toBe("process");
    expect(h.context.onNavigateScope).toHaveBeenCalledTimes(navigations);
    expect(h.factory).toHaveBeenCalledOnce();
  });

  it("normalizes external-label selection and suppresses view callbacks after detach", async () => {
    const h = control(); await ready(h);
    const target = { id: "work", businessObject: { id: "work" }, di: { id: "work-di" } };
    h.fire("selection.changed", { newSelection: [{ id: "work_label", labelTarget: target }] });
    expect(h.context.onSelect).toHaveBeenCalledWith({ elementId: "work", diId: "work-di" });
    vi.mocked(h.context.onSelect).mockClear(); h.detach();
    h.fire("selection.changed", { newSelection: [target] }); expect(h.context.onSelect).not.toHaveBeenCalled();
  });

  it("refreshes a hidden clean parent receipt and imports the changed source before visible mutations", async () => {
    const h = control(); await ready(h); h.detach(); h.replaceSource();
    await h.coordinator.capture("save");
    expect(h.modeler.importXML).toHaveBeenCalledOnce(); expect(h.admit).not.toHaveBeenCalled();
    h.reattach(); await vi.waitFor(() => expect(h.modeler.importXML).toHaveBeenCalledTimes(2));
    await ready(h); expect(h.factory).toHaveBeenCalledOnce();
  });

  it("retains an active textbox when a changed canonical baseline refuses completion", async () => {
    const h = control(); await ready(h); h.beginEditing("Still local"); h.replaceSource();
    await expect(h.coordinator.capture("save")).rejects.toThrow(/changed authoring source/);
    expect(h.editing.isActive()).toBe(true);
    expect(h.coordinator.probe()).toBe(true);
    expect(h.admit).not.toHaveBeenCalled();
    expect(authoring.readBpmnDocument(h.readLive().owner).byId.get("work")!.getAttribute("name")).toBe("Inspector");
  });
  it("finishes an active drag before detaching and retains its admitted source", async () => {
    const h = control(); await ready(h); h.beginDrag();
    h.detach();
    expect(h.controller.readWork().pending).toBe(false);
    expect(h.modeler.detach).toHaveBeenCalledOnce();
    expect(h.modeler.destroy).not.toHaveBeenCalled();
  });

  it("retains old-baseline text after authority changes without admitting it into the new source", async () => {
    const h = control(); await ready(h); h.beginEditing("Old local label"); h.replaceSource(); h.detach();
    await vi.waitFor(() => expect(h.controller.read().status).toBe("failed"));
    expect(h.editing.isActive()).toBe(false);
    expect(h.coordinator.probe()).toBe(true);
    expect(h.admit).not.toHaveBeenCalled();
    expect(() => h.controller.retire()).toThrow(/Pending|fenced/);
    expect(h.modeler.destroy).not.toHaveBeenCalled();
    expect(authoring.readBpmnDocument(h.readLive().owner).byId.get("work")!.getAttribute("name")).toBe("Inspector");
  });

  it("destroys a clean detached resource once and prevents later native mutations", async () => {
    const h = control(); await ready(h); h.detach(); h.controller.retire(); h.controller.retire();
    expect(h.modeler.destroy).toHaveBeenCalledOnce();
    expect(() => h.reattach()).toThrow(/retired/);
    expect(() => h.commands.execute("element.updateLabel", { newLabel: "late" })).toThrow(/unavailable/);
    expect(h.admit).not.toHaveBeenCalled();
  });

  it("refuses final destruction while a clean checkpoint lease is held", async () => {
    const h = control(); await ready(h);
    const lease = await h.coordinator.transition("switch-editor", () => {
      h.detach();
      expect(() => h.controller.retire()).toThrow(/fenced/);
      expect(h.modeler.destroy).not.toHaveBeenCalled();
    });
    expect(lease.applied).toBe(true);
    h.controller.retire(); expect(h.modeler.destroy).toHaveBeenCalledOnce();
  });

  it("retries a fallible modeler destroy without retiring its registration twice", async () => {
    const h = control(); await ready(h); h.detach();
    h.modeler.destroy.mockImplementationOnce(() => { throw new Error("destroy failed"); });
    expect(() => h.controller.retire()).toThrow("destroy failed");
    h.controller.retire(); expect(h.modeler.destroy).toHaveBeenCalledTimes(2);
  });

});
