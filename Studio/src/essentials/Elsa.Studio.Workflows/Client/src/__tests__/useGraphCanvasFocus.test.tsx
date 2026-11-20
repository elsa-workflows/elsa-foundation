import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Node } from "@xyflow/react";
import { WorkflowEditorToolbar } from "../workflow-editor/WorkflowEditorToolbar";
import { captureCanvasFocusIntent, useGraphCanvasInteractions } from "../graph-authoring/useGraphCanvasInteractions";
import type { WorkflowNodeData } from "../workflowAdapter";
import type { WorkflowEdge } from "../workflow-editor/editorTypes";

type HookResult = ReturnType<typeof useGraphCanvasInteractions>;
type HookHarnessProps = {
  nodes: Node<WorkflowNodeData>[];
  result: { current: HookResult | null };
  commitCanvas?: () => void;
};

const mounted: Array<{ root: Root; container: HTMLDivElement }> = [];
let animationFrameId = 0;
let animationFrames: Map<number, FrameRequestCallback>;

beforeEach(() => {
  animationFrameId = 0;
  animationFrames = new Map();
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
    const id = ++animationFrameId;
    animationFrames.set(id, callback);
    return id;
  });
  vi.stubGlobal("cancelAnimationFrame", (id: number) => {
    animationFrames.delete(id);
  });
});

