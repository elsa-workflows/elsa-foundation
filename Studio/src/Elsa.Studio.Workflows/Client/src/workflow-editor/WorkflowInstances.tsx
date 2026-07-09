import { useCallback, useEffect, useMemo, useState } from "react";
import { ReactFlow, Background, Controls, MiniMap, type Edge, type Node } from "@xyflow/react";
import { Activity as ActivityIcon, AlertCircle, Boxes, ChevronLeft, ChevronRight, ListTree, Maximize2, Minimize2, RotateCcw, SlidersHorizontal, Sparkles, Workflow as WorkflowIcon } from "lucide-react";
import type { StudioAiContributionApi, StudioAiPromptActionContribution, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getActivityExecutionInspection, getWorkflowDefinitionVersion, getWorkflowInstance, listActivities, listWorkflowInstances } from "../api/workflows";
import type { ActivityCatalogItem, ActivityExecutionInspection, ActivityExecutionInspectionValueSnapshot, ActivityExecutionStateSummary, ActivityNode, IncidentStateSummary, WorkflowDefinitionVersionDetails, WorkflowInstanceDetails, WorkflowInstanceSummary } from "../workflowTypes";
import {
  applyRuntimeOverlays,
  buildCanvas,
  buildUnsupportedActivityCanvas,
  getActivityDesignerSupport,
  getChildSlots,
  latestActivityExecution,
  resolveScope,
  type ChildSlot,
  type ScopeFrame,
  type WorkflowEdgeData,
  type WorkflowNodeData
} from "../workflowAdapter";
import { formatDate, formatDuration, shortTypeName } from "../workflowFormatting";
import { WorkflowExecutionTimeline } from "../WorkflowInstanceTimeline";
import { WfEmptyState, WfErrorCard, WfListSkeleton } from "./StatusViews";
import { PanelTabList } from "./PanelTabList";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import { nodeTypes, edgeTypes } from "./graph";
import { CopyValueButton } from "./executableShared";
import type { InstanceInspectorTab, WorkflowEditorPanelTab, WorkflowInstanceInspectionData } from "./editorTypes";
import {
  activityNodeKey,
  dispatchAiAction,
  findAiAction,
  formatRunKind,
  formatWorkflowRunLoadError,
  formatWorkflowVersionLoadError
} from "./editorHelpers";
import { useSidePanelLayout } from "./useSidePanelLayout";
import { maxInspectorWidth, minInspectorWidth } from "./constants";

