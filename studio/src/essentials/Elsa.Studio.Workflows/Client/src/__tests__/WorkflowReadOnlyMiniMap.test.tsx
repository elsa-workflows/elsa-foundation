import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { WorkflowReadOnlyMiniMap } from "../workflow-editor/WorkflowReadOnlyMiniMap";
import {
  READ_ONLY_MINIMAP_HEIGHT,
  READ_ONLY_MINIMAP_WIDTH,
  readOnlyMinimapMaskColor,
  readOnlyMinimapNodeColor,
  readOnlyMinimapNodeStrokeColor
} from "../workflow-editor/readOnlyMinimap";

const MiniMap = vi.hoisted(() => vi.fn((props: {
  style?: { width?: number; height?: number };
  nodeColor?: string;
  nodeStrokeColor?: string;
  maskColor?: string;
}) => (
  <div
    data-testid="read-only-minimap"
    data-width={props.style?.width}
    data-height={props.style?.height}
    data-node-color={props.nodeColor}
    data-mask-color={props.maskColor}
  />
)));

vi.mock("@xyflow/react", () => ({
  MiniMap: (props: {
    style?: { width?: number; height?: number };
    nodeColor?: string;
    nodeStrokeColor?: string;
    maskColor?: string;
  }) => MiniMap(props)
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  MiniMap.mockClear();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

describe("WorkflowReadOnlyMiniMap", () => {
  it("renders nothing for a sparse graph", () => {
    flushSync(() => root.render(<WorkflowReadOnlyMiniMap nodeCount={2} />));
    expect(container.querySelector("[data-testid='read-only-minimap']")).toBeNull();
  });

  it("renders the MiniMap once the graph has more than two nodes", () => {
    flushSync(() => root.render(<WorkflowReadOnlyMiniMap nodeCount={3} />));
    expect(container.querySelector("[data-testid='read-only-minimap']")).toBeTruthy();
  });

  it("passes an explicit 120×80 size and token colors so xyflow paints node rects", () => {
    flushSync(() => root.render(<WorkflowReadOnlyMiniMap nodeCount={3} />));
    expect(MiniMap).toHaveBeenCalled();
    const props = MiniMap.mock.calls.at(-1)?.[0];
    expect(props?.style).toEqual({ width: READ_ONLY_MINIMAP_WIDTH, height: READ_ONLY_MINIMAP_HEIGHT });
    expect(props?.style).toEqual({ width: 120, height: 80 });
    expect(props?.nodeColor).toBe(readOnlyMinimapNodeColor);
    expect(props?.nodeStrokeColor).toBe(readOnlyMinimapNodeStrokeColor);
    expect(props?.maskColor).toBe(readOnlyMinimapMaskColor);
    expect(props?.nodeColor).toBe("var(--studio-text)");
    expect(container.querySelector("[data-testid='read-only-minimap']")?.getAttribute("data-width")).toBe("120");
    expect(container.querySelector("[data-testid='read-only-minimap']")?.getAttribute("data-height")).toBe("80");
  });
});
