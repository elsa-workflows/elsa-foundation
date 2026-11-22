import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import { lazy, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, ChevronLeft, ChevronRight, Database } from "lucide-react";
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
import { authSessionEndedEvent, authSessionStartedEvent, useStudioWorkspaceWhenActive } from "@elsa-workflows/studio-sdk";
import { StatusChip, StudioDataGrid, StudioDataGridRow, StudioCollectionFooter, CopyableIdentifier, OverflowMenu, StudioButton, StudioCollectionEmptyState, StudioPageHeader, StudioSearchInput, StudioResultState, SkeletonRows, StudioField, StudioTimestamp, type OverflowMenuItem } from "@elsa-workflows/studio-ui";
import {
  isActivityDefinitionEnumValue,
  type ActivityDefinitionCollectionRequest,
  type ActivityDefinitionManagementView
} from "./activityDefinitionTypes";
import { isActivityDefinitionTimestamp, formatActivityVersion, resolveActivityDefinitionTitle } from "./workflowFormatting";
import { activityDesignKeys, archiveActivityDefinition, classifyActivityDefinitionReadFailure, isInvalidActivityDefinitionCursor, redactActivityDefinitionManagementCache, useActivityAuthoringCapabilities, useActivityDefinitions } from "./api/activityDesign";
import { activityDefinitionDurationBucket, activityDefinitionResultBand, observeActivityDefinitions } from "./activityDefinitionObservability";
import { WorkflowLazyBoundary } from "./WorkflowLazyBoundary";
import { WorkflowListIdentifiers } from "./workflow-editor/WorkflowListIdentifiers";
import { getDialogs } from "./workflow-editor/dialogs";
import { getNotifications } from "./workflow-editor/notifications";
import "./activityDefinitions.css";
import "./activityDefinitionCollection.css";
import "./workflow-editor/cleanListTables.css";

const ActivityDefinitionWorkbench = lazy(() => import("./ActivityDefinitionWorkbench").then(module => ({ default: module.ActivityDefinitionWorkbench })));
const ActivityDefinitionCreateDialog = lazy(() => import("./ActivityDefinitionCreateDialog").then(module => ({ default: module.ActivityDefinitionCreateDialog })));
const ActivityDefinitionDraftEditor = lazy(() => import("./ActivityDefinitionDraftEditor").then(module => ({ default: module.ActivityDefinitionDraftEditor })));

