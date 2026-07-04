import { useCallback, useEffect, useMemo, useState } from "react";
import { ReactFlow, Background, Controls, MiniMap, type Edge, type Node } from "@xyflow/react";
import { AlertCircle, Boxes, ChevronLeft, ListTree, RotateCcw, SlidersHorizontal, Sparkles } from "lucide-react";
import type { StudioAiContributionApi, StudioAiPromptActionContribution, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWorkflowDefinitionVersion, getWorkflowInstance, listActivities, listWorkflowInstances } from "../api/workflows";
import type { ActivityCatalogItem, IncidentStateSummary, WorkflowDefinitionVersionDetails, WorkflowInstanceDetails, WorkflowInstanceSummary } from "../workflowTypes";
import {
  applyRuntimeOverlays,
  buildCanvas,
  buildUnsupportedActivityCanvas,
  getActivityDesignerSupport,
  resolveScope,
  type WorkflowEdgeData,
  type WorkflowNodeData
} from "../workflowAdapter";
import { formatDate, formatDuration, shortTypeName } from "../workflowFormatting";
import { WorkflowExecutionTimeline } from "../WorkflowInstanceTimeline";
import { WfEmptyState, WfErrorCard, WfListSkeleton } from "./StatusViews";
import { PanelTabList } from "./PanelTabList";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import { nodeTypes, edgeTypes } from "./graph";
import type { InstanceInspectorTab, WorkflowEditorPanelTab, WorkflowInstanceInspectionData } from "./editorTypes";
import {
  activityNodeKey,
  dispatchAiAction,
  findAiAction,
  formatRunKind,
  formatWorkflowRunLoadError,
  formatWorkflowVersionLoadError,
  getVisibleWorkflowGraphNodeIds
} from "./editorHelpers";

export function WorkflowInstances({ context }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
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

  return (
    <>
      <div className="wf-toolbar">
        <button type="button" onClick={goBack}><ChevronLeft size={14} /> Runs</button>
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
        <div className="wf-instance-detail-workbench">
          <WorkflowInstanceCanvas
            definitionVersion={data.definitionVersion}
            definitionVersionError={data.definitionVersionError}
            activityCatalog={data.activityCatalog}
            details={data.details}
            selectedEvidenceId={selectedEvidenceId}
            onSelectEvidence={setSelectedEvidenceId}
          />
          <WorkflowInstanceInspector
            ai={ai}
            action={instanceAction}
            summary={data.details.instance}
            details={data.details}
            state="ready"
            error=""
            selectedEvidenceId={selectedEvidenceId}
            onSelectEvidence={setSelectedEvidenceId}
            activityCatalog={data.activityCatalog}
            graphNodeIds={data.definitionVersion ? getVisibleWorkflowGraphNodeIds(data.definitionVersion, data.activityCatalog) : undefined}
          />
        </div>
      ) : null}
    </>
  );
}

