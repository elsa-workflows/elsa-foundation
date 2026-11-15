import type { ScopeFrame } from "../workflowAdapter";

/** Ephemeral designer position. This is history metadata, never resource or runtime state. */
export interface GraphAuthoringView {
  frames: ScopeFrame[];
  selectedNodeIds: string[];
  primaryNodeId: string | null;
}
