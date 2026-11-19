import React, { useCallback, useEffect, useId, useRef, useState } from "react";
import { Bot, Plus, RefreshCw } from "lucide-react";
import { StudioHttpError, useOptionalAuthContext, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  SkeletonRows, StatusChip, StudioAlert, StudioButton, StudioCollectionFooter, StudioCollectionToolbar, StudioDataGrid,
  StudioField, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, type StudioDataGridColumn
} from "@elsa-workflows/studio-ui";
import {
  createAgent, getAgentAuthoring, listActiveAgentDrafts, listAgents, agentProviderKey, type AgentRow, type AgentAuthoring
} from "./agentsApi";
import { continuitySchemaVersion, initialAgentManifest } from "./agentManifest";

const pageSize = 25;

/** The activity design API writes enum values in camelCase; compare them without regard to case, as the generic client does. */
const isEnumValue = (value: string | null | undefined, expected: string) => value?.toLowerCase() === expected.toLowerCase();

type Collection =
  | { kind: "loading" }
  | { kind: "denied" }
  | { kind: "error" }
  | { kind: "ready"; items: AgentRow[]; totalCount: number; continuation: string | null; loadingMore: boolean; moreFailed: boolean; restart: Restart };

/** Why a continuation cannot be used: its retained snapshot expired, or it no longer matches this filter and authorization. */
type Restart = "expired" | "changed" | null;

// Creating a definition needs the Activities Design manage permission; the server stays authoritative.
const createPermissions = ["*", "activities/design:manage"];

/** The server answers an expired snapshot with 410, and a cursor from another filter or authorization with 400 cursor-invalid. */
function restartReason(error: unknown): Restart {
  if (!(error instanceof StudioHttpError)) return null;
  if (error.status === 410) return "expired";
  const problem = error.payload && typeof error.payload === "object" ? error.payload as { errorCode?: unknown } : {};
  return error.status === 400 && problem.errorCode === "activity.management.cursor-invalid" ? "changed" : null;
}

/** The row's resource, resolved explicitly: its published version, else its only active draft, else the definition to choose from. */
export function agentRowPath(row: AgentRow, drafts: string[] | null) {
  const published = publishedVersion(row);
  if (published) return activityDefinitionVersionPath(row.definition.definitionId, published.versionId);
  if (drafts?.length === 1) return activityDefinitionPath(row.definition.definitionId, drafts[0]);
  // Several drafts, or a lookup that could not answer: the definition's drafts, where the author chooses explicitly.
  return `/workflows/activity-definitions?${new URLSearchParams({ definition: row.definition.definitionId, section: "drafts" }).toString()}`;
}

function publishedVersion(row: AgentRow) {
  // The server matches a definition by its draft, head or recommended provider. Only a matching version is the agent's
  // publication: the recommendation first, then the head, with its actual lifecycle.
  const { head, recommendation } = row.lifecycle;
  return [recommendation, head].find(version => version?.providerKey === agentProviderKey) ?? null;
}

export function activityDefinitionVersionPath(definitionId: string, versionId: string) {
  return `/workflows/activity-definitions?${new URLSearchParams({ definition: definitionId, version: versionId }).toString()}`;
}

/** Where the generic activity editor opens one definition, or one of its drafts. */
export function activityDefinitionPath(definitionId: string, draftId?: string) {
  const parameters = new URLSearchParams({ definition: definitionId });
  if (draftId) { parameters.set("section", "editor"); parameters.set("draft", draftId); }
  return `/workflows/activity-definitions?${parameters.toString()}`;
}

/**
 * The module-owned Agents collection over the generic activity definitions API. The server filters by the agent
 * provider, searches, counts and pages; nothing is filtered after paging. Editing, publication and history open the
 * generic activity editor, which stays the authority for revisions and permissions.
 */
