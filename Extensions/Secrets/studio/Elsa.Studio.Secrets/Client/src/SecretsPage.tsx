import React, { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { AlertCircle, Ban, CheckCircle2, KeyRound, Pencil, Plus, RefreshCw, RotateCcw, Search, ShieldCheck, Trash2, X } from "lucide-react";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createSecret, deleteSecret, getSecretDescriptors, listSecrets, revokeSecret, rotateSecret, testSecret, updateSecret } from "./secretsApi";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata, SecretStatus, UpdateSecretRequest } from "./secretTypes";
import { CopySecretNameButton } from "./CopySecretNameButton";
import { SecretDetail } from "./SecretDetail";
import { CreateSecretDialog, RotateSecretDialog } from "./SecretDialogs";

const secretStatuses: SecretStatus[] = ["Active", "Retired", "Expired", "Revoked", "Deleted"];

export function SecretsPage({ context, dialogs }: { context: StudioEndpointContext; dialogs: StudioDialogApi }) {
  const [search, setSearch] = useState("");
  const [completedSearch, setCompletedSearch] = useState("");
  const [secrets, setSecrets] = useState<SecretMetadata[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [descriptors, setDescriptors] = useState<SecretDescriptorsResponse | null>(null);
  const [dialog, setDialog] = useState<{ kind: "create" } | { kind: "rotate" | "edit"; name: string } | null>(null);
  const [typeFilter, setTypeFilter] = useState("");
  const [storeFilter, setStoreFilter] = useState("");
  const [scopeFilter, setScopeFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [descriptorsLoading, setDescriptorsLoading] = useState(true);
  const [descriptorError, setDescriptorError] = useState(false);
  const [busyName, setBusyName] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const descriptorRequestSequence = useRef(0);
  const secretsRequestSequence = useRef(0);

  const scopes = useMemo(() => Array.from(new Set(secrets.map(secret => secret.scope).filter((scope): scope is string => !!scope))).sort(), [secrets]);
  const filteredSecrets = useMemo(() => {
    return secrets.filter(secret => {
      if (typeFilter && secret.typeName !== typeFilter) return false;
      if (storeFilter && secret.storeName !== storeFilter) return false;
      if (scopeFilter && (secret.scope ?? "") !== scopeFilter) return false;
      if (statusFilter && secret.status !== statusFilter) return false;
      return true;
    });
  }, [scopeFilter, secrets, statusFilter, storeFilter, typeFilter]);
  const dialogSecret = dialog?.kind === "edit" || dialog?.kind === "rotate"
    ? secrets.find(secret => secret.name === dialog.name) ?? null
    : null;
  const hasLocalFilters = Boolean(typeFilter || storeFilter || scopeFilter || statusFilter);
  const hasActiveFilters = Boolean(completedSearch.trim() || hasLocalFilters);
  const partialResult = totalCount > secrets.length;
  const emptyTitle = hasLocalFilters
    ? "No loaded secrets match your filters"
    : completedSearch.trim()
      ? `No results for “${completedSearch}”`
      : "No secrets yet";
  const emptyDescription = hasLocalFilters
    ? "Local filters only cover loaded secrets. Try adjusting them or clear the filters."
    : completedSearch.trim()
      ? "Try another name or display name, or clear the search."
      : "Create a secret to make it available to workflows.";

  const loadSecrets = useCallback(async (searchTerm: string) => {
    const requestSequence = ++secretsRequestSequence.current;
    setLoading(true);
    setLoadError(false);
    setError(null);
    try {
      const list = await listSecrets(context, { search: searchTerm, activeOnly: false, pageSize: 100 });
      if (requestSequence !== secretsRequestSequence.current) return;
      setSecrets(list.items);
      setTotalCount(list.totalCount);
      setCompletedSearch(searchTerm);
      setDialog(current => {
        if (current?.kind === "edit" && !list.items.some(secret => secret.name === current.name)) return null;
        if (current?.kind === "rotate" && !list.items.some(secret => secret.name === current.name)) return null;
        return current;
      });
    } catch {
      if (requestSequence !== secretsRequestSequence.current) return;
      setLoadError(true);
      setError(null);
      setSecrets([]);
      setDialog(current => current?.kind === "create" ? current : null);
    } finally {
      if (requestSequence === secretsRequestSequence.current) setLoading(false);
    }
  }, [context]);

  const loadDescriptors = useCallback(async () => {
    const requestSequence = ++descriptorRequestSequence.current;
    setDescriptorsLoading(true);
    try {
      const response = await getSecretDescriptors(context);
      if (requestSequence !== descriptorRequestSequence.current) return;
      setDescriptors(response);
      setDescriptorError(false);
    } catch {
      if (requestSequence !== descriptorRequestSequence.current) return;
      setDescriptorError(true);
    } finally {
      if (requestSequence === descriptorRequestSequence.current) setDescriptorsLoading(false);
    }
  }, [context]);

  const load = useCallback(async (searchTerm: string) => {
    await Promise.all([loadSecrets(searchTerm), loadDescriptors()]);
  }, [loadDescriptors, loadSecrets]);

  useEffect(() => {
    const descriptorSequence = descriptorRequestSequence;
    const secretsSequence = secretsRequestSequence;
    void load("");
    return () => {
      descriptorSequence.current++;
      secretsSequence.current++;
    };
  }, [load]);

  async function refreshWithStatus(message: string) {
    await load(search);
    setStatus(message);
  }

  async function saveMetadata(name: string, request: UpdateSecretRequest) {
    setBusyName(name);
    setError(null);
    setStatus(null);
    try {
      await updateSecret(context, name, request);
      await refreshWithStatus("Secret metadata updated.");
    } finally {
      setBusyName(null);
    }
  }

  async function saveCreatedSecret(request: CreateSecretRequest) {
    setError(null);
    setStatus(null);
    await createSecret(context, request);
    await refreshWithStatus("Secret created.");
  }

  async function saveRotation(name: string, request: RotateSecretRequest) {
    setBusyName(name);
    setError(null);
    setStatus(null);
    try {
      await rotateSecret(context, name, request);
      await refreshWithStatus("Secret rotated.");
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
      setDialog(current => current?.kind === "edit" && current.name === secret.name ? null : current);
      await refreshWithStatus("Secret deleted.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secret could not be deleted.");
    } finally {
      setBusyName(null);
    }
  }

  const rotateSelected = (secret: SecretMetadata) => {
    setDialog({ kind: "rotate", name: secret.name });
  };

  const clearFilters = () => {
    setSearch("");
    setTypeFilter("");
    setStoreFilter("");
    setScopeFilter("");
    setStatusFilter("");
    void load("");
  };

  return (
    <main className="secrets-shell">
      <header className="secrets-header">
        <div>
          <span className="secrets-kicker">Secret management</span>
          <h2><KeyRound size={18} aria-hidden /> Secrets</h2>
        </div>
        {!loading && !loadError ? (
          <p className="secrets-count" aria-live="polite">
            {filteredSecrets.length} shown · {secrets.length} loaded · {totalCount} {totalCount === 1 ? "result" : "results"}{completedSearch.trim() ? ` for “${completedSearch}”` : ""}
          </p>
        ) : null}
      </header>

      <section className="secrets-toolbar" aria-label="Secret filters">
        <div className="secrets-search-wrap">
          <label className="secrets-search">
            <Search size={15} aria-hidden />
            <input aria-label="Search secrets" value={search} onChange={event => setSearch(event.target.value)} onKeyDown={event => event.key === "Enter" && void load(search)} placeholder="Search secrets" />
          </label>
          <button type="button" className="secrets-search-action" onClick={() => void load(search)}>Search</button>
        </div>
        <FilterSelect label="Type" value={typeFilter} onChange={setTypeFilter} options={descriptors?.types.map(type => ({ value: type.name, label: type.displayName || type.name })) ?? []} />
        <FilterSelect label="Store" value={storeFilter} onChange={setStoreFilter} options={descriptors?.stores.map(store => ({ value: store.name, label: store.displayName || store.name })) ?? []} />
        <FilterSelect
          label="Scope"
          value={scopeFilter}
          onChange={setScopeFilter}
          options={scopes.map(scope => ({ value: scope, label: scope }))}
          helpText="Scopes in loaded results"
          description="The list loads up to 100 secrets, so other scopes may exist."
        />
        <FilterSelect label="Status" value={statusFilter} onChange={setStatusFilter} options={secretStatuses.map(value => ({ value, label: value }))} />
        <div className="secrets-toolbar-actions">
          <button type="button" onClick={() => void load(search)} disabled={loading || descriptorsLoading}><RefreshCw size={15} aria-hidden /> Refresh</button>
          <button type="button" onClick={() => setDialog({ kind: "create" })} disabled={!descriptors}><Plus size={15} aria-hidden /> Create</button>
        </div>
      </section>

      {descriptorError && dialog?.kind !== "create" ? (
        <p className="secrets-field-error" role="alert">
          Secret options could not be refreshed.{descriptors ? " Previously loaded options remain available." : " Create a secret after retrying."}
          {" "}<button type="button" onClick={() => void loadDescriptors()} disabled={descriptorsLoading}>Retry secret options</button>
        </p>
      ) : null}
      {error ? <SecretNotification message={error} error onDismiss={() => setError(null)} /> : null}
      {status ? <SecretNotification message={status} onDismiss={() => setStatus(null)} /> : null}
      {!loading && !loadError && partialResult ? (
        <p className="secrets-coverage">
          Only the first 100 results are loaded. Search by name or display name to find other secrets. Filters apply to loaded results.
        </p>
      ) : null}

      <section className="secrets-layout">
        <div className="secrets-table-shell">
          <table className="secrets-table" aria-label="Secrets">
            <thead>
              <tr>
                <th>Name</th>
                <th>Type</th>
                <th>Store</th>
                <th>Scope</th>
                <th>Status</th>
                <th>Updated</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={7} className="secrets-empty"><span className="secrets-loading" role="status">Loading secrets…</span></td></tr>
              ) : null}
              {!loading && loadError ? (
                <tr><td colSpan={7} className="secrets-empty">
                  <div className="secrets-empty-state" role="alert">
                    <span className="secrets-empty-icon" aria-hidden><AlertCircle size={20} /></span>
                    <strong>Secrets could not be loaded.</strong>
                    <p>Check your connection and try again.</p>
                    <button type="button" onClick={() => void load(search)}><RefreshCw size={14} aria-hidden /> Retry</button>
                  </div>
                </td></tr>
              ) : null}
              {!loading && !loadError && filteredSecrets.length === 0 ? (
                <tr><td colSpan={7} className="secrets-empty">
                  <div className="secrets-empty-state" role="status">
                    <span className="secrets-empty-icon" aria-hidden><KeyRound size={20} /></span>
                    <strong>{emptyTitle}</strong>
                    <p>{emptyDescription}</p>
                    {hasActiveFilters ? <button type="button" onClick={clearFilters}>Clear filters</button>
                      : <button type="button" className="secrets-primary" onClick={() => setDialog({ kind: "create" })} disabled={!descriptors}><Plus size={14} aria-hidden /> Create secret</button>}
                  </div>
                </td></tr>
              ) : null}
              {!loading && filteredSecrets.map(secret => (
                <tr key={secret.name}>
                  <td className="secrets-name-cell">
                    <div className="secrets-name-heading">
                      <button type="button" className="secrets-row-link" data-secret-open={secret.name} onClick={() => setDialog({ kind: "edit", name: secret.name })}><strong>{secret.displayName || secret.name}</strong></button>
                      {!secret.displayName || secret.displayName === secret.name ? <CopySecretNameButton name={secret.name} /> : null}
                    </div>
                    {secret.displayName && secret.displayName !== secret.name ? <div className="secrets-technical-name"><code>{secret.name}</code><CopySecretNameButton name={secret.name} /></div> : null}
                  </td>
                  <td>{labelType(descriptors, secret.typeName)}</td>
                  <td>{labelStore(descriptors, secret.storeName)}</td>
                  <td>{secret.scope || "-"}</td>
                  <td><StatusBadge status={secret.status} /></td>
                  <td>{formatDate(secret.updatedAt ?? secret.createdAt)}</td>
                  <td>
                    <div className="secrets-row-actions">
                      <button type="button" className="secrets-edit-action" data-secret-edit={secret.name} aria-label={`Edit ${secret.name}`} title={`Edit ${secret.displayName || secret.name}`} onClick={() => setDialog({ kind: "edit", name: secret.name })}><Pencil size={14} aria-hidden /> Edit</button>
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

        {dialog?.kind === "edit" && dialogSecret ? <SecretDetail
          key={dialogSecret.name}
          secret={dialogSecret}
          descriptors={descriptors}
          busy={busyName === dialogSecret.name}
          onSave={saveMetadata}
          onClose={() => setDialog(null)}
        /> : null}
      </section>

      {dialog?.kind === "create" && descriptors ? (
        <CreateSecretDialog
          descriptors={descriptors}
          scopeCatalogLoading={descriptorsLoading}
          scopeCatalogError={descriptorError}
          onRetryScopeCatalog={() => void loadDescriptors()}
          onCancel={() => setDialog(null)}
          onSave={saveCreatedSecret}
        />
      ) : null}
      {dialog?.kind === "rotate" && dialogSecret ? (
        <RotateSecretDialog
          key={dialogSecret.id}
          secret={dialogSecret}
          descriptors={descriptors}
          onCancel={() => setDialog(null)}
          onSave={request => saveRotation(dialogSecret.name, request)}
        />
      ) : null}
    </main>
  );
}

function SecretNotification({ message, error = false, onDismiss }: { message: string; error?: boolean; onDismiss(): void }) {
  const Icon = error ? AlertCircle : CheckCircle2;
  return (
    <div className={`secrets-notification ${error ? "secrets-alert" : "secrets-status"}`}>
      <div className="secrets-notification-message" role={error ? "alert" : "status"}>
        <Icon size={16} aria-hidden />
        <span>{message}</span>
      </div>
      <button type="button" className="secrets-notification-close" aria-label={error ? "Dismiss error" : "Dismiss notification"} title="Dismiss" onClick={onDismiss}>
        <X size={16} aria-hidden />
      </button>
    </div>
  );
}

function FilterSelect({ label, value, options, helpText, description, onChange }: { label: string; value: string; options: Array<{ value: string; label: string }>; helpText?: string; description?: string; onChange(value: string): void }) {
  const id = useId();
  const helpId = helpText ? `${id}-help` : undefined;
  const descriptionId = description ? `${id}-description` : undefined;
  return (
    <div className="secrets-filter">
      <div className="secrets-filter-heading">
        <label htmlFor={id}>{label}</label>
        {helpText ? <small className="secrets-filter-hint" id={helpId}>{helpText}</small> : null}
      </div>
      <select id={id} value={value} aria-describedby={[helpId, descriptionId].filter(Boolean).join(" ") || undefined} onChange={event => onChange(event.target.value)}>
        <option value="">All</option>
        {options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select>
      {description ? <span className="secrets-visually-hidden" id={descriptionId}>{description}</span> : null}
    </div>
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
