import { useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ReactFlow, Background, type Edge, type Node } from "@xyflow/react";
import { AlertCircle, Fingerprint, ListTree, RotateCcw, Sparkles, Workflow as WorkflowIcon } from "lucide-react";
import type { StudioWeaverRegistry, StudioEndpointContext, StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import { StudioResultState, CopyableIdentifier, StudioShellBreadcrumbs, StudioButton, StudioTimestamp } from "@elsa-workflows/studio-ui";
import { getDefinition } from "../api/workflowDesign";
import { listActivities } from "../api/activityDesign";
import { getExecutable, getExecutableInputSources } from "../api/runtime";
import type {
  ActivityCatalogItem,
  DesignMetadataRecord,
  WorkflowDefinitionSummary,
  WorkflowDraft,
  WorkflowExecutableDetails,
  WorkflowExecutableInputSources,
  WorkflowExecutableReference
} from "../workflowTypes";
import { SequenceDesigner } from "../graph-authoring/SequenceDesigner";
import { CompensationScopeDesigner } from "../graph-authoring/CompensationScopeDesigner";
import { compensationStepsSlotId } from "../compensationScope";
import { buildBpmnCanvas, type BpmnNodeData } from "../bpmn/bpmnAdapter";
import { formatActivitySummary } from "../activitySummary";
import { resolveRuntimeActivityLabel as resolveActivityLabel } from "../runtimeActivityDisplay";
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
import { ConversionPlanCaption } from "../ConversionPlanCaption";
import { nodeTypes, edgeTypes } from "./graph";
import { ScopeBreadcrumb } from "./ScopeBreadcrumb";
import { ExecutableReferenceList, ExecutableRunStatusLine } from "./executableShared";
import type { ExecutableRunState } from "./editorTypes";
import {
  computeExecutableSourceDrift,
  createDraftSnapshotId,
  dispatchAiAction,
  findAiAction,
  formatExecutableSourceKind,
  formatReferenceScope
} from "./editorHelpers";
import { useSidePanelLayout } from "./useSidePanelLayout";
import { WorkflowRunInputDialog } from "./WorkflowRunInputDialog";
import { notifySuccess } from "./notifications";
import {
  createExecutableWorkflowRunFeedback,
  findPublishedRunReference,
  useExecutableWorkflowRun
} from "./useExecutableWorkflowRun";
import { ExecutableRunButton } from "./ExecutableRunButton";
import { activateWorkflowCanvasNode, decorateWorkflowCanvasElements } from "./workflowAccessibility";
import { getReadOnlyFitViewOptions } from "./readOnlyMinimap";
import { ReadOnlyFlowControls } from "./ReadOnlyFlowControls";
import { FitReadOnlyFlowView, useReadOnlyFlowNodes } from "./useReadOnlyFlowCanvas";
import { WorkflowReadOnlyMiniMap } from "./WorkflowReadOnlyMiniMap";
import { RetainFlowViewport } from "../graph-authoring/RetainFlowViewport";
import { GraphAuthoringWorkbench } from "../graph-authoring/GraphAuthoringWorkbench";
import { StudioLink } from "../StudioLink";
import { WorkflowEditorToolbarFrame } from "./WorkflowEditorToolbar";
import "./executableInspector.css";
import type { WorkflowWeaverController } from "../workflowWeaver";
import { useWorkflowInspectionContext } from "./useWorkflowInspectionContext";
import {
  describeExecutableReferenceSelection,
  formatExecutableInspectorPageTitle
} from "./executableInspectorIdentity";

// The Executable Inspector (studio ADR 0010, plan §3): the routed read-only surface for one
// content-addressed artifact. Structure comes from the Execution Material tree, geometry from the
// chosen Source Reference's Layout Sidecar (auto-layout per node when the sidecar has no record),
// and rendering fidelity from the live activity catalog — catalog misses are honest ghost nodes.
// The Inspector never mutates the executable; Run dispatches it, everything else reads.

interface ExecutableInspectionData {
  context: StudioEndpointContext;
  detail: WorkflowExecutableDetails;
  activityCatalog: ActivityCatalogItem[];
  inputSources: WorkflowExecutableInputSources | null;
  requestedSourceReferenceId: string | null;
}

type SourceDefinitionState =
  | { status: "idle" | "loading"; definitionId: string | null }
  | { status: "ready"; definitionId: string; definition: WorkflowDefinitionSummary; draft: WorkflowDraft | null }
  | { status: "absent"; definitionId: string | null }
  | { status: "failed"; definitionId: string; error: string };

export function WorkflowExecutableInspectorWorkbench({ context, ai, weaver, runInputEditors, artifactId, sourceReferenceId, onSelectReference }: {
  context: StudioEndpointContext;
  ai: StudioWeaverRegistry;
  /** The Workflows module's Weaver state: the inspector contributes the exact Executable it shows (spec 1193 FR-028). */
  weaver?: WorkflowWeaverController;
  runInputEditors: StudioWorkflowRunInputEditorContribution[];
  artifactId: string;
  sourceReferenceId: string | null;
  onSelectReference(sourceReferenceId: string | null): void;
}) {

  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [data, setData] = useState<ExecutableInspectionData | null>(null);
  const [sourceDefinition, setSourceDefinition] = useState<SourceDefinitionState>({ status: "idle", definitionId: null });
  const [runStatus, setRunStatus] = useState("");
  const [runError, setRunError] = useState("");
  const [lastRun, setLastRun] = useState<ExecutableRunState | null>(null);
  const [frames, setFrames] = useState<ScopeFrame[]>([]);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [activeInspectorTab, setActiveInspectorTab] = useState<"activity" | "identity" | "references">("identity");
  const sidePanelLayout = useSidePanelLayout(undefined, { palettePresent: false });
  const explainAction = findAiAction(ai, "weaver.workflows.explain-executable");

  const loadId = useRef(0);
  const load = useCallback(async () => {
    const requestId = ++loadId.current;
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
      // The summary tree redacts binding source details (literals, pinned conversion plans); the
      // compiled bindings live behind the input-sources endpoint. Best effort — inspection still
      // renders from the summary tree when the endpoint or permission is unavailable.
      if (requestId !== loadId.current) return;
      const chosenReferenceId = detail.chosenReference?.sourceReferenceId ?? null;
      const inputSources = chosenReferenceId
        ? await getExecutableInputSources(context, detail.artifactId, chosenReferenceId).catch(() => null)
        : null;
      if (requestId !== loadId.current) return;
      setData({ context, detail, activityCatalog: activityCatalog.activities, inputSources, requestedSourceReferenceId: sourceReferenceId });
      setFrames([]);
      setSelectedNodeId(null);
      setActiveInspectorTab("identity");
      setState("ready");
    } catch (e) {
      if (requestId !== loadId.current) return;
      setData(null);
      setError(formatExecutableLoadError(e, artifactId));
      setState("failed");
    }
  }, [artifactId, context, sourceReferenceId]);

  useEffect(() => {
    void load();
    return () => { loadId.current++; };
  }, [load]);

  const currentData = state === "ready" && data?.context === context && data.detail.artifactId === artifactId
    && data.requestedSourceReferenceId === sourceReferenceId ? data : null;
  const chosenReference = useMemo(() => findChosenReference(currentData?.detail ?? null), [currentData]);
  const requestedDefinitionId = chosenReference?.definitionId?.trim() || null;
  const currentSourceDefinition: SourceDefinitionState = sourceDefinition.definitionId === requestedDefinitionId
    ? sourceDefinition
    : requestedDefinitionId
      ? { status: "loading", definitionId: requestedDefinitionId }
      : { status: "absent", definitionId: null };
  const currentDefinitionName = currentData && chosenReference && currentSourceDefinition.status === "ready"
    ? currentSourceDefinition.definition.name
    : null;
  const pageTitle = formatExecutableInspectorPageTitle(
    currentDefinitionName,
    artifactId,
    chosenReference?.artifactVersion
  );
  // The exact Executable Weaver may read while it is shown: identity bound to its Artifact Hash, withdrawn while loading,
  // after a failed load and on unmount. The server reads the content itself and refuses a hash that no longer matches.
  const inspected = currentData
    ? {
        artifactId: currentData.detail.artifactId,
        artifactHash: currentData.detail.artifactHash,
        sourceReferenceId: currentData.detail.chosenReference?.sourceReferenceId ?? chosenReference?.sourceReferenceId ?? null
      }
    : null;
  useWorkflowInspectionContext({ controller: weaver, clientId: ai.clientId, kind: "executable", identity: inspected });
  const publishedRunReference = findPublishedRunReference(data?.detail.references);
  const publishedRunTarget = data && publishedRunReference
    ? {
        artifactId: data.detail.artifactId,
        definitionVersionId: publishedRunReference.definitionVersionId,
        sourceReferenceId: publishedRunReference.sourceReferenceId
      }
    : null;
  const executableRun = useExecutableWorkflowRun({
    context,
    ...createExecutableWorkflowRunFeedback({ setStatus: setRunStatus, setLastRun, setError: setRunError })
  });

  // The source definition is looked up for the drift caption and the Open-source-definition gate.
  // A missing definition is a supported state (the artifact traveled without its source), not an error.
  useEffect(() => {
    const definitionId = chosenReference?.definitionId?.trim();
    if (!definitionId) {
      setSourceDefinition({ status: "absent", definitionId: null });
      return;
    }

    let cancelled = false;
    setSourceDefinition({ status: "loading", definitionId });
    getDefinition(context, definitionId).then(
      details => { if (!cancelled) setSourceDefinition({ status: "ready", definitionId, definition: details.definition, draft: details.draft ?? null }); },
      error => {
        if (cancelled) return;
        setSourceDefinition(isNotFound(error)
          ? { status: "absent", definitionId }
          : { status: "failed", definitionId, error: error instanceof Error ? error.message : String(error) });
      }
    );

    return () => { cancelled = true; };
  }, [chosenReference?.definitionId, context]);

  const graph = useMemo(
    () => data ? buildExecutableActivityGraph(
      data.detail.rootActivity,
      data.activityCatalog,
      data.inputSources?.authoredInputs ?? [],
      data.inputSources?.compiledInputs ?? [],
      data.inputSources?.accessState ?? data.inputSources?.access ?? null,
      data.detail.chosenReference?.activityPresentation ?? []
    ) : null,
    [data]
  );

  const explain = () => {
    if (!data || !explainAction) return;
    // The inspector contributes the exact Executable itself (workflows.executable-inspector), so the prompt attaches none;
    // without the Workflows Weaver state the prompt attaches the same identity.
    if (dispatchAiAction(ai, explainAction, {
      artifactId: data.detail.artifactId,
      artifactHash: data.detail.artifactHash,
      sourceReferenceId: data.detail.chosenReference?.sourceReferenceId ?? null,
      attached: Boolean(weaver?.inspection)
    })) {
      notifySuccess(`Sent ${data.detail.artifactId} to Weaver`);
    }
  };

  const openSourceDefinition = () => {
    const definitionId = chosenReference?.definitionId;
    if (!definitionId) return;
    window.history.pushState({}, "", qualifyStudioBackendPath(`/workflows/definitions?definition=${encodeURIComponent(definitionId)}`));
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const selectNode = (nodeId: string | null) => {
    setSelectedNodeId(nodeId);
    if (nodeId) {
      setActiveInspectorTab("activity");
      if (sidePanelLayout.inspectorCollapsed) sidePanelLayout.toggleSidePanelCollapsed("inspector");
    }
  };
  const inspectorPanelProps = currentData && graph ? {
    detail: currentData.detail,
    activityCatalog: currentData.activityCatalog,
    graph,
    chosenReference,
    sourceDefinition: currentSourceDefinition,
    selectedNodeId,
    onSelectReference
  } : null;

  return (
    <>
      {executableRun.pending ? (
        <WorkflowRunInputDialog
          inputs={executableRun.pending.inputs}
          returnFocusTo={executableRun.pending.opener}
          editors={runInputEditors}
          onSubmit={values => { void executableRun.confirm(values); }}
          onCancel={executableRun.cancel}
        />
      ) : null}
      <WorkflowEditorToolbarFrame
        className="wf-executable-toolbar"
        identity={<><StudioShellBreadcrumbs items={[{ href: "/workflows/executables", label: "Executables" }]} current={pageTitle}
          renderLink={item => <StudioLink href={item.href}>{item.label}</StudioLink>} />
          <h2 className="wf-editor-title"><strong title={pageTitle}>{pageTitle}</strong></h2></>}
        metadata={chosenReference ? <span className="wf-executable-header-meta">{formatExecutableSourceKind(chosenReference.sourceKind)} · {formatReferenceScope(chosenReference.scope)}</span> : null}
        actions={<>
          <StudioButton type="button" size="sm" aria-label="Refresh executable" title="Refresh executable" onClick={() => void load()}><RotateCcw size={14} /> Refresh</StudioButton>
          {currentData ? (
            <ExecutableRunButton
              target={publishedRunTarget}
              runningArtifactId={executableRun.runningArtifactId}
              iconSize={14}
              variant="primary"
              onRequest={executableRun.request}
            />
          ) : null}
          {currentData && explainAction ? (
            <StudioButton type="button" onClick={explain}><Sparkles size={13} /> Explain</StudioButton>
          ) : null}
          {currentData ? (
            <SourceDefinitionAction
              reference={chosenReference}
              sourceDefinition={currentSourceDefinition}
              onOpen={openSourceDefinition}
            />
          ) : null}
        </>}
      />
      {runError ? <StudioResultState tone="danger" icon={<AlertCircle size={18} aria-hidden />} description={runError || "Please try again, or check that the Elsa server is reachable."} title="The executable could not be run" /> : null}
      {runStatus ? <ExecutableRunStatusLine status={runStatus} run={lastRun} /> : null}
      {state === "loading" ? <div className="wf-empty">Loading executable...</div> : null}
      {state === "failed" ? <StudioResultState tone="danger" icon={<AlertCircle size={18} aria-hidden />} title="Something went wrong" description={error || "Please try again, or check that the Elsa server is reachable."} /> : null}
      {currentData && graph ? (
        <GraphAuthoringWorkbench
          layout={sidePanelLayout}
          className="wf-executable-workbench"
          canvas={<WorkflowExecutableCanvas
            detail={currentData.detail}
            activityCatalog={currentData.activityCatalog}
            graph={graph}
            frames={frames}
            onNavigateToScope={setFrames}
            selectedNodeId={selectedNodeId}
            onSelectNode={selectNode}
          />}
          inspector={{
            ariaLabel: "Executable details panel",
            tabLabel: "Executable details tabs",
            className: "wf-instance-inspector",
            tabs: [
              { id: "identity", title: "Identity", order: 0, icon: <Fingerprint size={14} />, render: () => <WorkflowExecutablePanelContent activeTab="identity" {...inspectorPanelProps!} /> },
              { id: "activity", title: "Activity", order: 1, icon: <WorkflowIcon size={14} />, render: () => <WorkflowExecutablePanelContent activeTab="activity" {...inspectorPanelProps!} /> },
              { id: "references", title: `References (${currentData.detail.references.length})`, order: 2, icon: <ListTree size={14} />, render: () => <WorkflowExecutablePanelContent activeTab="references" {...inspectorPanelProps!} /> }
            ],
            activeTabId: activeInspectorTab,
            onSelect: tabId => setActiveInspectorTab(tabId as "activity" | "identity" | "references")
          }}
        />
      ) : null}
    </>
  );
}

export function WorkflowExecutableCanvas({ detail, activityCatalog, graph, frames, onNavigateToScope, selectedNodeId, onSelectNode }: {
  detail: WorkflowExecutableDetails;
  activityCatalog: ActivityCatalogItem[];
  graph: ExecutableActivityGraph;
  frames: ScopeFrame[];
  onNavigateToScope(frames: ScopeFrame[]): void;
  selectedNodeId: string | null;
  onSelectNode(nodeId: string | null): void;
}) {

  const canvasRef = useRef<HTMLDivElement>(null);
  const canvas = useMemo(
    () => buildExecutableInspectorCanvas(graph, activityCatalog, detail.chosenReference?.layout ?? [], frames, onNavigateToScope, selectedNodeId),
    [activityCatalog, detail, frames, graph, onNavigateToScope, selectedNodeId]
  );
  const { flowNodes, onNodesChange } = useReadOnlyFlowNodes(canvas.nodes);
  const scopeKey = frames.length === 0 ? "root" : frames.map(frame => `${frame.ownerNodeId}:${frame.slotId}`).join("/");
  return (
    <section className="wf-canvas-shell wf-executable-canvas" aria-label="Executable canvas">
      {frames.length > 0 ? <ScopeBreadcrumb className="wf-instance-breadcrumb" frames={frames} onNavigate={onNavigateToScope} /> : null}
      <div className="wf-instance-canvas" onKeyDownCapture={event => activateWorkflowCanvasNode(event, nodeId => {
        const node = canvas.nodes.find(candidate => candidate.id === nodeId);
        if (node) onSelectNode(node.data.runtimeNodeId ?? node.id);
      })}>
        {canvas.mode === "compensation" ? (
          <CompensationScopeDesigner key={scopeKey} owner={canvas.owner} nodes={canvas.nodes}
            selectedNodeId={selectedNodeId} readOnly canvasRef={canvasRef}
            onSelect={onSelectNode} onOpenSlot={canvas.openSlot} />
        ) : canvas.mode === "sequence" ? (
          <SequenceDesigner key={scopeKey} nodes={canvas.nodes} selectedNodeId={selectedNodeId}
            readOnly canvasRef={canvasRef} onSelect={onSelectNode} onOpenSlot={canvas.openSlot} />
        ) : canvas.nodes.length === 0 ? <div className="wf-empty">This executable has no renderable activities.</div> : (
          <ReactFlow
            key={scopeKey}
            nodes={flowNodes}
            edges={canvas.edges}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            fitView
            fitViewOptions={getReadOnlyFitViewOptions(flowNodes.length)}
            minZoom={0.1}
            onNodesChange={onNodesChange}
            nodesDraggable={false}
            nodesConnectable={false}
            elementsSelectable
            ariaLabelConfig={{
              "node.a11yDescription.default": "Press Enter or Space to select and inspect this activity.",
              "node.a11yDescription.keyboardDisabled": "Press Enter or Space to select and inspect this activity."
            }}
            deleteKeyCode={null}
            edgesReconnectable={false}
            onNodeClick={(_, node) => onSelectNode(node.data.runtimeNodeId ?? node.id)}
            onPaneClick={() => onSelectNode(null)}
          >
            <Background />
            <FitReadOnlyFlowView nodeCount={flowNodes.length} />
            <WorkflowReadOnlyMiniMap nodeCount={flowNodes.length} />
            <ReadOnlyFlowControls nodeCount={flowNodes.length} showInteractive={false} />
            <RetainFlowViewport />
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
): {
  nodes: Node<WorkflowNodeData>[];
  edges: Edge<WorkflowEdgeData>[];
  mode: "sequence" | "compensation" | "graph";
  owner: ExecutableActivityGraph["root"];
  openSlot(ownerNodeId: string, ownerLabel: string, slot: ChildSlot): void;
} {
  const scope = resolveScope(graph.root, frames, activityCatalog);
  const scopeOwner = scope?.owner ?? graph.root;
  const scopeOwnerCatalogItem = activityCatalog.find(activity => activity.activityVersionId === scopeOwner.activityVersionId);
  const support = getActivityDesignerSupport(scopeOwner, scopeOwnerCatalogItem);
  const openSlot = (ownerNodeId: string, ownerLabel: string, slot: ChildSlot) => {
    const plan = planSlotNavigation(frames, scopeOwner, ownerNodeId, slot, slotCrumbLabel(ownerLabel, slot), activityCatalog);
    if (plan) onNavigateToScope(plan.frames);
  };
  const baseCanvas = support === "bpmn" && scope
    ? buildBpmnCanvas(scope, activityCatalog, layout, graph.activityPresentation)
    : support === "unsupported" || !scope
    ? buildUnsupportedActivityCanvas(
        scopeOwner,
        activityCatalog,
        layout,
        formatActivitySummary,
        graph.activityPresentation)
    : buildCanvas(
        scope,
        activityCatalog,
        layout,
        formatActivitySummary,
        graph.activityPresentation);

  const nodes = baseCanvas.nodes.map((node): Node<WorkflowNodeData> => {
    const bpmn = node.data as BpmnNodeData;
    const activityNodeId = bpmn.boundActivity?.nodeId ?? node.id;
    const fact = graph.factsByNodeId.get(activityNodeId);
    const ghost = isGhostFact(fact);
    const selected = activityNodeId === selectedNodeId;
    return {
      ...node,
      draggable: false,
      connectable: false,
      deletable: false,
      selected,
      data: {
        ...node.data as WorkflowNodeData,
        runtimeNodeId: activityNodeId,
        ...(ghost && fact ? {
          ghost: true,
          activityTypeKey: fact.activityType,
          category: "Unavailable activity",
          label: graph.activityPresentation.some(record => record.nodeId === activityNodeId)
            ? node.data.label
            : ghostNodeLabel(fact.activityType)
        } : {}),
        onEnterSlot: (slot: ChildSlot) => {
          openSlot(activityNodeId, node.data.label, slot);
        }
      }
    };
  });
  const edges = baseCanvas.edges.map(edge => ({ ...edge, deletable: false }));
  return {
    ...decorateWorkflowCanvasElements(nodes, edges),
    owner: scopeOwner,
    openSlot,
    mode: support !== "unsupported" && scope?.slot.id === compensationStepsSlotId ? "compensation"
      : support !== "unsupported" && scope?.slot.mode === "sequence" && scope.slot.cardinality === "many" ? "sequence" : "graph"
  };
}

function WorkflowExecutablePanelContent({ activeTab, detail, activityCatalog, graph, chosenReference, sourceDefinition, selectedNodeId, onSelectReference }: {
  activeTab: "activity" | "identity" | "references";
  detail: WorkflowExecutableDetails;
  activityCatalog: ActivityCatalogItem[];
  graph: ExecutableActivityGraph;
  chosenReference: WorkflowExecutableReference | null;
  sourceDefinition: SourceDefinitionState;
  selectedNodeId: string | null;
  onSelectReference(sourceReferenceId: string | null): void;
}) {
  useStudioTimeZone();
  const selectedFact = selectedNodeId ? graph.factsByNodeId.get(selectedNodeId) : null;
  const selectedPresentation = selectedNodeId
    ? graph.activityPresentation.find(record => record.nodeId === selectedNodeId)
    : null;
  const selectedCatalogItem = selectedFact
    ? activityCatalog.find(item =>
        item.activityTypeKey === selectedFact.activityType
        && item.version === selectedFact.activityTypeVersion)
      ?? activityCatalog.find(item => item.activityTypeKey === selectedFact.activityType)
    : null;
  const selectedDisplayName = selectedFact
    ? resolveActivityLabel(selectedPresentation, selectedCatalogItem, selectedFact.activityType)
    : null;
  const rootActivityLabel = resolveExecutableRootActivityLabel(detail, activityCatalog, graph);
  const definitionNames = definitionNamesFromSource(sourceDefinition);
  const layoutProvenance = chosenReference
    ? describeExecutableReferenceSelection(detail.chosenReference?.selection, detail.chosenReference?.sourceReferenceId ?? chosenReference.sourceReferenceId)
    : "No source reference; automatic layout";
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
    <div className="wf-instance-tab-content">
      {activeTab === "identity" ? (
        <>
          <section className="wf-instance-section">
            <h4>Identity</h4>
            <dl className="wf-instance-meta">
              <dt>Executable ID</dt>
              <dd><CopyableIdentifier label="Executable ID" value={detail.artifactId} labelPlacement="external" /></dd>
              <dt>Executable hash</dt>
              <dd><CopyableIdentifier label="Executable hash" value={detail.artifactHash} labelPlacement="external" /></dd>
              <dt>Root activity</dt>
              <dd className="wf-identity-cell">
                <span>{rootActivityLabel}</span>
                {detail.rootActivityVersion ? <small>Version {detail.rootActivityVersion}</small> : null}
              </dd>
              <dt>Nodes</dt>
              <dd>{detail.nodeCount}</dd>
              <dt>Resume targets</dt>
              <dd>{detail.resumeTargetCount}</dd>
              <dt>Created</dt>
              <dd><StudioTimestamp value={detail.createdAt} format="date-time" fallback="—" /></dd>
            </dl>
          </section>
          <section className="wf-instance-section">
            <h4>Source</h4>
            <dl className="wf-instance-meta">
              {chosenReference ? (
                <>
                  <dt>Kind</dt>
                  <dd>{formatExecutableSourceKind(chosenReference.sourceKind)}</dd>
                  <dt>Scope</dt>
                  <dd>{formatReferenceScope(chosenReference.scope)}</dd>
                </>
              ) : null}
              <dt>Layout source</dt>
              <dd className="wf-executable-layout-provenance">{layoutProvenance}</dd>
              {chosenReference ? (
                <>
                  <dt>Definition ID</dt>
                  <dd><CopyableIdentifier label="Definition ID" value={chosenReference.definitionId} labelPlacement="external" /></dd>
                  <dt>Definition version ID</dt>
                  <dd><CopyableIdentifier label="Definition version ID" value={chosenReference.definitionVersionId} labelPlacement="external" /></dd>
                </>
              ) : null}
            </dl>
            {!chosenReference ? (
              <p className="wf-muted">This artifact carries no source references.</p>
            ) : null}
            <SourceDriftCaption
              reference={chosenReference}
              sourceDefinition={sourceDefinition}
              hasDraftEquivalence={hasDraftEquivalence}
            />
          </section>
        </>
      ) : activeTab === "activity" ? (
        <WorkflowExecutableNodePanel
          fact={selectedFact ?? null}
          displayName={selectedDisplayName}
          description={selectedPresentation?.description}
        />
      ) : (
        <section className="wf-instance-section">
          <h4>Source references</h4>
          <ExecutableReferenceList
            references={detail.references}
            activeReferenceId={detail.chosenReference?.sourceReferenceId ?? null}
            definitionNames={definitionNames}
            variant="collection"
            onSelect={reference => onSelectReference(reference.sourceReferenceId)}
          />
        </section>
      )}
    </div>
  );
}

// Read-only view of the selected node's Execution Material facts: type identity, input-binding
// summaries (literals/expressions arrive pre-truncated; reference-typed values only name their
// type/id, never the secret), each binding's pinned conversion plan, and output captures with theirs.
function WorkflowExecutableNodePanel({
  fact,
  displayName,
  description
}: {
  fact: ExecutableGraphNodeFacts | null;
  displayName: string | null;
  description?: string | null;
}) {
  if (!fact) return (
    <section className="wf-instance-section">
      <h4>Activity details</h4>
      <p className="wf-muted">Select an activity in the graph to inspect its details.</p>
    </section>
  );

  return (
    <section className="wf-instance-section">
      <h4>Activity details</h4>
      <dl className="wf-instance-meta">
        <dt>Display name</dt>
        <dd>{displayName}</dd>
        {description ? (
          <>
            <dt>Description</dt>
            <dd>{description}</dd>
          </>
        ) : null}
        <dt>Type</dt>
        <dd>{fact.activityType} <small>{fact.activityTypeVersion}</small></dd>
        <dt>Executable node</dt>
        <dd>{fact.executableNodeId}</dd>
        <dt>Authored activity</dt>
        <dd>{fact.authoredActivityId}</dd>
      </dl>
      {!fact.available ? <p className="wf-instance-note">This activity is not available in this environment; the executable cannot run here.</p> : null}
      {fact.structureDiagnostics && fact.structureDiagnostics.length > 0 ? (
        <div className="wf-instance-note" role="note">
          <strong>Executable structure mapping</strong>
          <ul>
            {fact.structureDiagnostics.map((diagnostic, index) => (
              <li key={`${diagnostic.code}:${diagnostic.wireSlot}:${index}`}>
                <code>{diagnostic.wireSlot}</code>: {diagnostic.message}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
      {fact.inputBindings.length > 0 ? (
        <dl className="wf-instance-meta wf-executable-bindings">
          {fact.inputBindings.map(binding => (
            <div key={binding.inputName}>
              <dt>{binding.inputName}</dt>
              <dd>
                <span className="wf-chip">{binding.source}</span> {binding.summary ?? ""}
                <ConversionPlanCaption plan={binding.conversionPlan} />
              </dd>
            </div>
          ))}
        </dl>
      ) : (
        <p className="wf-muted">No input bindings.</p>
      )}
      {fact.outputCaptures.length > 0 ? (
        <>
          <h4>Output captures</h4>
          <dl className="wf-instance-meta wf-executable-bindings">
            {fact.outputCaptures.map(capture => (
              <div key={capture.outputName}>
                <dt>{capture.outputName}</dt>
                <dd>
                  {capture.storage ? <span className="wf-chip">{capture.storage}</span> : null}
                  <ConversionPlanCaption plan={capture.conversionPlan} />
                </dd>
              </div>
            ))}
          </dl>
        </>
      ) : null}
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
    <StudioButton
      type="button"
      disabled={!!disabledReason}
      title={disabledReason ?? undefined}
      aria-label={disabledReason ? `Open source definition unavailable: ${disabledReason}` : "Open source definition"}
      onClick={onOpen}
    >
      <WorkflowIcon size={14} /> Open source definition
    </StudioButton>
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

function resolveExecutableRootActivityLabel(
  detail: WorkflowExecutableDetails,
  activityCatalog: ActivityCatalogItem[],
  graph: ExecutableActivityGraph
) {
  const rootFact = graph.factsByNodeId.get(graph.root.nodeId);
  const activityType = rootFact?.activityType ?? detail.rootActivityType;
  const activityTypeVersion = rootFact?.activityTypeVersion ?? detail.rootActivityVersion;
  const catalogItem = activityCatalog.find(item =>
    item.activityTypeKey === activityType && item.version === activityTypeVersion)
    ?? activityCatalog.find(item => item.activityTypeKey === activityType);
  return resolveActivityLabel(rootFact?.presentation, catalogItem, activityType);
}

function definitionNamesFromSource(sourceDefinition: SourceDefinitionState): ReadonlyMap<string, string> {
  if (sourceDefinition.status !== "ready") return new Map();
  const name = sourceDefinition.definition.name?.trim();
  return name ? new Map([[sourceDefinition.definition.id, name]]) : new Map();
}

function findChosenReference(detail: WorkflowExecutableDetails | null): WorkflowExecutableReference | null {
  if (!detail) return null;
  const chosenId = detail.chosenReference?.sourceReferenceId;
  return detail.references.find(reference => reference.sourceReferenceId === chosenId)
    ?? detail.references[0]
    ?? null;
}

function formatExecutableLoadError(error: unknown, artifactId: string) {
  if (isNotFound(error)) return `Executable artifact ${artifactId} was not found.`;
  return error instanceof Error ? error.message : String(error);
}

function isNotFound(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  return /\b404\b/.test(message) || /not found/i.test(message);
}