export function AgentsPage({ context, navigate }: { context: StudioEndpointContext; navigate(path: string): void }) {
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [collection, setCollection] = useState<Collection>({ kind: "loading" });
  const [authoring, setAuthoring] = useState<AgentAuthoring | null | "loading">("loading");
  const [creating, setCreating] = useState(false);
  const newAgentRef = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false);
  const countRef = useRef<HTMLParagraphElement>(null);
  // The reload whose outcome takes focus, by its sequence number: its result, or Retry when it fails. Any later load cancels it.
  const focusAfterLoad = useRef(0);
  const retryRef = useRef<HTMLButtonElement>(null);
  const resultRef = useRef<HTMLDivElement>(null);
  const opening = useRef<AbortController | null>(null);
  useEffect(() => () => opening.current?.abort(), []);
  const sequence = useRef(0);
  const session = useOptionalAuthContext()?.session;
  const mayCreate = session?.status !== "authenticated" || createPermissions.some(permission => session.permissions.includes(permission));

  useEffect(() => {
    if (!creating && restoreFocus.current) { restoreFocus.current = false; newAgentRef.current?.focus(); }
  }, [creating]);

  useEffect(() => {
    const handle = window.setTimeout(() => setQuery(search.trim()), 250);
    return () => window.clearTimeout(handle);
  }, [search]);

  const loadFirst = useCallback(async () => {
    const current = ++sequence.current;
    setCollection({ kind: "loading" });
    try {
      const page = await listAgents(context, { limit: pageSize, search: query });
      if (current !== sequence.current) return;
      setCollection({ kind: "ready", items: page.items, totalCount: page.totalCount, continuation: page.hasMore ? page.continuation ?? null : null, loadingMore: false, moreFailed: false, restart: null });
    } catch (error) {
      if (current !== sequence.current) return;
      setCollection({ kind: error instanceof StudioHttpError && (error.status === 401 || error.status === 403) ? "denied" : "error" });
    }
  }, [context, query]);

  useEffect(() => { void loadFirst(); }, [loadFirst]);
  useEffect(() => {
    if (collection.kind === "loading" || focusAfterLoad.current === 0) return;
    // Only focus the control that started the reload would otherwise lose: never take it from where the author moved.
    const lost = !document.activeElement || document.activeElement === document.body;
    if (focusAfterLoad.current === sequence.current && lost)
      (collection.kind === "ready" ? countRef.current : retryRef.current ?? resultRef.current)?.focus();
    focusAfterLoad.current = 0;
  }, [collection]);
  const reload = () => { focusAfterLoad.current = sequence.current + 1; void loadFirst(); };

  // Only the latest activation navigates; a slower lookup for an earlier row, or one outliving the page, is dropped.
  const open = async (row: AgentRow) => {
    opening.current?.abort();
    if (publishedVersion(row)) { opening.current = null; navigate(agentRowPath(row, null)); return; }
    const abort = opening.current = new AbortController();
    let drafts: string[] | null = null;
    try { drafts = await listActiveAgentDrafts(context, row.definition.definitionId, abort.signal); }
    catch { drafts = null; }
    if (abort.signal.aborted) return;
    opening.current = null;
    navigate(agentRowPath(row, drafts));
  };

  useEffect(() => {
    const abort = new AbortController();
    getAgentAuthoring(context, abort.signal)
      .then(setAuthoring)
      .catch(() => { if (!abort.signal.aborted) setAuthoring(null); });
    return () => abort.abort();
  }, [context]);

  const loadMore = async () => {
    if (collection.kind !== "ready" || !collection.continuation || collection.loadingMore) return;
    const current = sequence.current;
    setCollection({ ...collection, loadingMore: true, moreFailed: false, restart: null });
    try {
      const page = await listAgents(context, { limit: pageSize, search: query, cursor: collection.continuation });
      if (current !== sequence.current) return;
      setCollection(previous => previous.kind !== "ready" ? previous : {
        ...previous, items: [...previous.items, ...page.items], totalCount: page.totalCount,
        continuation: page.hasMore ? page.continuation ?? null : null, loadingMore: false
      });
    } catch (error) {
      if (current !== sequence.current) return;
      // A continuation that cannot be used is said so, and the author restarts; snapshots are never mixed or totals silently replaced.
      const restart = restartReason(error);
      setCollection(previous => previous.kind !== "ready" ? previous : { ...previous, loadingMore: false, moreFailed: !restart, restart, continuation: restart ? null : previous.continuation });
    }
  };

  const canCreate = mayCreate && authoring !== "loading" && authoring !== null;
  if (creating && canCreate) return <StudioPage className="agent-agents-page" data-testid="agents-page">
    <CreateAgentForm context={context} authoring={authoring}
      onCancel={() => { restoreFocus.current = true; setCreating(false); }}
      onCreated={(definitionId, draftId) => navigate(activityDefinitionPath(definitionId, draftId))} />
  </StudioPage>;

  const columns: StudioDataGridColumn<AgentRow>[] = [
    { id: "name", header: "Agent", render: row => (
      <span className="agent-row-name">
        <strong>{row.definition.displayName}</strong>
        {row.definition.description ? <small>{row.definition.description}</small> : null}
      </span>
    ) },
    { id: "status", header: "Status", render: row => <AgentStatus row={row} /> },
    { id: "authority", header: "Defined in", render: row => <AgentAuthority row={row} /> },
    { id: "updated", header: "Updated", render: row => <time dateTime={row.updatedAt}>{formatDate(row.updatedAt)}</time> }
  ];

  return (
    <StudioPage className="agent-agents-page" data-testid="agents-page">
      <StudioPageHeader
        title="Agents"
        icon={<Bot aria-hidden="true" />}
        description="Agents you define here or in configuration. Each one is a reusable activity with typed inputs and results that workflows and BPMN processes can use."
        actions={<>{canCreate ? <StudioButton ref={newAgentRef} variant="primary" onClick={() => setCreating(true)}><Plus aria-hidden="true" /> New agent</StudioButton> : null}<StudioButton onClick={reload} disabled={collection.kind === "loading"}><RefreshCw aria-hidden="true" /> Refresh</StudioButton></>} />

      <StudioCollectionToolbar>
        <StudioSearchInput aria-label="Search agents" placeholder="Search agents" value={search} onChange={event => setSearch(event.target.value)} />
      </StudioCollectionToolbar>

      {collection.kind === "loading" ? <SkeletonRows /> : null}
      {collection.kind === "denied" ? <div ref={resultRef} tabIndex={-1} role="region" aria-label="You can't view agents"><StudioResultState title="You can't view agents" description="Your account has no permission to read activity definitions here." /></div> : null}
      {collection.kind === "error" ? (
        <StudioResultState title="Agents are unavailable" description="The server could not list agents."
          actions={<StudioButton ref={retryRef} onClick={reload}><RefreshCw aria-hidden="true" /> Retry</StudioButton>} />
      ) : null}
      {collection.kind === "ready" ? (
        <>
          {collection.items.length === 0 ? <div className="studio-table-container">
            <StudioResultState headingLevel={3} icon={<Bot aria-hidden="true" />}
              title={query ? "No matching agents" : "No agents yet"}
              description={query ? "Try a different name or clear your search." : canCreate ? "Create an agent to use it in workflows." : "Agents will appear here when available."}
              actions={query ? <StudioButton onClick={() => setSearch("")}>Clear search</StudioButton> : canCreate ? <StudioButton variant="primary" onClick={() => setCreating(true)}><Plus aria-hidden="true" /> Create agent</StudioButton> : undefined} />
          </div> : <StudioDataGrid columns={columns} items={collection.items} getKey={row => row.definition.definitionId}
            onSelect={row => void open(row)}
            gridColumns="minmax(12rem, 2fr) minmax(9rem, 1fr) minmax(8rem, 1fr) minmax(7rem, auto)" />}
          <StudioCollectionFooter>
          <p ref={countRef} tabIndex={-1} className="agent-collection-count" role="status">{collection.totalCount === 1 ? "1 agent" : `${collection.totalCount.toLocaleString()} agents`}</p>
          {collection.continuation || collection.moreFailed || collection.restart ? (
            <div>
              {collection.moreFailed ? <StudioAlert tone="warning">More agents could not be loaded.</StudioAlert> : null}
              {collection.restart ? <StudioAlert tone="warning">{collection.restart === "expired"
                ? "This list has expired since it was loaded, so it cannot continue. Reload it to see the current agents."
                : "Your access or this list's filter changed since it was loaded, so it cannot continue. Reload it to see the current agents."}</StudioAlert> : null}
              {collection.restart
                ? <StudioButton onClick={reload}><RefreshCw aria-hidden="true" /> Reload from the start</StudioButton>
                : <StudioButton disabled={collection.loadingMore} onClick={() => void loadMore()}>{collection.loadingMore ? "Loading…" : "Load more"}</StudioButton>}
            </div>
          ) : null}
          </StudioCollectionFooter>
        </>
      ) : null}
    </StudioPage>
  );
}

