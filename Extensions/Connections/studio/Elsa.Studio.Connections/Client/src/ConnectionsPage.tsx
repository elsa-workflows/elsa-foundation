import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Ban, Cable, Plus, Search, ShieldCheck, Trash2 } from "lucide-react";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { deleteConnection, getConnectionDescriptors, listConnections, revokeConnection, testConnection } from "./connectionsApi";
import type { ConnectionDescriptorsResponse, ConnectionMetadata } from "./connectionTypes";
import { ConnectionCreateDialog } from "./ConnectionCreateDialog";

export function ConnectionsPage({ context, dialogs }: { context: StudioEndpointContext; dialogs: StudioDialogApi }) {
  const [search, setSearch] = useState("");
  const [items, setItems] = useState<ConnectionMetadata[]>([]);
  const [selectedName, setSelectedName] = useState<string | null>(null);
  const [descriptors, setDescriptors] = useState<ConnectionDescriptorsResponse | null>(null);
  const [creating, setCreating] = useState(false);
  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loadedContext, setLoadedContext] = useState(context);
  const loadedContextRef = useRef(context);
  const currentContextRef = useRef(context);
  const loadRequestRef = useRef(0);
  const invalidateLoadRequest = useCallback(() => {
    loadRequestRef.current++;
  }, []);
  currentContextRef.current = context;

  const contextIsLoaded = loadedContext === context;
  const visibleItems = useMemo(() => contextIsLoaded ? items : [], [contextIsLoaded, items]);
  const visibleDescriptors = contextIsLoaded ? descriptors : null;
  const selected = useMemo(
    () => visibleItems.find(item => item.name === selectedName) ?? visibleItems[0] ?? null,
    [visibleItems, selectedName]
  );

  const load = useCallback(async (preferredName?: string) => {
    const requestContext = context;
    const wasContextLoaded = loadedContextRef.current === requestContext;
    const requestId = ++loadRequestRef.current;
    if (!wasContextLoaded) {
      setItems([]);
      setDescriptors(null);
      setSelectedName(null);
      setStatus(null);
    }
    setLoading(true);
    setError(null);
    try {
      const [list, descriptorResponse] = await Promise.all([
        listConnections(requestContext, search),
        getConnectionDescriptors(requestContext)
      ]);
      if (requestId !== loadRequestRef.current || currentContextRef.current !== requestContext) return;
      setItems(list.items);
      setDescriptors(descriptorResponse);
      setSelectedName(current => {
        const candidate = preferredName ?? (wasContextLoaded ? current : null);
        return candidate && list.items.some(item => item.name === candidate) ? candidate : list.items[0]?.name ?? null;
      });
      loadedContextRef.current = requestContext;
      setLoadedContext(requestContext);
    } catch (e) {
      if (requestId === loadRequestRef.current && currentContextRef.current === requestContext) {
        setError(e instanceof Error ? e.message : "Connections could not be loaded.");
        setItems([]);
        setDescriptors(null);
        setSelectedName(null);
        loadedContextRef.current = requestContext;
        setLoadedContext(requestContext);
      }
    } finally {
      if (requestId === loadRequestRef.current && currentContextRef.current === requestContext)
        setLoading(false);
    }
  }, [context, search]);

  const currentLoadRef = useRef(load);
  currentLoadRef.current = load;

  useEffect(() => {
    void currentLoadRef.current();
    setCreating(false);
    return invalidateLoadRequest;
  }, [context, invalidateLoadRequest]);

  return (
    <div className="connections-shell">
      <header>
        <h1><Cable aria-hidden="true" size={18} /> Connections</h1>
        <p>Named service connections. Credentials stay on secret fields and are never shown here.</p>
      </header>
      <div className="connections-toolbar">
        <label>
          <Search aria-hidden="true" size={14} />
          <input
            aria-label="Search connections"
            value={search}
            onChange={event => setSearch(event.target.value)}
            onKeyDown={event => {
              if (event.key === "Enter") void load();
            }}
          />
        </label>
        <button type="button" onClick={() => void load()}>Search</button>
        <button type="button" onClick={() => setCreating(true)} disabled={!contextIsLoaded || !visibleDescriptors}><Plus aria-hidden="true" size={14} /> New connection</button>
      </div>
      {contextIsLoaded && status ? <p role="status">{status}</p> : null}
      {contextIsLoaded && error ? <p role="alert">{error}</p> : null}
      {loading || !contextIsLoaded ? <p role="status">Loading…</p> : (
        <div className="connections-layout">
          <ul>
            {visibleItems.map(item => (
              <li key={item.name}>
                <button type="button" aria-current={selected?.name === item.name} onClick={() => setSelectedName(item.name)}>
                  {item.displayName || item.name}
                  <span>{item.typeName} · {item.authScheme} · {item.status}</span>
                </button>
              </li>
            ))}
          </ul>
          {selected ? (
            <article>
              <h2>{selected.displayName || selected.name}</h2>
              <dl>
                <dt>Name</dt><dd>{selected.name}</dd>
                <dt>Type</dt><dd>{selected.typeName}</dd>
                <dt>Scheme</dt><dd>{selected.authScheme}</dd>
                <dt>Status</dt><dd>{selected.status}</dd>
                <dt>Owner</dt><dd>{selected.owner}</dd>
              </dl>
              {selected.status === "NeedsAuthorization" ? <p role="status">Needs authorization. Publishing still succeeds; a run faults until this connection is authorized.</p> : null}
              <div className="connections-actions">
                <button type="button" onClick={async () => {
                  const requestContext = context;
                  const connectionName = selected.name;
                  try {
                    const result = await testConnection(requestContext, connectionName);
                    if (currentContextRef.current !== requestContext) return;
                    setStatus(result.message ?? (result.succeeded ? "Test succeeded." : "Test failed."));
                  } catch (e) {
                    if (currentContextRef.current !== requestContext) return;
                    setError(e instanceof Error ? e.message : "The connection could not be tested.");
                  }
                }}><ShieldCheck aria-hidden="true" size={14} /> Test</button>
                <button type="button" onClick={async () => {
                  const requestContext = context;
                  const connectionName = selected.name;
                  if (!await dialogs.confirm({ message: `Revoke ${connectionName}?`, tone: "danger" })) return;
                  if (currentContextRef.current !== requestContext) return;
                  try {
                    await revokeConnection(requestContext, connectionName);
                    if (currentContextRef.current !== requestContext) return;
                    await load(connectionName);
                  } catch (e) {
                    if (currentContextRef.current !== requestContext) return;
                    setError(e instanceof Error ? e.message : "The connection could not be revoked.");
                  }
                }}><Ban aria-hidden="true" size={14} /> Revoke</button>
                <button type="button" onClick={async () => {
                  const requestContext = context;
                  const connectionName = selected.name;
                  if (!await dialogs.confirm({ message: `Delete ${connectionName}?`, tone: "danger" })) return;
                  if (currentContextRef.current !== requestContext) return;
                  try {
                    await deleteConnection(requestContext, connectionName);
                    if (currentContextRef.current !== requestContext) return;
                    await load();
                  } catch (e) {
                    if (currentContextRef.current !== requestContext) return;
                    setError(e instanceof Error ? e.message : "The connection could not be deleted.");
                  }
                }}><Trash2 aria-hidden="true" size={14} /> Delete</button>
              </div>
            </article>
          ) : <p>No connections yet.</p>}
        </div>
      )}
      {creating && visibleDescriptors ? (
        <ConnectionCreateDialog
          context={context}
          descriptors={visibleDescriptors}
          onCancel={() => setCreating(false)}
          onCreated={created => {
            setStatus(`Connection '${created.name}' created.`);
            void load(created.name);
          }}
        />
      ) : null}
    </div>
  );
}
