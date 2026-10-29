import { useEffect, useRef } from "react";
import type { StudioAgentClientExecutionResult, StudioAgentPreparedInvocation } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import type { WorkflowAgentController } from "../workflowAgent";
import { applyWorkflowGraphOperationBatch } from "../workflowGraphOperationBatch";
import { cloneWorkflowDraftForUndo, getDraftRevision } from "./editorHelpers";
import type { WorkflowDocumentApi } from "./workflowDocument";
import type { WorkflowErrorInput } from "./editorTypes";

type UndoPoint = { before: WorkflowDraft; afterRevision: string; generationId: string; previousReviewHold: boolean };
type Pending = { id: string; expectedRevision: string; resolve(value: StudioAgentClientExecutionResult): void; result: StudioAgentClientExecutionResult; timeout: ReturnType<typeof setTimeout>; previousReviewHold: boolean };

export function useWorkflowGraphOperationBatch({ draft, details, catalog, document, controller, holdForReview, reviewHeld, canEdit, setStatus, setError }: {
  draft: WorkflowDraft | null; details: WorkflowDefinitionDetails | null; catalog: ActivityCatalogItem[];
  document: WorkflowDocumentApi; controller?: WorkflowAgentController; holdForReview(held: boolean): boolean; reviewHeld: boolean;
  canEdit?(): boolean; setStatus(value: string): void; setError(value: WorkflowErrorInput): void;
}) {
  const undoPoints = useRef(new Map<string, UndoPoint>());
  const pending = useRef<Pending | null>(null);
  const latest = useRef({ draft, details, catalog, document, canEdit });
  latest.current = { draft, details, catalog, document, canEdit };

  useEffect(() => {
    const item = pending.current;
    if (!item || document.state.batchTransaction?.id !== item.id) return;
    pending.current = null; clearTimeout(item.timeout);
    if (!document.state.batchTransaction.applied) {
      holdForReview(item.previousReviewHold);
      item.resolve({ outcome: "refused", summary: "The workflow changed before the atomic edit could apply." });
    } else if (draft && getDraftRevision(draft) === item.expectedRevision) {
      item.resolve(item.result);
      setStatus(item.result.summary); setError("");
    } else item.resolve({ outcome: "uncertain", summary: "The editor could not confirm the dispatched workflow revision." });
  }, [document.state.batchTransaction, draft, holdForReview, setError, setStatus]);

  useEffect(() => { if (!reviewHeld && !pending.current) undoPoints.current.clear(); }, [reviewHeld]);

  useEffect(() => {
    if (!controller) return;
    const observe = (id: string, expectedRevision: string, result: StudioAgentClientExecutionResult, expiresAt: string, previousReviewHold: boolean) => new Promise<StudioAgentClientExecutionResult>(resolve => {
      const timeout = setTimeout(() => {
        if (pending.current?.id !== id) return;
        pending.current = null;
        resolve({ outcome: "uncertain", summary: "The dispatched workflow change has no observed completion receipt." });
      }, Math.max(1, Date.parse(expiresAt) - Date.now()));
      pending.current = { id, expectedRevision, result, resolve, timeout, previousReviewHold };
    });
    const execute = async (invocation: StudioAgentPreparedInvocation, signal?: AbortSignal): Promise<StudioAgentClientExecutionResult> => {
      const current = latest.current;
      const snapshot = controller.snapshot;
      const refuse = (summary: string): StudioAgentClientExecutionResult => ({ outcome: "refused", summary });
      if (pending.current) return refuse("Another workflow edit is awaiting observation.");
      if (signal?.aborted || current.canEdit?.() === false || !current.draft || !current.details || !snapshot) return refuse("The originating editable workflow is unavailable.");
      if (!invocation.client || snapshot.client?.clientId !== invocation.client.clientId || snapshot.client?.generationId !== invocation.client.generationId) return refuse("The originating workflow selection changed.");
      const target = invocation.target;
      if (!target || target.moduleId !== "Elsa.Studio.Workflows" || target.resourceType !== "workflow-definition" || target.resourceId !== current.details.definition.id || target.resourceId !== current.draft.definitionId || target.revision !== getDraftRevision(current.draft)) return refuse("The workflow identity or draft revision changed.");
      const args = invocation.arguments as Record<string, unknown> | null;
      if (!args || typeof args.activityId !== "string" || typeof args.x !== "number" || !Number.isFinite(args.x) || typeof args.y !== "number" || !Number.isFinite(args.y)) return refuse("Layout arguments are malformed.");
      let applied;
      try {
        applied = applyWorkflowGraphOperationBatch(current.draft, { schemaVersion: "1", workflowDefinitionId: target.resourceId, baseRevision: target.revision,
          operations: [{ id: invocation.invocationId, kind: "set-designer-position", parameters: { activityId: args.activityId, x: args.x, y: args.y } }] }, current.catalog);
      } catch (error) { return refuse(error instanceof Error ? error.message : String(error)); }
      const afterRevision = getDraftRevision(applied.draft);
      if (afterRevision === target.revision) return refuse("The requested position produces no working-state change.");
      if (signal?.aborted || Date.parse(invocation.expiresAt) <= Date.now()) return refuse("The reviewed workflow change expired before dispatch.");
      const undoToken = `workflow-undo-${invocation.invocationId}`;
      const previousReviewHold = holdForReview(true);
      undoPoints.current.set(undoToken, { previousReviewHold, before: cloneWorkflowDraftForUndo(current.draft), afterRevision, generationId: invocation.client.generationId });
      const result: StudioAgentClientExecutionResult = { outcome: "applied", summary: "Changed the workflow layout in local working state; save or publish explicitly.",
        after: { ...target, revision: afterRevision }, result: { typeId: "workflows.layout-change", schemaVersion: "1", data: { ...args, revision: afterRevision, undoToken, observed: true } } };
      const completion = observe(invocation.invocationId, afterRevision, result, invocation.expiresAt, previousReviewHold);
      current.document.compareAndReplaceDraftByBatch(invocation.invocationId, target.resourceId, target.revision!, current.document.state.selectedNodeId, applied.draft, current.document.state.selectedNodeId);
      return completion;
    };
    const undo = async (token: string) => {
      const current = latest.current;
      const point = undoPoints.current.get(token);
      if (!point || !current.draft || !current.details || current.canEdit?.() === false || pending.current
        || controller.snapshot?.client?.generationId !== point.generationId || getDraftRevision(current.draft) !== point.afterRevision) throw new Error("The whole-change undo point is stale; no state was replaced.");
      const id = `undo-${token}`;
      const completion = observe(id, getDraftRevision(point.before), { outcome: "applied", summary: "Restored the working draft before the whole Weaver change." }, new Date(Date.now() + 15000).toISOString(), true);
      current.document.compareAndReplaceDraftByBatch(id, current.details.definition.id, point.afterRevision, current.document.state.selectedNodeId, point.before, current.document.state.selectedNodeId);
      const result = await completion;
      if (result.outcome !== "applied") throw new Error(result.summary);
      undoPoints.current.delete(token); holdForReview(point.previousReviewHold); return result.summary;
    };
    controller.execute = execute; controller.undo = undo;
    return () => {
      if (controller.execute === execute) controller.execute = null;
      if (controller.undo === undo) controller.undo = null;
      const item = pending.current;
      if (item) { clearTimeout(item.timeout); pending.current = null; item.resolve({ outcome: "uncertain", summary: "The editor disappeared after dispatch; application cannot be confirmed." }); }
    };
  }, [controller, holdForReview]);
}
