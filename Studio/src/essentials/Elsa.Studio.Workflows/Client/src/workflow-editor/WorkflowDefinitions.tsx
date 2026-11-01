import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import { lazy, Suspense, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent } from "react";
import { AlertCircle, Check, Package, Plus, Search } from "lucide-react";
import { StudioHttpError, type StudioAgentRegistry, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, OverflowMenu, StudioButton, type OverflowMenuItem, StudioPageHeader, StudioCollectionToolbar, StudioSearchInput } from "@elsa-workflows/studio-ui";
import { createDefinition, deleteDefinition, deleteDefinitionPermanently, listDefinitions, restoreDefinition, workflowDefinitionFolderMovePath } from "../api/workflowDesign";
import type { ActivityCatalogItem, DefinitionListState, WorkflowDefinitionDetails, WorkflowDefinitionSummary } from "../workflowTypes";
import { formatDate } from "../workflowFormatting";
import { getDialogs } from "./dialogs";
import type { CreateWorkflowDraft } from "./editorTypes";
import { dispatchAiAction, findAiAction, getTotalPages } from "./editorListHelpers";
import { WfEmptyState, WfErrorCard, WfListSkeleton } from "./StatusViews";
import { DefinitionPager } from "./DefinitionPager";
import { WorkflowFolderNavigation, type WorkflowFolderSelection, selectedFolderId } from "./WorkflowFolderNavigation";
import { clearUnavailableWorkflowBrowseConstraints, unavailableWorkflowBrowseConstraints, updateWorkflowDefinitionBrowseUrl, workflowFolderSelectionKey, workflowLibraryFilterKey, type WorkflowDefinitionBrowseLocation } from "./workflowDefinitionBrowseLocation";
import type { WorkflowOrganizationPermissions, WorkflowTag } from "../api/workflowOrganization";
import { workflowOrganizationSupport } from "../api/workflowOrganizationCapabilities";
import "./workflowFolders.css";
import "./workflowDefinitionTable.css";
import { LatestDefinitionVersion, PublishedDefinitionVersion } from "./DefinitionVersionCells";
import type { WorkflowDefinitionBrowseSession } from "./workflowDefinitionBrowseSession";
import { WorkflowListIdentifiers } from "./WorkflowListIdentifiers";

const CreateWorkflowDialog = lazy(() =>
  import("./CreateWorkflowDialog").then(module => ({ default: module.CreateWorkflowDialog }))
);
const MoveWorkflowDefinitionsDialog = lazy(() =>
  import("./MoveWorkflowDefinitionsDialog").then(module => ({ default: module.MoveWorkflowDefinitionsDialog }))
);
const WorkflowLibraryControls = lazy(() => import("./WorkflowLibraryControls").then(module => ({ default: module.WorkflowLibraryControls })));
const WorkflowOrganizationDialog = lazy(() => import("./WorkflowOrganizationDialog").then(module => ({ default: module.WorkflowOrganizationDialog })));
const WorkflowBulkTagsDialog = lazy(() => import("./WorkflowOrganizationDialog").then(module => ({ default: module.WorkflowBulkTagsDialog })));

interface BrowseIdentity {
  baseUrl: string;
  authorityRevision: number;
  requestedFolderKey: string;
  effectiveFolderKey: string;
  listState: DefinitionListState;
  search: string;
  libraryFilterKey: string;
  page: number;
  pageSize: number;
}

interface BrowseSnapshot {
  identity: BrowseIdentity;
  definitions: WorkflowDefinitionDetails["definition"][];
  totalCount: number;
  usesCursorPaging: boolean;
  hasKnownTotal: boolean;
}

const noDefinitions: BrowseSnapshot["definitions"] = [];

function sameBrowseIdentity(left: BrowseIdentity, right: BrowseIdentity) {
  return left.baseUrl === right.baseUrl
    && left.authorityRevision === right.authorityRevision
    && left.requestedFolderKey === right.requestedFolderKey
    && left.effectiveFolderKey === right.effectiveFolderKey
    && left.listState === right.listState
    && left.search === right.search
    && left.libraryFilterKey === right.libraryFilterKey
    && left.page === right.page
    && left.pageSize === right.pageSize;
}

