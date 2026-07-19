import { lazy, useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, ChevronLeft, ChevronRight, Database, RefreshCw, Search } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import type { StudioActivityDefinitionImplementationEditorContribution, StudioEndpointContext, StudioRuntimeSettings, StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionCollectionRequest, ActivityDefinitionManagementView } from "./activityDefinitionTypes";
import { classifyActivityDefinitionReadFailure, redactActivityDefinitionManagementCache, useActivityDefinitions } from "./api/activityDesign";
import { activityDefinitionDurationBucket, activityDefinitionResultBand, observeActivityDefinitions } from "./activityDefinitionObservability";
import { WorkflowLazyBoundary } from "./WorkflowLazyBoundary";
import "./activityDefinitions.css";

const ActivityDefinitionWorkbench = lazy(() => import("./ActivityDefinitionWorkbench").then(module => ({ default: module.ActivityDefinitionWorkbench })));
const ActivityDefinitionCreateDialog = lazy(() => import("./ActivityDefinitionCreateDialog").then(module => ({ default: module.ActivityDefinitionCreateDialog })));
const ActivityDefinitionDraftEditor = lazy(() => import("./ActivityDefinitionDraftEditor").then(module => ({ default: module.ActivityDefinitionDraftEditor })));

type PageSize = 10 | 25 | 50;
type RouteState = { definitionId: string | null; section: string | null; draftId: string | null; versionId: string | null; draftActionVersionId?: string | null };

