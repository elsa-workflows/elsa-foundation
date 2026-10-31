import type { FitViewOptions } from "@xyflow/react";

/** Hide the read-only MiniMap on sparse inspector and run-detail graphs (one or two nodes). */
export const READ_ONLY_MINIMAP_MAX_HIDDEN_NODES = 2;

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
