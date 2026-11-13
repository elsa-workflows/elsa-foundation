import { getViewportForBounds, type FitViewOptions, type Node, type Rect, type Viewport } from "@xyflow/react";
import { READ_ONLY_CONTROLS_INSET, READ_ONLY_MINIMAP_INSET_BOTTOM, READ_ONLY_MINIMAP_INSET_RIGHT } from "./readOnlyMinimap";

/**
 * Smallest zoom at which a run canvas's first view keeps outcome port labels readable.
 * `.wf-node-port-label` is 10px, so any zoom below 1 renders it under the 10px floor (#4064).
 */
export const READ_ONLY_READABLE_ZOOM = 1;

/** Screen margin, in pixels, on each side of the pane. */
export interface ReadableEdgeMargin { top: number; right: number; bottom: number; left: number }

/**
 * Screen margins that keep the graph's own edges in view when the first view is clamped to a
 * readable zoom. The right margin leaves room for outcome labels drawn beyond the node box, and the
 * bottom margin keeps the graph above the bottom-left zoom controls row (#4196).
 */
export const readableEdgeMargin: ReadableEdgeMargin = { top: 24, right: 112, bottom: READ_ONLY_CONTROLS_INSET, left: 32 };

/**
 * Margins when the read-only MiniMap is shown: the right and bottom margins cover the MiniMap's
 * rendered box and its offset from the pane's bottom-right corner, plus a clearance gap. The bottom
 * margin also clears the shorter controls row on the left.
 */
export const readableMinimapEdgeMargin: ReadableEdgeMargin = {
  ...readableEdgeMargin,
  right: READ_ONLY_MINIMAP_INSET_RIGHT,
  bottom: READ_ONLY_MINIMAP_INSET_BOTTOM
};
const minimumFocusInset = 8;

/**
 * The activity a readable first view starts from: the graph's reading start (leftmost, then
 * topmost). It depends only on the layout, never on runtime evidence that may still be loading,
 * so the first view is the same on every visit. Showing a selected activity is the separate
 * focus request's job.
 */
export function pickReadOnlyFocusNode<T extends Node>(nodes: T[]): T | undefined {
  return [...nodes].sort((left, right) => left.position.x - right.position.x || left.position.y - right.position.y)[0];
}

/** The translation that centres `[start, start + size]` between the screen margins. */
function centreAxis(start: number, size: number, pane: number, before: number, after: number, zoom: number) {
  return before + (pane - before - after - size * zoom) / 2 - start * zoom;
}

function clampAxis(focusStart: number, focusSize: number, start: number, size: number, pane: number, before: number, after: number, zoom: number) {
  const latest = before - start * zoom;
  const earliest = pane - after - (start + size) * zoom;
  // The graph fits along this axis: centre all of it.
  if (earliest >= latest) return centreAxis(start, size, pane, before, after, zoom);
  // A focus activity wider than the pane keeps its start in view, as text reads from its start.
  const focused = Math.max(centreAxis(focusStart, focusSize, pane, before, after, zoom), minimumFocusInset - focusStart * zoom);
  return Math.min(latest, Math.max(earliest, focused));
}

export interface ReadableFitViewportInput {
  graph: Rect;
  focus: Rect | null;
  width: number;
  height: number;
  padding: FitViewOptions["padding"];
  minZoom: number;
  maxZoom: number;
  readableZoom: number;
  /** Screen margins for the clamped readable view; use {@link readableMinimapEdgeMargin} when the MiniMap is shown. */
  margin?: ReadableEdgeMargin;
}

/**
 * Fit the whole graph when that keeps labels readable. Otherwise use the readable zoom, centre the
 * graph's reading start, and clamp so the view never scrolls past the graph's edges into empty canvas.
 */
export function getReadableFitViewport({ graph, focus, width, height, padding, minZoom, maxZoom, readableZoom, margin = readableEdgeMargin }: ReadableFitViewportInput): Viewport {
  const overview = getViewportForBounds(graph, width, height, minZoom, maxZoom, padding ?? 0.1);
  if (overview.zoom >= readableZoom || !focus) return overview;
  const zoom = Math.min(readableZoom, maxZoom);
  return {
    zoom,
    x: clampAxis(focus.x, focus.width, graph.x, graph.width, width, margin.left, margin.right, zoom),
    y: clampAxis(focus.y, focus.height, graph.y, graph.height, height, margin.top, margin.bottom, zoom)
  };
}