type PageSize = 10 | 25 | 50;
type RouteState = {
  definitionId: string | null;
  section: string | null;
  draftId: string | null;
  versionId: string | null;
  originDraftId?: string | null;
  originVersionId?: string | null;
  draftActionVersionId?: string | null;
  returnPlanId?: string | null;
  publicationReview?: boolean;
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
const activityRuntimeScopeId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
let activityHistoryEntrySequence = 0;
let activityAuthorityRevision = 0;

if (typeof window !== "undefined") {
  const recordAuthorityChange = incrementActivityAuthorityRevision;
  window.addEventListener(authSessionStartedEvent, recordAuthorityChange);
  window.addEventListener(authSessionEndedEvent, recordAuthorityChange);
}

export function ActivityDefinitionsPage({
  agentWorkspace = false,
  context,
  activityEditors = () => [],
  graphExtensions = () => ({ properties: [], expressions: [], panels: [] }),
  inputEditors = () => [],
  runtime = {},
  navigateToStudioPath: navigateToStudioPathNow = defaultStudioPathNavigation
}: {
  agentWorkspace?: boolean;
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
  // A create or upgrade can complete while this screen's workspace tab is in the background; its
  // navigation then waits for the tab to return instead of taking over the active tab's address.
  const whenActive = useStudioWorkspaceWhenActive();
  const navigateToStudioPath = (path: string) => whenActive(() => navigateToStudioPathNow(path));
  const routeRef = useRef(route);
  const navigationBlockedRef = useRef(false);
  const routeCollectionContextRef = useRef(readActivityHistoryMetadata()?.collection ?? null);
  const queryClient = useQueryClient();
  const restoredHistoryContext = readActivityHistoryMetadata()?.collection;
  const currentAuthorityRevision = activityAuthorityRevision;
  const initialScopeChanged = Boolean(restoredHistoryContext && (restoredHistoryContext.runtimeScopeId !== activityRuntimeScopeId || restoredHistoryContext.baseUrl !== context.baseUrl || restoredHistoryContext.authorityRevision !== currentAuthorityRevision));
  const [authorizationRevision, setAuthorizationRevision] = useState(currentAuthorityRevision);
  const [authorizationInvalidating, setAuthorizationInvalidating] = useState(initialScopeChanged);
  const pendingRedactionsRef = useRef(0);
  const initialRedactionStartedRef = useRef(false);
  const [historyRevision, setHistoryRevision] = useState(0);
  const historyRevisionRef = useRef(0);
  const baseUrlRef = useRef(context.baseUrl);
  const updateRouteCollectionContext = useCallback((value: ActivityCollectionHistoryContext) => {
    // Deferred collection effects must not write into a newer history entry.
    if (historyRevisionRef.current !== historyRevision) return;
    writeActivityCollectionHistoryContext(value);
    routeCollectionContextRef.current = value;
  }, [historyRevision]);

  const invalidateAuthorizationScope = useCallback(() => {
    setAuthorizationRevision(activityAuthorityRevision);
    setAuthorizationInvalidating(true);
    pendingRedactionsRef.current += 1;
    void redactActivityDefinitionManagementCache(queryClient).then(() => Promise.all([
      queryClient.invalidateQueries({ queryKey: activityDesignKeys.definitionCollections }),
      queryClient.invalidateQueries({ queryKey: activityDesignKeys.authoringCapabilities }),
      // A mounted draft has a separate cache key: refresh its redacted definition identity too.
      queryClient.invalidateQueries({ queryKey: activityDesignKeys.definitionResources })
    ])).finally(() => {
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
      const nextHistoryRevision = ++historyRevisionRef.current;
      routeCollectionContextRef.current = readActivityHistoryMetadata()?.collection ?? null;
      setRoute(readRouteState());
      setHistoryRevision(nextHistoryRevision);
    };
    window.addEventListener("popstate", sync);
    return () => window.removeEventListener("popstate", sync);
  }, []);

  useEffect(() => {
    observeActivityDefinitions({ event: "route-view", surface: route.definitionId ? "workbench" : "collection" });
  }, [route.definitionId]);

  const navigate = (next: RouteState, force = false, replace = false) => whenActive(() => {
    if (navigationBlockedRef.current && !force) return;
    navigationBlockedRef.current = false;
    if (agentWorkspace && !next.definitionId) { navigateToStudioPath("/agents"); return; }
    writeRouteState(next, replace ? "replace" : "push", routeCollectionContextRef.current);
    window.dispatchEvent(new PopStateEvent("popstate"));
  });

  if (route.definitionId) {
    if (route.section === "editor" && (route.draftId || route.versionId)) {
      const extensions = graphExtensions();
      return <WorkflowLazyBoundary label="activity definition draft editor"><ActivityDefinitionDraftEditor agentWorkspace={agentWorkspace} context={context} definitionId={route.definitionId} draftId={route.draftId ?? undefined} versionId={route.versionId ?? undefined} allowStartEditing={!route.draftId && Boolean(route.versionId)} openPublicationReviewOnMount={route.publicationReview} activityEditors={activityEditors()} propertyEditors={extensions.properties} expressionEditors={extensions.expressions} graphAuthoringPanels={extensions.panels} inputEditors={inputEditors()} recoverySettings={runtime.activityDefinitions?.localRecovery} identity={runtime.identity} onNavigationGuardChange={blocked => { navigationBlockedRef.current = blocked; }} onBack={force => {
        if (route.returnPlanId) {
          navigationBlockedRef.current = false;
          navigateToStudioPath(`/workflows/activity-definitions/upgrades?plan=${encodeURIComponent(route.returnPlanId)}`);
          return;
        }
        void queryClient.invalidateQueries({ queryKey: activityDesignKeys.definitionCollections });
        navigate({ definitionId: null, section: null, draftId: null, versionId: null }, force);
      }} onManage={section => navigate({ definitionId: route.definitionId, section, draftId: null, versionId: null, originDraftId: route.draftId, originVersionId: route.versionId, returnPlanId: route.returnPlanId })} onOpenDraft={(definitionId, draftId) => navigate({ definitionId, section: "editor", draftId, versionId: null, returnPlanId: route.returnPlanId }, true)} onOpenVersion={(definitionId, versionId) => navigate({ definitionId, section: "versions", draftId: null, versionId, originDraftId: route.draftId, originVersionId: route.versionId, returnPlanId: route.returnPlanId }, true)} onOpenStudioPath={navigateToStudioPath} /></WorkflowLazyBoundary>;
    }
    return (
      <WorkflowLazyBoundary label="activity definition workbench">
        <ActivityDefinitionWorkbench agentWorkspace={agentWorkspace} context={context} definitionId={route.definitionId} section={route.section} selectedDraftId={route.draftId} selectedVersionId={route.versionId} originDraftId={route.originDraftId} originVersionId={route.originVersionId} returnPlanId={route.returnPlanId} requestedDraftActionVersionId={route.draftActionVersionId} requestedPublicationReview={route.publicationReview} activityEditors={activityEditors()} onNavigate={(next, replace) => {
          const sameDefinition = next.definitionId !== null && next.definitionId === route.definitionId;
          const destination = sameDefinition ? {
            ...next,
            returnPlanId: next.returnPlanId ?? route.returnPlanId,
            ...(next.section === "editor" ? {} : {
              originDraftId: next.originDraftId ?? route.originDraftId,
              originVersionId: next.originVersionId ?? route.originVersionId
            })
          } : next;
          navigate(destination, false, replace);
        }} onOpenWorkflows={() => navigateToStudioPath("/workflows/definitions")} onPlanUpgrade={(kind, id) => navigateToStudioPath(`/workflows/activity-definitions/upgrades?rootKind=${encodeURIComponent(kind)}&rootId=${encodeURIComponent(id)}`)} />
      </WorkflowLazyBoundary>
    );
  }

  // Popstate changes the revision; persisting this entry's metadata must not remount an open form.
  return <ActivityDefinitionCollection key={historyRevision} context={context} baseUrl={context.baseUrl} authorityRevision={authorizationRevision} authorizationInvalidating={authorizationInvalidating} activityEditors={activityEditors()} onHistoryContextChange={updateRouteCollectionContext} onImport={() => navigateToStudioPath("/workflows/activity-definitions/import-elsa3")} onPlanUpgrade={() => navigateToStudioPath("/workflows/activity-definitions/upgrades")} onOpen={(definitionId, section, publicationReview) => navigate({ definitionId, section: section ?? null, draftId: null, versionId: null, publicationReview })} onCreated={(definitionId, draftId) => navigate({ definitionId, section: "editor", draftId, versionId: null })} />;
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
  if (route.originDraftId) parameters.set("originDraft", route.originDraftId);
  if (route.originVersionId) parameters.set("originVersion", route.originVersionId);
  if (route.draftActionVersionId) parameters.set("createDraftFrom", route.draftActionVersionId);
  if (route.returnPlanId) parameters.set("returnPlan", route.returnPlanId);
  if (route.publicationReview) parameters.set("reviewPublication", "true");
  const query = parameters.toString();
  const state = historyStateRecord(window.history.state);
  const metadata = readActivityHistoryMetadata(state) ?? {};
  if (collection === null) delete metadata.collection;
  else if (collection) metadata.collection = collection;
  window.history[mode === "push" ? "pushState" : "replaceState"]({ ...state, [activityHistoryStateKey]: metadata }, "", qualifyStudioBackendPath(`${window.location.pathname === "/agents/definitions" ? "/agents/definitions" : "/workflows/activity-definitions"}${query ? `?${query}` : ""}`));
}

function ActivityDefinitionCollection({ context, baseUrl, authorityRevision, authorizationInvalidating, activityEditors, onHistoryContextChange, onImport, onPlanUpgrade, onOpen, onCreated }: { context: StudioEndpointContext; baseUrl: string; authorityRevision: number; authorizationInvalidating: boolean; activityEditors: StudioActivityDefinitionImplementationEditorContribution[]; onHistoryContextChange(context: ActivityCollectionHistoryContext): void; onImport(): void; onPlanUpgrade(): void; onOpen(definitionId: string, section?: string | null, publicationReview?: boolean): void; onCreated(definitionId: string, draftId: string): void }) {
  const queryClient = useQueryClient();
  const capabilitiesQuery = useActivityAuthoringCapabilities(context);
  const providers = capabilitiesQuery.data?.providers ?? [];
  const [restoredContext] = useState(() => readActivityHistoryMetadata()?.collection ?? null);
  const restoredScopeMatches = !authorizationInvalidating && restoredContext?.runtimeScopeId === activityRuntimeScopeId && restoredContext?.baseUrl === baseUrl && restoredContext?.authorityRevision === authorityRevision;
  const [searchInput, setSearchInput] = useState(restoredContext?.searchInput ?? "");
  const [selectedProviderKey, setSelectedProviderKey] = useState(restoredContext?.providerKey ?? "");
  const search = useDebouncedValue(searchInput, 250, restoredContext?.search);
  const providerKey = selectedProviderKey || "";
  const [authority, setAuthority] = useState<ActivityDefinitionCollectionRequest["authority"]>(restoredContext?.authority ?? "Design");
  const [pageSize, setPageSize] = useState<PageSize>(restoredContext?.pageSize ?? 25);
  const [cursorStack, setCursorStack] = useState<(string | null)[]>(restoredScopeMatches ? restoredContext?.cursorStack ?? [null] : [null]);
  const [cursorIndex, setCursorIndex] = useState(restoredScopeMatches ? restoredContext?.cursorIndex ?? 0 : 0);
  const [lastConfirmedAt, setLastConfirmedAt] = useState<number | null>(null);
  const [privacyFailure, setPrivacyFailure] = useState<"unavailable" | "forbidden" | null>(null);
  const [freshPageRequired, setFreshPageRequired] = useState(false);
  const [scopeRecoveryNotice, setScopeRecoveryNotice] = useState(Boolean(restoredContext && !restoredScopeMatches && restoredContext.cursorIndex > 0));
  const [createOpen, setCreateOpen] = useState(false);
  const [refreshPending, setRefreshPending] = useState(false);
  const [archivingDefinitionId, setArchivingDefinitionId] = useState<string | null>(null);
  const [archiveFailure, setArchiveFailure] = useState<{ definition: ActivityDefinitionManagementView; message: string } | null>(null);
  const refreshPendingRef = useRef(false);
  const archiveInFlightRef = useRef(false);
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
    providerInput: selectedProviderKey,
    providerKey,
    authority,
    pageSize,
    cursorStack: effectiveCursorStack,
    cursorIndex: effectiveCursorIndex
  }), [authority, authorityRevision, baseUrl, effectiveCursorIndex, effectiveCursorStack, pageSize, selectedProviderKey, providerKey, search, searchInput]);
  const authorityScope = `${baseUrl}\u0000${authorityRevision}`;
  const query = useActivityDefinitions(context, request, authorityScope);
  const directFailure = query.error ? classifyActivityDefinitionReadFailure(query.error) : null;
  const invalidCursor = query.error ? isInvalidActivityDefinitionCursor(query.error) : false;
  const failure = privacyFailure ?? directFailure;
  const mustDiscardData = failure === "forbidden" || failure === "unavailable";
  const rejectedContinuation = failure === "expired" || invalidCursor;
  const data = authorizationInvalidating || scopeChanged || mustDiscardData || rejectedContinuation || freshPageRequired ? undefined : query.data;
  const canOpenRowActions = Boolean(data && query.isSuccess && !query.isError && !query.isPlaceholderData && !authorizationInvalidating && !scopeChanged && !mustDiscardData && !rejectedContinuation && !freshPageRequired);
  const defaultDesignedView = authority === "Design" && !searchInput.trim() && !selectedProviderKey;
  const filtersActive = Boolean(searchInput.trim() || selectedProviderKey || authority !== "Design");

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

  const openDefinition = (definition: ActivityDefinitionManagementView, section: string | null = null, publicationReview = false) => {
    observeActivityDefinitions({ event: "open-definition", surface: "collection" });
    onHistoryContextChange(historyContext);
    onOpen(definition.definition.definitionId, section, publicationReview);
  };
  const refresh = () => {
    if (refreshPendingRef.current) return;
    refreshPendingRef.current = true;
    setRefreshPending(true);
    setPrivacyFailure(null);
    observeActivityDefinitions({ event: "refresh", surface: "collection" });
    void query.refetch({ cancelRefetch: false }).then(() => {
      refreshPendingRef.current = false;
      setRefreshPending(false);
    }, () => {
      refreshPendingRef.current = false;
      setRefreshPending(false);
    });
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
  const archiveDefinition = async (definition: ActivityDefinitionManagementView, confirm = true) => {
    if (archiveInFlightRef.current) return;
    archiveInFlightRef.current = true;
    setArchivingDefinitionId(definition.definition.definitionId);
    try {
      if (confirm && !(await getDialogs().confirm({
        title: "Archive Activity Definition",
        message: `Archive "${resolveActivityDefinitionTitle(definition.definition.displayName, definition.definition.activityTypeKey)}"? It will disappear from new authoring and activity selection. Existing drafts, published versions, and workflow references will remain intact; archived definitions cannot currently be restored from Studio.`,
        confirmLabel: "Archive",
        tone: "danger"
      }))) return;

      setArchiveFailure(null);
      await archiveActivityDefinition(context, definition.definition.definitionId);
      getNotifications().notify({ level: "success", title: `Archived ${resolveActivityDefinitionTitle(definition.definition.displayName, definition.definition.activityTypeKey)}` });
      setPrivacyFailure(null);
      setFreshPageRequired(true);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: activityDesignKeys.definitionCollections, refetchType: "none" }),
        queryClient.invalidateQueries({ queryKey: activityDesignKeys.definitionResources }),
        queryClient.invalidateQueries({ queryKey: activityDesignKeys.recommendedDefinitions })
      ]);
      setCursorStack([null]);
      setCursorIndex(0);
      if (cursorIndex === 0) void query.refetch();
    } catch {
      setArchiveFailure({ definition, message: "The archive request failed. The definition remains available in this list; retry when the service is available." });
    } finally {
      archiveInFlightRef.current = false;
      setArchivingDefinitionId(null);
    }
  };
  const nextPage = () => {
    const continuation = data?.continuation;
    if (!continuation) return;
    setCursorStack(current => [...current.slice(0, cursorIndex + 1), continuation]);
    setCursorIndex(current => current + 1);
  };

  return (
    <main className="studio-page ad-page ad-designed-collection" aria-labelledby="activity-definitions-title">
      <StudioPageHeader title="Activity definitions" titleId="activity-definitions-title" description="Design reusable activities here. Drafts stay editable, while published versions remain in history." refresh={{ id: "activity-definitions", label: "Refresh activity definitions", onRefresh: refresh, refreshing: refreshPending, keepButtonStable: true, autoRefresh: true }} actions={<div className="studio-page-actions"><StudioButton variant="primary" size="md" onClick={() => setCreateOpen(true)}>New activity definition</StudioButton><details className="ad-collection-disclosure ad-tools-disclosure"><summary>Tools</summary><div className="ad-disclosure-panel"><StudioButton type="button" onClick={onImport}>Import from Elsa 3</StudioButton><StudioButton type="button" onClick={onPlanUpgrade}>Plan broad upgrade</StudioButton></div></details></div>} />

      <section className="studio-collection-toolbar ad-collection-filters" aria-label="Activity Definition filters">
        <StudioSearchInput aria-label="Search" value={searchInput} onChange={event => setSearchInput(event.target.value)} placeholder="Name, type key, or category" />
        <div className="studio-filter-group" role="group" aria-label="Activity definition collection">
          <StudioButton type="button" aria-pressed={authority === "Design"} onClick={() => setAuthority("Design")}>Designed</StudioButton>
          <StudioButton type="button" aria-pressed={authority === "ProviderSource"} onClick={() => setAuthority("ProviderSource")}>Built-in</StudioButton>
          <StudioButton type="button" aria-pressed={authority === ""} onClick={() => setAuthority("")}>All</StudioButton>
        </div>
        <details className="ad-collection-disclosure ad-advanced-filters">
          <summary>Advanced filters</summary>
          <div className="ad-disclosure-panel ad-advanced-filter-fields">
            <StudioField label={<><span>Provider</span></>}><select value={selectedProviderKey} onChange={event => setSelectedProviderKey(event.target.value)}><option value="">All providers</option>{selectedProviderKey && !providers.some(provider => provider.providerKey === selectedProviderKey) ? <option value={selectedProviderKey}>{selectedProviderKey}</option> : null}{providers.map(provider => (<option key={provider.providerKey} value={provider.providerKey}>{provider.displayName || provider.providerKey}</option>))}</select></StudioField>
          </div>
        </details>
      </section>

      {authorizationInvalidating ? <div className="ad-inline-status" role="status">The access scope changed. Reloading the authorized collection while keeping your filters and page size.</div> : null}
      {!authorizationInvalidating && scopeRecoveryNotice ? <div className="ad-inline-status" role="status">The collection scope changed, so Studio restarted at the first page and kept your filters and page size.</div> : null}
      {query.isError && data && failure !== "expired" && !invalidCursor ? <div className="ad-stale-warning" role="alert"><AlertTriangle size={18} aria-hidden /><div><strong>Refresh failed; showing retained data</strong><span>Last confirmed {formatTimestamp(lastConfirmedAt)}. Retry to request a new snapshot.</span></div><StudioButton type="button" onClick={refresh}>Retry</StudioButton></div> : null}
      {!data && !authorizationInvalidating && (query.isPending || query.isFetching) && !failure ? <ActivityDefinitionSkeleton /> : null}
      {!data && !authorizationInvalidating && failure ? <ActivityDefinitionFailure kind={failure} cursorInvalid={invalidCursor} onRetry={failure === "expired" || invalidCursor ? restartPaging : refresh} /> : null}
      {archiveFailure ? <p className="ad-inline-status" role="alert">Couldn’t archive this Activity Definition: {archiveFailure.message} <StudioButton type="button" disabled={archivingDefinitionId !== null} onClick={() => void archiveDefinition(archiveFailure.definition, false)}>Retry archive</StudioButton></p> : null}
      {data && !query.isError && !authorizationInvalidating && data.items.length === 0 ? <StudioCollectionEmptyState
        icon={<Database size={24} aria-hidden />}
        title={defaultDesignedView ? "No designed activities yet" : filtersActive ? "No Activity Definitions match" : "No Activity Definitions available"}
        description={defaultDesignedView ? "Create a reusable activity definition, or browse built-in activities to see source-owned entries." : filtersActive ? "Clear or change the server-side filters to search the authorized collection again." : "No authorized Activity Definitions are available in this scope."}
        actions={defaultDesignedView ? <><StudioButton variant="primary" onClick={() => setCreateOpen(true)}>New activity definition</StudioButton><StudioButton onClick={() => setAuthority("ProviderSource")}>Browse built-in activities</StudioButton></> : filtersActive ? <StudioButton onClick={() => { setSearchInput(""); setSelectedProviderKey(""); setAuthority("Design"); }}>Clear filters</StudioButton> : null}
      /> : null}
      {data && data.items.length > 0 ? <>
        <StudioDataGrid className="studio-table-container studio-collection-table ad-collection" aria-label="Activity Definitions" aria-rowcount={data.totalCount}>
          <StudioDataGridRow className="ad-collection-head"><span role="columnheader">Activity</span><span role="columnheader">Definition ID</span><span role="columnheader">Provider</span><span role="columnheader">Identifiers</span><span role="columnheader">State</span><span role="columnheader">Updated</span><span className="ad-collection-actions-header" role="columnheader">{canOpenRowActions ? "Actions" : ""}</span></StudioDataGridRow>
          {data.items.map(definition => {
            const title = resolveActivityDefinitionTitle(definition.definition.displayName, definition.definition.activityTypeKey);
            const recommendation = definition.lifecycle.recommendation;
            const recommendedVersion = recommendation ? formatActivityVersion(recommendation.version) : null;
            const providerKey = recommendation?.providerKey ?? definition.lifecycle.head?.providerKey;
            const providerLabel = providerKey ?? "Not established";
            const authorityKind = definition.definition.contentAuthority.kind;
            const designOwned = isActivityDefinitionEnumValue(authorityKind, "Design");
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
            const menuItems: OverflowMenuItem[] = canOpenRowActions ? [
              ...(designOwned ? [
                { id: "edit", label: "Edit", onSelect: () => openDefinition(definition) },
                ...(definition.lifecycle.draftCount > 0 ? [{ id: "publish", label: "Publish", onSelect: () => openDefinition(definition, "drafts", true) }] : []),
                ...(definition.actions.some(action => action.action === "archive-definition" && action.allowed)
                  ? [{ id: "archive", label: "Archive", onSelect: () => void archiveDefinition(definition) }]
                  : [])
              ] : []),
              { id: "versions", label: "Version history", onSelect: () => openDefinition(definition, "versions") },
              { id: "usage", label: "Usage", onSelect: () => openDefinition(definition, "relationships") }
            ] : [];
            return <StudioDataGridRow key={definition.definition.definitionId} className="ad-collection-row" aria-label={`Open Activity Definition ${title}`} title={rowDetails} activate={() => openDefinition(definition)}>
            <span role="cell" data-label="Activity" className="ad-collection-activity"><strong title={title}>{title}</strong></span>
            <span role="cell" data-label="Definition ID" className="wf-list-primary-id ad-primary-identifier"><CopyableIdentifier label="Definition ID" value={definition.definition.definitionId} /></span>
            <span role="cell" data-label="Provider" className="ad-collection-provider">{providerLabel}</span>
            <span role="cell" data-label="Identifiers"><WorkflowListIdentifiers kind="Activity Definition" name={title} identifiers={additionalIdentifiers} description={definition.definition.description}>
              <p>Category: {definition.definition.category || "Uncategorized"}</p>
              <p>{providerKey ? `Provider: ${providerKey} · ` : ""}Authority: {authorityKind}</p>
            </WorkflowListIdentifiers></span>
            <span role="cell" data-label="State" className="ad-collection-state">
              {definition.lifecycle.versionCount === 0 && definition.lifecycle.draftCount > 0 ? <StatusChip tone="warning">Draft</StatusChip> : null}
              {definition.lifecycle.versionCount > 0 ? <StatusChip tone="success">Published</StatusChip> : null}
              {definition.lifecycle.versionCount === 0 && definition.lifecycle.draftCount === 0 ? <span className="ad-status-copy">No versions yet</span> : null}
              {recommendedVersion ? <small title={`Exact recommended version ${recommendedVersion.full}`}>Recommended {recommendedVersion.short}</small> : definition.lifecycle.versionCount > 0 ? <small>No recommended version</small> : null}
            </span>
            <span role="cell" data-label="Updated"><StudioTimestamp value={isActivityDefinitionTimestamp(definition.updatedAt) ? definition.updatedAt : null} fallback="—" /></span>
            <span role="cell" data-label="Actions" className="ad-collection-actions" onClick={canOpenRowActions ? event => event.stopPropagation() : undefined}>
              {canOpenRowActions ? <OverflowMenu items={menuItems} ariaLabel={`Actions for ${title}`} /> : null}
            </span>
          </StudioDataGridRow>;
          })}
        </StudioDataGrid>
      </> : null}
      <StudioCollectionFooter aria-label="Activity Definition pagination"
        summary={data ? <span>Page {cursorIndex + 1} · {data.count} of {data.totalCount} definitions</span> : undefined}
        pageSize={{ label: "Rows per page", value: pageSize, sizes: [10, 25, 50], onChange: size => setPageSize(size as PageSize) }}>
        {data && data.items.length > 0 ? <div>
          <StudioButton size="sm" onClick={() => setCursorIndex(current => Math.max(0, current - 1))} disabled={cursorIndex === 0}><ChevronLeft size={16} aria-hidden /> Previous</StudioButton>
          <StudioButton size="sm" onClick={nextPage} disabled={!data.hasMore || !data.continuation}>Next <ChevronRight size={16} aria-hidden /></StudioButton>
        </div> : null}
      </StudioCollectionFooter>
      {createOpen ? <WorkflowLazyBoundary label="create activity definition"><ActivityDefinitionCreateDialog context={context} activityEditors={activityEditors} onClose={() => setCreateOpen(false)} onCreated={response => { setCreateOpen(false); onCreated(response.definition.definitionId, response.draft.draftId); }} /></WorkflowLazyBoundary> : null}
    </main>
  );
}

