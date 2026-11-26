import { MiniMap } from "@xyflow/react";
import {
  READ_ONLY_MINIMAP_HEIGHT,
  READ_ONLY_MINIMAP_WIDTH,
  readOnlyMinimapMaskColor,
  readOnlyMinimapMaskStrokeColor,
  readOnlyMinimapNodeColor,
  readOnlyMinimapNodeStrokeColor,
  shouldShowReadOnlyMiniMap
} from "./readOnlyMinimap";

/**
 * Compact overview for the Executable Inspector and run-detail canvases.
 * Sparse graphs (two nodes or fewer) keep the corner free; larger graphs get a
 * 120×80 token-styled MiniMap (see `.wf-instance-canvas .react-flow__minimap`).
 * Size is passed as MiniMap `style` so xyflow scales the SVG and pan/zoom math
 * instead of cropping a 200×150 overview. Token colors paint node rects and the
 * viewport mask so the overview is readable on porcelain and minimal-dark.
 */
export function WorkflowReadOnlyMiniMap({ nodeCount }: { nodeCount: number }) {
  if (!shouldShowReadOnlyMiniMap(nodeCount)) return null;
  return (
    <MiniMap
      pannable
      zoomable
      style={{ width: READ_ONLY_MINIMAP_WIDTH, height: READ_ONLY_MINIMAP_HEIGHT }}
      nodeColor={readOnlyMinimapNodeColor}
      nodeStrokeColor={readOnlyMinimapNodeStrokeColor}
      nodeStrokeWidth={2}
      maskColor={readOnlyMinimapMaskColor}
      maskStrokeColor={readOnlyMinimapMaskStrokeColor}
      maskStrokeWidth={1}
    />
  );
}
