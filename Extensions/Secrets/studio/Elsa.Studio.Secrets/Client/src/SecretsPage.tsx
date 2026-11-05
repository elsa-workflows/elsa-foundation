import React, { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { AlertCircle, KeyRound, Plus, RefreshCw, X } from "lucide-react";
import type { StudioDialogApi, StudioEndpointContext, StudioNotificationApi } from "@elsa-workflows/studio-sdk";
import { OverflowMenu, StudioButton, StudioSearchInput, type OverflowMenuItem , StudioPage, StudioPageHeader, StudioCollectionFooter, StudioCollectionToolbar, StudioTableContainer, StudioResultState} from "@elsa-workflows/studio-ui";
import { createSecret, deleteSecret, getSecretDescriptors, listSecrets, revokeSecret, rotateSecret, testSecret, updateSecret } from "./secretsApi";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata, SecretStatus, UpdateSecretRequest } from "./secretTypes";
import { CopySecretNameButton } from "./CopySecretNameButton";
import { SecretDetail } from "./SecretDetail";
import { CreateSecretDialog, RotateSecretDialog } from "./SecretDialogs";

const secretStatuses: SecretStatus[] = ["Active", "Retired", "Expired", "Revoked", "Deleted"];
const MAX_SECRETS_PER_LOAD = 100;

