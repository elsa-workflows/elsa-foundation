import { Component, lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from "react";
import { ReactFlow, Background, Controls, MiniMap, type Edge, type Node } from "@xyflow/react";
import { Activity as ActivityIcon, AlertCircle, Boxes, ChevronLeft, ChevronRight, ListTree, Maximize2, Minimize2, RotateCcw, SlidersHorizontal, Sparkles, Workflow as WorkflowIcon } from "lucide-react";
import type { StudioActivityInputDescriptor, StudioAiContributionApi, StudioEndpointContext, StudioExpressionEditorContribution, StudioExpressionSourceRendererContext } from "@elsa-workflows/studio-sdk";
import { listActivities } from "../api/activityDesign";
import { getActivityExecutionInspection, getExecutable, getExecutableInputSources, getWorkflowInstance, listWorkflowInstances, type WorkflowInstanceListPage } from "../api/runtime";
import type { ActivityCatalogItem, ActivityExecutionInspection, ActivityExecutionInspectionValueSnapshot, ActivityExecutionStateSummary, ActivityNode, DiagnosticSnapshotArrayNode, DiagnosticSnapshotNode, DiagnosticSnapshotObjectNode, DiagnosticSnapshotPayloadReferenceNode, DiagnosticSnapshotUnknownNode, IncidentStateSummary, WorkflowDefinitionVersionDetails, WorkflowExecutableDetails, WorkflowInstanceDetails, WorkflowInstanceSummary } from "../workflowTypes";
import {
  applyRuntimeOverlays,
  buildCanvas,
  buildUnsupportedActivityCanvas,
  getActivityDesignerSupport,
  getChildSlots,
  latestActivityExecution,
  planSlotNavigation,
  resolveScope,
  slotCrumbLabel,
  type ChildSlot,
  type ScopeFrame,
  type WorkflowEdgeData,
  type WorkflowNodeData
} from "../workflowAdapter";
import { buildInputInspectionRows, evaluationPhase, evaluationSequence, type InputInspectionRow, type InputInspectionState } from "../inputInspectionRows";
import { buildExecutableActivityGraph, findExecutableNodeFacts, type ExecutableActivityGraph, type ExecutableGraphNodeFacts } from "../executableGraph";
import { formatDate, formatDuration, shortTypeName } from "../workflowFormatting";
import { WorkflowExecutionTimeline } from "../WorkflowInstanceTimeline";
import { WfEmptyState, WfErrorCard, WfListSkeleton } from "./StatusViews";
import { PanelTabList } from "./PanelTabList";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import { nodeTypes, edgeTypes } from "./graph";
import { ScopeBreadcrumb } from "./ScopeBreadcrumb";
import { CopyValueButton } from "./executableShared";
import type { InstanceInspectorTab, WorkflowEditorPanelTab, WorkflowInstanceInspectionData } from "./editorTypes";
import {
  activityNodeKey,
  dispatchAiAction,
  findAiAction,
  formatRunKind,
  formatWorkflowRunLoadError
} from "./editorHelpers";
import { runDetailMinInspectorWidth, useRunDetailLayout, type RunDetailLayoutMode } from "./useRunDetailLayout";
import { decorateWorkflowCanvasElements } from "./workflowAccessibility";

const runHistoryPageSizes = [10, 25, 50, 100] as const;
const ReusableBoundaryInspector = lazy(async () => {
  const module = await import("./ReusableBoundaryInspector");
  return { default: module.ReusableBoundaryInspector };
});

interface RunHistoryFilters {
  status: string;
  runKind: string;
  definitionId: string;
  workflowExecutionId: string;
  artifactId: string;
  correlationId: string;
  from: string;
  to: string;
}

interface RunHistoryLocation {
  filters: RunHistoryFilters;
  pageSize: number;
  cursor: string | null;
}

const emptyRunHistoryPage: WorkflowInstanceListPage = {
  items: [],
  previousCursor: null,
  nextCursor: null,
  hasPrevious: false,
  hasNext: false,
  count: 0,
  totalCount: 0
};

export function WorkflowInstances({ context, navigate }: {
  context: StudioEndpointContext;
  navigate(path: string): void;
}) {
  const [location, setLocation] = useState<RunHistoryLocation>(readRunHistoryLocation);
  const [draftFilters, setDraftFilters] = useState<RunHistoryFilters>(location.filters);
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [page, setPage] = useState<WorkflowInstanceListPage>(emptyRunHistoryPage);
  const requestSequence = useRef(0);

  const load = useCallback(async () => {
    const requestId = ++requestSequence.current;
    setState("loading");
    setError("");
    try {
      const filters = location.filters;
      const nextPage = await listWorkflowInstances(context, {
        status: valueOrUndefined(filters.status),
        runKind: valueOrUndefined(filters.runKind),
        definitionId: valueOrUndefined(filters.definitionId),
        workflowExecutionId: valueOrUndefined(filters.workflowExecutionId),
        artifactId: valueOrUndefined(filters.artifactId),
        correlationId: valueOrUndefined(filters.correlationId),
        from: toUtcIso(filters.from),
        to: toUtcIso(filters.to),
        take: location.pageSize,
        cursor: location.cursor ?? undefined
      });
      if (requestId !== requestSequence.current) return;
      setPage(nextPage);
      setState("ready");
    } catch (e) {
      if (requestId !== requestSequence.current) return;
      setError(e instanceof Error ? e.message : String(e));
      setPage(emptyRunHistoryPage);
      setState("failed");
    }
  }, [context, location]);

  useEffect(() => {
    void load();
    return () => {
      requestSequence.current += 1;
    };
  }, [load]);

  useEffect(() => {
    const restoreLocation = () => {
      const next = readRunHistoryLocation();
      setLocation(next);
      setDraftFilters(next.filters);
    };
    window.addEventListener("popstate", restoreLocation);
    return () => window.removeEventListener("popstate", restoreLocation);
  }, []);

  const updateLocation = (next: RunHistoryLocation, syncDraft = true) => {
    setLocation(next);
    if (syncDraft) setDraftFilters(next.filters);
    navigate(buildRunHistoryUrl(next));
  };

  const applyFilters = () => updateLocation({
    filters: normalizeRunHistoryFilters(draftFilters),
    pageSize: location.pageSize,
    cursor: null
  });

  const clearFilters = () => updateLocation({
    filters: emptyRunHistoryFilters(),
    pageSize: location.pageSize,
    cursor: null
  });

  const openInstance = (workflowExecutionId: string) => {
    navigate(`/workflows/instances/${encodeURIComponent(workflowExecutionId)}`);
  };

  const instances = page.items;
  const activeFilterCount = Object.values(location.filters).filter(Boolean).length;
  const hasDraftFilters = Object.values(draftFilters).some(Boolean);

  return (
    <>
      <form className="wf-run-filters" aria-label="Workflow run filters" onSubmit={event => {
        event.preventDefault();
        applyFilters();
      }}>
        <div className="wf-toolbar">
        <button type="button" onClick={() => void load()}>Refresh</button>
        <label className="wf-toolbar-field">
          <span>Status</span>
          <select aria-label="Workflow run status" value={draftFilters.status} onChange={event => setDraftFilters(current => ({ ...current, status: event.target.value }))}>
            <option value="">All statuses</option>
            <option value="Pending">Pending</option>
            <option value="Running">Running</option>
            <option value="Suspended">Suspended</option>
            <option value="Completed">Completed</option>
            <option value="Faulted">Faulted</option>
            <option value="Cancelled">Cancelled</option>
          </select>
        </label>
        <label className="wf-toolbar-field">
          <span>Kind</span>
          <select aria-label="Run Kind" value={draftFilters.runKind} onChange={event => setDraftFilters(current => ({ ...current, runKind: event.target.value }))}>
            <option value="">All kinds</option>
            <option value="TestRun">Test Run</option>
            <option value="PublishedRun">Published Run</option>
            <option value="BackgroundWeaverRun">Background Weaver Run</option>
            <option value="Unknown">Unknown / legacy</option>
          </select>
        </label>
        </div>
        <div className="wf-run-filter-fields">
          <RunFilterInput label="Definition" value={draftFilters.definitionId} onChange={value => setDraftFilters(current => ({ ...current, definitionId: value }))} />
          <RunFilterInput label="Execution ID" value={draftFilters.workflowExecutionId} onChange={value => setDraftFilters(current => ({ ...current, workflowExecutionId: value }))} />
          <RunFilterInput label="Artifact" value={draftFilters.artifactId} onChange={value => setDraftFilters(current => ({ ...current, artifactId: value }))} />
          <RunFilterInput label="Correlation" value={draftFilters.correlationId} onChange={value => setDraftFilters(current => ({ ...current, correlationId: value }))} />
          <RunFilterInput label="From" type="datetime-local" value={draftFilters.from} onChange={value => setDraftFilters(current => ({ ...current, from: value }))} />
          <RunFilterInput label="To" type="datetime-local" value={draftFilters.to} onChange={value => setDraftFilters(current => ({ ...current, to: value }))} />
          <div className="wf-run-filter-actions">
            <button type="submit">Apply filters</button>
            <button type="button" onClick={clearFilters} disabled={activeFilterCount === 0 && !hasDraftFilters}>Clear filters</button>
          </div>
        </div>
      </form>
      {state === "failed" ? <div role="alert"><WfErrorCard message={error} /></div> : null}
      {state === "loading" ? <div role="status" aria-live="polite" aria-label="Loading workflow runs"><WfListSkeleton /></div> : null}
      {state === "ready" && instances.length === 0 ? (
        <div role="status" aria-live="polite">
          <WfEmptyState
            icon={<Boxes size={22} />}
            title={activeFilterCount ? "No workflow runs match these filters" : "No workflow runs yet"}
            description={activeFilterCount
              ? "Clear or adjust the operational filters to broaden the result set."
              : "Run a published workflow executable to create execution history here."}
          />
        </div>
      ) : null}
      {state === "ready" && instances.length > 0 ? (
        <div className="wf-grid wf-instance-grid" role="table" aria-label="Workflow runs">
          <div className="wf-grid-head" role="row">
            <span>Run</span>
            <span>Kind</span>
            <span>Status</span>
            <span>Definition</span>
            <span>Activity</span>
            <span>Started</span>
            <span>Duration</span>
          </div>
          {instances.map(instance => (
            <button
              type="button"
              className="wf-grid-row"
              role="row"
              aria-label={`Inspect workflow run ${instance.workflowExecutionId}`}
              key={instance.workflowExecutionId}
              onClick={() => openInstance(instance.workflowExecutionId)}
            >
              <span>
                <strong>{instance.workflowExecutionId}</strong>
                <small>{instance.artifactId}</small>
              </span>
              <span>{formatRunKind(instance.runKind)}</span>
              <span><WorkflowStatusBadge status={instance.status} subStatus={instance.subStatus} /></span>
              <span>
                <strong>{instance.definitionId}</strong>
                <small>{instance.definitionVersionId}</small>
              </span>
              <span>
                <strong>{instance.activityCount} activities</strong>
                <small>{instance.incidentCount} incidents</small>
              </span>
              <span>{formatDate(instance.startedAt ?? instance.createdAt)}</span>
              <span>{formatDuration(instance.startedAt ?? instance.createdAt, instance.completedAt ?? instance.updatedAt)}</span>
            </button>
          ))}
        </div>
      ) : null}
      {state === "ready" ? (
        <div className="wf-pagination" aria-label="Workflow run pagination">
          <span className="wf-pagination-summary" aria-live="polite">
            Showing {page.count} of {page.totalCount} matching runs
          </span>
          <label className="wf-page-size">
            Rows
            <select aria-label="Workflow run page size" value={location.pageSize} onChange={event => updateLocation({
              filters: location.filters,
              pageSize: Number(event.target.value),
              cursor: null
            }, false)}>
              {runHistoryPageSizes.map(size => <option key={size} value={size}>{size}</option>)}
            </select>
          </label>
          <div className="wf-page-controls">
            <button type="button" disabled={!page.hasPrevious || !page.previousCursor} onClick={() => {
              if (page.previousCursor) updateLocation({ ...location, cursor: page.previousCursor }, false);
            }} aria-label="Previous workflow run page">
              <ChevronLeft size={14} /> Previous
            </button>
            <span role="status">{page.hasNext ? "More results" : "End of results"}</span>
            <button type="button" disabled={!page.hasNext || !page.nextCursor} onClick={() => {
              if (page.nextCursor) updateLocation({ ...location, cursor: page.nextCursor }, false);
            }} aria-label="Next workflow run page">
              Next <ChevronRight size={14} />
            </button>
          </div>
        </div>
      ) : null}
    </>
  );
}

function RunFilterInput({ label, value, type = "text", onChange }: {
  label: string;
  value: string;
  type?: "text" | "datetime-local";
  onChange(value: string): void;
}) {
  return (
    <label className="wf-run-filter-field">
      <span>{label}</span>
      <input aria-label={`Workflow run ${label.toLowerCase()}`} type={type} value={value} onChange={event => onChange(event.target.value)} />
    </label>
  );
}

function emptyRunHistoryFilters(): RunHistoryFilters {
  return {
    status: "",
    runKind: "",
    definitionId: "",
    workflowExecutionId: "",
    artifactId: "",
    correlationId: "",
    from: "",
    to: ""
  };
}

function normalizeRunHistoryFilters(filters: RunHistoryFilters): RunHistoryFilters {
  return Object.fromEntries(Object.entries(filters).map(([key, value]) => [key, value.trim()])) as unknown as RunHistoryFilters;
}

export function readRunHistoryLocation(search = window.location.search): RunHistoryLocation {
  const parameters = new URLSearchParams(search);
  const requestedPageSize = Number(parameters.get("pageSize"));
  return {
    filters: {
      status: parameters.get("status") ?? "",
      runKind: parameters.get("runKind") ?? "",
      definitionId: parameters.get("definitionId") ?? "",
      workflowExecutionId: parameters.get("workflowExecutionId") ?? "",
      artifactId: parameters.get("artifactId") ?? "",
      correlationId: parameters.get("correlationId") ?? "",
      from: parameters.get("from") ?? "",
      to: parameters.get("to") ?? ""
    },
    pageSize: runHistoryPageSizes.includes(requestedPageSize as typeof runHistoryPageSizes[number]) ? requestedPageSize : 25,
    cursor: parameters.get("cursor")
  };
}

export function buildRunHistoryUrl(location: RunHistoryLocation) {
  const parameters = new URLSearchParams();
  Object.entries(location.filters).forEach(([name, value]) => {
    if (value) parameters.set(name, value);
  });
  if (location.pageSize !== 25) parameters.set("pageSize", String(location.pageSize));
  if (location.cursor) parameters.set("cursor", location.cursor);
  const query = parameters.toString();
  return `/workflows/instances${query ? `?${query}` : ""}`;
}

function valueOrUndefined(value: string) {
  return value || undefined;
}

function toUtcIso(value: string) {
  if (!value) return undefined;
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? undefined : date.toISOString();
}

interface WorkflowInstanceWorkbenchData extends WorkflowInstanceInspectionData {
  executableGraph: ExecutableActivityGraph | null;
}

export function WorkflowInstanceDetailsWorkbench({ context, ai, expressionEditors = [], workflowExecutionId, initialActivityExecutionId = null, navigate }: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  expressionEditors?: StudioExpressionEditorContribution[];
  workflowExecutionId: string;
  initialActivityExecutionId?: string | null;
  navigate(path: string): void;
}) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [data, setData] = useState<WorkflowInstanceWorkbenchData | null>(null);
  const [selectedEvidenceId, setSelectedEvidenceId] = useState<string | null>(null);
  const [frames, setFrames] = useState<ScopeFrame[]>([]);
  const selectedActivity = findSelectedActivityExecution(data?.details.activities ?? [], selectedEvidenceId);
  const {
    containerRef,
    mode,
    inspectorWidth,
    inspectorMaxWidth,
    inspectorCollapsed,
    inspectorMaximized,
    inspectorExpanded,
    canResizeInspector,
    mediumDrawerOpen,
    workbenchClassName,
    workbenchStyle,
    closeInspector,
    toggleInspectorCollapsed,
    toggleInspectorMaximized,
    startInspectorResize,
    handleInspectorResizeKeyDown
  } = useRunDetailLayout({ selectedActivityId: selectedActivity?.activityExecutionId });
  const instanceAction = findAiAction(ai, "weaver.workflows.explain-instance");

  const load = useCallback(async () => {
    if (!workflowExecutionId) {
      setError("No workflow execution id was provided.");
      setState("failed");
      return;
    }

    setState("loading");
    setError("");
    try {
      const details = await getWorkflowInstance(context, workflowExecutionId);
      const [executableResult, activityCatalog, sourceResult] = await Promise.all([
        getExecutable(context, details.instance.artifactId, details.instance.sourceReferenceId).then(
          executable => ({ executable, error: "" }),
          error => ({ executable: null, error: error instanceof Error ? error.message : String(error) })
        ),
        listActivities(context),
        details.instance.sourceReferenceId
          ? getExecutableInputSources(context, details.instance.artifactId, details.instance.sourceReferenceId).then(
              sources => ({ sources, access: sources.accessState || sources.access || "allowed" }),
              error => ({ sources: null, access: sourceAccessFromError(error) }))
          : Promise.resolve({ sources: null, access: "unavailable" })
      ]);
      const catalog = activityCatalog.activities;
      const executableGraph = executableResult.executable
        ? buildExecutableActivityGraph(
            executableResult.executable.rootActivity,
            catalog,
            sourceResult.sources?.authoredInputs ?? [],
            sourceResult.sources?.compiledInputs ?? [],
            sourceResult.access)
        : null;
      setData({
        details,
        definitionVersion: executableResult.executable
          ? projectPinnedExecutable(executableResult.executable, details, catalog, executableGraph ?? undefined)
          : null,
        definitionVersionError: executableResult.error,
        activityCatalog: catalog,
        executableGraph
      });
      const initialEvidence = resolveInitialActivityEvidenceId(
        details.activities,
        initialActivityExecutionId);
      setSelectedEvidenceId(initialEvidence);
      setFrames([]);
      setState("ready");
      if (initialEvidence) {
        requestAnimationFrame(() => requestAnimationFrame(() => focusSelectedCanvasActivity(initialEvidence)));
      }
    } catch (e) {
      setData(null);
      setError(formatWorkflowRunLoadError(e, workflowExecutionId));
      setState("failed");
    }
  }, [context, initialActivityExecutionId, workflowExecutionId]);

  useEffect(() => {
    void load();
  }, [load]);

  const goBack = () => {
    navigate("/workflows/instances");
  };

  const openDefinitionDesigner = () => {
    const definitionId = data?.details.instance.definitionId;
    if (!definitionId) return;

    navigate(`/workflows/definitions?definition=${encodeURIComponent(definitionId)}`);
  };
  const closeResponsiveInspector = () => {
    closeInspector();
    requestAnimationFrame(() => focusSelectedCanvasActivity(selectedEvidenceId));
  };

  return (
    <>
      <div className="wf-toolbar">
        <button type="button" onClick={goBack}><ChevronLeft size={14} /> Runs</button>
        {data?.details.instance.definitionId ? (
          <button type="button" onClick={openDefinitionDesigner}><WorkflowIcon size={14} /> Designer</button>
        ) : null}
        <button type="button" onClick={() => void load()}><RotateCcw size={14} /> Refresh</button>
        {data && instanceAction ? (
          <button type="button" onClick={() => dispatchAiAction(ai, instanceAction, data.details)}>
            <Sparkles size={13} /> Explain
          </button>
        ) : null}
      </div>
      {state === "loading" ? <div className="wf-empty">Loading workflow run...</div> : null}
      {state === "failed" ? <WfErrorCard message={error} /> : null}
      {state === "ready" && data ? (
        <div ref={containerRef} className={workbenchClassName} style={workbenchStyle}>
          <WorkflowInstanceCanvas
            definitionVersion={data.definitionVersion}
            definitionVersionError={data.definitionVersionError}
            activityCatalog={data.activityCatalog}
            details={data.details}
            selectedEvidenceId={selectedEvidenceId}
            onSelectEvidence={setSelectedEvidenceId}
            frames={frames}
            onNavigateToScope={setFrames}
            inactive={mode === "medium" && mediumDrawerOpen}
          />
          {mediumDrawerOpen ? (
            <button type="button" className="wf-run-inspector-backdrop" aria-label="Close run details" onClick={closeResponsiveInspector} />
          ) : null}
          {canResizeInspector ? (
            <div
              className="wf-side-resize-handle right"
              role="separator"
              aria-label="Resize run details panel"
              aria-orientation="vertical"
              aria-valuemin={runDetailMinInspectorWidth}
              aria-valuemax={inspectorMaxWidth}
              aria-valuenow={inspectorWidth}
              tabIndex={0}
              onPointerDown={startInspectorResize}
              onKeyDown={handleInspectorResizeKeyDown}
            />
          ) : <div className="wf-side-resize-spacer" />}
          <WorkflowInstanceInspector
            context={context}
            summary={data.details.instance}
            details={data.details}
            state="ready"
            error=""
            selectedEvidenceId={selectedEvidenceId}
            onSelectEvidence={setSelectedEvidenceId}
            activityCatalog={data.activityCatalog}
            executableGraph={data.executableGraph}
            expressionEditors={expressionEditors}
            graphNodeIds={data.definitionVersion ? collectWorkflowGraphNodeIds(data.definitionVersion, data.activityCatalog) : undefined}
            rootNodeId={data.definitionVersion?.state.rootActivity?.nodeId}
            collapsed={inspectorCollapsed}
            expanded={inspectorExpanded}
            maximized={inspectorMaximized}
            layoutMode={mode}
            responsiveOpen={mode === "medium" ? mediumDrawerOpen : mode === "narrow" && inspectorExpanded}
            onCloseResponsive={closeResponsiveInspector}
            onToggleCollapsed={toggleInspectorCollapsed}
            onToggleMaximized={toggleInspectorMaximized}
          />
        </div>
      ) : null}
    </>
  );
}

