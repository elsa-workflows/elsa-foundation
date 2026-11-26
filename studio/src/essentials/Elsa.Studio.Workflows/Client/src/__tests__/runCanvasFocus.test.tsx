import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  focusRunCanvasNode,
  isNodeVisibleInViewport,
  readStoredRunCanvasFocusMode,
  RUN_CANVAS_FOCUS_MAX_ZOOM,
  RUN_CANVAS_FOCUS_PADDING,
  runCanvasFocusModeStorageKey,
  writeStoredRunCanvasFocusMode,
  type RunCanvasFocusMode
} from "../workflow-editor/runCanvasFocus";
import { ReadOnlyFlowControls } from "../workflow-editor/ReadOnlyFlowControls";

vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    Controls: ({ children }: { children?: React.ReactNode }) => (
      <div className="react-flow__controls" data-testid="mock-controls">{children}</div>
    )
  };
});

describe("runCanvasFocus", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("defaults to 'pan' and persists valid focus modes in localStorage", () => {
    expect(readStoredRunCanvasFocusMode()).toBe("pan");

    writeStoredRunCanvasFocusMode("zoom");
    expect(window.localStorage.getItem(runCanvasFocusModeStorageKey)).toBe("zoom");
    expect(readStoredRunCanvasFocusMode()).toBe("zoom");

    writeStoredRunCanvasFocusMode("off");
    expect(readStoredRunCanvasFocusMode()).toBe("off");

    window.localStorage.setItem(runCanvasFocusModeStorageKey, "unexpected");
    expect(readStoredRunCanvasFocusMode()).toBe("pan");
  });

  it("checks whether a node is comfortably inside the visible viewport", () => {
    const container = { width: 800, height: 600 };
    const viewport = { x: 0, y: 0, zoom: 1 };

    // Comfortably inside the viewport.
    expect(isNodeVisibleInViewport({ x: 100, y: 100, width: 180, height: 60 }, viewport, container)).toBe(true);

    // Offscreen to the right.
    expect(isNodeVisibleInViewport({ x: 900, y: 100, width: 180, height: 60 }, viewport, container)).toBe(false);

    // Partially clipped on the left margin.
    expect(isNodeVisibleInViewport({ x: 10, y: 100, width: 180, height: 60 }, viewport, container)).toBe(false);

    // Hidden behind the bottom controls margin.
    expect(isNodeVisibleInViewport({ x: 200, y: 510, width: 180, height: 60 }, viewport, container)).toBe(false);

    // Respects viewport zoom and pan offset.
    expect(isNodeVisibleInViewport({ x: 500, y: 200, width: 180, height: 60 }, { x: -400, y: -100, zoom: 1 }, container)).toBe(true);
    expect(isNodeVisibleInViewport({ x: 500, y: 200, width: 180, height: 60 }, { x: 0, y: 0, zoom: 2 }, container)).toBe(false);

    // Oversized node (deeply zoomed in) checks center visibility.
    expect(isNodeVisibleInViewport({ x: 0, y: 0, width: 500, height: 400 }, { x: -100, y: -100, zoom: 2 }, container)).toBe(true);
    expect(isNodeVisibleInViewport({ x: 0, y: 0, width: 500, height: 400 }, { x: -1500, y: -100, zoom: 2 }, container)).toBe(false);

    // Unknown/zero container size reports not visible.
    expect(isNodeVisibleInViewport({ x: 100, y: 100, width: 100, height: 50 }, viewport, { width: 0, height: 0 })).toBe(false);
  });

  it("keeps the viewport still when 'pan' mode targets an onscreen node", () => {
    const flowInstance = {
      fitView: vi.fn(),
      setCenter: vi.fn(),
      getViewport: vi.fn(() => ({ x: 0, y: 0, zoom: 0.85 })),
      getInternalNode: vi.fn(() => ({
        id: "node-1",
        position: { x: 120, y: 120 },
        internals: { positionAbsolute: { x: 120, y: 120 } },
        measured: { width: 180, height: 60 }
      }) as never)
    };

    const outcome = focusRunCanvasNode({
      flowInstance,
      nodeId: "node-1",
      mode: "pan",
      animate: true,
      containerSize: { width: 800, height: 600 }
    });

    expect(outcome).toBe("visible");
    expect(flowInstance.setCenter).not.toHaveBeenCalled();
    expect(flowInstance.fitView).not.toHaveBeenCalled();
  });

  it("pans at the current zoom level when 'pan' mode targets an offscreen node", () => {
    const flowInstance = {
      fitView: vi.fn(),
      setCenter: vi.fn(),
      getViewport: vi.fn(() => ({ x: 0, y: 0, zoom: 0.75 })),
      getNode: vi.fn(() => ({
        id: "node-far",
        position: { x: 1400, y: 300 },
        measured: { width: 200, height: 80 }
      }) as never)
    };

    const outcome = focusRunCanvasNode({
      flowInstance,
      nodeId: "node-far",
      mode: "pan",
      animate: true,
      containerSize: { width: 800, height: 600 }
    });

    expect(outcome).toBe("panned");
    expect(flowInstance.fitView).not.toHaveBeenCalled();
    expect(flowInstance.setCenter).toHaveBeenCalledWith(1500, 340, { zoom: 0.75, duration: 220 });
  });

  it("fits the node with a capped maxZoom in 'zoom' mode", () => {
    const flowInstance = {
      fitView: vi.fn(),
      setCenter: vi.fn(),
      getViewport: vi.fn(() => ({ x: 0, y: 0, zoom: 0.75 }))
    };

    const outcome = focusRunCanvasNode({
      flowInstance,
      nodeId: "node-1",
      mode: "zoom",
      animate: false,
      containerSize: { width: 800, height: 600 }
    });

    expect(outcome).toBe("zoomed");
    expect(flowInstance.setCenter).not.toHaveBeenCalled();
    expect(flowInstance.fitView).toHaveBeenCalledWith({
      nodes: [{ id: "node-1" }],
      duration: 0,
      padding: RUN_CANVAS_FOCUS_PADDING,
      maxZoom: RUN_CANVAS_FOCUS_MAX_ZOOM
    });
  });

  it("never moves or zooms the canvas in 'off' mode", () => {
    const flowInstance = {
      fitView: vi.fn(),
      setCenter: vi.fn(),
      getViewport: vi.fn(() => ({ x: 0, y: 0, zoom: 1 }))
    };

    const outcome = focusRunCanvasNode({
      flowInstance,
      nodeId: "node-1",
      mode: "off",
      animate: true,
      containerSize: { width: 800, height: 600 }
    });

    expect(outcome).toBe("skipped");
    expect(flowInstance.setCenter).not.toHaveBeenCalled();
    expect(flowInstance.fitView).not.toHaveBeenCalled();
  });
});

