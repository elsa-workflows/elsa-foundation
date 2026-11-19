import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React from "react";
import { Check, Copy } from "lucide-react";
import type { WorkflowExecutableReference } from "../workflowTypes";
import { formatDate } from "../workflowFormatting";
import type { ExecutableRunState } from "./editorTypes";
import { copyTextToClipboard, formatReferenceScope, getExecutableReferenceStatus } from "./editorHelpers";

export function ExecutableRunStatusLine({ status, run, compact = false }: { status: string; run: ExecutableRunState | null; compact?: boolean }) {
  const openRun = () => {
    if (!run?.workflowExecutionId) return;
    window.history.pushState({}, "", qualifyStudioBackendPath(`/workflows/instances/${encodeURIComponent(run.workflowExecutionId)}`));
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

export function CopyValueButton({ value, ariaLabel, copiedLabel, textLabel, onCopied, onCopyFailed }: { value: string | null | undefined; ariaLabel: string; copiedLabel: string; textLabel?: string; onCopied(label: string): void; onCopyFailed(label: string): void }) {
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
      {textLabel ? <span>{textLabel}</span> : null}
    </button>
  );
}

// The Source Reference list shared by the executables table (row expansion) and the Executable
// Inspector: per reference the artifact version label, publish time, scope, expiry, and lifecycle
// status. `activeReferenceId`/`onSelect` are Inspector-only (re-render from another reference's
// Layout Sidecar); the table renders it read-only.
export function ExecutableReferenceList({ references, activeReferenceId, onSelect, definitionNames, ariaLabel = "Source references", variant = "compact" }: {
  references: WorkflowExecutableReference[];
  activeReferenceId?: string | null;
  definitionNames?: ReadonlyMap<string, string>;
  onSelect?(reference: WorkflowExecutableReference): void;
  ariaLabel?: string;
  variant?: "compact" | "collection";
}) {
  if (references.length === 0) return <p className="wf-muted">No source references.</p>;

  return (
    <ul className={["wf-reference-list", variant === "collection" ? "wf-reference-list--collection" : ""].filter(Boolean).join(" ")} aria-label={ariaLabel}>
      {references.map(reference => {
        const status = getExecutableReferenceStatus(reference);
        const active = reference.sourceReferenceId === activeReferenceId;
        const displayName = (reference.definitionId && definitionNames?.get(reference.definitionId)) || reference.definitionId || "Untitled workflow";
        const datedAt = reference.publishedAt || reference.createdAt;
        const selectLabel = `Show ${displayName} version ${reference.artifactVersion}`;
        const body = variant === "collection" ? (
          <>
            <strong className="wf-reference-origin">{displayName}</strong>
            <span className="wf-reference-collection">
              <span>
                <span className="wf-reference-collection-label">Version</span>
                <span className="wf-identity-cell">{reference.artifactVersion}</span>
              </span>
              <span>
                <span className="wf-reference-collection-label">Scope</span>
                <span className="wf-identity-cell">
                  <span className={`wf-chip wf-reference-scope-${reference.scope.toLowerCase()}`}>{formatReferenceScope(reference.scope)}</span>
                  {status !== "live" ? <span className="wf-chip wf-reference-retired">{status === "retired" ? "Retired" : "Expired"}</span> : null}
                  {active ? <span className="wf-chip">Shown</span> : null}
                </span>
              </span>
              {datedAt ? (
                <span>
                  <span className="wf-reference-collection-label">{reference.publishedAt ? "Published" : "Created"}</span>
                  <span className="wf-identity-cell">{formatDate(datedAt)}</span>
                </span>
              ) : null}
              {reference.expiresAt ? (
                <span>
                  <span className="wf-reference-collection-label">Expires</span>
                  <span className="wf-identity-cell">{formatDate(reference.expiresAt)}</span>
                </span>
              ) : null}
              {reference.deletedReason ? (
                <span>
                  <span className="wf-reference-collection-label">Reason</span>
                  <span className="wf-identity-cell">{reference.deletedReason}</span>
                </span>
              ) : null}
            </span>
          </>
        ) : (
          <>
            {definitionNames && reference.definitionId ? <strong className="wf-reference-origin">{definitionNames.get(reference.definitionId) ?? reference.definitionId}</strong> : null}
            <span className="wf-reference-heading">
              <strong>Version {reference.artifactVersion}</strong>
              <span className={`wf-chip wf-reference-scope-${reference.scope.toLowerCase()}`}>{formatReferenceScope(reference.scope)}</span>
              {status !== "live" ? <span className="wf-chip wf-reference-retired">{status === "retired" ? "Retired" : "Expired"}</span> : null}
              {active ? <span className="wf-chip">Shown</span> : null}
            </span>
            <span className="wf-reference-meta">
              {definitionNames && definitionNames.has(reference.definitionId) ? <small>Definition <code>{reference.definitionId}</code></small> : null}
              {definitionNames ? <small title={reference.sourceReferenceId}>Reference <code>{reference.sourceReferenceId}</code></small> : null}
              <small>{reference.publishedAt ? `Published ${formatDate(reference.publishedAt)}` : `Created ${formatDate(reference.createdAt)}`}</small>
              {reference.expiresAt ? <small>Expires {formatDate(reference.expiresAt)}</small> : null}
              {reference.deletedReason ? <small>{reference.deletedReason}</small> : null}
            </span>
          </>
        );

        return (
          <li key={reference.sourceReferenceId} className="wf-reference-item" data-active={active ? "true" : undefined}>
            {onSelect ? (
              <button type="button" className="wf-reference-select" aria-label={selectLabel} onClick={() => onSelect(reference)}>
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
