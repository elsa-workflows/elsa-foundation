import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertCircle, Check, Package, Plus, RotateCcw, Search, Sparkles, Trash2 } from "lucide-react";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createDefinition, deleteDefinition, deleteDefinitionPermanently, listDefinitions, restoreDefinition, workflowDefinitionFolderMovePath } from "../api/workflowDesign";
import { listActivities } from "../api/activityDesign";
import type { ActivityCatalogItem, DefinitionListState, WorkflowDefinitionDetails, WorkflowDefinitionSummary } from "../workflowTypes";
import { formatDate } from "../workflowFormatting";
import { getDialogs } from "./dialogs";
import { defaultDefinitionPageSize } from "./constants";
import type { CreateWorkflowDraft } from "./editorTypes";
import { dispatchAiAction, findAiAction, getCreateInitialState, getTotalPages } from "./editorHelpers";
import { WfEmptyState, WfErrorCard, WfListSkeleton } from "./StatusViews";
import { DefinitionPager } from "./DefinitionPager";
import { CreateWorkflowDialog } from "./CreateWorkflowDialog";
import { WorkflowFolderNavigation, type WorkflowFolderSelection, selectedFolderId } from "./WorkflowFolderNavigation";
import {
  parseWorkflowDefinitionBrowseLocation,
  updateWorkflowDefinitionBrowseUrl,
  type WorkflowDefinitionBrowseLocation
} from "./workflowDefinitionBrowseLocation";
import "./workflowFolders.css";

const MoveWorkflowDefinitionsDialog = lazy(() =>
  import("./MoveWorkflowDefinitionsDialog").then(module => ({ default: module.MoveWorkflowDefinitionsDialog }))
);

