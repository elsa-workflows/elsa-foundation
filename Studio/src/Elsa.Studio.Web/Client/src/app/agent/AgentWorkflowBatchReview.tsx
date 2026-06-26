import React from "react";
import { Check, RotateCcw } from "lucide-react";
import type { WorkflowGraphOperationBatch, WorkflowGraphOperationBatchApplyResult } from "./agentTypes";
import { summarizeWorkflowBatch } from "./workflowGraphOperations";

export interface WorkflowBatchReviewModel {
  id: string;
  batch: WorkflowGraphOperationBatch;
  status: "ready" | "applying" | "applied" | "failed" | "undone";
  canApply: boolean;
  disabledReason?: string;
  result?: WorkflowGraphOperationBatchApplyResult;
  error?: string;
}

export function AgentWorkflowBatchReview({
  batches,
  disabled,
  onApply,
  onUndo
}: {
  batches: WorkflowBatchReviewModel[];
  disabled?: boolean;
  onApply(batch: WorkflowBatchReviewModel): void;
  onUndo(batch: WorkflowBatchReviewModel): void;
}) {
  if (batches.length === 0) return null;

  return (
    <section className="agent-workflow-batches" aria-label="Workflow graph operation batches">
      {batches.map(item => {
        const summary = summarizeWorkflowBatch(item.batch);
        const pending = item.status === "applying";
        return (
          <article
            key={item.id}
            className="agent-workflow-batch"
            data-testid="weaver-workflow-graph-operation-batch"
            data-status={item.status}
            data-can-apply={item.canApply}
          >
            <header>
              <span>Workflow batch</span>
              <strong>{item.batch.operations.length} operation{item.batch.operations.length === 1 ? "" : "s"}</strong>
            </header>
            <dl>
              <div><dt>Schema</dt><dd>{item.batch.schemaVersion}</dd></div>
              <div><dt>Workflow</dt><dd>{item.batch.workflowDefinitionId}</dd></div>
              {item.batch.baseRevision ? <div><dt>Revision</dt><dd>{item.batch.baseRevision}</dd></div> : null}
            </dl>
            <div className="agent-workflow-operation-groups" aria-label="Operation summary">
              {summary.map(group => (
                <span key={group.group}>{group.group} <strong>{group.count}</strong></span>
              ))}
            </div>
            {item.result ? (
              <div className="agent-workflow-apply-result" data-testid="weaver-workflow-apply-summary">
                <span>{item.result.summary}</span>
                {Object.keys(item.result.temporaryReferences).length > 0 ? (
                  <ul>
                    {Object.entries(item.result.temporaryReferences).map(([temporaryId, finalId]) => (
                      <li key={temporaryId}><code>{temporaryId}</code> <span>{finalId}</span></li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : null}
            {item.error ? <small className="agent-workflow-batch-error">{item.error}</small> : null}
            {!item.canApply && item.status === "ready" ? <small>{item.disabledReason ?? "Review is required before this batch can be applied."}</small> : null}
            <footer>
              <button
                type="button"
                disabled={disabled || pending || !item.canApply || item.status === "applied"}
                onClick={() => onApply(item)}
                data-testid="weaver-apply-workflow-batch"
              >
                <Check size={14} /> Apply
              </button>
              <button
                type="button"
                disabled={disabled || pending || item.status !== "applied" || !item.result?.undoToken}
                onClick={() => onUndo(item)}
                data-testid="weaver-undo-workflow-batch"
              >
                <RotateCcw size={14} /> Undo
              </button>
            </footer>
          </article>
        );
      })}
    </section>
  );
}