function WorkflowInstanceCanvas({ definitionVersion, definitionVersionError, activityCatalog, details, selectedEvidenceId, onSelectEvidence }: {
  definitionVersion: WorkflowDefinitionVersionDetails | null;
  definitionVersionError: string;
  activityCatalog: ActivityCatalogItem[];
  details: WorkflowInstanceDetails;
  selectedEvidenceId: string | null;
  onSelectEvidence(evidenceId: string | null): void;
}) {
  const canvas = useMemo(() => {
    if (!definitionVersion) return { nodes: [] as Node<WorkflowNodeData>[], edges: [] as Edge<WorkflowEdgeData>[] };

    const root = definitionVersion.state.rootActivity;
    if (!root) return { nodes: [] as Node<WorkflowNodeData>[], edges: [] as Edge<WorkflowEdgeData>[] };

    const rootCatalogItem = activityCatalog.find(activity => activity.activityVersionId === root.activityVersionId);
    const support = getActivityDesignerSupport(root, rootCatalogItem);
    const scope = support === "unsupported" ? null : resolveScope(root, []);
    const baseCanvas = support === "unsupported"
      ? buildUnsupportedActivityCanvas(root, activityCatalog, definitionVersion.layout)
      : scope
        ? buildCanvas(scope, activityCatalog, definitionVersion.layout)
        : buildUnsupportedActivityCanvas(root, activityCatalog, definitionVersion.layout);
    const readonlyNodes = baseCanvas.nodes.map(node => ({
      ...node,
      draggable: false,
      connectable: false,
      deletable: false
    }));

    return {
      nodes: applyRuntimeOverlays(readonlyNodes, details.activities, details.incidents, selectedEvidenceId),
      edges: baseCanvas.edges.map(edge => ({ ...edge, deletable: false }))
    };
  }, [activityCatalog, definitionVersion, details, selectedEvidenceId]);

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

function WorkflowInstanceInspector({ ai, action, summary, details, state, error, selectedEvidenceId = null, onSelectEvidence, graphNodeIds, activityCatalog = [] }: {
  ai: StudioAiContributionApi;
  action: StudioAiPromptActionContribution | undefined;
  summary: WorkflowInstanceSummary | null;
  details: WorkflowInstanceDetails | null;
  state: "idle" | "loading" | "ready" | "failed";
  error: string;
  selectedEvidenceId?: string | null;
  onSelectEvidence?(evidenceId: string): void;
  graphNodeIds?: Set<string>;
  activityCatalog?: ActivityCatalogItem[];
}) {
  const [activeTab, setActiveTab] = useState<InstanceInspectorTab>("timeline");

  if (!summary) {
    return <aside className="wf-instance-inspector"><div className="wf-empty">Select a workflow run to inspect its timeline.</div></aside>;
  }

  const incidentCount = details?.incidents.length ?? 0;
  const tabs: WorkflowEditorPanelTab[] = [
    { id: "timeline", title: "Timeline", order: 0, icon: <ListTree size={14} />, render: () => null },
    { id: "issues", title: incidentCount > 0 ? `Issues (${incidentCount})` : "Issues", order: 1, icon: <AlertCircle size={14} />, render: () => null },
    { id: "details", title: "Details", order: 2, icon: <SlidersHorizontal size={14} />, render: () => null }
  ];

  return (
    <aside className="wf-instance-inspector" aria-label="Workflow run details">
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
      <div className="wf-instance-tabs">
        <PanelTabList label="Workflow run tabs" tabs={tabs} activeTabId={activeTab} onSelect={tabId => setActiveTab(tabId as InstanceInspectorTab)} />
      </div>
      {state === "loading" ? <div className="wf-empty">Loading run details...</div> : null}
      {state === "failed" ? <WfErrorCard message={error} /> : null}
      {state === "ready" && details ? (
        <div className="wf-instance-tab-content">
          {activeTab === "timeline" ? (
            <WorkflowExecutionTimeline
              activities={details.activities}
              activityCatalog={activityCatalog}
              selectedEvidenceId={selectedEvidenceId}
              onSelectEvidence={onSelectEvidence}
            />
          ) : activeTab === "issues" ? (
            <>
              <WorkflowIncidentList incidents={details.incidents} selectedEvidenceId={selectedEvidenceId} onSelectEvidence={onSelectEvidence} />
              <WorkflowUnmatchedEvidence details={details} graphNodeIds={graphNodeIds} />
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
    </aside>
  );
}

function WorkflowIncidentList({ incidents, selectedEvidenceId = null, onSelectEvidence }: {
  incidents: IncidentStateSummary[];
  selectedEvidenceId?: string | null;
  onSelectEvidence?(evidenceId: string): void;
}) {
  return (
    <section className="wf-instance-section">
      <h4>Incidents</h4>
      {incidents.length === 0 ? <p>No incidents recorded.</p> : null}
      {incidents.map(incident => (
        <button
          type="button"
          className="wf-instance-incident"
          data-severity={incident.severity.toLowerCase()}
          data-selected={incident.incidentId === selectedEvidenceId}
          key={incident.incidentId}
          onClick={() => onSelectEvidence?.(incident.incidentId)}
        >
          <strong>{incident.failureType}</strong>
          <span>{incident.status} · {incident.severity}</span>
          <p>{incident.message}</p>
        </button>
      ))}
    </section>
  );
}

function WorkflowUnmatchedEvidence({ details, graphNodeIds }: { details: WorkflowInstanceDetails; graphNodeIds?: Set<string> }) {
  if (!graphNodeIds) return null;

  const activityByExecutionId = new Map(details.activities.map(activity => [activity.activityExecutionId, activity]));
  const unmatchedActivities = details.activities.filter(activity => !graphNodeIds.has(activityNodeKey(activity)));
  const unmatchedIncidents = details.incidents.filter(incident => {
    const activity = incident.activityExecutionId ? activityByExecutionId.get(incident.activityExecutionId) : null;
    const nodeId = incident.executableNodeId ?? (activity ? activityNodeKey(activity) : "");
    return !nodeId || !graphNodeIds.has(nodeId);
  });

  if (unmatchedActivities.length === 0 && unmatchedIncidents.length === 0) return null;

  return (
    <section className="wf-instance-section">
      <h4>Unmatched runtime evidence</h4>
      <div className="wf-instance-unmatched-list">
        {unmatchedActivities.map(activity => (
          <div className="wf-instance-unmatched" key={`activity-${activity.activityExecutionId}`}>
            <strong>{shortTypeName(activity.activityType) ?? activity.activityType}</strong>
            <small>{activity.activityExecutionId}</small>
          </div>
        ))}
        {unmatchedIncidents.map(incident => (
          <div className="wf-instance-unmatched" key={`incident-${incident.incidentId}`}>
            <strong>{incident.failureType}</strong>
            <small>{incident.incidentId}</small>
          </div>
        ))}
      </div>
    </section>
  );
}
