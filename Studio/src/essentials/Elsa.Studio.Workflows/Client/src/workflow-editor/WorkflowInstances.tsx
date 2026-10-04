import "./activityInspection.css";
import { Component, lazy, Suspense, useCallback, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from "react";
import { ReactFlow, Background, Controls, MiniMap, type Edge, type Node } from "@xyflow/react";
import { Activity as ActivityIcon, AlertCircle, Boxes, ChevronLeft, ChevronRight, ListTree, Maximize2, Minimize2, RotateCcw, SlidersHorizontal, Sparkles, Workflow as WorkflowIcon } from "lucide-react";
import type { StudioActivityInputDescriptor, StudioAiContributionApi, StudioEndpointContext, StudioExpressionEditorContribution, StudioExpressionSourceRendererContext } from "@elsa-workflows/studio-sdk";
import { listActivities } from "../api/activityDesign";
import { getActivityExecutionInspection, getExecutable, getExecutableInputSources, getWorkflowInstance, listWorkflowInstances, type WorkflowInstanceListPage } from "../api/runtime";
import type { ActivityCatalogItem, ActivityExecutionInspection, ActivityExecutionInspectionValueSnapshot, ActivityExecutionStateSummary, ActivityNode, IncidentStateSummary, WorkflowDefinitionVersionDetails, WorkflowExecutableDetails, WorkflowInstanceDetails, WorkflowInstanceSummary } from "../workflowTypes";
import { formatActivitySummary } from "../activitySummary";
import { isMaskedInput, readSecretReference } from "../maskedInput";
import { resolveActivityLabel } from "../activityPresentation";
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
import {
  formatCaptureMode,
  formatSnapshotPayload,
  runtimeValueContextId,
  RuntimeValueEvidenceCard,
  RuntimeValueEvidenceContent,
  RuntimeValueEvidenceResolutionProvider
} from "./RuntimeValueEvidence";
export { formatSnapshotPayload } from "./RuntimeValueEvidence";
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
            sourceResult.access,
            executableResult.executable.chosenReference?.activityPresentation ?? [])
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
  const graph = executableGraph ?? buildExecutableActivityGraph(
    executable.rootActivity,
    catalog,
    [],
    [],
    null,
    executable.chosenReference?.activityPresentation ?? []);
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
    state: { rootActivity: graph.root },
    layout: executable.chosenReference?.layout ?? [],
    activityPresentation: graph.activityPresentation
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
    ? buildUnsupportedActivityCanvas(
        scopeOwner,
        activityCatalog,
        definitionVersion.layout,
        formatActivitySummary,
        definitionVersion.activityPresentation)
    : buildCanvas(
        scope,
        activityCatalog,
        definitionVersion.layout,
        formatActivitySummary,
        definitionVersion.activityPresentation);
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
                  executableGraph={executableGraph}
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
  workflowExecutionId: string | null;
  contextIdentity: number | string | null;
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
  const selectedContextIdentity = runtimeValueContextId(context);
  const [inspectionState, setInspectionState] = useState<ActivityExecutionInspectionState>({
    activityExecutionId: null,
    workflowExecutionId: null,
    contextIdentity: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  const [copyStatus, setCopyStatus] = useState("");

  useEffect(() => {
    setCopyStatus("");
    if (!selectedActivityExecutionId || !selectedWorkflowExecutionId) {
      setInspectionState({ activityExecutionId: null, workflowExecutionId: null, contextIdentity: selectedContextIdentity, status: "idle", inspection: null, error: "" });
      return;
    }

    const controller = new AbortController();
    const activityExecutionId = selectedActivityExecutionId;
    const workflowExecutionId = selectedWorkflowExecutionId;
    setInspectionState({ activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity, status: "loading", inspection: null, error: "" });

    getActivityExecutionInspection(context, workflowExecutionId, activityExecutionId, controller.signal).then(
      inspection => {
        if (!controller.signal.aborted) setInspectionState({ activityExecutionId, workflowExecutionId, contextIdentity: selectedContextIdentity, status: "ready", inspection, error: "" });
      },
      error => {
        if (!controller.signal.aborted) {
          setInspectionState({
            activityExecutionId,
            workflowExecutionId,
            contextIdentity: selectedContextIdentity,
            status: "failed",
            inspection: null,
            error: error instanceof Error ? error.message : String(error)
          });
        }
      }
    );

    return () => controller.abort();
  }, [context, selectedActivityExecutionId, selectedContextIdentity, selectedWorkflowExecutionId]);

  const currentInspectionState: ActivityExecutionInspectionState =
    inspectionState.activityExecutionId === selectedActivityExecutionId &&
    inspectionState.workflowExecutionId === selectedWorkflowExecutionId &&
    inspectionState.contextIdentity === selectedContextIdentity
      ? inspectionState
      : selectedActivityExecutionId && selectedWorkflowExecutionId
        ? { activityExecutionId: selectedActivityExecutionId, workflowExecutionId: selectedWorkflowExecutionId, contextIdentity: selectedContextIdentity, status: "loading", inspection: null, error: "" }
        : { activityExecutionId: null, workflowExecutionId: null, contextIdentity: selectedContextIdentity, status: "idle", inspection: null, error: "" };
  const resolutionScope = useMemo(
    () => selectedActivityExecutionId && selectedWorkflowExecutionId
      ? { context, activityExecutionId: selectedActivityExecutionId, workflowExecutionId: selectedWorkflowExecutionId }
      : null,
    [context, selectedActivityExecutionId, selectedWorkflowExecutionId]);

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
  const activityLabel = resolveActivityLabel(
    executableNodeFacts?.presentation,
    catalogItem,
    activity.activityType);
  const activityDescription = executableNodeFacts?.presentation?.description?.trim();
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
            {activityDescription
              ? <p className="wf-activity-overview-description">{activityDescription}</p>
              : null}
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
      {currentInspectionState.status === "ready" && currentInspectionState.inspection?.boundary ? (
        <Suspense fallback={<section className="wf-instance-section" role="status">Loading reusable boundary inspector...</section>}>
          <ReusableBoundaryInspector context={context} inspection={currentInspectionState.inspection} />
        </Suspense>
      ) : null}
      <RuntimeValueEvidenceResolutionProvider scope={resolutionScope}>
        <WorkflowActivityInputEvidence
          state={currentInspectionState}
          declarations={declaredInputs}
          executableNodeFacts={executableNodeFacts}
          expressionEditors={expressionEditors}
        />
        <WorkflowActivityValueEvidence
          state={currentInspectionState}
          subject="ActivityOutput"
          title="Outputs"
          loadingText="Loading runtime output evidence..."
          failureText="Runtime output evidence is unavailable."
          emptyText="No runtime output snapshots were recorded for this execution."
        />
      </RuntimeValueEvidenceResolutionProvider>
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
        <span className="wf-input-inspection-caption">Evaluated at runtime</span>
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
        <div className="wf-runtime-input-list wf-input-inspection-list" role="list">
          {rows.map(row => (
            <InputInspectionRowCard
              key={row.rowKey}
              row={row}
              sourceAccess={executableNodeFacts?.authoredInputsAccess}
              expressionEditors={expressionEditors}
              inspectionStatus={state.status}
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
  expressionEditors,
  inspectionStatus
}: {
  row: InputInspectionRow;
  sourceAccess?: string | null;
  expressionEditors: StudioExpressionEditorContribution[];
  inspectionStatus: ActivityExecutionInspectionState["status"];
}) {
  const latest = row.latestEvaluation;
  const sourceKind = row.authoredSource?.expressionType || row.compiledBinding?.source || "No source";
  // The input's declaration counts as much as the per-record flags: a masked input (a password hint, or declared
  // sensitive or secret-only) shows nothing of its authored source but the name of a Secret Reference it is bound
  // to, even when the backend did not flag the record. Any other source stays hidden, and so does a masked input's
  // compiled binding, whose fields Studio cannot vet. A record the backend flagged stays hidden.
  const masked = !!row.declaration && isMaskedInput(row.declaration);
  const secretReference = masked ? readSecretReference(row.authoredSource?.expressionType, row.authoredSource?.value) : null;
  const shownSource = secretReference && row.authoredSource ? { ...row.authoredSource, value: secretReference.name } : row.authoredSource;
  const sourceProtected = isProtectedSourceAccess(sourceAccess) || isProtectedSourceAccess(row.authoredSource?.accessState ?? row.authoredSource?.access) || !!row.authoredSource?.isSensitive || !!row.compiledBinding?.isSensitive || !!latest?.isSensitive
    || (masked && !secretReference);
  const regionId = useId();
  const sensitiveInput = masked || row.authoredSource?.isSensitive || row.compiledBinding?.isSensitive;
  const runtimeSnapshot = latest && sensitiveInput && !latest.isSensitive ? { ...latest, isSensitive: true } : latest;

  return (
    <div className="wf-runtime-input wf-input-inspection-row" role="listitem">
      <div className="wf-input-inspection-value-row">
        <div className="wf-input-inspection-identity">
          <strong className="wf-input-inspection-name">{row.name}</strong>
          <small className="wf-input-inspection-type" title={row.declaredType}>{shortTypeName(row.declaredType) || "Unknown type"}</small>
        </div>
        <div className="wf-input-inspection-value" aria-label={`${row.name} runtime value`}>
          {runtimeSnapshot ? <RuntimeValueEvidenceContent snapshot={runtimeSnapshot} presentation="input" /> : (
            <p className="wf-input-inspection-unavailable">
              {inspectionStatus === "loading" ? "Loading captured value…" : inspectionStatus === "failed" ? "Runtime evidence unavailable" : "Not evaluated"}
            </p>
          )}
        </div>
      </div>
      <details className="wf-input-inspection-disclosure">
        <summary className="wf-input-inspection-summary" aria-controls={regionId}>
          <ChevronRight size={14} aria-hidden="true" />
          <span>Source &amp; diagnostics</span>
          <small>{sourceProtected ? (isProtectedSourceAccess(sourceAccess) ? "Source hidden" : "Protected source") : sourceKind}</small>
        </summary>
        <div id={regionId} className="wf-input-inspection-content">
          <section className="wf-input-inspection-detail wf-input-inspection-source" aria-label={`${row.name} authored source`}>
            <h5>Authored source</h5>
            <AuthoredInputSource
              row={row}
              source={shownSource}
              compiledBindingShown={!masked}
              sourceAccess={sourceAccess}
              protectedSource={sourceProtected}
              expressionEditors={expressionEditors}
            />
          </section>
          <section className="wf-input-inspection-detail" aria-label={`${row.name} runtime evidence`}>
            <h5>Capture details</h5>
            {latest ? (
              <dl className="wf-input-inspection-metadata">
                {[
                  ["Type", row.declaredType || "Unknown type"],
                  ["Capture", formatCaptureMode(latest.captureMode)],
                  ["Captured", formatDate(latest.capturedAt)],
                  ...(evaluationPhase(latest) ? [["Phase", formatCaptureMode(evaluationPhase(latest)!)]] : []),
                  ...(typeof evaluationSequence(latest) === "number" ? [["Sequence", String(evaluationSequence(latest))]] : [])
                ].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
              </dl>
            ) : <p>No runtime evaluation was recorded.</p>}
            {row.states.length > 0 ? <p className="wf-instance-note">{row.states.map(formatInputInspectionState).join(" · ")}</p> : null}
            {row.evaluations.length > 1 ? <InputEvaluationHistory evaluations={row.evaluations} /> : null}
          </section>
        </div>
      </details>
    </div>
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
  source,
  compiledBindingShown,
  sourceAccess,
  protectedSource,
  expressionEditors
}: {
  row: InputInspectionRow;
  source: InputInspectionRow["authoredSource"];
  compiledBindingShown: boolean;
  sourceAccess?: string | null;
  protectedSource: boolean;
  expressionEditors: StudioExpressionEditorContribution[];
}) {
  if (protectedSource) {
    return <p>{isProtectedSourceAccess(sourceAccess) ? "Authored source is hidden by source permissions." : "Authored source is protected because this input is sensitive."}</p>;
  }

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
      {!row.compiledBinding ? (
        <p className="wf-instance-note">No compiled binding is available.</p>
      ) : compiledBindingShown ? (
        <details>
          <summary>Compiled binding ({row.compiledBinding.source || "Unknown"})</summary>
          <GenericExpressionSource expressionType={row.compiledBinding.source || "Unknown"} value={compiledBindingDetails(row.compiledBinding)} expanded />
        </details>
      ) : <p className="wf-instance-note">The compiled binding of a masked input is not shown.</p>}
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
    <div className="wf-input-inspection-expression">
      <small>{expressionType || "Unknown expression"}</small>
      {expanded && (bounded.includes("\n") || bounded.length > 160) ? <pre>{bounded}</pre> : <code>{bounded}</code>}
    </div>
  );
}

function compiledBindingDetails(binding: ExecutableGraphNodeFacts["inputBindings"][number]) {
  const { inputName: _inputName, inputKey: _inputKey, summary: _summary, ...details } = binding;
  return details;
}

function isProtectedSourceAccess(access: string | null | undefined) {
  if (!access) return false;
  const normalized = access.toLowerCase();
  return normalized !== "visible" && normalized !== "allowed";
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
        {snapshots.map((snapshot, index) => (
          <RuntimeValueEvidenceCard key={snapshot.evidenceId || `${snapshot.name}:${snapshot.capturedAt}:${snapshot.captureMode}:${index}`} snapshot={snapshot} />
        ))}
      </div>
    </section>
  );
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