export function WorkflowDefinitions({ context, ai, onOpen }: { context: StudioEndpointContext; ai: StudioAiContributionApi; onOpen(id: string): void }) {
  const [browseLocation, setBrowseLocation] = useState(() => parseWorkflowDefinitionBrowseLocation(window.location.search));
  const { folderSelection, listState, search } = browseLocation;
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(defaultDefinitionPageSize);
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [definitions, setDefinitions] = useState<WorkflowDefinitionDetails["definition"][]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [nextContinuationTokens, setNextContinuationTokens] = useState<Record<number, string>>({});
  const [usesCursorPaging, setUsesCursorPaging] = useState(false);
  const [selectedDefinitionIds, setSelectedDefinitionIds] = useState<Set<string>>(() => new Set());
  const [createDraft, setCreateDraft] = useState<CreateWorkflowDraft | null>(null);
  const [creating, setCreating] = useState(false);
  const [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
  const [catalogState, setCatalogState] = useState<"idle" | "loading" | "ready" | "failed">("idle");
  const [folderCapability, setFolderCapability] = useState<"unknown" | "available" | "unavailable">("unknown");
  const [moveCapabilityAvailable, setMoveCapabilityAvailable] = useState(false);
  const [moveDialogOpen, setMoveDialogOpen] = useState(false);
  const [focusRefreshAfterMove, setFocusRefreshAfterMove] = useState(false);
  const selectVisibleRef = useRef<HTMLInputElement | null>(null);
  const refreshButtonRef = useRef<HTMLButtonElement | null>(null);
  const loadGenerationRef = useRef(0);
  const visibleDefinitionIds = useMemo(() => definitions.map(definition => definition.id), [definitions]);
  const continuationToken = nextContinuationTokens[page];
  const suggestMetadataAction = findAiAction(ai, "weaver.workflows.suggest-create-metadata");
  const explainDefinitionAction = findAiAction(ai, "weaver.workflows.explain-definition");
  const selectedVisibleCount = visibleDefinitionIds.filter(id => selectedDefinitionIds.has(id)).length;
  const allVisibleSelected = visibleDefinitionIds.length > 0 && selectedVisibleCount === visibleDefinitionIds.length;
  const searchLabel = folderSelection === "all"
    ? "Search all workflows"
    : folderSelection === "unfiled"
      ? "Search Unfiled"
      : "Search selected folder";
  const emptyScope = folderSelection === "all"
    ? "All workflows"
    : folderSelection === "unfiled"
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

  const rebasePaging = useCallback((clearDefinitionSelection = false) => {
    loadGenerationRef.current += 1;
    setPage(1);
    setNextContinuationTokens({});
    if (clearDefinitionSelection) setSelectedDefinitionIds(new Set());
  }, []);

  const navigateBrowseLocation = useCallback((
    next: WorkflowDefinitionBrowseLocation,
    historyMode: "push" | "replace",
    clearDefinitionSelection = false
  ) => {
    const url = updateWorkflowDefinitionBrowseUrl(new URL(window.location.href), next);
    window.history[historyMode === "push" ? "pushState" : "replaceState"](
      {},
      "",
      `${url.pathname}${url.search}${url.hash}`
    );
    setBrowseLocation(next);
    rebasePaging(clearDefinitionSelection);
  }, [rebasePaging]);

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
    const syncFromLocation = () => {
      setBrowseLocation(parseWorkflowDefinitionBrowseLocation(window.location.search));
      rebasePaging(true);
    };
    window.addEventListener("popstate", syncFromLocation);
    return () => window.removeEventListener("popstate", syncFromLocation);
  }, [rebasePaging]);

  const load = useCallback(async (options?: { page?: number; continuationToken?: string; folderSelection?: WorkflowFolderSelection }) => {
    if (folderCapability === "unknown") return;
    const requestedPage = options?.page ?? page;
    const requestedContinuationToken = options && "continuationToken" in options ? options.continuationToken : continuationToken;
    const requestedFolderSelection = options && "folderSelection" in options ? options.folderSelection! : folderSelection;
    const generation = ++loadGenerationRef.current;
    setState("loading");
    setError("");
    try {
      const response = await listDefinitions(context, {
        search,
        state: listState,
        page: requestedPage,
        pageSize,
        continuationToken: requestedContinuationToken,
        folderId: folderCapability === "available" ? selectedFolderId(requestedFolderSelection) : null,
        unfiled: folderCapability === "available" && requestedFolderSelection === "unfiled"
      });
      if (generation !== loadGenerationRef.current) return;
      if (response.isPaged) {
        setDefinitions(response.definitions);
        setUsesCursorPaging(true);
        setNextContinuationTokens(current => {
          const next = Object.fromEntries(
            Object.entries(current).filter(([storedPage]) => Number(storedPage) <= requestedPage)
          ) as Record<number, string>;
          if (response.nextContinuationToken) next[requestedPage + 1] = response.nextContinuationToken;
          return next;
        });
      } else {
        const effectiveTotalCount = response.totalCount;
        const effectiveTotalPages = getTotalPages(effectiveTotalCount, pageSize);
        if (effectiveTotalCount > 0 && requestedPage > effectiveTotalPages) {
          setPage(effectiveTotalPages);
          return;
        }
        setDefinitions(response.definitions);
        setTotalCount(effectiveTotalCount);
        setUsesCursorPaging(false);
      }
      setState("ready");
    } catch (e) {
      if (generation !== loadGenerationRef.current) return;
      setError(e instanceof Error ? e.message : String(e));
      setState("failed");
    }
  }, [context, search, listState, page, pageSize, continuationToken, folderCapability, folderSelection]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    let cancelled = false;
    void workflowDefinitionFolderMovePath(context)
      .then(path => { if (!cancelled) setMoveCapabilityAvailable(!!path); })
      .catch(() => { if (!cancelled) setMoveCapabilityAvailable(false); });
    return () => { cancelled = true; };
  }, [context]);

  useEffect(() => {
    if (selectVisibleRef.current) {
      selectVisibleRef.current.indeterminate = selectedVisibleCount > 0 && !allVisibleSelected;
    }
  }, [allVisibleSelected, selectedVisibleCount]);

  const loadCatalog = useCallback(async () => {
    if (catalogState === "loading" || catalogState === "ready") return;
    setCatalogState("loading");
    try {
      const response = await listActivities(context);
      setCatalog(response.activities ?? []);
      setCatalogState("ready");
    } catch (e) {
      setCatalogState("failed");
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [catalogState, context]);

  const openCreateDialog = () => {
    setError("");
    setStatus("");
    setCreateDraft({ name: "", description: "", rootActivityVersionId: null });
    void loadCatalog();
  };

  const submitCreate = async () => {
    if (!createDraft?.name.trim()) return;
    setCreating(true);
    setError("");
    setStatus("");
    try {
      const folderId = folderCapability === "available" ? selectedFolderId(folderSelection) : null;
      const details = await createDefinition(context, {
        name: createDraft.name.trim(),
        description: createDraft.description.trim() || null,
        initialState: getCreateInitialState(createDraft, catalog),
        ...(folderId ? { folderId } : {})
      });
      setCreateDraft(null);
      onOpen(details.definition.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setCreating(false);
    }
  };

  const openDefinitionArtifacts = (definitionId: string) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(definitionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const refreshAfterMutation = async () => {
    if (definitions.length === 1 && page > 1) {
      setPage(page - 1);
      return;
    }

    await load();
  };

  const clearSelection = () => setSelectedDefinitionIds(new Set());

  useEffect(() => {
    if (moveDialogOpen || !focusRefreshAfterMove) return;
    clearSelection();
    refreshButtonRef.current?.focus();
    setFocusRefreshAfterMove(false);
  }, [focusRefreshAfterMove, moveDialogOpen]);

  const movedDefinitions = async () => {
    // Placement changes both the result set and its cursor ordering (FolderId, LastModifiedAt).
    // Never reuse the old continuation token: explicitly rebase onto the first page.
    setPage(1);
    setNextContinuationTokens({});
    await load({ page: 1, continuationToken: undefined });
    const count = selectedDefinitionIds.size;
    setStatus(`Moved ${count === 1 ? "1 workflow definition" : `${count} workflow definitions`}`);
    setFocusRefreshAfterMove(true);
  };

  const foldersMutated = async (nextFolderSelection?: WorkflowFolderSelection) => {
    setPage(1);
    setNextContinuationTokens({});
    await load({ page: 1, continuationToken: undefined, folderSelection: nextFolderSelection ?? folderSelection });
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

  const changeSearch = (value: string) => {
    navigateBrowseLocation({ ...browseLocation, search: value }, "replace");
  };

  const changePageSize = (value: number) => {
    setPageSize(value);
    setPage(1);
    setNextContinuationTokens({});
  };

  const changeFolder = (nextFolder: WorkflowFolderSelection) => {
    navigateBrowseLocation({ ...browseLocation, folderSelection: nextFolder }, "push", true);
  };

  const softDelete = async (definition: WorkflowDefinitionDetails["definition"]) => {
    if (!(await getDialogs().confirm({ message: `Delete workflow definition "${definition.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" }))) return;
    setStatus("");
    setError("");
    try {
      await deleteDefinition(context, definition.id);
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Deleted ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const restore = async (definition: WorkflowDefinitionDetails["definition"]) => {
    setStatus("");
    setError("");
    try {
      await restoreDefinition(context, definition.id);
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Restored ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const permanentDelete = async (definition: WorkflowDefinitionDetails["definition"]) => {
    if (!(await getDialogs().confirm({ message: `Permanently delete workflow definition "${definition.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" }))) return;
    setStatus("");
    setError("");
    try {
      await deleteDefinitionPermanently(context, definition.id);
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Permanently deleted ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  return (
    <>
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
        <div className="wf-segmented" role="tablist" aria-label="Definition state">
          <button type="button" className={listState === "active" ? "active" : ""} aria-selected={listState === "active"} onClick={() => changeListState("active")}>Active</button>
          <button type="button" className={listState === "deleted" ? "active" : ""} aria-selected={listState === "deleted"} onClick={() => changeListState("deleted")}>Deleted</button>
          <button type="button" className={listState === "all" ? "active" : ""} aria-selected={listState === "all"} onClick={() => changeListState("all")}>All states</button>
        </div>
        <label className="wf-search">
          <Search size={15} />
          <input aria-label={searchLabel} value={search} onChange={event => changeSearch(event.target.value)} placeholder="Search definitions" />
        </label>
        {folderSelection !== "all" ? <button type="button" onClick={() => changeFolder("all")}>Search all workflows</button> : null}
        <button ref={refreshButtonRef} type="button" onClick={() => void load()}>Refresh</button>
        <div className="wf-actions">
          <button type="button" title="Create workflow" onClick={openCreateDialog}><Plus size={15} /> Create</button>
          {moveCapabilityAvailable ? (
            <button
              type="button"
              aria-disabled={selectedDefinitionIds.size === 0}
              title={selectedDefinitionIds.size === 0 ? "Select one or more workflow definitions to move." : "Move selected workflow definitions to a folder"}
              onClick={() => {
                if (selectedDefinitionIds.size === 0) return;
                setError("");
                setStatus("");
                setMoveDialogOpen(true);
              }}
            >Move to folder</button>
          ) : null}
        </div>
      </div>

      {state === "failed" ? (
        <WfErrorCard
          message={error}
          title="Couldn't load workflow definitions"
          action={page > 1 ? (
            <button type="button" onClick={() => rebasePaging()}>Restart from first page</button>
          ) : undefined}
        />
      ) : null}
      {state !== "failed" && error ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}
      {status ? <div className="wf-status-line" role="status"><Check size={14} /> {status}</div> : null}
      {selectedDefinitionIds.size > 0 ? (
        <div className="wf-selection-bar" aria-live="polite">
          <span>{selectedDefinitionIds.size} selected</span>
          <button type="button" onClick={clearSelection}>Clear selection</button>
        </div>
      ) : null}
      {state === "loading" ? <WfListSkeleton /> : null}
      {state === "ready" && definitions.length === 0 ? (
        <WfEmptyState
          icon={<Package size={22} />}
          title={emptyTitle}
          description={emptyDescription}
          action={<button type="button" className="wf-link-button" onClick={openCreateDialog}><Plus size={15} /> Create workflow</button>}
        />
      ) : null}
      {state === "ready" && (definitions.length > 0 || page > 1) ? (
        <>
          {definitions.length > 0 ? <div className="wf-grid" role="table" aria-label="Workflow definitions">
            <div className="wf-grid-head" role="row">
              <label className="wf-row-select">
                <input
                  ref={selectVisibleRef}
                  type="checkbox"
                  checked={allVisibleSelected}
                  onChange={event => toggleVisibleSelection(event.target.checked)}
                  aria-label="Select visible workflow definitions"
                />
              </label>
              <span>Name</span>
              <span>Latest version</span>
              <span>{listState === "all" ? "State" : listState === "deleted" ? "Deleted" : "Draft"}</span>
              <span>Modified</span>
              <span>Actions</span>
            </div>
            {definitions.map(definition => {
              const isDeleted = Boolean(definition.deletedAt);
              return (
              <div
                className="wf-grid-row"
                role="row"
                aria-label={`Open workflow definition ${definition.name}`}
                aria-selected={selectedDefinitionIds.has(definition.id)}
                tabIndex={0}
                onClick={() => onOpen(definition.id)}
                onKeyDown={event => {
                  if (event.currentTarget !== event.target) return;
                  if (event.key !== "Enter" && event.key !== " ") return;
                  event.preventDefault();
                  onOpen(definition.id);
                }}
                key={definition.id}
              >
                <label className="wf-row-select" onClick={event => event.stopPropagation()}>
                  <input
                    type="checkbox"
                    checked={selectedDefinitionIds.has(definition.id)}
                    onChange={event => toggleDefinitionSelection(definition.id, event.target.checked)}
                    aria-label={`Select workflow definition ${definition.name}`}
                  />
                </label>
                <span>
                  <strong>{definition.name}</strong>
                  <small>{definition.description || definition.id}</small>
                  {folderSelection === "all" ? (
                    <ResultFolderBreadcrumb definition={definition} onSelect={changeFolder} />
                  ) : null}
                </span>
                <span>{definition.latestVersion ?? "No version"}</span>
                <span>{listState === "all"
                  ? isDeleted
                    ? `Deleted ${formatDate(definition.deletedAt)}`
                    : definition.draftId ? "Draft" : "Active"
                  : listState === "deleted"
                    ? formatDate(definition.deletedAt)
                    : definition.draftId ? "Draft" : "None"}</span>
                <span>{formatDate(definition.lastModifiedAt)}</span>
                <span className="wf-row-actions" onClick={event => event.stopPropagation()}>
                  {!isDeleted ? (
                    <>
                      <button type="button" onClick={event => { event.stopPropagation(); onOpen(definition.id); }}>Open</button>
                      <button type="button" onClick={event => { event.stopPropagation(); openDefinitionArtifacts(definition.id); }}>Artifacts</button>
                      {explainDefinitionAction ? (
                        <button type="button" onClick={() => dispatchAiAction(ai, explainDefinitionAction, definition)}><Sparkles size={13} /> Explain</button>
                      ) : null}
                      <button type="button" className="danger" onClick={() => void softDelete(definition)}><Trash2 size={13} /> Delete</button>
                    </>
                  ) : (
                    <>
                      <button type="button" onClick={() => void restore(definition)}><RotateCcw size={13} /> Restore</button>
                      <button type="button" className="danger" onClick={() => void permanentDelete(definition)}><Trash2 size={13} /> Delete permanently</button>
                    </>
                  )}
                </span>
              </div>
              );
            })}
          </div> : null}
          <DefinitionPager
            page={page}
            pageSize={pageSize}
            totalCount={usesCursorPaging ? undefined : totalCount}
            itemCount={definitions.length}
            hasNextPage={Boolean(nextContinuationTokens[page + 1])}
            onPageChange={setPage}
            onPageSizeChange={changePageSize}
          />
        </>
      ) : null}
      {createDraft ? (
        <CreateWorkflowDialog
          draft={createDraft}
          catalog={catalog}
          creating={creating}
          ai={ai}
          suggestMetadataAction={suggestMetadataAction}
          onChange={nextDraft => setCreateDraft(nextDraft)}
          onClose={() => setCreateDraft(null)}
          onSubmit={submitCreate}
        />
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
