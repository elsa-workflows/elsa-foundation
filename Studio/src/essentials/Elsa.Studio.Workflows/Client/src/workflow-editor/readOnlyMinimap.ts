import type { FitViewOptions } from "@xyflow/react";

/** Hide the read-only MiniMap on sparse inspector and run-detail graphs (one or two nodes). */
export const READ_ONLY_MINIMAP_MAX_HIDDEN_NODES = 2;

/** Pixel size passed to xyflow MiniMap so the SVG and pan/zoom math scale together. */
export const READ_ONLY_MINIMAP_WIDTH = 120;
export const READ_ONLY_MINIMAP_HEIGHT = 80;

/** Border width from `.wf-instance-canvas .react-flow__minimap` in `styles.css`; keep the two in step. */
export const READ_ONLY_MINIMAP_BORDER = 1;
/** xyflow's `.react-flow__panel` margin, which places the MiniMap this far from the pane's bottom-right corner. */
export const READ_ONLY_MINIMAP_PANEL_OFFSET = 15;
/** Rendered MiniMap box: the content size plus the border on both sides. */
export const READ_ONLY_MINIMAP_OUTER_WIDTH = READ_ONLY_MINIMAP_WIDTH + 2 * READ_ONLY_MINIMAP_BORDER;
export const READ_ONLY_MINIMAP_OUTER_HEIGHT = READ_ONLY_MINIMAP_HEIGHT + 2 * READ_ONLY_MINIMAP_BORDER;
/** Gap a readable first view keeps between the graph and the MiniMap. */
export const READ_ONLY_MINIMAP_CLEARANCE = 8;

/** Token fills so node rects and the viewport mask stay visible on porcelain and minimal-dark. */
export const readOnlyMinimapNodeColor = "var(--studio-text)";
export const readOnlyMinimapNodeStrokeColor = "var(--studio-text)";
export const readOnlyMinimapMaskColor = "color-mix(in srgb, var(--studio-text) 28%, transparent)";
export const readOnlyMinimapMaskStrokeColor = "var(--studio-text)";

/**
 * Extra fitView padding so a 120×80 bottom-right MiniMap never covers a node.
 * Applied only when {@link shouldShowReadOnlyMiniMap} is true.
 */
export const readOnlyMinimapFitViewOptions: FitViewOptions = {
  padding: { top: 0.12, right: 0.24, bottom: 0.24, left: 0.12 }
};

export function shouldShowReadOnlyMiniMap(nodeCount: number): boolean {
  return nodeCount > READ_ONLY_MINIMAP_MAX_HIDDEN_NODES;
}

// Sparse graphs have no MiniMap, but their port labels extend beyond measured node boxes.
const readOnlyOutcomeFitViewOptions: FitViewOptions = {
  padding: { top: "24px", right: "112px", bottom: "32px", left: "32px" }
};

export function getReadOnlyFitViewOptions(nodeCount: number): FitViewOptions {
  return shouldShowReadOnlyMiniMap(nodeCount) ? readOnlyMinimapFitViewOptions : readOnlyOutcomeFitViewOptions;
}
