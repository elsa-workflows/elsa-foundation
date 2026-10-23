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

/** Designer minimap is for graphs that do not fit the pane at their natural size. */
export function shouldShowGraphMinimap(
  nodes: readonly GraphMinimapNode[],
  viewport: GraphViewportBox
): boolean {
  const bounds = graphNodeBounds(nodes);
  return bounds !== null && graphOverflowsViewport(bounds, viewport);
}
