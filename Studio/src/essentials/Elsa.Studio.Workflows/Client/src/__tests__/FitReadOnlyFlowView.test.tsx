import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { readOnlyMinimapFitViewOptions } from "../workflow-editor/readOnlyMinimap";

const fitView = vi.hoisted(() => vi.fn(async () => true));
const pane = vi.hoisted(() => ({ initialized: true, width: 800, height: 600 }));

vi.mock("@xyflow/react", () => ({
  useNodesInitialized: () => pane.initialized,
  useReactFlow: () => ({ fitView }),
  useStore: (selector: (state: { width: number; height: number }) => unknown) => selector({ width: pane.width, height: pane.height })
}));

import { FitReadOnlyFlowView } from "../workflow-editor/useReadOnlyFlowCanvas";

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  fitView.mockClear();
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

  it("re-fits when the pane shrinks so a status line cannot clip the last node", () => {
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    expect(fitView).toHaveBeenCalledTimes(1);

    pane.height = 500;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    expect(fitView).toHaveBeenCalledTimes(2);
    expect(fitView).toHaveBeenLastCalledWith(readOnlyMinimapFitViewOptions);
  });

  it("does not fit an unmeasured pane", () => {
    pane.width = 0;
    pane.height = 0;
    flushSync(() => root.render(<FitReadOnlyFlowView nodeCount={4} />));
    expect(fitView).not.toHaveBeenCalled();
  });
});
