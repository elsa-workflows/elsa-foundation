import React, { useState } from "react";
import { Ban, Pencil, ShieldCheck, Trash2 } from "lucide-react";
import type { ConnectionDescriptorsResponse, ConnectionMetadata } from "./connectionTypes";
import { StudioButton, StudioDialog, StudioDialogFooter, StudioTimestamp } from "@elsa-workflows/studio-ui";
import { connectionSchemeLabel, ConnectionStatusBadge, connectionTypeLabel } from "./connectionPresentation";

export type ConnectionAction = "Test" | "Revoke" | "Delete" | "Edit";

export function ConnectionDetailDialog({ connection, descriptors, busy, notification, onClose, onAction, restoreFocus }: {
  connection: ConnectionMetadata;
  descriptors: ConnectionDescriptorsResponse | null;
  busy: ConnectionAction | null;
  notification: React.ReactNode;
  onClose(): void;
  onAction(action: ConnectionAction): void;
  restoreFocus(): void;
}) {
  const [opener] = useState(() => document.activeElement instanceof HTMLElement ? document.activeElement : null);
  const settings = Object.entries(connection.settings);
  const settingFields = descriptors?.types.find(type => type.name === connection.typeName)?.settings ?? [];

  return <StudioDialog open title={connection.displayName || connection.name} closeLabel="Close connection details"
    description={connection.displayName && connection.displayName !== connection.name ? connection.name : undefined}
    className="connections-detail-shell" dismissDisabled={!!busy} onClose={onClose} onCloseAutoFocus={event => {
      event.preventDefault();
      if (document.activeElement !== document.body) return;
      if (opener?.isConnected && !opener.matches(":disabled")) opener.focus();
      else restoreFocus();
    }}
    footer={<StudioDialogFooter cancelLabel="Close" onCancel={onClose} cancelDisabled={!!busy}
      leading={<>
        <StudioButton variant="danger" disabled={!!busy || connection.status === "Revoked"} onClick={() => onAction("Revoke")}><Ban size={14} />{busy === "Revoke" ? "Revoking…" : "Revoke"}</StudioButton>
        <StudioButton variant="danger" disabled={!!busy} onClick={() => onAction("Delete")}><Trash2 size={14} />{busy === "Delete" ? "Deleting…" : "Delete"}</StudioButton>
      </>}
      extra={<StudioButton disabled={!!busy} onClick={() => onAction("Edit")}><Pencil size={14} />{busy === "Edit" ? "Opening…" : "Edit"}</StudioButton>}
      confirmType="button" confirmDisabled={!!busy} onConfirm={() => onAction("Test")}
      confirmLabel={<><ShieldCheck size={14} />{busy === "Test" ? "Testing…" : "Test"}</>} />}>
      <div className="connections-dialog-body">
        <div className="connections-detail-status"><ConnectionStatusBadge status={connection.status} />
          <p>{connection.status === "Ready" ? "Available to workflows that use this connection." : connection.status === "NeedsAuthorization" ? "Workflows that use it can still be promoted. Runs that use this connection will fail until it is authorized." : connection.status === "Revoked" ? "This connection can no longer authorize requests." : "Test this connection to check its configuration."}</p>
        </div>
        {notification}
        <dl className="connections-summary">
          <div><dt>Service</dt><dd>{connectionTypeLabel(descriptors, connection.typeName)}</dd></div>
          <div><dt>Authentication</dt><dd>{connectionSchemeLabel(descriptors, connection.authScheme)}</dd></div>
          <div><dt>Last tested</dt><dd><StudioTimestamp value={connection.lastTestedAt} fallback="Not tested yet" invalidFallback="Unavailable" /></dd></div>
          <div><dt>Created</dt><dd><StudioTimestamp value={connection.createdAt} fallback="Unavailable" invalidFallback="Unavailable" /></dd></div>
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
  </StudioDialog>;
}
