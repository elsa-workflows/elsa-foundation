import { StudioPageHeader, StudioButton, StudioSearchInput } from "@elsa-workflows/studio-ui";
import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertCircle, ChevronDown, ChevronRight, Play, RotateCcw, ScanSearch, Search, Sparkles, X } from "lucide-react";
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
import {
  compareExecutablesByPublishedDate,
  dispatchAiAction,
  findAiAction,
  formatExecutableRoot,
  formatExecutableSourceKind
} from "./editorHelpers";

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
  const normalizedDefinitionFilter = definitionFilter?.trim().toLowerCase() ?? "";
  const visibleExecutables = useMemo(
    () => normalizedDefinitionFilter
      ? executables.filter(executable => executableMatchesDefinitionSearch(executable, normalizedDefinitionFilter, definitionNames))
      : executables,
    [normalizedDefinitionFilter, executables, definitionNames]
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
    <>
      {executableRun.pending ? (
        <WorkflowRunInputDialog
          inputs={executableRun.pending.inputs}
          editors={runInputEditors}
          onSubmit={values => { void executableRun.confirm(values); }}
          onCancel={executableRun.cancel}
        />
      ) : null}
      <StudioPageHeader title="Executables" headingLevel={2} description="Inspect and run immutable workflow executables." actions={<StudioButton onClick={() => void load()}>Refresh</StudioButton>} />
      <div className="studio-collection-toolbar">
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
      </div>
      {state === "failed" ? <WfErrorCard message={error} /> : null}
      {state === "ready" && error ? <WfErrorCard message={error} /> : null}
      {status ? <ExecutableRunStatusLine status={status} run={lastRun} /> : null}
      {state === "loading" ? <WfListSkeleton /> : null}
      {state === "ready" && visibleExecutables.length === 0 ? (
        definitionNamesLoading ? (
          <WfEmptyState icon={<Search size={22} />} title="Loading workflow names" description="Checking definition names for matching executables." />
        ) : (
          <WfEmptyState
            icon={<Play size={22} />}
            title="No workflow executables"
            description={definitionFilter ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."}
          />
        )
      ) : null}
      {state === "ready" && visibleExecutables.length > 0 ? (
        <div className="studio-table-container">
          <table className="studio-table wf-executable-table" aria-label="Workflow executables">
            <colgroup>
              <col className="wf-executable-workflow-column" /><col className="wf-executable-artifact-column" />
              <col className="wf-executable-version-column" /><col className="wf-executable-root-column" />
              <col className="wf-executable-date-column" /><col className="wf-executable-actions-column" />
            </colgroup>
            <thead><tr>
              <th scope="col">Workflow</th><th scope="col">Artifact</th><th scope="col">Version</th>
              <th scope="col">Root</th><th scope="col">Published</th><th scope="col">Actions</th>
            </tr></thead>
            <tbody>
              {visibleExecutables.map(executable => {
                const references = executable.references ?? [];
                const expanded = expandedArtifactIds.has(executable.artifactId);
                const retired = Boolean(executable.deletedAt);
                const sourceId = executable.sourceId || executable.definitionVersionId;
                const runTarget = createExecutableWorkflowRunTarget(executable);
                return (
                  <Fragment key={executable.artifactId}>
                    <tr>
                      <td><WorkflowExecutableOrigins executable={executable} definitionNames={definitionNames} onCopied={markCopied} onCopyFailed={markCopyFailed} /></td>
                      <td><span className="wf-artifact-cell">
                        <span className="wf-cell-line">
                          <strong title={executable.artifactId}>{executable.artifactId}</strong>
                          <CopyValueButton value={executable.artifactId} ariaLabel={`Copy artifact ID ${executable.artifactId}`} copiedLabel="artifact ID" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                          {retired ? <span className="wf-chip wf-reference-retired">Retired</span> : null}
                        </span>
                        <span className="wf-cell-line wf-cell-line-muted">
                          <small title={executable.artifactHash}>{executable.artifactHash}</small>
                          <CopyValueButton value={executable.artifactHash} ariaLabel={`Copy artifact hash ${executable.artifactHash}`} copiedLabel="artifact hash" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                        </span>
                        {sourceId ? <span className="wf-cell-line wf-cell-line-muted">
                          <small>Source ID</small><code title={sourceId}>{sourceId}</code>
                          <CopyValueButton value={sourceId} ariaLabel={`Copy source ID ${sourceId}`} copiedLabel="source ID" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                        </span> : null}
                        {executable.sourceKind || executable.sourceVersion ? <span className="wf-cell-line wf-cell-line-muted" title={`${formatExecutableSourceKind(executable.sourceKind)}${executable.sourceVersion ? ` · ${executable.sourceVersion}` : ""}`}>
                          <small>Source</small><small>{[executable.sourceKind ? formatExecutableSourceKind(executable.sourceKind) : null, executable.sourceVersion ? `Version ${executable.sourceVersion}` : null].filter(Boolean).join(" · ")}</small>
                        </span> : null}
                      </span></td>
                      <td><span className="wf-version-cell wf-executable-version-cell">
                        <span className="wf-cell-line">
                          <span>{executable.artifactVersion}</span>
                          <CopyValueButton value={executable.artifactVersion} ariaLabel={`Copy artifact version ${executable.artifactVersion}`} copiedLabel="artifact version" onCopied={markCopied} onCopyFailed={markCopyFailed} />
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
                      </span></td>
                      <td>{formatExecutableRoot(executable)}</td>
                      <td><time dateTime={executable.publishedAt ?? executable.createdAt}>{formatDate(executable.publishedAt ?? executable.createdAt)}</time></td>
                      <td><span className="wf-row-actions">
                        <button type="button" aria-label={`Inspect executable ${executable.artifactId}`} onClick={() => openExecutableInspector(executable.artifactId)}><ScanSearch size={13} /> Inspect</button>
                        <ExecutableRunButton
                          target={runTarget}
                          runningArtifactId={executableRun.runningArtifactId}
                          onRequest={executableRun.request}
                        />
                        {artifactExport.supported ? (
                          <ExecutableExportButton
                            executable={executable}
                            exportingArtifactId={artifactExport.exportingArtifactId}
                            ariaLabel={`Export executable artifact ${executable.artifactId}`}
                            onRequest={artifactExport.exportArtifact}
                          />
                        ) : null}
                        {explainExecutableAction ? (
                          <button type="button" onClick={() => explain(executable)}><Sparkles size={13} /> Explain</button>
                        ) : null}
                        {retired ? <span className="wf-muted">Retained for inspection</span> : null}
                      </span></td>
                    </tr>
                    {expanded ? (
                      <tr className="wf-executable-reference-row"><td colSpan={6}>
                        <ExecutableReferenceList references={references} definitionNames={definitionNames} ariaLabel={`References of ${executable.artifactId}`} />
                      </td></tr>
                    ) : null}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}

function WorkflowExecutableOrigins({ executable, definitionNames, onCopied, onCopyFailed }: { executable: WorkflowExecutableSummary; definitionNames: ReadonlyMap<string, string>; onCopied(label: string): void; onCopyFailed(label: string): void }) {
  const definitionIds = getExecutableDefinitionIds(executable);

  const openSourceDefinition = (definitionId: string) => {
    window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(definitionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return (
    <div className="wf-executable-workflow-cell">
      {definitionIds.map(definitionId => (
        <div className="wf-executable-origin" key={definitionId}>
          <button type="button" className="wf-link-button wf-executable-workflow-name" aria-label={`Open source definition ${definitionId}: ${definitionNames.get(definitionId) ?? "Workflow definition"}`} onClick={() => openSourceDefinition(definitionId)}>
            {definitionNames.get(definitionId) ?? "Workflow definition"}
          </button>
          <span className="wf-cell-line wf-cell-line-muted">
            <code title={definitionId}>{definitionId}</code>
            <CopyValueButton value={definitionId} ariaLabel={`Copy definition ID ${definitionId}`} copiedLabel="definition ID" onCopied={onCopied} onCopyFailed={onCopyFailed} />
          </span>
        </div>
      ))}
      {definitionIds.length === 0 ? <strong>Source unavailable</strong> : null}
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
