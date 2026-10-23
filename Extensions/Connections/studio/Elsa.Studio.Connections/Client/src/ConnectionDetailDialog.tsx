import React, { useId, useRef } from "react";
import { Ban, Cable, ShieldCheck, Trash2, X } from "lucide-react";
import { useDialogFocus } from "@elsa-workflows/studio-sdk";
import type { ConnectionDescriptorsResponse, ConnectionMetadata } from "./connectionTypes";
import { connectionDate, connectionSchemeLabel, ConnectionStatusBadge, connectionTypeLabel } from "./connectionPresentation";

export type ConnectionAction = "Test" | "Revoke" | "Delete";

export function ConnectionDetailDialog({ connection, descriptors, busy, notification, onClose, onAction, restoreFocus }: {
  connection: ConnectionMetadata;
  descriptors: ConnectionDescriptorsResponse | null;
  busy: ConnectionAction | null;
  notification: React.ReactNode;
  onClose(): void;
  onAction(action: ConnectionAction): void;
  restoreFocus(): void;
}) {
  const dialogRef = useRef<HTMLElement>(null);
  const titleId = useId();
  useDialogFocus(dialogRef, busy ? null : onClose, true, restoreFocus);
  const settings = Object.entries(connection.settings);
  const settingFields = descriptors?.types.find(type => type.name === connection.typeName)?.settings ?? [];

  return <div className="connections-modal-backdrop" onMouseDown={event => { if (event.target === event.currentTarget && !busy) onClose(); }}>
    <section ref={dialogRef} className="connections-modal connections-detail" role="dialog" aria-modal="true" aria-labelledby={titleId} aria-busy={!!busy || undefined} tabIndex={-1}>
      <header className="connections-dialog-header">
        <div className="connections-dialog-title"><span className="connections-service-icon" aria-hidden="true"><Cable size={20} /></span><div>
          <span className="connections-kicker">Connection details</span><h2 id={titleId}>{connection.displayName || connection.name}</h2>
          {connection.displayName && connection.displayName !== connection.name ? <code>{connection.name}</code> : null}
        </div></div>
        <button type="button" className="connections-icon-button" aria-label="Close connection details" disabled={!!busy} onClick={onClose}><X size={18} aria-hidden="true" /></button>
      </header>
      <div className="connections-dialog-body">
        <div className="connections-detail-status"><ConnectionStatusBadge status={connection.status} />
          <p>{connection.status === "Ready" ? "Available to workflows that use this connection." : connection.status === "NeedsAuthorization" ? "Workflows that use it can still be promoted. Runs that use this connection will fail until it is authorized." : connection.status === "Revoked" ? "This connection can no longer authorize requests." : "Test this connection to check its configuration."}</p>
        </div>
        {notification}
        <dl className="connections-summary">
          <div><dt>Service</dt><dd>{connectionTypeLabel(descriptors, connection.typeName)}</dd></div>
          <div><dt>Authentication</dt><dd>{connectionSchemeLabel(descriptors, connection.authScheme)}</dd></div>
          <div><dt>Last tested</dt><dd>{connectionDate(connection.lastTestedAt)}</dd></div>
          <div><dt>Created</dt><dd>{connectionDate(connection.createdAt)}</dd></div>
        </dl>
        {settings.length ? <section className="connections-detail-section"><h3>Service settings</h3><dl className="connections-summary connections-settings">{settings.map(([name, value]) => {
          const field = settingFields.find(candidate => candidate.name === name);
          const displayValue = field?.editorHint === "checkbox" && (value === "true" || value === "false")
            ? (value === "true" ? "Enabled" : "Disabled") : value || "—";
          return <div key={name}><dt>{field?.displayName || name}</dt><dd>{displayValue}</dd></div>;
        })}</dl></section> : null}
        <details className="connections-technical"><summary>Technical details</summary><dl className="connections-summary">
          <div><dt>Name</dt><dd><code>{connection.name}</code></dd></div>
          <div><dt>Type</dt><dd><code>{connection.typeName}</code></dd></div>
          <div><dt>Auth scheme</dt><dd><code>{connection.authScheme}</code></dd></div>
          <div><dt>Owner</dt><dd>{connection.owner || "—"}</dd></div>
          <div><dt>Managed by</dt><dd>{connection.managedBy || "—"}</dd></div>
          <div><dt>Credential fields</dt><dd>{connection.secretFieldNames.join(", ") || "None"}</dd></div>
        </dl></details>
      </div>
      <footer className="connections-dialog-footer">
        <div className="connections-danger-actions"><button type="button" className="connections-danger" disabled={!!busy || connection.status === "Revoked"} onClick={() => onAction("Revoke")}><Ban size={14} aria-hidden="true" />{busy === "Revoke" ? "Revoking…" : "Revoke"}</button>
          <button type="button" className="connections-danger" disabled={!!busy} onClick={() => onAction("Delete")}><Trash2 size={14} aria-hidden="true" />{busy === "Delete" ? "Deleting…" : "Delete"}</button></div>
        <button type="button" disabled={!!busy} onClick={() => onAction("Test")}><ShieldCheck size={14} aria-hidden="true" />{busy === "Test" ? "Testing…" : "Test"}</button>
      </footer>
    </section>
  </div>;
}
