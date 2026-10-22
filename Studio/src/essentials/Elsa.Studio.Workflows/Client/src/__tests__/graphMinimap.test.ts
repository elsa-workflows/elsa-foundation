import { describe, expect, it } from "vitest";
import {
  graphNodeBounds,
  graphOverflowsViewport,
  shouldShowGraphMinimap,
  type GraphMinimapNode
} from "../graph-authoring/graphMinimap";

function node(
  id: string,
  x: number,
  y: number,
  size: { width: number; height: number } = { width: 180, height: 80 },
  extras: Partial<GraphMinimapNode> = {}
): GraphMinimapNode & { id: string } {
  return { id, position: { x, y }, measured: size, ...extras };
}

describe("graphOverflowsViewport", () => {
  it("is false while the pane has not been measured", () => {
    expect(graphOverflowsViewport({ width: 800, height: 600 }, { width: 0, height: 400 })).toBe(false);
    expect(graphOverflowsViewport({ width: 800, height: 600 }, { width: 400, height: 0 })).toBe(false);
  });

  it("is false when a small graph fits the pane", () => {
    expect(graphOverflowsViewport({ width: 400, height: 200 }, { width: 530, height: 500 })).toBe(false);
  });

  it("is true when the graph is wider or taller than the pane", () => {
    expect(graphOverflowsViewport({ width: 800, height: 200 }, { width: 530, height: 500 })).toBe(true);
    expect(graphOverflowsViewport({ width: 400, height: 700 }, { width: 530, height: 500 })).toBe(true);
  });

  it("treats an exact fit as not overflowing", () => {
    expect(graphOverflowsViewport({ width: 530, height: 500 }, { width: 530, height: 500 })).toBe(false);
  });
});

describe("graphNodeBounds", () => {
  it("returns null for an empty or fully hidden set", () => {
    expect(graphNodeBounds([])).toBeNull();
    expect(graphNodeBounds([node("a", 0, 0, { width: 180, height: 80 }, { hidden: true })])).toBeNull();
  });

  it("uses measured size when present", () => {
    expect(graphNodeBounds([
      node("a", 80, 120),
      node("b", 360, 120)
    ])).toEqual({ width: 460, height: 80 });
  });

  it("falls back to width/height and ignores hidden nodes", () => {
    expect(graphNodeBounds([
      { position: { x: 0, y: 0 }, width: 100, height: 40 },
      { position: { x: 400, y: 200 }, width: 100, height: 40, hidden: true }
    ])).toEqual({ width: 100, height: 40 });
  });
});

describe("shouldShowGraphMinimap", () => {
  const laptopPane = { width: 530, height: 500 };

  it("hides the minimap for a newcomer two-node graph on a laptop canvas", () => {
    expect(shouldShowGraphMinimap([
      node("write-one", 80, 120),
      node("write-two", 360, 120)
    ], laptopPane)).toBe(false);
  });

  it("shows the minimap once the graph no longer fits", () => {
    expect(shouldShowGraphMinimap([
      node("a", 0, 0),
      node("b", 0, 600)
    ], laptopPane)).toBe(true);
  });

  it("stays hidden with no nodes", () => {
    expect(shouldShowGraphMinimap([], laptopPane)).toBe(false);
  });
});
