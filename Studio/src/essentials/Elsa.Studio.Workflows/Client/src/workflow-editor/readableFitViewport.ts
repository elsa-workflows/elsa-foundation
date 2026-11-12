import { getViewportForBounds, type FitViewOptions, type Node, type Rect, type Viewport } from "@xyflow/react";
import type { WorkflowRuntimeNodeOverlay } from "../workflowAdapter";

/**
 * Smallest zoom at which a run canvas's first view keeps outcome port labels readable.
 * `.wf-node-port-label` is 10px, so any zoom below 1 renders it under the 10px floor (#4064).
 */
export const READ_ONLY_READABLE_ZOOM = 1;

/**
 * Screen margins that keep the graph's own edges in view when the first view is clamped to a
 * readable zoom. The right margin leaves room for outcome labels drawn beyond the node box.
 */
const readableEdgeMargin = { top: 24, right: 112, bottom: 32, left: 32 };
const minimumFocusInset = 8;

const settledStatuses = new Set(["Completed", "Canceled", "Cancelled"]);

function focusRank(node: Node): number {
  const runtime = (node.data as { runtime?: WorkflowRuntimeNodeOverlay } | undefined)?.runtime;
  if (node.selected || runtime?.selected) return 0;
  if (runtime?.hasBlockingIncident) return 1;
  if (runtime && (runtime.incidentCount > 0 || runtime.status === "Faulted")) return 2;
  if (runtime?.status && !settledStatuses.has(runtime.status)) return 3;
  return 4;
}

/**
 * The activity a readable first view centres on: the selected one, then one that needs attention
 * (blocking incident, incident or fault), then one still running or suspended, and otherwise the
 * graph's reading start (leftmost, then topmost).
 */
export function pickReadOnlyFocusNode<T extends Node>(nodes: T[]): T | undefined {
  return [...nodes].sort((left, right) =>
    focusRank(left) - focusRank(right) || left.position.x - right.position.x || left.position.y - right.position.y)[0];
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
}

/**
 * Fit the whole graph when that keeps labels readable. Otherwise use the readable zoom, centre the
 * focus activity, and clamp so the view never scrolls past the graph's edges into empty canvas.
 */
export function getReadableFitViewport({ graph, focus, width, height, padding, minZoom, maxZoom, readableZoom }: ReadableFitViewportInput): Viewport {
  const overview = getViewportForBounds(graph, width, height, minZoom, maxZoom, padding ?? 0.1);
  if (overview.zoom >= readableZoom || !focus) return overview;
  const zoom = Math.min(readableZoom, maxZoom);
  const margin = readableEdgeMargin;
  return {
    zoom,
    x: clampAxis(focus.x, focus.width, graph.x, graph.width, width, margin.left, margin.right, zoom),
    y: clampAxis(focus.y, focus.height, graph.y, graph.height, height, margin.top, margin.bottom, zoom)
  };
}
