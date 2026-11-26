import { expect, it } from "vitest";
import { nextNativeBlockPosition } from "../graph-authoring/nativeBlockPlacement";

it("starts an empty scope at the normal canvas origin", () => {
  expect(nextNativeBlockPosition([], null)).toEqual({ x: 100, y: 100 });
});

it("clears every projected node, including measured and resized heights, and aligns to the selected step", () => {
  const nodes = [{ id: "wide", position: { x: 80, y: 100 }, height: 600, measured: { height: 650 } },
    { id: "last", position: { x: 500, y: 400 }, height: 640 }];
  const before = structuredClone(nodes);
  const position = nextNativeBlockPosition(nodes, "wide");
  expect(position.y).toBeGreaterThan(400 + 640);
  expect(position.y).toBeGreaterThan(100 + 650);
  expect(position.x).toBe(80); expect(nodes).toEqual(before);
});

it("uses measured geometry when a node has no saved layout height", () => {
  expect(nextNativeBlockPosition([{ id: "projected", position: { x: 120, y: 340 }, measured: { height: 900 } }], null))
    .toEqual({ x: 120, y: 1288 });
});

it("keeps repeated new blocks separate without moving any prior step", () => {
  const original = { id: "existing", position: { x: 80, y: 100 }, measured: { height: 320 } };
  const first = nextNativeBlockPosition([original], "existing");
  const second = nextNativeBlockPosition([original, { id: "first", position: first, measured: { height: 400 } }], "first");
  expect(first.y).toBeGreaterThan(original.position.y + 320);
  expect(second.y).toBeGreaterThan(first.y + 400);
  expect(original.position).toEqual({ x: 80, y: 100 });
});
