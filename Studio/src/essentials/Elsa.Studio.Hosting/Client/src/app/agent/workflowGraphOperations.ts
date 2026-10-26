import type { WorkflowGraphOperationBatch, WorkflowGraphOperationBatchApplyResult } from "./agentTypes";

export const workflowGraphOperationApplyEvent = "elsa-studio:apply-workflow-graph-operation-batch";
export const workflowGraphOperationUndoEvent = "elsa-studio:undo-workflow-graph-operation-batch";

export interface WorkflowGraphOperationApplyRequest {
  batch: WorkflowGraphOperationBatch;
  respond(result: WorkflowGraphOperationApplyResponse): void;
}

export type WorkflowGraphOperationApplyResponse =
  | { ok: true; result: WorkflowGraphOperationBatchApplyResult }
  | { ok: false; message: string };

export interface WorkflowGraphOperationUndoRequest {
  undoToken: string;
  respond(result: WorkflowGraphOperationUndoResponse): void;
}

export type WorkflowGraphOperationUndoResponse =
  | { ok: true; summary: string }
  | { ok: false; message: string };

export function summarizeWorkflowBatch(batch: WorkflowGraphOperationBatch) {
  const groups = new Map<string, number>();

  for (const operation of batch.operations) {
    const group = getOperationGroup(operation.kind);
    groups.set(group, (groups.get(group) ?? 0) + 1);
  }

  return [...groups.entries()].map(([group, count]) => ({ group, count }));
}

export function canDirectApplyWorkflowBatch(batch: WorkflowGraphOperationBatch, providerRiskProfile: string | undefined, providerOperations: readonly string[] = []) {
  const risk = providerRiskProfile?.toLowerCase();
  const supportsToolApproval = providerOperations.includes("tool-approval");
  const metadataRisk = String(batch.metadata?.risk ?? batch.metadata?.riskProfile ?? "").toLowerCase();
  return metadataRisk === "direct-apply"
    || metadataRisk === "low"
    || risk === "sandboxed-execution"
    || (risk === "review-required" && supportsToolApproval);
}

export function requestWorkflowBatchApply(batch: WorkflowGraphOperationBatch, timeoutMs = 250): Promise<WorkflowGraphOperationApplyResponse> {
  return new Promise(resolve => {
    let settled = false;
    const timeoutId = window.setTimeout(() => {
      if (!settled) {
        settled = true;
        resolve({ ok: false, message: "No active workflow designer accepted the batch." });
      }
    }, timeoutMs);

    window.dispatchEvent(new CustomEvent<WorkflowGraphOperationApplyRequest>(workflowGraphOperationApplyEvent, {
      detail: {
        batch,
        respond(result) {
          if (settled) return;
          settled = true;
          window.clearTimeout(timeoutId);
          resolve(result);
        }
      }
    }));
  });
}

export function requestWorkflowBatchUndo(undoToken: string, timeoutMs = 250): Promise<WorkflowGraphOperationUndoResponse> {
  return new Promise(resolve => {
    let settled = false;
    const timeoutId = window.setTimeout(() => {
      if (!settled) {
        settled = true;
        resolve({ ok: false, message: "No active workflow designer accepted the undo request." });
      }
    }, timeoutMs);

    window.dispatchEvent(new CustomEvent<WorkflowGraphOperationUndoRequest>(workflowGraphOperationUndoEvent, {
      detail: {
        undoToken,
        respond(result) {
          if (settled) return;
          settled = true;
          window.clearTimeout(timeoutId);
          resolve(result);
        }
      }
    }));
  });
}

function getOperationGroup(kind: string) {
  if (kind.includes("add")) return "add";
  if (kind.includes("update")) return "update";
  if (kind.includes("remove")) return "remove";
  if (kind.includes("connect") && !kind.includes("disconnect")) return "connect";
  if (kind.includes("disconnect")) return "disconnect";
  if (kind.includes("root")) return "root";
  if (kind.includes("position") || kind.includes("layout") || kind.includes("designer")) return "layout";
  if (kind.includes("property")) return "property";
  return "other";
}