export function projectPinnedExecutable(
  executable: WorkflowExecutableDetails,
  details: WorkflowInstanceDetails,
  catalog: ActivityCatalogItem[],
  executableGraph?: ExecutableActivityGraph
): WorkflowDefinitionVersionDetails {
  const timestamp = executable.createdAt ?? details.instance.createdAt;
  return {
    id: details.instance.definitionVersionId,
    version: details.instance.artifactVersion,
    definition: {
      id: details.instance.definitionId,
      name: details.instance.definitionId,
      description: `Pinned Runtime executable ${executable.artifactId}`,
      createdAt: timestamp,
      lastModifiedAt: timestamp
    },
    state: { rootActivity: (executableGraph ?? buildExecutableActivityGraph(executable.rootActivity, catalog)).root },
    layout: executable.chosenReference?.layout ?? []
  };
}

export function resolveInitialActivityEvidenceId(
  activities: ReadonlyArray<{ activityExecutionId: string }>,
  requestedActivityExecutionId: string | null
) {
  return requestedActivityExecutionId &&
    activities.some(activity => activity.activityExecutionId === requestedActivityExecutionId)
    ? requestedActivityExecutionId
    : null;
}

function sourceAccessFromError(error: unknown) {
  const status = typeof error === "object" && error !== null && "status" in error
    ? Number((error as { status?: unknown }).status)
    : undefined;
  const message = error instanceof Error ? error.message : String(error);
  return status === 401 || status === 403 || /forbidden|permission|unauthori[sz]ed/i.test(message)
    ? "permissionHidden"
    : "unavailable";
}

