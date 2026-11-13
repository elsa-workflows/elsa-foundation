import type { XYPosition } from "@xyflow/react";
import { defaultNodeWidth } from "./nodeWidth";

/** A rectangle in canvas (flow) coordinates. */
export interface PlacementRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * Room a node needs beyond its own box: the START badge and the selection toolbar sit above it,
 * the target port sits on its left and the outcome port labels ("Approve", "Unmatched") sit
 * outside its right edge. A palette placement keeps every node's reserved area clear.
 */
export const placementReserve = { top: 52, right: 120, bottom: 24, left: 24 } as const;

/**
 * The visible pane in canvas coordinates, from its top-left and bottom-right corners and the zoom.
 * The selection toolbar keeps its screen size, so below zoom 1 the top reserve covers fewer screen
 * pixels than the toolbar needs; the pane's top is moved down by the difference.
 */
export function visiblePlacementPane(start: XYPosition, end: XYPosition, zoom: number): PlacementRect {
  const toolbarInset = placementReserve.top * Math.max(0, 1 / zoom - 1);
  return { x: start.x, y: start.y + toolbarInset, width: end.x - start.x, height: Math.max(0, end.y - start.y - toolbarInset) };
}

/** Height assumed for a node that has not been measured yet (a new palette node). */
export const estimatedNodeHeight = 112;

const scanStep = 24;

export interface PalettePlacement {
  position: XYPosition;
  /** False when no free spot fits the visible pane and the canvas must pan to show the new node. */
  visible: boolean;
}

function reserved(rect: PlacementRect): PlacementRect {
  return {
    x: rect.x - placementReserve.left,
    y: rect.y - placementReserve.top,
    width: rect.width + placementReserve.left + placementReserve.right,
    height: rect.height + placementReserve.top + placementReserve.bottom
  };
}

function overlaps(a: PlacementRect, b: PlacementRect) {
  return a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
}

function contains(outer: PlacementRect, inner: PlacementRect) {
  return inner.x >= outer.x && inner.y >= outer.y
    && inner.x + inner.width <= outer.x + outer.width
    && inner.y + inner.height <= outer.y + outer.height;
}

/**
 * Where a palette click puts a new node when the user did not choose a point. The new node and
 * everything it reserves (badge, toolbar, port labels) must not overlap another node's reserved
 * area. It prefers the next column to the right of the rightmost node, then the first free spot
 * in reading order inside the visible pane, and only then the next column outside the pane.
 */
export function choosePalettePlacement(nodes: readonly PlacementRect[], visible: PlacementRect | null, size?: { width: number; height: number }): PalettePlacement {
  const width = size?.width ?? defaultNodeWidth;
  const height = size?.height ?? estimatedNodeHeight;
  const occupied = nodes.map(reserved);
  const fits = (position: XYPosition) => {
    const area = reserved({ ...position, width, height });
    return !occupied.some(other => overlaps(area, other));
  };
  const inView = (position: XYPosition) => !visible || contains(visible, reserved({ ...position, width, height }));

  if (!nodes.length) {
    const origin = visible ? { x: visible.x + placementReserve.left + scanStep, y: visible.y + placementReserve.top + scanStep } : { x: 0, y: placementReserve.top };
    return { position: { x: Math.round(origin.x), y: Math.round(origin.y) }, visible: true };
  }

  const rightmost = nodes.reduce((best, node) => node.x + node.width > best.x + best.width ? node : best);
  const nextColumn = { x: Math.round(rightmost.x + rightmost.width + placementReserve.right + placementReserve.left), y: Math.round(rightmost.y) };
  if (inView(nextColumn)) return { position: nextColumn, visible: true };

  if (visible) {
    const left = visible.x + placementReserve.left;
    const top = visible.y + placementReserve.top;
    const right = visible.x + visible.width - width - placementReserve.right;
    const bottom = visible.y + visible.height - height - placementReserve.bottom;
    for (let y = top; y <= bottom; y += scanStep) {
      for (let x = left; x <= right; x += scanStep) {
        const candidate = { x: Math.round(x), y: Math.round(y) };
        if (fits(candidate)) return { position: candidate, visible: true };
      }
    }
  }

  // Nothing to the right of the rightmost node reserves space, so the next column is always free.
  return { position: nextColumn, visible: inView(nextColumn) };
}
