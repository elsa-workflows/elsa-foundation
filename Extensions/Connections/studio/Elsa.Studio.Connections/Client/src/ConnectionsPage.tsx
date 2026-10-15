import React, { useEffect, useMemo, useState } from "react";
import { Ban, Cable, Plus, Search, ShieldCheck, Trash2 } from "lucide-react";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createConnection, deleteConnection, getConnectionDescriptors, listConnections, revokeConnection, testConnection } from "./connectionsApi";
import type { ConnectionDescriptorsResponse, ConnectionMetadata, CreateConnectionRequest } from "./connectionTypes";

export function ConnectionsPage({ context, dialogs }: { context: StudioEndpointContext; dialogs: StudioDialogApi }) {
  const [search, setSearch] = useState("");
  const [items, setItems] = useState<ConnectionMetadata[]>([]);
  const [selectedName, setSelectedName] = useState<string | null>(null);
  const [descriptors, setDescriptors] = useState<ConnectionDescriptorsResponse | null>(null);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [typeName, setTypeName] = useState("http");
  const [authScheme, setAuthScheme] = useState("bearer");
  const [allowedHosts, setAllowedHosts] = useState("");
  const [secretValue, setSecretValue] = useState("");
  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const selected = useMemo(
    () => items.find(item => item.name === selectedName) ?? items[0] ?? null,
    [items, selectedName]
  );

  async function load(preferredName?: string) {
    setLoading(true);
    setError(null);
    try {
      const [list, descriptorResponse] = await Promise.all([
        listConnections(context, search),
        getConnectionDescriptors(context)
      ]);
      setItems(list.items);
      setDescriptors(descriptorResponse);
      setSelectedName(current => {
        const candidate = preferredName ?? current;
        return candidate && list.items.some(item => item.name === candidate) ? candidate : list.items[0]?.name ?? null;
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Connections could not be loaded.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function onCreate(event: React.FormEvent) {
    event.preventDefault();
    const request: CreateConnectionRequest = {
      name,
      typeName,
      authScheme,
      settings: allowedHosts.trim() ? { allowedHosts: allowedHosts.trim() } : {},
      secretFields: secretValue
        ? {
            [authScheme === "basic" ? "password" : authScheme === "api-key" ? "apiKey" : "token"]: { value: secretValue }
          }
        : {}
    };
    try {
      const created = await createConnection(context, request);
      setCreating(false);
      setName("");
      setSecretValue("");
      setStatus(`Connection '${created.name}' created.`);
      await load(created.name);
    } catch (e) {
      setError(e instanceof Error ? e.message : "The connection could not be created.");
    }
  }

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
        <button type="button" onClick={() => setCreating(true)}><Plus aria-hidden="true" size={14} /> New connection</button>
      </div>
      {status ? <p role="status">{status}</p> : null}
      {error ? <p role="alert">{error}</p> : null}
      {loading ? <p>Loading…</p> : (
        <div className="connections-layout">
          <ul>
            {items.map(item => (
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
                  const result = await testConnection(context, selected.name);
                  setStatus(result.message ?? (result.succeeded ? "Test succeeded." : "Test failed."));
                }}><ShieldCheck aria-hidden="true" size={14} /> Test</button>
                <button type="button" onClick={async () => {
                  if (!await dialogs.confirm({ message: `Revoke ${selected.name}?`, tone: "danger" })) return;
                  await revokeConnection(context, selected.name);
                  await load(selected.name);
                }}><Ban aria-hidden="true" size={14} /> Revoke</button>
                <button type="button" onClick={async () => {
                  if (!await dialogs.confirm({ message: `Delete ${selected.name}?`, tone: "danger" })) return;
                  await deleteConnection(context, selected.name);
                  await load();
                }}><Trash2 aria-hidden="true" size={14} /> Delete</button>
              </div>
            </article>
          ) : <p>No connections yet.</p>}
        </div>
      )}
      {creating ? (
        <form className="connections-modal" onSubmit={onCreate}>
          <h2>New connection</h2>
          <label>Name <input required value={name} onChange={event => setName(event.target.value)} autoComplete="off" /></label>
          <label>Type
            <select value={typeName} onChange={event => setTypeName(event.target.value)}>
              {(descriptors?.types ?? [{ name: "http", displayName: "HTTP" }]).map(type => (
                <option key={type.name} value={type.name}>{type.displayName}</option>
              ))}
            </select>
          </label>
          <label>Auth scheme
            <select value={authScheme} onChange={event => setAuthScheme(event.target.value)}>
              {(descriptors?.schemes ?? [{ name: "bearer", displayName: "Bearer" }, { name: "basic", displayName: "Basic" }, { name: "api-key", displayName: "API key" }]).map(scheme => (
                <option key={scheme.name} value={scheme.name}>{scheme.displayName}</option>
              ))}
            </select>
          </label>
          <label>Allowed hosts <input value={allowedHosts} onChange={event => setAllowedHosts(event.target.value)} placeholder="api.example, *.googleapis.com" /></label>
          <label>Credential <input type="password" autoComplete="new-password" value={secretValue} onChange={event => setSecretValue(event.target.value)} /></label>
          <div>
            <button type="submit">Create</button>
            <button type="button" onClick={() => setCreating(false)}>Cancel</button>
          </div>
        </form>
      ) : null}
    </div>
  );
}