function ActivityDefinitionSkeleton() {
  return <><span>Loading Activity Definitions…</span><SkeletonRows label="Loading Activity Definitions" /></>;
}

function ActivityDefinitionFailure({ kind, cursorInvalid, onRetry }: { kind: ReturnType<typeof classifyActivityDefinitionReadFailure>; cursorInvalid: boolean; onRetry(): void }) {
  if (cursorInvalid) return <StudioResultState tone="danger" icon={<AlertTriangle size={22} aria-hidden />} title="Collection continuation is no longer valid" description="Restart paging to request a fresh authorized snapshot while keeping your filters and page size." actions={<StudioButton onClick={onRetry}>Restart paging</StudioButton>} />;
  const copy = {
    unavailable: ["Activity definitions unavailable", "This backend does not support activity definition management."],
    forbidden: ["Activity Definitions unavailable", "This account cannot open the Activity Definitions collection. No identities or counts were loaded."],
    "not-found": ["Activity Definitions unavailable", "The requested collection is not available."],
    expired: ["Collection snapshot expired", "Restart paging to request a fresh authorized snapshot."],
    failed: ["Couldn't load Activity Definitions", "Studio could not confirm the authorized collection. No unconfirmed data is shown."]
  }[kind];
  return <StudioResultState tone="danger" icon={<AlertTriangle size={22} aria-hidden />} title={copy[0]} description={copy[1]} actions={<StudioButton onClick={onRetry}>{kind === "expired" ? "Restart paging" : "Retry"}</StudioButton>} />;
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

function incrementActivityAuthorityRevision() {
  activityAuthorityRevision += 1;
}

function readRouteState(): RouteState {
  const parameters = new URLSearchParams(window.location.search);
  return {
    definitionId: parameters.get("definition"),
    section: parameters.get("section"),
    draftId: parameters.get("draft"),
    versionId: parameters.get("version"),
    originDraftId: parameters.get("originDraft"),
    originVersionId: parameters.get("originVersion"),
    draftActionVersionId: parameters.get("createDraftFrom"),
    returnPlanId: parameters.get("returnPlan"),
    publicationReview: parameters.get("reviewPublication") === "true"
  };
}

function formatTimestamp(value: number | null) {
  return value ? new Date(value).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "at an earlier time";
}