describe("ReadOnlyFlowControls focus mode menu", () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement("div");
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(() => {
    flushSync(() => root.unmount());
    container.remove();
  });

  it("omits the focus behavior trigger when focusMode is not passed", () => {
    flushSync(() => root.render(<ReadOnlyFlowControls nodeCount={3} />));
    expect(container.querySelector(".wf-canvas-focus-mode-trigger")).toBeNull();
  });

  it("opens the focus mode menu from the controls bar, selects modes, and closes on Escape", () => {
    const onChange = vi.fn();
    function Harness() {
      const [mode, setMode] = React.useState<RunCanvasFocusMode>("pan");
      return (
        <ReadOnlyFlowControls
          nodeCount={3}
          focusMode={mode}
          onFocusModeChange={next => {
            setMode(next);
            onChange(next);
          }}
        />
      );
    }

    flushSync(() => root.render(<Harness />));
    const trigger = container.querySelector<HTMLButtonElement>(".wf-canvas-focus-mode-trigger")!;
    expect(trigger).toBeTruthy();
    expect(trigger.getAttribute("aria-label")).toBe("Canvas focus behavior: Pan when offscreen");
    vi.spyOn(trigger, "getBoundingClientRect").mockReturnValue(new DOMRect(20, 560, 26, 26));

    flushSync(() => trigger.click());
    const menu = document.body.querySelector<HTMLElement>("[role='menu'][aria-label='Canvas focus behavior']");
    expect(menu).toBeTruthy();
    const items = [...menu!.querySelectorAll<HTMLButtonElement>("[role='menuitemradio']")];
    expect(items).toHaveLength(3);
    expect(items[0].getAttribute("aria-checked")).toBe("true");
    expect(items[1].getAttribute("aria-checked")).toBe("false");

    // Select "Zoom to activity" and verify menu closes and focus returns to trigger.
    flushSync(() => items[1].click());
    expect(onChange).toHaveBeenCalledWith("zoom");
    expect(document.body.querySelector("[role='menu'][aria-label='Canvas focus behavior']")).toBeNull();
    expect(trigger.getAttribute("aria-label")).toBe("Canvas focus behavior: Zoom to activity");
    expect(document.activeElement).toBe(trigger);

    // Reopen and close with Escape.
    flushSync(() => trigger.click());
    expect(document.body.querySelector("[role='menu'][aria-label='Canvas focus behavior']")).toBeTruthy();
    flushSync(() => document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(document.body.querySelector("[role='menu'][aria-label='Canvas focus behavior']")).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });
});