function AgentStatus({ row }: { row: AgentRow }) {
  const { head, recommendation, draftCount, versionCount } = row.lifecycle;
  const published = publishedVersion(row);
  // Another provider's recommendation or latest version is disclosed by role, without its provider key.
  const others = [
    recommendation && recommendation.providerKey !== agentProviderKey ? "recommended" : null,
    head && head.providerKey !== agentProviderKey && head.versionId !== recommendation?.versionId ? "latest" : null
  ].filter(Boolean);
  // The server counts drafts and versions of every kind, so the counts are labelled as the definition's.
  const counts = [draftCount > 0 ? (draftCount === 1 ? "1 draft" : `${draftCount} drafts`) : null, versionCount > 0 ? (versionCount === 1 ? "1 version" : `${versionCount} versions`) : null].filter(Boolean);
  return (
    <span className="agent-row-status">
      {!published ? <StatusChip tone="warning">Not published</StatusChip>
        : isEnumValue(published.lifecycle, "Active") ? <StatusChip tone="success">Published {published.version}</StatusChip>
        : <StatusChip tone="warning">{published.version} {published.lifecycle.toLowerCase()}</StatusChip>}
      {others.length > 0 ? <small>Its {others.join(" and ")} version{others.length > 1 ? "s are" : " is"} another kind of activity</small> : null}
      {counts.length > 0 ? <small>{counts.join(" · ")} of any kind in this definition</small> : null}
    </span>
  );
}

