import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { BpmnDocumentCanvasEditorContext } from "@elsa-workflows/studio-workflows";
import { BpmnDocumentJsCanvasEditor } from "../BpmnDocumentJsCanvas";
import { authoring, owner, rawXml, scope } from "./bpmnDocumentFixture";
import { createBpmnDocumentSessionHost } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnDocumentSessionHost";
import { createAuthoringCheckpoint } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/authoringCheckpoint";

const controls = vi.hoisted(() => ({ modeler: null as object | null, created: vi.fn() }));
vi.mock("@elsa-workflows/studio-workflows", () => ({ loadBpmnDocumentAuthoring: async () => authoring }));
vi.mock("bpmn-js/lib/Modeler", () => ({ default: class { constructor() { controls.created(); return controls.modeler!; } } }));
const cleanups: (() => void)[] = [];
afterEach(() => { cleanups.splice(0).forEach(cleanup => cleanup()); controls.created.mockClear(); });

function renderCanvas(ready = true) {
  const source = owner(rawXml.replace('id="sub-di" bpmnElement="sub"', 'id="sub-di" bpmnElement="sub" isExpanded="true"'));
  const token = { authorityId: "author", resourceId: "draft", scopeId: "root", revision: 0 };
  const readLive = () => ({ owner: source, token });
  const coordinator = createAuthoringCheckpoint({ readLive: () => ({ ...readLive(), snapshot: source, lineage: [] }) });
  const retained = new Map<string, unknown>();
  const events = { on() {}, off() {} };
  const nativeRoot = { id: "process", businessObject: { id: "process" } };
  let xml = "", shown = nativeRoot;
  const services = { eventBus: events, canvas: { getRootElements: () => [nativeRoot], getRootElement: () => shown, setRootElement: (next: typeof nativeRoot) => { shown = next; return next; }, viewbox: () => ({}), zoom() {}, resized() {} },
    directEditing: { complete() {}, isActive: () => false }, dragging: { context: () => undefined },
    commandStack: { execute() {}, clear() {}, undo() {}, redo() {} }, elementRegistry: { get: () => null }, selection: { select() {} } };
  const modeler = { importXML: vi.fn(async (value: string) => { xml = value; return { warnings: [] }; }), saveXML: async () => ({ xml }),
    get: (name: keyof typeof services) => services[name], attachTo: vi.fn(), detach: vi.fn() };
  controls.modeler = modeler;
  const register = vi.fn((id, participant) => createBpmnDocumentSessionHost({ id, participant, coordinator, readLive, canEdit: () => true, admit: () => { throw new Error("No mutation expected"); } }));
  let context: BpmnDocumentCanvasEditorContext = { protocol: { id: "elsa.workflows.bpmn-document-canvas", version: 1 }, resourceKind: "workflow-definition", documentId: "draft", scope,
    selection: null, owner: source, preparation: null, ready, readOnly: false, readLive, createSessionHost: register,
    retainCanvasController<T>(id: string, create: () => T): T { if (!retained.has(id)) retained.set(id, create()); return retained.get(id) as T; },
    onSelect: vi.fn(), onNavigateScope: vi.fn(), resolveStandardScope: () => null, onUndo: vi.fn(), onRedo: vi.fn(), onStateChange: vi.fn() };
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  const render = (visible: boolean) => flushSync(() => root.render(visible ? <BpmnDocumentJsCanvasEditor context={context} /> : null));
  render(true);
  cleanups.push(() => { flushSync(() => root.unmount()); container.remove(); });
  return { container, modeler, register, source, coordinator, readLive, render,
    ready() { context = { ...context, ready: true }; render(true); },
    readOnly() { context = { ...context, readOnly: true }; render(true); }
  };
}

describe("source2 native React attachment with controlled Modeler ports", () => {
  it("keeps source unchanged and reuses the retained controller after a fallible view remount", async () => {
    const h = renderCanvas(), original = h.readLive().owner;
    await vi.waitFor(() => expect(h.container.querySelector("[data-bpmn-js-state=ready]")).not.toBeNull());
    expect(h.container.querySelector('[aria-label="bpmn-js process editor"]')).not.toBeNull();
    expect(h.readLive().owner).toBe(original); expect(controls.created).toHaveBeenCalledOnce(); expect(h.register).toHaveBeenCalledOnce();
    h.render(false); expect(h.modeler.detach).toHaveBeenCalledOnce(); h.render(true);
    await vi.waitFor(() => expect(h.modeler.attachTo).toHaveBeenCalledTimes(2));
    expect(controls.created).toHaveBeenCalledOnce(); expect(h.register).toHaveBeenCalledOnce();
    expect(h.modeler.importXML).toHaveBeenCalledOnce();
    expect((await h.coordinator.capture("save")).snapshot).toEqual(original);
  });

  it("does not construct a native Modeler while legacy preparation/capability is unavailable and preserves clean capture", async () => {
    const h = renderCanvas(false);
    await vi.waitFor(() => expect(h.register).toHaveBeenCalledOnce());
    expect(controls.created).not.toHaveBeenCalled();
    expect((await h.coordinator.capture("save")).snapshot).toEqual(h.source);
    expect(controls.created).not.toHaveBeenCalled();
    h.ready();
    await vi.waitFor(() => expect(h.container.querySelector("[data-bpmn-js-state=ready]")).not.toBeNull());
    expect(controls.created).toHaveBeenCalledOnce();
    h.readOnly();
    expect(h.container.querySelector("[data-read-only=true]")).not.toBeNull();
  });
});
