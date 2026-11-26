import type { Rect, Viewport } from "@xyflow/react";
import { readStoredString, writeStoredValue } from "./editorHelpers";

export type RunCanvasFocusMode = "pan" | "zoom" | "off";

export const runCanvasFocusModeStorageKey = "elsa-studio-run-canvas-focus-mode";
export const RUN_CANVAS_FOCUS_MAX_ZOOM = 1.2;
export const RUN_CANVAS_FOCUS_PADDING = 0.35;
export const RUN_CANVAS_OFFSCREEN_MARGIN_PX = 36;
export const RUN_CANVAS_BOTTOM_MARGIN_PX = 56;

export const runCanvasFocusModeOptions: ReadonlyArray<{
  value: RunCanvasFocusMode;
  label: string;
  description: string;
}> = [
  {
    value: "pan",
    label: "Pan when offscreen",
    description: "Keep zoom; pan only if the activity is outside the view."
  },
  {
    value: "zoom",
    label: "Zoom to activity",
    description: "Center and fit the selected activity into view."
  },
  {
    value: "off",
    label: "Off",
    description: "Highlight only; never move the canvas automatically."
  }
];

export function readStoredRunCanvasFocusMode(fallback: RunCanvasFocusMode = "pan"): RunCanvasFocusMode {
  const value = readStoredString(runCanvasFocusModeStorageKey);
  return value === "pan" || value === "zoom" || value === "off" ? value : fallback;
}

export function writeStoredRunCanvasFocusMode(mode: RunCanvasFocusMode): void {
  writeStoredValue(runCanvasFocusModeStorageKey, mode);
}

export interface ViewportMargins {
  top?: number;
  right?: number;
  bottom?: number;
  left?: number;
}

/**
 * Returns true when a flow-space node rectangle already lies comfortably inside the visible
 * canvas pane at the current viewport. For nodes larger than the padded viewport (e.g. deeply
 * zoomed in), checks that the node center is inside the padded bounds so repeated selections
 * do not keep panning unnecessarily.
 */
export function isNodeVisibleInViewport(
  nodeBounds: Rect,
  viewport: Viewport,
  container: { width: number; height: number },
  margins: ViewportMargins = {}
): boolean {
  if (container.width <= 0 || container.height <= 0 || viewport.zoom <= 0) return false;

  const leftMargin = Math.min(margins.left ?? RUN_CANVAS_OFFSCREEN_MARGIN_PX, container.width / 6);
  const rightMargin = Math.min(margins.right ?? RUN_CANVAS_OFFSCREEN_MARGIN_PX, container.width / 6);
  const topMargin = Math.min(margins.top ?? RUN_CANVAS_OFFSCREEN_MARGIN_PX, container.height / 6);
  const bottomMargin = Math.min(margins.bottom ?? RUN_CANVAS_BOTTOM_MARGIN_PX, container.height / 4);

  const safeLeft = leftMargin;
  const safeRight = container.width - rightMargin;
  const safeTop = topMargin;
  const safeBottom = container.height - bottomMargin;

  const left = nodeBounds.x * viewport.zoom + viewport.x;
  const top = nodeBounds.y * viewport.zoom + viewport.y;
  const right = (nodeBounds.x + nodeBounds.width) * viewport.zoom + viewport.x;
  const bottom = (nodeBounds.y + nodeBounds.height) * viewport.zoom + viewport.y;

  const horizontalVisible = right - left <= safeRight - safeLeft
    ? left >= safeLeft && right <= safeRight
    : (left + right) / 2 >= safeLeft && (left + right) / 2 <= safeRight;

  const verticalVisible = bottom - top <= safeBottom - safeTop
    ? top >= safeTop && bottom <= safeBottom
    : (top + bottom) / 2 >= safeTop && (top + bottom) / 2 <= safeBottom;

  return horizontalVisible && verticalVisible;
}

interface FocusableFlowNode {
  position: { x: number; y: number };
  measured?: { width?: number; height?: number };
  width?: number;
  height?: number;
  internals?: { positionAbsolute?: { x: number; y: number } };
}

export interface FocusableFlowInstance {
  fitView(options?: { nodes?: Array<{ id: string }>; duration?: number; padding?: number; maxZoom?: number }): Promise<boolean> | boolean | void;
  getViewport?(): Viewport;
  setCenter?(x: number, y: number, options?: { zoom?: number; duration?: number }): Promise<boolean> | boolean | void;
  getInternalNode?(id: string): FocusableFlowNode | undefined;
  getNode?(id: string): FocusableFlowNode | undefined;
  getNodesBounds?(nodes: string[]): Rect;
}

export function resolveFlowNodeBounds(flowInstance: FocusableFlowInstance, nodeId: string): Rect | null {
  const internal = flowInstance.getInternalNode?.(nodeId);
  if (internal) {
    const position = internal.internals?.positionAbsolute ?? internal.position;
    if (position && Number.isFinite(position.x) && Number.isFinite(position.y)) {
      return {
        x: position.x,
        y: position.y,
        width: internal.measured?.width ?? internal.width ?? 0,
        height: internal.measured?.height ?? internal.height ?? 0
      };
    }
  }

  const node = flowInstance.getNode?.(nodeId);
  if (node && Number.isFinite(node.position.x) && Number.isFinite(node.position.y)) {
    return {
      x: node.position.x,
      y: node.position.y,
      width: node.measured?.width ?? node.width ?? 0,
      height: node.measured?.height ?? node.height ?? 0
    };
  }

  const bounds = flowInstance.getNodesBounds?.([nodeId]);
  if (bounds && Number.isFinite(bounds.x) && Number.isFinite(bounds.y) && (bounds.width > 0 || bounds.height > 0)) {
    return bounds;
  }

  return null;
}

export type RunCanvasFocusOutcome = "skipped" | "visible" | "panned" | "zoomed";

export function focusRunCanvasNode({
  flowInstance,
  nodeId,
  mode,
  animate,
  containerSize
}: {
  flowInstance: FocusableFlowInstance;
  nodeId: string;
  mode: RunCanvasFocusMode;
  animate: boolean;
  containerSize?: { width: number; height: number } | null;
}): RunCanvasFocusOutcome {
  if (mode === "off") return "skipped";

  const duration = animate ? 220 : 0;

  if (mode === "zoom") {
    void flowInstance.fitView({
      nodes: [{ id: nodeId }],
      duration,
      padding: RUN_CANVAS_FOCUS_PADDING,
      maxZoom: RUN_CANVAS_FOCUS_MAX_ZOOM
    });
    return "zoomed";
  }

  if (typeof flowInstance.getViewport !== "function" || typeof flowInstance.setCenter !== "function") {
    return "skipped";
  }

  const bounds = resolveFlowNodeBounds(flowInstance, nodeId);
  if (!bounds) return "skipped";

  const viewport = flowInstance.getViewport();
  if (containerSize && containerSize.width > 0 && containerSize.height > 0 && isNodeVisibleInViewport(bounds, viewport, containerSize)) {
    return "visible";
  }

  void flowInstance.setCenter(
    bounds.x + bounds.width / 2,
    bounds.y + bounds.height / 2,
    { zoom: viewport.zoom, duration }
  );
  return "panned";
}
