import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ActivityNode } from "@elsa-workflows/studio-workflows";
import { BpmnJsCanvas, type BpmnJsCanvasProps } from "../BpmnJsCanvas";

// Control-plane evidence only. Native SVG/modeling and semantic preservation are covered by the
// separate real-modeler browser and real-moddle bridge suites, rather than fabricated jsdom SVG APIs.
const controls = vi.hoisted(() => ({ instances: [] as FakeModeler[] }));

vi.mock("../bpmnJsBridge", () => ({
  projectBpmnJsXml: (owner: ActivityNode, layout: unknown) => JSON.stringify([owner, layout]),
  mergeBpmnJsDefinitions: (owner: ActivityNode, definitions: unknown, layout: unknown) => ({
    owner: { ...owner, ...(definitions as { patch: Partial<ActivityNode> }).patch }, layout
  })
}));

vi.mock("bpmn-js/lib/Modeler", () => ({ default: class {
  constructor() { return new FakeModeler(); }
} }));

class FakeModeler {
  imports: { xml: string; resolve(result: { warnings: [] }): void }[] = [];
  handlers = new Map<string, ((event: unknown) => unknown)[]>();
  definitions = { patch: {} as Partial<ActivityNode> };
  executed = vi.fn();
  destroyed = false;
  commandStack = {
    execute: (...args: unknown[]) => { this.executed(...args); this.fire("commandStack.changed", {}); },
    undo: vi.fn(), redo: vi.fn()
  };
  canvas = { zoom: vi.fn(), viewbox: vi.fn(() => ({ x: 0, y: 0, width: 800, height: 600 })) };
  selection = { select: vi.fn() };
  constructor() { controls.instances.push(this); }
  importXML(xml: string) {
    return new Promise<{ warnings: [] }>(resolve => this.imports.push({ xml, resolve }));
  }
  getDefinitions() { return this.definitions; }
  get(name: string) {
    if (name === "commandStack") return this.commandStack;
    if (name === "canvas") return this.canvas;
    if (name === "selection") return this.selection;
    if (name === "elementRegistry") return { get: (id: string) => ({ id }) };
    if (name === "eventBus") return { on: (event: string, _priority: number, handler: (event: unknown) => unknown) => {
      this.handlers.set(event, [...(this.handlers.get(event) ?? []), handler]);
    } };
    throw new Error(`Unexpected service ${name}`);
  }
  fire(name: string, value: unknown) { for (const handler of this.handlers.get(name) ?? []) handler(value); }
  destroy() { this.destroyed = true; }
}

const process = (nodeId = "process"): ActivityNode => ({ nodeId, activityVersionId: "bpmn-v1", inputs: [], outputs: [] });
let root: Root;
let container: HTMLDivElement;
let props: BpmnJsCanvasProps;
const render = (changes: Partial<BpmnJsCanvasProps> = {}) => {
  props = { ...props, ...changes };
  flushSync(() => root.render(<BpmnJsCanvas {...props} />));
};
const ready = async (modeler: FakeModeler, importIndex = 0) => {
  modeler.imports[importIndex].resolve({ warnings: [] });
  await vi.waitFor(() => expect(container.querySelector("[data-bpmn-js-state='ready']")).not.toBeNull());
};
const mounted = async () => {
  await vi.waitFor(() => expect(controls.instances).toHaveLength(1));
  const modeler = controls.instances[0];
  await vi.waitFor(() => expect(modeler.imports).toHaveLength(1));
  return modeler;
};

