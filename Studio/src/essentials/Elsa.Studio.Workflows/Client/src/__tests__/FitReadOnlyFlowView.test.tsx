import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { readOnlyMinimapFitViewOptions } from "../workflow-editor/readOnlyMinimap";

const viewport = vi.hoisted(() => ({ x: 0, y: 0, zoom: 1 }));
const fitView = vi.hoisted(() => vi.fn(async () => true));
const getViewport = vi.hoisted(() => vi.fn(() => ({ x: viewport.x, y: viewport.y, zoom: viewport.zoom })));
const pane = vi.hoisted(() => ({ initialized: true, width: 800, height: 600 }));

vi.mock("@xyflow/react", () => ({
  useNodesInitialized: () => pane.initialized,
  useReactFlow: () => ({ fitView, getViewport }),
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
