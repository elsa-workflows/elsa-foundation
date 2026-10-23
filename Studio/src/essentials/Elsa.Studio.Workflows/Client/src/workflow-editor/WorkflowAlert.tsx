import { AlertCircle, X } from "lucide-react";
import type { WorkflowEditorError } from "./editorTypes";
import { CopyValueButton } from "./executableShared";

// The editor's surface-scoped error banner. Beyond the headline message it surfaces the ProblemDetails
// `detail` (when it adds information beyond the message), a structured error `code`, the HTTP `status`,
// and a copyable `traceId` so a failure can be correlated with server logs — and it can be dismissed.
export function WorkflowAlert({ error, onDismiss, onCopied, onCopyFailed }: {
  error: WorkflowEditorError;
  onDismiss(): void;
  onCopied(label: string): void;
  onCopyFailed(label: string): void;
}) {
  const detail = error.detail && error.detail !== error.message ? error.detail : null;
  const showStatus = typeof error.status === "number" && error.status > 0;

  return (
    <div className="wf-alert wf-alert-detailed" role="alert">
      <AlertCircle size={16} className="wf-alert-icon" aria-hidden="true" />
      <div className="wf-alert-body">
        <p className="wf-alert-message">{error.message}</p>
        {detail ? <p className="wf-alert-detail">{detail}</p> : null}
        {(showStatus || error.code || error.traceId) ? (
          <div className="wf-alert-meta">
            {error.code ? <span className="wf-alert-code">{error.code}</span> : null}
            {showStatus ? <span className="wf-alert-status">HTTP {error.status}</span> : null}
            {error.traceId ? (
              <span className="wf-alert-trace">
                <span className="wf-alert-trace-label">Trace</span>
                <code>{error.traceId}</code>
                <CopyValueButton
                  value={error.traceId}
                  ariaLabel="Copy trace id"
                  copiedLabel="Trace id"
                  onCopied={onCopied}
                  onCopyFailed={onCopyFailed}
                />
              </span>
            ) : null}
          </div>
        ) : null}
      </div>
      <button type="button" className="wf-alert-dismiss" aria-label="Dismiss error" title="Dismiss" onClick={onDismiss}>
        <X size={14} aria-hidden="true" />
      </button>
    </div>
  );
}
