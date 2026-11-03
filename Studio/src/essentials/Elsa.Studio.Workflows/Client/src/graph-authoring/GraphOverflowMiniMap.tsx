import { useRef } from "react";
import { MiniMap, useStore } from "@xyflow/react";
import { shouldShowGraphMinimap } from "./graphMinimap";

/**
 * React Flow minimap that stays off until the graph is larger than the canvas pane.
 * Newcomer graphs (a start node and a couple of activities) keep the corner free.
 */
export function GraphOverflowMiniMap() {
  const shown = useRef(false);
  const visible = useStore(state => shouldShowGraphMinimap(
    state.nodes,
    { width: state.width, height: state.height },
    shown.current
  ));
  shown.current = visible;
  if (!visible) return null;
  return <MiniMap pannable zoomable />;
}
