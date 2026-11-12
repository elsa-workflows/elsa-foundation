import type { FitViewOptions } from "@xyflow/react";

/** Hide the read-only MiniMap on sparse inspector and run-detail graphs (one or two nodes). */
export const READ_ONLY_MINIMAP_MAX_HIDDEN_NODES = 2;

/** Pixel size passed to xyflow MiniMap so the SVG and pan/zoom math scale together. */
export const READ_ONLY_MINIMAP_WIDTH = 120;
export const READ_ONLY_MINIMAP_HEIGHT = 80;

/** xyflow floats every panel (controls, MiniMap) 15px in from the pane's edge: `.react-flow__panel { margin: 15px }`. */
export const READ_ONLY_PANEL_MARGIN = 15;
/** Room kept between a fitted node and a floating panel. */
export const READ_ONLY_PANEL_GAP = 8;
/** One xyflow control button is 26px square (`box-sizing: border-box`, so its 1px border is inside). */
export const READ_ONLY_CONTROLS_BUTTON_SIZE = 26;

/**
 * Read-only canvases lay their zoom controls out as one row along the bottom-left (#4196). The default
 * vertical stack is about 104px tall, so on a short phone-width pane (about 245px) it covers the left
 * edge of whichever activity sits there, on the first view and after Fit View alike. A row is one
 * button tall and lies in the bottom strip the fitted views below already keep clear.
 */
export const READ_ONLY_CONTROLS_ORIENTATION = "horizontal" as const;

/** Bottom inset that keeps fitted nodes clear of the controls row. */
export const READ_ONLY_CONTROLS_CLEARANCE = READ_ONLY_PANEL_MARGIN + READ_ONLY_CONTROLS_BUTTON_SIZE + READ_ONLY_PANEL_GAP;
/** Right and bottom insets that keep fitted nodes clear of the MiniMap and its 1px border. */
export const READ_ONLY_MINIMAP_CLEARANCE_RIGHT = READ_ONLY_PANEL_MARGIN + READ_ONLY_MINIMAP_WIDTH + 2 + READ_ONLY_PANEL_GAP;
export const READ_ONLY_MINIMAP_CLEARANCE_BOTTOM = READ_ONLY_PANEL_MARGIN + READ_ONLY_MINIMAP_HEIGHT + 2 + READ_ONLY_PANEL_GAP;

/** Token fills so node rects and the viewport mask stay visible on porcelain and minimal-dark. */
export const readOnlyMinimapNodeColor = "var(--studio-text)";
export const readOnlyMinimapNodeStrokeColor = "var(--studio-text)";
export const readOnlyMinimapMaskColor = "color-mix(in srgb, var(--studio-text) 28%, transparent)";
export const readOnlyMinimapMaskStrokeColor = "var(--studio-text)";

/**
 * Extra fitView padding so a 120×80 bottom-right MiniMap never covers a node.
 * Applied only when {@link shouldShowReadOnlyMiniMap} is true. The bottom inset is in pixels: a 0.24
 * fraction is only about 23px on a 245px pane, too little for the MiniMap and the controls row (#4196).
 * The other sides stay fractions so a phone-width overview keeps its width.
 */
export const readOnlyMinimapFitViewOptions: FitViewOptions = {
  padding: { top: 0.12, right: 0.24, bottom: `${READ_ONLY_MINIMAP_CLEARANCE_BOTTOM}px`, left: 0.12 }
};

export function shouldShowReadOnlyMiniMap(nodeCount: number): boolean {
  return nodeCount > READ_ONLY_MINIMAP_MAX_HIDDEN_NODES;
}

// Sparse graphs have no MiniMap, but their port labels extend beyond measured node boxes, and the
// bottom inset keeps them above the controls row.
const readOnlyOutcomeFitViewOptions: FitViewOptions = {
  padding: { top: "24px", right: "112px", bottom: `${READ_ONLY_CONTROLS_CLEARANCE}px`, left: "32px" }
};

export function getReadOnlyFitViewOptions(nodeCount: number): FitViewOptions {
  return shouldShowReadOnlyMiniMap(nodeCount) ? readOnlyMinimapFitViewOptions : readOnlyOutcomeFitViewOptions;
}
