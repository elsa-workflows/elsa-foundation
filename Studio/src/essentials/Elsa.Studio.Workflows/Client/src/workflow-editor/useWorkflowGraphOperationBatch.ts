import { useEffect, useRef } from "react";
import type { StudioAgentClientExecutionResult, StudioAgentPreparedInvocation, StudioAgentToolAvailability } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import type { WorkflowAgentController } from "../workflowAgent";
import {
  applyWorkflowDesignerPosition,
  applyWorkflowOperationBatch,
  classifyWorkflowOperationRisk,
  parseFrozenWorkflowBatch,
  type WorkflowOperationRisk
} from "../workflowGraphOperationBatch";
import { findActivityNode, resolveScopeFrames } from "../workflowAdapter";
import { cloneWorkflowDraftForUndo, getDraftContentRevision } from "./editorHelpers";
import type { WorkflowDocumentApi } from "./workflowDocument";
import type { WorkflowErrorInput } from "./editorTypes";
import type { WorkflowBatchHistory } from "./useDraftHistory";

export const workflowBatchToolName = "workflow_apply_operation_batch";
export const workflowLayoutToolName = "workflow_set_designer_position";
export const workflowBatchResultType = "workflows.batch-result";
const batchUndoPrefix = "workflow-batch-";
/**
 * A change is dispatched only while its observation still has this long to run, so the editor's own commit is observed
 * before the deadline. Should the reducer still commit later, the batch is recorded anyway (see the observation effect).
 */
export const workflowObservationMarginMs = 5000;

type UndoPoint = { before: WorkflowDraft; afterRevision: string; generationId: string; previousReviewHold: boolean };
type Pending = {
  id: string; expectedRevision: string; resolve(value: StudioAgentClientExecutionResult): void; result: StudioAgentClientExecutionResult;
  timeout: ReturnType<typeof setTimeout>; previousReviewHold: boolean;
  /** Present for an Operation Batch: its history entry is recorded once the reducer reports it applied. */
  batch?: { id: string; applied: WorkflowDraft };
};