afterEach(() => {
  for (const item of mounted.splice(0)) {
    flushSync(() => item.root.unmount());
    item.container.remove();
  }
  animationFrames.clear();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("useGraphCanvasInteractions focus", () => {
  it("waits for the new node mirror to mount and become measurable before focusing", async () => {
    const fixture = renderHook([]);
    fixture.result.current!.queueCanvasNodeFocus("new-node");

    fixture.render([node("new-node")]);
    const target = fixture.container.querySelector<HTMLElement>(".react-flow__node[data-id='new-node']")!;
    const focus = target.focus.bind(target);
    const focusSpy = vi.spyOn(target, "focus").mockImplementation(options => {
      if (target.style.visibility !== "hidden") focus(options);
    });

    flushAnimationFrames();
    await flushMutationObservers();

    expect(focusSpy).toHaveBeenCalled();
    expect(document.activeElement).not.toBe(target);

    target.style.visibility = "visible";
    target.tabIndex = 0;
    await flushMutationObservers();

    expect(document.activeElement).toBe(target);
  });

  it("cancels queued focus when the hidden target is removed before measurement", async () => {
    const fixture = renderHook([]);
    fixture.result.current!.queueCanvasNodeFocus("removed-node");
    fixture.render([node("removed-node")]);

    const target = fixture.container.querySelector<HTMLElement>(".react-flow__node[data-id='removed-node']")!;
    const focus = target.focus.bind(target);
    vi.spyOn(target, "focus").mockImplementation(options => {
      if (target.style.visibility !== "hidden") focus(options);
    });
    flushAnimationFrames();
    await flushMutationObservers();

    fixture.render([]);
    await flushMutationObservers();

    // Reusing the ID after cancellation must not let a stale observer focus a replacement element.
    const replacement = document.createElement("div");
    replacement.className = "react-flow__node";
    replacement.dataset.id = "removed-node";
    replacement.tabIndex = 0;
    fixture.container.querySelector(".test-canvas")!.append(replacement);
    const replacementFocus = vi.spyOn(replacement, "focus");
    await flushMutationObservers();

    expect(replacementFocus).not.toHaveBeenCalled();
    expect(document.activeElement).not.toBe(replacement);
  });
});

describe("pending canvas focus ownership", () => {
  it("does not let a superseded restoration steal focus before the newer node is queued", async () => {
    const fixture = renderHook([node("first-node"), node("second-node")]);
    const toolbar = renderFocusToolbar();
    toolbar.undo.focus();
    const first = captureCanvasFocusIntent();
    fixture.result.current!.queueCanvasNodeFocus("first-node", first);
    first.dispose();
    const second = captureCanvasFocusIntent();
    const firstTarget = fixture.container.querySelector<HTMLElement>('[data-id="first-node"]')!;
    firstTarget.style.visibility = "visible";
    firstTarget.tabIndex = 0;
    await flushMutationObservers();
    flushAnimationFrames();
    expect(document.activeElement).toBe(toolbar.undo);
    fixture.result.current!.queueCanvasNodeFocus("second-node", second);
    const secondTarget = fixture.container.querySelector<HTMLElement>('[data-id="second-node"]')!;
    secondTarget.style.visibility = "visible";
    secondTarget.tabIndex = 0;
    await flushMutationObservers();
    flushAnimationFrames();
    expect(document.activeElement).toBe(secondTarget);
  });

  it("keeps a newer queued focus when an expired history request arrives late", async () => {
    const fixture = renderHook([]);
    const toolbar = renderFocusToolbar();
    toolbar.undo.focus();
    const expired = captureCanvasFocusIntent();
    toolbar.open();
    fixture.render([node("newer-node"), node("restored-node")]);
    fixture.result.current!.queueCanvasNodeFocus("newer-node");
    fixture.result.current!.queueCanvasNodeFocus("restored-node", expired);
    const target = fixture.container.querySelector<HTMLElement>('[data-id="newer-node"]')!;
    target.style.visibility = "visible";
    target.tabIndex = 0;
    await flushMutationObservers();
    flushAnimationFrames();
    expect(document.activeElement).toBe(target);
  });

  it("retains the history action's intent while waiting to queue the restored node", async () => {
    const fixture = renderHook([]);
    const toolbar = renderFocusToolbar();
    toolbar.undo.focus();
    const intent = captureCanvasFocusIntent();
    toolbar.open();
    fixture.render([node("restored-node")]);
    fixture.result.current!.queueCanvasNodeFocus("restored-node", intent);
    const target = fixture.container.querySelector<HTMLElement>(".react-flow__node")!;
    target.style.visibility = "visible";
    target.tabIndex = 0;
    await flushMutationObservers();
    flushAnimationFrames();
    expect(document.activeElement).toBe(toolbar.autosave());
    toolbar.assertNavigation();
  });

  it("does not renew a cancelled intent when a node re-renders or focus returns to the origin", async () => {
    const fixture = renderHook([]);
    const toolbar = renderFocusToolbar();
    toolbar.undo.focus();
    fixture.result.current!.queueCanvasNodeFocus("restored-node");
    toolbar.open();
    flushSync(() => toolbar.autosave().dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    toolbar.undo.focus();
    fixture.render([node("restored-node")]);
    const target = fixture.container.querySelector<HTMLElement>(".react-flow__node")!;
    target.style.visibility = "visible";
    target.tabIndex = 0;
    await flushMutationObservers();
    flushAnimationFrames();
    expect(document.activeElement).toBe(toolbar.undo);
  });

  it("preserves the workflow menu when a queued node becomes measurable later", async () => {
    const fixture = renderHook([]);
    const toolbar = renderFocusToolbar();
    toolbar.undo.focus();
    fixture.result.current!.queueCanvasNodeFocus("new-node");
    fixture.render([node("new-node")]);
    const target = fixture.container.querySelector<HTMLElement>(".react-flow__node")!;
    const focus = target.focus.bind(target);
    vi.spyOn(target, "focus").mockImplementation(options => {
      if (target.style.visibility !== "hidden") focus(options);
    });
    flushAnimationFrames();
    await flushMutationObservers();
    toolbar.open();
    expect(document.activeElement).toBe(toolbar.autosave());
    target.style.visibility = "visible";
    target.tabIndex = 0;
    await flushMutationObservers();
    flushAnimationFrames();
    expect(document.activeElement).toBe(toolbar.autosave());
    toolbar.assertNavigation();
  });
});

describe("useGraphCanvasInteractions auto-layout", () => {
  it("clamps fit-view zoom so a small graph stays at its natural size", () => {
    const fitView = vi.fn();
    const fixture = renderHook([node("a"), node("b")]);

    flushSync(() => {
      fixture.result.current!.setReactFlowInstance({ fitView } as never);
    });
    fixture.result.current!.autoLayout();
    flushAnimationFrames();

    expect(fitView).toHaveBeenCalledWith({ padding: 0.2, maxZoom: 1 });
  });
});

describe("useGraphCanvasInteractions node resize", () => {
  it("commits the resized width as one canvas transaction", () => {
    const commitCanvas = vi.fn();
    const fixture = renderHook([node("a"), node("b")], commitCanvas);

    fixture.result.current!.resizeNode("a", 412.4, 246);

    expect(commitCanvas).toHaveBeenCalledTimes(1);
    const [committedNodes] = commitCanvas.mock.calls[0] as unknown as [Node<WorkflowNodeData>[]];
    expect(committedNodes.find(current => current.id === "a")?.width).toBe(412);
    expect(committedNodes.find(current => current.id === "b")?.width).toBeUndefined();
  });

  it("commits nothing when a click on the handle leaves the width unchanged", () => {
    const commitCanvas = vi.fn();
    const fixture = renderHook([node("a")], commitCanvas);

    fixture.result.current!.resizeNode("a", 246.3, 246);

    expect(commitCanvas).not.toHaveBeenCalled();
  });
});

function renderHook(nodes: Node<WorkflowNodeData>[], commitCanvas?: () => void) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  const result: { current: HookResult | null } = { current: null };
  const fixture = {
    container,
    result,
    render(nextNodes: Node<WorkflowNodeData>[]) {
      flushSync(() => root.render(<HookHarness nodes={nextNodes} result={result} commitCanvas={commitCanvas} />));
    }
  };
  mounted.push({ root, container });
  fixture.render(nodes);
  return fixture;
}

function HookHarness({ nodes, result, commitCanvas = vi.fn() }: HookHarnessProps) {
  const hook = useGraphCanvasInteractions({
    nodes,
    edges: [] as WorkflowEdge[],
    setNodes: vi.fn(),
    setEdges: vi.fn(),
    mode: "flowchart",
    scopeKey: "root",
    canAddActivities: false,
    selectedNodeId: null,
    catalogByVersion: new Map(),
    select: vi.fn(),
    commitCanvas,
    placeActivity: vi.fn(() => null)
  });
  result.current = hook;
  return (
    <div ref={hook.canvasRef} className="test-canvas">
      {nodes.map(current => (
        <div
          key={current.id}
          className="react-flow__node"
          data-id={current.id}
          tabIndex={-1}
          style={{ visibility: "hidden" }}
        />
      ))}
    </div>
  );
}

function node(id: string): Node<WorkflowNodeData> {
  return { id, type: "workflowActivity", position: { x: 0, y: 0 }, data: {} as WorkflowNodeData };
}

function flushAnimationFrames() {
  const callbacks = [...animationFrames.values()];
  animationFrames.clear();
  callbacks.forEach(callback => callback(0));
}

async function flushMutationObservers() {
  await Promise.resolve();
}

function renderFocusToolbar() {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue(new DOMRect(100, 100, 100, 40));
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  mounted.push({ root, container });
  flushSync(() => root.render(<WorkflowEditorToolbar name="Focus ownership" status="Autosaved" saving={false} busy={false}
    autosaveEnabled onAutosaveChange={vi.fn()} canUndo canRedo canAutoLayout onUndo={vi.fn()} onRedo={vi.fn()}
    onAutoLayout={vi.fn()} onBack={vi.fn()} onSave={vi.fn()} onPublish={vi.fn()} onRun={vi.fn()} canRun runTitle="Test Run"
    moreActions={[{ label: "Import & export", actions: [{ label: "Export JSON", icon: null, onSelect: vi.fn() }] }]} />));
  const trigger = container.querySelector<HTMLButtonElement>('[aria-label="More workflow actions"]')!;
  const undo = container.querySelector<HTMLButtonElement>('[aria-label="Undo"]')!;
  const menu = () => document.querySelector<HTMLElement>('[role="menu"][aria-label="More workflow actions"]')!;
  const autosave = () => menu().querySelector<HTMLButtonElement>('[role="menuitemcheckbox"]')!;
  return { undo, autosave, open: () => { trigger.focus(); flushSync(() => trigger.click()); }, assertNavigation: () => {
    flushSync(() => autosave().dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    const item = menu().querySelector<HTMLButtonElement>('[role="menuitem"]')!;
    expect(document.activeElement).toBe(item);
    flushSync(() => item.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(document.activeElement).toBe(trigger);
  } };
}
