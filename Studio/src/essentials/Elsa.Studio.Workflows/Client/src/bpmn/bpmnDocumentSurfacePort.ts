import type { ReactNode } from "react";
import type { ActivityNode, DesignMetadataRecord } from "../workflowTypes";
import type { AuthoringCheckpointCoordinator } from "../graph-authoring/authoringCheckpoint";
import type { BpmnDocumentSelection, BpmnScopeAddress } from "./bpmnDocument";
import type { BpmnDocumentSessionSource, createBpmnDocumentSessionHost } from "./bpmnDocumentSessionHost";
import type { BpmnDocumentSyntaxPlan } from "./bpmnDocumentSyntaxChange";
import type { WorkflowCanvasState } from "../graph-authoring/canvasEditorContract";

export interface BpmnDocumentCanonicalSource extends BpmnDocumentSessionSource { readonly layout: DesignMetadataRecord[] }
/** A detached projection, explicitly paired with the v1 authority; never persisted merely by mounting it. */
export interface BpmnDocumentPreparedSource {
  readonly canonicalBaseOwner: ActivityNode;
  readonly canonicalBaseLayout: DesignMetadataRecord[];
  readonly preparedOwner: ActivityNode;
}
export interface BpmnDocumentSurfaceHost {
  readonly id: string;
  readonly authorityKey: string;
  readonly coordinator: Pick<AuthoringCheckpointCoordinator<unknown>, "register" | "probe">;
  readCanonical(): BpmnDocumentCanonicalSource;
  canEdit(): boolean;
  /** Ordinary parent history/CAS and side-table cleanup; first edit also verifies the exact legacy pair. */
  admit(expected: BpmnDocumentCanonicalSource, owner: ActivityNode, plan: BpmnDocumentSyntaxPlan,
    context: { preparation: BpmnDocumentPreparedSource | null; nextScope?: BpmnScopeAddress }): boolean;
  /** Parent completes participants, invalidates its monotonic scope epoch, then invokes the action in its fence. */
  transitionScope(action: () => undefined): Promise<void>;
  notifyWorkChanged(): void;
}

export type BpmnDocumentCanvasSession = ReturnType<typeof createBpmnDocumentSessionHost>;
export type BpmnDocumentCanvasParticipant = Parameters<typeof createBpmnDocumentSessionHost>[0]["participant"];
/** Optional native renderers receive Workflows' protocol in SDK's unknown context. No BPMN types enter SDK. */
export interface BpmnDocumentCanvasEditorContext {
  protocol: { id: "elsa.workflows.bpmn-document-canvas"; version: 1 };
  resourceKind: "workflow-definition" | "activity-definition-graph";
  documentId: string;
  scope: BpmnScopeAddress;
  selection: BpmnDocumentSelection | null;
  owner: ActivityNode;
  preparation: BpmnDocumentPreparedSource | null;
  ready: boolean;
  readOnly: boolean;
  /** Cached cross-editor draft/fence facts; do not rely on a delayed read-only render for admission. */
  canMutate?(): boolean;
  /** Canonical v1 while preparation is unavailable; a clean participant may acknowledge it without a Modeler import. */
  readLive(): BpmnDocumentSessionSource;
  createSessionHost(id: string, participant: BpmnDocumentCanvasParticipant): BpmnDocumentCanvasSession;
  /** Resource-local retention above a fallible canvas view; exact authority and scope own this store. */
  retainCanvasController<T>(id: string, create: () => T): T;
  onSelect(selection: BpmnDocumentSelection | null): void;
  onNavigateScope(scope: BpmnScopeAddress): void | Promise<void>;
  resolveStandardScope(elementId: string): BpmnScopeAddress | null;
  onUndo(): void;
  onRedo(): void;
  onStateChange(state: WorkflowCanvasState): void;
}
export interface BpmnDocumentSurfaceSlots {
  palette: ReactNode;
  canvas: ReactNode;
  inspector: ReactNode;
  controlsBlocked: boolean;
}