function canRetainBrowseSnapshot(previous: BrowseIdentity, next: BrowseIdentity) {
  return previous.baseUrl === next.baseUrl
    && previous.authorityRevision === next.authorityRevision
    && previous.requestedFolderKey === next.requestedFolderKey
    && previous.effectiveFolderKey === next.effectiveFolderKey
    && previous.listState === next.listState
    && previous.libraryFilterKey === next.libraryFilterKey
    && previous.pageSize === next.pageSize
    && (previous.search === next.search ? previous.page === next.page : true);
}

export function WorkflowDefinitions({ context, ai, onOpen, session, homeCreateRequestId = null, onHomeCreateRequestHandled }: {
  context: StudioEndpointContext;
  ai: StudioAgentRegistry;
  onOpen(id: string): void;
  session: WorkflowDefinitionBrowseSession;
  homeCreateRequestId?: string | null;
  onHomeCreateRequestHandled?(requestId: string): void;
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
  // Retired dialogs can still finish writes. Their callbacks belong to the originating authority/context.
  const workGenerationRef = useRef(0);
  const renderGeneration = workGenerationRef.current;
  const isCurrentGeneration = () => renderGeneration === workGenerationRef.current;
  const handledHomeCreateRequestRef = useRef<string | null>(null);
  const [folderCapability, setFolderCapability] = useState<"unknown" | "available" | "unavailable">("unknown");
  const [moveCapabilityAvailable, setMoveCapabilityAvailable] = useState(false);
  const [moveDialogOpen, setMoveDialogOpen] = useState(false);
  const [focusRefreshAfterMove, setFocusRefreshAfterMove] = useState(false);
  const [librarySupport, setLibrarySupport] = useState<Awaited<ReturnType<typeof workflowOrganizationSupport>> | null>(null);
  const [librarySupportError, setLibrarySupportError] = useState<string | null>(null);
  const [libraryPermissions, setLibraryPermissions] = useState<WorkflowOrganizationPermissions>({});
  const [tagCatalog, setTagCatalog] = useState<WorkflowTag[]>([]);
  const [organizationTarget, setOrganizationTarget] = useState<{ id: string; name: string } | null>(null);
  const [bulkTagTargets, setBulkTagTargets] = useState<string[] | null>(null);
  const selectVisibleRef = useRef<HTMLInputElement | null>(null);
  const refreshButtonRef = useRef<HTMLButtonElement | null>(null);
  const browseSupport = librarySupport && { ...librarySupport, folders: folderCapability === "available" };
  const unavailableConstraints = browseSupport && unavailableWorkflowBrowseConstraints(browseLocation, browseSupport);
  const browseScopeBlocked = Boolean(unavailableConstraints && Object.values(unavailableConstraints).some(Boolean));
  const availabilityResolved = librarySupport !== null && folderCapability !== "unknown";
  const selectionMatchesScope = selectionScopeRevision === scopeRevision;
  const effectiveFolderKey = folderCapability === "unknown"
    ? savedEffectiveFolderKey ?? "unknown"
    : folderCapability === "available"
      ? workflowFolderSelectionKey(folderSelection)
      : "all";
  const effectiveFolderResolved = folderCapability !== "unknown" && savedEffectiveFolderKey === effectiveFolderKey;
  const libraryFilterKey = workflowLibraryFilterKey(browseLocation);
  const targetIdentity = useMemo<BrowseIdentity>(() => ({
    baseUrl: context.baseUrl,
    authorityRevision,
    requestedFolderKey: workflowFolderSelectionKey(folderSelection),
    effectiveFolderKey,
    listState,
    search: search.trim(),
    libraryFilterKey,
    page,
    pageSize
  }), [authorityRevision, context.baseUrl, effectiveFolderKey, folderSelection, libraryFilterKey, listState, page, pageSize, search]);
  const endpointIdentityCurrent = context.baseUrl === authorityBaseUrl;
  const snapshotVisible = availabilityResolved && !browseScopeBlocked && lastSuccess && (sameBrowseIdentity(lastSuccess.identity, targetIdentity)
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
  const hasKnownTotal = snapshotVisible?.hasKnownTotal ?? false;
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
    if (current !== next) window.history.replaceState(window.history.state, "", next);
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
    if (!librarySupport || browseScopeBlocked || folderCapability === "unknown" || !effectiveFolderResolved || !endpointIdentityCurrent || requestsPaused || searchPending || settledSearch !== search) return;
    if (browseLocation.invalidTagFilters) { setRequestState("failed"); setLastSuccess(null); setRequestError({ identity: targetIdentity, message: "The tag filters in this link are invalid. Clear or repair them before browsing." }); return; }
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
        unfiled: folderCapability === "available" && requestedFolderSelection === "unfiled",
        tagFilters: browseLocation.tagFilters,
        includeDescendants: browseLocation.includeDescendants,
        favoritesOnly: browseLocation.favoritesOnly,
        sort: browseLocation.sort,
        publicationState: browseLocation.publicationState
      }, controller.signal);
      if (!isRequestCurrent(generation)) return;
      if (response.isPaged) {
        setLastSuccess({ identity, definitions: response.definitions, totalCount: response.totalCount ?? 0, usesCursorPaging: true, hasKnownTotal: response.totalCount !== undefined });
        recordNextCursor(requestedPage, response.nextContinuationToken ?? null);
      } else {
        const effectiveTotalCount = response.totalCount ?? response.definitions.length;
        const effectiveTotalPages = getTotalPages(effectiveTotalCount, pageSize);
        if (effectiveTotalCount > 0 && requestedPage > effectiveTotalPages) {
          changePage(effectiveTotalPages);
          return;
        }
        setLastSuccess({ identity, definitions: response.definitions, totalCount: effectiveTotalCount, usesCursorPaging: false, hasKnownTotal: true });
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
    librarySupport,
    browseScopeBlocked,
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
    targetIdentity,
    browseLocation
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
    ?? librarySupportError
    ?? (!searchPending && requestError && sameBrowseIdentity(requestError.identity, targetIdentity) ? requestError.message : null);

  useEffect(() => {
    if (selectVisibleRef.current) {
      selectVisibleRef.current.indeterminate = selectedVisibleCount > 0 && !allVisibleSelected;
    }
  }, [allVisibleSelected, selectedVisibleCount]);

  const loadCatalog = useCallback(async () => {
    if (catalogState === "loading" || catalogState === "ready") return;
    const generation = workGenerationRef.current;
    setCatalogState("loading");
    setCatalogError("");
    try {
      const { listActivities } = await import("../api/activityDesign");
      const response = await listActivities(context);
      if (generation !== workGenerationRef.current) return;
      setCatalog(response.activities ?? []);
      setCatalogState("ready");
    } catch (e) {
      if (generation !== workGenerationRef.current) return;
      setCatalogState("failed");
      setCatalogError(e instanceof Error ? e.message : String(e));
    }
  }, [catalogState, context]);

  const openCreateDialog = useCallback(() => {
    if (browseScopeBlocked) return;
    setRequestError(null);
    setCreateError("");
    setStatus("");
    setCreateDraft({ name: "", description: "", rootActivityVersionId: null });
    void loadCatalog();
  }, [browseScopeBlocked, loadCatalog]);

  useEffect(() => {
    if (!homeCreateRequestId || handledHomeCreateRequestRef.current === homeCreateRequestId) return;
    handledHomeCreateRequestRef.current = homeCreateRequestId;
    onHomeCreateRequestHandled?.(homeCreateRequestId);
    openCreateDialog();
  }, [homeCreateRequestId, onHomeCreateRequestHandled, openCreateDialog]);

  const submitCreate = async () => {
    if (createInFlightRef.current || !createDraft?.name.trim() || catalogState !== "ready") return;
    createInFlightRef.current = true;
    setCreating(true);
    setCreateError("");
    setStatus("");
    try {
      const folderId = folderCapability === "available" ? selectedFolderId(folderSelection) : null;
      const { getCreateInitialDocument } = await import("./editorHelpers");
      const initialDocument = getCreateInitialDocument(createDraft, catalog);
      if (!initialDocument.initialState?.rootActivity) {
        throw new Error("Choose a starting activity that has an authoring template before creating this workflow.");
      }
      const details = await createDefinition(context, {
        name: createDraft.name.trim(),
        description: createDraft.description.trim() || null,
        ...initialDocument,
        ...(folderId ? { folderId } : {})
      });
      if (!isCurrentGeneration()) return;
      setCreateDraft(null);
      onOpen(details.definition.id);
    } catch (e) {
      if (!isCurrentGeneration()) return;
      setCreateError(e instanceof Error ? e.message : String(e));
    } finally {
      if (isCurrentGeneration()) {
        createInFlightRef.current = false;
        setCreating(false);
      }
    }
  };

  const openDefinitionArtifacts = (definitionId: string) => {
    window.history.pushState({}, "", qualifyStudioBackendPath(`/workflows/executables?definition=${encodeURIComponent(definitionId)}`));
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const refreshAfterMutation = async () => {
    if (!isCurrentGeneration()) return;
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

  const closeMoveDialog = () => { if (isCurrentGeneration()) setMoveDialogOpen(false); };
  const movedDefinitions = async () => {
    if (!isCurrentGeneration()) return;
    rebase(true);
    const count = selectedDefinitionIds.size;
    setStatus(`Moved ${count === 1 ? "1 workflow definition" : `${count} workflow definitions`}`);
    setFocusRefreshAfterMove(true);
  };

  const foldersMutated = async (nextFolderSelection?: WorkflowFolderSelection) => {
    if (!isCurrentGeneration()) return;
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
    if (!isCurrentGeneration()) return;
    navigateBrowseLocation({ ...browseLocation, folderSelection: nextFolder, includeDescendants: typeof nextFolder === "object" ? browseLocation.includeDescendants : false }, "push", true);
  };
  const changeLibraryLocation = useCallback((next: WorkflowDefinitionBrowseLocation) => {
    if (renderGeneration !== workGenerationRef.current) return;
    navigateBrowseLocation(next, "push", true);
  }, [navigateBrowseLocation, renderGeneration]);
  const organizationSaved = () => { if (isCurrentGeneration()) { rebase(true); setStatus("Workflow organization updated"); } };
  const closeOrganization = () => {
    if (!isCurrentGeneration()) return;
    setOrganizationTarget(null); setBulkTagTargets(null);
    requestAnimationFrame(() => { if (isCurrentGeneration()) refreshButtonRef.current?.focus(); });
  };
  const changeFavorite = async (definition: WorkflowDefinitionSummary) => {
    try { const { setWorkflowFavorite } = await import("../api/workflowOrganization"); await setWorkflowFavorite(context, definition.id, !definition.favorite); if (!isCurrentGeneration()) return; setStatus(definition.favorite ? "Removed from favorites" : "Added to favorites"); rebase(true); }
    catch (caught) { if (isCurrentGeneration()) setStatus(caught instanceof Error ? caught.message : "Couldn't change this favorite."); }
  };

  const softDelete = async (definition: WorkflowDefinitionDetails["definition"]) => {
    if (!(await getDialogs().confirm({ message: `Delete workflow definition "${definition.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) || !isCurrentGeneration()) return;
    setStatus("");
    setRequestError(null);
    try {
      await deleteDefinition(context, definition.id);
      if (!isCurrentGeneration()) return;
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Deleted ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      if (!isCurrentGeneration()) return;
      setRequestError({ identity: targetIdentity, message: e instanceof Error ? e.message : String(e) });
    }
  };

  const restore = async (definition: WorkflowDefinitionDetails["definition"]) => {
    setStatus("");
    setRequestError(null);
    try {
      await restoreDefinition(context, definition.id);
      if (!isCurrentGeneration()) return;
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Restored ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      if (!isCurrentGeneration()) return;
      setRequestError({ identity: targetIdentity, message: e instanceof Error ? e.message : String(e) });
    }
  };

  const permanentDelete = async (definition: WorkflowDefinitionDetails["definition"]) => {
    if (!(await getDialogs().confirm({ message: `Permanently delete workflow definition "${definition.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) || !isCurrentGeneration()) return;
    setStatus("");
    setRequestError(null);
    try {
      await deleteDefinitionPermanently(context, definition.id);
      if (!isCurrentGeneration()) return;
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Permanently deleted ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      if (!isCurrentGeneration()) return;
      setRequestError({ identity: targetIdentity, message: e instanceof Error ? e.message : String(e) });
    }
  };

  const displayState = requestsPaused || browseScopeBlocked ? "failed" : !availabilityResolved || searchPending ? "loading" : requestState;
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

  useLayoutEffect(() => {
    workGenerationRef.current += 1;
    setFolderCapability("unknown"); setMoveCapabilityAvailable(false); setMoveDialogOpen(false); setFocusRefreshAfterMove(false); setStatus("");
    createInFlightRef.current = false; setCreating(false);
    setCreateDraft(null); setCatalog([]); setCatalogState("idle"); setCatalogError("");
    setLastSuccess(null); setRequestError(null);
    return () => { workGenerationRef.current += 1; };
  }, [authorityRevision, context]);

  useEffect(() => {
    let cancelled = false;
    void workflowDefinitionFolderMovePath(context)
      .then(path => { if (!cancelled) setMoveCapabilityAvailable(!!path); })
      .catch(() => { if (!cancelled) setMoveCapabilityAvailable(false); });
    return () => { cancelled = true; };
  }, [authorityRevision, context]);

  useEffect(() => {
    let cancelled = false;
    setLibrarySupport(null); setLibrarySupportError(null); setLibraryPermissions({}); setTagCatalog([]);
    setOrganizationTarget(null); setBulkTagTargets(null);
    void workflowOrganizationSupport(context).then(support => { if (!cancelled) setLibrarySupport(support); }).catch(caught => {
      if (cancelled) return;
      setLibrarySupport({ tags: false, organization: false, bulk: false, views: false, favorites: false, folders: false, paging: false, queryOptions: false });
      setLibrarySupportError(caught instanceof Error ? caught.message : "Couldn't inspect workflow support. Refresh the page to retry.");
    });
    return () => { cancelled = true; };
  }, [authorityRevision, context]);

  const showTagColumn = librarySupport?.tags === true && (browseLocation.columns?.includes("tags") ?? false);
  const showLocationColumn = folderCapability === "available" && (browseLocation.columns?.includes("location") ?? false);
  const hasLibrarySupport = librarySupport && (librarySupport.queryOptions || librarySupport.tags || librarySupport.folders || librarySupport.views);

  return (
    <>
      <StudioPageHeader title="Workflow definitions" titleId="workflow-definitions-title" description="Create and organize workflow definitions, then open a definition to edit its behavior." actions={        <div className="studio-page-actions" role="group" aria-label="Workflow definition page actions">
          <StudioButton variant="primary" size="md" title="Create workflow" aria-label="Create workflow" disabled={browseScopeBlocked} onClick={openCreateDialog}><Plus size={15} /> Create workflow</StudioButton>
          <StudioButton ref={refreshButtonRef} variant="default" size="md" title="Refresh workflow definitions" aria-label="Refresh workflow definitions" onClick={retry}>Refresh</StudioButton>
        </div>} />
      <div className="wf-definitions-list">
        <WorkflowFolderNavigation
          key={`${context.baseUrl}:${authorityRevision}`}
          context={context}
          selection={folderSelection}
          onSelect={changeFolder}
          onAvailable={handleFolderAvailability}
          onFoldersMutated={foldersMutated}
          canManage={librarySupport === null ? false : librarySupport.organization ? libraryPermissions.canManageFolders === true : true}
        >
        <StudioCollectionToolbar>
          <StudioSearchInput aria-label={searchLabel} value={search} onChange={event => changeSearch(event.target.value)} placeholder="Search definitions" />
          <div className="studio-filter-group" role="group" aria-label="Definition state">
            <button type="button" className={listState === "active" ? "active" : ""} aria-pressed={listState === "active"} onClick={() => changeListState("active")}>Active</button>
            <button type="button" className={listState === "deleted" ? "active" : ""} aria-pressed={listState === "deleted"} onClick={() => changeListState("deleted")}>Deleted</button>
            <button type="button" className={listState === "all" ? "active" : ""} aria-pressed={listState === "all"} onClick={() => changeListState("all")}>All states</button>
          </div>

          {effectiveFolderSelection !== "all" ? <StudioButton onClick={() => changeFolder("all")}>Search all workflows</StudioButton> : null}
        </StudioCollectionToolbar>

        {hasLibrarySupport ? <Suspense fallback={<p role="status">Loading library organization…</p>}><WorkflowLibraryControls
          key={`${context.baseUrl}:${authorityRevision}`} context={context} support={librarySupport}
          location={browseLocation} onChange={changeLibraryLocation} onCatalog={setTagCatalog} onPermissions={setLibraryPermissions}
        /></Suspense> : null}
        {browseScopeBlocked && browseSupport ? <div className="wf-alert" role="alert">
          Some organization filters are unavailable on this backend. Your link and saved column choices are preserved. Clear the unavailable filters to continue browsing.
          <StudioButton onClick={() => changeLibraryLocation(clearUnavailableWorkflowBrowseConstraints(browseLocation, browseSupport))}>Clear unavailable organization filters</StudioButton>
        </div> : null}

        {staleStatus ? <div className="wf-status-line wf-definition-refresh-status" role="status">{staleStatus}</div> : null}
        {displayState === "failed" && currentError ? (
          <WfErrorCard
            message={currentError}
            title={errorTitle}
            action={(
              <div className="wf-definition-search-actions">
                <StudioButton onClick={librarySupportError ? () => window.location.reload() : retry}>{librarySupportError ? "Reload workflow support" : "Retry current search"}</StudioButton>
                {page > 1 ? <StudioButton onClick={() => rebase()}>Restart from first page</StudioButton> : null}
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
              {moveCapabilityAvailable && librarySupport !== null && (!librarySupport.organization || libraryPermissions.canManageFolders === true) ? <button type="button" onClick={openMoveDialog}>Move to folder</button> : null}
              {librarySupport?.bulk && libraryPermissions.canAssignTags === true && listState !== "deleted" ? <button type="button" disabled={selectedCount > 100} onClick={() => setBulkTagTargets([...selectedIds])}>Edit tags</button> : null}
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
            action={<StudioButton variant="primary" onClick={openCreateDialog}><Plus size={15} /> Create workflow</StudioButton>}
          />
        ) : null}
        {renderResultRows && definitions.length > 0 ? (
          <>
            <div className="studio-table-container wf-definition-table-scroll" role="region" aria-label="Workflow definitions table" tabIndex={0}><div className="studio-collection-table wf-grid wf-definition-grid" data-library-columns={showTagColumn && showLocationColumn ? "both" : showTagColumn ? "tags" : showLocationColumn ? "location" : undefined} role="table" aria-label="Workflow definitions">
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
                <span className="wf-definition-id-header" role="columnheader">Definition ID</span>
                <span role="columnheader">Identifiers</span>
                <span role="columnheader">Latest version</span>
                <span role="columnheader">Published version</span>
                <span role="columnheader">Modified</span>
                {showTagColumn ? <span role="columnheader">Tags</span> : null}
                {showLocationColumn ? <span role="columnheader">Location</span> : null}
                <span className="wf-definition-actions-header" role="columnheader">{isCurrentData ? "Actions" : ""}</span>
              </div>
              {definitions.map(definition => {
                const isDeleted = Boolean(definition.deletedAt);
                const secondaryIdentifiers = [
                  { label: "Draft ID", value: definition.draftId },
                  { label: "Draft source version ID", value: definition.draftSourceVersionId },
                  { label: "Latest version ID", value: definition.latestVersionId },
                  { label: "Published version ID", value: definition.publishedVersionId }
                ].filter((identifier): identifier is { label: string; value: string } => Boolean(identifier.value));
                const definitionUrl = new URL(window.location.href);
                definitionUrl.searchParams.set("definition", definition.id);
                const menuItems: OverflowMenuItem[] = !isCurrentData ? [] : isDeleted
                  ? [
                    { id: "restore", label: "Restore", onSelect: () => void restore(definition) },
                    { id: "permanent-delete", label: "Delete permanently", destructive: true, onSelect: () => void permanentDelete(definition) }
                  ]
                  : [
                    { id: "artifacts", label: "Artifacts", onSelect: () => openDefinitionArtifacts(definition.id) },
                    ...(librarySupport?.organization ? [{ id: "organization", label: "Organization", onSelect: () => setOrganizationTarget({ id: definition.id, name: definition.name }) }] : []),
                    ...(librarySupport?.favorites ? [{ id: "favorite", label: definition.favorite ? "Remove from favorites" : "Add to favorites", onSelect: () => void changeFavorite(definition) }] : []),
                    ...(explainDefinitionAction ? [{ id: "explain", label: "Explain", onSelect: () => dispatchAiAction(ai, explainDefinitionAction, definition) }] : []),
                    { id: "delete", label: "Delete", destructive: true, onSelect: () => void softDelete(definition) }
                  ];
                return (
                  <div
                    className="wf-grid-row"
                    role="row"
                    {...(isCurrentData ? {
                      "aria-label": `Open workflow definition ${definition.name}`,
                      "aria-selected": selectedIds.has(definition.id),
                      onClick: (event: ReactMouseEvent<HTMLDivElement>) => {
                        if (event.target instanceof Element && event.target.closest("button, input, label, a")) return;
                        onOpen(definition.id);
                      }
                    } : {})}
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
                      {definition.description ? <small className="wf-definition-description">{definition.description}</small> : null}
                    </span>
                    <span className="wf-definition-id wf-definition-primary-identifier" role="cell" onClick={event => event.stopPropagation()}>
                      {isCurrentData ? <CopyableIdentifier label="Definition ID" value={definition.id} /> : <span title={definition.id}>{definition.id}</span>}
                    </span>
                    <span className="wf-definition-identities-cell" role="cell" onClick={event => event.stopPropagation()}>
                      {isCurrentData ? <WorkflowListIdentifiers name={definition.name} identifiers={secondaryIdentifiers} description={definition.description}>
                        {folderCapability === "available" && effectiveFolderSelection === "all" ? <ResultFolderBreadcrumb definition={definition} onSelect={changeFolder} /> : null}
                        {isDeleted ? <p>Deleted {formatDate(definition.deletedAt)}</p> : null}
                        {librarySupport?.tags && definition.tags?.length ? <p>Tags: {formatWorkflowTags(definition, tagCatalog)}</p> : null}
                      </WorkflowListIdentifiers> : null}
                    </span>
                    <span className="wf-definition-version" role="cell"><LatestDefinitionVersion definition={definition} />{isDeleted ? <span className="wf-status-badge" data-status="cancelled">Deleted</span> : null}</span>
                    <span className="wf-definition-version" role="cell"><PublishedDefinitionVersion definition={definition} /></span>
                    <span className="wf-definition-modified" role="cell">{formatDate(definition.lastModifiedAt)}</span>
                    {showTagColumn ? <span className="wf-library-tag-summary" role="cell" title={formatWorkflowTags(definition, tagCatalog)}>{formatWorkflowTags(definition, tagCatalog) || "—"}</span> : null}
                    {showLocationColumn ? <span className="wf-library-location" role="cell" title={definition.folderBreadcrumb?.map(folder => folder.name).join(" / ") || "Unfiled"}>{definition.folderBreadcrumb?.map(folder => folder.name).join(" / ") || "Unfiled"}</span> : null}
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
          totalCount={hasKnownTotal || !usesCursorPaging ? totalCount : undefined}
          itemCount={definitions.length}
          hasNextPage={Boolean(continuationTokens[page + 1])}
          usesCursorPaging={usesCursorPaging}
          onPageChange={changePage}
          onPageSizeChange={changePageSize}
        /> : null}
        </WorkflowFolderNavigation>
      </div>
      {librarySupport?.organization && organizationTarget ? <Suspense fallback={<p role="status">Loading workflow organization…</p>}><WorkflowOrganizationDialog context={context} definitionId={organizationTarget.id} name={organizationTarget.name} onClose={closeOrganization} onSaved={organizationSaved} /></Suspense> : null}
      {librarySupport?.bulk && bulkTagTargets ? <Suspense fallback={<p role="status">Loading bulk tag editor…</p>}><WorkflowBulkTagsDialog context={context} definitionIds={bulkTagTargets} onClose={closeOrganization} onSaved={organizationSaved} /></Suspense> : null}
      {createDraft ? (
        <Suspense fallback={<div className="wf-dialog-backdrop" role="status">Loading create dialog…</div>}>
          <CreateWorkflowDialog
            context={context}
            draft={createDraft}
            catalog={catalog}
            catalogState={catalogState}
            catalogError={catalogError}
            creating={creating}
            submitError={createError}
            ai={ai}
            suggestMetadataAction={suggestMetadataAction}
            onChange={nextDraft => { if (isCurrentGeneration()) setCreateDraft(nextDraft); }}
            onClose={() => { if (isCurrentGeneration()) setCreateDraft(null); }}
            onRetryCatalog={() => void loadCatalog()}
            onSubmit={submitCreate}
          />
        </Suspense>
      ) : null}
      {moveCapabilityAvailable && moveDialogOpen ? (
        <Suspense fallback={<div className="wf-dialog-backdrop" role="status">Loading move dialog…</div>}>
          <MoveWorkflowDefinitionsDialog
            context={context}
            definitionIds={[...selectedDefinitionIds]}
            onClose={closeMoveDialog}
            onMoved={movedDefinitions}
          />
        </Suspense>
      ) : null}
    </>
  );
}

function formatWorkflowTags(definition: WorkflowDefinitionSummary, catalog: WorkflowTag[]) {
  return (definition.tags ?? []).map(assignment => {
    const tag = catalog.find(item => item.id === assignment.tagId);
    const value = tag?.values.find(item => item.id === assignment.valueId);
    return `${tag?.name ?? assignment.tagId}${assignment.valueId ? `: ${value?.name ?? assignment.valueId}` : ""}`;
  }).join(", ");
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