function AgentAuthority({ row }: { row: AgentRow }) {
  const allowed = (name: string) => row.actions.some(action => action.action === name && action.allowed);
  const configuration = isEnumValue(row.definition.contentAuthority.kind, "ProviderSource");
  return (
    <span className="agent-row-authority">
      <span>{configuration ? "Configuration" : "Studio"}</span>
      {!allowed("edit-definition") ? <small>{allowed("fork-definition") ? "Read-only; fork it to change it" : configuration ? "Read-only" : "Read-only for you"}</small> : null}
    </span>
  );
}

function CreateAgentForm({ context, authoring, onCancel, onCreated }: {
  context: StudioEndpointContext;
  authoring: AgentAuthoring;
  onCancel(): void;
  onCreated(definitionId: string, draftId: string): void;
}) {
  const nameId = useId();
  const [name, setName] = useState("");
  const [purpose, setPurpose] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const errorRef = useRef<HTMLDivElement>(null);
  useEffect(() => { nameRef.current?.focus(); }, []);
  useEffect(() => { if (error) errorRef.current?.focus(); }, [error]);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim() || submitting) return;
    setSubmitting(true);
    setError(null);
    // The newest authorable manifest schema; continuity stays off until the author turns it on.
    const schemaVersion = [...authoring.schemaVersions].sort((a, b) => Number(b) - Number(a))[0];
    const payload = { ...initialAgentManifest(), ...(schemaVersion === continuitySchemaVersion ? { allowContinuity: false } : {}) };
    try {
      const created = await createAgent(context, { displayName: name.trim(), description: purpose.trim() || null, schemaVersion, payload, authoring });
      onCreated(created.definition.definitionId, created.draft.draftId);
    } catch (cause) {
      setError(cause instanceof StudioHttpError && cause.status === 403 ? "You don't have permission to create agents." : "The agent could not be created. Nothing was saved.");
      setSubmitting(false);
    }
  };

  return (
    <form className="agent-create-form" aria-labelledby={`${nameId}-title`} onSubmit={submit}
      onKeyDown={event => { if (event.key === "Escape" && !submitting) onCancel(); }}>
      <StudioPageHeader title="New agent" titleId={`${nameId}-title`}
        description="Give it a name and a purpose. Choose its model, instructions, inputs and results in the editor next." />
      <StudioField label="Name">
        <input ref={nameRef} id={nameId} value={name} required maxLength={200} disabled={submitting} onChange={event => setName(event.target.value)} />
      </StudioField>
      <StudioField label="Purpose" description="Optional. Shown wherever the agent is picked.">
        <textarea value={purpose} rows={2} maxLength={2000} disabled={submitting} onChange={event => setPurpose(event.target.value)} />
      </StudioField>
      {error ? <div ref={errorRef} tabIndex={-1}><StudioAlert tone="danger">{error}</StudioAlert></div> : null}
      <div className="agent-create-actions">
        <StudioButton type="submit" variant="primary" disabled={!name.trim() || submitting}>{submitting ? "Creating…" : "Create and edit"}</StudioButton>
        <StudioButton disabled={submitting} onClick={onCancel}>Cancel</StudioButton>
      </div>
    </form>
  );
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
}