function WorkflowInstanceCanvas({
  definitionVersion,
  definitionVersionError,
  activityCatalog,
  details,
  selectedEvidenceId,
  onSelectEvidence,
  frames,
  onNavigateToScope,
  inactive = false
}: {
  definitionVersion: WorkflowDefinitionVersionDetails | null;
  definitionVersionError: string;
  activityCatalog: ActivityCatalogItem[];
  details: WorkflowInstanceDetails;
  selectedEvidenceId: string | null;
  onSelectEvidence(evidenceId: string | null): void;
  frames: ScopeFrame[];
  onNavigateToScope(frames: ScopeFrame[]): void;
  inactive?: boolean;
}) {
  const scopeKey = getInstanceScopeKey(frames);
  const canvas = useMemo(
    () => buildInstanceCanvas(definitionVersion, activityCatalog, details, selectedEvidenceId, frames, onNavigateToScope),
    [activityCatalog, definitionVersion, details, frames, onNavigateToScope, selectedEvidenceId]
  );

  return (
    <section className="wf-instance-canvas-shell" aria-label="Workflow run canvas" aria-hidden={inactive || undefined} inert={inactive || undefined}>
      <header>
        <div>
          <span>Pinned Runtime executable</span>
          <h3>{definitionVersion ? (
            <>
              {definitionVersion.definition.name} <small>{definitionVersion.version}</small>
            </>
          ) : (
            <>
              Executable graph unavailable <small>{details.instance.artifactId}</small>
            </>
          )}</h3>
        </div>
        <WorkflowStatusBadge status={details.instance.status} subStatus={details.instance.subStatus} />
      </header>
      {definitionVersion ? (
        <ScopeBreadcrumb className="wf-instance-breadcrumb" frames={frames} onNavigate={onNavigateToScope} />
      ) : null}
      <div className="wf-instance-canvas">
        {!definitionVersion ? (
          <div className="wf-empty">
            The workflow run loaded, but its pinned executable graph could not be resolved.
            {definitionVersionError ? <small>{definitionVersionError}</small> : null}
          </div>
        ) : null}
        {definitionVersion && canvas.nodes.length === 0 ? <div className="wf-empty">No workflow activities are available for this executable.</div> : null}
        {canvas.nodes.length > 0 ? (
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
            onNodeClick={(_, node) => onSelectEvidence(node.id)}
            onPaneClick={() => onSelectEvidence(null)}
          >
            <Background />
            <MiniMap pannable zoomable />
            <Controls />
          </ReactFlow>
        ) : null}
      </div>
    </section>
  );
}

