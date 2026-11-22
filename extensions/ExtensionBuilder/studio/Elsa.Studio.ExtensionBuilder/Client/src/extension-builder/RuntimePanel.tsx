import { RefreshCcw } from "lucide-react";
import { StatusChip, StudioAlert } from "@elsa-workflows/studio-ui";
import type { ExtensionBuilderCapabilities, ExtensionRuntimeStatus } from "../extensionBuilderApi";
import { diagnosticTone, runtimeMessage, runtimeMessageTone, runtimeTone } from "./helpers";
import { RuntimeVersionRow } from "./RuntimeVersionRow";

export function RuntimePanel({
  capabilities,
  runtimeStatus,
  busy,
  onRefreshRuntime,
  onRetryReconciliation,
  onRollback
}: {
  capabilities: ExtensionBuilderCapabilities;
  runtimeStatus: ExtensionRuntimeStatus | null;
  busy: boolean;
  onRefreshRuntime(): void;
  onRetryReconciliation(): void;
  onRollback(version: string): void;
}) {
  return (
    <div className="extension-builder-inspector-section">
      <div className="extension-builder-runtime-heading">
        <h4>Runtime status</h4>
        <button type="button" className="studio-button" disabled={busy} onClick={onRefreshRuntime}>
          <RefreshCcw size={15} />
          Refresh
        </button>
      </div>
      {!runtimeStatus ? <p className="extension-builder-muted">Runtime status is not available for this project yet.</p> : null}
      {runtimeStatus ? (
        <>
          <dl className="extension-builder-metadata">
            <div><dt>Package</dt><dd>{runtimeStatus.packageId}</dd></div>
            <div><dt>Version</dt><dd>{runtimeStatus.version ?? "n/a"}</dd></div>
            <div><dt>State</dt><dd><StatusChip tone={runtimeTone(runtimeStatus.state)}>{runtimeStatus.state}</StatusChip></dd></div>
            <div><dt>Features</dt><dd>{runtimeStatus.features.length}</dd></div>
          </dl>
          <StudioAlert tone={runtimeMessageTone(runtimeStatus.state)}>{runtimeMessage(runtimeStatus)}</StudioAlert>
          <h4>Contributions</h4>
          <div className="extension-builder-contribution-list">
            {runtimeStatus.features.length === 0 ? <p className="extension-builder-muted">This package contributed no runtime capabilities or activities.</p> : null}
            {runtimeStatus.features.map(feature => (
              <div key={feature.id} className="extension-builder-contribution-row">
                <span>{feature.label}</span>
                <code>{feature.type ?? feature.id}</code>
              </div>
            ))}
          </div>
          <h4>Recovery</h4>
          <div className="extension-builder-operation-grid">
            <button type="button" className="studio-button" disabled={busy || runtimeStatus.state !== "FailedReconciliation"} onClick={onRetryReconciliation}>
              <RefreshCcw size={15} />
              Retry reconciliation
            </button>
          </div>
          <h4>Version history</h4>
          <div className="extension-builder-list">
            {runtimeStatus.history.length === 0 ? <p className="extension-builder-muted">No rollback history reported.</p> : null}
            {runtimeStatus.history.map(version => (
              <RuntimeVersionRow
                key={version.version}
                version={version}
                busy={busy}
                canRollback={capabilities.canRollback}
                currentVersion={runtimeStatus.version}
                onRollback={onRollback}
              />
            ))}
          </div>
          {runtimeStatus.diagnostics.length > 0 ? (
            <>
              <h4>Runtime diagnostics</h4>
              <div className="extension-builder-diagnostics-list">
                {runtimeStatus.diagnostics.map((diagnostic, index) => (
                  <div key={`${diagnostic.message}-${index}`} className="extension-builder-diagnostic-row">
                    <StatusChip tone={diagnosticTone(diagnostic.severity ?? "warning")}>{diagnostic.severity ?? "warning"}</StatusChip>
                    <span>{diagnostic.message}<small>{diagnostic.source ?? "runtime"}</small></span>
                  </div>
                ))}
              </div>
            </>
          ) : null}
        </>
      ) : null}
    </div>
  );
}
