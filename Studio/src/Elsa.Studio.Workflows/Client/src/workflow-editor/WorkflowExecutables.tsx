import { useCallback, useEffect, useMemo, useState } from "react";
import { AlertCircle, ChevronDown, ChevronRight, Play, RotateCcw, ScanSearch, Search, Sparkles, X } from "lucide-react";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listExecutables } from "../api/runtime";
import { listPublicationSlots, restorePublicationSlot, unpublishSlot, type PublicationSlot } from "../api/publishing";
import type { WorkflowExecutableListScope, WorkflowExecutableSummary } from "../workflowTypes";
import { formatDate } from "../workflowFormatting";
import { WfEmptyState, WfErrorCard, WfListSkeleton } from "./StatusViews";
import { ExecutableReferenceList, ExecutableRunStatusLine, CopyValueButton } from "./executableShared";
import { getDialogs } from "./dialogs";
import type { ExecutableRunState } from "./editorTypes";
import { WorkflowRunInputDialog } from "./WorkflowRunInputDialog";
import { useExecutableWorkflowRun } from "./useExecutableWorkflowRun";
import {
  compareExecutablesByPublishedDate,
  dispatchAiAction,
  executableMatchesDefinitionFilter,
  findAiAction,
  formatExecutableRoot,
  formatExecutableRunError,
  formatExecutableSourceKind,
  readExecutableRunWorkflowExecutionId
} from "./editorHelpers";

