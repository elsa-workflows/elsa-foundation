import { describe, expect, it } from "vitest";
import { choosePalettePlacement, estimatedNodeHeight, placementReserve, visiblePlacementPane, type PlacementRect } from "../graph-authoring/palettePlacement";
import { defaultNodeWidth } from "../graph-authoring/nodeWidth";

const reservedArea = (rect: PlacementRect): PlacementRect => ({
  x: rect.x - placementReserve.left,
  y: rect.y - placementReserve.top,
  width: rect.width + placementReserve.left + placementReserve.right,
  height: rect.height + placementReserve.top + placementReserve.bottom
});
const overlaps = (a: PlacementRect, b: PlacementRect) => a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
const inside = (outer: PlacementRect, inner: PlacementRect) =>
  inner.x >= outer.x && inner.y >= outer.y && inner.x + inner.width <= outer.x + outer.width && inner.y + inner.height <= outer.y + outer.height;
const newNode = (position: { x: number; y: number }): PlacementRect => ({ ...position, width: defaultNodeWidth, height: estimatedNodeHeight });

describe("choosePalettePlacement", () => {
  // The #3847 review: a 590px pane at zoom 1, a User Task with three outcome labels on its right.
  const pane = { x: 0, y: 0, width: 590, height: 630 };

  it("keeps the first node's START badge and toolbar inside the pane", () => {
    const placement = choosePalettePlacement([], pane);
    expect(placement.visible).toBe(true);
    expect(placement.position.y - placementReserve.top).toBeGreaterThanOrEqual(pane.y);
    expect(placement.position.x - placementReserve.left).toBeGreaterThanOrEqual(pane.x);
  });

  it("does not put the next node on another node's outcome labels", () => {
    const task = { x: 48, y: 76, width: 246, height: 120 };
    const placement = choosePalettePlacement([task], pane);
    expect(placement.visible).toBe(true);
    expect(overlaps(reservedArea(newNode(placement.position)), reservedArea(task))).toBe(false);
    expect(inside(pane, reservedArea(newNode(placement.position)))).toBe(true);
  });

  it("prefers the next column when it fits the pane", () => {
    const wide = { x: 0, y: 0, width: 1400, height: 600 };
    const task = { x: 48, y: 76, width: 246, height: 120 };
    const placement = choosePalettePlacement([task], wide);
    expect(placement.position).toEqual({ x: task.x + task.width + placementReserve.right + placementReserve.left, y: task.y });
  });

  it("falls back to the next column outside a full pane and asks the canvas to pan", () => {
    const tiny = { x: 0, y: 0, width: 400, height: 260 };
    const task = { x: 48, y: 76, width: 246, height: 120 };
    const placement = choosePalettePlacement([task], tiny);
    expect(placement.visible).toBe(false);
    expect(overlaps(reservedArea(newNode(placement.position)), reservedArea(task))).toBe(false);
  });

  it("keeps the screen-size toolbar inside the pane when zoomed out", () => {
    // A 590x630 screen pane at zoom 0.5 spans 1180x1260 canvas units.
    const zoom = 0.5;
    const visible = visiblePlacementPane({ x: 0, y: 0 }, { x: 1180, y: 1260 }, zoom);
    const placement = choosePalettePlacement([], visible);
    // The screen distance from the pane top to the node must cover the toolbar's screen-size reserve.
    expect(placement.position.y * zoom).toBeGreaterThanOrEqual(placementReserve.top);
    expect(visible.y + visible.height).toBe(1260);
  });

  it("leaves the pane unchanged at zoom 1 and above", () => {
    expect(visiblePlacementPane({ x: 10, y: 20 }, { x: 600, y: 650 }, 1)).toEqual({ x: 10, y: 20, width: 590, height: 630 });
    expect(visiblePlacementPane({ x: 10, y: 20 }, { x: 600, y: 650 }, 1.5)).toEqual({ x: 10, y: 20, width: 590, height: 630 });
  });

  it("measures the visible pane in canvas coordinates", () => {
    const panned = { x: -300, y: 200, width: 590, height: 630 };
    const placement = choosePalettePlacement([], panned);
    expect(inside(panned, reservedArea(newNode(placement.position)))).toBe(true);
  });
});
