import { MiniMap } from "@xyflow/react";
import { shouldShowReadOnlyMiniMap } from "./readOnlyMinimap";

/**
 * Compact overview for the Executable Inspector and run-detail canvases.
 * Sparse graphs (two nodes or fewer) keep the corner free; larger graphs get a
 * 120×80 token-styled MiniMap (see `.wf-instance-canvas .react-flow__minimap`).
 */
export function WorkflowReadOnlyMiniMap({ nodeCount }: { nodeCount: number }) {
  if (!shouldShowReadOnlyMiniMap(nodeCount)) return null;
  return <MiniMap pannable zoomable />;
}