export function openExecutableInspector(artifactId: string, sourceReferenceId?: string | null) {
  const query = sourceReferenceId ? `?ref=${encodeURIComponent(sourceReferenceId)}` : "";
  window.history.pushState({}, "", `/workflows/executables/${encodeURIComponent(artifactId)}${query}`);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

export function WorkflowExecutables({ context, ai, definitionFilter, onDefinitionFilterChange }: { context: StudioEndpointContext; ai: StudioAiContributionApi; definitionFilter: string | null; onDefinitionFilterChange(filter: string | null): void }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [lastRun, setLastRun] = useState<ExecutableRunState | null>(null);
  const [executables, setExecutables] = useState<WorkflowExecutableSummary[]>([]);
  const [scope, setScope] = useState<WorkflowExecutableListScope>("published");
  const [includeRetired, setIncludeRetired] = useState(false);
  const [expandedArtifactIds, setExpandedArtifactIds] = useState<ReadonlySet<string>>(new Set());
  const normalizedDefinitionFilter = definitionFilter?.trim().toLowerCase() ?? "";
  const visibleExecutables = useMemo(
    () => normalizedDefinitionFilter
      ? executables.filter(executable => executableMatchesDefinitionFilter(executable, normalizedDefinitionFilter))
      : executables,
    [normalizedDefinitionFilter, executables]
  );
  const definitionOptions = useMemo(
    () => Array.from(new Set(executables.flatMap(executable => [
      executable.definitionId,
      executable.definitionVersionId,
      executable.sourceId
    ]).filter((value): value is string => Boolean(value)))).sort((left, right) => left.localeCompare(right)),
    [executables]
  );
  const explainExecutableAction = findAiAction(ai, "weaver.workflows.explain-executable");
  const executableRun = useExecutableWorkflowRun({
    context,
    onDispatchStart: () => {
      setStatus("");
      setLastRun(null);
      setError("");
    },
    onStarted: (target, result) => {
      setLastRun({ artifactId: target.artifactId, workflowExecutionId: readExecutableRunWorkflowExecutionId(result) });
      setStatus(`Started ${target.artifactId}`);
    },
    onError: error => setError(formatExecutableRunError(error))
  });

  const load = useCallback(async () => {
    setState("loading");
    setError("");
    try {
      setExecutables(await listExecutables(context, { scope, includeRetired }));
      setState("ready");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setState("failed");
    }
  }, [context, includeRetired, scope]);

  useEffect(() => {
    void load();
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
          onSubmit={values => { void executableRun.confirm(values); }}
          onCancel={executableRun.cancel}
        />
      ) : null}
      <div className="wf-toolbar">
        <button type="button" onClick={() => void load()}>Refresh</button>
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
        <label className="wf-search wf-executable-definition-filter">
          <Search size={14} />
          <input
            aria-label="Filter executables by workflow definition"
            list="wf-executable-definition-options"
            placeholder="Filter by definition ID"
            value={definitionFilter ?? ""}
            onChange={event => onDefinitionFilterChange(event.currentTarget.value || null)}
          />
        </label>
        <datalist id="wf-executable-definition-options">
          {definitionOptions.map(option => <option key={option} value={option} />)}
        </datalist>
        {definitionFilter ? (
          <button type="button" onClick={() => onDefinitionFilterChange(null)}><X size={13} /> Clear</button>
        ) : null}
      </div>
      {state === "failed" ? <WfErrorCard message={error} /> : null}
      {state === "ready" && error ? <WfErrorCard message={error} /> : null}
      {status ? <ExecutableRunStatusLine status={status} run={lastRun} /> : null}
      {state === "loading" ? <WfListSkeleton /> : null}
      {state === "ready" && visibleExecutables.length === 0 ? (
        <WfEmptyState
          icon={<Play size={22} />}
          title="No workflow executables"
          description={definitionFilter ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."}
        />
      ) : null}
      {state === "ready" && visibleExecutables.length > 0 ? (
        <div className="wf-grid wf-executable-grid" role="table" aria-label="Workflow executables">
          <div className="wf-grid-head" role="row">
            <span>Artifact</span>
            <span>Version</span>
            <span>Source</span>
            <span>Root</span>
            <span>Published</span>
            <span>Actions</span>
          </div>
          {visibleExecutables.map(executable => {
            const references = executable.references ?? [];
            const expanded = expandedArtifactIds.has(executable.artifactId);
            const retired = Boolean(executable.deletedAt);
            return (
              <div className="wf-executable-row-group" key={executable.artifactId}>
                <div className="wf-grid-row" role="row">
                  <span className="wf-artifact-cell">
                    <span className="wf-cell-line">
                      <strong title={executable.artifactId}>{executable.artifactId}</strong>
                      <CopyValueButton value={executable.artifactId} ariaLabel={`Copy artifact ID ${executable.artifactId}`} copiedLabel="artifact ID" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                      {retired ? <span className="wf-chip wf-reference-retired">Retired</span> : null}
                    </span>
                    <span className="wf-cell-line wf-cell-line-muted">
                      <small title={executable.artifactHash}>{executable.artifactHash}</small>
                      <CopyValueButton value={executable.artifactHash} ariaLabel={`Copy artifact hash ${executable.artifactHash}`} copiedLabel="artifact hash" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                    </span>
                  </span>
                  <span className="wf-version-cell wf-executable-version-cell">
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
                  </span>
                  <WorkflowExecutableSourceCell executable={executable} onCopied={markCopied} onCopyFailed={markCopyFailed} />
                  <span>{formatExecutableRoot(executable)}</span>
                  <span>{formatDate(executable.publishedAt ?? executable.createdAt)}</span>
                  <span className="wf-row-actions">
                    <button type="button" aria-label={`Inspect executable ${executable.artifactId}`} onClick={() => openExecutableInspector(executable.artifactId)}><ScanSearch size={13} /> Inspect</button>
                    <button
                      type="button"
                      disabled={Boolean(executableRun.runningArtifactId)}
                      onClick={() => void executableRun.request(executable)}
                    >
                      <Play size={13} /> {executableRun.runningArtifactId === executable.artifactId ? "Running..." : "Run"}
                    </button>
                    {explainExecutableAction ? (
                      <button type="button" onClick={() => explain(executable)}><Sparkles size={13} /> Explain</button>
                    ) : null}
                    {retired ? <span className="wf-muted">Retained for inspection</span> : null}
                  </span>
                </div>
                {expanded ? (
                  <div className="wf-executable-references">
                    <ExecutableReferenceList references={references} ariaLabel={`References of ${executable.artifactId}`} />
                  </div>
                ) : null}
              </div>
            );
          })}
        </div>
      ) : null}
    </>
  );
}

function WorkflowExecutableSourceCell({ executable, onCopied, onCopyFailed }: { executable: WorkflowExecutableSummary; onCopied(label: string): void; onCopyFailed(label: string): void }) {
  const sourceId = executable.sourceId || executable.definitionVersionId || executable.definitionId;
  const sourceVersion = executable.sourceVersion;
  const definitionId = executable.definitionId;

  const openSourceDefinition = () => {
    window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(definitionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return (
    <span className="wf-source-cell">
      <span className="wf-source-kind">{formatExecutableSourceKind(executable.sourceKind)}</span>
      {sourceId ? (
        <span className="wf-cell-line">
          {definitionId ? (
            <button type="button" className="wf-link-button" aria-label={`Open source definition ${definitionId}`} onClick={openSourceDefinition}>
              <code title={sourceId}>{sourceId}</code>
            </button>
          ) : (
            <code title={sourceId}>{sourceId}</code>
          )}
          <CopyValueButton value={sourceId} ariaLabel={`Copy source ID ${sourceId}`} copiedLabel="source ID" onCopied={onCopied} onCopyFailed={onCopyFailed} />
        </span>
      ) : null}
      {sourceVersion ? <small>Version {sourceVersion}</small> : null}
    </span>
  );
}

export function WorkflowArtifactsPanel({ context, ai, definitionId, publishedArtifactId }: { context: StudioEndpointContext; ai: StudioAiContributionApi; definitionId: string; publishedArtifactId: string | null }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [lastRun, setLastRun] = useState<ExecutableRunState | null>(null);
  const [artifacts, setArtifacts] = useState<WorkflowExecutableSummary[]>([]);
  const [slots, setSlots] = useState<PublicationSlot[]>([]);
  const explainExecutableAction = findAiAction(ai, "weaver.workflows.explain-executable");
  const executableRun = useExecutableWorkflowRun({
    context,
    onDispatchStart: () => {
      setStatus("");
      setLastRun(null);
      setError("");
    },
    onStarted: (target, result) => {
      setLastRun({ artifactId: target.artifactId, workflowExecutionId: readExecutableRunWorkflowExecutionId(result) });
      setStatus(`Started ${target.artifactId}`);
    },
    onError: error => setError(formatExecutableRunError(error))
  });

  const load = useCallback(async () => {
    setState("loading");
    setError("");
    try {
      const [executables, publicationSlots] = await Promise.all([
        listExecutables(context, { scope: "all", includeRetired: true }),
        listPublicationSlots(context, definitionId)
      ]);
      const publicationArtifactIds = new Set(publicationSlots.flatMap(slot => slot.publication?.artifactId ? [slot.publication.artifactId] : []));
      setArtifacts(executables.filter(executable => publicationArtifactIds.has(executable.artifactId)).sort(compareExecutablesByPublishedDate));
      setSlots(publicationSlots);
      setState("ready");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setArtifacts([]);
      setSlots([]);
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
                <span className="wf-chip">{slot.status ?? "empty"}</span>
              </div>
              {slot.publication ? <p><code>{slot.publication.artifactId}</code></p> : <p className="wf-muted">No active publication</p>}
              <div className="wf-row-actions">
                {slot.status === "retired" ? (
                  <button type="button" aria-label={`Restore publication slot ${slot.slotName}`} onClick={() => void restore(slot)}><RotateCcw size={13} /> Restore</button>
                ) : slot.publication ? (
                  <button type="button" aria-label={`Unpublish publication slot ${slot.slotName}`} onClick={() => void unpublish(slot)}>Unpublish</button>
                ) : null}
              </div>
            </article>
          ))}
        </div>
      ) : null}
      {state === "ready" && artifacts.length === 0 ? <p className="wf-muted">No published artifacts for this workflow yet.</p> : null}
      {state === "ready" && artifacts.length > 0 ? (
        <div className="wf-artifact-list" role="list" aria-label="Workflow artifacts">
          {artifacts.map(artifact => (
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
                <button
                  type="button"
                  disabled={Boolean(executableRun.runningArtifactId)}
                  onClick={() => void executableRun.request(artifact)}
                >
                  <Play size={13} /> {executableRun.runningArtifactId === artifact.artifactId ? "Running..." : "Run"}
                </button>
                {explainExecutableAction ? <button type="button" onClick={() => explain(artifact)}><Sparkles size={13} /> Explain</button> : null}
              </div>
            </article>
          ))}
        </div>
      ) : null}
    </div>
  );
}
