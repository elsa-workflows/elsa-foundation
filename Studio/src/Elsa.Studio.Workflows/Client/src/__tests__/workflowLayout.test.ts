import { describe, expect, it } from "vitest";
import { computeAutoLayout, type LayoutEdge, type LayoutNode } from "../workflowLayout";

const nodes = (...ids: string[]): LayoutNode[] => ids.map(id => ({ id }));
const edge = (source: string, target: string): LayoutEdge => ({ source, target });

describe("computeAutoLayout", () => {
  it("lays out a sequence left to right", () => {
    const positions = computeAutoLayout(nodes("a", "b", "c"), [], "sequence");
    expect(positions.get("a")).toEqual({ x: 0, y: 0 });
    expect(positions.get("b")).toEqual({ x: 280, y: 0 });
    expect(positions.get("c")).toEqual({ x: 560, y: 0 });
  });

  it("assigns flowchart columns by longest path", () => {
    const positions = computeAutoLayout(nodes("a", "b", "c"), [edge("a", "b"), edge("b", "c")], "flowchart");
    expect(positions.get("a")?.x).toBe(0);
    expect(positions.get("b")?.x).toBe(320);
    expect(positions.get("c")?.x).toBe(640);
  });

  it("stacks branches in the same column on separate rows", () => {
    const positions = computeAutoLayout(
      nodes("root", "left", "right"),
      [edge("root", "left"), edge("root", "right")],
      "flowchart"
    );
    expect(positions.get("left")?.x).toBe(320);
    expect(positions.get("right")?.x).toBe(320);
    expect(positions.get("left")?.y).not.toBe(positions.get("right")?.y);
  });

  it("places a merge node past its deepest predecessor", () => {
    const positions = computeAutoLayout(
      nodes("a", "b", "c", "merge"),
      [edge("a", "b"), edge("b", "c"), edge("a", "merge"), edge("c", "merge")],
      "flowchart"
    );
    // merge follows the a -> b -> c chain, so it must sit in column 3.
    expect(positions.get("merge")?.x).toBe(960);
  });

  it("does not loop forever on a cycle", () => {
    const positions = computeAutoLayout(
      nodes("a", "b", "c"),
      [edge("a", "b"), edge("b", "c"), edge("c", "a")],
      "flowchart"
    );
    expect(positions.size).toBe(3);
    for (const id of ["a", "b", "c"]) {
      expect(Number.isFinite(positions.get(id)?.x)).toBe(true);
    }
  });

  it("parks fully disconnected nodes in a trailing column", () => {
    const positions = computeAutoLayout(
      nodes("a", "b", "lonely"),
      [edge("a", "b")],
      "flowchart"
    );
    // a -> b spans columns 0 and 1; the disconnected node goes to column 2.
    expect(positions.get("lonely")?.x).toBe(640);
  });

  it("returns an empty map for no nodes", () => {
    expect(computeAutoLayout([], [], "flowchart").size).toBe(0);
  });
});
