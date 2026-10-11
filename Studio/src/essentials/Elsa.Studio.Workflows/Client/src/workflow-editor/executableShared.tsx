import React from "react";
import { Check, Copy } from "lucide-react";
import type { WorkflowExecutableReference } from "../workflowTypes";
import { formatDate } from "../workflowFormatting";
import type { ExecutableRunState } from "./editorTypes";
import { copyTextToClipboard, formatReferenceScope, getExecutableReferenceStatus } from "./editorHelpers";

export function ExecutableRunStatusLine({ status, run, compact = false }: { status: string; run: ExecutableRunState | null; compact?: boolean }) {
  const openRun = () => {
    if (!run?.workflowExecutionId) return;
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(run.workflowExecutionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return (
    <div className={`wf-status-line${compact ? " compact" : ""}`}>
      <Check size={compact ? 13 : 14} />
      <span>{status}</span>
      {run?.workflowExecutionId ? (
        <button type="button" onClick={openRun}>Open Run {run.workflowExecutionId}</button>
      ) : null}
    </div>
  );
}

export function CopyValueButton({ value, ariaLabel, copiedLabel, onCopied, onCopyFailed }: { value: string | null | undefined; ariaLabel: string; copiedLabel: string; onCopied(label: string): void; onCopyFailed(label: string): void }) {
  if (!value) return null;

  const copy = async (event: React.MouseEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();
    try {
      await copyTextToClipboard(value);
      onCopied(copiedLabel);
    } catch {
      onCopyFailed(copiedLabel);
    }
  };

  return (
    <button type="button" className="wf-copy-button" aria-label={ariaLabel} title={ariaLabel} onClick={event => void copy(event)}>
      <Copy size={12} />
    </button>
  );
}

// The Source Reference list shared by the executables table (row expansion) and the Executable
// Inspector: per reference the artifact version label, publish time, scope, expiry, and lifecycle
// status. `activeReferenceId`/`onSelect` are Inspector-only (re-render from another reference's
// Layout Sidecar); the table renders it read-only.
export function ExecutableReferenceList({ references, activeReferenceId, onSelect, ariaLabel = "Source references" }: {
  references: WorkflowExecutableReference[];
  activeReferenceId?: string | null;
  onSelect?(reference: WorkflowExecutableReference): void;
  ariaLabel?: string;
}) {
  if (references.length === 0) return <p className="wf-muted">No source references.</p>;

  return (
    <ul className="wf-reference-list" aria-label={ariaLabel}>
      {references.map(reference => {
        const status = getExecutableReferenceStatus(reference);
        const active = reference.sourceReferenceId === activeReferenceId;
        const body = (
          <>
            <span className="wf-reference-heading">
              <strong>Version {reference.artifactVersion}</strong>
              <span className={`wf-chip wf-reference-scope-${reference.scope.toLowerCase()}`}>{formatReferenceScope(reference.scope)}</span>
              {status !== "live" ? <span className="wf-chip wf-reference-retired">{status === "retired" ? "Retired" : "Expired"}</span> : null}
              {active ? <span className="wf-chip">Shown</span> : null}
            </span>
            <span className="wf-reference-meta">
              <small>{reference.publishedAt ? `Published ${formatDate(reference.publishedAt)}` : `Created ${formatDate(reference.createdAt)}`}</small>
              {reference.expiresAt ? <small>Expires {formatDate(reference.expiresAt)}</small> : null}
              {reference.deletedReason ? <small>{reference.deletedReason}</small> : null}
            </span>
          </>
        );

        return (
          <li key={reference.sourceReferenceId} className="wf-reference-item" data-active={active ? "true" : undefined}>
            {onSelect ? (
              <button type="button" className="wf-reference-select" aria-label={`Show reference ${reference.sourceReferenceId}`} onClick={() => onSelect(reference)}>
                {body}
              </button>
            ) : (
              <span className="wf-reference-select">{body}</span>
            )}
          </li>
        );
      })}
    </ul>
  );
}
