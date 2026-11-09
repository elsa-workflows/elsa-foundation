import type { ValidationError, WorkflowDraft } from "../workflowTypes";
import { getDraftSignature, readWorkflowErrorPayload } from "./editorHelpers";
import { START_TRIGGER_PLACEMENT_TYPE } from "../validationDiagnostics";

export const triggerPlacementErrorCode = START_TRIGGER_PLACEMENT_TYPE;

/** Only the Publishing owner's typed Placement refusal is authoritative; do not infer it from text. */
export function readTriggerPlacementErrors(error: unknown): ValidationError[] {
  const payload = readWorkflowErrorPayload(error);
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) return [];
  const problem = payload as Record<string, unknown>;
  if (problem.errorCode !== triggerPlacementErrorCode || problem.status !== 400 ||
      typeof problem.triggerNodeId !== "string" || !problem.triggerNodeId.trim() ||
      typeof problem.compositeNodeId !== "string" || !problem.compositeNodeId.trim() ||
      typeof problem.detail !== "string" || !problem.detail.trim()) return [];
  return [{ type: triggerPlacementErrorCode, code: triggerPlacementErrorCode, severity: "Error",
    path: problem.triggerNodeId, message: problem.detail }];
}

export function isCurrentPublicationSnapshot(current: WorkflowDraft | null, snapshot: WorkflowDraft): boolean {
  return current?.id === snapshot.id && getDraftSignature(current) === getDraftSignature(snapshot);
}
