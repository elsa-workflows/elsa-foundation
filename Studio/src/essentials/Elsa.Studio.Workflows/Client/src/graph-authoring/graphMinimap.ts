/** Unzoomed graph box used to decide whether the designer minimap would earn its space. */
export interface GraphBox {
  width: number;
  height: number;
}

/** Canvas pane size in CSS pixels. */
export interface GraphViewportBox {
  width: number;
  height: number;
}

/** A node’s position and measured size — enough to decide overflow without a React Flow store. */
export interface GraphMinimapNode {
  hidden?: boolean;
  position: { x: number; y: number };
  width?: number | null;
  height?: number | null;
  measured?: { width?: number; height?: number };
}

/**
 * True when the graph’s natural (zoom = 1) size is larger than the pane. An unmeasured pane never
 * counts as overflow, so the minimap stays off until the canvas has a real size.
 */
export function graphOverflowsViewport(graph: GraphBox, viewport: GraphViewportBox): boolean {
  return viewport.width > 0
    && viewport.height > 0
    && (graph.width > viewport.width || graph.height > viewport.height);
}

/** Axis-aligned bounds of visible nodes at zoom 1. Hidden and empty sets have no box. */
export function graphNodeBounds(nodes: readonly GraphMinimapNode[]): GraphBox | null {
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const node of nodes) {
    if (node.hidden) continue;
    const width = node.measured?.width ?? node.width ?? 0;
    const height = node.measured?.height ?? node.height ?? 0;
    minX = Math.min(minX, node.position.x);
    minY = Math.min(minY, node.position.y);
    maxX = Math.max(maxX, node.position.x + width);
    maxY = Math.max(maxY, node.position.y + height);
  }
  if (!Number.isFinite(minX)) return null;
  return { width: maxX - minX, height: maxY - minY };
}

/** Hide the minimap only after the graph shrinks below this fraction of the pane. */
export const graphMinimapHideThreshold = 0.95;

/** React Flow's default MiniMap footprint, which the designer minimap uses. */
export const graphMinimapSize: GraphViewportBox = { width: 200, height: 150 };

/**
 * Narrowest pane that can spare a corner for the minimap: 2.5× its width. On narrower,
 * phone-width panes the 200×150 overview covers nodes the author needs to click, so the
 * pane keeps its full width for the graph instead. Height is not gated: docked layouts
 * legitimately run short, wide canvases where the overview still leaves the graph usable.
 */
export const graphMinimapMinimumPaneWidth = graphMinimapSize.width * 2.5;

/** True when the pane is wide enough that the minimap leaves the graph usable. */
export function paneFitsGraphMinimap(viewport: GraphViewportBox): boolean {
  return viewport.width >= graphMinimapMinimumPaneWidth;
}

/**
 * Designer minimap is for graphs that do not fit the pane at their natural size, on panes
 * wide enough to give up a corner for it.
 */
export function shouldShowGraphMinimap(
  nodes: readonly GraphMinimapNode[],
  viewport: GraphViewportBox,
  currentlyShown = false
): boolean {
  const bounds = graphNodeBounds(nodes);
  if (bounds === null) return false;
  if (!(viewport.width > 0 && viewport.height > 0)) return false;
  if (!paneFitsGraphMinimap(viewport)) return false;
  if (currentlyShown) {
    return bounds.width >= viewport.width * graphMinimapHideThreshold
      || bounds.height >= viewport.height * graphMinimapHideThreshold;
  }
  return graphOverflowsViewport(bounds, viewport);
}
