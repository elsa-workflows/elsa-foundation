import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Plus } from "lucide-react";
import type { StudioDialogApi, StudioEndpointContext, StudioNotificationApi } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton, StudioSearchInput, StudioPage, StudioPageHeader, StudioCollectionFooter, StudioCollectionToolbar } from "@elsa-workflows/studio-ui";
import { deleteConnection, getConnectionDescriptors, listConnections, revokeConnection, testConnection } from "./connectionsApi";
import type { ConnectionDescriptorsResponse, ConnectionMetadata } from "./connectionTypes";
import { ConnectionCreateDialog } from "./ConnectionCreateDialog";
import { ConnectionDetailDialog, type ConnectionAction } from "./ConnectionDetailDialog";
import { ConnectionList } from "./ConnectionList";

type Notice = { message: string; error?: boolean };

export function ConnectionsPage({ context, dialogs, notifications }: { context: StudioEndpointContext; dialogs: StudioDialogApi; notifications?: StudioNotificationApi }) {
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const searchRef = useRef(search); searchRef.current = search;
  const [items, setItems] = useState<ConnectionMetadata[]>([]);
  const [selectedName, setSelectedName] = useState<string | null>(null);
  const [descriptors, setDescriptors] = useState<ConnectionDescriptorsResponse | null>(null);
  const [creating, setCreating] = useState(false);
  const [editingName, setEditingName] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState<ConnectionAction | null>(null);
  const [loadedContext, setLoadedContext] = useState(context);
  const currentContextRef = useRef(context);
  const loadRequestRef = useRef(0);
  const operationRef = useRef<object | null>(null);
  const searchFormRef = useRef<HTMLFormElement>(null);
  const focusAfterCreate = useRef(false);
  const owner = useMemo(() => ({ context }), [context]);
  const currentOwner = useRef(owner); currentOwner.current = owner;
  currentContextRef.current = context;
  const contextIsLoaded = loadedContext === context;
  const visibleItems = contextIsLoaded ? items : [];
  const visibleDescriptors = contextIsLoaded ? descriptors : null;
  const selected = visibleItems.find(item => item.name === selectedName) ?? null;
  const editingConnection = editingName ? (visibleItems.find(item => item.name === editingName) ?? null) : null;

  const load = useCallback(async (query: string, knownDescriptors?: ConnectionDescriptorsResponse | null) => {
    const requestContext = context;
    const requestId = ++loadRequestRef.current;
    setLoading(true);
    setLoadError(null);
    try {
      const [list, descriptorResponse] = await Promise.all([
        listConnections(requestContext, query), knownDescriptors ?? getConnectionDescriptors(requestContext)
      ]);
      if (requestId !== loadRequestRef.current || currentContextRef.current !== requestContext) return;
      setItems(list.items);
      setDescriptors(descriptorResponse);
      setAppliedSearch(query.trim());
      setSelectedName(current => {
        const candidate = current;
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
    setEditingName(null);
    setNotice(null);
    setBusy(null);
    setSearch("");
    setAppliedSearch("");
    operationRef.current = null;
    void load("");
    return invalidateRequests;
  }, [load, invalidateRequests]);

  useEffect(() => {
    if (!creating && focusAfterCreate.current) {
      focusAfterCreate.current = false;
      // Dialog cleanup can restore its opener before React removes the empty result.
      // Restore to the stable search field after that commit, without refocusing on later loads.
      if (!document.activeElement || document.activeElement === document.body)
        searchFormRef.current?.querySelector<HTMLInputElement>("input")?.focus();
    }
  }, [creating]);

  async function runAction(action: ConnectionAction, connection: ConnectionMetadata) {
    if (operationRef.current) return;
    const requestContext = context;
    const operation = {};
    operationRef.current = operation;
    const isCurrent = () => currentContextRef.current === requestContext && operationRef.current === operation;
    setBusy(action);
    setNotice(null);
    try {
      if (action === "Edit") {
        setSelectedName(null);
        setEditingName(connection.name);
        operationRef.current = null;
        setBusy(null);
        return;
      }
      if (action !== "Test" && !await dialogs.confirm({ title: `${action} connection`, message: `${action} ${connection.name}?`, confirmLabel: action, tone: "danger" })) return;
      if (!isCurrent()) return;
      if (action === "Test") {
        const result = await testConnection(requestContext, connection.name);
        if (isCurrent()) {
          if (result.succeeded) notifications?.notify({ level: "success", title: "Connection test succeeded.", message: result.message || undefined });
          else setNotice({ message: result.message || "Test failed.", error: true });
          await load(searchRef.current);
        }
      } else {
        if (action === "Revoke") await revokeConnection(requestContext, connection.name);
        else await deleteConnection(requestContext, connection.name);
        if (!isCurrent()) return;
        notifications?.notify({ level: "success", title: `Connection '${connection.name}' ${action === "Revoke" ? "revoked" : "deleted"}.` });
        await load(searchRef.current);
      }
    } catch (cause) {
      if (isCurrent()) setNotice({ message: cause instanceof Error ? cause.message : `The connection could not be ${action === "Test" ? "tested" : action === "Revoke" ? "revoked" : "deleted"}.`, error: true });
    } finally {
      if (isCurrent()) { operationRef.current = null; setBusy(null); }
    }
  }

  const notification = contextIsLoaded && notice ? <StudioAlert tone={notice.error ? "danger" : "info"}
    dismiss={{ label: "Dismiss notification", onDismiss: () => setNotice(null) }}>{notice.message}</StudioAlert> : null;

  return <StudioPage className="connections-shell" aria-label="Connections management">
    <StudioPageHeader title="Connections" headingLevel={2} description="Connect your services once, then reuse them in your workflows."
      refresh={{ id: "connections", label: "Refresh connections", refreshing: loading, autoRefresh: true, onRefresh: () => load(searchRef.current) }}
      actions={<StudioButton variant="primary" size="md" onClick={() => { setNotice(null); setCreating(true); }} disabled={!visibleDescriptors}><Plus size={14} aria-hidden="true" />New connection</StudioButton>} />
    <StudioCollectionToolbar>
      <form ref={searchFormRef} className="connections-search" role="search" aria-label="Connections" onSubmit={event => event.preventDefault()}>
        <StudioSearchInput className="connections-search-control" aria-label="Search connections" placeholder="Search connections" value={search} onChange={event => { setSearch(event.target.value); void load(event.target.value, visibleDescriptors); }} />
      </form>
    </StudioCollectionToolbar>
    {!selected ? notification : null}
    {loadError && contextIsLoaded ? <p className="connections-load-error">{loadError}</p> : null}
    <ConnectionList items={visibleItems} descriptors={visibleDescriptors} loading={loading || !contextIsLoaded} error={!!loadError && contextIsLoaded} searched={!!appliedSearch} onRetry={() => void load(search)} onClear={() => { setSearch(""); void load(""); }} onCreate={visibleDescriptors ? () => { setNotice(null); setCreating(true); } : undefined} onOpen={name => { setNotice(null); setSelectedName(name); }} />
    {!loading && contextIsLoaded && !loadError ? <StudioCollectionFooter><span>{visibleItems.length} {visibleItems.length === 1 ? "connection" : "connections"}</span></StudioCollectionFooter> : null}
    {visibleItems.length === 100 && !loading ? <p className="connections-limit">Showing the first 100 matches. Search by name to narrow the list.</p> : null}
    {selected ? <ConnectionDetailDialog key={selected.name} connection={selected} descriptors={visibleDescriptors} busy={busy} notification={notification} onClose={() => setSelectedName(null)} onAction={action => void runAction(action, selected)} restoreFocus={() => searchFormRef.current?.querySelector<HTMLInputElement>("input")?.focus()} /> : null}
    {creating && visibleDescriptors ? <ConnectionCreateDialog context={context} descriptors={visibleDescriptors} onCancel={() => setCreating(false)} onCreated={(created, canNotify) => {
      if (currentOwner.current !== owner) return;
      if (canNotify) {
        focusAfterCreate.current = true;
        notifications?.notify({ level: "success", title: `Connection '${created.name}' created.` });
        setCreating(false); setSelectedName(null);
      }
      void load(searchRef.current);
    }} /> : null}
    {editingConnection && visibleDescriptors ? <ConnectionCreateDialog context={context} descriptors={visibleDescriptors} initialConnection={editingConnection} onCancel={() => setEditingName(null)} onCreated={(updated, canNotify) => {
      if (currentOwner.current !== owner) return;
      if (canNotify) {
        notifications?.notify({ level: "success", title: `Connection '${updated.name}' updated.` });
        setEditingName(null); setSelectedName(null);
      }
      void load(searchRef.current);
    }} /> : null}
  </StudioPage>;
}
