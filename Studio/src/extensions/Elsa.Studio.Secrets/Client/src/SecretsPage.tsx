import React, { useEffect, useMemo, useState } from "react";
import { AlertCircle, Ban, CheckCircle2, KeyRound, Pencil, Plus, RotateCcw, Search, ShieldCheck, Trash2 } from "lucide-react";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createSecret, deleteSecret, getSecretDescriptors, listSecrets, revokeSecret, rotateSecret, testSecret, updateSecret } from "./secretsApi";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata, SecretStatus, UpdateSecretRequest } from "./secretTypes";
import { SecretDetail } from "./SecretDetail";
import { CreateSecretDialog, RotateSecretDialog } from "./SecretDialogs";

const secretStatuses: SecretStatus[] = ["Active", "Retired", "Expired", "Revoked", "Deleted"];

export function SecretsPage({ context, dialogs }: { context: StudioEndpointContext; dialogs: StudioDialogApi }) {
  const [search, setSearch] = useState("");
  const [secrets, setSecrets] = useState<SecretMetadata[]>([]);
  const [selectedName, setSelectedName] = useState<string | null>(null);
  const [descriptors, setDescriptors] = useState<SecretDescriptorsResponse | null>(null);
  const [dialog, setDialog] = useState<"create" | "rotate" | null>(null);
  const [typeFilter, setTypeFilter] = useState("");
  const [storeFilter, setStoreFilter] = useState("");
  const [scopeFilter, setScopeFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [loading, setLoading] = useState(true);
  const [busyName, setBusyName] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const scopes = useMemo(() => Array.from(new Set(secrets.map(secret => secret.scope?.trim()).filter((scope): scope is string => !!scope))).sort(), [secrets]);
  const filteredSecrets = useMemo(() => {
    return secrets.filter(secret => {
      if (typeFilter && secret.typeName !== typeFilter) return false;
      if (storeFilter && secret.storeName !== storeFilter) return false;
      if (scopeFilter && (secret.scope ?? "") !== scopeFilter) return false;
      if (statusFilter && secret.status !== statusFilter) return false;
      return true;
    });
  }, [scopeFilter, secrets, statusFilter, storeFilter, typeFilter]);
  const selected = useMemo(() => {
    return filteredSecrets.find(secret => secret.name === selectedName) ?? filteredSecrets[0] ?? null;
  }, [filteredSecrets, selectedName]);

  async function load(preferredName?: string) {
    setLoading(true);
    setError(null);
    try {
      const [list, descriptorResponse] = await Promise.all([listSecrets(context, { search, activeOnly: false, pageSize: 100 }), getSecretDescriptors(context)]);
      setSecrets(list.items);
      setDescriptors(descriptorResponse);
      setSelectedName(current => {
        const candidate = preferredName ?? current;
        return candidate && list.items.some(secret => secret.name === candidate) ? candidate : list.items[0]?.name ?? null;
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secrets could not be loaded.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function refreshWithStatus(message: string, preferredName?: string) {
    await load(preferredName);
    setStatus(message);
  }

  async function saveMetadata(name: string, request: UpdateSecretRequest) {
    setBusyName(name);
    setError(null);
    setStatus(null);
    try {
      await updateSecret(context, name, request);
      await refreshWithStatus("Secret metadata updated.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secret metadata could not be updated.");
      throw e;
    } finally {
      setBusyName(null);
    }
  }

  async function saveCreatedSecret(request: CreateSecretRequest) {
    setError(null);
    setStatus(null);
    const created = await createSecret(context, request);
    await refreshWithStatus("Secret created.", created.name);
  }

  async function saveRotation(name: string, request: RotateSecretRequest) {
    setBusyName(name);
    setError(null);
    setStatus(null);
    try {
      await rotateSecret(context, name, request);
      await refreshWithStatus("Secret rotated.", name);
    } finally {
      setBusyName(null);
    }
  }

  async function runTest(secret: SecretMetadata) {
    setBusyName(secret.name);
    setError(null);
    setStatus(null);
    try {
      const result = await testSecret(context, secret.name);
      const summary = result.message?.trim() || (result.succeeded ? "Secret resolved successfully." : "Secret test failed.");
      setStatus(`${result.succeeded ? "Test succeeded" : "Test failed"} (${result.code}): ${summary}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secret test failed.");
    } finally {
      setBusyName(null);
    }
  }

  async function revoke(secret: SecretMetadata) {
    if (secret.status === "Revoked" || secret.status === "Deleted") return;
    if (!(await dialogs.confirm({ message: `Revoke secret "${secret.name}"? Existing references keep the name, but the secret can no longer be used as active material.`, confirmLabel: "Revoke", tone: "danger" }))) return;
    setBusyName(secret.name);
    setError(null);
    setStatus(null);
    try {
      await revokeSecret(context, secret.name);
      await refreshWithStatus("Secret revoked.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secret could not be revoked.");
    } finally {
      setBusyName(null);
    }
  }

  async function remove(secret: SecretMetadata) {
    if (!(await dialogs.confirm({ message: `Delete secret "${secret.name}"? This removes the metadata record and cannot reveal or recover its value.`, confirmLabel: "Delete", tone: "danger" }))) return;
    setBusyName(secret.name);
    setError(null);
    setStatus(null);
    try {
      await deleteSecret(context, secret.name);
      await refreshWithStatus("Secret deleted.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secret could not be deleted.");
    } finally {
      setBusyName(null);
    }
  }

  const rotateSelected = (secret: SecretMetadata) => {
    setSelectedName(secret.name);
    setDialog("rotate");
  };

  return (
    <main className="secrets-shell">
      <header className="secrets-header">
        <div>
          <h2><KeyRound size={18} /> Secrets</h2>
          <p>{filteredSecrets.length} shown · {secrets.length} total</p>
        </div>
        <button type="button" className="secrets-primary" onClick={() => setDialog("create")}>
          <Plus size={16} /> Create
        </button>
      </header>

      <section className="secrets-toolbar" aria-label="Secret filters">
        <label className="secrets-search">
          <Search size={16} />
          <input value={search} onChange={event => setSearch(event.target.value)} onKeyDown={event => event.key === "Enter" && void load()} placeholder="Search secrets" />
        </label>
        <FilterSelect label="Type" value={typeFilter} onChange={setTypeFilter} options={descriptors?.types.map(type => ({ value: type.name, label: type.displayName || type.name })) ?? []} />
        <FilterSelect label="Store" value={storeFilter} onChange={setStoreFilter} options={descriptors?.stores.map(store => ({ value: store.name, label: store.displayName || store.name })) ?? []} />
        <FilterSelect label="Scope" value={scopeFilter} onChange={setScopeFilter} options={scopes.map(scope => ({ value: scope, label: scope }))} />
        <FilterSelect label="Status" value={statusFilter} onChange={setStatusFilter} options={secretStatuses.map(value => ({ value, label: value }))} />
        <button type="button" onClick={() => void load()}>Search</button>
      </section>

      {error ? <p className="secrets-alert"><AlertCircle size={16} /> {error}</p> : null}
      {status ? <p className="secrets-status"><CheckCircle2 size={16} /> {status}</p> : null}

      <section className="secrets-layout">
        <div className="secrets-table-shell">
          <table className="secrets-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Display name</th>
                <th>Type</th>
                <th>Store</th>
                <th>Scope</th>
                <th>Status</th>
                <th>Version</th>
                <th>Updated</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={9} className="secrets-empty">Loading secrets...</td></tr>
              ) : null}
              {!loading && filteredSecrets.length === 0 ? (
                <tr><td colSpan={9} className="secrets-empty">No secrets found.</td></tr>
              ) : null}
              {!loading && filteredSecrets.map(secret => (
                <tr key={secret.name} className={secret.name === selected?.name ? "active" : ""}>
                  <td><button type="button" className="secrets-row-link" onClick={() => setSelectedName(secret.name)}>{secret.name}</button></td>
                  <td>{secret.displayName || "-"}</td>
                  <td>{labelType(descriptors, secret.typeName)}</td>
                  <td>{labelStore(descriptors, secret.storeName)}</td>
                  <td>{secret.scope || "-"}</td>
                  <td><StatusBadge status={secret.status} /></td>
                  <td>{secret.currentVersion ?? "-"}</td>
                  <td>{formatDate(secret.updatedAt ?? secret.createdAt)}</td>
                  <td>
                    <div className="secrets-row-actions">
                      <button type="button" title="Open" aria-label={`Open ${secret.name}`} onClick={() => setSelectedName(secret.name)}><Pencil size={14} /></button>
                      <button type="button" title="Rotate" aria-label={`Rotate ${secret.name}`} disabled={secret.status === "Deleted" || busyName === secret.name} onClick={() => rotateSelected(secret)}><RotateCcw size={14} /></button>
                      <button type="button" title="Test" aria-label={`Test ${secret.name}`} disabled={busyName === secret.name || secret.status === "Deleted"} onClick={() => void runTest(secret)}><ShieldCheck size={14} /></button>
                      <button type="button" title="Revoke" aria-label={`Revoke ${secret.name}`} disabled={busyName === secret.name || secret.status === "Revoked" || secret.status === "Deleted"} onClick={() => void revoke(secret)}><Ban size={14} /></button>
                      <button type="button" className="danger" title="Delete" aria-label={`Delete ${secret.name}`} disabled={busyName === secret.name || secret.status === "Deleted"} onClick={() => void remove(secret)}><Trash2 size={14} /></button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <SecretDetail
          secret={selected}
          descriptors={descriptors}
          busy={selected ? busyName === selected.name : false}
          onSave={saveMetadata}
          onRotate={rotateSelected}
          onTest={secret => void runTest(secret)}
          onRevoke={secret => void revoke(secret)}
          onDelete={secret => void remove(secret)}
        />
      </section>

      {dialog === "create" && descriptors ? (
        <CreateSecretDialog
          descriptors={descriptors}
          onCancel={() => setDialog(null)}
          onSave={saveCreatedSecret}
        />
      ) : null}
      {dialog === "rotate" && selected ? (
        <RotateSecretDialog
          secret={selected}
          descriptors={descriptors}
          onCancel={() => setDialog(null)}
          onSave={request => saveRotation(selected.name, request)}
        />
      ) : null}
    </main>
  );
}

function FilterSelect({ label, value, options, onChange }: { label: string; value: string; options: Array<{ value: string; label: string }>; onChange(value: string): void }) {
  return (
    <label className="secrets-filter">
      <span>{label}</span>
      <select value={value} onChange={event => onChange(event.target.value)}>
        <option value="">All</option>
        {options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select>
    </label>
  );
}

function StatusBadge({ status }: { status: SecretStatus }) {
  return <span className={`secrets-badge ${status.toLowerCase()}`}>{status}</span>;
}

function labelType(descriptors: SecretDescriptorsResponse | null, typeName: string) {
  return descriptors?.types.find(type => type.name === typeName)?.displayName ?? typeName;
}

function labelStore(descriptors: SecretDescriptorsResponse | null, storeName: string) {
  return descriptors?.stores.find(store => store.name === storeName)?.displayName ?? storeName;
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleString() : "-";
}
