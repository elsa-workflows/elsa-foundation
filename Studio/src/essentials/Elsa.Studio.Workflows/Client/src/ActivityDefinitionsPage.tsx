import { lazy, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, ChevronLeft, ChevronRight, Database, RefreshCw, Search } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import type {
  StudioActivityDefinitionImplementationEditorContribution,
  StudioActivityPropertyEditorContribution,
  StudioEndpointContext,
  StudioExpressionEditorContribution,
  StudioRuntimeSettings,
  StudioWorkflowDesignerPanelContribution,
  StudioWorkflowRunInputEditorContribution
} from "@elsa-workflows/studio-sdk";
import { authSessionEndedEvent, authSessionStartedEvent } from "@elsa-workflows/studio-sdk";
import {
  type ActivityDefinitionCollectionRequest,
  type ActivityDefinitionManagementView
} from "./activityDefinitionTypes";
import { formatActivityDefinitionDate, formatActivityVersion, resolveActivityDefinitionTitle } from "./workflowFormatting";
import { activityDesignKeys, classifyActivityDefinitionReadFailure, isInvalidActivityDefinitionCursor, redactActivityDefinitionManagementCache, useActivityDefinitions } from "./api/activityDesign";
import { activityDefinitionDurationBucket, activityDefinitionResultBand, observeActivityDefinitions } from "./activityDefinitionObservability";
import { WorkflowLazyBoundary } from "./WorkflowLazyBoundary";
import { WorkflowListIdentity } from "./workflow-editor/WorkflowListIdentity";
import "./activityDefinitions.css";
import "./activityDefinitionCollection.css";

const ActivityDefinitionWorkbench = lazy(() => import("./ActivityDefinitionWorkbench").then(module => ({ default: module.ActivityDefinitionWorkbench })));
const ActivityDefinitionCreateDialog = lazy(() => import("./ActivityDefinitionCreateDialog").then(module => ({ default: module.ActivityDefinitionCreateDialog })));
const ActivityDefinitionDraftEditor = lazy(() => import("./ActivityDefinitionDraftEditor").then(module => ({ default: module.ActivityDefinitionDraftEditor })));

type PageSize = 10 | 25 | 50;
type RouteState = {
  definitionId: string | null;
  section: string | null;
  draftId: string | null;
  versionId: string | null;
  draftActionVersionId?: string | null;
  returnPlanId?: string | null;
};

type ActivityCollectionHistoryContext = {
  entryId: string;
  runtimeScopeId: string;
  baseUrl: string;
  authorityRevision: number;
  searchInput: string;
  search: string;
  providerInput: string;
  providerKey: string;
  authority: ActivityDefinitionCollectionRequest["authority"];
  pageSize: PageSize;
  cursorStack: (string | null)[];
  cursorIndex: number;
};

type ActivityHistoryMetadata = { collection?: ActivityCollectionHistoryContext };
const activityHistoryStateKey = "elsaActivityDefinitions";
const activityAuthorityRevisionStorageKey = "elsaActivityDefinitions.authorityRevision";
const activityRuntimeScopeId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
let activityHistoryEntrySequence = 0;
let activityAuthorityRevision = readStoredActivityAuthorityRevision();

if (typeof window !== "undefined") {
  const recordAuthorityChange = incrementActivityAuthorityRevision;
  window.addEventListener(authSessionStartedEvent, recordAuthorityChange);
  window.addEventListener(authSessionEndedEvent, recordAuthorityChange);
}

