import React from "react";
import { Check, Copy } from "lucide-react";
import type { ExecutableRunState } from "./editorTypes";
import { copyTextToClipboard } from "./editorHelpers";

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
