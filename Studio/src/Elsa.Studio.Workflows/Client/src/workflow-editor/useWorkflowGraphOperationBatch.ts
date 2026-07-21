import { useEffect, useRef } from "react";
import type { ActivityCatalogItem, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import {
  applyWorkflowGraphOperationBatch,
  workflowGraphOperationApplyEvent,
  workflowGraphOperationUndoEvent,
  type WorkflowGraphOperationBatch
} from "../workflowGraphOperationBatch";
import { cloneWorkflowDraftForUndo } from "./editorHelpers";
import type { WorkflowErrorInput } from "./editorTypes";

interface WorkflowGraphOperationBatchParams {
  draft: WorkflowDraft | null;
  details: WorkflowDefinitionDetails | null;
  catalog: ActivityCatalogItem[];
  // Applies a batch result / undo restore to the editor document (resets scope, selection, run, artifact).
  replaceDraftByBatch(draft: WorkflowDraft, selectedNodeId: string | null): void;
  setStatus(value: string): void;
  setError(value: WorkflowErrorInput): void;
}

// Bridges Weaver's window-dispatched graph-operation batches into the editor. Apply mutates the live
// draft (keeping an undo snapshot keyed by token); Undo restores that snapshot. Both funnel through the
// document reducer's batch transition so scope/selection/test-run/artifact reset consistently.
export function useWorkflowGraphOperationBatch({
  draft,
  details,
  catalog,
  replaceDraftByBatch,
  setStatus,
  setError
}: WorkflowGraphOperationBatchParams) {
  const undoSnapshotsRef = useRef(new Map<string, WorkflowDraft>());

  useEffect(() => {
    const handleApply = (event: Event) => {
      const detail = (event as CustomEvent<{
        batch: WorkflowGraphOperationBatch;
        respond(result: { ok: true; result: { appliedCount: number; finalActivityIds: string[]; temporaryReferences: Record<string, string>; summary: string; undoToken: string } } | { ok: false; message: string }): void;
      }>).detail;
      if (!detail?.batch || !detail.respond) return;
      if (!draft || !details) {
        detail.respond({ ok: false, message: "No active workflow draft is open." });
        return;
      }

      const batchWorkflowId = detail.batch.workflowDefinitionId;
      if (batchWorkflowId && batchWorkflowId !== "active-draft" && batchWorkflowId !== details.definition.id) {
        detail.respond({ ok: false, message: `Batch targets workflow '${batchWorkflowId}', but '${details.definition.id}' is active.` });
        return;
      }

      try {
        const previousDraft = cloneWorkflowDraftForUndo(draft);
        const applied = applyWorkflowGraphOperationBatch(draft, detail.batch, catalog);
        const undoToken = `weaver-batch-${Date.now()}`;
        undoSnapshotsRef.current.set(undoToken, previousDraft);
        replaceDraftByBatch(applied.draft, applied.finalActivityIds.at(-1) ?? null);
        setStatus(applied.summary);
        setError("");
        detail.respond({ ok: true, result: { ...applied, undoToken } });
      } catch (e) {
        const message = e instanceof Error ? e.message : String(e);
        setError(message);
        detail.respond({ ok: false, message });
      }
    };

    const handleUndo = (event: Event) => {
      const detail = (event as CustomEvent<{
        undoToken: string;
        respond(result: { ok: true; summary: string } | { ok: false; message: string }): void;
      }>).detail;
      if (!detail?.undoToken || !detail.respond) return;

      const previousDraft = undoSnapshotsRef.current.get(detail.undoToken);
      if (!previousDraft) {
        detail.respond({ ok: false, message: "The Weaver batch undo point is no longer available." });
        return;
      }

      undoSnapshotsRef.current.delete(detail.undoToken);
      replaceDraftByBatch(previousDraft, null);
      setStatus("Restored workflow draft before Weaver batch.");
      setError("");
      detail.respond({ ok: true, summary: "Restored workflow draft before Weaver batch." });
    };

    window.addEventListener(workflowGraphOperationApplyEvent, handleApply);
    window.addEventListener(workflowGraphOperationUndoEvent, handleUndo);
    return () => {
      window.removeEventListener(workflowGraphOperationApplyEvent, handleApply);
      window.removeEventListener(workflowGraphOperationUndoEvent, handleUndo);
    };
  }, [catalog, details, draft, replaceDraftByBatch, setStatus, setError]);
}