export function ActivityDefinitionsPage({
  context,
  activityEditors = () => [],
  graphExtensions = () => ({ properties: [], expressions: [], panels: [] }),
  inputEditors = () => [],
  runtime = {},
  navigateToStudioPath = defaultStudioPathNavigation
}: {
  context: StudioEndpointContext;
  activityEditors?: () => StudioActivityDefinitionImplementationEditorContribution[];
  graphExtensions?: () => {
    properties: StudioActivityPropertyEditorContribution[];
    expressions: StudioExpressionEditorContribution[];
    panels: StudioWorkflowDesignerPanelContribution[];
  };
  inputEditors?: () => StudioWorkflowRunInputEditorContribution[];
  runtime?: StudioRuntimeSettings;
  navigateToStudioPath?(path: string): void;
}) {
  const [route, setRoute] = useState(readRouteState);
  const routeRef = useRef(route);
  const navigationBlockedRef = useRef(false);
  const routeCollectionContextRef = useRef(readActivityHistoryMetadata()?.collection ?? null);
  const updateRouteCollectionContext = useCallback((value: ActivityCollectionHistoryContext) => {
    routeCollectionContextRef.current = value;
  }, []);
  const queryClient = useQueryClient();
  const restoredHistoryContext = readActivityHistoryMetadata()?.collection;
  const currentAuthorityRevision = getActivityAuthorityRevision();
  const initialScopeChanged = Boolean(restoredHistoryContext && (restoredHistoryContext.runtimeScopeId !== activityRuntimeScopeId || restoredHistoryContext.baseUrl !== context.baseUrl || restoredHistoryContext.authorityRevision !== currentAuthorityRevision));
  const [authorizationRevision, setAuthorizationRevision] = useState(currentAuthorityRevision);
  const [authorizationInvalidating, setAuthorizationInvalidating] = useState(initialScopeChanged);
  const pendingRedactionsRef = useRef(0);
  const initialRedactionStartedRef = useRef(false);
  const [historyRevision, setHistoryRevision] = useState(0);
  const baseUrlRef = useRef(context.baseUrl);

  const invalidateAuthorizationScope = useCallback(() => {
    setAuthorizationRevision(getActivityAuthorityRevision());
    setAuthorizationInvalidating(true);
    pendingRedactionsRef.current += 1;
    void redactActivityDefinitionManagementCache(queryClient).then(() => queryClient.invalidateQueries({ queryKey: activityDesignKeys.definitionCollections })).finally(() => {
      pendingRedactionsRef.current -= 1;
      if (pendingRedactionsRef.current === 0) setAuthorizationInvalidating(false);
    });
  }, [queryClient]);

  useEffect(() => {
    window.addEventListener(authSessionStartedEvent, invalidateAuthorizationScope);
    window.addEventListener(authSessionEndedEvent, invalidateAuthorizationScope);
    return () => {
      window.removeEventListener(authSessionStartedEvent, invalidateAuthorizationScope);
      window.removeEventListener(authSessionEndedEvent, invalidateAuthorizationScope);
    };
  }, [invalidateAuthorizationScope]);

  useEffect(() => {
    if (baseUrlRef.current === context.baseUrl) return;
    baseUrlRef.current = context.baseUrl;
    incrementActivityAuthorityRevision();
    invalidateAuthorizationScope();
  }, [context.baseUrl, invalidateAuthorizationScope]);

  useEffect(() => {
    if (!initialScopeChanged || initialRedactionStartedRef.current) return;
    initialRedactionStartedRef.current = true;
    if (restoredHistoryContext?.baseUrl !== context.baseUrl) incrementActivityAuthorityRevision();
    invalidateAuthorizationScope();
  }, [context.baseUrl, initialScopeChanged, invalidateAuthorizationScope, restoredHistoryContext?.baseUrl]);

  useEffect(() => {
    routeRef.current = route;
  }, [route]);

  useEffect(() => {
    const sync = () => {
      if (navigationBlockedRef.current) {
        writeRouteState(routeRef.current, "push", routeCollectionContextRef.current);
        return;
      }
      routeCollectionContextRef.current = readActivityHistoryMetadata()?.collection ?? null;
      setRoute(readRouteState());
      setHistoryRevision(value => value + 1);
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
    writeRouteState(next, "push", routeCollectionContextRef.current);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  if (route.definitionId) {
    if (route.section === "editor" && route.draftId) {
      const extensions = graphExtensions();
      return <WorkflowLazyBoundary label="activity definition draft editor"><ActivityDefinitionDraftEditor context={context} definitionId={route.definitionId} draftId={route.draftId} activityEditors={activityEditors()} propertyEditors={extensions.properties} expressionEditors={extensions.expressions} graphAuthoringPanels={extensions.panels} inputEditors={inputEditors()} recoverySettings={runtime.activityDefinitions?.localRecovery} identity={runtime.identity} onNavigationGuardChange={blocked => { navigationBlockedRef.current = blocked; }} onBack={force => {
        if (route.returnPlanId) {
          navigationBlockedRef.current = false;
          navigateToStudioPath(`/workflows/activity-definitions/upgrades?plan=${encodeURIComponent(route.returnPlanId)}`);
          return;
        }
        navigate({ definitionId: route.definitionId, section: null, draftId: null, versionId: null }, force);
      }} onOpenDraft={(definitionId, draftId) => navigate({ definitionId, section: "editor", draftId, versionId: null, returnPlanId: route.returnPlanId }, true)} onOpenVersion={(definitionId, versionId) => navigate({ definitionId, section: "versions", draftId: null, versionId, returnPlanId: route.returnPlanId }, true)} onOpenStudioPath={navigateToStudioPath} /></WorkflowLazyBoundary>;
    }
    return (
      <WorkflowLazyBoundary label="activity definition workbench">
        <ActivityDefinitionWorkbench context={context} definitionId={route.definitionId} section={route.section} selectedDraftId={route.draftId} selectedVersionId={route.versionId} requestedDraftActionVersionId={route.draftActionVersionId} activityEditors={activityEditors()} onNavigate={navigate} onOpenWorkflows={() => navigateToStudioPath("/workflows/definitions")} onPlanUpgrade={(kind, id) => navigateToStudioPath(`/workflows/activity-definitions/upgrades?rootKind=${encodeURIComponent(kind)}&rootId=${encodeURIComponent(id)}`)} />
      </WorkflowLazyBoundary>
    );
  }

  const collectionEntryId = readActivityHistoryMetadata()?.collection?.entryId ?? "new-entry";
  return <ActivityDefinitionCollection key={`${collectionEntryId}:${historyRevision}`} context={context} baseUrl={context.baseUrl} authorityRevision={authorizationRevision} authorizationInvalidating={authorizationInvalidating} activityEditors={activityEditors()} onHistoryContextChange={updateRouteCollectionContext} onImport={() => navigateToStudioPath("/workflows/activity-definitions/import-elsa3")} onPlanUpgrade={() => navigateToStudioPath("/workflows/activity-definitions/upgrades")} onOpen={definitionId => navigate({ definitionId, section: null, draftId: null, versionId: null })} onCreated={(definitionId, draftId) => navigate({ definitionId, section: "editor", draftId, versionId: null })} />;
}

function defaultStudioPathNavigation(path: string) {
  window.history.pushState({}, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

function writeRouteState(route: RouteState, mode: "push" | "replace", collection?: ActivityCollectionHistoryContext | null) {
  const parameters = new URLSearchParams();
  if (route.definitionId) parameters.set("definition", route.definitionId);
  if (route.section) parameters.set("section", route.section);
  if (route.draftId) parameters.set("draft", route.draftId);
  if (route.versionId) parameters.set("version", route.versionId);
  if (route.draftActionVersionId) parameters.set("createDraftFrom", route.draftActionVersionId);
  if (route.returnPlanId) parameters.set("returnPlan", route.returnPlanId);
  const query = parameters.toString();
  const state = historyStateRecord(window.history.state);
  const metadata = readActivityHistoryMetadata(state) ?? {};
  if (collection === null) delete metadata.collection;
  else if (collection) metadata.collection = collection;
  window.history[mode === "push" ? "pushState" : "replaceState"]({ ...state, [activityHistoryStateKey]: metadata }, "", `/workflows/activity-definitions${query ? `?${query}` : ""}`);
}

function ActivityDefinitionCollection({ context, baseUrl, authorityRevision, authorizationInvalidating, activityEditors, onHistoryContextChange, onImport, onPlanUpgrade, onOpen, onCreated }: { context: StudioEndpointContext; baseUrl: string; authorityRevision: number; authorizationInvalidating: boolean; activityEditors: StudioActivityDefinitionImplementationEditorContribution[]; onHistoryContextChange(context: ActivityCollectionHistoryContext): void; onImport(): void; onPlanUpgrade(): void; onOpen(definitionId: string): void; onCreated(definitionId: string, draftId: string): void }) {
  const queryClient = useQueryClient();
  const [restoredContext] = useState(() => readActivityHistoryMetadata()?.collection ?? null);
  const restoredScopeMatches = !authorizationInvalidating && restoredContext?.runtimeScopeId === activityRuntimeScopeId && restoredContext?.baseUrl === baseUrl && restoredContext?.authorityRevision === authorityRevision;
  const [searchInput, setSearchInput] = useState(restoredContext?.searchInput ?? "");
  const [providerInput, setProviderInput] = useState(restoredContext?.providerInput ?? "");
  const search = useDebouncedValue(searchInput, 250, restoredContext?.search);
  const providerKey = useDebouncedValue(providerInput, 250, restoredContext?.providerKey);
  const [authority, setAuthority] = useState<ActivityDefinitionCollectionRequest["authority"]>(restoredContext?.authority ?? "Design");
  const [pageSize, setPageSize] = useState<PageSize>(restoredContext?.pageSize ?? 25);
  const [cursorStack, setCursorStack] = useState<(string | null)[]>(restoredScopeMatches ? restoredContext?.cursorStack ?? [null] : [null]);
  const [cursorIndex, setCursorIndex] = useState(restoredScopeMatches ? restoredContext?.cursorIndex ?? 0 : 0);
  const [lastConfirmedAt, setLastConfirmedAt] = useState<number | null>(null);
  const [privacyFailure, setPrivacyFailure] = useState<"unavailable" | "forbidden" | null>(null);
  const [freshPageRequired, setFreshPageRequired] = useState(false);
  const [scopeRecoveryNotice, setScopeRecoveryNotice] = useState(Boolean(restoredContext && !restoredScopeMatches && restoredContext.cursorIndex > 0));
  const [createOpen, setCreateOpen] = useState(false);
  const observation = useRef({ fetching: false, startedAt: 0 });
  const entryId = useRef(restoredContext?.entryId ?? createActivityHistoryEntryId());
  const scopeRef = useRef({ baseUrl: restoredContext?.baseUrl ?? baseUrl, authorityRevision: restoredContext?.authorityRevision ?? authorityRevision });
  const scopeChanged = authorizationInvalidating || scopeRef.current.baseUrl !== baseUrl || scopeRef.current.authorityRevision !== authorityRevision;
  const previousFilters = useRef({ authority, pageSize, providerKey, search });
  const filtersChanged = previousFilters.current.authority !== authority || previousFilters.current.pageSize !== pageSize || previousFilters.current.providerKey !== providerKey || previousFilters.current.search !== search;
  const effectiveCursorStack = useMemo(() => scopeChanged || filtersChanged ? [null] : cursorStack, [cursorStack, filtersChanged, scopeChanged]);
  const effectiveCursorIndex = scopeChanged || filtersChanged ? 0 : cursorIndex;
  const cursor = effectiveCursorStack[effectiveCursorIndex];
  const request = useMemo(() => ({ limit: pageSize, cursor, search, authority, providerKey }), [authority, cursor, pageSize, providerKey, search]);
  const historyContext = useMemo<ActivityCollectionHistoryContext>(() => ({
    entryId: entryId.current,
    runtimeScopeId: activityRuntimeScopeId,
    baseUrl,
    authorityRevision,
    searchInput,
    search,
    providerInput,
    providerKey,
    authority,
    pageSize,
    cursorStack: effectiveCursorStack,
    cursorIndex: effectiveCursorIndex
  }), [authority, authorityRevision, baseUrl, effectiveCursorIndex, effectiveCursorStack, pageSize, providerInput, providerKey, search, searchInput]);
  const authorityScope = `${baseUrl}\u0000${authorityRevision}`;
  const query = useActivityDefinitions(context, request, authorityScope);
  const directFailure = query.error ? classifyActivityDefinitionReadFailure(query.error) : null;
  const invalidCursor = query.error ? isInvalidActivityDefinitionCursor(query.error) : false;
  const failure = privacyFailure ?? directFailure;
  const mustDiscardData = failure === "forbidden" || failure === "unavailable";
  const rejectedContinuation = failure === "expired" || invalidCursor;
  const data = authorizationInvalidating || scopeChanged || mustDiscardData || rejectedContinuation || freshPageRequired ? undefined : query.data;
  const defaultDesignedView = authority === "Design" && !searchInput.trim() && !providerInput.trim();
  const filtersActive = Boolean(searchInput.trim() || providerInput.trim() || authority !== "Design");

  useEffect(() => {
    if (!scopeChanged) return;
    const hadContinuation = cursorIndex > 0;
    scopeRef.current = { baseUrl, authorityRevision };
    setPrivacyFailure(null);
    setFreshPageRequired(false);
    setCursorStack([null]);
    setCursorIndex(0);
    if (hadContinuation) setScopeRecoveryNotice(true);
  }, [authorityRevision, baseUrl, cursorIndex, scopeChanged]);

  useEffect(() => {
    if (freshPageRequired && query.isSuccess && !query.isFetching && !query.isPlaceholderData) setFreshPageRequired(false);
  }, [freshPageRequired, query.isFetching, query.isPlaceholderData, query.isSuccess]);

  useEffect(() => {
    const previous = previousFilters.current;
    if (previous.authority === authority && previous.pageSize === pageSize && previous.providerKey === providerKey && previous.search === search) return;
    previousFilters.current = { authority, pageSize, providerKey, search };
    setCursorStack([null]);
    setCursorIndex(0);
    setScopeRecoveryNotice(false);
  }, [authority, pageSize, providerKey, search]);

  useEffect(() => {
    writeActivityCollectionHistoryContext(historyContext);
    onHistoryContextChange(historyContext);
  }, [historyContext, onHistoryContextChange]);

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
    writeActivityCollectionHistoryContext(historyContext);
    onHistoryContextChange(historyContext);
    onOpen(definition.definition.definitionId);
  };
  const refresh = () => {
    setPrivacyFailure(null);
    observeActivityDefinitions({ event: "refresh", surface: "collection" });
    void query.refetch();
  };
  const restartPaging = () => {
    setPrivacyFailure(null);
    setFreshPageRequired(true);
    if (cursorIndex === 0) {
      void query.refetch();
      return;
    }
    const firstPageRequest = { ...request, cursor: null };
    void queryClient.invalidateQueries({ queryKey: [...activityDesignKeys.definitionCollection(firstPageRequest), authorityScope], exact: true });
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
    <main className="ad-page ad-designed-collection" aria-labelledby="activity-definitions-title">
      <header className="ad-page-header">
        <div><span className="ad-kicker">Workflows</span><h1 id="activity-definitions-title">Activity Definitions</h1><p>Design reusable activities here. Drafts stay editable, while published versions remain in history.</p></div>
        <div className="ad-header-actions"><button type="button" className="ad-primary-action" onClick={() => setCreateOpen(true)}>Create activity</button><button type="button" onClick={refresh} disabled={query.isFetching}><RefreshCw size={16} aria-hidden /> {query.isFetching && data ? "Refreshing" : "Refresh"}</button><details className="ad-collection-disclosure ad-tools-disclosure"><summary>Tools</summary><div className="ad-disclosure-panel"><button type="button" onClick={onImport}>Import from Elsa 3</button><button type="button" onClick={onPlanUpgrade}>Plan broad upgrade</button></div></details></div>
      </header>

      <section className="ad-filters ad-collection-filters" aria-label="Activity Definition filters">
        <label className="ad-search-field"><span>Search</span><span className="ad-input-shell"><Search size={16} aria-hidden /><input value={searchInput} onChange={event => setSearchInput(event.target.value)} placeholder="Name, type key, or category" /></span></label>
        <div className="ad-collection-view" role="group" aria-label="Activity definition collection">
          <button type="button" aria-pressed={authority === "Design"} onClick={() => setAuthority("Design")}>Designed</button>
          <button type="button" aria-pressed={authority === "ProviderSource"} onClick={() => setAuthority("ProviderSource")}>Built-in</button>
          <button type="button" aria-pressed={authority === ""} onClick={() => setAuthority("")}>All</button>
        </div>
        <details className="ad-collection-disclosure ad-advanced-filters">
          <summary>Advanced filters</summary>
          <div className="ad-disclosure-panel ad-advanced-filter-fields">
            <label><span>Provider key</span><input value={providerInput} onChange={event => setProviderInput(event.target.value)} placeholder="Exact provider key" /></label>
            <label><span>Rows per page</span><select value={pageSize} onChange={event => setPageSize(Number(event.target.value) as PageSize)}><option value={10}>10</option><option value={25}>25</option><option value={50}>50</option></select></label>
          </div>
        </details>
      </section>

      {authorizationInvalidating ? <div className="ad-inline-status" role="status">The access scope changed. Reloading the authorized collection while keeping your filters and page size.</div> : null}
      {!authorizationInvalidating && scopeRecoveryNotice ? <div className="ad-inline-status" role="status">The collection scope changed, so Studio restarted at the first page and kept your filters and page size.</div> : null}
      {query.isFetching && data ? <div className="ad-inline-status" role="status" aria-live="polite">Refreshing the authorized collection while keeping the last confirmed page visible.</div> : null}
      {query.isError && data && (failure === "expired" || invalidCursor) ? <div className="ad-stale-warning" role="alert"><AlertTriangle size={18} aria-hidden /><div><strong>{invalidCursor ? "Collection continuation is no longer valid" : "Collection snapshot expired"}</strong><span>{invalidCursor ? "The server rejected this continuation. Restart paging to request a fresh authorized snapshot while keeping your filters and page size." : "The retained page is stale. Restart paging to request a new authorized snapshot."}</span></div><button type="button" onClick={restartPaging}>Restart paging</button></div> : null}
      {query.isError && data && failure !== "expired" && !invalidCursor ? <div className="ad-stale-warning" role="alert"><AlertTriangle size={18} aria-hidden /><div><strong>Refresh failed; showing retained data</strong><span>Last confirmed {formatTimestamp(lastConfirmedAt)}. Retry to request a new snapshot.</span></div><button type="button" onClick={refresh}>Retry</button></div> : null}
      {!data && !authorizationInvalidating && (query.isPending || query.isFetching) && !failure ? <ActivityDefinitionSkeleton /> : null}
      {!data && !authorizationInvalidating && failure ? <ActivityDefinitionFailure kind={failure} cursorInvalid={invalidCursor} onRetry={failure === "expired" || invalidCursor ? restartPaging : refresh} /> : null}
      {data && data.items.length === 0 ? <section className="ad-empty ad-collection-empty" role="status"><Database size={24} aria-hidden /><h2>{defaultDesignedView ? "No designed activities yet" : filtersActive ? "No Activity Definitions match" : "No Activity Definitions available"}</h2><p>{defaultDesignedView ? "Create a reusable activity definition, or browse built-in activities to see source-owned entries." : filtersActive ? "Clear or change the server-side filters to search the authorized collection again." : "No authorized Activity Definitions are available in this scope."}</p>{defaultDesignedView ? <div><button type="button" className="ad-primary-action" onClick={() => setCreateOpen(true)}>Create activity</button><button type="button" onClick={() => setAuthority("ProviderSource")}>Browse built-in activities</button></div> : filtersActive ? <button type="button" onClick={() => { setSearchInput(""); setProviderInput(""); setAuthority("Design"); }}>Clear filters</button> : null}</section> : null}
      {data && data.items.length > 0 ? <>
        <div className="ad-collection" role="table" aria-label="Activity Definitions" aria-rowcount={data.totalCount}>
          <div className="ad-collection-head" role="row"><span role="columnheader">Activity</span><span role="columnheader">State</span><span role="columnheader">Updated</span></div>
          {data.items.map(definition => {
            const title = resolveActivityDefinitionTitle(definition.definition.displayName, definition.definition.activityTypeKey);
            const recommendation = definition.lifecycle.recommendation;
            const recommendedVersion = recommendation ? formatActivityVersion(recommendation.version) : null;
            const providerKey = recommendation?.providerKey ?? definition.lifecycle.head?.providerKey;
            const authorityKind = definition.definition.contentAuthority.kind;
            const rowDetails = [
              `Activity Type Key: ${definition.definition.activityTypeKey}`,
              providerKey ? `Provider: ${providerKey}` : null,
              `Authority: ${authorityKind}`
            ].filter(Boolean).join(" · ");
            const additionalIdentifiers = [
              { label: "Activity Type Key", value: definition.definition.activityTypeKey },
              { label: "Head Version ID", value: definition.definition.headVersionId ?? definition.lifecycle.head?.versionId ?? "" },
              { label: "Recommended Version ID", value: definition.definition.recommendedVersionId ?? definition.lifecycle.recommendation?.versionId ?? "" }
            ].filter(identifier => identifier.value);
            return <div key={definition.definition.definitionId} className="ad-collection-row" role="row" tabIndex={0} aria-label={`Open Activity Definition ${title}`} title={rowDetails} onClick={event => { if ((event.target as HTMLElement).closest("button, input, label, a")) return; openDefinition(definition); }} onKeyDown={event => { if (event.target !== event.currentTarget || (event.key !== "Enter" && event.key !== " ")) return; event.preventDefault(); openDefinition(definition); }}>
            <span role="cell" data-label="Activity" className="ad-collection-activity"><strong>{title}</strong>{definition.definition.description ? <small className="ad-collection-description">{definition.definition.description}</small> : null}<small className="ad-collection-category">{definition.definition.category}</small><WorkflowListIdentity primaryLabel="Definition ID" primaryValue={definition.definition.definitionId} secondaryIdentifiers={additionalIdentifiers} primaryClassName="ad-primary-identifier" secondaryClassName="ad-row-identifiers" detailsClassName="ad-row-identifiers-list" detailsAriaLabel={`Activity Definition identifiers for ${title}`} /></span>
            <span role="cell" data-label="State" className="ad-collection-state">
              {definition.lifecycle.versionCount === 0 && definition.lifecycle.draftCount > 0 ? <span className="ad-status-chip is-draft">Draft</span> : null}
              {definition.lifecycle.versionCount > 0 ? <span className="ad-status-chip is-published">Published</span> : null}
              {definition.lifecycle.versionCount === 0 && definition.lifecycle.draftCount === 0 ? <span className="ad-status-copy">No versions yet</span> : null}
              {recommendedVersion ? <small title={`Exact recommended version ${recommendedVersion.full}`}>Recommended {recommendedVersion.short}</small> : definition.lifecycle.versionCount > 0 ? <small>No recommended version</small> : null}
            </span>
            <span role="cell" data-label="Updated">{formatActivityDefinitionDate(definition.updatedAt)}</span>
          </div>;
          })}
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

function ActivityDefinitionFailure({ kind, cursorInvalid, onRetry }: { kind: ReturnType<typeof classifyActivityDefinitionReadFailure>; cursorInvalid: boolean; onRetry(): void }) {
  if (cursorInvalid) return <section className="ad-failure" role="alert"><AlertTriangle size={22} aria-hidden /><h2>Collection continuation is no longer valid</h2><p>Restart paging to request a fresh authorized snapshot while keeping your filters and page size.</p><button type="button" onClick={onRetry}>Restart paging</button></section>;
  const copy = {
    unavailable: ["Unavailable contribution", "This backend does not advertise the Activity Definition management capability supported by Studio. No legacy activity fallback is used."],
    forbidden: ["Activity Definitions unavailable", "This account cannot open the Activity Definitions collection. No identities or counts were loaded."],
    "not-found": ["Activity Definitions unavailable", "The requested collection is not available."],
    expired: ["Collection snapshot expired", "Restart paging to request a fresh authorized snapshot."],
    failed: ["Couldn't load Activity Definitions", "Studio could not confirm the authorized collection. No unconfirmed data is shown."]
  }[kind];
  return <section className="ad-failure" role="alert"><AlertTriangle size={22} aria-hidden /><h2>{copy[0]}</h2><p>{copy[1]}</p><button type="button" onClick={onRetry}>{kind === "expired" ? "Restart paging" : "Try again"}</button></section>;
}

function useDebouncedValue(value: string, delay: number, initialValue = value.trim()) {
  const [debounced, setDebounced] = useState(initialValue);
  useEffect(() => { const timeout = window.setTimeout(() => setDebounced(value.trim()), delay); return () => window.clearTimeout(timeout); }, [delay, value]);
  return debounced;
}

function createActivityHistoryEntryId() {
  activityHistoryEntrySequence += 1;
  return `${Date.now().toString(36)}-${activityHistoryEntrySequence.toString(36)}`;
}

function historyStateRecord(state: unknown): Record<string, unknown> {
  return state !== null && typeof state === "object" && !Array.isArray(state) ? state as Record<string, unknown> : {};
}

function isPageSize(value: unknown): value is PageSize {
  return value === 10 || value === 25 || value === 50;
}

function isActivityCollectionHistoryContext(value: unknown): value is ActivityCollectionHistoryContext {
  if (value === null || typeof value !== "object") return false;
  const context = value as Partial<ActivityCollectionHistoryContext>;
  return typeof context.entryId === "string"
    && typeof context.runtimeScopeId === "string"
    && typeof context.baseUrl === "string"
    && Number.isSafeInteger(context.authorityRevision)
    && context.authorityRevision! >= 0
    && typeof context.searchInput === "string"
    && typeof context.search === "string"
    && typeof context.providerInput === "string"
    && typeof context.providerKey === "string"
    && (context.authority === "" || context.authority === "Design" || context.authority === "ProviderSource")
    && isPageSize(context.pageSize)
    && Array.isArray(context.cursorStack)
    && context.cursorStack.length > 0
    && context.cursorStack.every(cursor => cursor === null || typeof cursor === "string")
    && Number.isInteger(context.cursorIndex)
    && context.cursorIndex! >= 0
    && context.cursorIndex! < context.cursorStack.length;
}

function readActivityHistoryMetadata(state: unknown = window.history.state): ActivityHistoryMetadata | null {
  const raw = historyStateRecord(state)[activityHistoryStateKey];
  if (raw === null || typeof raw !== "object") return null;
  const value = raw as Partial<ActivityHistoryMetadata>;
  return isActivityCollectionHistoryContext(value.collection) ? { collection: value.collection } : null;
}

function writeActivityCollectionHistoryContext(context: ActivityCollectionHistoryContext) {
  const state = historyStateRecord(window.history.state);
  const metadata = readActivityHistoryMetadata(state) ?? {};
  window.history.replaceState({ ...state, [activityHistoryStateKey]: { ...metadata, collection: context } }, "");
}

function readStoredActivityAuthorityRevision() {
  if (typeof window === "undefined") return 0;
  try {
    const raw = window.sessionStorage.getItem(activityAuthorityRevisionStorageKey);
    if (raw === null) return 0;
    const stored = Number(raw);
    return Number.isSafeInteger(stored) && stored >= 0 ? stored : 0;
  } catch {
    return 0;
  }
}

function getActivityAuthorityRevision() {
  if (typeof window === "undefined") return activityAuthorityRevision;
  try {
    const raw = window.sessionStorage.getItem(activityAuthorityRevisionStorageKey);
    if (raw !== null) {
      const stored = Number(raw);
      if (Number.isSafeInteger(stored) && stored >= 0) activityAuthorityRevision = Math.max(activityAuthorityRevision, stored);
    }
  } catch {
    // Keep the in-memory generation when session storage is unavailable.
  }
  return activityAuthorityRevision;
}

function incrementActivityAuthorityRevision() {
  activityAuthorityRevision = getActivityAuthorityRevision() + 1;
  persistActivityAuthorityRevision();
}

function persistActivityAuthorityRevision() {
  try {
    window.sessionStorage.setItem(activityAuthorityRevisionStorageKey, String(activityAuthorityRevision));
  } catch {
    // The current document still has its in-memory generation if storage is unavailable.
  }
}

function readRouteState(): RouteState {
  const parameters = new URLSearchParams(window.location.search);
  return {
    definitionId: parameters.get("definition"),
    section: parameters.get("section"),
    draftId: parameters.get("draft"),
    versionId: parameters.get("version"),
    draftActionVersionId: parameters.get("createDraftFrom"),
    returnPlanId: parameters.get("returnPlan")
  };
}

function formatTimestamp(value: number | null) {
  return value ? new Date(value).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "at an earlier time";
}
