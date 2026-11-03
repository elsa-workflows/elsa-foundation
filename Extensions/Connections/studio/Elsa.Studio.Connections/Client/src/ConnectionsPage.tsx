import React, { useCallback, useEffect, useRef, useState } from "react";
import { AlertCircle, CheckCircle2, Plus, RefreshCw, X } from "lucide-react";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioSearchInput , StudioPage, StudioPageHeader, StudioCollectionFooter, StudioCollectionToolbar} from "@elsa-workflows/studio-ui";
import { deleteConnection, getConnectionDescriptors, listConnections, revokeConnection, testConnection } from "./connectionsApi";
import type { ConnectionDescriptorsResponse, ConnectionMetadata } from "./connectionTypes";
import { ConnectionCreateDialog } from "./ConnectionCreateDialog";
import { ConnectionDetailDialog, type ConnectionAction } from "./ConnectionDetailDialog";
import { ConnectionList } from "./ConnectionList";

type Notice = { message: string; error?: boolean };

export function ConnectionsPage({ context, dialogs }: { context: StudioEndpointContext; dialogs: StudioDialogApi }) {
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [items, setItems] = useState<ConnectionMetadata[]>([]);
  const [selectedName, setSelectedName] = useState<string | null>(null);
  const [descriptors, setDescriptors] = useState<ConnectionDescriptorsResponse | null>(null);
  const [creating, setCreating] = useState(false);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState<ConnectionAction | null>(null);
  const [loadedContext, setLoadedContext] = useState(context);
  const currentContextRef = useRef(context);
  const loadRequestRef = useRef(0);
  const operationRef = useRef<object | null>(null);
  const searchFormRef = useRef<HTMLFormElement>(null);
  currentContextRef.current = context;
  const contextIsLoaded = loadedContext === context;
  const visibleItems = contextIsLoaded ? items : [];
  const visibleDescriptors = contextIsLoaded ? descriptors : null;
  const selected = visibleItems.find(item => item.name === selectedName) ?? null;

  const load = useCallback(async (query: string, preferredName?: string) => {
    const requestContext = context;
    const requestId = ++loadRequestRef.current;
    setLoading(true);
    setLoadError(null);
    try {
      const [list, descriptorResponse] = await Promise.all([
        listConnections(requestContext, query), getConnectionDescriptors(requestContext)
      ]);
      if (requestId !== loadRequestRef.current || currentContextRef.current !== requestContext) return;
      setItems(list.items);
      setDescriptors(descriptorResponse);
      setAppliedSearch(query.trim());
      setSelectedName(current => {
        const candidate = preferredName ?? current;
        return list.items.some(item => item.name === candidate) ? candidate : null;
      });
    } catch (cause) {
      if (requestId !== loadRequestRef.current || currentContextRef.current !== requestContext) return;
      setLoadError(cause instanceof Error ? cause.message : "Connections could not be loaded.");
      setItems([]);
      setDescriptors(null);
      setSelectedName(null);
    } finally {
      if (requestId === loadRequestRef.current && currentContextRef.current === requestContext) {
        setLoadedContext(requestContext);
        setLoading(false);
      }
    }
  }, [context]);

  const invalidateRequests = useCallback(() => { loadRequestRef.current++; operationRef.current = null; }, []);

  useEffect(() => {
    setItems([]);
    setDescriptors(null);
    setSelectedName(null);
    setCreating(false);
    setNotice(null);
    setBusy(null);
    setSearch("");
    setAppliedSearch("");
    operationRef.current = null;
    void load("");
    return invalidateRequests;
  }, [load, invalidateRequests]);

  async function runAction(action: ConnectionAction, connection: ConnectionMetadata) {
    if (operationRef.current) return;
    const requestContext = context;
    const operation = {};
    operationRef.current = operation;
    const isCurrent = () => currentContextRef.current === requestContext && operationRef.current === operation;
    setBusy(action);
    setNotice(null);
    try {
      if (action !== "Test" && !await dialogs.confirm({ message: `${action} ${connection.name}?`, tone: "danger" })) return;
      if (!isCurrent()) return;
      if (action === "Test") {
        const result = await testConnection(requestContext, connection.name);
        if (isCurrent()) {
          setNotice({ message: result.message || (result.succeeded ? "Test succeeded." : "Test failed."), error: !result.succeeded });
          await load(appliedSearch);
        }
      } else {
        if (action === "Revoke") await revokeConnection(requestContext, connection.name);
        else await deleteConnection(requestContext, connection.name);
        if (!isCurrent()) return;
        setNotice({ message: `Connection '${connection.name}' ${action === "Revoke" ? "revoked" : "deleted"}.` });
        await load(appliedSearch);
      }
    } catch (cause) {
      if (isCurrent()) setNotice({ message: cause instanceof Error ? cause.message : `The connection could not be ${action === "Test" ? "tested" : action === "Revoke" ? "revoked" : "deleted"}.`, error: true });
    } finally {
      if (isCurrent()) { operationRef.current = null; setBusy(null); }
    }
  }

  const notification = contextIsLoaded && notice ? <div className={`connections-notice${notice.error ? " connections-notice--error" : ""}`} role={notice.error ? "alert" : "status"}>
    {notice.error ? <AlertCircle size={16} aria-hidden="true" /> : <CheckCircle2 size={16} aria-hidden="true" />}<span>{notice.message}</span>
    <button type="button" className="connections-icon-button" aria-label="Dismiss notification" onClick={() => setNotice(null)}><X size={14} aria-hidden="true" /></button>
  </div> : null;

  return <StudioPage className="connections-shell" aria-label="Connections management">
    <StudioPageHeader title="Connections" headingLevel={2} description="Connect your services once, then reuse them in your workflows." actions={        <div className="studio-page-actions" role="group" aria-label="Connection page actions">
          <StudioButton variant="primary" size="md" onClick={() => { setNotice(null); setCreating(true); }} disabled={!visibleDescriptors}><Plus size={14} aria-hidden="true" />Create connection</StudioButton>
          <StudioButton size="md" onClick={() => void load(appliedSearch)} disabled={loading}><RefreshCw size={14} aria-hidden="true" />Refresh</StudioButton>
        </div>} />
    <StudioCollectionToolbar>
      <form ref={searchFormRef} className="connections-search" role="search" aria-label="Connections" onSubmit={event => { event.preventDefault(); void load(search); }}>
        <StudioSearchInput className="connections-search-control" aria-label="Search connections" placeholder="Search connections" value={search} onChange={event => setSearch(event.target.value)} />
        <StudioButton type="submit" size="md" disabled={loading}>Search</StudioButton>
      </form>
    </StudioCollectionToolbar>
    {!selected ? notification : null}
    {loadError && contextIsLoaded ? <p className="connections-load-error">{loadError}</p> : null}
    <ConnectionList items={visibleItems} descriptors={visibleDescriptors} loading={loading || !contextIsLoaded} error={!!loadError && contextIsLoaded} searched={!!appliedSearch} onRetry={() => void load(search)} onClear={() => { setSearch(""); void load(""); }} onOpen={name => { setNotice(null); setSelectedName(name); }} />
    {!loading && contextIsLoaded && !loadError ? <StudioCollectionFooter><span>{visibleItems.length} shown</span></StudioCollectionFooter> : null}
    {visibleItems.length === 100 && !loading ? <p className="connections-limit">Showing the first 100 matches. Search by name to narrow the list.</p> : null}
    {selected ? <ConnectionDetailDialog key={selected.name} connection={selected} descriptors={visibleDescriptors} busy={busy} notification={notification} onClose={() => setSelectedName(null)} onAction={action => void runAction(action, selected)} restoreFocus={() => searchFormRef.current?.querySelector<HTMLInputElement>("input")?.focus()} /> : null}
    {creating && visibleDescriptors ? <ConnectionCreateDialog context={context} descriptors={visibleDescriptors} onCancel={() => setCreating(false)} onCreated={created => { setNotice({ message: `Connection '${created.name}' created.` }); setSearch(appliedSearch); void load(appliedSearch, created.name); }} /> : null}
  </StudioPage>;
}