// Builds the read-only run-inspection canvas for the scope addressed by `frames`. Slot badges navigate
// along the same planned frame path as the editor (planSlotNavigation): a slot holding a single canvas
// container is descended THROUGH so the canvas shows the container's contents instead of a one-node
// canvas holding the container. Only the plan's frames are used — selection stays evidence-driven.
export function buildInstanceCanvas(
  definitionVersion: WorkflowDefinitionVersionDetails | null,
  activityCatalog: ActivityCatalogItem[],
  details: WorkflowInstanceDetails,
  selectedEvidenceId: string | null,
  frames: ScopeFrame[],
  onNavigateToScope: (frames: ScopeFrame[]) => void
): { nodes: Node<WorkflowNodeData>[]; edges: Edge<WorkflowEdgeData>[] } {
  const root = definitionVersion?.state.rootActivity;
  if (!definitionVersion || !root) return { nodes: [], edges: [] };

  const scope = resolveScope(root, frames, activityCatalog);
  const scopeOwner = scope?.owner ?? root;
  const scopeOwnerCatalogItem = activityCatalog.find(activity => activity.activityVersionId === scopeOwner.activityVersionId);
  const support = getActivityDesignerSupport(scopeOwner, scopeOwnerCatalogItem);
  const baseCanvas = support === "unsupported" || !scope
    ? buildUnsupportedActivityCanvas(scopeOwner, activityCatalog, definitionVersion.layout)
    : buildCanvas(scope, activityCatalog, definitionVersion.layout);
  const readonlyNodes = baseCanvas.nodes.map(node => ({
    ...node,
    draggable: false,
    connectable: false,
    deletable: false,
    data: {
      ...node.data,
      onEnterSlot: (slot: ChildSlot) => {
        const plan = planSlotNavigation(frames, scopeOwner, node.id, slot, slotCrumbLabel(node.data.label, slot), activityCatalog);
        if (plan) onNavigateToScope(plan.frames);
      }
    }
  }));

  return decorateWorkflowCanvasElements(
    applyRuntimeOverlays(readonlyNodes, details.activities, details.incidents, selectedEvidenceId),
    baseCanvas.edges.map(edge => ({ ...edge, deletable: false }))
  );
}

function getInstanceScopeKey(frames: ScopeFrame[]) {
  if (frames.length === 0) return "root";
  return frames.map(frame => `${frame.ownerNodeId}:${frame.slotId}`).join("/");
}

function collectWorkflowGraphNodeIds(definitionVersion: WorkflowDefinitionVersionDetails, activityCatalog: ActivityCatalogItem[]) {
  const ids = new Set<string>();
  const visit = (activity: ActivityNode | null | undefined) => {
    if (!activity) return;
    ids.add(activity.nodeId);
    for (const slot of getChildSlots(activity, activityCatalog)) {
      slot.activities.forEach(visit);
    }
  };
  visit(definitionVersion.state.rootActivity);
  return ids;
}

function WorkflowInstanceInspector({
  context,
  summary,
  details,
  state,
  error,
  selectedEvidenceId = null,
  onSelectEvidence,
  graphNodeIds,
  rootNodeId,
  activityCatalog = [],
  executableGraph,
  expressionEditors = [],
  collapsed = false,
  expanded = true,
  maximized = false,
  layoutMode = "desktop",
  responsiveOpen = false,
  onCloseResponsive,
  onToggleCollapsed,
  onToggleMaximized
}: {
  context: StudioEndpointContext;
  summary: WorkflowInstanceSummary | null;
  details: WorkflowInstanceDetails | null;
  state: "idle" | "loading" | "ready" | "failed";
  error: string;
  selectedEvidenceId?: string | null;
  onSelectEvidence?(evidenceId: string): void;
  graphNodeIds?: Set<string>;
  rootNodeId?: string | null;
  activityCatalog?: ActivityCatalogItem[];
  executableGraph?: ExecutableActivityGraph | null;
  expressionEditors?: StudioExpressionEditorContribution[];
  collapsed?: boolean;
  expanded?: boolean;
  maximized?: boolean;
  layoutMode?: RunDetailLayoutMode;
  responsiveOpen?: boolean;
  onCloseResponsive?(): void;
  onToggleCollapsed?(): void;
  onToggleMaximized?(): void;
}) {
  const [activeTab, setActiveTab] = useState<InstanceInspectorTab>("timeline");
  const inspectorRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (!responsiveOpen) return;
    const inspector = inspectorRef.current;
    const firstControl = inspector?.querySelector<HTMLElement>("button:not([disabled]), [href], [tabindex]:not([tabindex='-1'])");
    requestAnimationFrame(() => (firstControl ?? inspector)?.focus());
  }, [responsiveOpen]);

  if (!summary) {
    return <aside className="wf-instance-inspector"><div className="wf-empty">Select a workflow run to inspect its timeline.</div></aside>;
  }

  const incidentCount = details?.incidents.length ?? 0;
  const selectedActivity = findSelectedActivityExecution(details?.activities ?? [], selectedEvidenceId);
  const openActivityEvidence = (evidenceId: string) => {
    onSelectEvidence?.(evidenceId);
    setActiveTab("activity");
  };
  const tabs: WorkflowEditorPanelTab[] = [
    { id: "timeline", title: "Timeline", order: 0, icon: <ListTree size={14} />, render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: <ActivityIcon size={14} />, render: () => null },
    { id: "issues", title: incidentCount > 0 ? `Issues (${incidentCount})` : "Issues", order: 2, icon: <AlertCircle size={14} />, render: () => null },
    { id: "details", title: "Details", order: 3, icon: <SlidersHorizontal size={14} />, render: () => null }
  ];

  return (
    <aside
      ref={inspectorRef}
      className="wf-instance-inspector"
      aria-label="Run details panel"
      role={layoutMode === "medium" ? "dialog" : undefined}
      aria-modal={layoutMode === "medium" ? true : undefined}
      tabIndex={layoutMode === "medium" ? -1 : undefined}
      onKeyDown={event => {
        if (layoutMode === "medium" && event.key === "Escape") {
          event.preventDefault();
          onCloseResponsive?.();
        }
        if (layoutMode === "medium" && event.key === "Tab") trapFocusWithin(event, inspectorRef.current);
      }}
    >
      <div className="wf-panel-title wf-instance-panel-title">
        <PanelTabList label="Run details tabs" tabs={tabs} activeTabId={activeTab} onSelect={tabId => setActiveTab(tabId as InstanceInspectorTab)} />
        <span className="wf-panel-actions">
          {layoutMode !== "desktop" ? (
            <button
              type="button"
              className="wf-panel-action-button"
              aria-label={layoutMode === "narrow" ? "Back to workflow run canvas" : "Close run details panel"}
              title={layoutMode === "narrow" ? "Back to canvas" : "Close"}
              onClick={onCloseResponsive}
            >
              <ChevronLeft size={14} />
            </button>
          ) : null}
          {layoutMode === "desktop" ? (
          <button
            type="button"
            className="wf-panel-action-button"
            aria-label={collapsed ? "Expand run details panel" : "Collapse run details panel"}
            title={collapsed ? "Expand" : "Collapse"}
            onClick={onToggleCollapsed}
          >
            {collapsed ? <ChevronLeft size={14} /> : <ChevronRight size={14} />}
          </button>
          ) : null}
          {layoutMode === "desktop" && !collapsed ? (
            <button
              type="button"
              className="wf-panel-action-button"
              aria-label={maximized ? "Restore run details panel" : "Maximize run details panel"}
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
          {state === "loading" ? <div className="wf-empty">Loading run details...</div> : null}
          {state === "failed" ? <WfErrorCard message={error} /> : null}
          {state === "ready" && details ? (
            <div className="wf-instance-tab-content">
              {activeTab === "timeline" ? (
                <WorkflowExecutionTimeline
                  activities={details.activities}
                  activityCatalog={activityCatalog}
                  selectedEvidenceId={selectedEvidenceId}
                  onSelectEvidence={openActivityEvidence}
                />
              ) : activeTab === "activity" ? (
                <WorkflowActivityExecutionDetails
                  context={context}
                  activity={selectedActivity}
                  activityCatalog={activityCatalog}
                  executableNodeFacts={findExecutableNodeFacts(executableGraph, selectedActivity)}
                  expressionEditors={expressionEditors}
                />
              ) : activeTab === "issues" ? (
                <>
                  <WorkflowIncidentList incidents={details.incidents} selectedEvidenceId={selectedEvidenceId} onSelectEvidence={onSelectEvidence} />
                  <WorkflowUnmatchedEvidence details={details} graphNodeIds={graphNodeIds} rootNodeId={rootNodeId} />
                </>
              ) : (
                <dl className="wf-instance-meta">
                  <dt>Status</dt>
                  <dd><WorkflowStatusBadge status={summary.status} subStatus={summary.subStatus} /></dd>
                  <dt>Run Kind</dt>
                  <dd>{formatRunKind(summary.runKind)}</dd>
                  <dt>Artifact</dt>
                  <dd>{summary.artifactId} <small>{summary.artifactVersion}</small></dd>
                  <dt>Definition</dt>
                  <dd>{summary.definitionId} <small>{summary.definitionVersionId}</small></dd>
                  <dt>Created</dt>
                  <dd>{formatDate(summary.createdAt)}</dd>
                  <dt>Started</dt>
                  <dd>{formatDate(summary.startedAt)}</dd>
                  <dt>Completed</dt>
                  <dd>{formatDate(summary.completedAt)}</dd>
                  <dt>Correlation</dt>
                  <dd>{summary.correlationId || "None"}</dd>
                </dl>
              )}
            </div>
          ) : null}
        </>
      ) : null}
    </aside>
  );
}

