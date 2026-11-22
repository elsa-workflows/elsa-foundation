import { StudioLoadingStatus, studioRowInteraction, StudioCollectionEmptyState, StudioTableContainer, StudioResultState, StudioButton, StudioTimestamp } from "@elsa-workflows/studio-ui";
import React from "react";
import { AlertCircle, Cable, RefreshCw, Search, Plus } from "lucide-react";
import type { ConnectionDescriptorsResponse, ConnectionMetadata } from "./connectionTypes";
import { connectionSchemeLabel, ConnectionStatusBadge, connectionTypeLabel } from "./connectionPresentation";

export function ConnectionList({ items, descriptors, loading, error, searched, onRetry, onClear, onCreate, onOpen }: {
  items: ConnectionMetadata[];
  descriptors: ConnectionDescriptorsResponse | null;
  loading: boolean;
  error: boolean;
  searched: boolean;
  onRetry(): void;
  onClear(): void;
  onCreate?(): void;
  onOpen(name: string): void;
}) {
  const empty = items.length === 0;
  if (!loading && !error && empty) return <StudioCollectionEmptyState headingLevel={3}
    icon={<Cable size={24} />}
    title={searched ? "No matching connections" : "No connections yet"}
    description={searched ? "Try a different name or clear your search." : "Connect a service to use it in your workflows."}
    actions={searched ? <StudioButton onClick={onClear}>Clear search</StudioButton> : onCreate ? <StudioButton variant="primary" onClick={onCreate}><Plus size={14} aria-hidden />New connection</StudioButton> : undefined} />;
  return (
    <StudioTableContainer>
      <table className="studio-table connections-table" aria-label="Connections" aria-busy={loading || undefined}>
        <thead><tr><th>Name</th><th>Service</th><th>Authentication</th><th>Status</th><th>Last tested</th></tr></thead>
        <tbody>
          {loading || error ? <tr><td colSpan={5} className="connections-empty-cell">
            {loading ? <StudioLoadingStatus>Loading connections…</StudioLoadingStatus> : <StudioResultState headingLevel={3} tone="danger"
              icon={<AlertCircle size={24} />}
              title="Connections could not be loaded"
              description="Try loading the list again."
              actions={<StudioButton onClick={onRetry}><RefreshCw size={14} aria-hidden /> Retry</StudioButton>} />}
          </td></tr> : items.map(item => (
            <tr key={item.name} {...studioRowInteraction<HTMLTableRowElement>(() => onOpen(item.name))}>
              <td><div className="connections-name">
                <span className="connections-service-icon" aria-hidden="true"><Cable size={18} /></span>
                <div><strong>{item.displayName || item.name}</strong>
                  {item.displayName && item.displayName !== item.name ? <code>{item.name}</code> : null}</div>
              </div></td>
              <td data-label="Service">{connectionTypeLabel(descriptors, item.typeName)}</td>
              <td data-label="Authentication">{connectionSchemeLabel(descriptors, item.authScheme)}</td>
              <td data-label="Status"><ConnectionStatusBadge status={item.status} /></td>
              <td className="connections-date" data-label="Last tested"><StudioTimestamp value={item.lastTestedAt} fallback="Not tested yet" invalidFallback="Unavailable" /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </StudioTableContainer>
  );
}
