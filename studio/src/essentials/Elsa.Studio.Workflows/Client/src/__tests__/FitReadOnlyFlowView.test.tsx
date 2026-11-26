import React from "react";
import { getViewportForBounds, type FitViewOptions } from "@xyflow/react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getReadOnlyFitViewOptions, readOnlyMinimapFitViewOptions } from "../workflow-editor/readOnlyMinimap";

const viewport = vi.hoisted(() => ({ x: 0, y: 0, zoom: 1 }));
const fitView = vi.hoisted(() => vi.fn(async (_options: FitViewOptions) => true));
const getViewport = vi.hoisted(() => vi.fn(() => ({ x: viewport.x, y: viewport.y, zoom: viewport.zoom })));
const pane = vi.hoisted(() => ({ initialized: true, width: 800, height: 600 }));
type FixtureNode = { id: string; position: { x: number; y: number }; data: Record<string, unknown>; selected?: boolean; box: { x: number; y: number; width: number; height: number } };
const graphNodes = vi.hoisted(() => ({ items: [] as FixtureNode[] }));
const getNodes = vi.hoisted(() => vi.fn(() => graphNodes.items));
const getNodesBounds = vi.hoisted(() => vi.fn((nodes: FixtureNode[]) => {
  const left = Math.min(...nodes.map(node => node.box.x)), top = Math.min(...nodes.map(node => node.box.y));
  const right = Math.max(...nodes.map(node => node.box.x + node.box.width)), bottom = Math.max(...nodes.map(node => node.box.y + node.box.height));
  return { x: left, y: top, width: right - left, height: bottom - top };
}));
const setViewport = vi.hoisted(() => vi.fn(async (next: { x: number; y: number; zoom: number }) => {
  viewport.x = next.x; viewport.y = next.y; viewport.zoom = next.zoom;
  return true;
}));

vi.mock("@xyflow/react", async importOriginal => ({
  ...await importOriginal<typeof import("@xyflow/react")>(),
  useNodesInitialized: () => pane.initialized,
  useReactFlow: () => ({ fitView, getViewport, getNodes, getNodesBounds, setViewport }),
  useStore: (selector: (state: { width: number; height: number }) => unknown) => selector({ width: pane.width, height: pane.height })
}));

import { FitReadOnlyFlowView } from "../workflow-editor/useReadOnlyFlowCanvas";

let container: HTMLDivElement;
let root: Root;

async function settleAutoFit() {
  await Promise.resolve();
  await Promise.resolve();
}

beforeEach(() => {
  fitView.mockClear();
  getViewport.mockClear();
  setViewport.mockClear();
  graphNodes.items = [
    { id: "task", position: { x: 0, y: 0 }, data: {}, box: { x: 0, y: 0, width: 240, height: 96 } },
    { id: "output", position: { x: 420, y: 8 }, data: {}, box: { x: 420, y: 8, width: 240, height: 80 } }
  ];
  viewport.x = 0;
  viewport.y = 0;
  viewport.zoom = 1;
  pane.initialized = true;
  pane.width = 800;
  pane.height = 600;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

describe("FitReadOnlyFlowView", () => {
  it("fits with MiniMap padding once nodes are initialized", () => {
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    expect(fitView).toHaveBeenCalledTimes(1);
    expect(fitView).toHaveBeenCalledWith(readOnlyMinimapFitViewOptions);
  });

  it("fits without MiniMap padding on a canvas that never shows one (BPMN run)", () => {
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} miniMap={false} />));
    expect(fitView).toHaveBeenCalledWith(getReadOnlyFitViewOptions(2));
  });

  it("re-fits an untouched viewport when run-feedback height shrinks the pane", async () => {
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    await settleAutoFit();
    expect(fitView).toHaveBeenCalledTimes(1);

    pane.height = 500;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    expect(fitView).toHaveBeenCalledTimes(2);
    expect(fitView).toHaveBeenLastCalledWith(readOnlyMinimapFitViewOptions);
  });

  it("does not fit after a resize once the user moves the canvas", async () => {
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    await settleAutoFit();
    expect(fitView).toHaveBeenCalledTimes(1);

    viewport.x = 80;
    viewport.y = 40;
    viewport.zoom = 1.6;
    pane.width = 640;
    pane.height = 520;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    expect(fitView).toHaveBeenCalledTimes(1);
  });

  it("does not fit the whole graph when a focus request is followed by the pane narrowing", async () => {
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    await settleAutoFit();
    expect(fitView).toHaveBeenCalledTimes(1);

    // Focus/zoom-to-node is requested before the inspector open narrows the pane, so the
    // viewport may still match the last auto-fit when width changes.
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} focusRequestId={1} />));
    pane.width = 480;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} focusRequestId={1} />));
    expect(fitView).toHaveBeenCalledTimes(1);
  });

  it("does not fit an unmeasured pane", () => {
    pane.width = 0;
    pane.height = 0;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    expect(fitView).not.toHaveBeenCalled();
  });
});

it("fits a sparse mobile graph with room for external outcome labels", () => {
  pane.width = 360; pane.height = 280;
  flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={2} />));
  const options = fitView.mock.calls[0][0];
  const view = getViewportForBounds({ x: 0, y: 0, width: 220, height: 260 }, pane.width, pane.height, 0.1, 2, options.padding ?? 0);
  expect(view.x + (220 + 88) * view.zoom).toBeLessThanOrEqual(pane.width);
});

describe("FitReadOnlyFlowView with a readable minimum zoom (#4064)", () => {
  it("fits the whole graph when the overview is already readable", async () => {
    pane.width = 1200; pane.height = 700;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={2} minReadableZoom={1} />));
    await settleAutoFit();
    expect(fitView).not.toHaveBeenCalled();
    expect(setViewport).toHaveBeenCalledTimes(1);
    expect(setViewport.mock.calls[0][0].zoom).toBeGreaterThanOrEqual(1);
  });

  it("keeps outcome labels readable on a narrow pane and re-fits on resize while untouched", async () => {
    pane.width = 340; pane.height = 600;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={2} minReadableZoom={1} />));
    await settleAutoFit();
    expect(setViewport.mock.calls[0][0].zoom).toBe(1);

    pane.width = 1200;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={2} minReadableZoom={1} />));
    await settleAutoFit();
    expect(setViewport).toHaveBeenCalledTimes(2);
    expect(setViewport.mock.calls[1][0].zoom).toBeGreaterThanOrEqual(1);
  });

  it("starts at the graph's reading start even when a later activity is selected", async () => {
    graphNodes.items[1]!.selected = true;
    pane.width = 340; pane.height = 600;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={2} minReadableZoom={1} />));
    await settleAutoFit();
    const view = setViewport.mock.calls[0][0];
    expect(view.zoom).toBe(1);
    // The first (task) activity at x 0..240 is in view, not the selected output at x 420.
    expect(view.x).toBeGreaterThanOrEqual(0);
    expect(240 + view.x).toBeLessThanOrEqual(340);
  });

  it("does not take the viewport back after the user moves it", async () => {
    pane.width = 340; pane.height = 600;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={2} minReadableZoom={1} />));
    await settleAutoFit();
    viewport.x += 40;
    pane.height = 520;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={2} minReadableZoom={1} />));
    await settleAutoFit();
    expect(setViewport).toHaveBeenCalledTimes(1);
  });
});
