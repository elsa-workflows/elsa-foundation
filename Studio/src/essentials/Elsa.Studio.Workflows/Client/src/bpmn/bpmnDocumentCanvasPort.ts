import type { ActivityNode } from "../workflowTypes";
import type { AuthoringCheckpointToken } from "../graph-authoring/authoringCheckpoint";
import type { ChildSlot } from "../workflowAdapter";
import type { BpmnNodeData } from "./bpmnAdapter";
import type { BpmnDocumentBinding, BpmnDocumentCommand, BpmnDocumentSelection, BpmnDocumentTransaction, BpmnPoint, BpmnScopeAddress } from "./bpmnDocument";

/** Workflows-owned, unenabled source2 view boundary. Resource hosts retain admission/history/checkpoints. */
export interface BpmnDocumentCanvasSnapshot {
  owner: ActivityNode;
  token: AuthoringCheckpointToken;
  scope: BpmnScopeAddress;
}
export interface BpmnDocumentCanvasRequest {
  expected: AuthoringCheckpointToken;
  expectedOwner: ActivityNode;
  scope: BpmnScopeAddress;
  sessionId: string;
  sequence: number;
  label: string;
  transaction: BpmnDocumentTransaction;
}
export type BpmnDocumentCanvasAcknowledgement =
  | { kind: "accepted"; current: BpmnDocumentCanvasSnapshot }
  | { kind: "refused" | "stale"; reason: string; current: BpmnDocumentCanvasSnapshot };
export interface BpmnDocumentNativePresentation {
  activity: NonNullable<BpmnNodeData["boundActivity"]>;
  childSlots: ChildSlot[];
}
export interface BpmnDocumentCanvasViewProps {
  snapshot: BpmnDocumentCanvasSnapshot;
  selection: BpmnDocumentSelection | null;
  readOnly: boolean;
  className?: string;
  onSelect(selection: BpmnDocumentSelection | null): void;
  /** Must retain the request independently of view lifetime and acknowledge only canonical admission. */
  onTransaction(request: BpmnDocumentCanvasRequest): BpmnDocumentCanvasAcknowledgement | Promise<BpmnDocumentCanvasAcknowledgement>;
  /** Participant/fence registration belongs to the host; this is observable view status only. */
  onStateChange?(state: { pending: boolean; failed: boolean; message: string | null }): void;
  onHistory?(direction: "undo" | "redo"): void;
  resolveNativePresentation?(elementId: string, binding: BpmnDocumentBinding): BpmnDocumentNativePresentation | null;
  /** Host resolves a real DI plane, including independent child planes; no fictitious child canvas. */
  resolveStandardScope?(elementId: string, current: BpmnScopeAddress): BpmnScopeAddress | null;
  onNavigateStandard?(scope: BpmnScopeAddress): void | Promise<void>;
  onOpenNativeSlot?(ownerNodeId: string, slot: ChildSlot): void | Promise<void>;
  /** Retained resource controller observes each preview synchronously, before pointerup or React state commit. */
  editing?: {
    generation: number;
    fenced: boolean;
    begin(source: BpmnDocumentCanvasSnapshot): void;
    preview(source: BpmnDocumentCanvasSnapshot, commands: readonly BpmnDocumentCommand[], label: string): void;
  };
  /** Palette placement uses React Flow's actual viewport transform. */
  placing?: boolean;
  onPlace?(position: BpmnPoint, hitElementId?: string): void;
}