function findSelectedActivityExecution(activities: ActivityExecutionStateSummary[], selectedEvidenceId: string | null) {
  if (!selectedEvidenceId) return null;

  const exactActivity = activities.find(activity => activity.activityExecutionId === selectedEvidenceId);
  if (exactActivity) return exactActivity;

  const nodeActivities = activities.filter(activity =>
    activity.executableNodeId === selectedEvidenceId ||
    activity.authoredActivityId === selectedEvidenceId);
  return nodeActivities.length > 0 ? latestActivityExecution(nodeActivities) : null;
}

function focusSelectedCanvasActivity(selectedEvidenceId: string | null) {
  if (!selectedEvidenceId) return;
  const node = [...document.querySelectorAll<HTMLElement>(".wf-instance-canvas [data-id]")]
    .find(element => element.dataset.id === selectedEvidenceId);
  node?.focus();
}

function trapFocusWithin(event: ReactKeyboardEvent<HTMLElement>, container: HTMLElement | null) {
  if (!container) return;
  const controls = [...container.querySelectorAll<HTMLElement>(
    "button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])"
  )].filter(element => !element.hidden && element.getAttribute("aria-hidden") !== "true");
  if (controls.length === 0) {
    event.preventDefault();
    container.focus();
    return;
  }

  const first = controls[0]!;
  const last = controls.at(-1)!;
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

type ActivityExecutionInspectionState = {
  activityExecutionId: string | null;
  status: "idle" | "loading" | "ready" | "failed";
  inspection: ActivityExecutionInspection | null;
  error: string;
};

export function WorkflowActivityExecutionDetails({
  context,
  activity,
  activityCatalog,
  executableNodeFacts,
  expressionEditors = []
}: {
  context: StudioEndpointContext;
  activity: ActivityExecutionStateSummary | null;
  activityCatalog: ActivityCatalogItem[];
  executableNodeFacts?: ExecutableGraphNodeFacts;
  expressionEditors?: StudioExpressionEditorContribution[];
}) {
  const selectedActivityExecutionId = activity?.activityExecutionId ?? null;
  const selectedWorkflowExecutionId = activity?.workflowExecutionId ?? null;
  const [inspectionState, setInspectionState] = useState<ActivityExecutionInspectionState>({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  const [copyStatus, setCopyStatus] = useState("");

  useEffect(() => {
    setCopyStatus("");
    if (!selectedActivityExecutionId || !selectedWorkflowExecutionId) {
      setInspectionState({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }

    const controller = new AbortController();
    const activityExecutionId = selectedActivityExecutionId;
    setInspectionState({ activityExecutionId, status: "loading", inspection: null, error: "" });

    getActivityExecutionInspection(context, selectedWorkflowExecutionId, activityExecutionId, controller.signal).then(
      inspection => {
        if (!controller.signal.aborted) setInspectionState({ activityExecutionId, status: "ready", inspection, error: "" });
      },
      error => {
        if (!controller.signal.aborted) {
          setInspectionState({
            activityExecutionId,
            status: "failed",
            inspection: null,
            error: error instanceof Error ? error.message : String(error)
          });
        }
      }
    );

    return () => controller.abort();
  }, [context, selectedActivityExecutionId, selectedWorkflowExecutionId]);

  if (!activity) {
    return (
      <section className="wf-instance-section">
        <h4>Activity</h4>
        <p>No activity selected.</p>
      </section>
    );
  }

  const catalogItem = activityCatalog.find(item => item.activityTypeKey === activity.activityType);
  const declaredInputs = readDeclaredActivityInputs(catalogItem?.inputs);
  const activityLabel = catalogItem?.displayName || shortTypeName(activity.activityType) || activity.activityType;
  const activityTypeLabel = shortTypeName(activity.activityType) ?? activity.activityType;
  const statusLabel = [activity.status, activity.subStatus].filter(Boolean).join(" · ");
  const startedLabel = formatDate(activity.startedAt);
  const completedLabel = formatDate(activity.completedAt);
  const durationLabel = formatDuration(activity.startedAt, activity.completedAt) || "Unknown";
  const bookmarkCount = activity.bookmarkIds?.length ?? 0;
  const incidentCount = activity.incidentIds?.length ?? 0;
  const markCopied = (label: string) => setCopyStatus(`Copied ${label}.`);
  const markCopyFailed = (label: string) => setCopyStatus(`Could not copy ${label}.`);

  return (
    <>
      <section className="wf-instance-section wf-activity-overview">
        <div className="wf-activity-overview-heading">
          <span className="wf-activity-overview-icon" aria-hidden="true">
            <ActivityIcon size={18} />
          </span>
          <div className="wf-activity-overview-title">
            <span className="wf-activity-overview-eyebrow">Selected activity</span>
            <div className="wf-activity-copy-line">
              <h4>{activityLabel}</h4>
              <CopyValueButton
                value={activityLabel}
                ariaLabel="Copy activity name"
                copiedLabel="activity name"
                onCopied={markCopied}
                onCopyFailed={markCopyFailed}
              />
            </div>
            <div className="wf-activity-copy-line wf-activity-overview-type">
              <span title={activity.activityType}>{activityTypeLabel} <small>{activity.activityTypeVersion}</small></span>
              <CopyValueButton
                value={`${activityTypeLabel} ${activity.activityTypeVersion}`}
                ariaLabel="Copy activity type"
                copiedLabel="activity type"
                onCopied={markCopied}
                onCopyFailed={markCopyFailed}
              />
            </div>
          </div>
          <span className="wf-activity-overview-status">
            <WorkflowStatusBadge status={activity.status} subStatus={activity.subStatus} />
            <CopyValueButton
              value={statusLabel}
              ariaLabel="Copy activity status"
              copiedLabel="activity status"
              onCopied={markCopied}
              onCopyFailed={markCopyFailed}
            />
          </span>
        </div>

        <dl className="wf-activity-summary-grid">
          <ActivityMetadataValue label="Started" value={startedLabel} copiedLabel="start time" onCopied={markCopied} onCopyFailed={markCopyFailed} />
          <ActivityMetadataValue label="Duration" value={durationLabel} copiedLabel="duration" onCopied={markCopied} onCopyFailed={markCopyFailed} />
          <ActivityMetadataValue label="Incidents" value={String(incidentCount)} copiedLabel="incident count" onCopied={markCopied} onCopyFailed={markCopyFailed} />
        </dl>

        {copyStatus ? <p className="wf-copy-status" role="status" aria-live="polite">{copyStatus}</p> : null}
      </section>
      {inspectionState.status === "ready" && inspectionState.inspection?.boundary ? (
        <Suspense fallback={<section className="wf-instance-section" role="status">Loading reusable boundary inspector...</section>}>
          <ReusableBoundaryInspector context={context} inspection={inspectionState.inspection} />
        </Suspense>
      ) : null}
      <WorkflowActivityInputEvidence
        state={inspectionState}
        declarations={declaredInputs}
        executableNodeFacts={executableNodeFacts}
        expressionEditors={expressionEditors}
      />
      <WorkflowActivityValueEvidence
        state={inspectionState}
        subject="ActivityOutput"
        title="Outputs"
        loadingText="Loading runtime output evidence..."
        failureText="Runtime output evidence is unavailable."
        emptyText="No runtime output snapshots were recorded for this execution."
      />
      <section className="wf-instance-section">
        <details className="wf-activity-execution-details">
          <summary>
            <span>Execution details</span>
            <small>4 values</small>
          </summary>
          <dl className="wf-activity-detail-list">
            <ActivityMetadataValue label="Activity Execution ID" value={activity.activityExecutionId} copiedLabel="activity execution ID" code onCopied={markCopied} onCopyFailed={markCopyFailed} />
            <ActivityMetadataValue label="Authored Activity ID" value={activity.authoredActivityId} copiedLabel="authored activity ID" code onCopied={markCopied} onCopyFailed={markCopyFailed} />
            <ActivityMetadataValue label="Completed" value={completedLabel} copiedLabel="completion time" onCopied={markCopied} onCopyFailed={markCopyFailed} />
            <ActivityMetadataValue label="Bookmarks" value={String(bookmarkCount)} copiedLabel="bookmark count" onCopied={markCopied} onCopyFailed={markCopyFailed} />
          </dl>
        </details>
      </section>
    </>
  );
}

function readDeclaredActivityInputs(inputs: unknown[] | undefined): StudioActivityInputDescriptor[] {
  return (inputs ?? []).filter((input): input is StudioActivityInputDescriptor => {
    if (!input || typeof input !== "object") return false;
    const record = input as Record<string, unknown>;
    return typeof record.name === "string" && typeof record.typeName === "string";
  });
}

function ActivityMetadataValue({
  label,
  value,
  copiedLabel,
  code = false,
  onCopied,
  onCopyFailed
}: {
  label: string;
  value: string;
  copiedLabel: string;
  code?: boolean;
  onCopied(label: string): void;
  onCopyFailed(label: string): void;
}) {
  const content = code ? <code title={value}>{value}</code> : <span title={value}>{value}</span>;

  return (
    <div className="wf-activity-meta-item">
      <dt>{label}</dt>
      <dd>
        <span className="wf-activity-meta-value">{content}</span>
        <CopyValueButton
          value={value}
          ariaLabel={`Copy ${copiedLabel}`}
          copiedLabel={copiedLabel}
          onCopied={onCopied}
          onCopyFailed={onCopyFailed}
        />
      </dd>
    </div>
  );
}

function WorkflowActivityInputEvidence({
  state,
  declarations,
  executableNodeFacts,
  expressionEditors
}: {
  state: ActivityExecutionInspectionState;
  declarations: StudioActivityInputDescriptor[];
  executableNodeFacts?: ExecutableGraphNodeFacts;
  expressionEditors: StudioExpressionEditorContribution[];
}) {
  if (state.status === "idle") return null;

  const evaluations = state.status === "ready"
    ? (state.inspection?.valueSnapshots ?? []).filter(snapshot => snapshot.subject === "ActivityInput")
    : [];
  const rows = buildInputInspectionRows({
    declarations,
    authoredSources: executableNodeFacts?.authoredInputs ?? [],
    compiledBindings: executableNodeFacts?.inputBindings ?? [],
    evaluations,
    authoredSourceAccess: executableNodeFacts?.authoredInputsAccess
  });

  return (
    <section className="wf-instance-section">
      <header className="wf-runtime-evidence-heading">
        <h4>
          Inputs
          {rows.length > 0 ? <span className="wf-runtime-evidence-count" aria-label={`${rows.length} inputs`}>{rows.length}</span> : null}
        </h4>
        <span className="wf-runtime-capture-mode">Paired evidence</span>
      </header>
      {state.status === "loading" ? <p>Loading runtime input evidence...</p> : null}
      {state.status === "failed" ? (
        <>
          <p>Runtime input evidence is unavailable. Authored source remains independently inspectable where permitted.</p>
          {state.error ? <p className="wf-instance-note">{state.error}</p> : null}
        </>
      ) : null}
      {rows.length === 0 && state.status === "ready" ? <p>No declared inputs, pinned bindings, or runtime input evidence are available for this execution.</p> : null}
      {rows.length > 0 ? (
        <div className="wf-runtime-input-list" role="list">
          {rows.map(row => (
            <InputInspectionRowCard
              key={row.rowKey}
              row={row}
              sourceAccess={executableNodeFacts?.authoredInputsAccess}
              expressionEditors={expressionEditors}
            />
          ))}
        </div>
      ) : null}
    </section>
  );
}

function InputInspectionRowCard({
  row,
  sourceAccess,
  expressionEditors
}: {
  row: InputInspectionRow;
  sourceAccess?: string | null;
  expressionEditors: StudioExpressionEditorContribution[];
}) {
  const latest = row.latestEvaluation;
  const sourceKind = row.authoredSource?.expressionType || row.compiledBinding?.source || "No source";
  const sourceProtected = isProtectedSourceAccess(sourceAccess) || isProtectedSourceAccess(row.authoredSource?.accessState ?? row.authoredSource?.access) || !!row.authoredSource?.isSensitive || !!row.compiledBinding?.isSensitive || !!latest?.isSensitive;
  const regionId = `input-inspection-${safeDomId(row.rowKey)}`;

  return (
    <details className="wf-runtime-input" role="listitem">
      <summary aria-controls={regionId}>
        <span>
          <strong>{row.name}</strong>
          <small>{row.declaredType || "Unknown type"}</small>
        </span>
        <span>
          <small>Evaluated at runtime</small>
          <code>{runtimeEvidencePreview(latest)}</code>
        </span>
        <span>
          <small>{sourceKind}</small>
          <code>{sourcePreview(row, sourceProtected, sourceAccess)}</code>
        </span>
      </summary>
      <div id={regionId} className="wf-runtime-input-content">
        {row.states.length > 0 ? (
          <p className="wf-instance-note">{row.states.map(formatInputInspectionState).join(" · ")}</p>
        ) : null}
        <section aria-label={`${row.name} runtime evidence`}>
          <h5>Evaluated at runtime</h5>
          {latest ? <RuntimeValueEvidenceCard snapshot={latest} listItem={false} /> : <p>No runtime evaluation was recorded.</p>}
          {row.evaluations.length > 1 ? <InputEvaluationHistory evaluations={row.evaluations} /> : null}
        </section>
        <section aria-label={`${row.name} authored source`}>
          <h5>Authored source</h5>
          <AuthoredInputSource
            row={row}
            sourceAccess={sourceAccess}
            protectedSource={sourceProtected}
            expressionEditors={expressionEditors}
          />
        </section>
      </div>
    </details>
  );
}

function InputEvaluationHistory({ evaluations }: { evaluations: ActivityExecutionInspectionValueSnapshot[] }) {
  return (
    <details>
      <summary>Evaluation history ({evaluations.length})</summary>
      <ol>
        {evaluations.map((evaluation, index) => (
          <li key={evaluation.evaluationId || `${evaluation.capturedAt}:${index}`}>
            <strong>{evaluationPhase(evaluation) ? formatCaptureMode(evaluationPhase(evaluation)!) : "Unknown phase"}</strong>
            {typeof evaluationSequence(evaluation) === "number" ? ` · sequence ${evaluationSequence(evaluation)}` : " · legacy sequence"}
            {` · ${formatDate(evaluation.capturedAt)}`}
            {evaluation.failure?.code ? ` · ${evaluation.failure.code}` : ""}
          </li>
        ))}
      </ol>
    </details>
  );
}

function AuthoredInputSource({
  row,
  sourceAccess,
  protectedSource,
  expressionEditors
}: {
  row: InputInspectionRow;
  sourceAccess?: string | null;
  protectedSource: boolean;
  expressionEditors: StudioExpressionEditorContribution[];
}) {
  if (protectedSource) {
    return <p>{isProtectedSourceAccess(sourceAccess) ? "Authored source is hidden by source permissions." : "Authored source is protected because this input is sensitive."}</p>;
  }

  const source = row.authoredSource;
  const expressionType = source?.expressionType || "Unknown";
  const fallback = source ? <GenericExpressionSource expressionType={expressionType} value={source.value} expanded /> : null;
  const renderer = source ? resolveExpressionSourceRenderer(expressionEditors, row, expressionType) : undefined;
  const Renderer = renderer?.sourceRenderer?.expanded;
  const rendererContext: StudioExpressionSourceRendererContext | undefined = source ? {
    expressionType,
    value: source.value,
    metadata: {},
    isSensitive: !!source.isSensitive,
    surface: "expanded"
  } : undefined;

  return (
    <>
      {source && Renderer && rendererContext ? (
        <ExpressionSourceRendererBoundary fallback={fallback}>
          <Renderer context={rendererContext} />
        </ExpressionSourceRendererBoundary>
      ) : fallback ?? <p>No authored source is available for this pinned Source Reference.</p>}
      {row.compiledBinding ? (
        <details>
          <summary>Compiled binding ({row.compiledBinding.source || "Unknown"})</summary>
          <GenericExpressionSource expressionType={row.compiledBinding.source || "Unknown"} value={compiledBindingDetails(row.compiledBinding)} expanded />
        </details>
      ) : <p className="wf-instance-note">No compiled binding is available.</p>}
    </>
  );
}

class ExpressionSourceRendererBoundary extends Component<{ children: ReactNode; fallback: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
}

function resolveExpressionSourceRenderer(
  expressionEditors: StudioExpressionEditorContribution[],
  row: InputInspectionRow,
  expressionType: string
) {
  const descriptor = row.declaration ?? {
    name: row.name,
    displayName: row.name,
    typeName: row.declaredType || "System.Object",
    referenceKey: row.inputKey
  };
  const context = {
    syntax: expressionType,
    surface: "expanded" as const,
    descriptor,
    activity: null,
    expressionDescriptors: [],
    readOnly: true
  };

  return expressionEditors.find(editor => {
    if (!editor.sourceRenderer) return false;
    try {
      return editor.supports(context);
    } catch {
      return false;
    }
  });
}

function GenericExpressionSource({ expressionType, value, expanded = false }: { expressionType: string; value: unknown; expanded?: boolean }) {
  const text = formatSnapshotPayload(value);
  const bounded = text.length > 4_000 ? `${text.slice(0, 3_997)}...` : text;
  return (
    <div>
      <small>{expressionType || "Unknown expression"}</small>
      {expanded && (bounded.includes("\n") || bounded.length > 160) ? <pre>{bounded}</pre> : <code>{bounded}</code>}
    </div>
  );
}

function compiledBindingDetails(binding: ExecutableGraphNodeFacts["inputBindings"][number]) {
  const { inputName: _inputName, inputKey: _inputKey, summary: _summary, ...details } = binding;
  return details;
}

function runtimeEvidencePreview(snapshot: ActivityExecutionInspectionValueSnapshot | undefined) {
  if (!snapshot) return "Not evaluated";
  if (snapshot.failure) return snapshot.failure.code || "Evaluation failed";
  const access = snapshot.accessState ?? snapshot.access;
  if (access && access.toLowerCase() !== "visible" && access.toLowerCase() !== "allowed") return formatCaptureMode(access);
  if (snapshot.isSensitive) return "Protected value";
  const node = snapshot.snapshot;
  if (node && isKnownSnapshotNode(node) && node.kind === "string") return previewSnapshotPayload(node.preview ?? "");
  if (node && isKnownSnapshotNode(node) && (node.kind === "scalar" || node.kind === "number")) return previewSnapshotPayload(formatSnapshotPayload(node.value));
  if (node) return formatSnapshotKind(node.kind);
  if (snapshot.captureMode === "Payload" && snapshot.payload !== undefined) return previewSnapshotPayload(formatSnapshotPayload(snapshot.payload));
  return snapshot.captureReason || formatCaptureMode(snapshot.state || snapshot.captureMode);
}

function sourcePreview(row: InputInspectionRow, protectedSource: boolean, sourceAccess?: string | null) {
  if (protectedSource) return isProtectedSourceAccess(sourceAccess) ? "Source hidden" : "Protected source";
  if (!row.authoredSource) return row.compiledBinding ? "Compiled source only" : "Source unavailable";
  return previewSnapshotPayload(formatSnapshotPayload(row.authoredSource.value));
}

function isProtectedSourceAccess(access: string | null | undefined) {
  if (!access) return false;
  const normalized = access.toLowerCase();
  return normalized !== "visible" && normalized !== "allowed";
}

function safeDomId(value: string) {
  return value.replace(/[^a-zA-Z0-9_-]/g, "-");
}

function formatInputInspectionState(state: InputInspectionState) {
  const labels: Record<InputInspectionState, string> = {
    missingDeclaration: "Declaration unavailable",
    missingAuthoredSource: "Authored source unavailable",
    missingCompiledBinding: "Compiled binding unavailable",
    noEvaluation: "Not evaluated",
    orphanEvidence: "Orphan runtime evidence",
    compiledOnly: "Compiled source only",
    sourceUnavailable: "Source unavailable",
    duplicateKey: "Duplicate input key",
    legacyIdentity: "Legacy name-only identity",
    redacted: "Redacted",
    permissionHidden: "Permission hidden",
    metadataOnly: "Metadata only",
    unavailable: "Unavailable",
    unsupported: "Unsupported snapshot",
    truncated: "Truncated snapshot",
    failedEvaluation: "Evaluation failed"
  };
  return labels[state];
}

function WorkflowActivityValueEvidence({
  state,
  subject,
  title,
  loadingText,
  failureText,
  emptyText
}: {
  state: ActivityExecutionInspectionState;
  subject: "ActivityInput" | "ActivityOutput";
  title: string;
  loadingText: string;
  failureText: string;
  emptyText: string;
}) {
  if (state.status === "idle") return null;

  if (state.status === "loading") {
    return (
      <section className="wf-instance-section">
        <h4>{title}</h4>
        <p>{loadingText}</p>
      </section>
    );
  }

  if (state.status === "failed") {
    return (
      <section className="wf-instance-section">
        <h4>{title}</h4>
        <p>{failureText}</p>
        {state.error ? <p className="wf-instance-note">{state.error}</p> : null}
      </section>
    );
  }

  const snapshots = (state.inspection?.valueSnapshots ?? []).filter(snapshot => snapshot.subject === subject);
  if (snapshots.length === 0) {
    return (
      <section className="wf-instance-section">
        <h4>{title}</h4>
        <p>{emptyText}</p>
      </section>
    );
  }

  const captureModes = [...new Set(snapshots.map(snapshot => snapshot.captureMode))];
  const captureModeLabel = captureModes.length === 1 ? formatCaptureMode(captureModes[0] ?? "Evidence") : "Mixed evidence";

  return (
    <section className="wf-instance-section">
      <header className="wf-runtime-evidence-heading">
        <h4>
          {title}
          <span className="wf-runtime-evidence-count" aria-label={`${snapshots.length} ${title.toLowerCase()}`}>
            {snapshots.length}
          </span>
        </h4>
        <span className="wf-runtime-capture-mode">{captureModeLabel}</span>
      </header>
      <div className="wf-runtime-input-list" role="list">
        {snapshots.map(snapshot => (
          <RuntimeValueEvidenceCard key={`${snapshot.name}:${snapshot.capturedAt}:${snapshot.captureMode}`} snapshot={snapshot} />
        ))}
      </div>
    </section>
  );
}

function RuntimeValueEvidenceCard({ snapshot, listItem = true }: { snapshot: ActivityExecutionInspectionValueSnapshot; listItem?: boolean }) {
  const typeName = snapshot.type?.displayName || snapshot.type?.typeName || snapshot.type?.alias || "Unknown";
  const diagnosticSnapshot = snapshot.snapshot ?? (snapshot.captureMode === "DiagnosticSnapshot" ? snapshot.payload : null);
  const hasPayload = snapshot.captureMode === "Payload" && snapshot.payload !== undefined;

  return (
    <article className="wf-runtime-input" role={listItem ? "listitem" : undefined}>
      <header>
        <span>
          <strong>{snapshot.name}</strong>
          <small>{typeName}</small>
        </span>
        <small>{formatCaptureMode(snapshot.captureMode)}</small>
      </header>
      <div className="wf-runtime-input-content">
        {snapshot.isSensitive && !isProtectedDiagnosticSnapshot(diagnosticSnapshot) ? (
          <p>Runtime value is protected because this input is sensitive.</p>
        ) : snapshot.failure ? (
          <p>{snapshot.failure.message || snapshot.failure.code || "The input could not be evaluated."}{snapshot.failure.incidentId ? ` Incident ${snapshot.failure.incidentId}.` : ""}</p>
        ) : isDiagnosticSnapshotNode(diagnosticSnapshot) ? (
          <DiagnosticSnapshotTree node={diagnosticSnapshot} />
        ) : hasPayload ? (
          <RuntimeInputPayload payload={snapshot.payload} />
        ) : (
          <p>{formatEvidenceMessage(snapshot)}</p>
        )}
      </div>
      {snapshot.isSensitive ? <p className="wf-instance-note">Marked sensitive by runtime evidence.</p> : null}
    </article>
  );
}

// `DiagnosticSnapshotUnknownNode` has `kind: string`, which defeats discriminated-union narrowing —
// route unknown kinds out first so the switch below narrows to the concrete node interfaces.
type KnownDiagnosticSnapshotNode = Exclude<DiagnosticSnapshotNode, DiagnosticSnapshotUnknownNode>;

// Record<K, true> makes the compiler reject a missing or misspelled kind when the union grows.
const knownSnapshotKinds: Record<KnownDiagnosticSnapshotNode["kind"], true> = {
  null: true, scalar: true, number: true, string: true, object: true, array: true,
  redacted: true, truncated: true, unsupported: true, error: true,
  permissionHidden: true, payloadReference: true
};

function isKnownSnapshotNode(node: DiagnosticSnapshotNode): node is KnownDiagnosticSnapshotNode {
  return Object.hasOwn(knownSnapshotKinds, node.kind);
}

function DiagnosticSnapshotTree({ node, depth = 0 }: { node: DiagnosticSnapshotNode; depth?: number }) {
  if (!isKnownSnapshotNode(node)) {
    return <DiagnosticSnapshotMarker node={{ kind: "unsupported", reason: `Unknown snapshot node: ${node.kind}` }} />;
  }

  switch (node.kind) {
    case "null":
      return <code className="wf-runtime-input-value">null</code>;
    case "scalar":
    case "number":
      return <code className="wf-runtime-input-value">{formatSnapshotPayload(node.value)}</code>;
    case "string":
      return (
        <code className="wf-runtime-input-value">
          {node.preview ?? ""}
          {node.truncated ? ` (${node.length ?? "unknown"} chars, truncated)` : ""}
        </code>
      );
    case "object":
      return <DiagnosticSnapshotObject node={node} depth={depth} />;
    case "array":
      return <DiagnosticSnapshotArray node={node} depth={depth} />;
    case "redacted":
    case "truncated":
    case "unsupported":
    case "error":
    case "permissionHidden":
      return <DiagnosticSnapshotMarker node={node} />;
    case "payloadReference":
      return <DiagnosticSnapshotReference node={node} />;
  }
}

function DiagnosticSnapshotObject({ node, depth }: { node: DiagnosticSnapshotObjectNode; depth: number }) {
  const properties = node.properties ?? [];
  if (properties.length === 0) return <code className="wf-runtime-input-value">{"{}"}</code>;

  return (
    <details className="wf-runtime-snapshot-node" open={depth === 0}>
      <summary>{node.typeName || "Object"}{node.truncated ? " (truncated)" : ""}</summary>
      <div className="wf-runtime-snapshot-children">
        {properties.map(property => (
          <div className="wf-runtime-snapshot-property" key={property.name}>
            <span>{property.name}</span>
            <DiagnosticSnapshotTree node={property.value} depth={depth + 1} />
          </div>
        ))}
      </div>
    </details>
  );
}

function DiagnosticSnapshotArray({ node, depth }: { node: DiagnosticSnapshotArrayNode; depth: number }) {
  const items = node.items ?? [];
  if (items.length === 0) return <code className="wf-runtime-input-value">[]</code>;

  return (
    <details className="wf-runtime-snapshot-node" open={depth === 0}>
      <summary>Array ({node.itemCount ?? items.length}){node.truncated ? " (truncated)" : ""}</summary>
      <div className="wf-runtime-snapshot-children">
        {items.map((item, index) => (
          <div className="wf-runtime-snapshot-property" key={index}>
            <span>{index}</span>
            <DiagnosticSnapshotTree node={item} depth={depth + 1} />
          </div>
        ))}
      </div>
    </details>
  );
}

function DiagnosticSnapshotMarker({ node }: { node: Pick<DiagnosticSnapshotNode, "kind" | "displayName"> & { reason?: string | null; omittedCount?: number | null; message?: string | null; requiredPermission?: string | null } }) {
  const reason = node.message || node.reason || node.requiredPermission || node.displayName;
  return (
    <span className={`wf-runtime-snapshot-marker ${node.kind}`}>
      {formatSnapshotKind(node.kind)}
      {reason ? `: ${reason}` : ""}
      {node.omittedCount ? ` (${node.omittedCount} omitted)` : ""}
    </span>
  );
}

function DiagnosticSnapshotReference({ node }: { node: DiagnosticSnapshotPayloadReferenceNode }) {
  const label = node.displayName || node.referenceKind || "Referenced payload";
  const resolutionReason = node.resolution?.reason || "Reference resolution is not available.";
  return (
    <div className="wf-runtime-snapshot-reference">
      <strong>{label}</strong>
      {node.contentType ? <small>{node.contentType}</small> : null}
      {typeof node.size === "number" ? <small>{node.size} bytes</small> : null}
      <span>{resolutionReason}</span>
    </div>
  );
}

function RuntimeInputPayload({ payload }: { payload: unknown }) {
  const text = formatSnapshotPayload(payload);
  const compact = text.length <= 160 && !text.includes("\n");

  return compact ? (
    <code className="wf-runtime-input-value">{text}</code>
  ) : (
    <details className="wf-runtime-input-value-details">
      <summary>{previewSnapshotPayload(text)}</summary>
      <pre>{text}</pre>
    </details>
  );
}

function formatCaptureMode(mode: string) {
  return mode.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function formatSnapshotKind(kind: string) {
  return kind.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function formatEvidenceMessage(snapshot: ActivityExecutionInspectionValueSnapshot) {
  const access = snapshot.accessState ?? snapshot.access;
  if (access === "redacted") return "Runtime value evidence is redacted.";
  if (access === "unavailable") return "Runtime value evidence is unavailable.";
  if (access === "permissionHidden" || access === "permission-hidden") return "Runtime value evidence is hidden by permissions.";
  if (snapshot.state === "permissionHidden") return "Runtime value evidence is hidden by permissions.";
  if (snapshot.state === "metadataOnly") return snapshot.captureReason || "Runtime value evidence is metadata-only.";
  if (snapshot.state === "notCaptured") return snapshot.captureReason || "Runtime value evidence was not captured.";
  return snapshot.captureReason || "The runtime capture policy did not include this value.";
}

function isProtectedDiagnosticSnapshot(value: unknown) {
  return isDiagnosticSnapshotNode(value) && (value.kind === "redacted" || value.kind === "permissionHidden");
}

function isDiagnosticSnapshotNode(value: unknown): value is DiagnosticSnapshotNode {
  return typeof value === "object" && value !== null && typeof (value as { kind?: unknown }).kind === "string";
}

function previewSnapshotPayload(text: string) {
  const firstLine = text.split("\n", 1)[0] || text;
  return firstLine.length > 120 ? `${firstLine.slice(0, 117)}...` : firstLine;
}

export function formatSnapshotPayload(payload: unknown) {
  if (payload === null) return "null";
  if (payload === undefined) return "undefined";
  if (typeof payload === "string") return payload;
  if (typeof payload === "number" || typeof payload === "boolean" || typeof payload === "bigint") return String(payload);

  try {
    return JSON.stringify(payload, null, 2);
  } catch {
    return String(payload);
  }
}

const incidentStackTraceMetadataKeys = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];

export function WorkflowIncidentList({ incidents, selectedEvidenceId = null, onSelectEvidence }: {
  incidents: IncidentStateSummary[];
  selectedEvidenceId?: string | null;
  onSelectEvidence?(evidenceId: string): void;
}) {
  const [copyStatus, setCopyStatus] = useState("");

  return (
    <section className="wf-instance-section">
      <h4>Incidents</h4>
      {incidents.length === 0 ? <p>No incidents recorded.</p> : null}
      {incidents.map(incident => (
        <article
          className="wf-instance-incident"
          data-severity={incident.severity.toLowerCase()}
          data-selected={incident.incidentId === selectedEvidenceId}
          key={incident.incidentId}
        >
          <button
            type="button"
            className="wf-instance-incident-summary"
            aria-label={`Select incident ${incident.failureType}`}
            onClick={() => onSelectEvidence?.(incident.incidentId)}
          >
            <strong>{incident.failureType}</strong>
            <span>{incident.status} · {incident.severity}</span>
            <p>{incident.message}</p>
          </button>
          <CopyValueButton
            value={formatIncidentForClipboard(incident)}
            ariaLabel={`Copy incident ${incident.failureType}`}
            copiedLabel="incident"
            onCopied={label => setCopyStatus(`Copied ${label}`)}
            onCopyFailed={label => setCopyStatus(`Could not copy ${label}.`)}
          />
          <IncidentStackTrace incident={incident} />
        </article>
      ))}
      {copyStatus ? <p className="wf-copy-status" role="status">{copyStatus}</p> : null}
    </section>
  );
}

function IncidentStackTrace({ incident }: { incident: IncidentStateSummary }) {
  const stackTrace = getIncidentStackTrace(incident);
  if (!stackTrace) return null;

  return (
    <details className="wf-incident-stacktrace">
      <summary>{previewStackTrace(stackTrace)}</summary>
      <pre>{stackTrace}</pre>
    </details>
  );
}

export function getIncidentStackTrace(incident: IncidentStateSummary) {
  const directStackTrace = firstNonBlank(incident.stackTrace, incident.exceptionStackTrace);
  if (directStackTrace) return directStackTrace;

  for (const key of incidentStackTraceMetadataKeys) {
    const value = incident.metadata?.[key];
    if (value && value.trim()) return value;
  }

  return null;
}

function firstNonBlank(...values: Array<string | null | undefined>) {
  return values.find(value => value?.trim()) ?? null;
}

function previewStackTrace(stackTrace: string) {
  const firstMeaningfulLine = stackTrace.split("\n").find(line => line.trim()) ?? stackTrace;
  const preview = firstMeaningfulLine.trim();
  return preview.length > 120 ? `${preview.slice(0, 117)}...` : preview;
}

function WorkflowUnmatchedEvidence({ details, graphNodeIds, rootNodeId }: { details: WorkflowInstanceDetails; graphNodeIds?: Set<string>; rootNodeId?: string | null }) {
  if (!graphNodeIds) return null;

  const unmatchedActivities = details.activities.filter(activity => {
    const nodeId = activityNodeKey(activity);
    return nodeId && nodeId !== rootNodeId && !graphNodeIds.has(nodeId);
  });

  if (unmatchedActivities.length === 0) return null;

  return (
    <section className="wf-instance-section">
      <h4>Executions outside canvas</h4>
      <div className="wf-instance-unmatched-list">
        {unmatchedActivities.map(activity => (
          <div className="wf-instance-unmatched" key={`activity-${activity.activityExecutionId}`}>
            <strong>{shortTypeName(activity.activityType) ?? activity.activityType}</strong>
            <small>{activity.activityExecutionId}</small>
          </div>
        ))}
      </div>
    </section>
  );
}

function formatIncidentForClipboard(incident: IncidentStateSummary) {
  const lines = [
    `Incident: ${incident.failureType}`,
    `Incident ID: ${incident.incidentId}`,
    `Workflow execution ID: ${incident.workflowExecutionId}`,
    incident.activityExecutionId ? `Activity execution ID: ${incident.activityExecutionId}` : "",
    incident.executableNodeId ? `Executable node ID: ${incident.executableNodeId}` : "",
    `Status: ${incident.status}`,
    `Severity: ${incident.severity}`,
    `Blocking: ${incident.isBlocking ? "Yes" : "No"}`,
    incident.resolutionAction ? `Resolution action: ${incident.resolutionAction}` : "",
    `Created: ${formatDate(incident.createdAt)}`,
    incident.resolvedAt ? `Resolved: ${formatDate(incident.resolvedAt)}` : "",
    "",
    incident.message
  ].filter(Boolean);

  if (incident.metadata && Object.keys(incident.metadata).length > 0) {
    lines.push("", "Metadata:", JSON.stringify(incident.metadata, null, 2));
  }

  return lines.join("\n");
}
