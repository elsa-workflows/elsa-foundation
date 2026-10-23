import React from "react";
import { AlertCircle, Cable, ChevronRight, RefreshCw, Search } from "lucide-react";
import type { ConnectionDescriptorsResponse, ConnectionMetadata } from "./connectionTypes";
import { connectionDate, connectionSchemeLabel, ConnectionStatusBadge, connectionTypeLabel } from "./connectionPresentation";

export function ConnectionList({ items, descriptors, loading, error, searched, onRetry, onClear, onOpen }: {
  items: ConnectionMetadata[];
  descriptors: ConnectionDescriptorsResponse | null;
  loading: boolean;
  error: boolean;
  searched: boolean;
  onRetry(): void;
  onClear(): void;
  onOpen(name: string): void;
}) {
  const empty = items.length === 0;
  return (
    <div className="connections-table-wrap">
      <table className="connections-table" aria-label="Connections" aria-busy={loading || undefined}>
        <thead><tr><th>Name</th><th>Service</th><th>Authentication</th><th>Status</th><th>Last tested</th><th><span className="connections-sr-only">Actions</span></th></tr></thead>
        <tbody>
          {loading || error || empty ? <tr><td colSpan={6} className="connections-empty-cell">
            <div className="connections-empty" role={error ? "alert" : "status"}>
              <span className="connections-empty-icon" aria-hidden="true">{error ? <AlertCircle size={24} /> : searched ? <Search size={24} /> : <Cable size={24} />}</span>
              <strong>{loading ? "Loading connections…" : error ? "Connections could not be loaded" : searched ? "No matching connections" : "No connections yet"}</strong>
              {!loading ? <p>{error ? "Try loading the list again." : searched ? "Try a different name or clear your search." : "Connect a service to use it in your workflows."}</p> : null}
              {!loading && error ? <button type="button" onClick={onRetry}><RefreshCw size={14} aria-hidden="true" /> Retry</button> : null}
              {!loading && !error && searched ? <button type="button" onClick={onClear}>Clear search</button> : null}
            </div>
          </td></tr> : items.map(item => (
            <tr key={item.name}>
              <td><div className="connections-name">
                <span className="connections-service-icon" aria-hidden="true"><Cable size={18} /></span>
                <div><button type="button" className="connections-name-link" onClick={() => onOpen(item.name)}>{item.displayName || item.name}</button>
                  {item.displayName && item.displayName !== item.name ? <code>{item.name}</code> : null}</div>
              </div></td>
              <td data-label="Service">{connectionTypeLabel(descriptors, item.typeName)}</td>
              <td data-label="Authentication">{connectionSchemeLabel(descriptors, item.authScheme)}</td>
              <td data-label="Status"><ConnectionStatusBadge status={item.status} /></td>
              <td className="connections-date" data-label="Last tested">{connectionDate(item.lastTestedAt)}</td>
              <td><button type="button" className="connections-details-action" aria-label={`Details for ${item.displayName || item.name}`} onClick={() => onOpen(item.name)}>Details<ChevronRight size={14} aria-hidden="true" /></button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