export function SecretsPage({ context, dialogs, notifications }: { context: StudioEndpointContext; dialogs: StudioDialogApi; notifications?: StudioNotificationApi }) {
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
  const [error, setError] = useState<string | null>(null);
  const descriptorRequestSequence = useRef(0);
  const secretsRequestSequence = useRef(0);
  const attemptedSearch = useRef("");
  const operationScope = useMemo(() => ({ context, active: false, generation: 0 }), [context]);
  const currentScope = useRef(operationScope);
  currentScope.current = operationScope;
  const busyScope = useRef(operationScope);
  useLayoutEffect(() => {
    operationScope.active = true;
    if (busyScope.current !== operationScope) { busyScope.current = operationScope; setBusyName(null); }
    return () => { operationScope.active = false; operationScope.generation++; };
  }, [operationScope]);
  const captureOperation = () => {
    const generation = operationScope.generation;
    return Object.assign(
      () => currentScope.current === operationScope && operationScope.active && operationScope.generation === generation,
      { isOwned: () => currentScope.current === operationScope }
    );
  };

  const scopes = useMemo(() => Array.from(new Set(secrets.map(secret => secret.scope).filter((scope): scope is string => !!scope))).sort(), [secrets]);
  const scopeOptions = useMemo(() => {
    const options = scopes.map(scope => ({ value: scope, label: scope }));
    if (scopeFilter && !scopes.includes(scopeFilter)) {
      options.push({ value: scopeFilter, label: `${scopeFilter} (not in loaded results)` });
    }
    return options;
  }, [scopeFilter, scopes]);
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
  const noSearchMatches = Boolean(completedSearch.trim()) && totalCount === 0;
  const localFilterEmpty = !noSearchMatches && hasLocalFilters && secrets.length > 0 && filteredSecrets.length === 0;
  const emptyTitle = localFilterEmpty
    ? "No loaded secrets match your filters"
    : completedSearch.trim()
      ? `No results for “${completedSearch}”`
      : "No secrets yet";
  const emptyDescription = localFilterEmpty
    ? "Local filters only cover loaded secrets. Try adjusting them or clear the filters."
    : completedSearch.trim()
      ? "Try another name or display name, or clear the search."
      : "Create a secret to make it available to workflows.";

  const loadSecrets = useCallback(async (searchTerm: string) => {
    const requestSequence = ++secretsRequestSequence.current;
    attemptedSearch.current = searchTerm;
    setLoading(true);
    setLoadError(false);
    setError(null);
    try {
      const list = await listSecrets(context, { search: searchTerm, activeOnly: false, pageSize: MAX_SECRETS_PER_LOAD });
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
      attemptedSearch.current = "";
    };
  }, [load]);

  async function refreshWithConfirmation(message: string, isCurrent: ReturnType<typeof captureOperation>) {
    if (!isCurrent.isOwned()) return;
    if (isCurrent()) notifications?.notify({ level: "success", title: message });
    await load(attemptedSearch.current);
  }

  async function saveMetadata(name: string, request: UpdateSecretRequest) {
    const isCurrent = captureOperation();
    setBusyName(name);
    setError(null);
    try {
      await updateSecret(context, name, request);
      await refreshWithConfirmation("Secret metadata updated.", isCurrent);
    } finally {
      if (isCurrent.isOwned()) setBusyName(null);
    }
  }

  async function saveCreatedSecret(request: CreateSecretRequest) {
    const isCurrent = captureOperation();
    setError(null);
    await createSecret(context, request);
    await refreshWithConfirmation("Secret created.", isCurrent);
  }

  async function saveRotation(name: string, request: RotateSecretRequest) {
    const isCurrent = captureOperation();
    setBusyName(name);
    setError(null);
    try {
      await rotateSecret(context, name, request);
      await refreshWithConfirmation("Secret rotated.", isCurrent);
    } finally {
      if (isCurrent.isOwned()) setBusyName(null);
    }
  }

  async function runTest(secret: SecretMetadata) {
    const isCurrent = captureOperation();
    setBusyName(secret.name);
    setError(null);
    try {
      const result = await testSecret(context, secret.name);
      const summary = result.message?.trim() || (result.succeeded ? "Secret resolved successfully." : "Secret test failed.");
      if (!isCurrent.isOwned()) return;
      if (result.succeeded && isCurrent()) notifications?.notify({ level: "success", title: "Secret test succeeded.", message: `(${result.code}): ${summary}` });
      else if (!result.succeeded) setError(`Test failed (${result.code}): ${summary}`);
    } catch (e) {
      if (isCurrent.isOwned()) setError(e instanceof Error ? e.message : "Secret test failed.");
    } finally {
      if (isCurrent.isOwned()) setBusyName(null);
    }
  }

  async function revoke(secret: SecretMetadata) {
    const isCurrent = captureOperation();
    if (secret.status === "Revoked" || secret.status === "Deleted") return;
    if (!(await dialogs.confirm({ message: `Revoke secret "${secret.name}"? Existing references keep the name, but the secret can no longer be used as active material.`, confirmLabel: "Revoke", tone: "danger" })) || !isCurrent.isOwned()) return;
    setBusyName(secret.name);
    setError(null);
    try {
      await revokeSecret(context, secret.name);
      await refreshWithConfirmation("Secret revoked.", isCurrent);
    } catch (e) {
      if (isCurrent.isOwned()) setError(e instanceof Error ? e.message : "Secret could not be revoked.");
    } finally {
      if (isCurrent.isOwned()) setBusyName(null);
    }
  }

  async function remove(secret: SecretMetadata) {
    const isCurrent = captureOperation();
    if (!(await dialogs.confirm({ message: `Delete secret "${secret.name}"? This removes the metadata record and cannot reveal or recover its value.`, confirmLabel: "Delete", tone: "danger" })) || !isCurrent.isOwned()) return;
    setBusyName(secret.name);
    setError(null);
    try {
      await deleteSecret(context, secret.name);
      if (!isCurrent.isOwned()) return;
      setDialog(current => current?.kind === "edit" && current.name === secret.name ? null : current);
      await refreshWithConfirmation("Secret deleted.", isCurrent);
    } catch (e) {
      if (isCurrent.isOwned()) setError(e instanceof Error ? e.message : "Secret could not be deleted.");
    } finally {
      if (isCurrent.isOwned()) setBusyName(null);
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
    <StudioPage className="secrets-shell">
      <StudioPageHeader title="Secrets" headingLevel={2} description="Manage secret metadata; existing values stay masked." actions={          <div className="studio-page-actions" role="group" aria-label="Secret page actions">
            <StudioButton variant="primary" size="md" aria-label="Create secret" onClick={() => setDialog({ kind: "create" })} disabled={!descriptors}><Plus size={15} aria-hidden /> Create secret</StudioButton>
            <StudioButton variant="default" size="md" onClick={() => void load(completedSearch)} disabled={loading || descriptorsLoading} aria-label="Refresh secrets"><RefreshCw size={15} aria-hidden /> Refresh</StudioButton>
          </div>} />

      <StudioCollectionToolbar className="secrets-toolbar" aria-label="Secret filters">
        <form className="secrets-search-wrap" role="search" aria-label="Secrets" onSubmit={event => { event.preventDefault(); void load(search); }}>
          <StudioSearchInput className="secrets-search-control" aria-label="Search secrets" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search secrets" />
          <StudioButton type="submit" className="secrets-search-action" size="md">Search</StudioButton>
        </form>
        <FilterSelect label="Type" value={typeFilter} onChange={setTypeFilter} options={descriptors?.types.map(type => ({ value: type.name, label: type.displayName || type.name })) ?? []} />
        <FilterSelect label="Store" value={storeFilter} onChange={setStoreFilter} options={descriptors?.stores.map(store => ({ value: store.name, label: store.displayName || store.name })) ?? []} />
        <FilterSelect
          label="Scope"
          value={scopeFilter}
          onChange={setScopeFilter}
          options={scopeOptions}
          helpText="Scopes in loaded results"
          description={`The list loads up to ${MAX_SECRETS_PER_LOAD} secrets, so other scopes may exist.`}
        />
        <FilterSelect label="Status" value={statusFilter} onChange={setStatusFilter} options={secretStatuses.map(value => ({ value, label: value }))} />
      </StudioCollectionToolbar>

      {descriptorError && dialog?.kind !== "create" ? (
        <p className="secrets-field-error" role="alert">
          Secret options could not be refreshed.{descriptors ? " Previously loaded options remain available." : " Create a secret after retrying."}
          {" "}<button type="button" onClick={() => void loadDescriptors()} disabled={descriptorsLoading}>Retry secret options</button>
        </p>
      ) : null}
      {error ? <SecretNotification message={error} onDismiss={() => setError(null)} /> : null}
      {!loading && !loadError && partialResult ? (
        <p className="secrets-coverage">
          Only the first {MAX_SECRETS_PER_LOAD} results are loaded. Search by name or display name to find other secrets. Filters apply to loaded results.
        </p>
      ) : null}

      <section className="secrets-layout">
        <StudioTableContainer>
          <table className="studio-table secrets-table" aria-label="Secrets">
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
                  <StudioResultState headingLevel={3} tone="danger" title="Secrets could not be loaded." description="Check your connection and try again." icon={<AlertCircle size={20} />} actions={<StudioButton onClick={() => void load(attemptedSearch.current)}><RefreshCw size={14} aria-hidden /> Retry</StudioButton>} />
                </td></tr>
              ) : null}
              {!loading && !loadError && filteredSecrets.length === 0 ? (
                <tr><td colSpan={7} className="secrets-empty">
                  <StudioResultState headingLevel={3} title={emptyTitle} description={emptyDescription} icon={<KeyRound size={20} />} actions={hasActiveFilters ? <StudioButton onClick={clearFilters}>Clear filters</StudioButton> : <StudioButton variant="primary" onClick={() => setDialog({ kind: "create" })} disabled={!descriptors}><Plus size={14} aria-hidden /> Create secret</StudioButton>} />
                </td></tr>
              ) : null}
              {!loading && filteredSecrets.map(secret => (
                <tr key={secret.name}>
                  <td className="secrets-name-cell">
                    <div className="secrets-name-heading">
                      <button type="button" className="secrets-row-link" data-secret-open={secret.name} onClick={() => setDialog({ kind: "edit", name: secret.name })}><strong>{secret.displayName || secret.name}</strong></button>
                      {!secret.displayName || secret.displayName === secret.name ? <CopySecretNameButton name={secret.name} notifications={notifications} scope={context} /> : null}
                    </div>
                    {secret.displayName && secret.displayName !== secret.name ? <div className="secrets-technical-name"><code>{secret.name}</code><CopySecretNameButton name={secret.name} notifications={notifications} scope={context} /></div> : null}
                  </td>
                  <td>{labelType(descriptors, secret.typeName)}</td>
                  <td>{labelStore(descriptors, secret.storeName)}</td>
                  <td>{secret.scope || "-"}</td>
                  <td><StatusBadge status={secret.status} /></td>
                  <td>{formatDate(secret.updatedAt ?? secret.createdAt)}</td>
                  <td>
                    <div className="secrets-row-actions">
                      <StudioButton variant="default" size="sm" className="secrets-edit-action" data-secret-edit={secret.name} aria-label={`Edit ${secret.name}`} title={`Edit ${secret.displayName || secret.name}`} onClick={() => setDialog({ kind: "edit", name: secret.name })}>Edit</StudioButton>
                      <OverflowMenu
                        ariaLabel={`Actions for ${secret.name}`}
                        items={[
                          { id: "rotate", label: "Rotate", disabled: secret.status === "Deleted" || busyName === secret.name, onSelect: () => rotateSelected(secret) },
                          { id: "test", label: "Test", disabled: busyName === secret.name || secret.status === "Deleted", onSelect: () => void runTest(secret) },
                          { id: "revoke", label: "Revoke", disabled: busyName === secret.name || secret.status === "Revoked" || secret.status === "Deleted", destructive: true, onSelect: () => void revoke(secret) },
                          { id: "delete", label: "Delete", disabled: busyName === secret.name || secret.status === "Deleted", destructive: true, onSelect: () => void remove(secret) }
                        ] satisfies OverflowMenuItem[]}
                      />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </StudioTableContainer>

        {dialog?.kind === "edit" && dialogSecret ? <SecretDetail
          copyScope={context}
          notifications={notifications}
          key={dialogSecret.name}
          secret={dialogSecret}
          descriptors={descriptors}
          busy={busyName === dialogSecret.name}
          onSave={saveMetadata}
          onClose={() => setDialog(null)}
        /> : null}
      </section>

      <StudioCollectionFooter>{!loading && !loadError ? (
            <p className="secrets-count" aria-live="polite">
              {filteredSecrets.length} shown · {secrets.length} loaded · {totalCount} {totalCount === 1 ? "result" : "results"}{completedSearch.trim() ? ` for “${completedSearch}”` : ""}
            </p>
          ) : null}</StudioCollectionFooter>

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
    </StudioPage>
  );
}

function SecretNotification({ message, onDismiss }: { message: string; onDismiss(): void }) {
  return (
    <div className="secrets-notification secrets-alert">
      <div className="secrets-notification-message" role="alert">
        <AlertCircle size={16} aria-hidden />
        <span>{message}</span>
      </div>
      <button type="button" className="secrets-notification-close" aria-label="Dismiss error" title="Dismiss" onClick={onDismiss}>
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
