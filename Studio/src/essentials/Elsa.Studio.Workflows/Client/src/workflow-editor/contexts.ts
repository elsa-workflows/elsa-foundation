import React from "react";
import type { ChildSlot } from "../workflowAdapter";
import type { WorkflowNodeAvailabilityLookup } from "./editorTypes";

export interface WorkflowEdgeActions {
  highlightedEdgeId: string | null;
  deleteEdge(edgeId: string): void;
  requestInsertActivity(edgeId: string, clientX: number, clientY: number): void;
}

export const WorkflowEdgeActionsContext = React.createContext<WorkflowEdgeActions | null>(null);

export const WorkflowNodeAvailabilityContext = React.createContext<WorkflowNodeAvailabilityLookup | null>(null);

// Slot-badge navigation for canvas nodes. A single context handler (instead of a per-node closure in
// node data) keeps node data identity stable across drag re-renders, so container nodes don't
// re-render on every pointer-move while an unrelated node is dragged. A per-node data.onEnterSlot
// (used by the read-only run viewer, whose nodes never re-render from drags) takes precedence.
export type WorkflowSlotNavigation = (ownerNodeId: string, ownerLabel: string, slot: ChildSlot) => void;

export const WorkflowSlotNavigationContext = React.createContext<WorkflowSlotNavigation | null>(null);
