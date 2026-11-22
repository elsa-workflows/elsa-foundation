import { qualifyStudioBackendPath, StudioHttpError, useStudioWorkspaceTab } from "@elsa-workflows/studio-sdk";
import { StudioResultState, SkeletonRows, studioRowInteraction, CopyableIdentifier, OverflowMenu, StudioButton, StudioCollectionEmptyState, StudioCollectionFooter, StudioCollectionToolbar, StudioDialog, StudioDialogFooter, StudioPageHeader, StudioSearchInput, StudioTimestamp, type OverflowMenuItem, StudioField } from "@elsa-workflows/studio-ui";
import { Fragment, useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { AlertCircle, ChevronDown, ChevronLeft, ChevronRight, Play, RotateCcw, ScanSearch, Search, Sparkles, X } from "lucide-react";
import type { StudioWeaverRegistry, StudioEndpointContext, StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import { listExecutables } from "../api/runtime";
import { listDefinitions } from "../api/workflowDesign";
import { executableMatchesDefinitionSearch, getExecutableDefinitionIds, loadDefinitionNames } from "./executableDefinitionNames";
import {
  describeActivationSource,
  getPublicationSlotLifecycleSupport,
  listPublicationSlots,
  restorePublicationSlot,
  unpublishReviewedSlot,
  unpublishSlot,
  type PublicationSlot
} from "../api/publishing";
import type { WorkflowExecutableListScope, WorkflowExecutableSummary } from "../workflowTypes";
import { formatDate } from "../workflowFormatting";
import { WfEmptyState } from "./StatusViews";
import { ExecutableReferenceList, ExecutableRunStatusLine, CopyValueButton } from "./executableShared";
import { getNotifications, useNotificationGuard } from "./notifications";
import { getDialogs } from "./dialogs";
import type { ExecutableRunState } from "./editorTypes";
import { WorkflowRunInputDialog } from "./WorkflowRunInputDialog";
import {
  createExecutableWorkflowRunFeedback,
  createExecutableWorkflowRunTarget,
  useExecutableWorkflowRun
} from "./useExecutableWorkflowRun";
import { ExecutableRunButton } from "./ExecutableRunButton";
import { ExecutableExportButton } from "./ExecutableExportButton";
import { useExecutableArtifactDownload } from "./useExecutableArtifactExport";
import { defaultDefinitionPageSize, definitionPageSizes } from "./constants";
import {
  compareExecutablesByPublishedDate,
  dispatchAiAction,
  findAiAction,
  formatExecutableRoot,
  formatExecutableSourceKind,
  getTotalPages,
  pageItems
} from "./editorHelpers";
import { WorkflowListIdentifiers, type WorkflowListIdentifier } from "./WorkflowListIdentifiers";
import { updateWorkflowDefinitionIdInUrl } from "./workflowDefinitionUrl";

export function openExecutableInspector(artifactId: string, sourceReferenceId?: string | null) {
  const query = sourceReferenceId ? `?ref=${encodeURIComponent(sourceReferenceId)}` : "";
  window.history.pushState({}, "", qualifyStudioBackendPath(`/workflows/executables/${encodeURIComponent(artifactId)}${query}`));
  window.dispatchEvent(new PopStateEvent("popstate"));
}

export function WorkflowExecutables({ context, ai, runInputEditors, definitionFilter, onDefinitionFilterChange, canBrowseDefinitions = true, canManagePublications = false }: { context: StudioEndpointContext; ai: StudioWeaverRegistry; runInputEditors: StudioWorkflowRunInputEditorContribution[]; definitionFilter: string | null; onDefinitionFilterChange(filter: string | null): void; canBrowseDefinitions?: boolean; canManagePublications?: boolean }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [refreshing, setRefreshing] = useState(false);
  const workspaceTab = useStudioWorkspaceTab();
  const [definitionNamesLoading, setDefinitionNamesLoading] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [lastRun, setLastRun] = useState<ExecutableRunState | null>(null);
  const [executables, setExecutables] = useState<WorkflowExecutableSummary[]>([]);
  const [definitionNames, setDefinitionNames] = useState<ReadonlyMap<string, string>>(new Map());
  const loadGeneration = useRef(0);
  const [scope, setScope] = useState<WorkflowExecutableListScope>("published");
  const [includeRetired, setIncludeRetired] = useState(false);
  const [expandedArtifactIds, setExpandedArtifactIds] = useState<ReadonlySet<string>>(new Set());
  const [reviewedUnpublishAvailable, setReviewedUnpublishAvailable] = useState(false);
  const [unpublishDialogTarget, setUnpublishDialogTarget] = useState<{ artifactId: string; definitionIds: string[] } | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(defaultDefinitionPageSize);
  const normalizedDefinitionFilter = definitionFilter?.trim().toLowerCase() ?? "";
  const visibleExecutables = useMemo(
    () => normalizedDefinitionFilter
      ? executables.filter(executable => executableMatchesDefinitionSearch(executable, normalizedDefinitionFilter, definitionNames))
      : executables,
    [normalizedDefinitionFilter, executables, definitionNames]
  );
  const totalCount = visibleExecutables.length;
  const totalPages = getTotalPages(totalCount, pageSize);
  const currentPage = Math.min(page, totalPages);
  const pagedExecutables = useMemo(
    () => pageItems(visibleExecutables, currentPage, pageSize),
    [visibleExecutables, currentPage, pageSize]
  );
  const definitionOptions = useMemo(
    () => Array.from(new Set(executables.flatMap(getExecutableDefinitionIds)))
      .map(id => ({ id, name: definitionNames.get(id) ?? id }))
      .sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
    [executables, definitionNames]
  );
  const explainExecutableAction = findAiAction(ai, "weaver.workflows.explain-executable");
  const executableRun = useExecutableWorkflowRun({
    context,
    ...createExecutableWorkflowRunFeedback({ setStatus, setLastRun, setError })
  });
  const artifactExport = useExecutableArtifactDownload({
    context,
    onExported: (fileName, _executable, canNotify) => { setError(""); setLastRun(null); setStatus(""); if (canNotify()) getNotifications().notify({ level: "success", title: `Exported ${fileName}` }); },
    onFailed: message => { setStatus(""); setLastRun(null); setError(message); }
  });
  const captureNotificationGuard = useNotificationGuard(context);

  const load = useCallback(async (background = false) => {
    const generation = ++loadGeneration.current;
    if (!background) setState("loading");
    setRefreshing(true);
    setDefinitionNamesLoading(false);
    setError("");
    try {
      const [items, lifecycleSupport] = await Promise.all([
        listExecutables(context, { scope, includeRetired }),
        canManagePublications ? getPublicationSlotLifecycleSupport(context).catch(() => null) : Promise.resolve(null)
      ]);
      if (generation !== loadGeneration.current) return;
      setExecutables(items);
      setReviewedUnpublishAvailable(lifecycleSupport?.unpublishReviewed ?? false);
      if (!background) setDefinitionNames(new Map());
      setState("ready");
      const definitionIds = items.flatMap(getExecutableDefinitionIds);
      setDefinitionNamesLoading(definitionIds.length > 0);
      const names = await loadDefinitionNames(definitionIds, request => listDefinitions(context, request));
      if (generation === loadGeneration.current) {
        setDefinitionNames(names);
        setDefinitionNamesLoading(false);
      }
    } catch (e) {
      if (generation !== loadGeneration.current) return;
      setReviewedUnpublishAvailable(false);
      setDefinitionNamesLoading(false);
      setError(e instanceof Error ? e.message : String(e));
      const accessDenied = e instanceof StudioHttpError && (e.status === 401 || e.status === 403);
      if (accessDenied) { setExecutables([]); setDefinitionNames(new Map()); }
      setState(current => background && !accessDenied && current === "ready" ? current : "failed");
    } finally {
      if (generation === loadGeneration.current) setRefreshing(false);
    }
  }, [canManagePublications, context, includeRetired, scope]);

  useEffect(() => {
    void load();
    return () => { loadGeneration.current += 1; };
  }, [load]);

  useEffect(() => {
    setPage(1);
  }, [normalizedDefinitionFilter, scope, includeRetired]);

  const explain = (executable: WorkflowExecutableSummary) => {
    if (!explainExecutableAction) return;

    if (dispatchAiAction(ai, explainExecutableAction, executable)) {
      setError("");
      setLastRun(null);
      setStatus(""); getNotifications().notify({ level: "success", title: `Sent ${executable.artifactId} to Weaver` });
    }
  };

  const toggleReferences = (artifactId: string) => {
    setExpandedArtifactIds(previous => {
      const next = new Set(previous);
      if (next.has(artifactId)) next.delete(artifactId);
      else next.add(artifactId);
      return next;
    });
  };

  const openDefinitions = () => {
    window.history.pushState({}, "", qualifyStudioBackendPath("/workflows/definitions"));
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const pagerFirstItem = totalCount === 0 ? 0 : ((currentPage - 1) * pageSize) + 1;
  const pagerLastItem = totalCount === 0 ? 0 : Math.min(currentPage * pageSize, totalCount);

  return (
    <>
      {unpublishDialogTarget ? (
        <UnpublishExecutablePublicationDialog
          context={context}
          artifactId={unpublishDialogTarget.artifactId}
          definitionIds={unpublishDialogTarget.definitionIds}
          definitionNames={definitionNames}
          onClose={() => setUnpublishDialogTarget(null)}
          onUnpublished={async candidate => {
            const canNotify = captureNotificationGuard();
            setUnpublishDialogTarget(null);
            setError("");
            setLastRun(null);
            setStatus("");
            await load(true);
            if (canNotify()) getNotifications().notify({ level: "success", title: `Unpublished ${definitionNames.get(candidate.definitionId) ?? candidate.definitionId} · ${candidate.slot.slotName}` });
          }}
        />
      ) : null}
      {executableRun.pending ? (
        <WorkflowRunInputDialog
          inputs={executableRun.pending.inputs}
          editors={runInputEditors}
          onSubmit={values => { void executableRun.confirm(values); }}
          onCancel={executableRun.cancel}
        />
      ) : null}
      <div className="wf-executables-list">
      <StudioPageHeader title="Executables" headingLevel={2} description="Inspect and run immutable workflow executables." refresh={{
        id: `workflow-executables:${context.baseUrl}`,
        label: "Refresh executables",
        onRefresh: () => load(true),
        refreshing,
        autoRefresh: true,
        active: workspaceTab?.active ?? true,
        disabled: !!executableRun.pending || !!executableRun.runningArtifactId || !!artifactExport.exportingArtifactId
      }} />
      <StudioCollectionToolbar>
        <StudioSearchInput
          aria-label="Filter executables by workflow definition"
          list="wf-executable-definition-options"
          placeholder="Workflow name or ID"
          value={definitionFilter ?? ""}
          onChange={event => onDefinitionFilterChange(event.currentTarget.value || null)}
        />
        <StudioField className="wf-toolbar-field" label={<><span>Scope</span></>}><select aria-label="Executable reference scope" value={scope} onChange={event => setScope(event.target.value as WorkflowExecutableListScope)}>
            <option value="published">Published</option>
            <option value="test-runs">Test runs</option>
            <option value="all">All</option>
          </select></StudioField>
        <label className="wf-toolbar-field wf-toolbar-checkbox">
          <input
            type="checkbox"
            aria-label="Include retired references"
            checked={includeRetired}
            onChange={event => setIncludeRetired(event.target.checked)}
          />
          <span>Include retired</span>
        </label>
        <datalist id="wf-executable-definition-options">
          {definitionOptions.map(option => <option key={option.id} value={option.id} label={option.name === option.id ? option.id : `${option.name} · ${option.id}`} />)}
        </datalist>
        {definitionFilter ? (
          <StudioButton onClick={() => onDefinitionFilterChange(null)}><X size={13} /> Clear</StudioButton>
        ) : null}
      </StudioCollectionToolbar>
      {state === "failed" ? <StudioResultState tone="danger" icon={<AlertCircle size={18} aria-hidden />} title="Something went wrong" headingLevel={3} description={error || "Please try again, or check that the Elsa server is reachable."} /> : null}
      {state === "ready" && error ? <StudioResultState tone="danger" icon={<AlertCircle size={18} aria-hidden />} title="Something went wrong" headingLevel={3} description={error || "Please try again, or check that the Elsa server is reachable."} /> : null}
      {status ? <ExecutableRunStatusLine status={status} run={lastRun} /> : null}
      {state === "loading" ? <SkeletonRows /> : null}
      {state === "ready" && !error && visibleExecutables.length === 0 ? definitionNamesLoading ? (
        <div className="studio-table-container"><WfEmptyState headingLevel={3} icon={<Search size={22} />} title="Loading workflow names" description="Checking definition names for matching executables." /></div>
      ) : (
        <StudioCollectionEmptyState headingLevel={3}
          icon={<Play size={22} />}
          title="No workflow executables"
          description={definitionFilter ? "No executables match this definition filter." : "This backend has no executables in the selected scope."}
          actions={definitionFilter
            ? <StudioButton onClick={() => onDefinitionFilterChange(null)}><X size={13} /> Clear filter</StudioButton>
            : canBrowseDefinitions ? <StudioButton variant="primary" onClick={openDefinitions}>Open workflow definitions</StudioButton> : undefined}
        />
      ) : null}
      {state === "ready" && visibleExecutables.length > 0 ? (
        <div className="studio-table-container wf-executable-table-scroll" role="region" aria-label="Workflow executables table" tabIndex={0}>
          <div className="studio-collection-table wf-grid wf-executable-grid" role="table" aria-label="Workflow executables">
            <div className="wf-grid-head" role="row">
              <span role="columnheader">Workflow</span>
              <span role="columnheader">Identifiers</span>
              <span role="columnheader">Version</span>
              <span role="columnheader">Root</span>
              <span role="columnheader">Published</span>
              <span className="wf-executable-actions-header" role="columnheader">Actions</span>
            </div>
            {pagedExecutables.map(executable => {
              const references = executable.references ?? [];
              const expanded = expandedArtifactIds.has(executable.artifactId);
              const retired = Boolean(executable.deletedAt);
              const runTarget = createExecutableWorkflowRunTarget(executable);
              const definitionIds = getExecutableDefinitionIds(executable);
              const primaryName = definitionIds.length ? (definitionNames.get(definitionIds[0]) ?? "Workflow definition") : "Source unavailable";
              const publicationDefinitionIds = canManagePublications ? getPublishedWorkflowDefinitionIds(executable) : [];
              const identifiers = collectExecutableIdentifiers(executable);
              const sourceSummary = [executable.sourceKind ? formatExecutableSourceKind(executable.sourceKind) : null, executable.sourceVersion ? `Version ${executable.sourceVersion}` : null].filter(Boolean).join(" · ");
              const menuItems: OverflowMenuItem[] = [
                ...publicationDefinitionIds.map(definitionId => ({
                  id: `manage-publication:${definitionId}`,
                  label: publicationDefinitionIds.length === 1
                    ? "Manage publication"
                    : `Manage publication · ${definitionNames.get(definitionId) ?? definitionId} (${definitionId})`,
                  onSelect: () => openExecutablePublicationManagement(definitionId)
                })),
                ...(reviewedUnpublishAvailable && publicationDefinitionIds.length ? [{
                  id: "unpublish-publication",
                  label: "Unpublish…",
                  onSelect: () => setUnpublishDialogTarget({ artifactId: executable.artifactId, definitionIds: publicationDefinitionIds })
                }] : []),
                ...(artifactExport.supported ? [{
                  id: "export",
                  label: artifactExport.exportingArtifactId === executable.artifactId ? "Exporting…" : "Export",
                  disabled: !executable.definitionVersionId || Boolean(artifactExport.exportingArtifactId),
                  onSelect: () => { void artifactExport.exportArtifact(executable); }
                }] : []),
                ...(explainExecutableAction ? [{
                  id: "explain",
                  label: "Explain",
                  onSelect: () => explain(executable)
                }] : [])
              ];
              return (
                <Fragment key={executable.artifactId}>
                  <div
                    className="wf-grid-row"
                    role="row"
                    {...studioRowInteraction<HTMLDivElement>(() => openExecutableInspector(executable.artifactId))}
                    aria-label={`Inspect executable ${executable.artifactId}`}
                  >
                    <span className="wf-executable-name" role="cell">
                      <WorkflowExecutableOrigins executable={executable} definitionNames={definitionNames} />
                    </span>
                    <span className="wf-executable-identifiers" role="cell">
                      <span className="wf-list-primary-id">
                        <CopyableIdentifier label="Executable ID" value={executable.artifactId} />
                      </span>
                      <WorkflowListIdentifiers kind="Workflow executable" name={primaryName} identifiers={identifiers}>
                        {retired ? <p>Retired · Retained for inspection</p> : null}
                        {sourceSummary ? <p title={sourceSummary}>{sourceSummary}</p> : null}
                      </WorkflowListIdentifiers>
                      {retired ? <span className="wf-chip wf-reference-retired">Retired</span> : null}
                    </span>
                    <span className="wf-executable-version-cell" role="cell">
                      <span className="wf-cell-line">
                        <span>{executable.artifactVersion}</span>
                      </span>
                      {references.length > 0 ? (
                        <StudioButton
                          type="button"
                          className="wf-link-button wf-reference-toggle"
                          aria-expanded={expanded}
                          aria-label={`${expanded ? "Hide" : "Show"} references of ${executable.artifactId}`}
                          onClick={() => toggleReferences(executable.artifactId)}
                        >
                          {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
                          {references.length} reference{references.length === 1 ? "" : "s"}
                        </StudioButton>
                      ) : null}
                    </span>
                    <span className="wf-executable-root" role="cell">{formatExecutableRoot(executable)}</span>
                    <span className="wf-executable-published" role="cell">
                      <StudioTimestamp value={executable.publishedAt ?? executable.createdAt} fallback={executable.publishedAt ?? executable.createdAt ?? ""} />
                    </span>
                    <span className="wf-row-actions" role="cell" onClick={event => event.stopPropagation()}>
                      <StudioButton type="button" aria-label={`Inspect executable ${executable.artifactId}`} onClick={() => openExecutableInspector(executable.artifactId)}><ScanSearch size={13} /> Inspect</StudioButton>
                      <ExecutableRunButton
                        target={runTarget}
                        runningArtifactId={executableRun.runningArtifactId}
                        onRequest={executableRun.request}
                      />
                      {menuItems.length ? <OverflowMenu items={menuItems} ariaLabel={`Actions for ${primaryName}`} /> : null}
                      {retired ? <span className="wf-visually-hidden">Retained for inspection</span> : null}
                    </span>
                  </div>
                  {expanded ? (
                    <div className="wf-executable-reference-row" role="row">
                      <div role="cell">
                        <ExecutableReferenceList references={references} definitionNames={definitionNames} ariaLabel={`References of ${executable.artifactId}`} />
                      </div>
                    </div>
                  ) : null}
                </Fragment>
              );
            })}
          </div>
        </div>
      ) : null}
      {state === "ready" ? (
        <StudioCollectionFooter aria-label="Workflow executable pagination">
          <span className="wf-pagination-summary" aria-live="polite">
            Showing {pagerFirstItem}-{pagerLastItem} of {totalCount}
          </span>
          <StudioField className="wf-page-size" label="Rows"><select aria-label="Executable page size" value={pageSize} onChange={event => { setPageSize(Number(event.target.value)); setPage(1); }}>
              {definitionPageSizes.map(size => <option key={size} value={size}>{size}</option>)}
            </select></StudioField>
          <div className="wf-page-controls">
            <StudioButton onClick={() => setPage(currentPage - 1)} disabled={currentPage <= 1} aria-label="Previous page" title="Previous page">
              <ChevronLeft size={14} /> Previous
            </StudioButton>
            <span>Page {currentPage} of {totalPages}</span>
            <StudioButton onClick={() => setPage(currentPage + 1)} disabled={currentPage >= totalPages} aria-label="Next page" title="Next page">
              Next <ChevronRight size={14} />
            </StudioButton>
          </div>
        </StudioCollectionFooter>
      ) : null}
      </div>
    </>
  );
}

function getPublishedWorkflowDefinitionIds(executable: WorkflowExecutableSummary) {
  const definitionIds = new Set<string>();
  for (const reference of executable.references ?? []) {
    const definitionId = reference.definitionId;
    if (reference.scope.toLowerCase() !== "published" || !definitionId?.trim() || definitionIds.has(definitionId)) continue;
    definitionIds.add(definitionId);
  }
  return [...definitionIds];
}

interface ExecutablePublicationSlotMatch {
  definitionId: string;
  slot: PublicationSlot;
}

function executablePublicationSlotMatchKey(match: ExecutablePublicationSlotMatch) {
  return JSON.stringify([match.definitionId, match.slot.slotName, match.slot.activeActivationId, match.slot.revision]);
}

function UnpublishExecutablePublicationDialog({ context, artifactId, definitionIds, definitionNames, onClose, onUnpublished }: {
  context: StudioEndpointContext;
  artifactId: string;
  definitionIds: string[];
  definitionNames: ReadonlyMap<string, string>;
  onClose(): void;
  onUnpublished(match: ExecutablePublicationSlotMatch): Promise<void>;
}) {
  const [loadState, setLoadState] = useState<"loading" | "ready" | "failed">("loading");
  const [matches, setMatches] = useState<ExecutablePublicationSlotMatch[]>([]);
  const [selectedMatchKey, setSelectedMatchKey] = useState<string | null>(null);
  const [readError, setReadError] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    let active = true;
    setLoadState("loading");
    setMatches([]);
    setSelectedMatchKey(null);
    setReadError("");
    setError("");
    void Promise.all(definitionIds.map(async definitionId => {
      const view = await listPublicationSlots(context, definitionId);
      if (!view.available) throw new Error(view.reason);
      return view.slots
        .filter(slot => Boolean(slot.activeActivationId) && slot.publication?.artifactId === artifactId)
        .map(slot => ({ definitionId, slot }));
    })).then(results => {
      if (!active) return;
      const candidates = results.flat().sort((left, right) =>
        left.definitionId.localeCompare(right.definitionId)
        || left.slot.slotName.localeCompare(right.slot.slotName));
      setMatches(candidates);
      setSelectedMatchKey(!notice && candidates.length === 1 ? executablePublicationSlotMatchKey(candidates[0]) : null);
      setLoadState("ready");
    }).catch(caught => {
      if (!active) return;
      setReadError(caught instanceof Error ? caught.message : String(caught));
      setLoadState("failed");
    });
    return () => { active = false; };
  }, [artifactId, context, definitionIds, notice, reload]);

  const selectedMatch = matches.find(match => executablePublicationSlotMatchKey(match) === selectedMatchKey) ?? null;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!selectedMatch || loadState !== "ready" || busy) return;
    setBusy(true);
    setError("");
    try {
      await unpublishReviewedSlot(context, selectedMatch.definitionId, selectedMatch.slot.slotName, selectedMatch.slot.revision);
      await onUnpublished(selectedMatch);
    } catch (caught) {
      if (caught instanceof StudioHttpError && caught.status === 409) {
        setNotice("This slot changed after you reviewed it. The current live slots have been reloaded; review and confirm again.");
        setSelectedMatchKey(null);
        setLoadState("loading");
        setReload(current => current + 1);
      } else {
        setError(caught instanceof Error ? caught.message : String(caught));
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <StudioDialog open title="Unpublish publication slot" description="Choose the live publication slot that currently serves this artifact."
      onClose={onClose} dismissDisabled={busy} dismissOnOutsideClick={false}
      onSubmit={event => { void submit(event); }}
      footer={<StudioDialogFooter
        onCancel={onClose}
        cancelDisabled={busy}
        confirmLabel={busy ? "Unpublishing…" : "Unpublish"}
        confirmDisabled={busy || loadState !== "ready" || !selectedMatch}
      />}>
      <div className="wf-run-input-list">
        <p>Unpublishing a slot stops new runs from starting through that slot. Existing runs and other publication slots are unaffected.</p>
        {notice ? <p role="status">{notice}</p> : null}
        {loadState === "loading" ? <p role="status">Checking live publication slots…</p> : null}
        {loadState === "failed" ? (
          <div role="alert">
            <p>Couldn't verify which live slots serve this artifact. {readError}</p>
            <StudioButton type="button" disabled={busy} onClick={() => { setNotice(""); setReload(current => current + 1); }}>Reload slots</StudioButton>
          </div>
        ) : null}
        {loadState === "ready" && matches.length === 0 ? <p role="status">No live publication slots currently serve this artifact.</p> : null}
        {loadState === "ready" && matches.length > 0 ? (
          <fieldset disabled={busy}>
            <legend>{matches.length === 1 ? "Publication slot" : "Select a publication slot"}</legend>
            {matches.map(match => {
              const key = executablePublicationSlotMatchKey(match);
              const definitionName = definitionNames.get(match.definitionId) ?? match.definitionId;
              return (
                <label key={key}>
                  <input
                    type="radio"
                    name="executable-publication-slot"
                    value={key}
                    checked={selectedMatchKey === key}
                    onChange={() => setSelectedMatchKey(key)}
                  />
                  {definitionName} ({match.definitionId}) · Slot {match.slot.slotName}
                </label>
              );
            })}
          </fieldset>
        ) : null}
        {selectedMatch ? (
          <p role="note">
            Confirming will stop new runs through {definitionNames.get(selectedMatch.definitionId) ?? selectedMatch.definitionId} · slot {selectedMatch.slot.slotName}. Existing runs and other slots will continue unaffected.
          </p>
        ) : null}
        {error ? <p role="alert">Couldn't unpublish this slot. {error}</p> : null}
      </div>
    </StudioDialog>
  );
}

function openExecutablePublicationManagement(definitionId: string) {
  const definitionUrl = new URL(qualifyStudioBackendPath("/workflows/definitions"), window.location.href);
  const url = updateWorkflowDefinitionIdInUrl(definitionUrl, definitionId, "artifacts");
  window.history.pushState({}, "", `${url.pathname}${url.search}${url.hash}`);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

function collectExecutableIdentifiers(executable: WorkflowExecutableSummary): WorkflowListIdentifier[] {
  const definitionIds = getExecutableDefinitionIds(executable);
  const items: WorkflowListIdentifier[] = [
    { label: "Executable hash", value: executable.artifactHash },
    { label: "Source ID", value: executable.sourceId || executable.definitionVersionId },
    { label: "Definition version ID", value: executable.definitionVersionId },
    ...definitionIds.map((id, index) => ({
      label: definitionIds.length === 1 ? "Definition ID" : `Definition ID ${index + 1}`,
      value: id
    }))
  ];
  const seen = new Set<string>();
  return items.filter(item => {
    if (!item.value || seen.has(item.value)) return false;
    seen.add(item.value);
    return true;
  });
}

function WorkflowExecutableOrigins({ executable, definitionNames }: { executable: WorkflowExecutableSummary; definitionNames: ReadonlyMap<string, string> }) {
  const definitionIds = getExecutableDefinitionIds(executable);

  const openSourceDefinition = (definitionId: string) => {
    window.history.pushState({}, "", qualifyStudioBackendPath(`/workflows/definitions?definition=${encodeURIComponent(definitionId)}`));
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return (
    <div className="wf-executable-workflow-cell">
      {definitionIds.map(definitionId => {
        const name = definitionNames.get(definitionId) ?? "Workflow definition";
        return (
          <StudioButton
            type="button"
            className="wf-executable-workflow-name"
            key={definitionId}
            title={`Open source workflow: ${name}`}
            aria-label={`Open source definition ${definitionId}: ${name}`}
            onClick={() => openSourceDefinition(definitionId)}
          >
            {name}
          </StudioButton>
        );
      })}
      {definitionIds.length === 0 ? <strong title="Source unavailable">Source unavailable</strong> : null}
    </div>
  );
}

export function WorkflowArtifactsPanel({ context, ai, runInputEditors, definitionId, publishedArtifactId }: { context: StudioEndpointContext; ai: StudioWeaverRegistry; runInputEditors: StudioWorkflowRunInputEditorContribution[]; definitionId: string; publishedArtifactId: string | null }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [lastRun, setLastRun] = useState<ExecutableRunState | null>(null);
  const [artifacts, setArtifacts] = useState<WorkflowExecutableSummary[]>([]);
  const [slots, setSlots] = useState<PublicationSlot[]>([]);
  const [slotsUnavailableReason, setSlotsUnavailableReason] = useState<string | null>(null);
  const [lifecycleSupport, setLifecycleSupport] = useState({ unpublish: false, restore: false });
  const explainExecutableAction = findAiAction(ai, "weaver.workflows.explain-executable");
  const executableRun = useExecutableWorkflowRun({
    context,
    ...createExecutableWorkflowRunFeedback({ setStatus, setLastRun, setError })
  });
  const artifactExport = useExecutableArtifactDownload({
    context,
    onExported: (fileName, _executable, canNotify) => { setError(""); setLastRun(null); setStatus(""); if (canNotify()) getNotifications().notify({ level: "success", title: `Exported ${fileName}` }); },
    onFailed: message => { setStatus(""); setLastRun(null); setError(message); }
  });

  const load = useCallback(async () => {
    setState("loading");
    setError("");
    try {
      const [executables, slotsView, lifecycle] = await Promise.all([
        listExecutables(context, { scope: "all", includeRetired: true }),
        listPublicationSlots(context, definitionId),
        getPublicationSlotLifecycleSupport(context)
      ]);
      const publicationSlots = slotsView.available ? slotsView.slots : [];
      const publicationArtifactIds = new Set(publicationSlots.flatMap(slot => slot.publication?.artifactId ? [slot.publication.artifactId] : []));
      setArtifacts(executables.filter(executable => publicationArtifactIds.has(executable.artifactId)).sort(compareExecutablesByPublishedDate));
      setSlots(publicationSlots);
      setSlotsUnavailableReason(slotsView.available ? null : slotsView.reason);
      setLifecycleSupport(lifecycle);
      setState("ready");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setArtifacts([]);
      setSlots([]);
      setSlotsUnavailableReason(null);
      setState("failed");
    }
  }, [context, definitionId]);

  useEffect(() => {
    void load();
  }, [load, publishedArtifactId]);

  const explain = (artifact: WorkflowExecutableSummary) => {
    if (!explainExecutableAction) return;

    if (dispatchAiAction(ai, explainExecutableAction, artifact)) {
      setError("");
      setLastRun(null);
      setStatus(""); getNotifications().notify({ level: "success", title: `Sent ${artifact.artifactId} to Weaver` });
    }
  };

  const captureNotificationGuard = useNotificationGuard(context);
  const unpublish = async (slot: PublicationSlot) => {
    const canNotify = captureNotificationGuard();
    if (!(await getDialogs().confirm({ message: `Unpublish slot ${slot.slotName}? Existing workflow runs keep their pinned executable, but this slot will stop starting new runs.`, confirmLabel: "Unpublish", tone: "danger" }))) return;
    setStatus("");
    setLastRun(null);
    setError("");
    try {
      await unpublishSlot(context, definitionId, slot.slotName);
      if (canNotify()) getNotifications().notify({ level: "success", title: `Unpublished ${slot.slotName}` });
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const restore = async (slot: PublicationSlot) => {
    const canNotify = captureNotificationGuard();
    setStatus("");
    setLastRun(null);
    setError("");
    try {
      await restorePublicationSlot(context, definitionId, slot.slotName);
      if (canNotify()) getNotifications().notify({ level: "success", title: `Restored ${slot.slotName}` });
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const unavailableLifecycleCommands = [
    !lifecycleSupport.unpublish && slots.some(slot => slot.publication) ? "unpublish" : null,
    !lifecycleSupport.restore && slots.some(slot => !slot.activeActivationId) ? "restore" : null
  ].filter((command): command is string => Boolean(command));

  const openExecutablePage = () => {
    window.history.pushState({}, "", qualifyStudioBackendPath(`/workflows/executables?definition=${encodeURIComponent(definitionId)}`));
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const markCopied = (label: string) => {
    setError("");
    setLastRun(null);
    setStatus(""); getNotifications().notify({ level: "success", title: `Copied ${label}` });
  };

  const markCopyFailed = (label: string) => {
    setStatus("");
    setLastRun(null);
    setError(`Could not copy ${label}.`);
  };

  return (
    <div className="wf-artifacts-panel">
      {executableRun.pending ? (
        <WorkflowRunInputDialog
          inputs={executableRun.pending.inputs}
          editors={runInputEditors}
          onSubmit={values => { void executableRun.confirm(values); }}
          onCancel={executableRun.cancel}
        />
      ) : null}
      <div className="wf-artifacts-toolbar">
        <span>{artifacts.length} artifact{artifacts.length === 1 ? "" : "s"}</span>
        <StudioButton type="button" onClick={() => void load()}><RotateCcw size={13} /> Refresh</StudioButton>
        <StudioButton type="button" onClick={openExecutablePage}>Open list</StudioButton>
      </div>
      {state === "failed" || error ? <div className="wf-alert compact"><AlertCircle size={14} /> {error}</div> : null}
      {status ? <ExecutableRunStatusLine status={status} run={lastRun} compact /> : null}
      {state === "loading" ? <p className="wf-muted">Loading artifacts...</p> : null}
      {state === "ready" && slots.length > 0 ? (
        <div className="wf-artifact-list" role="list" aria-label="Publication slots">
          {slots.map(slot => (
            <article className="wf-artifact-card" role="listitem" key={slot.slotName}>
              <div className="wf-artifact-card-heading">
                <strong>Slot {slot.slotName}</strong>
                <span className="wf-chip">{slot.publication?.status ?? (slot.activeActivationId ? "active" : "inactive")}</span>
              </div>
              {slot.publication ? (
                <p><code>{slot.publication.artifactId}</code></p>
              ) : slot.activeActivationId ? (
                <p className="wf-muted">Occupied by {describeActivationSource(slot)}, not by a Studio publication</p>
              ) : (
                <p className="wf-muted">No active publication</p>
              )}
              <div className="wf-row-actions">
                {/* Restore is offered for every inactive slot, not just ones known to have been published: Runtime
                    clears the activation source on deactivation, so Studio cannot tell which owner last held the
                    slot. The backend answers with a 404 (surfaced above as an error) when there is nothing to
                    restore. */}
                {!slot.activeActivationId ? (
                  <StudioButton type="button" aria-label={`Restore publication slot ${slot.slotName}`} disabled={!lifecycleSupport.restore} onClick={() => void restore(slot)}><RotateCcw size={13} /> Restore</StudioButton>
                ) : slot.publication ? (
                  <StudioButton type="button" aria-label={`Unpublish publication slot ${slot.slotName}`} disabled={!lifecycleSupport.unpublish} onClick={() => void unpublish(slot)}>Unpublish</StudioButton>
                ) : null}
              </div>
            </article>
          ))}
        </div>
      ) : null}
      {state === "ready" && unavailableLifecycleCommands.length ? (
        <p className="wf-muted" role="note">
          This backend does not advertise the publication slot {unavailableLifecycleCommands.join(" and ")} command{unavailableLifecycleCommands.length === 1 ? "" : "s"}. No fallback route is used.
        </p>
      ) : null}
      {state === "ready" && slotsUnavailableReason ? <p className="wf-muted" role="note">{slotsUnavailableReason}</p> : null}
      {state === "ready" && !slotsUnavailableReason && artifacts.length === 0 ? <p className="wf-muted">No published artifacts for this workflow yet.</p> : null}
      {state === "ready" && artifacts.length > 0 ? (
        <div className="wf-artifact-list" role="list" aria-label="Workflow artifacts">
          {artifacts.map(artifact => {
            const runTarget = createExecutableWorkflowRunTarget(artifact);
            return (
              <article className="wf-artifact-card" role="listitem" key={artifact.artifactId} data-active={artifact.artifactId === publishedArtifactId ? "true" : undefined}>
              <div className="wf-artifact-card-heading">
                <div>
                  <span className="wf-artifact-version">Version {artifact.artifactVersion}</span>
                  {artifact.artifactId === publishedArtifactId ? <span className="wf-chip">Latest publish</span> : null}
                </div>
                <span>{formatDate(artifact.publishedAt ?? artifact.createdAt)}</span>
              </div>
              <div className="wf-artifact-card-values">
                <span className="wf-cell-line">
                  <code title={artifact.artifactId}>{artifact.artifactId}</code>
                  <CopyValueButton scope={context} value={artifact.artifactId} ariaLabel={`Copy executable ID ${artifact.artifactId}`} copiedLabel="executable ID" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                </span>
                <span className="wf-cell-line wf-cell-line-muted">
                  <code title={artifact.artifactHash}>{artifact.artifactHash}</code>
                  <CopyValueButton scope={context} value={artifact.artifactHash} ariaLabel={`Copy executable hash ${artifact.artifactHash}`} copiedLabel="executable hash" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                </span>
              </div>
              <dl>
                <div><dt>Source</dt><dd>{formatExecutableSourceKind(artifact.sourceKind)} {artifact.sourceVersion ? `v${artifact.sourceVersion}` : ""}</dd></div>
                <div><dt>Root</dt><dd>{formatExecutableRoot(artifact)}</dd></div>
              </dl>
              <div className="wf-row-actions">
                <StudioButton type="button" aria-label={`Inspect executable ${artifact.artifactId}`} onClick={() => openExecutableInspector(artifact.artifactId)}><ScanSearch size={13} /> Inspect</StudioButton>
                <ExecutableRunButton
                  target={runTarget}
                  runningArtifactId={executableRun.runningArtifactId}
                  ariaLabel={`Run executable ${artifact.artifactId}`}
                  onRequest={executableRun.request}
                />
                {artifactExport.supported ? (
                  <ExecutableExportButton
                    executable={artifact}
                    exportingArtifactId={artifactExport.exportingArtifactId}
                    ariaLabel={`Export executable artifact ${artifact.artifactId}`}
                    onRequest={artifactExport.exportArtifact}
                  />
                ) : null}
                {explainExecutableAction ? <StudioButton type="button" onClick={() => explain(artifact)}><Sparkles size={13} /> Explain</StudioButton> : null}
              </div>
              </article>
            );
          })}
        </div>
      ) : null}
    </div>
  );
}
