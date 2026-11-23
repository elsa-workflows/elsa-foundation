import { useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { formatStudioDate } from "@elsa-workflows/studio-sdk";
import React, { useCallback, useEffect, useId, useRef, useState } from "react";
import { Bot, Plus, RefreshCw } from "lucide-react";
import { StudioHttpError, useOptionalAuthContext, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  SkeletonRows, StatusChip, StudioAlert, StudioButton, StudioCollectionEmptyState, StudioCollectionFooter, StudioCollectionToolbar, StudioDataGrid,
  StudioDialog, StudioDialogFooter, StudioField, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTimestamp, type StudioDataGridColumn
} from "@elsa-workflows/studio-ui";
import {
  createAgent, getAgentAuthoring, listAgents, agentProviderKey, type AgentRow, type AgentAuthoring
} from "./agentsApi";
import { agentRowPath, agentWorkspaceDraftPath, publishedVersion } from "./agentWorkspaceRoutes";
export { activityDefinitionPath, activityDefinitionVersionPath, agentRowPath, agentWorkspaceDraftPath } from "./agentWorkspaceRoutes";
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

/**
 * The module-owned Agents collection over the generic activity definitions API. The server filters by the agent
 * provider, searches, counts and pages; nothing is filtered after paging. Editing, publication and history open the
 * Agent workspace, which uses the generic activity editor for revisions and permissions.
 */
export function AgentsPage({ context, navigate }: { context: StudioEndpointContext; navigate(path: string): void }) {
  useStudioTimeZone();
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

  const open = (row: AgentRow) => navigate(agentRowPath(row));

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
  const columns: StudioDataGridColumn<AgentRow>[] = [
    { id: "name", header: "Agent", render: row => (
      <span className="agent-row-name">
        <strong>{row.definition.displayName}</strong>
        {row.definition.description ? <small>{row.definition.description}</small> : null}
      </span>
    ) },
    { id: "latest", header: "Latest version", render: row => <AgentLatestVersion row={row} /> },
    { id: "published", header: "Published version", render: row => <AgentPublishedVersion row={row} /> },
    { id: "authority", header: "Defined in", render: row => <AgentAuthority row={row} /> },
    { id: "updated", header: "Updated", render: row => <StudioTimestamp value={row.updatedAt} fallback="Unavailable" invalidFallback="Unavailable" /> }
  ];

  return (
    <StudioPage className="agent-agents-page" data-testid="agents-page">
      <StudioPageHeader
        title="Agents"
        icon={<Bot aria-hidden="true" />}
        description="Agents you define here or in configuration. Each one is a reusable activity with typed inputs and results that workflows and BPMN processes can use."
        refresh={{ id: "agents", label: "Refresh agents", refreshing: collection.kind === "loading", disabled: creating, autoRefresh: true, onRefresh: loadFirst }}
        actions={canCreate ? <StudioButton ref={newAgentRef} variant="primary" onClick={() => setCreating(true)}><Plus aria-hidden="true" /> New agent</StudioButton> : null} />

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
          {collection.items.length === 0 ? <StudioCollectionEmptyState headingLevel={3} icon={<Bot aria-hidden="true" />}
              title={query ? "No matching agents" : "No agents yet"}
              description={query ? "Try a different name or clear your search." : canCreate ? "Create an agent to use it in workflows." : "Agents will appear here when available."}
              actions={query ? <StudioButton onClick={() => setSearch("")}>Clear search</StudioButton> : canCreate ? <StudioButton variant="primary" onClick={() => setCreating(true)}><Plus aria-hidden="true" /> Create agent</StudioButton> : undefined} /> : <StudioDataGrid columns={columns} items={collection.items} getKey={row => row.definition.definitionId}
            onSelect={row => void open(row)}
            gridColumns="minmax(12rem, 2fr) minmax(7rem, 1fr) minmax(7rem, 1fr) minmax(8rem, 1fr) minmax(7rem, auto)" />}
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
      {creating && canCreate ? <CreateAgentForm context={context} authoring={authoring}
        onCancel={() => { restoreFocus.current = true; setCreating(false); }}
        onCreated={(definitionId, draftId) => { setCreating(false); navigate(agentWorkspaceDraftPath(definitionId, draftId)); }} /> : null}
    </StudioPage>
  );
}

function AgentLatestVersion({ row }: { row: AgentRow }) {
  useStudioTimeZone();
  const { head, draftCount } = row.lifecycle;
  const latest = head?.providerKey === agentProviderKey ? head
    : row.lifecycle.recommendation?.providerKey === agentProviderKey ? row.lifecycle.recommendation : null;
  const latestIsAnotherActivity = head && head.providerKey !== agentProviderKey;
  return (
    <span className="agent-row-version">
      {latest ? <>{latest.version}{!isEnumValue(latest.lifecycle, "Active") ? <StatusChip tone="warning">{latest.lifecycle.toLowerCase()}</StatusChip> : null}</>
        : <span>{head ? "No Agent version" : "No version yet"}</span>}
      {latestIsAnotherActivity ? <small>Latest version belongs to another kind of activity</small> : null}
      {draftCount > 0 ? <small>{draftCount === 1 ? "1 draft" : `${draftCount} drafts`} in this definition</small> : null}
    </span>
  );
}

function AgentPublishedVersion({ row }: { row: AgentRow }) {
  const published = publishedVersion(row);
  return (
    <span className="agent-row-version">
      {!published ? <StatusChip tone="warning">Not published</StatusChip>
        : isEnumValue(published.lifecycle, "Active") ? <StatusChip tone="success">Published {published.version}</StatusChip>
        : <StatusChip tone="warning">{published.version} {published.lifecycle.toLowerCase()}</StatusChip>}
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
    <StudioDialog open title="New agent" description="Give it a name and a purpose. Choose its model, instructions, inputs and results in the editor next."
      initialFocusRef={nameRef} dismissDisabled={submitting} onClose={onCancel} onSubmit={submit}
      footer={<StudioDialogFooter onCancel={onCancel} cancelDisabled={submitting}
        confirmLabel={submitting ? "Creating…" : "Create"} confirmDisabled={!name.trim() || submitting} />}>
      <StudioField label="Name">
        <input ref={nameRef} id={nameId} value={name} required maxLength={200} disabled={submitting} onChange={event => setName(event.target.value)} />
      </StudioField>
      <StudioField label="Purpose" description="Shown wherever the agent is picked.">
        <textarea value={purpose} rows={2} maxLength={2000} disabled={submitting} onChange={event => setPurpose(event.target.value)} />
      </StudioField>
      {error ? <div ref={errorRef} tabIndex={-1}><StudioAlert tone="danger">{error}</StudioAlert></div> : null}
    </StudioDialog>
  );
}
