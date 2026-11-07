import type { StudioGraphAuthoringResourceKind } from "@elsa-workflows/studio-sdk";
import type { ActivityNode, DesignMetadataRecord } from "../workflowTypes";

/** Workflows owns the protocol; hosts retain their canonical document and history. */
export interface WorkflowCanvasChange {
  owner: ActivityNode;
  layout: DesignMetadataRecord[];
  baseOwner: ActivityNode;
  baseLayout: DesignMetadataRecord[];
}

export interface WorkflowCanvasState {
  status: "loading" | "ready" | "refused" | "failed";
  message?: string;
  /** Cached local work may exist during an active gesture even while the visible canvas is ready. */
  pending?: boolean;
}

export interface WorkflowCanvasEditorContext {
  protocol: { id: "elsa.workflows.controlled-canvas"; version: 1 };
  resourceKind: StudioGraphAuthoringResourceKind;
  documentId: string;
  scopeId: string;
  owner: ActivityNode;
  layout: DesignMetadataRecord[];
  selectedElementId: string | null;
  readOnly: boolean;
  /** Synchronous revision-checked acknowledgement; false restores the current canonical document. */
  onChange(change: WorkflowCanvasChange): boolean;
  onSelect(elementId: string | null): void;
  onUndo(): void;
  onRedo(): void;
  onStateChange(state: WorkflowCanvasState): void;
}
