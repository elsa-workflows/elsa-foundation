import { useCallback, useEffect, useMemo, useState } from "react";
import { ReactFlow, Background, Controls, MiniMap, type Edge, type Node } from "@xyflow/react";
import { ChevronLeft, ChevronRight, Fingerprint, ListTree, Maximize2, Minimize2, Play, RotateCcw, Sparkles, Workflow as WorkflowIcon } from "lucide-react";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getDefinition } from "../api/workflowDesign";
import { listActivities } from "../api/activityDesign";
import { getExecutable } from "../api/runtime";
import type {
  ActivityCatalogItem,
  DesignMetadataRecord,
  WorkflowDefinitionSummary,
  WorkflowDraft,
  WorkflowExecutableDetails,
  WorkflowExecutableReference
} from "../workflowTypes";
import {
  buildCanvas,
  buildUnsupportedActivityCanvas,
  getActivityDesignerSupport,
  planSlotNavigation,
  resolveScope,
  slotCrumbLabel,
  type ChildSlot,
  type ScopeFrame,
  type WorkflowEdgeData,
  type WorkflowNodeData
} from "../workflowAdapter";
import { buildExecutableActivityGraph, ghostNodeLabel, isGhostFact, type ExecutableActivityGraph, type ExecutableGraphNodeFacts } from "../executableGraph";
import { formatDate } from "../workflowFormatting";
import { WfErrorCard } from "./StatusViews";
import { PanelTabList } from "./PanelTabList";
import { nodeTypes, edgeTypes } from "./graph";
import { ScopeBreadcrumb } from "./ScopeBreadcrumb";
import { CopyValueButton, ExecutableReferenceList, ExecutableRunStatusLine } from "./executableShared";
import type { ExecutableRunState, WorkflowEditorPanelTab } from "./editorTypes";
import {
  computeExecutableSourceDrift,
  createDraftSnapshotId,
  dispatchAiAction,
  findAiAction,
  formatExecutableSourceKind,
  formatReferenceScope
} from "./editorHelpers";
import { useSidePanelLayout } from "./useSidePanelLayout";
import { maxInspectorWidth, minInspectorWidth } from "./constants";
import { WorkflowRunInputDialog } from "./WorkflowRunInputDialog";
import { createExecutableWorkflowRunFeedback, useExecutableWorkflowRun } from "./useExecutableWorkflowRun";

// The Executable Inspector (studio ADR 0010, plan §3): the routed read-only surface for one
// content-addressed artifact. Structure comes from the Execution Material tree, geometry from the
// chosen Source Reference's Layout Sidecar (auto-layout per node when the sidecar has no record),
// and rendering fidelity from the live activity catalog — catalog misses are honest ghost nodes.
// The Inspector never mutates the executable; Run dispatches it, everything else reads.

interface ExecutableInspectionData {
  detail: WorkflowExecutableDetails;
  activityCatalog: ActivityCatalogItem[];
}

type SourceDefinitionState =
  | { status: "idle" | "loading" }
  | { status: "ready"; definition: WorkflowDefinitionSummary; draft: WorkflowDraft | null }
  | { status: "absent" }
  | { status: "failed"; error: string };