export function WorkflowInstances({ context }: { context: StudioEndpointContext }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [runKindFilter, setRunKindFilter] = useState("");
  const [instances, setInstances] = useState<WorkflowInstanceSummary[]>([]);

  const load = useCallback(async () => {
    setState("loading");
    setError("");
    try {
      const nextInstances = await listWorkflowInstances(context, {
        status: statusFilter || undefined,
        runKind: runKindFilter || undefined,
        take: 100
      });
      setInstances(nextInstances);
      setState("ready");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setInstances([]);
      setState("failed");
    }
  }, [context, runKindFilter, statusFilter]);

  useEffect(() => {
    void load();
  }, [load]);

  const openInstance = (workflowExecutionId: string) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(workflowExecutionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return (
    <>
      <div className="wf-toolbar">
        <button type="button" onClick={() => void load()}>Refresh</button>
        <label className="wf-toolbar-field">
          <span>Status</span>
          <select aria-label="Workflow run status" value={statusFilter} onChange={event => setStatusFilter(event.target.value)}>
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
          <select aria-label="Run Kind" value={runKindFilter} onChange={event => setRunKindFilter(event.target.value)}>
            <option value="">All kinds</option>
            <option value="TestRun">Test Run</option>
            <option value="PublishedRun">Published Run</option>
            <option value="BackgroundWeaverRun">Background Weaver Run</option>
            <option value="Unknown">Unknown / legacy</option>
          </select>
        </label>
      </div>
      {state === "failed" ? <WfErrorCard message={error} /> : null}
      {state === "loading" ? <WfListSkeleton /> : null}
      {state === "ready" && instances.length === 0 ? (
        <WfEmptyState
          icon={<Boxes size={22} />}
          title="No workflow runs yet"
          description="Run a published workflow executable to create execution history here."
        />
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
    </>
  );
}

export function WorkflowInstanceDetailsWorkbench({ context, ai, workflowExecutionId }: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  workflowExecutionId: string;
}) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [data, setData] = useState<WorkflowInstanceInspectionData | null>(null);
  const [selectedEvidenceId, setSelectedEvidenceId] = useState<string | null>(null);
  const [frames, setFrames] = useState<ScopeFrame[]>([]);
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
      const [definitionVersionResult, activityCatalog] = await Promise.all([
        getWorkflowDefinitionVersion(context, details.instance.definitionVersionId).then(
          definitionVersion => ({ definitionVersion, error: "" }),
          error => ({ definitionVersion: null, error: error instanceof Error ? error.message : String(error) })
        ),
        listActivities(context)
      ]);
      setData({
        details,
        definitionVersion: definitionVersionResult.definitionVersion,
        definitionVersionError: definitionVersionResult.error,
        activityCatalog: activityCatalog.activities
      });
      setSelectedEvidenceId(null);
      setFrames([]);
      setState("ready");
    } catch (e) {
      setData(null);
      setError(formatWorkflowRunLoadError(e, workflowExecutionId));
      setState("failed");
    }
  }, [context, workflowExecutionId]);

  useEffect(() => {
    void load();
  }, [load]);

  const goBack = () => {
    window.history.pushState({}, "", "/workflows/instances");
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const openDefinitionDesigner = () => {
    const definitionId = data?.details.instance.definitionId;
    if (!definitionId) return;

    window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(definitionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };
  const detailWorkbenchClassName = [
    "wf-instance-detail-workbench",
    inspectorCollapsed ? "inspector-collapsed" : "",
    maximizedSidePanel === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");

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
        <div className={detailWorkbenchClassName} style={editorBodyStyle}>
          <WorkflowInstanceCanvas
            definitionVersion={data.definitionVersion}
            definitionVersionError={data.definitionVersionError}
            activityCatalog={data.activityCatalog}
            details={data.details}
            selectedEvidenceId={selectedEvidenceId}
            onSelectEvidence={setSelectedEvidenceId}
            frames={frames}
            onNavigateToScope={setFrames}
          />
          {inspectorExpanded && !maximizedSidePanel ? (
            <div
              className="wf-side-resize-handle right"
              role="separator"
              aria-label="Resize run details panel"
              aria-orientation="vertical"
              aria-valuemin={minInspectorWidth}
              aria-valuemax={maxInspectorWidth}
              aria-valuenow={inspectorWidth}
              tabIndex={0}
              onPointerDown={event => startSidePanelResize("inspector", event)}
              onKeyDown={event => handleSidePanelResizeKeyDown("inspector", event)}
            />
          ) : <div className="wf-side-resize-spacer" />}
          <WorkflowInstanceInspector
            context={context}
            ai={ai}
            action={instanceAction ?? undefined}
            summary={data.details.instance}
            details={data.details}
            state="ready"
            error=""
            selectedEvidenceId={selectedEvidenceId}
            onSelectEvidence={setSelectedEvidenceId}
            activityCatalog={data.activityCatalog}
            graphNodeIds={data.definitionVersion ? collectWorkflowGraphNodeIds(data.definitionVersion, data.activityCatalog) : undefined}
            rootNodeId={data.definitionVersion?.state.rootActivity?.nodeId}
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

function WorkflowInstanceCanvas({
  definitionVersion,
  definitionVersionError,
  activityCatalog,
  details,
  selectedEvidenceId,
  onSelectEvidence,
  frames,
  onNavigateToScope
}: {
  definitionVersion: WorkflowDefinitionVersionDetails | null;
  definitionVersionError: string;
  activityCatalog: ActivityCatalogItem[];
  details: WorkflowInstanceDetails;
  selectedEvidenceId: string | null;
  onSelectEvidence(evidenceId: string | null): void;
  frames: ScopeFrame[];
  onNavigateToScope(frames: ScopeFrame[]): void;
}) {
  const scopeKey = getInstanceScopeKey(frames);
  const canvas = useMemo(() => {
    if (!definitionVersion) return { nodes: [] as Node<WorkflowNodeData>[], edges: [] as Edge<WorkflowEdgeData>[] };

    const root = definitionVersion.state.rootActivity;
    if (!root) return { nodes: [] as Node<WorkflowNodeData>[], edges: [] as Edge<WorkflowEdgeData>[] };

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
          onNavigateToScope([...frames, {
            ownerNodeId: node.id,
            slotId: slot.id,
            label: `${node.data.label} / ${slot.label}`
          }]);
        }
      }
    }));

    return {
      nodes: applyRuntimeOverlays(readonlyNodes, details.activities, details.incidents, selectedEvidenceId),
      edges: baseCanvas.edges.map(edge => ({ ...edge, deletable: false }))
    };
  }, [activityCatalog, definitionVersion, details, frames, onNavigateToScope, selectedEvidenceId]);

  return (
    <section className="wf-instance-canvas-shell" aria-label="Workflow run canvas">
      <header>
        <div>
          <span>Definition version</span>
          <h3>{definitionVersion ? (
            <>
              {definitionVersion.definition.name} <small>{definitionVersion.version}</small>
            </>
          ) : (
            <>
              Definition graph unavailable <small>{details.instance.definitionVersionId}</small>
            </>
          )}</h3>
        </div>
        <WorkflowStatusBadge status={details.instance.status} subStatus={details.instance.subStatus} />
      </header>
      {definitionVersion ? (
        <div className="wf-breadcrumb wf-instance-breadcrumb">
          <button type="button" onClick={() => onNavigateToScope([])}>Root</button>
          {frames.map((frame, index) => (
            <span className="wf-breadcrumb-segment" key={`${frame.ownerNodeId}-${frame.slotId}-${index}`}>
              <ChevronRight size={13} />
              <button type="button" onClick={() => onNavigateToScope(frames.slice(0, index + 1))}>{frame.label}</button>
            </span>
          ))}
        </div>
      ) : null}
      <div className="wf-instance-canvas">
        {!definitionVersion ? (
          <div className="wf-empty">
            The workflow run loaded, but its definition graph could not be resolved for this version.
            {definitionVersionError ? <small>{formatWorkflowVersionLoadError(definitionVersionError)}</small> : null}
          </div>
        ) : null}
        {definitionVersion && canvas.nodes.length === 0 ? <div className="wf-empty">No workflow activities are available for this definition version.</div> : null}
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
  ai,
  action,
  summary,
  details,
  state,
  error,
  selectedEvidenceId = null,
  onSelectEvidence,
  graphNodeIds,
  rootNodeId,
  activityCatalog = [],
  collapsed = false,
  expanded = true,
  maximized = false,
  onToggleCollapsed,
  onToggleMaximized
}: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  action: StudioAiPromptActionContribution | undefined;
  summary: WorkflowInstanceSummary | null;
  details: WorkflowInstanceDetails | null;
  state: "idle" | "loading" | "ready" | "failed";
  error: string;
  selectedEvidenceId?: string | null;
  onSelectEvidence?(evidenceId: string): void;
  graphNodeIds?: Set<string>;
  rootNodeId?: string | null;
  activityCatalog?: ActivityCatalogItem[];
  collapsed?: boolean;
  expanded?: boolean;
  maximized?: boolean;
  onToggleCollapsed?(): void;
  onToggleMaximized?(): void;
}) {
  const [activeTab, setActiveTab] = useState<InstanceInspectorTab>("timeline");

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
    <aside className="wf-instance-inspector" aria-label="Run details panel">
      <div className="wf-panel-title wf-instance-panel-title">
        <PanelTabList label="Run details tabs" tabs={tabs} activeTabId={activeTab} onSelect={tabId => setActiveTab(tabId as InstanceInspectorTab)} />
        <span className="wf-panel-actions">
          <button
            type="button"
            className="wf-panel-action-button"
            aria-label={collapsed ? "Expand run details panel" : "Collapse run details panel"}
            title={collapsed ? "Expand" : "Collapse"}
            onClick={onToggleCollapsed}
          >
            {collapsed ? <ChevronLeft size={14} /> : <ChevronRight size={14} />}
          </button>
          {!collapsed ? (
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
        <header>
        <div>
          <span>Workflow Instance ID</span>
          <h3>{summary.workflowExecutionId}</h3>
        </div>
        {action ? (
          <button type="button" onClick={() => dispatchAiAction(ai, action, details ?? summary)}>
            <Sparkles size={13} /> Explain
          </button>
        ) : null}
      </header>
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
            <WorkflowActivityExecutionDetails context={context} activity={selectedActivity} activityCatalog={activityCatalog} />
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

type ActivityExecutionInspectionState = {
  activityExecutionId: string | null;
  status: "idle" | "loading" | "ready" | "failed";
  inspection: ActivityExecutionInspection | null;
  error: string;
};

export function WorkflowActivityExecutionDetails({ context, activity, activityCatalog }: {
  context: StudioEndpointContext;
  activity: ActivityExecutionStateSummary | null;
  activityCatalog: ActivityCatalogItem[];
}) {
  const selectedActivityExecutionId = activity?.activityExecutionId ?? null;
  const selectedWorkflowExecutionId = activity?.workflowExecutionId ?? null;
  const [inspectionState, setInspectionState] = useState<ActivityExecutionInspectionState>({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });

  useEffect(() => {
    if (!selectedActivityExecutionId || !selectedWorkflowExecutionId) {
      setInspectionState({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }

    let cancelled = false;
    const activityExecutionId = selectedActivityExecutionId;
    setInspectionState({ activityExecutionId, status: "loading", inspection: null, error: "" });

    getActivityExecutionInspection(context, selectedWorkflowExecutionId, activityExecutionId).then(
      inspection => {
        if (!cancelled) setInspectionState({ activityExecutionId, status: "ready", inspection, error: "" });
      },
      error => {
        if (!cancelled) {
          setInspectionState({
            activityExecutionId,
            status: "failed",
            inspection: null,
            error: error instanceof Error ? error.message : String(error)
          });
        }
      }
    );

    return () => {
      cancelled = true;
    };
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
  const activityLabel = catalogItem?.displayName || shortTypeName(activity.activityType) || activity.activityType;
  const bookmarkCount = activity.bookmarkIds?.length ?? 0;
  const incidentCount = activity.incidentIds?.length ?? 0;

  return (
    <>
      <section className="wf-instance-section">
        <h4>Activity</h4>
        <dl className="wf-instance-meta">
          <dt>Name</dt>
          <dd>{activityLabel}</dd>
          <dt>Status</dt>
          <dd><WorkflowStatusBadge status={activity.status} subStatus={activity.subStatus} /></dd>
          <dt>Activity Execution ID</dt>
          <dd>{activity.activityExecutionId}</dd>
          <dt>Authored Activity ID</dt>
          <dd>{activity.authoredActivityId}</dd>
          <dt>Type</dt>
          <dd>{shortTypeName(activity.activityType) ?? activity.activityType} <small>{activity.activityTypeVersion}</small></dd>
          <dt>Started</dt>
          <dd>{formatDate(activity.startedAt)}</dd>
          <dt>Completed</dt>
          <dd>{formatDate(activity.completedAt)}</dd>
          <dt>Duration</dt>
          <dd>{formatDuration(activity.startedAt, activity.completedAt) || "Unknown"}</dd>
          <dt>Bookmarks</dt>
          <dd>{bookmarkCount}</dd>
          <dt>Incidents</dt>
          <dd>{incidentCount}</dd>
        </dl>
      </section>
      <WorkflowActivityInputSnapshots state={inspectionState} />
    </>
  );
}

function WorkflowActivityInputSnapshots({ state }: { state: ActivityExecutionInspectionState }) {
  if (state.status === "idle") return null;

  if (state.status === "loading") {
    return (
      <section className="wf-instance-section">
        <h4>Inputs</h4>
        <p>Loading runtime input evidence...</p>
      </section>
    );
  }

  if (state.status === "failed") {
    return (
      <section className="wf-instance-section">
        <h4>Inputs</h4>
        <p>Runtime input evidence is unavailable.</p>
        {state.error ? <p className="wf-instance-note">{state.error}</p> : null}
      </section>
    );
  }

  const snapshots = (state.inspection?.valueSnapshots ?? []).filter(snapshot => snapshot.subject === "ActivityInput");
  if (snapshots.length === 0) {
    return (
      <section className="wf-instance-section">
        <h4>Inputs</h4>
        <p>No runtime input snapshots were recorded for this execution.</p>
      </section>
    );
  }

  return (
    <section className="wf-instance-section">
      <h4>Inputs</h4>
      <div className="wf-runtime-input-list">
        {snapshots.map(snapshot => (
          <RuntimeInputSnapshot key={`${snapshot.name}:${snapshot.capturedAt}:${snapshot.captureMode}`} snapshot={snapshot} />
        ))}
      </div>
    </section>
  );
}

function RuntimeInputSnapshot({ snapshot }: { snapshot: ActivityExecutionInspectionValueSnapshot }) {
  const typeName = snapshot.type?.displayName || snapshot.type?.typeName || snapshot.type?.alias || "Unknown";
  const hasPayload = snapshot.captureMode === "Payload";

  return (
    <article className="wf-runtime-input">
      <header>
        <span>
          <strong>{snapshot.name}</strong>
          <small>{typeName}</small>
        </span>
        <span className="wf-runtime-capture-mode">{formatCaptureMode(snapshot.captureMode)}</span>
      </header>
      {hasPayload ? (
        <RuntimeInputPayload payload={snapshot.payload} />
      ) : (
        <p>{snapshot.captureReason || "The runtime capture policy did not include this input value."}</p>
      )}
      {snapshot.isSensitive ? <p className="wf-instance-note">Marked sensitive by runtime evidence.</p> : null}
    </article>
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

function WorkflowIncidentList({ incidents, selectedEvidenceId = null, onSelectEvidence }: {
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
        <div
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
        </div>
      ))}
      {copyStatus ? <p className="wf-copy-status" role="status">{copyStatus}</p> : null}
    </section>
  );
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