export function ActivityDefinitionsPage({
  context,
  activityEditors = () => [],
  inputEditors = () => [],
  runtime = {},
  navigateToStudioPath = defaultStudioPathNavigation
}: {
  context: StudioEndpointContext;
  activityEditors?: () => StudioActivityDefinitionImplementationEditorContribution[];
  inputEditors?: () => StudioWorkflowRunInputEditorContribution[];
  runtime?: StudioRuntimeSettings;
  navigateToStudioPath?(path: string): void;
}) {
  const [route, setRoute] = useState(readRouteState);
  const routeRef = useRef(route);
  const navigationBlockedRef = useRef(false);

  useEffect(() => {
    routeRef.current = route;
  }, [route]);

  useEffect(() => {
    const sync = () => {
      if (navigationBlockedRef.current) {
        writeRouteState(routeRef.current, "push");
        return;
      }
      setRoute(readRouteState());
    };
    window.addEventListener("popstate", sync);
    return () => window.removeEventListener("popstate", sync);
  }, []);

  useEffect(() => {
    observeActivityDefinitions({ event: "route-view", surface: route.definitionId ? "workbench" : "collection" });
  }, [route.definitionId]);

  const navigate = (next: RouteState, force = false) => {
    if (navigationBlockedRef.current && !force) return;
    navigationBlockedRef.current = false;
    writeRouteState(next, "push");
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  if (route.definitionId) {
    if (route.section === "editor" && route.draftId) {
      return <WorkflowLazyBoundary label="activity definition draft editor"><ActivityDefinitionDraftEditor context={context} definitionId={route.definitionId} draftId={route.draftId} activityEditors={activityEditors()} inputEditors={inputEditors()} recoverySettings={runtime.activityDefinitions?.localRecovery} identity={runtime.identity} onNavigationGuardChange={blocked => { navigationBlockedRef.current = blocked; }} onBack={force => navigate({ definitionId: route.definitionId, section: "drafts", draftId: route.draftId, versionId: null }, force)} onOpenDraft={(definitionId, draftId) => navigate({ definitionId, section: "editor", draftId, versionId: null }, true)} onOpenVersion={(definitionId, versionId) => navigate({ definitionId, section: "versions", draftId: null, versionId }, true)} onOpenStudioPath={navigateToStudioPath} /></WorkflowLazyBoundary>;
    }
    return (
      <WorkflowLazyBoundary label="activity definition workbench">
        <ActivityDefinitionWorkbench context={context} definitionId={route.definitionId} section={route.section} selectedDraftId={route.draftId} selectedVersionId={route.versionId} requestedDraftActionVersionId={route.draftActionVersionId} activityEditors={activityEditors()} onNavigate={navigate} />
      </WorkflowLazyBoundary>
    );
  }

  return <ActivityDefinitionCollection context={context} activityEditors={activityEditors()} onOpen={definitionId => navigate({ definitionId, section: null, draftId: null, versionId: null })} onCreated={(definitionId, draftId) => navigate({ definitionId, section: "editor", draftId, versionId: null })} />;
}

function defaultStudioPathNavigation(path: string) {
  window.history.pushState({}, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

function writeRouteState(route: RouteState, mode: "push" | "replace") {
  const parameters = new URLSearchParams();
  if (route.definitionId) parameters.set("definition", route.definitionId);
  if (route.section) parameters.set("section", route.section);
  if (route.draftId) parameters.set("draft", route.draftId);
  if (route.versionId) parameters.set("version", route.versionId);
  if (route.draftActionVersionId) parameters.set("createDraftFrom", route.draftActionVersionId);
  const query = parameters.toString();
  window.history[mode === "push" ? "pushState" : "replaceState"]({}, "", `/workflows/activity-definitions${query ? `?${query}` : ""}`);
}

function ActivityDefinitionCollection({ context, activityEditors, onOpen, onCreated }: { context: StudioEndpointContext; activityEditors: StudioActivityDefinitionImplementationEditorContribution[]; onOpen(definitionId: string): void; onCreated(definitionId: string, draftId: string): void }) {
  const queryClient = useQueryClient();
  const [searchInput, setSearchInput] = useState("");
  const [providerInput, setProviderInput] = useState("");
  const search = useDebouncedValue(searchInput, 250);
  const providerKey = useDebouncedValue(providerInput, 250);
  const [authority, setAuthority] = useState<ActivityDefinitionCollectionRequest["authority"]>("");
  const [pageSize, setPageSize] = useState<PageSize>(25);
  const [cursorStack, setCursorStack] = useState<(string | null)[]>([null]);
  const [cursorIndex, setCursorIndex] = useState(0);
  const [lastConfirmedAt, setLastConfirmedAt] = useState<number | null>(null);
  const [privacyFailure, setPrivacyFailure] = useState<"unavailable" | "forbidden" | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const observation = useRef({ fetching: false, startedAt: 0 });
  const cursor = cursorStack[cursorIndex];
  const request = useMemo(() => ({ limit: pageSize, cursor, search, authority, providerKey }), [authority, cursor, pageSize, providerKey, search]);
  const query = useActivityDefinitions(context, request);
  const directFailure = query.error ? classifyActivityDefinitionReadFailure(query.error) : null;
  const failure = privacyFailure ?? directFailure;
  const mustDiscardData = failure === "forbidden" || failure === "unavailable";
  const data = mustDiscardData ? undefined : query.data;
  const filtersActive = Boolean(search.trim() || providerKey.trim() || authority);

  useEffect(() => {
    setCursorStack([null]);
    setCursorIndex(0);
  }, [authority, pageSize, providerKey, search]);

  useEffect(() => {
    if (query.isSuccess && !query.isPlaceholderData && query.dataUpdatedAt > 0) setLastConfirmedAt(query.dataUpdatedAt);
  }, [query.dataUpdatedAt, query.isPlaceholderData, query.isSuccess]);

  useEffect(() => {
    if (directFailure === "forbidden" || directFailure === "unavailable") setPrivacyFailure(directFailure);
  }, [directFailure]);

  useEffect(() => {
    if (!mustDiscardData) return;
    void redactActivityDefinitionManagementCache(queryClient);
  }, [mustDiscardData, queryClient]);

  useEffect(() => {
    const active = observation.current;
    if (query.isFetching && !active.fetching) {
      active.fetching = true;
      active.startedAt = performance.now();
      observeActivityDefinitions({ event: "query-start", surface: "collection", hasSearch: Boolean(search), hasAuthorityFilter: Boolean(authority), hasProviderFilter: Boolean(providerKey), pageSize });
      return;
    }
    if (query.isFetching || !active.fetching) return;
    active.fetching = false;
    const durationBucket = activityDefinitionDurationBucket(performance.now() - active.startedAt);
    if (query.error) observeActivityDefinitions({ event: "query-failure", surface: "collection", outcome: failure ?? "failed", durationBucket });
    else if (query.data) observeActivityDefinitions({ event: "query-success", surface: "collection", outcome: query.data.totalCount === 0 ? "empty" : "ready", resultBand: activityDefinitionResultBand(query.data.totalCount), durationBucket });
  }, [authority, failure, pageSize, providerKey, query.data, query.error, query.isFetching, search]);

  const openDefinition = (definition: ActivityDefinitionManagementView) => {
    observeActivityDefinitions({ event: "open-definition", surface: "collection" });
    onOpen(definition.definition.definitionId);
  };
  const refresh = () => {
    setPrivacyFailure(null);
    observeActivityDefinitions({ event: "refresh", surface: "collection" });
    void query.refetch();
  };
  const restartPaging = () => {
    setPrivacyFailure(null);
    if (cursorIndex === 0) {
      void query.refetch();
      return;
    }
    setCursorStack([null]);
    setCursorIndex(0);
  };
  const nextPage = () => {
    const continuation = data?.continuation;
    if (!continuation) return;
    setCursorStack(current => [...current.slice(0, cursorIndex + 1), continuation]);
    setCursorIndex(current => current + 1);
  };

  return (
    <main className="ad-page" aria-labelledby="activity-definitions-title">
      <header className="ad-page-header">
        <div><span className="ad-kicker">Workflows</span><h1 id="activity-definitions-title">Activity Definitions</h1><p>Browse stable reusable activity identities. Draft and version history stay attached to the definition you open.</p></div>
        <div className="ad-header-actions"><button type="button" onClick={() => setCreateOpen(true)}>Create Activity Definition</button><button type="button" className="ad-primary-action" onClick={refresh} disabled={query.isFetching}><RefreshCw size={16} aria-hidden /> {query.isFetching && data ? "Refreshing" : "Refresh"}</button></div>
      </header>

      <section className="ad-filters" aria-label="Activity Definition filters">
        <label className="ad-search-field"><span>Search</span><span className="ad-input-shell"><Search size={16} aria-hidden /><input value={searchInput} onChange={event => setSearchInput(event.target.value)} placeholder="Name, type key, or category" /></span></label>
        <label><span>Authority</span><select value={authority} onChange={event => setAuthority(event.target.value as ActivityDefinitionCollectionRequest["authority"])}><option value="">All authorized</option><option value="Design">Design owned</option><option value="ProviderSource">Source owned</option></select></label>
        <label><span>Provider key</span><input value={providerInput} onChange={event => setProviderInput(event.target.value)} placeholder="Exact provider key" /></label>
        <label><span>Rows</span><select value={pageSize} onChange={event => setPageSize(Number(event.target.value) as PageSize)}><option value={10}>10</option><option value={25}>25</option><option value={50}>50</option></select></label>
      </section>

      {query.isFetching && data ? <div className="ad-inline-status" role="status" aria-live="polite">Refreshing the authorized collection while keeping the last confirmed page visible.</div> : null}
      {query.isError && data && failure === "expired" ? <div className="ad-stale-warning" role="alert"><AlertTriangle size={18} aria-hidden /><div><strong>Collection snapshot expired</strong><span>The retained page is stale. Restart paging to request a new authorized snapshot.</span></div><button type="button" onClick={restartPaging}>Restart paging</button></div> : null}
      {query.isError && data && failure !== "expired" ? <div className="ad-stale-warning" role="alert"><AlertTriangle size={18} aria-hidden /><div><strong>Refresh failed; showing retained data</strong><span>Last confirmed {formatTimestamp(lastConfirmedAt)}. Retry to request a new snapshot.</span></div><button type="button" onClick={refresh}>Retry</button></div> : null}
      {!data && query.isPending && !failure ? <ActivityDefinitionSkeleton /> : null}
      {!data && failure ? <ActivityDefinitionFailure kind={failure} onRetry={failure === "expired" ? restartPaging : refresh} /> : null}
      {data && data.items.length === 0 ? <section className="ad-empty" role="status"><Database size={24} aria-hidden /><h2>{filtersActive ? "No Activity Definitions match" : "No Activity Definitions yet"}</h2><p>{filtersActive ? "Clear or change the server-side filters to search the authorized collection again." : "No authorized Activity Definitions are available in this scope."}</p>{filtersActive ? <button type="button" onClick={() => { setSearchInput(""); setProviderInput(""); setAuthority(""); }}>Clear filters</button> : null}</section> : null}
      {data && data.items.length > 0 ? <>
        <div className="ad-collection" role="table" aria-label="Activity Definitions" aria-rowcount={data.totalCount}>
          <div className="ad-collection-head" role="row"><span role="columnheader">Definition</span><span role="columnheader">Authority</span><span role="columnheader">Provider</span><span role="columnheader">Recommendation</span><span role="columnheader">Updated</span></div>
          {data.items.map(definition => <div key={definition.definition.definitionId} className="ad-collection-row" role="row" tabIndex={0} aria-label={`Open Activity Definition ${definition.definition.displayName}`} onClick={() => openDefinition(definition)} onKeyDown={event => { if (event.key !== "Enter" && event.key !== " ") return; event.preventDefault(); openDefinition(definition); }}>
            <span role="cell" data-label="Definition"><strong>{definition.definition.displayName}</strong><small>{definition.definition.activityTypeKey}</small></span>
            <span role="cell" data-label="Authority"><AuthorityBadge kind={definition.definition.contentAuthority.kind} /></span>
            <span role="cell" data-label="Provider">{definition.lifecycle.recommendation?.providerKey ?? definition.lifecycle.head?.providerKey ?? "Not established"}</span>
            <span role="cell" data-label="Recommendation">{definition.lifecycle.recommendation ? `${definition.lifecycle.recommendation.version} · ${definition.lifecycle.recommendation.lifecycle}` : "Not recommended"}</span>
            <span role="cell" data-label="Updated">{formatDate(definition.updatedAt)}</span>
          </div>)}
        </div>
        <footer className="ad-pager" aria-label="Activity Definition pagination"><span>Page {cursorIndex + 1} · {data.count} of {data.totalCount} authorized definitions</span><div><button type="button" onClick={() => setCursorIndex(current => Math.max(0, current - 1))} disabled={cursorIndex === 0}><ChevronLeft size={15} aria-hidden /> Previous</button><button type="button" onClick={nextPage} disabled={!data.hasMore || !data.continuation}>Next <ChevronRight size={15} aria-hidden /></button></div></footer>
      </> : null}
      {createOpen ? <WorkflowLazyBoundary label="create activity definition"><ActivityDefinitionCreateDialog context={context} activityEditors={activityEditors} onClose={() => setCreateOpen(false)} onCreated={response => { setCreateOpen(false); onCreated(response.definition.definitionId, response.draft.draftId); }} /></WorkflowLazyBoundary> : null}
    </main>
  );
}

function ActivityDefinitionSkeleton() {
  return <div className="ad-skeleton" role="status" aria-live="polite" aria-busy="true"><span>Loading Activity Definitions…</span>{Array.from({ length: 5 }, (_, index) => <div key={index} />)}</div>;
}

function ActivityDefinitionFailure({ kind, onRetry }: { kind: ReturnType<typeof classifyActivityDefinitionReadFailure>; onRetry(): void }) {
  const copy = {
    unavailable: ["Unavailable contribution", "This backend does not advertise the Activity Definition management capability supported by Studio. No legacy activity fallback is used."],
    forbidden: ["Activity Definitions unavailable", "This account cannot open the Activity Definitions collection. No identities or counts were loaded."],
    "not-found": ["Activity Definitions unavailable", "The requested collection is not available."],
    expired: ["Collection snapshot expired", "Restart paging to request a fresh authorized snapshot."],
    failed: ["Couldn't load Activity Definitions", "Studio could not confirm the authorized collection. No unconfirmed data is shown."]
  }[kind];
  return <section className="ad-failure" role="alert"><AlertTriangle size={22} aria-hidden /><h2>{copy[0]}</h2><p>{copy[1]}</p><button type="button" onClick={onRetry}>{kind === "expired" ? "Restart paging" : "Try again"}</button></section>;
}

function AuthorityBadge({ kind }: { kind: string }) {
  return <span className={`ad-badge ${kind === "Design" ? "is-design" : "is-source"}`}>{kind === "Design" ? "Design owned" : "Source owned"}</span>;
}

function useDebouncedValue(value: string, delay: number) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => { const timeout = window.setTimeout(() => setDebounced(value.trim()), delay); return () => window.clearTimeout(timeout); }, [delay, value]);
  return debounced;
}

function readRouteState(): RouteState {
  const parameters = new URLSearchParams(window.location.search);
  return { definitionId: parameters.get("definition"), section: parameters.get("section"), draftId: parameters.get("draft"), versionId: parameters.get("version"), draftActionVersionId: parameters.get("createDraftFrom") };
}

function formatDate(value: string | null | undefined) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "—" : date.toLocaleDateString();
}

function formatTimestamp(value: number | null) {
  return value ? new Date(value).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "at an earlier time";
}
