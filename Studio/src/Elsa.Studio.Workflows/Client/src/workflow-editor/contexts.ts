import React from "react";
import type { WorkflowNodeAvailabilityLookup } from "./editorTypes";

export interface WorkflowEdgeActions {
  highlightedEdgeId: string | null;
  deleteEdge(edgeId: string): void;
  requestInsertActivity(edgeId: string, clientX: number, clientY: number): void;
}

export const WorkflowEdgeActionsContext = React.createContext<WorkflowEdgeActions | null>(null);

export const WorkflowNodeAvailabilityContext = React.createContext<WorkflowNodeAvailabilityLookup | null>(null);
