import { Controls } from "@xyflow/react";
import { getReadOnlyFitViewOptions, READ_ONLY_CONTROLS_ORIENTATION } from "./readOnlyMinimap";

/**
 * Zoom controls for read-only run and inspection canvases: one row along the bottom-left, so they
 * stay below the graph that the first view and Fit View place above the controls row (#4196).
 * Fit View uses the same padding as the canvas's own auto-fit.
 */
export function ReadOnlyFlowControls({ nodeCount, showInteractive = true }: { nodeCount: number; showInteractive?: boolean }) {
  return <Controls orientation={READ_ONLY_CONTROLS_ORIENTATION} showInteractive={showInteractive} fitViewOptions={getReadOnlyFitViewOptions(nodeCount)} />;
}