export function WorkflowExecutableInspectorWorkbench({ context, ai, artifactId, sourceReferenceId, onSelectReference }: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  artifactId: string;
  sourceReferenceId: string | null;
  onSelectReference(sourceReferenceId: string | null): void;
}) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [data, setData] = useState<ExecutableInspectionData | null>(null);
  const [sourceDefinition, setSourceDefinition] = useState<SourceDefinitionState>({ status: "idle" });
  const [runStatus, setRunStatus] = useState("");
  const [runError, setRunError] = useState("");
  const [lastRun, setLastRun] = useState<ExecutableRunState | null>(null);
  const [frames, setFrames] = useState<ScopeFrame[]>([]);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const {
    inspectorWidth,
    inspectorCollapsed,
    maximizedSidePanel,
    inspectorExpanded,
    editorBodyStyle,
    toggleSidePanelCollapsed,
    toggleSidePanelMaximized,
    startSidePanelResize,
    handleSidePanelResizeKeyDown
  } = useSidePanelLayout();
  const explainAction = findAiAction(ai, "weaver.workflows.explain-executable");

  const load = useCallback(async () => {
    if (!artifactId) {
      setError("No executable artifact id was provided.");
      setState("failed");
      return;
    }

    setState("loading");
    setError("");
    try {
      const [detail, activityCatalog] = await Promise.all([
        getExecutable(context, artifactId, sourceReferenceId),
        listActivities(context)
      ]);
      setData({ detail, activityCatalog: activityCatalog.activities });
      setFrames([]);
      setSelectedNodeId(null);
      setState("ready");
    } catch (e) {
      setData(null);
      setError(formatExecutableLoadError(e, artifactId));
      setState("failed");
    }
  }, [artifactId, context, sourceReferenceId]);

  useEffect(() => {
    void load();
  }, [load]);

  const chosenReference = useMemo(() => findChosenReference(data?.detail ?? null), [data]);
  const executableRun = useExecutableWorkflowRun({
    context,
    ...createExecutableWorkflowRunFeedback({ setStatus: setRunStatus, setLastRun, setError: setRunError })
  });

  // The source definition is looked up for the drift caption and the Open-source-definition gate.
  // A missing definition is a supported state (the artifact traveled without its source), not an error.
  useEffect(() => {
    const definitionId = chosenReference?.definitionId;
    if (!definitionId) {
      setSourceDefinition({ status: "absent" });
      return;
    }

    let cancelled = false;
    setSourceDefinition({ status: "loading" });
    getDefinition(context, definitionId).then(
      details => { if (!cancelled) setSourceDefinition({ status: "ready", definition: details.definition, draft: details.draft ?? null }); },
      error => {
        if (cancelled) return;
        setSourceDefinition(isNotFound(error)
          ? { status: "absent" }
          : { status: "failed", error: error instanceof Error ? error.message : String(error) });
      }
    );

    return () => { cancelled = true; };
  }, [chosenReference?.definitionId, context]);

  const graph = useMemo(
    () => data ? buildExecutableActivityGraph(data.detail.rootActivity, data.activityCatalog) : null,
    [data]
  );

  const explain = () => {
    if (!data || !explainAction) return;
    // The Weaver explain action consumes the executable summary shape; mirror it from the detail
    // plus the inspected reference so Explain works identically to the table.
    if (dispatchAiAction(ai, explainAction, {
      ...data.detail,
      artifactVersion: chosenReference?.artifactVersion ?? "",
      definitionId: chosenReference?.definitionId ?? "",
      definitionVersionId: chosenReference?.definitionVersionId ?? "",
      publishedAt: chosenReference?.publishedAt ?? null,
      sourceKind: chosenReference?.sourceKind ?? null,
      sourceId: chosenReference?.sourceId ?? null,
      sourceVersion: chosenReference?.sourceVersion ?? null
    })) {
      setRunError("");
      setLastRun(null);
      setRunStatus(`Sent ${data.detail.artifactId} to Weaver`);
    }
  };

  const goBack = () => {
    window.history.pushState({}, "", "/workflows/executables");
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const openSourceDefinition = () => {
    const definitionId = chosenReference?.definitionId;
    if (!definitionId) return;
    window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(definitionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const workbenchClassName = [
    "wf-instance-detail-workbench",
    inspectorCollapsed ? "inspector-collapsed" : "",
    maximizedSidePanel === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");

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
        <button type="button" onClick={goBack}><ChevronLeft size={14} /> Executables</button>
        <button type="button" onClick={() => void load()}><RotateCcw size={14} /> Refresh</button>
        {data ? (
          <button
            type="button"
            disabled={Boolean(executableRun.runningArtifactId)}
            onClick={() => void executableRun.request({
              artifactId: data.detail.artifactId,
              definitionVersionId: chosenReference?.definitionVersionId
            })}
          >
            <Play size={14} /> {executableRun.runningArtifactId ? "Running..." : "Run"}
          </button>
        ) : null}
        {data && explainAction ? (
          <button type="button" onClick={explain}><Sparkles size={13} /> Explain</button>
        ) : null}
        {data ? (
          <SourceDefinitionAction
            reference={chosenReference}
            sourceDefinition={sourceDefinition}
            onOpen={openSourceDefinition}
          />
        ) : null}
      </div>
      {runError ? <WfErrorCard message={runError} title="The executable could not be run" /> : null}
      {runStatus ? <ExecutableRunStatusLine status={runStatus} run={lastRun} /> : null}
      {state === "loading" ? <div className="wf-empty">Loading executable...</div> : null}
      {state === "failed" ? <WfErrorCard message={error} /> : null}
      {state === "ready" && data && graph ? (
        <div className={workbenchClassName} style={editorBodyStyle}>
          <WorkflowExecutableCanvas
            detail={data.detail}
            activityCatalog={data.activityCatalog}
            graph={graph}
            chosenReference={chosenReference}
            frames={frames}
            onNavigateToScope={setFrames}
            selectedNodeId={selectedNodeId}
            onSelectNode={setSelectedNodeId}
          />
          {inspectorExpanded && !maximizedSidePanel ? (
            <div
              className="wf-side-resize-handle right"
              role="separator"
              aria-label="Resize executable details panel"
              aria-orientation="vertical"
              aria-valuemin={minInspectorWidth}
              aria-valuemax={maxInspectorWidth}
              aria-valuenow={inspectorWidth}
              tabIndex={0}
              onPointerDown={event => startSidePanelResize("inspector", event)}
              onKeyDown={event => handleSidePanelResizeKeyDown("inspector", event)}
            />
          ) : <div className="wf-side-resize-spacer" />}
          <WorkflowExecutableSidePanel
            detail={data.detail}
            graph={graph}
            chosenReference={chosenReference}
            sourceDefinition={sourceDefinition}
            selectedNodeId={selectedNodeId}
            onSelectReference={onSelectReference}
            collapsed={inspectorCollapsed}
            expanded={inspectorExpanded}
            maximized={maximizedSidePanel === "inspector"}
            onToggleCollapsed={() => toggleSidePanelCollapsed("inspector")}
            onToggleMaximized={() => toggleSidePanelMaximized("inspector")}
          />
        </div>
      ) : null}
    </>
  );
}

function WorkflowExecutableCanvas({ detail, activityCatalog, graph, chosenReference, frames, onNavigateToScope, selectedNodeId, onSelectNode }: {
  detail: WorkflowExecutableDetails;
  activityCatalog: ActivityCatalogItem[];
  graph: ExecutableActivityGraph;
  chosenReference: WorkflowExecutableReference | null;
  frames: ScopeFrame[];
  onNavigateToScope(frames: ScopeFrame[]): void;
  selectedNodeId: string | null;
  onSelectNode(nodeId: string | null): void;
}) {
  const canvas = useMemo(
    () => buildExecutableInspectorCanvas(graph, activityCatalog, detail.chosenReference?.layout ?? [], frames, onNavigateToScope, selectedNodeId),
    [activityCatalog, detail, frames, graph, onNavigateToScope, selectedNodeId]
  );
  const scopeKey = frames.length === 0 ? "root" : frames.map(frame => `${frame.ownerNodeId}:${frame.slotId}`).join("/");

  return (
    <section className="wf-instance-canvas-shell" aria-label="Executable canvas">
      <header>
        <div>
          <span>Executable</span>
          <h3>{detail.artifactId} <small>{chosenReference ? `Version ${chosenReference.artifactVersion}` : detail.rootActivityType}</small></h3>
        </div>
        {detail.chosenReference ? (
          <span className="wf-executable-selection">{describeReferenceSelection(detail.chosenReference.selection, detail.chosenReference.sourceReferenceId)}</span>
        ) : (
          <span className="wf-executable-selection">No source reference; automatic layout</span>
        )}
      </header>
      <ScopeBreadcrumb className="wf-instance-breadcrumb" frames={frames} onNavigate={onNavigateToScope} />
      <div className="wf-instance-canvas">
        {canvas.nodes.length === 0 ? <div className="wf-empty">This executable has no renderable activities.</div> : (
          <ReactFlow
            key={scopeKey}
            nodes={canvas.nodes}
            edges={canvas.edges}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            fitView
            nodesDraggable={false}
            nodesConnectable={false}
            elementsSelectable
            onNodeClick={(_, node) => onSelectNode(node.id)}
            onPaneClick={() => onSelectNode(null)}
          >
            <Background />
            <MiniMap pannable zoomable />
            <Controls />
          </ReactFlow>
        )}
      </div>
    </section>
  );
}

// Builds the read-only Inspector canvas for the scope addressed by `frames`, mirroring the Runs-page
// pattern (buildInstanceCanvas): resolve the scope on the adapted tree, reuse buildCanvas with the
// chosen reference's Layout Sidecar for geometry (buildCanvas auto-places nodes the sidecar misses),
// then mark catalog-miss nodes as ghosts. Exported for tests.
export function buildExecutableInspectorCanvas(
  graph: ExecutableActivityGraph,
  activityCatalog: ActivityCatalogItem[],
  layout: DesignMetadataRecord[],
  frames: ScopeFrame[],
  onNavigateToScope: (frames: ScopeFrame[]) => void,
  selectedNodeId: string | null = null
): { nodes: Node<WorkflowNodeData>[]; edges: Edge<WorkflowEdgeData>[] } {
  const scope = resolveScope(graph.root, frames, activityCatalog);
  const scopeOwner = scope?.owner ?? graph.root;
  const scopeOwnerCatalogItem = activityCatalog.find(activity => activity.activityVersionId === scopeOwner.activityVersionId);
  const support = getActivityDesignerSupport(scopeOwner, scopeOwnerCatalogItem);
  const baseCanvas = support === "unsupported" || !scope
    ? buildUnsupportedActivityCanvas(scopeOwner, activityCatalog, layout)
    : buildCanvas(scope, activityCatalog, layout);

  return {
    nodes: baseCanvas.nodes.map(node => {
      const fact = graph.factsByNodeId.get(node.id);
      const ghost = isGhostFact(fact);
      const selected = node.id === selectedNodeId;
      return {
        ...node,
        draggable: false,
        connectable: false,
        deletable: false,
        selected,
        data: {
          ...node.data,
          ...(ghost && fact ? { ghost: true, label: ghostNodeLabel(fact.activityType) } : {}),
          onEnterSlot: (slot: ChildSlot) => {
            const plan = planSlotNavigation(frames, scopeOwner, node.id, slot, slotCrumbLabel(node.data.label, slot), activityCatalog);
            if (plan) onNavigateToScope(plan.frames);
          }
        }
      };
    }),
    edges: baseCanvas.edges.map(edge => ({ ...edge, deletable: false }))
  };
}

function WorkflowExecutableSidePanel({ detail, graph, chosenReference, sourceDefinition, selectedNodeId, onSelectReference, collapsed, expanded, maximized, onToggleCollapsed, onToggleMaximized }: {
  detail: WorkflowExecutableDetails;
  graph: ExecutableActivityGraph;
  chosenReference: WorkflowExecutableReference | null;
  sourceDefinition: SourceDefinitionState;
  selectedNodeId: string | null;
  onSelectReference(sourceReferenceId: string | null): void;
  collapsed: boolean;
  expanded: boolean;
  maximized: boolean;
  onToggleCollapsed(): void;
  onToggleMaximized(): void;
}) {
  const [activeTab, setActiveTab] = useState<"identity" | "references">("identity");
  const [copyStatus, setCopyStatus] = useState("");
  const tabs: WorkflowEditorPanelTab[] = [
    { id: "identity", title: "Identity", order: 0, icon: <Fingerprint size={14} />, render: () => null },
    { id: "references", title: `References (${detail.references.length})`, order: 1, icon: <ListTree size={14} />, render: () => null }
  ];
  const selectedFact = selectedNodeId ? graph.factsByNodeId.get(selectedNodeId) : null;
  const sourceDraft = sourceDefinition.status === "ready" ? sourceDefinition.draft : null;
  const sourceDefinitionId = sourceDefinition.status === "ready" ? sourceDefinition.definition.id : null;
  const currentDraftVersionId = useMemo(
    () => sourceDraft ? `draft:${createDraftSnapshotId(sourceDraft)}` : null,
    [sourceDraft]
  );
  // The routed Inspector does not retain the editor's transient test-run view, so it recovers the same
  // exact-current-draft signal from the artifact's TestRun reference. The synthetic version id embeds
  // both the persisted draft id and its state hash; a stale test run therefore cannot claim equivalence.
  const hasDraftEquivalence = useMemo(
    () => !!currentDraftVersionId && !!sourceDefinitionId && detail.references.some(reference =>
      reference.definitionVersionId === currentDraftVersionId
      && reference.definitionId === sourceDefinitionId
      && ["testrun", "test-run"].includes(reference.scope.trim().toLowerCase())),
    [currentDraftVersionId, detail.references, sourceDefinitionId]
  );

  return (
    <aside className="wf-instance-inspector" aria-label="Executable details panel">
      <div className="wf-panel-title wf-instance-panel-title">
        <PanelTabList label="Executable details tabs" tabs={tabs} activeTabId={activeTab} onSelect={tabId => setActiveTab(tabId as "identity" | "references")} />
        <span className="wf-panel-actions">
          <button
            type="button"
            className="wf-panel-action-button"
            aria-label={collapsed ? "Expand executable details panel" : "Collapse executable details panel"}
            title={collapsed ? "Expand" : "Collapse"}
            onClick={onToggleCollapsed}
          >
            {collapsed ? <ChevronLeft size={14} /> : <ChevronRight size={14} />}
          </button>
          {!collapsed ? (
            <button
              type="button"
              className="wf-panel-action-button"
              aria-label={maximized ? "Restore executable details panel" : "Maximize executable details panel"}
              title={maximized ? "Restore" : "Maximize"}
              onClick={onToggleMaximized}
            >
              {maximized ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
            </button>
          ) : null}
        </span>
      </div>
      {expanded ? (
        <>
          <header>
            <div>
              <span>Executable Artifact</span>
              <h3>{detail.artifactId}</h3>
            </div>
          </header>
          <div className="wf-instance-tab-content">
            {activeTab === "identity" ? (
              <>
                <section className="wf-instance-section">
                  <h4>Identity</h4>
                  <dl className="wf-instance-meta">
                    <dt>Artifact ID</dt>
                    <dd className="wf-cell-line">
                      <code title={detail.artifactId}>{detail.artifactId}</code>
                      <CopyValueButton value={detail.artifactId} ariaLabel={`Copy artifact ID ${detail.artifactId}`} copiedLabel="artifact ID" onCopied={label => setCopyStatus(`Copied ${label}`)} onCopyFailed={label => setCopyStatus(`Could not copy ${label}.`)} />
                    </dd>
                    <dt>Artifact Hash</dt>
                    <dd className="wf-cell-line">
                      <code title={detail.artifactHash}>{detail.artifactHash}</code>
                      <CopyValueButton value={detail.artifactHash} ariaLabel={`Copy artifact hash ${detail.artifactHash}`} copiedLabel="artifact hash" onCopied={label => setCopyStatus(`Copied ${label}`)} onCopyFailed={label => setCopyStatus(`Could not copy ${label}.`)} />
                    </dd>
                    <dt>Root activity</dt>
                    <dd>{detail.rootActivityType} <small>{detail.rootActivityVersion}</small></dd>
                    <dt>Nodes</dt>
                    <dd>{detail.nodeCount}</dd>
                    <dt>Resume targets</dt>
                    <dd>{detail.resumeTargetCount}</dd>
                    <dt>Created</dt>
                    <dd>{formatDate(detail.createdAt)}</dd>
                  </dl>
                  {copyStatus ? <p className="wf-copy-status" role="status">{copyStatus}</p> : null}
                </section>
                <section className="wf-instance-section">
                  <h4>Source</h4>
                  {chosenReference ? (
                    <dl className="wf-instance-meta">
                      <dt>Kind</dt>
                      <dd>{formatExecutableSourceKind(chosenReference.sourceKind)}</dd>
                      <dt>Definition</dt>
                      <dd>{chosenReference.definitionId} <small>{chosenReference.definitionVersionId}</small></dd>
                      <dt>Scope</dt>
                      <dd>{formatReferenceScope(chosenReference.scope)}</dd>
                    </dl>
                  ) : (
                    <p className="wf-muted">This artifact carries no source references.</p>
                  )}
                  <SourceDriftCaption
                    reference={chosenReference}
                    sourceDefinition={sourceDefinition}
                    hasDraftEquivalence={hasDraftEquivalence}
                  />
                </section>
                <WorkflowExecutableNodePanel fact={selectedFact ?? null} />
              </>
            ) : (
              <section className="wf-instance-section">
                <h4>Source references</h4>
                <ExecutableReferenceList
                  references={detail.references}
                  activeReferenceId={detail.chosenReference?.sourceReferenceId ?? null}
                  onSelect={reference => onSelectReference(reference.sourceReferenceId)}
                />
              </section>
            )}
          </div>
        </>
      ) : null}
    </aside>
  );
}

// Read-only view of the selected node's Execution Material facts: type identity and input-binding
// summaries (literals/expressions arrive pre-truncated; reference-typed values only name their
// type/id, never the secret).
function WorkflowExecutableNodePanel({ fact }: { fact: ExecutableGraphNodeFacts | null }) {
  if (!fact) return null;

  return (
    <section className="wf-instance-section">
      <h4>Selected node</h4>
      <dl className="wf-instance-meta">
        <dt>Type</dt>
        <dd>{fact.activityType} <small>{fact.activityTypeVersion}</small></dd>
        <dt>Executable node</dt>
        <dd>{fact.executableNodeId}</dd>
        <dt>Authored activity</dt>
        <dd>{fact.authoredActivityId}</dd>
      </dl>
      {!fact.available ? <p className="wf-instance-note">This activity is not available in this environment; the executable cannot run here.</p> : null}
      {fact.inputBindings.length > 0 ? (
        <dl className="wf-instance-meta wf-executable-bindings">
          {fact.inputBindings.map(binding => (
            <div key={binding.inputName}>
              <dt>{binding.inputName}</dt>
              <dd><span className="wf-chip">{binding.source}</span> {binding.summary ?? ""}</dd>
            </div>
          ))}
        </dl>
      ) : (
        <p className="wf-muted">No input bindings.</p>
      )}
    </section>
  );
}

function SourceDefinitionAction({ reference, sourceDefinition, onOpen }: {
  reference: WorkflowExecutableReference | null;
  sourceDefinition: SourceDefinitionState;
  onOpen(): void;
}) {
  const disabledReason = getSourceDefinitionDisabledReason(reference, sourceDefinition);

  return (
    <button
      type="button"
      disabled={!!disabledReason}
      title={disabledReason ?? undefined}
      aria-label={disabledReason ? `Open source definition unavailable: ${disabledReason}` : "Open source definition"}
      onClick={onOpen}
    >
      <WorkflowIcon size={14} /> Open source definition
    </button>
  );
}

function getSourceDefinitionDisabledReason(reference: WorkflowExecutableReference | null, sourceDefinition: SourceDefinitionState) {
  if (!reference) return "This artifact carries no source references.";
  if (sourceDefinition.status === "absent") return "This workflow definition is not available in this environment.";
  if (sourceDefinition.status === "failed") return `The source definition could not be loaded: ${sourceDefinition.error}`;
  if (sourceDefinition.status === "loading" || sourceDefinition.status === "idle") return "Checking the source definition...";
  return null;
}

// The drift caption under the Source panel (plan §3): a version comparison between the inspected
// reference and the definition's latest. studio#262's behavioral-equivalence signal (draft test run
// resolving to this artifact id) upgrades this caption; keep the rendering here so that lands in one
// place.
function SourceDriftCaption({ reference, sourceDefinition, hasDraftEquivalence }: {
  reference: WorkflowExecutableReference | null;
  sourceDefinition: SourceDefinitionState;
  hasDraftEquivalence: boolean;
}) {
  if (!reference) return null;
  if (sourceDefinition.status === "absent") {
    return <p className="wf-instance-note">This workflow definition is not available in this environment. The artifact renders from its own Execution Material and carried layout.</p>;
  }
  if (sourceDefinition.status !== "ready") return null;

  const drift = computeExecutableSourceDrift(reference, sourceDefinition.definition, hasDraftEquivalence);
  if (drift.kind === "equivalent") {
    return (
      <p className="wf-instance-note wf-executable-drift wf-executable-equivalent" role="note">
        Current draft is behaviorally identical to this artifact.
      </p>
    );
  }
  if (drift.kind !== "behind") return null;

  return (
    <p className="wf-instance-note wf-executable-drift" role="note">
      This reference was published from version {drift.referenceVersion ?? reference.definitionVersionId}; the definition&apos;s latest is {drift.latestVersion ?? sourceDefinition.definition.latestVersionId}.
    </p>
  );
}

function findChosenReference(detail: WorkflowExecutableDetails | null): WorkflowExecutableReference | null {
  if (!detail) return null;
  const chosenId = detail.chosenReference?.sourceReferenceId;
  return detail.references.find(reference => reference.sourceReferenceId === chosenId)
    ?? detail.references[0]
    ?? null;
}

function describeReferenceSelection(selection: string, sourceReferenceId: string) {
  const normalized = selection.trim().toLowerCase();
  if (normalized === "requested") return `Showing requested reference ${sourceReferenceId}`;
  if (normalized === "newest-live") return `Showing newest live reference ${sourceReferenceId}`;
  if (normalized === "newest") return `Showing newest reference ${sourceReferenceId} (no live references remain)`;
  return `Showing reference ${sourceReferenceId}`;
}

function formatExecutableLoadError(error: unknown, artifactId: string) {
  if (isNotFound(error)) return `Executable artifact ${artifactId} was not found.`;
  return error instanceof Error ? error.message : String(error);
}

function isNotFound(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  return /\b404\b/.test(message) || /not found/i.test(message);
}