export function useWorkflowGraphOperationBatch({ draft, details, catalog, document, controller, holdForReview, reviewHeld, canEdit, setStatus, setError, history, resourceType }: {
  draft: WorkflowDraft | null; details: WorkflowDefinitionDetails | null; catalog: ActivityCatalogItem[];
  document: WorkflowDocumentApi; controller?: WorkflowAgentController; holdForReview(held: boolean): boolean; reviewHeld: boolean;
  canEdit?(): boolean; setStatus(value: string): void; setError(value: WorkflowErrorInput): void;
  /** The designer history an Operation Batch is recorded in and undone through; without it a batch is refused. */
  history?: WorkflowBatchHistory;
  /** The working state's own resource type: a template editor's batch targets `workflow-template`, never a definition (FR-024). */
  resourceType?: string;
}) {
  const undoPoints = useRef(new Map<string, UndoPoint>());
  const pending = useRef<Pending | null>(null);
  // Every dispatched batch until the reducer reports it, independent of its observation: a batch the reducer applies is
  // always one history entry with its autosave hold, even if its observation already timed out as uncertain.
  const dispatchedBatches = useRef(new Map<string, { id: string; applied: WorkflowDraft }>());
  const latest = useRef({ draft, details, catalog, document, canEdit, reviewHeld, history, resourceType });
  latest.current = { draft, details, catalog, document, canEdit, reviewHeld, history, resourceType };

  useEffect(() => {
    const transaction = document.state.batchTransaction;
    const dispatched = transaction ? dispatchedBatches.current.get(transaction.id) : undefined;
    if (transaction && dispatched) {
      dispatchedBatches.current.delete(transaction.id);
      // Applied by the reducer: from this moment the batch is one designer-history entry, whatever follows it.
      if (transaction.applied) latest.current.history?.recordBatch(dispatched.id, dispatched.applied);
    }
    const item = pending.current;
    if (!item || !transaction || transaction.id !== item.id) return;
    pending.current = null; clearTimeout(item.timeout);
    if (!transaction.applied) {
      if (!item.batch) holdForReview(item.previousReviewHold);
      item.resolve({ outcome: "refused", summary: item.batch
        ? "The working draft, its selection or its scope changed after the change was reviewed, so nothing was applied. The change is stale; ask Weaver to prepare it again."
        : "The workflow changed before the atomic edit could apply." });
      return;
    }
    if (draft && getDraftContentRevision(draft) === item.expectedRevision) {
      item.resolve(item.result);
      setStatus(item.result.summary); setError("");
    } else item.resolve({ outcome: "uncertain", summary: "The editor could not confirm the dispatched workflow revision." });
  }, [document.state.batchTransaction, draft, holdForReview, setError, setStatus]);

  useEffect(() => { if (!reviewHeld && !pending.current) undoPoints.current.clear(); }, [reviewHeld]);

  useEffect(() => {
    if (!controller) return;
    const observe = (id: string, expectedRevision: string, result: StudioAgentClientExecutionResult, expiresAt: string, previousReviewHold: boolean,
      batch?: Pending["batch"]) => new Promise<StudioAgentClientExecutionResult>(resolve => {
      const timeout = setTimeout(() => {
        if (pending.current?.id !== id) return;
        pending.current = null;
        resolve({ outcome: "uncertain", summary: "The dispatched workflow change has no observed completion receipt." });
      }, Math.max(1, Date.parse(expiresAt) - Date.now()));
      pending.current = { id, expectedRevision, result, resolve, timeout, previousReviewHold, batch };
    });
    const refuse = (summary: string): StudioAgentClientExecutionResult => ({ outcome: "refused", summary });

    // The exact originating binding and target, checked before anything is applied. Anything else is refused as stale
    // with zero change: a change is never rebased onto, or retargeted to, the working state that is open now.
    const bindingProblem = (invocation: StudioAgentPreparedInvocation, signal?: AbortSignal): string | null => {
      const current = latest.current;
      const snapshot = controller.snapshot;
      if (pending.current) return "Another workflow edit is awaiting observation.";
      if (signal?.aborted || current.canEdit?.() === false || !current.draft || !current.details || !snapshot) return "The originating editable workflow is unavailable.";
      if (!invocation.client || snapshot.client?.clientId !== invocation.client.clientId || snapshot.client?.generationId !== invocation.client.generationId)
        return "The originating workflow editor was reloaded, remounted, opened in another tab or its selection changed, so this change is stale.";
      const target = invocation.target;
      if (!target || target.moduleId !== "Elsa.Studio.Workflows" || target.resourceType !== (current.resourceType ?? "workflow-definition") || target.resourceId !== current.details.definition.id
        || target.resourceId !== current.draft.definitionId || target.revision !== getDraftContentRevision(current.draft))
        return "The workflow identity or its working-state revision changed after review, so this change is stale.";
      return null;
    };

    const executeLayout = async (invocation: StudioAgentPreparedInvocation, signal?: AbortSignal): Promise<StudioAgentClientExecutionResult> => {
      const problem = bindingProblem(invocation, signal);
      if (problem) return refuse(problem);
      const current = latest.current;
      const base = current.draft!;
      const target = invocation.target!;
      const args = invocation.arguments as Record<string, unknown> | null;
      if (!args || typeof args.activityId !== "string" || typeof args.x !== "number" || !Number.isFinite(args.x) || typeof args.y !== "number" || !Number.isFinite(args.y)) return refuse("Layout arguments are malformed.");
      let applied;
      try { applied = applyWorkflowDesignerPosition(base, args.activityId, args.x, args.y, current.catalog); }
      catch (error) { return refuse(error instanceof Error ? error.message : String(error)); }
      const afterRevision = getDraftContentRevision(applied.draft);
      if (afterRevision === target.revision) return refuse("The requested position produces no working-state change.");
      if (signal?.aborted || Date.parse(invocation.expiresAt) <= Date.now()) return refuse("The reviewed workflow change expired before dispatch.");
      const undoToken = `workflow-undo-${invocation.invocationId}`;
      const previousReviewHold = holdForReview(true);
      undoPoints.current.set(undoToken, { previousReviewHold, before: cloneWorkflowDraftForUndo(base), afterRevision, generationId: invocation.client!.generationId });
      const result: StudioAgentClientExecutionResult = { outcome: "applied", summary: "Changed the workflow layout in local working state; save or publish explicitly.",
        after: { ...target, revision: afterRevision }, result: { typeId: "workflows.layout-change", schemaVersion: "1", data: { ...args, revision: afterRevision, undoToken, observed: true } } };
      const completion = observe(invocation.invocationId, afterRevision, result, invocation.expiresAt, previousReviewHold);
      const state = current.document.state;
      current.document.compareAndReplaceDraftByBatch(invocation.invocationId, target.resourceId!, target.revision!, state.selectedNodeId, applied.draft, state.selectedNodeId,
        state.frames, state.frames);
      return completion;
    };

    const executeBatch = async (invocation: StudioAgentPreparedInvocation, signal?: AbortSignal): Promise<StudioAgentClientExecutionResult> => {
      const problem = bindingProblem(invocation, signal);
      if (problem) return refuse(problem);
      const current = latest.current;
      const history = current.history;
      if (!history) return refuse("The designer history is unavailable, so the change cannot be applied as one Undo step. Nothing was applied.");
      const target = invocation.target!;
      const base = current.draft!;
      let batch;
      try { batch = parseFrozenWorkflowBatch(invocation.arguments); }
      catch (error) { return refuse(`The batch does not match the strict schema, so nothing was applied. ${error instanceof Error ? error.message : String(error)}`); }
      // Studio rechecks the server's classification against the live working draft (ADR 0003): any disagreement fails
      // closed back into review, with nothing applied.
      const live = classifyWorkflowOperationRisk(base, batch);
      const liveRisk: WorkflowOperationRisk = live.includes("destructive") ? "destructive" : "reviewRequired";
      const reviewed = batch.operations;
      const mismatch = live.findIndex((risk, index) => risk !== reviewed[index].risk);
      if (mismatch >= 0 || batch.risk !== liveRisk || invocation.risk !== liveRisk)
        return refuse(`Studio's recheck against the live working draft classifies ${mismatch >= 0 ? `operation ${mismatch} as ${live[mismatch]}` : `the batch as ${liveRisk}`}, `
          + "which differs from the reviewed risk. Nothing was applied; ask Weaver to prepare the change again so it is reviewed with the right risk.");
      let applied;
      try { applied = applyWorkflowOperationBatch(base, batch, current.catalog); }
      catch (error) { return refuse(`The whole batch was refused and nothing was applied. ${error instanceof Error ? error.message : String(error)}`); }
      const afterRevision = getDraftContentRevision(applied.draft);
      if (afterRevision === target.revision) return refuse("The batch produces no working-state change.");
      if (signal?.aborted || Date.parse(invocation.expiresAt) - Date.now() < workflowObservationMarginMs)
        return refuse("The reviewed workflow change is too close to its deadline to be applied and observed. Nothing was applied; ask Weaver to prepare it again.");

      // FR-008: the selection survives unless the batch removed the selected node; then the inspector moves to the
      // nearest surviving ancestor scope (no node selected), or clears when nothing survives. The scope frame stays when
      // a node with the same id and slot still exists, and otherwise falls back to its nearest existing ancestor.
      const state = current.document.state;
      const catalogByVersion = new Map(current.catalog.map(item => [item.activityVersionId, item]));
      const root = applied.draft.state.rootActivity;
      const selection = state.selectedNodeId && root && findActivityNode(root, state.selectedNodeId, catalogByVersion) ? state.selectedNodeId : null;
      const frames = resolveScopeFrames(root, state.frames, catalogByVersion);
      // A hand edit not yet captured by designer history is part of the base revision: capture it first, from the latest
      // committed draft, so the batch becomes exactly one history entry of its own.
      history.flushFromLatest();
      const batchId = invocation.invocationId;
      const undoToken = `${batchUndoPrefix}${batchId}`;
      const result: StudioAgentClientExecutionResult = {
        outcome: "applied",
        summary: `Applied all ${applied.appliedCount} operations to the unsaved working draft as one Undo step. Save or publish explicitly.`,
        after: { ...target, revision: afterRevision },
        result: { typeId: workflowBatchResultType, schemaVersion: "1", data: {
          observed: true, revision: afterRevision, baseRevision: target.revision, appliedCount: applied.appliedCount, addedNodeIds: applied.addedNodeIds,
          historyEntry: true, undoToken, selection: { before: state.selectedNodeId, after: selection }, clears: ["testRunResult", "publishedArtifactAssociation"]
        } }
      };
      // The receipt answers the originating binding: it is computed now, before the selection change rotates the binding.
      dispatchedBatches.current.set(invocation.invocationId, { id: batchId, applied: applied.draft });
      const completion = observe(invocation.invocationId, afterRevision, result, invocation.expiresAt, false, { id: batchId, applied: applied.draft });
      current.document.compareAndReplaceDraftByBatch(invocation.invocationId, target.resourceId!, target.revision!, state.selectedNodeId, applied.draft, selection,
        state.frames, frames);
      return completion;
    };

    const execute = (invocation: StudioAgentPreparedInvocation, signal?: AbortSignal): Promise<StudioAgentClientExecutionResult> =>
      invocation.toolName === workflowBatchToolName ? executeBatch(invocation, signal)
        : invocation.toolName === workflowLayoutToolName ? executeLayout(invocation, signal)
          : Promise.resolve(refuse("This workflow editor has no client handler for that tool."));

    // Reported before dispatch (spec 1189): a closed, remounted or reloaded editor, or a changed selection, is not the
    // originating target any more, and the operation is refused with zero effect rather than retargeted.
    const targetAvailability = (invocation: StudioAgentPreparedInvocation): StudioAgentToolAvailability => {
      const snapshot = controller.snapshot;
      if (!snapshot || latest.current.canEdit?.() === false) return { status: "unavailable", reason: "The originating workflow editor is unavailable. Nothing was applied." };
      if (!invocation.client || snapshot.client?.clientId !== invocation.client.clientId || snapshot.client?.generationId !== invocation.client.generationId)
        return { status: "unavailable", reason: "The originating workflow editor was reloaded, remounted, opened in another tab or its selection changed. Nothing was applied." };
      return { status: "available" };
    };

    const undo = async (token: string) => {
      const current = latest.current;
      const point = undoPoints.current.get(token);
      if (!point || !current.reviewHeld || !current.draft || !current.details || current.canEdit?.() === false || pending.current
        || controller.snapshot?.client?.generationId !== point.generationId || getDraftContentRevision(current.draft) !== point.afterRevision) throw new Error("The whole-change undo point is stale; no state was replaced.");
      const id = `undo-${token}`;
      const completion = observe(id, getDraftContentRevision(point.before), { outcome: "applied", summary: "Restored the working draft before the whole Weaver change." }, new Date(Date.now() + 15000).toISOString(), true);
      const state = current.document.state;
      current.document.compareAndReplaceDraftByBatch(id, current.details.definition.id, point.afterRevision, state.selectedNodeId, point.before, state.selectedNodeId, state.frames, state.frames);
      const result = await completion;
      if (result.outcome !== "applied") throw new Error(result.summary);
      undoPoints.current.delete(token); holdForReview(point.previousReviewHold); return result.summary;
    };

    // The review card's whole-change Undo for a batch is designer-history Undo of the batch entry, so a later history
    // Undo can never apply the batch twice. It acts only while the batch entry is the current position.
    const batchIdOf = (token: string) => token.startsWith(batchUndoPrefix) ? token.slice(batchUndoPrefix.length) : null;
    const batch: NonNullable<WorkflowAgentController["batch"]> = {
      state(token) {
        const id = batchIdOf(token);
        const current = latest.current;
        if (!id || !current.history) return { status: "unavailable", persisted: null, reason: "The originating workflow editor is unavailable." };
        const state = current.history.undoState(id);
        if (state.status === "available" && (current.canEdit?.() === false || pending.current))
          return { ...state, status: "unavailable", reason: "The designer is read-only or busy right now. Try again in a moment." };
        return state;
      },
      async undo(token) {
        const id = batchIdOf(token);
        const current = latest.current;
        if (!id || !current.history || current.canEdit?.() === false || pending.current || !current.history.undoBatch(id))
          throw new Error(batch.state(token).reason ?? "The whole-change Undo is unavailable; no state was replaced.");
        return "Undid the whole Weaver change through the designer's history. The designer's Redo restores it.";
      },
      subscribe(listener) {
        return latest.current.history?.subscribe(listener) ?? (() => undefined);
      }
    };

    controller.execute = execute; controller.undo = undo; controller.batch = batch; controller.targetAvailability = targetAvailability;
    return () => {
      if (controller.execute === execute) controller.execute = null;
      if (controller.undo === undo) controller.undo = null;
      if (controller.batch === batch) controller.batch = null;
      if (controller.targetAvailability === targetAvailability) controller.targetAvailability = null;
      const item = pending.current;
      if (item) { clearTimeout(item.timeout); pending.current = null; item.resolve({ outcome: "uncertain", summary: "The editor disappeared after dispatch; application cannot be confirmed." }); }
    };
  }, [controller, holdForReview]);
}
