import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent } from "react";
import { AlertCircle, Check, Package, Plus, Search } from "lucide-react";
import { StudioHttpError, type StudioAiContributionApi, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { OverflowMenu, type OverflowMenuItem } from "@elsa-workflows/studio-ui";
import { createDefinition, deleteDefinition, deleteDefinitionPermanently, listDefinitions, restoreDefinition, workflowDefinitionFolderMovePath } from "../api/workflowDesign";
import type { ActivityCatalogItem, DefinitionListState, WorkflowDefinitionDetails, WorkflowDefinitionSummary } from "../workflowTypes";
import { formatDate } from "../workflowFormatting";
import { getDialogs } from "./dialogs";
import type { CreateWorkflowDraft } from "./editorTypes";
import { dispatchAiAction, findAiAction, getTotalPages } from "./editorListHelpers";
import { WfEmptyState, WfErrorCard, WfListSkeleton } from "./StatusViews";
import { DefinitionPager } from "./DefinitionPager";
import { WorkflowFolderNavigation, type WorkflowFolderSelection, selectedFolderId } from "./WorkflowFolderNavigation";
import { updateWorkflowDefinitionBrowseUrl, workflowFolderSelectionKey } from "./workflowDefinitionBrowseLocation";
import "./workflowFolders.css";
import "./workflowDefinitionTable.css";
import { LatestDefinitionVersion, PublishedDefinitionVersion } from "./DefinitionVersionCells";
import type { WorkflowDefinitionBrowseSession } from "./workflowDefinitionBrowseSession";

const CreateWorkflowDialog = lazy(() =>
  import("./CreateWorkflowDialog").then(module => ({ default: module.CreateWorkflowDialog }))
);
const MoveWorkflowDefinitionsDialog = lazy(() =>
  import("./MoveWorkflowDefinitionsDialog").then(module => ({ default: module.MoveWorkflowDefinitionsDialog }))
);

interface BrowseIdentity {
  baseUrl: string;
  authorityRevision: number;
  requestedFolderKey: string;
  effectiveFolderKey: string;
  listState: DefinitionListState;
  search: string;
  page: number;
  pageSize: number;
}

interface BrowseSnapshot {
  identity: BrowseIdentity;
  definitions: WorkflowDefinitionDetails["definition"][];
  totalCount: number;
  usesCursorPaging: boolean;
}

const noDefinitions: BrowseSnapshot["definitions"] = [];

function sameBrowseIdentity(left: BrowseIdentity, right: BrowseIdentity) {
  return left.baseUrl === right.baseUrl
    && left.authorityRevision === right.authorityRevision
    && left.requestedFolderKey === right.requestedFolderKey
    && left.effectiveFolderKey === right.effectiveFolderKey
    && left.listState === right.listState
    && left.search === right.search
    && left.page === right.page
    && left.pageSize === right.pageSize;
}

function canRetainBrowseSnapshot(previous: BrowseIdentity, next: BrowseIdentity) {
  return previous.baseUrl === next.baseUrl
    && previous.authorityRevision === next.authorityRevision
    && previous.requestedFolderKey === next.requestedFolderKey
    && previous.effectiveFolderKey === next.effectiveFolderKey
    && previous.listState === next.listState
    && previous.pageSize === next.pageSize
    && (previous.search === next.search ? previous.page === next.page : true);
}

export function WorkflowDefinitions({ context, ai, onOpen, session }: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  onOpen(id: string): void;
  session: WorkflowDefinitionBrowseSession;
}) {
  const {
    browseLocation,
    page,
    pageSize,
    continuationTokens,
    settledSearch,
    searchPending,
    requestNonce,
    authorityRevision,
    authorityBaseUrl,
    scopeRevision,
    effectiveFolderKey: savedEffectiveFolderKey,
    requestsPaused,
    blockedReason,
    changeSearch,
    navigateBrowseLocation,
    changePage,
    changePageSize,
    rebase,
    recordNextCursor,
    observeEffectiveFolder,
    beginRequest,
    finishRequest,
    isRequestCurrent,
    reportAccessDenied,
    retry,
    resumeAfterReturn
  } = session;
  const { folderSelection, listState, search } = browseLocation;
  const [requestState, setRequestState] = useState<"loading" | "ready" | "failed">("loading");
  const [requestError, setRequestError] = useState<{ identity: BrowseIdentity; message: string } | null>(null);
  const [status, setStatus] = useState("");
  const [lastSuccess, setLastSuccess] = useState<BrowseSnapshot | null>(null);
  const [selectedDefinitionIds, setSelectedDefinitionIds] = useState<Set<string>>(() => new Set());
  const [selectionScopeRevision, setSelectionScopeRevision] = useState(scopeRevision);
  const [createDraft, setCreateDraft] = useState<CreateWorkflowDraft | null>(null);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState("");
  const [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
  const [catalogState, setCatalogState] = useState<"idle" | "loading" | "ready" | "failed">("idle");
  const [catalogError, setCatalogError] = useState("");
  const createInFlightRef = useRef(false);
  const [folderCapability, setFolderCapability] = useState<"unknown" | "available" | "unavailable">("unknown");
  const [moveCapabilityAvailable, setMoveCapabilityAvailable] = useState(false);
  const [moveDialogOpen, setMoveDialogOpen] = useState(false);
  const [focusRefreshAfterMove, setFocusRefreshAfterMove] = useState(false);
  const selectVisibleRef = useRef<HTMLInputElement | null>(null);
  const refreshButtonRef = useRef<HTMLButtonElement | null>(null);
  const selectionMatchesScope = selectionScopeRevision === scopeRevision;
  const effectiveFolderKey = folderCapability === "unknown"
    ? savedEffectiveFolderKey ?? "unknown"
    : folderCapability === "available"
      ? workflowFolderSelectionKey(folderSelection)
      : "all";
  const effectiveFolderResolved = folderCapability !== "unknown" && savedEffectiveFolderKey === effectiveFolderKey;
  const targetIdentity = useMemo<BrowseIdentity>(() => ({
    baseUrl: context.baseUrl,
    authorityRevision,
    requestedFolderKey: workflowFolderSelectionKey(folderSelection),
    effectiveFolderKey,
    listState,
    search: search.trim(),
    page,
    pageSize
  }), [authorityRevision, context.baseUrl, effectiveFolderKey, folderSelection, listState, page, pageSize, search]);
  const endpointIdentityCurrent = context.baseUrl === authorityBaseUrl;
  const snapshotVisible = lastSuccess && (sameBrowseIdentity(lastSuccess.identity, targetIdentity)
    || canRetainBrowseSnapshot(lastSuccess.identity, targetIdentity)) ? lastSuccess : null;
  const isCurrentData = !!snapshotVisible
    && sameBrowseIdentity(snapshotVisible.identity, targetIdentity)
    && requestState === "ready"
    && !searchPending
    && !requestsPaused;
  const hasPreviousData = !!snapshotVisible && !isCurrentData;
  const definitions = snapshotVisible?.definitions ?? noDefinitions;
  const totalCount = snapshotVisible?.totalCount ?? 0;
  const usesCursorPaging = snapshotVisible?.usesCursorPaging ?? false;
  const visibleDefinitionIds = useMemo(() => definitions.map(definition => definition.id), [definitions]);
  const continuationToken = continuationTokens[page];
  const effectiveFolderSelection: WorkflowFolderSelection =
    folderCapability === "available" ? folderSelection : "all";
  const suggestMetadataAction = findAiAction(ai, "weaver.workflows.suggest-create-metadata");
  const explainDefinitionAction = findAiAction(ai, "weaver.workflows.explain-definition");
  const selectedVisibleCount = selectionMatchesScope ? visibleDefinitionIds.filter(id => selectedDefinitionIds.has(id)).length : 0;
  const allVisibleSelected = visibleDefinitionIds.length > 0 && selectedVisibleCount === visibleDefinitionIds.length;
  const searchLabel = effectiveFolderSelection === "all"
    ? "Search all workflows"
    : effectiveFolderSelection === "unfiled"
      ? "Search Unfiled"
      : "Search selected folder";
  const emptyScope = effectiveFolderSelection === "all"
    ? "All workflows"
    : effectiveFolderSelection === "unfiled"
      ? "Unfiled"
      : "the selected folder";
  const emptyLifecycle = listState === "all"
    ? "workflow definitions in any lifecycle state"
    : `${listState} workflow definitions`;
  const emptyTitle = search
    ? `No workflows match “${search}”`
    : `No ${emptyLifecycle} in ${emptyScope}`;
  const emptyDescription = search
    ? `No ${emptyLifecycle} in ${emptyScope} match this search.`
    : "Create a workflow to start designing automation, or adjust your filters to see more.";

  const handleFolderAvailability = useCallback((available: boolean) => {
    setFolderCapability(available ? "available" : "unavailable");
  }, []);

  useEffect(() => {
    const canonical = updateWorkflowDefinitionBrowseUrl(new URL(window.location.href), browseLocation);
    const current = `${window.location.pathname}${window.location.search}${window.location.hash}`;
    const next = `${canonical.pathname}${canonical.search}${canonical.hash}`;
    if (current !== next) window.history.replaceState({}, "", next);
  }, [browseLocation]);

  useEffect(() => {
    if (folderCapability === "unknown") return;
    observeEffectiveFolder(effectiveFolderKey);
  }, [effectiveFolderKey, folderCapability, observeEffectiveFolder]);

  useEffect(() => {
    resumeAfterReturn();
  }, [resumeAfterReturn]);

  useEffect(() => {
    setSelectedDefinitionIds(new Set());
    setSelectionScopeRevision(scopeRevision);
  }, [scopeRevision]);

  const load = useCallback(async () => {
    if (folderCapability === "unknown" || !effectiveFolderResolved || !endpointIdentityCurrent || requestsPaused || searchPending || settledSearch !== search) return;
    const requestedPage = page;
    const requestedContinuationToken = continuationToken;
    const requestedFolderSelection = folderSelection;
    const identity: BrowseIdentity = { ...targetIdentity, search: settledSearch.trim() };
    const controller = new AbortController();
    const generation = beginRequest(controller);
    setRequestState("loading");
    setRequestError(null);
    try {
      const response = await listDefinitions(context, {
        search: settledSearch,
        state: listState,
        page: requestedPage,
        pageSize,
        continuationToken: requestedContinuationToken,
        folderId: folderCapability === "available" ? selectedFolderId(requestedFolderSelection) : null,
        unfiled: folderCapability === "available" && requestedFolderSelection === "unfiled"
      }, controller.signal);
      if (!isRequestCurrent(generation)) return;
      if (response.isPaged) {
        setLastSuccess({ identity, definitions: response.definitions, totalCount: 0, usesCursorPaging: true });
        recordNextCursor(requestedPage, response.nextContinuationToken);
      } else {
        const effectiveTotalCount = response.totalCount;
        const effectiveTotalPages = getTotalPages(effectiveTotalCount, pageSize);
        if (effectiveTotalCount > 0 && requestedPage > effectiveTotalPages) {
          changePage(effectiveTotalPages);
          return;
        }
        setLastSuccess({ identity, definitions: response.definitions, totalCount: effectiveTotalCount, usesCursorPaging: false });
      }
      setRequestState("ready");
    } catch (e) {
      if (!isRequestCurrent(generation)) return;
      if (e instanceof StudioHttpError && (e.status === 401 || e.status === 403)) {
        setLastSuccess(null);
        reportAccessDenied(e.message || "Your access changed. Refresh to retry this request.");
        return;
      }
      setRequestError({ identity, message: e instanceof Error ? e.message : String(e) });
      setRequestState("failed");
    } finally {
      finishRequest(controller);
    }
  }, [
    context,
    continuationToken,
    endpointIdentityCurrent,
    effectiveFolderResolved,
    folderCapability,
    folderSelection,
    listState,
    page,
    pageSize,
    requestsPaused,
    searchPending,
    search,
    beginRequest,
    changePage,
    finishRequest,
    isRequestCurrent,
    recordNextCursor,
    reportAccessDenied,
    settledSearch,
    targetIdentity
  ]);

  useEffect(() => {
    if (requestsPaused) {
      setRequestState("failed");
      return;
    }
    if (!endpointIdentityCurrent || searchPending || settledSearch !== search) return;
    void load();
  }, [endpointIdentityCurrent, load, requestNonce, requestsPaused, search, searchPending, settledSearch]);

  const currentError = blockedReason
    ?? (!searchPending && requestError && sameBrowseIdentity(requestError.identity, targetIdentity) ? requestError.message : null);

  useEffect(() => {
    if (selectVisibleRef.current) {
      selectVisibleRef.current.indeterminate = selectedVisibleCount > 0 && !allVisibleSelected;
    }
  }, [allVisibleSelected, selectedVisibleCount]);

  const loadCatalog = useCallback(async () => {
    if (catalogState === "loading" || catalogState === "ready") return;
    setCatalogState("loading");
    setCatalogError("");
    try {
      const { listActivities } = await import("../api/activityDesign");
      const response = await listActivities(context);
      setCatalog(response.activities ?? []);
      setCatalogState("ready");
    } catch (e) {
      setCatalogState("failed");
      setCatalogError(e instanceof Error ? e.message : String(e));
    }
  }, [catalogState, context]);

  const openCreateDialog = () => {
    setRequestError(null);
    setCreateError("");
    setStatus("");
    setCreateDraft({ name: "", description: "", rootActivityVersionId: null });
    void loadCatalog();
  };

  const submitCreate = async () => {
    if (createInFlightRef.current || !createDraft?.name.trim() || catalogState !== "ready") return;
    createInFlightRef.current = true;
    setCreating(true);
    setCreateError("");
    setStatus("");
    try {
      const folderId = folderCapability === "available" ? selectedFolderId(folderSelection) : null;
      const { getCreateInitialState } = await import("./editorHelpers");
      const initialState = getCreateInitialState(createDraft, catalog);
      if (!initialState?.rootActivity) {
        throw new Error("Choose a starting activity that has an authoring template before creating this workflow.");
      }
      const details = await createDefinition(context, {
        name: createDraft.name.trim(),
        description: createDraft.description.trim() || null,
        initialState,
        ...(folderId ? { folderId } : {})
      });
      setCreateDraft(null);
      onOpen(details.definition.id);
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : String(e));
    } finally {
      createInFlightRef.current = false;
      setCreating(false);
    }
  };

  const openDefinitionArtifacts = (definitionId: string) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(definitionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const refreshAfterMutation = async () => {
    if (definitions.length === 1 && page > 1) {
      changePage(page - 1);
      return;
    }

    retry();
  };

  const clearSelection = () => setSelectedDefinitionIds(new Set());

  const openMoveDialog = () => {
    setRequestError(null);
    setStatus("");
    setMoveDialogOpen(true);
  };

  useEffect(() => {
    if (moveDialogOpen || !focusRefreshAfterMove) return;
    clearSelection();
    refreshButtonRef.current?.focus();
    setFocusRefreshAfterMove(false);
  }, [focusRefreshAfterMove, moveDialogOpen]);

  const movedDefinitions = async () => {
    rebase(true);
    const count = selectedDefinitionIds.size;
    setStatus(`Moved ${count === 1 ? "1 workflow definition" : `${count} workflow definitions`}`);
    setFocusRefreshAfterMove(true);
  };

  const foldersMutated = async (nextFolderSelection?: WorkflowFolderSelection) => {
    if (nextFolderSelection) {
      navigateBrowseLocation({ ...browseLocation, folderSelection: nextFolderSelection }, "replace", true);
      return;
    }
    rebase(true);
  };

  const toggleDefinitionSelection = (definitionId: string, selected: boolean) => {
    setSelectedDefinitionIds(current => {
      const next = new Set(current);
      if (selected) next.add(definitionId);
      else next.delete(definitionId);
      return next;
    });
  };

  const toggleVisibleSelection = (selected: boolean) => {
    setSelectedDefinitionIds(current => {
      const next = new Set(current);
      for (const definitionId of visibleDefinitionIds) {
        if (selected) next.add(definitionId);
        else next.delete(definitionId);
      }
      return next;
    });
  };

  const changeListState = (nextState: DefinitionListState) => {
    navigateBrowseLocation({ ...browseLocation, listState: nextState }, "push", true);
  };

  const changeFolder = (nextFolder: WorkflowFolderSelection) => {
    navigateBrowseLocation({ ...browseLocation, folderSelection: nextFolder }, "push", true);
  };

  const softDelete = async (definition: WorkflowDefinitionDetails["definition"]) => {
    if (!(await getDialogs().confirm({ message: `Delete workflow definition "${definition.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" }))) return;
    setStatus("");
    setRequestError(null);
    try {
      await deleteDefinition(context, definition.id);
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Deleted ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      setRequestError({ identity: targetIdentity, message: e instanceof Error ? e.message : String(e) });
    }
  };

  const restore = async (definition: WorkflowDefinitionDetails["definition"]) => {
    setStatus("");
    setRequestError(null);
    try {
      await restoreDefinition(context, definition.id);
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Restored ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      setRequestError({ identity: targetIdentity, message: e instanceof Error ? e.message : String(e) });
    }
  };

  const permanentDelete = async (definition: WorkflowDefinitionDetails["definition"]) => {
    if (!(await getDialogs().confirm({ message: `Permanently delete workflow definition "${definition.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" }))) return;
    setStatus("");
    setRequestError(null);
    try {
      await deleteDefinitionPermanently(context, definition.id);
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Permanently deleted ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      setRequestError({ identity: targetIdentity, message: e instanceof Error ? e.message : String(e) });
    }
  };

  const displayState = requestsPaused ? "failed" : searchPending ? "loading" : requestState;
  const previousResultDescription = hasPreviousData && snapshotVisible
    ? snapshotVisible.definitions.length === 0
      ? `The previous search “${snapshotVisible.identity.search || "all workflows"}” on page ${snapshotVisible.identity.page} returned no workflows`
      : `Showing previous results for “${snapshotVisible.identity.search || "all workflows"}” on page ${snapshotVisible.identity.page}`
    : null;
  const staleStatus = previousResultDescription
    ? displayState === "failed"
      ? `${previousResultDescription}. The latest search “${search.trim() || "all workflows"}” failed.`
      : searchPending
        ? `${previousResultDescription} while “${search.trim() || "all workflows"}” waits to refresh.`
        : `${previousResultDescription} while “${search.trim() || "all workflows"}” refreshes.`
    : null;
  const errorTitle = search.trim()
    ? `Couldn't load workflow definitions for “${search.trim()}”`
    : "Couldn't load workflow definitions";
  const selectedIds = selectionMatchesScope && isCurrentData ? selectedDefinitionIds : new Set<string>();
  const selectedCount = selectedIds.size;
  const selectedVisibleCurrentCount = visibleDefinitionIds.filter(id => selectedIds.has(id)).length;
  const allCurrentVisibleSelected = visibleDefinitionIds.length > 0 && selectedVisibleCurrentCount === visibleDefinitionIds.length;
  const renderResultRows = isCurrentData || hasPreviousData;

  useEffect(() => {
    let cancelled = false;
    void workflowDefinitionFolderMovePath(context)
      .then(path => { if (!cancelled) setMoveCapabilityAvailable(!!path); })
      .catch(() => { if (!cancelled) setMoveCapabilityAvailable(false); });
    return () => { cancelled = true; };
  }, [context]);

  return (
    <>
      <div className="wf-definitions-list">
        <WorkflowFolderNavigation
          context={context}
          selection={folderSelection}
          onSelect={changeFolder}
          onAvailable={handleFolderAvailability}
          onFoldersMutated={foldersMutated}
        />
        {folderCapability === "unavailable" && folderSelection !== "all" ? (
          <div className="wf-alert" role="status">
            Workflow folders are unavailable. Showing all workflows while preserving the requested folder context.
          </div>
        ) : null}
        <div className="wf-toolbar">
          <div className="wf-segmented" role="group" aria-label="Definition state">
            <button type="button" className={listState === "active" ? "active" : ""} aria-pressed={listState === "active"} onClick={() => changeListState("active")}>Active</button>
            <button type="button" className={listState === "deleted" ? "active" : ""} aria-pressed={listState === "deleted"} onClick={() => changeListState("deleted")}>Deleted</button>
            <button type="button" className={listState === "all" ? "active" : ""} aria-pressed={listState === "all"} onClick={() => changeListState("all")}>All states</button>
          </div>
          <label className="wf-search">
            <Search size={15} />
            <input aria-label={searchLabel} value={search} onChange={event => changeSearch(event.target.value)} placeholder="Search definitions" />
          </label>
          {effectiveFolderSelection !== "all" ? <button type="button" onClick={() => changeFolder("all")}>Search all workflows</button> : null}
          <button ref={refreshButtonRef} type="button" onClick={retry}>Refresh</button>
          <div className="wf-actions">
            <button type="button" title="Create workflow" onClick={openCreateDialog}><Plus size={15} /> Create</button>
          </div>
        </div>

        {staleStatus ? <div className="wf-status-line wf-definition-refresh-status" role="status">{staleStatus}</div> : null}
        {displayState === "failed" && currentError ? (
          <WfErrorCard
            message={currentError}
            title={errorTitle}
            action={(
              <div className="wf-definition-search-actions">
                <button type="button" className="wf-link-button" onClick={retry}>Retry current search</button>
                {page > 1 ? <button type="button" className="wf-link-button" onClick={() => rebase()}>Restart from first page</button> : null}
              </div>
            )}
          />
        ) : null}
        {displayState !== "failed" && currentError ? <div className="wf-alert"><AlertCircle size={16} /> {currentError}</div> : null}
        {status ? <div className="wf-status-line" role="status"><Check size={14} /> {status}</div> : null}
        {selectedCount > 0 && isCurrentData ? (
          <div className="wf-selection-bar" aria-live="polite">
            <span>{selectedCount} selected</span>
            <div className="wf-selection-actions">
              {moveCapabilityAvailable ? <button type="button" onClick={openMoveDialog}>Move to folder</button> : null}
              <button type="button" onClick={clearSelection}>Clear selection</button>
            </div>
          </div>
        ) : null}
        {displayState === "loading" && !renderResultRows ? <WfListSkeleton /> : null}
        {isCurrentData && definitions.length === 0 ? (
          <WfEmptyState
            icon={<Package size={22} />}
            title={emptyTitle}
            description={emptyDescription}
            action={<button type="button" className="wf-link-button" onClick={openCreateDialog}><Plus size={15} /> Create workflow</button>}
          />
        ) : null}
        {renderResultRows && definitions.length > 0 ? (
          <>
            <div className="wf-definition-table-scroll" role="region" aria-label="Workflow definitions table" tabIndex={0}><div className="wf-grid wf-definition-grid" role="table" aria-label="Workflow definitions">
              <div className="wf-grid-head" role="row">
                <div className="wf-row-select" role="columnheader">
                  {isCurrentData ? (
                  <input
                    ref={selectVisibleRef}
                    type="checkbox"
                    checked={allCurrentVisibleSelected}
                    onChange={event => toggleVisibleSelection(event.target.checked)}
                    aria-label="Select visible workflow definitions"
                  />
                  ) : null}
                </div>
                <span role="columnheader">Name</span>
                <span role="columnheader">Latest version</span>
                <span role="columnheader">Published version</span>
                <span role="columnheader">Modified</span>
                <span className="wf-definition-actions-header" role="columnheader">{isCurrentData ? "Actions" : ""}</span>
              </div>
              {definitions.map(definition => {
                const isDeleted = Boolean(definition.deletedAt);
                const definitionUrl = new URL(window.location.href);
                definitionUrl.searchParams.set("definition", definition.id);
                const menuItems: OverflowMenuItem[] = !isCurrentData ? [] : isDeleted
                  ? [
                    { id: "restore", label: "Restore", onSelect: () => void restore(definition) },
                    { id: "permanent-delete", label: "Delete permanently", destructive: true, onSelect: () => void permanentDelete(definition) }
                  ]
                  : [
                    { id: "artifacts", label: "Artifacts", onSelect: () => openDefinitionArtifacts(definition.id) },
                    ...(explainDefinitionAction ? [{ id: "explain", label: "Explain", onSelect: () => dispatchAiAction(ai, explainDefinitionAction, definition) }] : []),
                    { id: "delete", label: "Delete", destructive: true, onSelect: () => void softDelete(definition) }
                  ];
                return (
                  <div
                    className="wf-grid-row"
                    role="row"
                    {...(isCurrentData ? { "aria-label": `Open workflow definition ${definition.name}`, "aria-selected": selectedIds.has(definition.id), onClick: () => onOpen(definition.id) } : {})}
                    key={definition.id}
                  >
                    <div className="wf-row-select" role="cell" onClick={isCurrentData ? event => event.stopPropagation() : undefined}>
                      {isCurrentData ? (
                      <input
                        type="checkbox"
                        checked={selectedIds.has(definition.id)}
                        onChange={event => toggleDefinitionSelection(definition.id, event.target.checked)}
                        aria-label={`Select workflow definition ${definition.name}`}
                      />
                      ) : null}
                    </div>
                    <span className="wf-definition-name" role="cell">
                      <strong title={definition.name}>
                        {isCurrentData ? (
                          <a
                            className="wf-definition-link"
                            href={`${definitionUrl.pathname}${definitionUrl.search}${definitionUrl.hash}`}
                            onClick={(event: ReactMouseEvent<HTMLAnchorElement>) => {
                              event.stopPropagation();
                              if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
                              event.preventDefault();
                              onOpen(definition.id);
                            }}
                          >
                            {definition.name}
                          </a>
                        ) : definition.name}
                      </strong>
                      <small>{definition.description || definition.id}</small>
                      {isDeleted ? <><span className="wf-status-badge" data-status="cancelled">Deleted</span><small>{formatDate(definition.deletedAt)}</small></> : null}
                      {isCurrentData && effectiveFolderSelection === "all" ? (
                        <ResultFolderBreadcrumb definition={definition} onSelect={changeFolder} />
                      ) : null}
                    </span>
                    <span className="wf-definition-version" role="cell"><LatestDefinitionVersion definition={definition} /></span>
                    <span className="wf-definition-version" role="cell"><PublishedDefinitionVersion definition={definition} /></span>
                    <span role="cell">{formatDate(definition.lastModifiedAt)}</span>
                    <span className="wf-row-actions" role="cell" onClick={isCurrentData ? event => event.stopPropagation() : undefined}>
                      {isCurrentData ? <OverflowMenu items={menuItems} ariaLabel={`Actions for ${definition.name}`} /> : null}
                    </span>
                  </div>
                );
              })}
            </div></div>
          </>
        ) : null}
        {isCurrentData ? <DefinitionPager
          page={page}
          pageSize={pageSize}
          totalCount={usesCursorPaging ? undefined : totalCount}
          itemCount={definitions.length}
          hasNextPage={Boolean(continuationTokens[page + 1])}
          onPageChange={changePage}
          onPageSizeChange={changePageSize}
        /> : null}
      </div>
      {createDraft ? (
        <Suspense fallback={<div className="wf-dialog-backdrop" role="status">Loading create dialog…</div>}>
          <CreateWorkflowDialog
            draft={createDraft}
            catalog={catalog}
            catalogState={catalogState}
            catalogError={catalogError}
            creating={creating}
            submitError={createError}
            ai={ai}
            suggestMetadataAction={suggestMetadataAction}
            onChange={nextDraft => setCreateDraft(nextDraft)}
            onClose={() => setCreateDraft(null)}
            onRetryCatalog={() => void loadCatalog()}
            onSubmit={submitCreate}
          />
        </Suspense>
      ) : null}
      {moveDialogOpen ? (
        <Suspense fallback={<div className="wf-dialog-backdrop" role="status">Loading move dialog…</div>}>
          <MoveWorkflowDefinitionsDialog
            context={context}
            definitionIds={[...selectedDefinitionIds]}
            onClose={() => setMoveDialogOpen(false)}
            onMoved={movedDefinitions}
          />
        </Suspense>
      ) : null}
    </>
  );
}

function ResultFolderBreadcrumb({
  definition,
  onSelect
}: {
  definition: WorkflowDefinitionSummary;
  onSelect(selection: WorkflowFolderSelection): void;
}) {
  const hasFolderProjection = Object.prototype.hasOwnProperty.call(definition, "folderId");
  const crumbs = definition.folderBreadcrumb ?? [];
  const isProjectedUnfiled = hasFolderProjection && definition.folderId === null && crumbs.length === 0;
  if (!isProjectedUnfiled && crumbs.length === 0) return null;

  return (
    <nav
      className="wf-result-folder-breadcrumb"
      aria-label={`Folder for ${definition.name}`}
      onClick={event => event.stopPropagation()}
      onKeyDown={event => event.stopPropagation()}
    >
      {isProjectedUnfiled ? (
        <button type="button" onClick={() => onSelect("unfiled")}>Unfiled</button>
      ) : crumbs.map((crumb, index) => (
        <span key={crumb.id}>
          {index > 0 ? <span aria-hidden="true">/</span> : null}
          <button type="button" onClick={() => onSelect({ id: crumb.id })}>{crumb.name}</button>
        </span>
      ))}
    </nav>
  );
}