beforeEach(() => {
  controls.instances.length = 0;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  props = { owner: process(), layout: [], selectedElementId: null, readOnly: false,
    onChange: vi.fn(() => true), onSelect: vi.fn(), onUndo: vi.fn(), onRedo: vi.fn(), onStateChange: vi.fn() };
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

describe("controlled bpmn-js canvas", () => {
  it("refuses a stale host revision and restores the current process without advancing its baseline", async () => {
    const change = vi.fn((_next: Parameters<BpmnJsCanvasProps["onChange"]>[0]) => false);
    render({ onChange: change });
    const modeler = await mounted();
    await ready(modeler);
    modeler.definitions.patch = { inputs: [{ referenceKey: "changed" }] };
    flushSync(() => modeler.commandStack.execute("shape.move", {}));
    expect(change).toHaveBeenCalledWith(expect.objectContaining({ baseOwner: props.owner, baseLayout: props.layout }));
    expect(container.textContent).toContain("process changed before this canvas edit could apply");
    await vi.waitFor(() => expect(modeler.imports).toHaveLength(2));
    expect(JSON.parse(modeler.imports[1].xml)[0]).toEqual(props.owner);
    await ready(modeler, 1);
    flushSync(() => modeler.commandStack.execute("shape.move", {}));
    expect(change.mock.calls[1][0].baseOwner.inputs).toEqual([]);
  });

  it("acknowledges a synchronous host edit without starting a competing projection import", async () => {
    const change = vi.fn((next: Parameters<BpmnJsCanvasProps["onChange"]>[0]) => {
      render({ owner: next.owner, layout: next.layout });
      return true;
    });
    render({ onChange: change });
    const modeler = await mounted();
    await ready(modeler);
    modeler.definitions.patch = { inputs: [{ referenceKey: "accepted" }] };
    modeler.commandStack.execute("shape.move", {});
    await vi.waitFor(() => expect(props.owner.inputs).toEqual([{ referenceKey: "accepted" }]));
    expect(modeler.imports).toHaveLength(1);
    modeler.definitions.patch = { outputs: [{ referenceKey: "next" }] };
    modeler.commandStack.execute("shape.move", {});
    expect(change.mock.calls[1][0].baseOwner.inputs).toEqual([{ referenceKey: "accepted" }]);
    expect(props.owner.outputs).toEqual([{ referenceKey: "next" }]);
  });

  it("serializes a changed scope behind an outstanding import and keeps modeling disabled", async () => {
    render();
    const modeler = await mounted();
    render({ owner: process("other-process"), readOnly: true });
    modeler.commandStack.execute("shape.move", {});
    expect(modeler.executed).not.toHaveBeenCalled();
    modeler.imports[0].resolve({ warnings: [] });
    await vi.waitFor(() => expect(modeler.imports).toHaveLength(2));
    expect(container.querySelector("[data-bpmn-js-state='ready']")).toBeNull();
    expect(JSON.parse(modeler.imports[1].xml)[0].nodeId).toBe("other-process");
    await ready(modeler, 1);
    modeler.commandStack.execute("shape.move", {});
    modeler.commandStack.undo();
    expect(modeler.executed).not.toHaveBeenCalled();
    expect(props.onChange).not.toHaveBeenCalled();
    expect(props.onUndo).not.toHaveBeenCalled();
  });

  it("delegates native Undo/Redo to the host and prevents empty subprocess drilldown", async () => {
    render();
    const modeler = await mounted();
    await ready(modeler);
    modeler.commandStack.undo();
    modeler.commandStack.redo();
    expect(props.onUndo).toHaveBeenCalledOnce();
    expect(props.onRedo).toHaveBeenCalledOnce();
    flushSync(() => modeler.commandStack.execute("shape.toggleCollapse", {}));
    expect(modeler.executed).not.toHaveBeenCalled();
    expect(props.onChange).not.toHaveBeenCalled();
    expect(container.textContent).toContain("Elsa inspector's Slots");
  });

  it("retires an outstanding import on unmount without announcing readiness", async () => {
    render();
    const modeler = await mounted();
    const state = props.onStateChange;
    flushSync(() => root.unmount());
    root = createRoot(container);
    modeler.imports[0].resolve({ warnings: [] });
    await Promise.resolve();
    expect(modeler.destroyed).toBe(true);
    expect(state).not.toHaveBeenCalledWith({ status: "ready" });
  });
});
