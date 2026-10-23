import { MiniMap, useNodes, useStore } from "@xyflow/react";
import { shouldShowGraphMinimap } from "./graphMinimap";

/**
 * React Flow minimap that stays off until the graph is larger than the canvas pane.
 * Newcomer graphs (a start node and a couple of activities) keep the corner free.
 */
export function GraphOverflowMiniMap() {
  const nodes = useNodes();
  const width = useStore(state => state.width);
  const height = useStore(state => state.height);
  if (!shouldShowGraphMinimap(nodes, { width, height })) return null;
  return <MiniMap pannable zoomable />;
}
