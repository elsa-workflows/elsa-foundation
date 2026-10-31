import { CopyableIdentifier, OverflowMenu, StudioButton, StudioCollectionFooter, StudioCollectionToolbar, StudioPageHeader, StudioSearchInput, type OverflowMenuItem } from "@elsa-workflows/studio-ui";
import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertCircle, ChevronDown, ChevronLeft, ChevronRight, Play, RotateCcw, ScanSearch, Search, Sparkles, X } from "lucide-react";
import type { StudioAgentRegistry, StudioEndpointContext, StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import { listExecutables } from "../api/runtime";
import { listDefinitions } from "../api/workflowDesign";
import { executableMatchesDefinitionSearch, getExecutableDefinitionIds, loadDefinitionNames } from "./executableDefinitionNames";
import {
  describeActivationSource,
  getPublicationSlotLifecycleSupport,
  listPublicationSlots,
  restorePublicationSlot,
  unpublishSlot,
  type PublicationSlot
} from "../api/publishing";
import type { WorkflowExecutableListScope, WorkflowExecutableSummary } from "../workflowTypes";
import { formatDate } from "../workflowFormatting";
import { WfEmptyState, WfErrorCard, WfListSkeleton } from "./StatusViews";
import { ExecutableReferenceList, ExecutableRunStatusLine, CopyValueButton } from "./executableShared";
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

export function openExecutableInspector(artifactId: string, sourceReferenceId?: string | null) {
  const query = sourceReferenceId ? `?ref=${encodeURIComponent(sourceReferenceId)}` : "";
  window.history.pushState({}, "", `/workflows/executables/${encodeURIComponent(artifactId)}${query}`);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

export function WorkflowExecutables({ context, ai, runInputEditors, definitionFilter, onDefinitionFilterChange }: { context: StudioEndpointContext; ai: StudioAgentRegistry; runInputEditors: StudioWorkflowRunInputEditorContribution[]; definitionFilter: string | null; onDefinitionFilterChange(filter: string | null): void }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
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
    onExported: fileName => { setError(""); setLastRun(null); setStatus(`Exported ${fileName}`); },
    onFailed: message => { setStatus(""); setLastRun(null); setError(message); }
  });

  const load = useCallback(async () => {
    const generation = ++loadGeneration.current;
    setState("loading");
    setDefinitionNamesLoading(false);
    setError("");
    try {
      const items = await listExecutables(context, { scope, includeRetired });
      if (generation !== loadGeneration.current) return;
      setExecutables(items);
      setDefinitionNames(new Map());
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
      setDefinitionNamesLoading(false);
      setError(e instanceof Error ? e.message : String(e));
      setState("failed");
    }
  }, [context, includeRetired, scope]);

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
      setStatus(`Sent ${executable.artifactId} to Weaver`);
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
    window.history.pushState({}, "", "/workflows/definitions");
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const pagerFirstItem = totalCount === 0 ? 0 : ((currentPage - 1) * pageSize) + 1;
  const pagerLastItem = totalCount === 0 ? 0 : Math.min(currentPage * pageSize, totalCount);

  return (
    <>
      {executableRun.pending ? (
        <WorkflowRunInputDialog
          inputs={executableRun.pending.inputs}
          editors={runInputEditors}
          onSubmit={values => { void executableRun.confirm(values); }}
          onCancel={executableRun.cancel}
        />
      ) : null}
      <div className="wf-executables-list">
      <StudioPageHeader title="Executables" headingLevel={2} description="Inspect and run immutable workflow executables." actions={<StudioButton onClick={() => void load()}>Refresh</StudioButton>} />
      <StudioCollectionToolbar>
        <StudioSearchInput
          aria-label="Filter executables by workflow definition"
          list="wf-executable-definition-options"
          placeholder="Workflow name or ID"
          value={definitionFilter ?? ""}
          onChange={event => onDefinitionFilterChange(event.currentTarget.value || null)}
        />
        <label className="wf-toolbar-field">
          <span>Scope</span>
          <select aria-label="Executable reference scope" value={scope} onChange={event => setScope(event.target.value as WorkflowExecutableListScope)}>
            <option value="published">Published</option>
            <option value="test-runs">Test runs</option>
            <option value="all">All</option>
          </select>
        </label>
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
      {state === "failed" ? <WfErrorCard headingLevel={3} message={error} /> : null}
      {state === "ready" && error ? <WfErrorCard headingLevel={3} message={error} /> : null}
      {status ? <ExecutableRunStatusLine status={status} run={lastRun} /> : null}
      {state === "loading" ? <WfListSkeleton /> : null}
      {state === "ready" && visibleExecutables.length === 0 ? (
        definitionNamesLoading ? (
          <WfEmptyState headingLevel={3} icon={<Search size={22} />} title="Loading workflow names" description="Checking definition names for matching executables." />
        ) : (
          <WfEmptyState headingLevel={3}
            icon={<Play size={22} />}
            title="No workflow executables"
            description={definitionFilter ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."}
            action={definitionFilter
              ? <StudioButton onClick={() => onDefinitionFilterChange(null)}><X size={13} /> Clear filter</StudioButton>
              : <StudioButton variant="primary" onClick={openDefinitions}>Open workflow definitions</StudioButton>}
          />
        )
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
              const identifiers = collectExecutableIdentifiers(executable);
              const sourceSummary = [executable.sourceKind ? formatExecutableSourceKind(executable.sourceKind) : null, executable.sourceVersion ? `Version ${executable.sourceVersion}` : null].filter(Boolean).join(" · ");
              const menuItems: OverflowMenuItem[] = [
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
                    tabIndex={0}
                    aria-label={`Inspect executable ${executable.artifactId}`}
                    onClick={event => {
                      if (event.target instanceof Element && event.target.closest("button, input, label, a")) return;
                      openExecutableInspector(executable.artifactId);
                    }}
                    onKeyDown={event => {
                      if (event.target !== event.currentTarget) return;
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        openExecutableInspector(executable.artifactId);
                      }
                    }}
                  >
                    <span className="wf-executable-name" role="cell">
                      <WorkflowExecutableOrigins executable={executable} definitionNames={definitionNames} />
                    </span>
                    <span className="wf-executable-identifiers" role="cell" onClick={event => event.stopPropagation()}>
                      <span className="wf-list-primary-id">
                        <CopyableIdentifier label="Artifact ID" value={executable.artifactId} />
                      </span>
                      <WorkflowListIdentifiers kind="Workflow executable" name={primaryName} identifiers={identifiers}>
                        {retired ? <p>Retired · Retained for inspection</p> : null}
                        {sourceSummary ? <p title={sourceSummary}>{sourceSummary}</p> : null}
                      </WorkflowListIdentifiers>
                      {retired ? <span className="wf-chip wf-reference-retired">Retired</span> : null}
                    </span>
                    <span className="wf-executable-version-cell" role="cell" onClick={event => event.stopPropagation()}>
                      <span className="wf-cell-line">
                        <span>{executable.artifactVersion}</span>
                      </span>
                      {references.length > 0 ? (
                        <button
                          type="button"
                          className="wf-link-button wf-reference-toggle"
                          aria-expanded={expanded}
                          aria-label={`${expanded ? "Hide" : "Show"} references of ${executable.artifactId}`}
                          onClick={() => toggleReferences(executable.artifactId)}
                        >
                          {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
                          {references.length} reference{references.length === 1 ? "" : "s"}
                        </button>
                      ) : null}
                    </span>
                    <span className="wf-executable-root" role="cell">{formatExecutableRoot(executable)}</span>
                    <span className="wf-executable-published" role="cell">
                      <time
                        dateTime={executable.publishedAt ?? executable.createdAt}
                        title={formatDate(executable.publishedAt ?? executable.createdAt)}
                      >
                        {formatExecutableListDate(executable.publishedAt ?? executable.createdAt)}
                      </time>
                    </span>
                    <span className="wf-row-actions" role="cell" onClick={event => event.stopPropagation()}>
                      <button type="button" aria-label={`Inspect executable ${executable.artifactId}`} onClick={() => openExecutableInspector(executable.artifactId)}><ScanSearch size={13} /> Inspect</button>
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
          <label className="wf-page-size">
            Rows
            <select aria-label="Executable page size" value={pageSize} onChange={event => { setPageSize(Number(event.target.value)); setPage(1); }}>
              {definitionPageSizes.map(size => <option key={size} value={size}>{size}</option>)}
            </select>
          </label>
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

function formatExecutableListDate(value?: string | null) {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString();
}

function collectExecutableIdentifiers(executable: WorkflowExecutableSummary): WorkflowListIdentifier[] {
  const definitionIds = getExecutableDefinitionIds(executable);
  const items: WorkflowListIdentifier[] = [
    { label: "Artifact hash", value: executable.artifactHash },
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
    window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(definitionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return (
    <div className="wf-executable-workflow-cell">
      {definitionIds.map(definitionId => {
        const name = definitionNames.get(definitionId) ?? "Workflow definition";
        return (
          <button
            type="button"
            className="wf-link-button wf-executable-workflow-name"
            key={definitionId}
            title={name}
            aria-label={`Open source definition ${definitionId}: ${name}`}
            onClick={() => openSourceDefinition(definitionId)}
          >
            {name}
          </button>
        );
      })}
      {definitionIds.length === 0 ? <strong title="Source unavailable">Source unavailable</strong> : null}
    </div>
  );
}

export function WorkflowArtifactsPanel({ context, ai, runInputEditors, definitionId, publishedArtifactId }: { context: StudioEndpointContext; ai: StudioAgentRegistry; runInputEditors: StudioWorkflowRunInputEditorContribution[]; definitionId: string; publishedArtifactId: string | null }) {
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
    onExported: fileName => { setError(""); setLastRun(null); setStatus(`Exported ${fileName}`); },
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
      setStatus(`Sent ${artifact.artifactId} to Weaver`);
    }
  };

  const unpublish = async (slot: PublicationSlot) => {
    if (!(await getDialogs().confirm({ message: `Unpublish slot ${slot.slotName}? Existing workflow runs keep their pinned executable, but this slot will stop starting new runs.`, confirmLabel: "Unpublish", tone: "danger" }))) return;
    setStatus("");
    setLastRun(null);
    setError("");
    try {
      await unpublishSlot(context, definitionId, slot.slotName);
      setStatus(`Unpublished ${slot.slotName}`);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const restore = async (slot: PublicationSlot) => {
    setStatus("");
    setLastRun(null);
    setError("");
    try {
      await restorePublicationSlot(context, definitionId, slot.slotName);
      setStatus(`Restored ${slot.slotName}`);
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
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(definitionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const markCopied = (label: string) => {
    setError("");
    setLastRun(null);
    setStatus(`Copied ${label}`);
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
        <button type="button" onClick={() => void load()}><RotateCcw size={13} /> Refresh</button>
        <button type="button" onClick={openExecutablePage}>Open list</button>
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
                  <button type="button" aria-label={`Restore publication slot ${slot.slotName}`} disabled={!lifecycleSupport.restore} onClick={() => void restore(slot)}><RotateCcw size={13} /> Restore</button>
                ) : slot.publication ? (
                  <button type="button" aria-label={`Unpublish publication slot ${slot.slotName}`} disabled={!lifecycleSupport.unpublish} onClick={() => void unpublish(slot)}>Unpublish</button>
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
                  <CopyValueButton value={artifact.artifactId} ariaLabel={`Copy artifact ID ${artifact.artifactId}`} copiedLabel="artifact ID" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                </span>
                <span className="wf-cell-line wf-cell-line-muted">
                  <code title={artifact.artifactHash}>{artifact.artifactHash}</code>
                  <CopyValueButton value={artifact.artifactHash} ariaLabel={`Copy artifact hash ${artifact.artifactHash}`} copiedLabel="artifact hash" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                </span>
              </div>
              <dl>
                <div><dt>Source</dt><dd>{formatExecutableSourceKind(artifact.sourceKind)} {artifact.sourceVersion ? `v${artifact.sourceVersion}` : ""}</dd></div>
                <div><dt>Root</dt><dd>{formatExecutableRoot(artifact)}</dd></div>
              </dl>
              <div className="wf-row-actions">
                <button type="button" aria-label={`Inspect executable ${artifact.artifactId}`} onClick={() => openExecutableInspector(artifact.artifactId)}><ScanSearch size={13} /> Inspect</button>
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
                {explainExecutableAction ? <button type="button" onClick={() => explain(artifact)}><Sparkles size={13} /> Explain</button> : null}
              </div>
              </article>
            );
          })}
        </div>
      ) : null}
    </div>
  );
}
