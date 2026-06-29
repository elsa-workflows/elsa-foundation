import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ReactFlow,
  Background,
  BaseEdge,
  Controls,
  EdgeLabelRenderer,
  Handle,
  MiniMap,
  Position,
  addEdge,
  applyEdgeChanges,
  applyNodeChanges,
  getSmoothStepPath,
  reconnectEdge,
  type Connection,
  type Edge,
  type EdgeChange,
  type EdgeProps,
  type Node,
  type NodeChange,
  type NodeProps,
  type OnConnectEnd,
  type OnConnectStart,
  type OnReconnect,
  type ReactFlowInstance,
  type XYPosition
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { AlertCircle, AlertTriangle, Boxes, Check, ChevronDown, ChevronLeft, ChevronRight, Code2, Copy, Download, GitBranch, GripVertical, ListTree, Maximize2, Minimize2, Network, Package, Play, Plus, Redo2, RotateCcw, Save, Search, SlidersHorizontal, Sparkles, Trash2, Undo2, Workflow as WorkflowIcon, X } from "lucide-react";
import type { ElsaStudioModuleApi, StudioActivityDescriptor, StudioActivityPropertyEditorContribution, StudioAiContributionApi, StudioAiPromptActionContribution, StudioDialogApi, StudioEndpointContext, StudioExpressionDescriptor, StudioExpressionEditorContribution, StudioWorkflowDesignerPanelContribution } from "@elsa-workflows/studio-sdk";
import {
  createDefinition,
  deleteDefinition,
  deleteDefinitionPermanently,
  getDefinition,
  getWorkflowDefinitionVersion,
  getWorkflowInstance,
  listActivities,
  listActivityAvailabilityDiagnostics,
  listActivityDescriptors,
  listDefinitions,
  listExecutables,
  listWorkflowInstances,
  listExpressionDescriptors,
  fallbackExpressionDescriptors,
  promoteDraft,
  publishVersion,
  restoreDefinition,
  runExecutable,
  startWorkflowDraftTestRun,
  updateDraft
} from "./api/workflows";
import type { ActivityAvailabilityDiagnosticEntry, ActivityAvailabilityDiagnostics, ActivityCatalogItem, ActivityExecutionStateSummary, ActivityNode, DefinitionListState, IncidentStateSummary, WorkflowDefinitionDetails, WorkflowDefinitionVersionDetails, WorkflowDraft, WorkflowExecutableRunResponse, WorkflowExecutableSummary, WorkflowInstanceDetails, WorkflowInstanceSummary, WorkflowTestRunView } from "./workflowTypes";
import { findActivityAvailabilityDiagnostic, getAvailabilityStateLabel } from "./activityAvailability";
import {
  applyRuntimeOverlays,
  buildCanvas,
  buildUnsupportedActivityCanvas,
  createActivityNode,
  createWorkflowEdge,
  getActivityDesignerSupport,
  getActivityDisplay,
  getActivitySourcePorts,
  getChildSlots,
  resolveScope,
  resolveScopeOwner,
  resolveActivityIcon,
  spliceWorkflowEdge,
  updateActivity,
  updateLayout,
  updateScopeActivities,
  updateScopeOwner,
  syncCanvasToScope,
  withFlowchartConnections,
  flowchartEdges,
  type ScopeFrame,
  type WorkflowEdgeData,
  type WorkflowNodeData
} from "./workflowAdapter";
import { ActivityPropertiesPanel } from "./ActivityPropertiesPanel";
import { buildExportPayload, downloadWorkflowJson, buildDraftFromJson } from "./workflowSerialization";
import { computeAutoLayout } from "./workflowLayout";
import { canRedo, canUndo, createHistory, pushSnapshot, redo as redoHistory, undo as undoHistory, type HistoryState } from "./workflowHistory";
import { formatDate, formatDuration, renderActivityIcon, shortTypeName } from "./workflowFormatting";
import { WorkflowCodeView } from "./WorkflowCodeView";
import { WorkflowPropertiesView } from "./WorkflowPropertiesView";
import { WorkflowExecutionTimeline } from "./WorkflowInstanceTimeline";
import { ActivityAvailabilityPage } from "./ActivityAvailabilityPage";
import {
  applyWorkflowGraphOperationBatch,
  workflowGraphOperationApplyEvent,
  workflowGraphOperationUndoEvent,
  type WorkflowGraphOperationBatch
} from "./workflowGraphOperationBatch";
import "./styles.css";

function WfListSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="wf-grid" aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }).map((_, index) => (
        <div key={index} className="wf-skeleton wf-skeleton-row" style={{ width: `${90 - (index % 3) * 12}%` }} />
      ))}
    </div>
  );
}

function WfEmptyState({ icon, title, description, action }: { icon?: React.ReactNode; title: string; description?: string; action?: React.ReactNode }) {
  return (
    <div className="wf-empty-state" role="status">
      <div className="wf-empty-state-icon" aria-hidden>{icon ?? <Boxes size={22} />}</div>
      <h3>{title}</h3>
      {description ? <p>{description}</p> : null}
      {action ? <div className="wf-empty-state-action">{action}</div> : null}
    </div>
  );
}

function WfErrorCard({ message, title = "Something went wrong" }: { message?: string; title?: string }) {
  return (
    <div className="wf-error-card" role="alert">
      <AlertCircle size={18} />
      <div className="wf-error-card-body">
        <strong>{title}</strong>
        <span>{message || "Please try again, or check that the Elsa server is reachable."}</span>
      </div>
    </div>
  );
}

const nodeTypes = { workflowActivity: WorkflowActivityNode };
const edgeTypes = { workflow: WorkflowFlowEdge };
const activityDragDataType = "application/x-elsa-activity-version-id";
const pointerDragThreshold = 6;
const autosaveDelayMs = 1200;
const historyCaptureDelayMs = 250;
const definitionPageSizes = [10, 25, 50];
const defaultDefinitionPageSize = 10;
const workflowPaletteWidthStorageKey = "elsa-studio-workflow-palette-width";
const workflowInspectorWidthStorageKey = "elsa-studio-workflow-inspector-width";
const workflowPaletteCollapsedStorageKey = "elsa-studio-workflow-palette-collapsed";
const workflowInspectorCollapsedStorageKey = "elsa-studio-workflow-inspector-collapsed";
const workflowSidePanelMaximizedStorageKey = "elsa-studio-workflow-side-panel-maximized";
const minPaletteWidth = 180;
const maxPaletteWidth = 460;
const defaultPaletteWidth = 260;
const minInspectorWidth = 260;
const maxInspectorWidth = 560;
const defaultInspectorWidth = 320;
const collapsedSidePanelWidth = 42;
const sidePanelResizeStep = 16;
type CreateWorkflowKind = "sequence" | "flowchart";
interface CreateWorkflowDraft {
  name: string;
  description: string;
  rootKind: CreateWorkflowKind;
  rootActivityVersionId?: string | null;
}

interface ActivityPaletteGroup {
  category: string;
  activities: ActivityCatalogItem[];
}

type CanvasView = "designer" | "code" | "properties";

type WorkflowEdge = Edge<WorkflowEdgeData>;
type WorkflowEditorOperation = "idle" | "saving" | "promoting" | "testRunPreparing" | "testRunStarting";
interface WorkflowTestRunState {
  draftSignature: string;
  view: WorkflowTestRunView;
}

interface ExecutableRunState {
  artifactId: string;
  workflowExecutionId: string | null;
}

export type WorkflowConnectSource = { nodeId: string; handleId: string | null };

type ConnectMenuState =
  | { kind: "fromEmpty"; clientX: number; clientY: number }
  | { kind: "fromPort"; sourceNodeId: string; sourceHandleId: string | null; clientX: number; clientY: number }
  | { kind: "spliceEdge"; edgeId: string; clientX: number; clientY: number };

interface WorkflowEdgeActions {
  highlightedEdgeId: string | null;
  deleteEdge(edgeId: string): void;
  requestInsertActivity(edgeId: string, clientX: number, clientY: number): void;
}

const WorkflowEdgeActionsContext = React.createContext<WorkflowEdgeActions | null>(null);

// Maps an authored node to a non-blocking availability warning (or null when still addable).
// Provided by the designer so the standalone node renderer can surface badges without prop-drilling.
type WorkflowNodeAvailabilityLookup = (input: { activityVersionId?: string | null; activityTypeKey?: string | null }) => ActivityAvailabilityDiagnosticEntry | null;

const WorkflowNodeAvailabilityContext = React.createContext<WorkflowNodeAvailabilityLookup | null>(null);

declare global {
  interface Window {
    __ELSA_STUDIO_WORKFLOW_CONTEXT__?: {
      workflowId: string;
      workflowDefinitionId?: string;
      workflowVersionId?: string | null;
      draftId?: string | null;
      revision?: string | null;
      selectedNodeId?: string | null;
      selectedActivityType?: string | null;
      summary?: string;
      activities?: Array<{ id: string; type: string; displayName?: string }>;
      connections?: Array<{ source: string; target: string; sourcePort?: string; targetPort?: string }>;
      diagnostics?: Array<{ severity: string; message: string }>;
    };
  }
}

// Host-provided dialog service, captured at registration so imperative handlers can await it
// without prop-drilling. Assigned before any route component renders.
let dialogs: StudioDialogApi;

export function register(api: ElsaStudioModuleApi) {
  dialogs = api.dialogs;
  api.featureAreas.add({
    id: "workflows",
    title: "Workflows",
    description: "Design, publish and run workflow definitions and inspect runs.",
    navGroup: "Workspace",
    ownedPaths: ["/workflows"],
    required: true,
    defaultEnabled: true,
    order: 20,
    nav: {
      title: "Workflows",
      path: "/workflows/definitions",
      iconColor: "#0ea5e9",
      items: [
        { title: "Definitions", path: "/workflows/definitions", iconColor: "#0ea5e9" },
        { title: "Executables", path: "/workflows/executables", iconColor: "#0ea5e9" },
        { title: "Runs", path: "/workflows/instances", iconColor: "#0ea5e9" },
        { title: "Activity Availability", path: "/workflows/activity-availability", iconColor: "#0ea5e9" }
      ]
    },
    routes: [
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => <WorkflowManagementPage context={api.backend} ai={api.ai} propertyEditors={api.propertyEditors.list()} expressionEditors={api.expressionEditors?.list() ?? []} workflowDesignerPanels={api.workflowDesigner.panels.list()} />
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => <WorkflowExecutablesPage context={api.backend} ai={api.ai} />
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => <WorkflowInstancesPage context={api.backend} ai={api.ai} />
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => <WorkflowInstanceDetailsPage context={api.backend} ai={api.ai} />
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => <ActivityAvailabilityPage context={api.backend} />
      }
    ]
  });
}

function WorkflowManagementPage({
  context,
  ai,
  propertyEditors,
  expressionEditors,
  workflowDesignerPanels
}: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  workflowDesignerPanels: StudioWorkflowDesignerPanelContribution[];
}) {
  const [definitionId, setDefinitionId] = useState(readDefinitionIdFromUrl);

  useEffect(() => {
    const syncFromLocation = () => setDefinitionId(readDefinitionIdFromUrl());
    window.addEventListener("popstate", syncFromLocation);
    return () => window.removeEventListener("popstate", syncFromLocation);
  }, []);

  const openDefinition = (id: string | null) => {
    const url = id ? `/workflows/definitions?definition=${encodeURIComponent(id)}` : "/workflows/definitions";
    window.history.pushState({}, "", url);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return definitionId
    ? <WorkflowEditor context={context} definitionId={definitionId} ai={ai} propertyEditors={propertyEditors} expressionEditors={expressionEditors} workflowDesignerPanels={workflowDesignerPanels} onBack={() => openDefinition(null)} />
    : (
      <WorkflowsPageFrame title="Definitions">
        <WorkflowDefinitions context={context} ai={ai} onOpen={openDefinition} />
      </WorkflowsPageFrame>
    );
}

function WorkflowExecutablesPage({ context, ai }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
  const [definitionFilter, setDefinitionFilter] = useState(readExecutableDefinitionFilterFromUrl);

  useEffect(() => {
    const syncFromLocation = () => setDefinitionFilter(readExecutableDefinitionFilterFromUrl());
    window.addEventListener("popstate", syncFromLocation);
    return () => window.removeEventListener("popstate", syncFromLocation);
  }, []);

  const updateDefinitionFilter = useCallback((filter: string | null) => {
    const normalizedFilter = filter?.trim() ?? "";
    const url = new URL(window.location.href);

    if (normalizedFilter) {
      url.searchParams.set("definition", normalizedFilter);
    } else {
      url.searchParams.delete("definition");
    }

    setDefinitionFilter(normalizedFilter || null);
    window.history.replaceState({}, "", `${url.pathname}${url.search}${url.hash}`);
  }, []);

  return (
    <WorkflowsPageFrame title="Executables">
      <WorkflowExecutables context={context} ai={ai} definitionFilter={definitionFilter} onDefinitionFilterChange={updateDefinitionFilter} />
    </WorkflowsPageFrame>
  );
}

function WorkflowInstancesPage({ context, ai }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
  return (
    <WorkflowsPageFrame title="Runs">
      <WorkflowInstances context={context} ai={ai} />
    </WorkflowsPageFrame>
  );
}

function WorkflowInstanceDetailsPage({ context, ai }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
  const workflowExecutionId = readWorkflowExecutionIdFromUrl();

  return (
    <WorkflowsPageFrame title="Run">
      <WorkflowInstanceDetailsWorkbench context={context} ai={ai} workflowExecutionId={workflowExecutionId} />
    </WorkflowsPageFrame>
  );
}

function WorkflowsPageFrame({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="wf-page">
      <div className="wf-page-header">
        <div>
          <span className="wf-kicker">Workflow management</span>
          <h2>{title}</h2>
        </div>
      </div>
      {children}
    </section>
  );
}

function readDefinitionIdFromUrl() {
  return new URLSearchParams(window.location.search).get("definition");
}

function readExecutableDefinitionFilterFromUrl() {
  return new URLSearchParams(window.location.search).get("definition");
}

function readWorkflowExecutionIdFromUrl() {
  const match = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return match ? decodeURIComponent(match[1]) : "";
}

function WorkflowDefinitions({ context, ai, onOpen }: { context: StudioEndpointContext; ai: StudioAiContributionApi; onOpen(id: string): void }) {
  const [search, setSearch] = useState("");
  const [listState, setListState] = useState<DefinitionListState>("active");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(defaultDefinitionPageSize);
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [definitions, setDefinitions] = useState<WorkflowDefinitionDetails["definition"][]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [selectedDefinitionIds, setSelectedDefinitionIds] = useState<Set<string>>(() => new Set());
  const [createDraft, setCreateDraft] = useState<CreateWorkflowDraft | null>(null);
  const [creating, setCreating] = useState(false);
  const [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
  const [catalogState, setCatalogState] = useState<"idle" | "loading" | "ready" | "failed">("idle");
  const selectVisibleRef = useRef<HTMLInputElement | null>(null);
  const visibleDefinitionIds = useMemo(() => definitions.map(definition => definition.id), [definitions]);
  const suggestMetadataAction = findAiAction(ai, "weaver.workflows.suggest-create-metadata");
  const explainDefinitionAction = findAiAction(ai, "weaver.workflows.explain-definition");
  const selectedVisibleCount = visibleDefinitionIds.filter(id => selectedDefinitionIds.has(id)).length;
  const allVisibleSelected = visibleDefinitionIds.length > 0 && selectedVisibleCount === visibleDefinitionIds.length;

  const load = useCallback(async () => {
    setState("loading");
    setError("");
    try {
      const response = await listDefinitions(context, { search, state: listState, page, pageSize });
      const backendPaged = typeof response.totalCount === "number";
      const effectiveTotalCount = response.totalCount ?? response.definitions.length;
      const effectiveTotalPages = getTotalPages(effectiveTotalCount, pageSize);

      if (effectiveTotalCount > 0 && page > effectiveTotalPages) {
        setPage(effectiveTotalPages);
        return;
      }

      setDefinitions(backendPaged ? response.definitions : pageItems(response.definitions, page, pageSize));
      setTotalCount(effectiveTotalCount);
      setState("ready");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setState("failed");
    }
  }, [context, search, listState, page, pageSize]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (selectVisibleRef.current) {
      selectVisibleRef.current.indeterminate = selectedVisibleCount > 0 && !allVisibleSelected;
    }
  }, [allVisibleSelected, selectedVisibleCount]);

  const loadCatalog = useCallback(async () => {
    if (catalogState === "loading" || catalogState === "ready") return;
    setCatalogState("loading");
    try {
      const response = await listActivities(context);
      setCatalog(response.activities ?? []);
      setCatalogState("ready");
    } catch (e) {
      setCatalogState("failed");
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [catalogState, context]);

  const openCreateDialog = () => {
    setError("");
    setStatus("");
    setCreateDraft({ name: "", description: "", rootKind: "flowchart" });
    void loadCatalog();
  };

  const submitCreate = async () => {
    if (!createDraft?.name.trim()) return;
    setCreating(true);
    setError("");
    setStatus("");
    try {
      const details = await createDefinition(context, {
        name: createDraft.name.trim(),
        description: createDraft.description.trim() || null,
        rootKind: createDraft.rootKind,
        rootActivityVersionId: getCreateRootActivityVersionId(createDraft, catalog)
      });
      setCreateDraft(null);
      onOpen(details.definition.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setCreating(false);
    }
  };

  const openDefinitionArtifacts = (definitionId: string) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(definitionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  const refreshAfterMutation = async () => {
    if (definitions.length === 1 && page > 1) {
      setPage(page - 1);
      return;
    }

    await load();
  };

  const clearSelection = () => setSelectedDefinitionIds(new Set());

  const toggleDefinitionSelection = (definitionId: string, selected: boolean) => {
    setSelectedDefinitionIds(current => {
      const next = new Set(current);
      if (selected) next.add(definitionId);
      else next.delete(definitionId);
      return next;
    });
  };

  const toggleVisibleSelection = (selected: boolean) => {
    setSelectedDefinitionIds(current => {
      const next = new Set(current);
      for (const definitionId of visibleDefinitionIds) {
        if (selected) next.add(definitionId);
        else next.delete(definitionId);
      }
      return next;
    });
  };

  const changeListState = (nextState: DefinitionListState) => {
    setListState(nextState);
    setPage(1);
    clearSelection();
  };

  const changeSearch = (value: string) => {
    setSearch(value);
    setPage(1);
    clearSelection();
  };

  const softDelete = async (definition: WorkflowDefinitionDetails["definition"]) => {
    if (!(await dialogs.confirm({ message: `Delete workflow definition "${definition.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" }))) return;
    setStatus("");
    setError("");
    try {
      await deleteDefinition(context, definition.id);
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Deleted ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const restore = async (definition: WorkflowDefinitionDetails["definition"]) => {
    setStatus("");
    setError("");
    try {
      await restoreDefinition(context, definition.id);
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Restored ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const permanentDelete = async (definition: WorkflowDefinitionDetails["definition"]) => {
    if (!(await dialogs.confirm({ message: `Permanently delete workflow definition "${definition.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" }))) return;
    setStatus("");
    setError("");
    try {
      await deleteDefinitionPermanently(context, definition.id);
      toggleDefinitionSelection(definition.id, false);
      setStatus(`Permanently deleted ${definition.name}`);
      await refreshAfterMutation();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  return (
    <>
      <div className="wf-toolbar">
        <div className="wf-segmented" role="tablist" aria-label="Definition state">
          <button type="button" className={listState === "active" ? "active" : ""} aria-selected={listState === "active"} onClick={() => changeListState("active")}>Active</button>
          <button type="button" className={listState === "deleted" ? "active" : ""} aria-selected={listState === "deleted"} onClick={() => changeListState("deleted")}>Deleted</button>
        </div>
        <label className="wf-search">
          <Search size={15} />
          <input value={search} onChange={event => changeSearch(event.target.value)} placeholder="Search definitions" />
        </label>
        <button type="button" onClick={() => void load()}>Refresh</button>
        <div className="wf-actions">
          <button type="button" title="Create workflow" onClick={openCreateDialog}><Plus size={15} /> Create</button>
        </div>
      </div>

      {state === "failed" ? <WfErrorCard message={error} title="Couldn't load workflow definitions" /> : null}
      {state !== "failed" && error ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}
      {status ? <div className="wf-status-line"><Check size={14} /> {status}</div> : null}
      {selectedDefinitionIds.size > 0 ? (
        <div className="wf-selection-bar" aria-live="polite">
          <span>{selectedDefinitionIds.size} selected</span>
          <button type="button" onClick={clearSelection}>Clear selection</button>
        </div>
      ) : null}
      {state === "loading" ? <WfListSkeleton /> : null}
      {state === "ready" && definitions.length === 0 ? (
        <WfEmptyState
          icon={<Package size={22} />}
          title={`No ${listState} workflow definitions`}
          description="Create a workflow to start designing automation, or adjust your filters to see more."
          action={<button type="button" className="wf-link-button" onClick={openCreateDialog}><Plus size={15} /> Create workflow</button>}
        />
      ) : null}
      {state === "ready" && definitions.length > 0 ? (
        <>
          <div className="wf-grid" role="table" aria-label="Workflow definitions">
            <div className="wf-grid-head" role="row">
              <label className="wf-row-select">
                <input
                  ref={selectVisibleRef}
                  type="checkbox"
                  checked={allVisibleSelected}
                  onChange={event => toggleVisibleSelection(event.target.checked)}
                  aria-label="Select visible workflow definitions"
                />
              </label>
              <span>Name</span>
              <span>Latest version</span>
              <span>{listState === "deleted" ? "Deleted" : "Draft"}</span>
              <span>Modified</span>
              <span>Actions</span>
            </div>
            {definitions.map(definition => (
              <div
                className="wf-grid-row"
                role="row"
                aria-label={`Open workflow definition ${definition.name}`}
                aria-selected={selectedDefinitionIds.has(definition.id)}
                tabIndex={0}
                onClick={() => onOpen(definition.id)}
                onKeyDown={event => {
                  if (event.currentTarget !== event.target) return;
                  if (event.key !== "Enter" && event.key !== " ") return;
                  event.preventDefault();
                  onOpen(definition.id);
                }}
                key={definition.id}
              >
                <label className="wf-row-select" onClick={event => event.stopPropagation()}>
                  <input
                    type="checkbox"
                    checked={selectedDefinitionIds.has(definition.id)}
                    onChange={event => toggleDefinitionSelection(definition.id, event.target.checked)}
                    aria-label={`Select workflow definition ${definition.name}`}
                  />
                </label>
                <span>
                  <strong>{definition.name}</strong>
                  <small>{definition.description || definition.id}</small>
                </span>
                <span>{definition.latestVersion ?? "No version"}</span>
                <span>{listState === "deleted" ? formatDate(definition.deletedAt) : (definition.draftId ? "Draft" : "None")}</span>
                <span>{formatDate(definition.lastModifiedAt)}</span>
                <span className="wf-row-actions" onClick={event => event.stopPropagation()}>
                  {listState === "active" ? (
                    <>
                      <button type="button" onClick={event => { event.stopPropagation(); onOpen(definition.id); }}>Open</button>
                      <button type="button" onClick={event => { event.stopPropagation(); openDefinitionArtifacts(definition.id); }}>Artifacts</button>
                      {explainDefinitionAction ? (
                        <button type="button" onClick={() => dispatchAiAction(ai, explainDefinitionAction, definition)}><Sparkles size={13} /> Explain</button>
                      ) : null}
                      <button type="button" className="danger" onClick={() => void softDelete(definition)}><Trash2 size={13} /> Delete</button>
                    </>
                  ) : (
                    <>
                      <button type="button" onClick={() => void restore(definition)}><RotateCcw size={13} /> Restore</button>
                      <button type="button" className="danger" onClick={() => void permanentDelete(definition)}><Trash2 size={13} /> Delete permanently</button>
                    </>
                  )}
                </span>
              </div>
            ))}
          </div>
          <DefinitionPager
            page={page}
            pageSize={pageSize}
            totalCount={totalCount}
            onPageChange={setPage}
            onPageSizeChange={value => {
              setPageSize(value);
              setPage(1);
            }}
          />
        </>
      ) : null}
      {createDraft ? (
        <CreateWorkflowDialog
          draft={createDraft}
          activities={catalog}
          catalogState={catalogState}
          creating={creating}
          suggestMetadataAction={suggestMetadataAction}
          onSuggestMetadata={suggestMetadataAction ? () => dispatchAiAction(ai, suggestMetadataAction, { draft: createDraft, activities: catalog }) : undefined}
          onChange={nextDraft => setCreateDraft(nextDraft)}
          onClose={() => setCreateDraft(null)}
          onSubmit={submitCreate}
        />
      ) : null}
    </>
  );
}

function CreateWorkflowDialog({ draft, activities, catalogState, creating, suggestMetadataAction, onSuggestMetadata, onChange, onClose, onSubmit }: {
  draft: CreateWorkflowDraft;
  activities: ActivityCatalogItem[];
  catalogState: "idle" | "loading" | "ready" | "failed";
  creating: boolean;
  suggestMetadataAction?: StudioAiPromptActionContribution | null;
  onSuggestMetadata?: () => void;
  onChange(draft: CreateWorkflowDraft): void;
  onClose(): void;
  onSubmit(): void;
}) {
  const groupedActivities = useMemo(() => groupCreateRootActivities(activities), [activities]);
  const selectedRootValue = getSelectedRootValue(draft, activities);

  const changeRootActivity = (value: string) => {
    if (value.startsWith("kind:")) {
      onChange({ ...draft, rootKind: value.slice(5) as CreateWorkflowKind, rootActivityVersionId: null });
      return;
    }

    const selectedActivity = activities.find(activity => activity.activityVersionId === value);
    onChange({
      ...draft,
      rootKind: getRootKind(selectedActivity) ?? draft.rootKind,
      rootActivityVersionId: value
    });
  };

  return (
    <div className="wf-dialog-backdrop" role="presentation">
      <section className="wf-dialog" role="dialog" aria-modal="true" aria-labelledby="workflow-create-title">
        <form
          onSubmit={event => {
            event.preventDefault();
            onSubmit();
          }}
        >
          <div className="wf-dialog-heading">
            <h3 id="workflow-create-title">Create Workflow</h3>
            {suggestMetadataAction ? (
              <button type="button" className="wf-ai-action" onClick={onSuggestMetadata} title={suggestMetadataAction.description ?? suggestMetadataAction.label}>
                <Sparkles size={13} /> {suggestMetadataAction.label}
              </button>
            ) : null}
          </div>
          <label className="wf-form-field">
            <span>Display name</span>
            <input
              autoFocus
              aria-label="Display name"
              value={draft.name}
              onChange={event => onChange({ ...draft, name: event.target.value })}
            />
          </label>
          <label className="wf-form-field">
            <span>Description</span>
            <textarea
              aria-label="Description"
              rows={3}
              value={draft.description}
              onChange={event => onChange({ ...draft, description: event.target.value })}
            />
          </label>
          <label className="wf-form-field">
            <span>Root activity</span>
            <select
              aria-label="Root activity"
              value={selectedRootValue}
              onChange={event => changeRootActivity(event.target.value)}
              disabled={catalogState === "loading"}
            >
              <optgroup label="Composite roots">
                {groupedActivities.compositeRoots.map(activity => (
                  <option key={activity.value} value={activity.value}>{activity.label}</option>
                ))}
              </optgroup>
              {groupedActivities.otherCategories.map(category => (
                <optgroup key={category.name} label={category.name}>
                  {category.activities.map(activity => (
                    <option key={activity.activityVersionId} value={activity.activityVersionId}>
                      {getActivityDisplay(activity)}
                    </option>
                  ))}
                </optgroup>
              ))}
            </select>
          </label>
          {catalogState === "loading" ? <div className="wf-dialog-note">Loading activity catalog...</div> : null}
          {catalogState === "failed" ? <div className="wf-dialog-note">Activity catalog could not be loaded. Composite roots remain available.</div> : null}
          <div className="wf-dialog-actions">
            <button type="button" onClick={onClose} disabled={creating}>Cancel</button>
            <button type="submit" disabled={creating || !draft.name.trim()}>{creating ? "Creating..." : "Create"}</button>
          </div>
        </form>
      </section>
    </div>
  );
}

function WorkflowExecutables({ context, ai, definitionFilter, onDefinitionFilterChange }: { context: StudioEndpointContext; ai: StudioAiContributionApi; definitionFilter: string | null; onDefinitionFilterChange(filter: string | null): void }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [lastRun, setLastRun] = useState<ExecutableRunState | null>(null);
  const [executables, setExecutables] = useState<WorkflowExecutableSummary[]>([]);
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

  const load = useCallback(async () => {
    setState("loading");
    setError("");
    try {
      setExecutables(await listExecutables(context));
      setState("ready");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setState("failed");
    }
  }, [context]);

  useEffect(() => {
    void load();
  }, [load]);

  const run = async (executable: WorkflowExecutableSummary) => {
    setStatus("");
    setLastRun(null);
    setError("");
    try {
      const result = await runExecutable(context, executable.artifactId);
      const workflowExecutionId = readExecutableRunWorkflowExecutionId(result);
      setLastRun({ artifactId: executable.artifactId, workflowExecutionId });
      setStatus(`Started ${executable.artifactId}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const explain = (executable: WorkflowExecutableSummary) => {
    if (!explainExecutableAction) return;

    if (dispatchAiAction(ai, explainExecutableAction, executable)) {
      setError("");
      setLastRun(null);
      setStatus(`Sent ${executable.artifactId} to Weaver`);
    }
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
      <div className="wf-toolbar">
        <button type="button" onClick={() => void load()}>Refresh</button>
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
          {visibleExecutables.map(executable => (
            <div className="wf-grid-row" role="row" key={executable.artifactId}>
              <span className="wf-artifact-cell">
                <span className="wf-cell-line">
                  <strong title={executable.artifactId}>{executable.artifactId}</strong>
                  <CopyValueButton value={executable.artifactId} ariaLabel={`Copy artifact ID ${executable.artifactId}`} copiedLabel="artifact ID" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                </span>
                <span className="wf-cell-line wf-cell-line-muted">
                  <small title={executable.artifactHash}>{executable.artifactHash}</small>
                  <CopyValueButton value={executable.artifactHash} ariaLabel={`Copy artifact hash ${executable.artifactHash}`} copiedLabel="artifact hash" onCopied={markCopied} onCopyFailed={markCopyFailed} />
                </span>
              </span>
              <span className="wf-cell-line wf-version-cell">
                <span>{executable.artifactVersion}</span>
                <CopyValueButton value={executable.artifactVersion} ariaLabel={`Copy artifact version ${executable.artifactVersion}`} copiedLabel="artifact version" onCopied={markCopied} onCopyFailed={markCopyFailed} />
              </span>
              <WorkflowExecutableSourceCell executable={executable} onCopied={markCopied} onCopyFailed={markCopyFailed} />
              <span>{formatExecutableRoot(executable)}</span>
              <span>{formatDate(executable.publishedAt ?? executable.createdAt)}</span>
              <span className="wf-row-actions">
                <button type="button" onClick={() => void run(executable)}><Play size={13} /> Run</button>
                {explainExecutableAction ? (
                  <button type="button" onClick={() => explain(executable)}><Sparkles size={13} /> Explain</button>
                ) : null}
              </span>
            </div>
          ))}
        </div>
      ) : null}
    </>
  );
}

function WorkflowExecutableSourceCell({ executable, onCopied, onCopyFailed }: { executable: WorkflowExecutableSummary; onCopied(label: string): void; onCopyFailed(label: string): void }) {
  const sourceId = executable.sourceId || executable.definitionVersionId || executable.definitionId;
  const sourceVersion = executable.sourceVersion;

  return (
    <span className="wf-source-cell">
      <span className="wf-source-kind">{formatExecutableSourceKind(executable.sourceKind)}</span>
      {sourceId ? (
        <span className="wf-cell-line">
          <code title={sourceId}>{sourceId}</code>
          <CopyValueButton value={sourceId} ariaLabel={`Copy source ID ${sourceId}`} copiedLabel="source ID" onCopied={onCopied} onCopyFailed={onCopyFailed} />
        </span>
      ) : null}
      {sourceVersion ? <small>Version {sourceVersion}</small> : null}
    </span>
  );
}

function ExecutableRunStatusLine({ status, run, compact = false }: { status: string; run: ExecutableRunState | null; compact?: boolean }) {
  const openRun = () => {
    if (!run?.workflowExecutionId) return;
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(run.workflowExecutionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return (
    <div className={`wf-status-line${compact ? " compact" : ""}`}>
      <Check size={compact ? 13 : 14} />
      <span>{status}</span>
      {run?.workflowExecutionId ? (
        <button type="button" onClick={openRun}>Open Run {run.workflowExecutionId}</button>
      ) : null}
    </div>
  );
}

function CopyValueButton({ value, ariaLabel, copiedLabel, onCopied, onCopyFailed }: { value: string | null | undefined; ariaLabel: string; copiedLabel: string; onCopied(label: string): void; onCopyFailed(label: string): void }) {
  if (!value) return null;

  const copy = async (event: React.MouseEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();
    try {
      await copyTextToClipboard(value);
      onCopied(copiedLabel);
    } catch {
      onCopyFailed(copiedLabel);
    }
  };

  return (
    <button type="button" className="wf-copy-button" aria-label={ariaLabel} title={ariaLabel} onClick={event => void copy(event)}>
      <Copy size={12} />
    </button>
  );
}

function WorkflowArtifactsPanel({ context, ai, definitionId, publishedArtifactId }: { context: StudioEndpointContext; ai: StudioAiContributionApi; definitionId: string; publishedArtifactId: string | null }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [lastRun, setLastRun] = useState<ExecutableRunState | null>(null);
  const [artifacts, setArtifacts] = useState<WorkflowExecutableSummary[]>([]);
  const explainExecutableAction = findAiAction(ai, "weaver.workflows.explain-executable");

  const load = useCallback(async () => {
    setState("loading");
    setError("");
    try {
      const executables = await listExecutables(context);
      setArtifacts(executables.filter(executable => executableBelongsToDefinition(executable, definitionId)).sort(compareExecutablesByPublishedDate));
      setState("ready");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setArtifacts([]);
      setState("failed");
    }
  }, [context, definitionId]);

  useEffect(() => {
    void load();
  }, [load, publishedArtifactId]);

  const run = async (artifact: WorkflowExecutableSummary) => {
    setStatus("");
    setLastRun(null);
    setError("");
    try {
      const result = await runExecutable(context, artifact.artifactId);
      setLastRun({ artifactId: artifact.artifactId, workflowExecutionId: readExecutableRunWorkflowExecutionId(result) });
      setStatus(`Started ${artifact.artifactId}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const explain = (artifact: WorkflowExecutableSummary) => {
    if (!explainExecutableAction) return;

    if (dispatchAiAction(ai, explainExecutableAction, artifact)) {
      setError("");
      setLastRun(null);
      setStatus(`Sent ${artifact.artifactId} to Weaver`);
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
      <div className="wf-artifacts-toolbar">
        <span>{artifacts.length} artifact{artifacts.length === 1 ? "" : "s"}</span>
        <button type="button" onClick={() => void load()}><RotateCcw size={13} /> Refresh</button>
        <button type="button" onClick={openExecutablePage}>Open list</button>
      </div>
      {state === "failed" ? <div className="wf-alert compact"><AlertCircle size={14} /> {error}</div> : null}
      {status ? <ExecutableRunStatusLine status={status} run={lastRun} compact /> : null}
      {state === "loading" ? <p className="wf-muted">Loading artifacts...</p> : null}
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
                <button type="button" onClick={() => void run(artifact)}><Play size={13} /> Run</button>
                {explainExecutableAction ? <button type="button" onClick={() => explain(artifact)}><Sparkles size={13} /> Explain</button> : null}
              </div>
            </article>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function WorkflowInstances({ context }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
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

interface WorkflowInstanceInspectionData {
  details: WorkflowInstanceDetails;
  definitionVersion: WorkflowDefinitionVersionDetails | null;
  definitionVersionError: string;
  activityCatalog: ActivityCatalogItem[];
}

function WorkflowInstanceDetailsWorkbench({ context, ai, workflowExecutionId }: {
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

type InstanceInspectorTab = "timeline" | "issues" | "details";

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

function WorkflowStatusBadge({ status, subStatus }: { status: string; subStatus?: string | null }) {
  return <span className="wf-status-badge" data-status={status.toLowerCase()}>{subStatus ? `${status} · ${subStatus}` : status}</span>;
}

function formatRunKind(runKind?: string | null) {
  switch (normalizeRunKind(runKind)) {
    case "testrun":
      return "Test Run";
    case "publishedrun":
      return "Published Run";
    case "backgroundweaverrun":
      return "Background Weaver Run";
    default:
      return "Unknown / legacy";
  }
}

function normalizeRunKind(runKind?: string | null) {
  return (runKind ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}

function getVisibleWorkflowGraphNodeIds(definitionVersion: WorkflowDefinitionVersionDetails, activityCatalog: ActivityCatalogItem[]) {
  const root = definitionVersion.state.rootActivity;
  if (!root) return new Set<string>();

  const rootCatalogItem = activityCatalog.find(activity => activity.activityVersionId === root.activityVersionId);
  if (getActivityDesignerSupport(root, rootCatalogItem) === "unsupported") return new Set([root.nodeId]);

  const scope = resolveScope(root, []);
  return new Set(scope?.slot.activities.map(activity => activity.nodeId) ?? [root.nodeId]);
}

function activityNodeKey(activity: ActivityExecutionStateSummary) {
  return activity.authoredActivityId || activity.executableNodeId;
}

function DefinitionPager({ page, pageSize, totalCount, onPageChange, onPageSizeChange }: {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange(page: number): void;
  onPageSizeChange(pageSize: number): void;
}) {
  const totalPages = getTotalPages(totalCount, pageSize);
  const firstItem = totalCount === 0 ? 0 : ((page - 1) * pageSize) + 1;
  const lastItem = Math.min(page * pageSize, totalCount);

  return (
    <div className="wf-pagination" aria-label="Workflow definition pagination">
      <span className="wf-pagination-summary" aria-live="polite">
        Showing {firstItem}-{lastItem} of {totalCount}
      </span>
      <label className="wf-page-size">
        Rows
        <select value={pageSize} onChange={event => onPageSizeChange(Number(event.target.value))}>
          {definitionPageSizes.map(size => <option key={size} value={size}>{size}</option>)}
        </select>
      </label>
      <div className="wf-page-controls">
        <button type="button" onClick={() => onPageChange(page - 1)} disabled={page <= 1} aria-label="Previous page" title="Previous page">
          <ChevronLeft size={14} /> Previous
        </button>
        <span>Page {page} of {totalPages}</span>
        <button type="button" onClick={() => onPageChange(page + 1)} disabled={page >= totalPages} aria-label="Next page" title="Next page">
          Next <ChevronRight size={14} />
        </button>
      </div>
    </div>
  );
}

function pageItems<T>(items: T[], page: number, pageSize: number) {
  return items.slice((page - 1) * pageSize, page * pageSize);
}

function getTotalPages(totalCount: number, pageSize: number) {
  return Math.max(1, Math.ceil(totalCount / pageSize));
}

function findAiAction(ai: StudioAiContributionApi, id: string) {
  return ai.promptActions.list().find(action => action.id === id) ?? null;
}

function dispatchAiAction<TContext>(ai: StudioAiContributionApi, action: StudioAiPromptActionContribution<TContext>, context: TContext) {
  const prompt = action.createPrompt(context);
  if (!prompt) return false;

  ai.dispatchPrompt(prompt);
  return true;
}

function groupCreateRootActivities(activities: ActivityCatalogItem[]) {
  const flowchartActivity = findRootKindActivity(activities, "flowchart");
  const sequenceActivity = findRootKindActivity(activities, "sequence");
  const compositeRoots = [
    { value: flowchartActivity?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: sequenceActivity?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ];
  const categories = new Map<string, ActivityCatalogItem[]>();

  for (const activity of activities.filter(isActivityBrowsable)) {
    if (isCompositeRootActivity(activity)) continue;
    const category = activity.category || "Uncategorized";
    categories.set(category, [...(categories.get(category) ?? []), activity]);
  }

  const otherCategories = Array.from(categories.entries())
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([name, categoryActivities]) => ({
      name,
      activities: categoryActivities.sort((left, right) => getActivityDisplay(left).localeCompare(getActivityDisplay(right)))
    }));

  return { compositeRoots, otherCategories };
}

function getSelectedRootValue(draft: CreateWorkflowDraft, activities: ActivityCatalogItem[]) {
  return draft.rootActivityVersionId ?? findRootKindActivity(activities, draft.rootKind)?.activityVersionId ?? `kind:${draft.rootKind}`;
}

function getCreateRootActivityVersionId(draft: CreateWorkflowDraft, activities: ActivityCatalogItem[]) {
  return draft.rootActivityVersionId ?? findRootKindActivity(activities, draft.rootKind)?.activityVersionId ?? null;
}

function findRootKindActivity(activities: ActivityCatalogItem[], rootKind: CreateWorkflowKind) {
  return activities.find(activity => getRootKind(activity) === rootKind);
}

function getRootKind(activity: ActivityCatalogItem | undefined) {
  if (!activity) {
    return null;
  }

  if (isFlowchartActivity(activity)) {
    return "flowchart";
  }

  if (isSequenceActivity(activity)) {
    return "sequence";
  }

  return null;
}

function groupActivityPalette(activities: ActivityCatalogItem[]): ActivityPaletteGroup[] {
  const categories = new Map<string, ActivityCatalogItem[]>();

  for (const activity of activities) {
    const category = activity.category?.trim() || "Uncategorized";
    categories.set(category, [...(categories.get(category) ?? []), activity]);
  }

  return Array.from(categories.entries())
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([category, categoryActivities]) => ({
      category,
      activities: categoryActivities.sort((left, right) => getActivityDisplay(left).localeCompare(getActivityDisplay(right)))
    }));
}

function isCompositeRootActivity(activity: ActivityCatalogItem) {
  return isFlowchartActivity(activity) || isSequenceActivity(activity);
}

function isFlowchartActivity(activity: ActivityCatalogItem) {
  const displayName = getActivityDisplay(activity);
  return displayName === "Flowchart" || activity.activityTypeKey.endsWith(".Flowchart");
}

function isSequenceActivity(activity: ActivityCatalogItem) {
  const displayName = getActivityDisplay(activity);
  return displayName === "Sequence" || activity.activityTypeKey.endsWith(".Sequence");
}

function isActivityBrowsable(activity: ActivityCatalogItem) {
  return (activity as { isBrowsable?: unknown }).isBrowsable !== false && (activity as { browsable?: unknown }).browsable !== false;
}

function formatExecutableRoot(executable: WorkflowExecutableSummary) {
  const rootName = formatActivityTypeName(executable.rootActivityType);
  return rootName || executable.rootActivityType;
}

function executableMatchesDefinitionFilter(executable: WorkflowExecutableSummary, normalizedFilter: string) {
  return [
    executable.definitionId,
    executable.definitionVersionId,
    executable.sourceId,
    executable.sourceVersion
  ].some(value => value?.toLowerCase().includes(normalizedFilter));
}

function executableBelongsToDefinition(executable: WorkflowExecutableSummary, definitionId: string) {
  return executable.definitionId === definitionId || executable.sourceId === definitionId;
}

function compareExecutablesByPublishedDate(left: WorkflowExecutableSummary, right: WorkflowExecutableSummary) {
  return getExecutablePublishedTime(right) - getExecutablePublishedTime(left);
}

function getExecutablePublishedTime(executable: WorkflowExecutableSummary) {
  const value = executable.publishedAt ?? executable.createdAt;
  const time = value ? new Date(value).getTime() : 0;
  return Number.isNaN(time) ? 0 : time;
}

function formatExecutableSourceKind(sourceKind?: string | null) {
  const normalized = sourceKind?.trim().toLowerCase() ?? "";

  if (!normalized || normalized === "definition" || normalized === "workflowdefinition") {
    return "Definition";
  }

  if (normalized === "definitionversion" || normalized === "workflowdefinitionversion") {
    return "Definition version";
  }

  return sourceKind!
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[-_]+/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/\b\w/g, letter => letter.toUpperCase());
}

function readExecutableRunWorkflowExecutionId(response: WorkflowExecutableRunResponse | null | undefined) {
  const workflowExecutionId = response?.workflowExecutionId ?? response?.runId ?? response?.executionId;
  return typeof workflowExecutionId === "string" && workflowExecutionId.trim() ? workflowExecutionId : null;
}

async function copyTextToClipboard(value: string) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(value);
    return;
  }

  const textArea = document.createElement("textarea");
  textArea.value = value;
  textArea.setAttribute("readonly", "");
  textArea.style.position = "fixed";
  textArea.style.opacity = "0";
  document.body.appendChild(textArea);
  textArea.select();
  const copied = document.execCommand("copy");
  textArea.remove();

  if (!copied) {
    throw new Error("Clipboard copy failed.");
  }
}

function formatActivityTypeName(typeName: string) {
  return typeName.split(".").filter(Boolean).at(-1) ?? typeName;
}

function indexActivityDescriptors(descriptors: StudioActivityDescriptor[]) {
  const index = new Map<string, StudioActivityDescriptor>();

  for (const descriptor of descriptors) {
    addDescriptorIndex(index, descriptor.typeName, descriptor);
    addDescriptorIndex(index, descriptor.name, descriptor);
    addDescriptorIndex(index, descriptor.displayName, descriptor);
    const shortName = descriptor.typeName.split(".").filter(Boolean).at(-1);
    addDescriptorIndex(index, shortName, descriptor);
  }

  return index;
}

function resolveActivityDescriptor(
  activity: ActivityNode,
  catalogByVersion: Map<string, ActivityCatalogItem>,
  descriptorsByType: Map<string, StudioActivityDescriptor>
) {
  const catalogItem = catalogByVersion.get(activity.activityVersionId);
  return descriptorsByType.get(normalizeDescriptorKey(catalogItem?.activityTypeKey)) ??
    descriptorsByType.get(normalizeDescriptorKey(shortTypeName(catalogItem?.activityTypeKey))) ??
    descriptorsByType.get(normalizeDescriptorKey(catalogItem?.displayName)) ??
    descriptorsByType.get(normalizeDescriptorKey(activity.activityVersionId)) ??
    null;
}

function addDescriptorIndex(index: Map<string, StudioActivityDescriptor>, key: string | null | undefined, descriptor: StudioActivityDescriptor) {
  const normalized = normalizeDescriptorKey(key);
  if (normalized && !index.has(normalized)) index.set(normalized, descriptor);
}

function normalizeDescriptorKey(key: string | null | undefined) {
  return key?.trim().toLowerCase() ?? "";
}

function readStoredNumber(key: string, fallback: number, min: number, max: number) {
  const storage = getLocalStorage();
  if (!storage) return fallback;

  const storedValue = storage.getItem(key);
  if (storedValue == null) return fallback;

  const value = Number(storedValue);
  return Number.isFinite(value) ? clamp(value, min, max) : fallback;
}

function readStoredBoolean(key: string, fallback: boolean) {
  const storage = getLocalStorage();
  if (!storage) return fallback;

  const value = storage.getItem(key);
  if (value === "true") return true;
  if (value === "false") return false;
  return fallback;
}

function readStoredMaximizedSide(): WorkflowSidePanel | null {
  const storage = getLocalStorage();
  if (!storage) return null;

  const value = storage.getItem(workflowSidePanelMaximizedStorageKey);
  return value === "palette" || value === "inspector" ? value : null;
}

function getLocalStorage(): Pick<Storage, "getItem" | "setItem" | "removeItem"> | null {
  if (typeof window === "undefined") return null;

  const storage = window.localStorage as Partial<Storage> | undefined;
  return storage &&
    typeof storage.getItem === "function" &&
    typeof storage.setItem === "function" &&
    typeof storage.removeItem === "function"
    ? storage as Pick<Storage, "getItem" | "setItem" | "removeItem">
    : null;
}

function writeStoredValue(key: string, value: string | null) {
  const storage = getLocalStorage();
  if (!storage) return;

  if (value == null) {
    storage.removeItem(key);
  } else {
    storage.setItem(key, value);
  }
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, Math.round(value)));
}

type WorkflowSidePanel = "palette" | "inspector";

interface WorkflowEditorPanelTab {
  id: string;
  title: string;
  order: number;
  icon: React.ReactNode;
  render(): React.ReactNode;
}

interface WorkflowDesignerPanelContext {
  definition: WorkflowDefinitionDetails["definition"];
  draft: WorkflowDraft;
  selectedActivity: ActivityNode | null;
  selectedActivityDescriptor: StudioActivityDescriptor | null;
  selectedActivitySlots: ReturnType<typeof getChildSlots>;
  catalog: ActivityCatalogItem[];
  currentScopeOwner: ActivityNode | null;
  frames: ScopeFrame[];
}

function WorkflowEditor({
  context,
  definitionId,
  ai,
  propertyEditors,
  expressionEditors,
  workflowDesignerPanels,
  onBack
}: {
  context: StudioEndpointContext;
  definitionId: string;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  workflowDesignerPanels: StudioWorkflowDesignerPanelContribution[];
  onBack(): void;
}) {
  const [details, setDetails] = useState<WorkflowDefinitionDetails | null>(null);
  const [draft, setDraft] = useState<WorkflowDraft | null>(null);
  const [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
  const [activityDescriptors, setActivityDescriptors] = useState<StudioActivityDescriptor[]>([]);
  const [availabilityDiagnostics, setAvailabilityDiagnostics] = useState<ActivityAvailabilityDiagnostics | null>(null);
  const [expressionDescriptors, setExpressionDescriptors] = useState<StudioExpressionDescriptor[]>(fallbackExpressionDescriptors);
  const [descriptorStatus, setDescriptorStatus] = useState<"loading" | "ready" | "failed">("loading");
  const [frames, setFrames] = useState<ScopeFrame[]>([]);
  const [nodes, setNodes] = useState<Node<WorkflowNodeData>[]>([]);
  const [edges, setEdges] = useState<WorkflowEdge[]>([]);
  const [reactFlowInstance, setReactFlowInstance] = useState<ReactFlowInstance<Node<WorkflowNodeData>, WorkflowEdge> | null>(null);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [connectMenu, setConnectMenu] = useState<ConnectMenuState | null>(null);
  const [highlightedEdgeId, setHighlightedEdgeId] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [operation, setOperation] = useState<WorkflowEditorOperation>("idle");
  const [testRun, setTestRun] = useState<WorkflowTestRunState | null>(null);
  const [autosaveEnabled, setAutosaveEnabled] = useState(false);
  const [publishedArtifactId, setPublishedArtifactId] = useState<string | null>(null);
  const [expandedPaletteCategories, setExpandedPaletteCategories] = useState<Set<string>>(() => new Set());
  const [paletteSearch, setPaletteSearch] = useState("");
  const [paletteWidth, setPaletteWidth] = useState(() => readStoredNumber(workflowPaletteWidthStorageKey, defaultPaletteWidth, minPaletteWidth, maxPaletteWidth));
  const [inspectorWidth, setInspectorWidth] = useState(() => readStoredNumber(workflowInspectorWidthStorageKey, defaultInspectorWidth, minInspectorWidth, maxInspectorWidth));
  const [paletteCollapsed, setPaletteCollapsed] = useState(() => readStoredBoolean(workflowPaletteCollapsedStorageKey, false));
  const [inspectorCollapsed, setInspectorCollapsed] = useState(() => readStoredBoolean(workflowInspectorCollapsedStorageKey, false));
  const [maximizedSidePanel, setMaximizedSidePanel] = useState<WorkflowSidePanel | null>(readStoredMaximizedSide);
  const [activeLeftPanelId, setActiveLeftPanelId] = useState("activities");
  const [activeRightPanelId, setActiveRightPanelId] = useState("inspector");
  const [canvasView, setCanvasView] = useState<CanvasView>("designer");
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const connectSourceRef = useRef<WorkflowConnectSource | null>(null);
  const lastSavedDraftSignatureRef = useRef("");
  const saveRequestIdRef = useRef(0);
  const saveQueueRef = useRef<Promise<unknown>>(Promise.resolve());
  const workflowBatchUndoRef = useRef(new Map<string, WorkflowDraft>());
  // User-facing undo/redo. Snapshots are whole drafts; nodes/edges rebuild from them.
  // This is intentionally separate from the Weaver token-addressed batch undo above —
  // both flow through setDraft, so a Weaver change is also a normal undo step here.
  const historyRef = useRef<HistoryState<WorkflowDraft>>(createHistory());
  const lastCapturedDraftRef = useRef<WorkflowDraft | null>(null);
  const lastCapturedSignatureRef = useRef("");
  const restoringRef = useRef(false);
  const [historyVersion, setHistoryVersion] = useState(0);
  const pointerDragRef = useRef<{
    activity: ActivityCatalogItem;
    startX: number;
    startY: number;
    dragging: boolean;
  } | null>(null);
  const nativePaletteDragRef = useRef<{ activityVersionId: string; handledDrop: boolean } | null>(null);
  const suppressPaletteClickRef = useRef(false);

  const root = draft?.state.rootActivity ?? null;
  const catalogByVersion = useMemo(() => new Map(catalog.map(activity => [activity.activityVersionId, activity])), [catalog]);
  const availabilityLookup = useCallback<WorkflowNodeAvailabilityLookup>(
    input => findActivityAvailabilityDiagnostic([input.activityVersionId, input.activityTypeKey], availabilityDiagnostics),
    [availabilityDiagnostics]
  );
  const descriptorsByType = useMemo(() => indexActivityDescriptors(activityDescriptors), [activityDescriptors]);
  const scopeOwner = useMemo(() => resolveScopeOwner(root, frames), [root, frames]);
  const designerSupport = getActivityDesignerSupport(scopeOwner, scopeOwner ? catalogByVersion.get(scopeOwner.activityVersionId) : undefined);
  const isUnsupportedDesigner = !!scopeOwner && designerSupport === "unsupported";
  const scope = useMemo(() => isUnsupportedDesigner ? null : resolveScope(root, frames), [root, frames, isUnsupportedDesigner]);
  const paletteGroups = useMemo(() => groupActivityPalette(catalog), [catalog]);
  const filteredPaletteGroups = useMemo(() => {
    const term = paletteSearch.trim().toLowerCase();
    if (!term) return paletteGroups;
    const matches = catalog.filter(activity =>
      getActivityDisplay(activity).toLowerCase().includes(term) ||
      activity.activityTypeKey.toLowerCase().includes(term) ||
      (activity.category ?? "").toLowerCase().includes(term) ||
      (activity.description ?? "").toLowerCase().includes(term));
    return groupActivityPalette(matches);
  }, [catalog, paletteSearch, paletteGroups]);
  const selectedNode = useMemo(() => {
    if (isUnsupportedDesigner && scopeOwner?.nodeId === selectedNodeId) return scopeOwner;
    return scope?.slot.activities.find(activity => activity.nodeId === selectedNodeId) ?? null;
  }, [isUnsupportedDesigner, scope, scopeOwner, selectedNodeId]);
  const selectedDescriptor = useMemo(
    () => selectedNode ? resolveActivityDescriptor(selectedNode, catalogByVersion, descriptorsByType) : null,
    [catalogByVersion, descriptorsByType, selectedNode]
  );
  const selectedNodeAvailability = useMemo(
    () => selectedNode
      ? availabilityLookup({ activityVersionId: selectedNode.activityVersionId, activityTypeKey: catalogByVersion.get(selectedNode.activityVersionId)?.activityTypeKey })
      : null,
    [availabilityLookup, catalogByVersion, selectedNode]
  );
  const selectedSlots = selectedNode ? getChildSlots(selectedNode) : [];
  const isFlowchartDesigner = designerSupport === "flowchart" && scope?.slot.mode === "flowchart";
  const canAddActivitiesToCanvas = !root || !isUnsupportedDesigner;
  const busy = operation !== "idle";
  const canRunTest = !!draft?.state.rootActivity && !busy;
  const findRisksAction = findAiAction(ai, "weaver.workflows.find-draft-risks");
  const proposeUpdateAction = findAiAction(ai, "weaver.workflows.propose-update");

  useEffect(() => {
    if (!details || !draft) return;

    window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
      workflowId: details.definition.id,
      workflowDefinitionId: details.definition.id,
      workflowVersionId: draft.sourceVersionId ?? null,
      draftId: draft.id,
      revision: getDraftRevision(draft),
      selectedNodeId,
      selectedActivityType: selectedDescriptor?.typeName ?? (selectedNode ? catalogByVersion.get(selectedNode.activityVersionId)?.activityTypeKey ?? selectedNode.activityVersionId : null),
      summary: details.definition.name,
      activities: collectWorkflowContextActivities(draft.state.rootActivity, catalogByVersion),
      connections: collectWorkflowContextConnections(draft.state.rootActivity),
      diagnostics: draft.validationErrors.map(error => ({ severity: error.code ?? "warning", message: error.message ?? "Workflow validation issue." }))
    };

    return () => {
      if (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === details.definition.id) {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = undefined;
      }
    };
  }, [catalogByVersion, details, draft, selectedDescriptor, selectedNode, selectedNodeId]);

  useEffect(() => {
    const handleApply = (event: Event) => {
      const detail = (event as CustomEvent<{
        batch: WorkflowGraphOperationBatch;
        respond(result: { ok: true; result: { appliedCount: number; finalActivityIds: string[]; temporaryReferences: Record<string, string>; summary: string; undoToken: string } } | { ok: false; message: string }): void;
      }>).detail;
      if (!detail?.batch || !detail.respond) return;
      if (!draft || !details) {
        detail.respond({ ok: false, message: "No active workflow draft is open." });
        return;
      }

      const batchWorkflowId = detail.batch.workflowDefinitionId;
      if (batchWorkflowId && batchWorkflowId !== "active-draft" && batchWorkflowId !== details.definition.id) {
        detail.respond({ ok: false, message: `Batch targets workflow '${batchWorkflowId}', but '${details.definition.id}' is active.` });
        return;
      }

      try {
        const previousDraft = cloneWorkflowDraftForUndo(draft);
        const applied = applyWorkflowGraphOperationBatch(draft, detail.batch, catalog);
        const undoToken = `weaver-batch-${Date.now()}`;
        workflowBatchUndoRef.current.set(undoToken, previousDraft);
        setDraft(applied.draft);
        setFrames([]);
        setSelectedNodeId(applied.finalActivityIds.at(-1) ?? null);
        setPublishedArtifactId(null);
        setTestRun(null);
        setStatus(applied.summary);
        setError("");
        detail.respond({ ok: true, result: { ...applied, undoToken } });
      } catch (e) {
        const message = e instanceof Error ? e.message : String(e);
        setError(message);
        detail.respond({ ok: false, message });
      }
    };

    const handleUndo = (event: Event) => {
      const detail = (event as CustomEvent<{
        undoToken: string;
        respond(result: { ok: true; summary: string } | { ok: false; message: string }): void;
      }>).detail;
      if (!detail?.undoToken || !detail.respond) return;

      const previousDraft = workflowBatchUndoRef.current.get(detail.undoToken);
      if (!previousDraft) {
        detail.respond({ ok: false, message: "The Weaver batch undo point is no longer available." });
        return;
      }

      workflowBatchUndoRef.current.delete(detail.undoToken);
      setDraft(previousDraft);
      setFrames([]);
      setSelectedNodeId(null);
      setPublishedArtifactId(null);
      setTestRun(null);
      setStatus("Restored workflow draft before Weaver batch.");
      setError("");
      detail.respond({ ok: true, summary: "Restored workflow draft before Weaver batch." });
    };

    window.addEventListener(workflowGraphOperationApplyEvent, handleApply);
    window.addEventListener(workflowGraphOperationUndoEvent, handleUndo);
    return () => {
      window.removeEventListener(workflowGraphOperationApplyEvent, handleApply);
      window.removeEventListener(workflowGraphOperationUndoEvent, handleUndo);
    };
  }, [catalog, details, draft]);

  useEffect(() => {
    writeStoredValue(workflowPaletteWidthStorageKey, String(paletteWidth));
  }, [paletteWidth]);

  useEffect(() => {
    writeStoredValue(workflowInspectorWidthStorageKey, String(inspectorWidth));
  }, [inspectorWidth]);

  useEffect(() => {
    writeStoredValue(workflowPaletteCollapsedStorageKey, String(paletteCollapsed));
  }, [paletteCollapsed]);

  useEffect(() => {
    writeStoredValue(workflowInspectorCollapsedStorageKey, String(inspectorCollapsed));
  }, [inspectorCollapsed]);

  useEffect(() => {
    writeStoredValue(workflowSidePanelMaximizedStorageKey, maximizedSidePanel);
  }, [maximizedSidePanel]);

  useEffect(() => {
    if (!maximizedSidePanel) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setMaximizedSidePanel(null);
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [maximizedSidePanel]);

  const load = useCallback(async () => {
    setError("");
    setDescriptorStatus("loading");
    const [nextDetails, nextCatalog, nextDescriptors, nextExpressions, nextAvailability] = await Promise.all([
      getDefinition(context, definitionId),
      listActivities(context),
      listActivityDescriptors(context).then(
        descriptors => ({ ok: true as const, descriptors }),
        () => ({ ok: false as const, descriptors: [] as StudioActivityDescriptor[] })
      ),
      listExpressionDescriptors(context).then(
        descriptors => ({ ok: true as const, descriptors }),
        () => ({ ok: false as const, descriptors: fallbackExpressionDescriptors })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      listActivityAvailabilityDiagnostics(context).then(
        diagnostics => diagnostics,
        () => null
      )
    ]);
    const nextDraft = nextDetails.draft ?? null;
    setDetails(nextDetails);
    lastSavedDraftSignatureRef.current = nextDraft ? getDraftSignature(nextDraft) : "";
    historyRef.current = createHistory();
    lastCapturedDraftRef.current = nextDraft ? cloneWorkflowDraftForUndo(nextDraft) : null;
    lastCapturedSignatureRef.current = nextDraft ? getDraftSignature(nextDraft) : "";
    restoringRef.current = false;
    setHistoryVersion(0);
    setDraft(nextDraft);
    setCatalog(nextCatalog.activities ?? []);
    setActivityDescriptors(nextDescriptors.descriptors);
    setAvailabilityDiagnostics(nextAvailability);
    setExpressionDescriptors(nextExpressions.descriptors.length > 0 ? nextExpressions.descriptors : fallbackExpressionDescriptors);
    setDescriptorStatus(nextDescriptors.ok ? "ready" : "failed");
    setFrames([]);
    setSelectedNodeId(null);
  }, [context, definitionId]);

  useEffect(() => {
    void load().catch(e => setError(e instanceof Error ? e.message : String(e)));
  }, [load]);

  useEffect(() => {
    setExpandedPaletteCategories(current => {
      let changed = false;
      const next = new Set(current);

      for (const group of paletteGroups) {
        if (!next.has(group.category)) {
          next.add(group.category);
          changed = true;
        }
      }

      return changed ? next : current;
    });
  }, [paletteGroups]);

  useEffect(() => {
    if (!scopeOwner) {
      setNodes([]);
      setEdges([]);
      return;
    }

    const canvas = isUnsupportedDesigner
      ? buildUnsupportedActivityCanvas(scopeOwner, catalog, draft?.layout ?? [])
      : scope
        ? buildCanvas(scope, catalog, draft?.layout ?? [])
        : { nodes: [], edges: [] };
    setNodes(canvas.nodes);
    setEdges(canvas.edges as WorkflowEdge[]);
  }, [catalog, draft?.layout, isUnsupportedDesigner, scope, scopeOwner]);

  const setRoot = (rootActivity: ActivityNode) => {
    setDraft(current => current ? { ...current, state: { ...current.state, rootActivity } } : current);
  };

  const addActivity = useCallback((activity: ActivityCatalogItem, position?: XYPosition) => {
    if (draft?.state.rootActivity && isUnsupportedDesigner) {
      return;
    }

    const next = createActivityNode(activity, createNodeId(activity));
    if (!draft?.state.rootActivity) {
      setRoot(next);
      setSelectedNodeId(next.nodeId);
      return;
    }

    if (!scope) {
      const childSlot = getChildSlots(next)[0];
      if (!childSlot) {
        setStatus("");
        setError("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }

      setDraft(current => {
        if (!current?.state.rootActivity) return current;

        const existingRoot = current.state.rootActivity;
        const wrappedRoot = updateScopeActivities(next, [], [existingRoot]);
        const layout = position
          ? [
              ...current.layout.filter(record => record.nodeId !== existingRoot.nodeId),
              {
                nodeId: existingRoot.nodeId,
                x: Math.round(position.x),
                y: Math.round(position.y)
              }
            ]
          : current.layout;

        return {
          ...current,
          layout,
          state: {
            ...current.state,
            rootActivity: wrappedRoot
          }
        };
      });
      setSelectedNodeId(draft.state.rootActivity.nodeId);
      setError("");
      setStatus(`Wrapped root in ${getActivityDisplay(activity)}`);
      return;
    }

    setDraft(current => {
      if (!current?.state.rootActivity) return current;

      const currentScope = resolveScope(current.state.rootActivity, frames);
      if (!currentScope) return current;

      const updatedRoot = updateScopeActivities(current.state.rootActivity, frames, [...currentScope.slot.activities, next]);
      const layout = position
        ? [
            ...current.layout.filter(record => record.nodeId !== next.nodeId),
            {
              nodeId: next.nodeId,
              x: Math.round(position.x),
              y: Math.round(position.y)
            }
          ]
        : current.layout;

      return {
        ...current,
        layout,
        state: {
          ...current.state,
          rootActivity: updatedRoot
        }
      };
    });
    setSelectedNodeId(next.nodeId);
  }, [draft?.state.rootActivity, frames, isUnsupportedDesigner, scope]);

  const createCanvasActivity = useCallback((activity: ActivityCatalogItem, position: XYPosition) => {
    const activityNode = createActivityNode(activity, createNodeId(activity));
    const node: Node<WorkflowNodeData> = {
      id: activityNode.nodeId,
      type: "workflowActivity",
      position,
      selected: true,
      data: {
        label: getActivityDisplay(activity),
        activityVersionId: activity.activityVersionId,
        activityTypeKey: activity.activityTypeKey,
        category: activity.category,
        executionType: activity.executionType,
        icon: resolveActivityIcon(activity),
        childSlots: getChildSlots(activityNode),
        acceptsInbound: String(activity.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: getActivitySourcePorts(activityNode, activity)
      }
    };

    return { activityNode, node };
  }, []);

  const commitCanvas = useCallback((nextNodes: Node<WorkflowNodeData>[], nextEdges: WorkflowEdge[], additionalActivities: ActivityNode[] = []) => {
    if (isUnsupportedDesigner) return;

    setDraft(current => {
      if (!current) return current;

      const nextLayout = updateLayout(current.layout, nextNodes);
      const rootActivity = current.state.rootActivity;
      if (!rootActivity) return { ...current, layout: nextLayout };

      const currentScope = resolveScope(rootActivity, frames);
      if (!currentScope) return { ...current, layout: nextLayout };

      const ownerWithActivities = syncCanvasToScope(currentScope, nextNodes, nextEdges, additionalActivities);
      const nextOwner = currentScope.slot.mode === "flowchart"
        ? withFlowchartConnections(ownerWithActivities, nextEdges)
        : ownerWithActivities;

      return {
        ...current,
        layout: nextLayout,
        state: {
          ...current.state,
          rootActivity: updateScopeOwner(rootActivity, frames, nextOwner)
        }
      };
    });
  }, [frames, isUnsupportedDesigner]);

  const toCanvasPosition = useCallback((clientX: number, clientY: number): XYPosition | null => {
    if (!canvasRef.current) return null;

    const canvasRect = canvasRef.current.getBoundingClientRect();
    if (!reactFlowInstance) {
      return {
        x: clientX - canvasRect.left,
        y: clientY - canvasRect.top
      };
    }

    return reactFlowInstance.screenToFlowPosition({ x: clientX, y: clientY });
  }, [reactFlowInstance]);

  const findEdgeUnderCursor = useCallback((clientX: number, clientY: number) => {
    const element = document.elementFromPoint(clientX, clientY) as HTMLElement | null;
    const edgeElement = element?.closest(".react-flow__edge") as HTMLElement | null;
    return edgeElement?.getAttribute("data-id") ?? null;
  }, []);

  const spliceActivityIntoEdge = useCallback((activity: ActivityCatalogItem, edge: WorkflowEdge, fallbackPosition: XYPosition) => {
    const sourceNode = nodes.find(node => node.id === edge.source);
    const targetNode = nodes.find(node => node.id === edge.target);
    const position = sourceNode && targetNode
      ? midpointBetween(sourceNode, targetNode)
      : sourceNode
        ? rightOf(sourceNode)
        : fallbackPosition;
    const placed = createCanvasActivity(activity, position);
    const clearedNodes = nodes.map(node => node.selected ? { ...node, selected: false } : node);
    const nextNodes = [...clearedNodes, placed.node];
    const nextEdges = spliceWorkflowEdge(edges, edge, placed.node.id);

    setNodes(nextNodes);
    setEdges(nextEdges);
    setSelectedNodeId(placed.node.id);
    commitCanvas(nextNodes, nextEdges, [placed.activityNode]);
  }, [commitCanvas, createCanvasActivity, edges, nodes]);

  const tryAddActivityAtClientPoint = useCallback((activity: ActivityCatalogItem, clientX: number, clientY: number) => {
    if (!canAddActivitiesToCanvas) return false;
    if (!canvasRef.current) return false;

    const canvasRect = canvasRef.current.getBoundingClientRect();
    const isOverCanvas =
      clientX >= canvasRect.left &&
      clientX <= canvasRect.right &&
      clientY >= canvasRect.top &&
      clientY <= canvasRect.bottom;

    if (!isOverCanvas) return false;

    const position = toCanvasPosition(clientX, clientY);
    if (!position) return false;

    if (isFlowchartDesigner) {
      const edgeId = findEdgeUnderCursor(clientX, clientY);
      const edge = edgeId ? edges.find(candidate => candidate.id === edgeId) : undefined;
      if (edge) {
        spliceActivityIntoEdge(activity, edge, position);
        return true;
      }
    }

    addActivity(activity, position);
    return true;
  }, [addActivity, canAddActivitiesToCanvas, edges, findEdgeUnderCursor, isFlowchartDesigner, spliceActivityIntoEdge, toCanvasPosition]);

  useEffect(() => {
    const onPointerMove = (event: PointerEvent) => {
      const drag = pointerDragRef.current;
      if (!drag) return;

      const distance = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY);
      if (distance >= pointerDragThreshold) drag.dragging = true;
    };

    const onPointerUp = (event: PointerEvent) => {
      const drag = pointerDragRef.current;
      pointerDragRef.current = null;
      if (!drag?.dragging || !canvasRef.current) return;
      if (nativePaletteDragRef.current) return;

      const canvasRect = canvasRef.current.getBoundingClientRect();
      const isOverCanvas =
        event.clientX >= canvasRect.left &&
        event.clientX <= canvasRect.right &&
        event.clientY >= canvasRect.top &&
        event.clientY <= canvasRect.bottom;

      if (!isOverCanvas) return;

      suppressPaletteClickRef.current = true;
      window.setTimeout(() => {
        suppressPaletteClickRef.current = false;
      }, 0);

      tryAddActivityAtClientPoint(drag.activity, event.clientX, event.clientY);
    };

    window.addEventListener("pointermove", onPointerMove);
    window.addEventListener("pointerup", onPointerUp);
    window.addEventListener("pointercancel", onPointerUp);

    return () => {
      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUp);
      window.removeEventListener("pointercancel", onPointerUp);
    };
  }, [reactFlowInstance, tryAddActivityAtClientPoint]);

  const onPaletteDragStart = (event: React.DragEvent<HTMLButtonElement>, activity: ActivityCatalogItem) => {
    nativePaletteDragRef.current = { activityVersionId: activity.activityVersionId, handledDrop: false };
    event.dataTransfer.setData(activityDragDataType, activity.activityVersionId);
    event.dataTransfer.setData("text/plain", activity.activityVersionId);
    event.dataTransfer.effectAllowed = "copy";
  };

  const onPaletteDragEnd = (event: React.DragEvent<HTMLButtonElement>, activity: ActivityCatalogItem) => {
    const nativeDrag = nativePaletteDragRef.current;
    nativePaletteDragRef.current = null;
    if (nativeDrag?.handledDrop) return;
    if (event.clientX === 0 && event.clientY === 0) return;
    if (!tryAddActivityAtClientPoint(activity, event.clientX, event.clientY)) return;

    suppressPaletteClickRef.current = true;
    window.setTimeout(() => {
      suppressPaletteClickRef.current = false;
    }, 0);
  };

  const onPalettePointerDown = (event: React.PointerEvent<HTMLButtonElement>, activity: ActivityCatalogItem) => {
    if (event.button !== 0) return;
    pointerDragRef.current = {
      activity,
      startX: event.clientX,
      startY: event.clientY,
      dragging: false
    };
  };

  const onPaletteClick = (activity: ActivityCatalogItem) => {
    if (suppressPaletteClickRef.current) return;
    if (!canAddActivitiesToCanvas) return;
    addActivity(activity);
  };

  const onCanvasDragOver = (event: React.DragEvent) => {
    if (!canAddActivitiesToCanvas) {
      event.dataTransfer.dropEffect = "none";
      return;
    }

    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
    if (!isFlowchartDesigner) return;

    const edgeId = findEdgeUnderCursor(event.clientX, event.clientY);
    setHighlightedEdgeId(edgeId);
  };

  const onCanvasDragLeave = (event: React.DragEvent) => {
    if (!canvasRef.current) return;
    const related = event.relatedTarget as globalThis.Node | null;
    if (related && canvasRef.current.contains(related)) return;

    setHighlightedEdgeId(null);
  };

  const onCanvasDrop = (event: React.DragEvent) => {
    event.preventDefault();
    setHighlightedEdgeId(null);

    const activityVersionId = event.dataTransfer.getData(activityDragDataType) || event.dataTransfer.getData("text/plain");
    if (!activityVersionId) return;
    event.stopPropagation();
    if (nativePaletteDragRef.current?.activityVersionId === activityVersionId) {
      nativePaletteDragRef.current.handledDrop = true;
    }
    if (!canAddActivitiesToCanvas) return;

    const activity = catalogByVersion.get(activityVersionId);
    if (!activity) return;

    tryAddActivityAtClientPoint(activity, event.clientX, event.clientY);
  };

  const openEmptyConnectMenu = () => {
    if (!isFlowchartDesigner) return;

    const rect = canvasRef.current?.getBoundingClientRect();
    if (!rect) return;
    setConnectMenu({
      kind: "fromEmpty",
      clientX: rect.left + rect.width / 2,
      clientY: rect.top + rect.height / 2
    });
  };

  const saveDraft = useCallback(async (draftSnapshot: WorkflowDraft, savedStatus: string) => {
    const queuedSave = async () => {
      const requestId = ++saveRequestIdRef.current;
      const requestedSignature = getDraftSignature(draftSnapshot);
      setError("");
      try {
        const saved = await updateDraft(context, draftSnapshot);
        const savedSignature = getDraftSignature(saved);
        lastSavedDraftSignatureRef.current = savedSignature;
        setDraft(current => {
          if (!current || current.id !== saved.id) return current;
          return getDraftSignature(current) === requestedSignature
            ? saved
            : { ...current, validationErrors: saved.validationErrors };
        });
        if (requestId === saveRequestIdRef.current) setStatus(savedStatus);
        return saved;
      } catch (e) {
        if (requestId === saveRequestIdRef.current) {
          setStatus("");
          setError(e instanceof Error ? e.message : String(e));
        }
        throw e;
      }
    };
    const queued = saveQueueRef.current.then(queuedSave, queuedSave);
    saveQueueRef.current = queued.catch(() => undefined);
    return queued;
  }, [context]);

  useEffect(() => {
    if (!autosaveEnabled || !draft) return;

    const draftSignature = getDraftSignature(draft);
    if (draftSignature === lastSavedDraftSignatureRef.current) return;

    setStatus("Autosaving...");
    const timeoutId = window.setTimeout(() => {
      void saveDraft(draft, "Autosaved").catch(() => undefined);
    }, autosaveDelayMs);

    return () => window.clearTimeout(timeoutId);
  }, [autosaveEnabled, draft, saveDraft]);

  // Signature-driven history capture: records every committed mutation (palette add, drag,
  // property edit, auto-layout, code apply, Weaver batch) without touching each handler.
  useEffect(() => {
    if (!draft) return;
    if (restoringRef.current) {
      restoringRef.current = false;
      return;
    }
    const signature = getDraftSignature(draft);
    if (signature === lastCapturedSignatureRef.current) return;

    const timeoutId = window.setTimeout(() => {
      const previous = lastCapturedDraftRef.current;
      if (previous) {
        historyRef.current = pushSnapshot(historyRef.current, previous);
        setHistoryVersion(version => version + 1);
      }
      lastCapturedDraftRef.current = cloneWorkflowDraftForUndo(draft);
      lastCapturedSignatureRef.current = signature;
    }, historyCaptureDelayMs);

    return () => window.clearTimeout(timeoutId);
  }, [draft]);

  const exportJson = useCallback(() => {
    if (!draft) return;
    const name = details?.definition.name;
    downloadWorkflowJson(buildExportPayload(draft, name), name);
    setStatus("Exported workflow as JSON.");
  }, [draft, details]);

  const applyEditedJson = useCallback((text: string): string | null => {
    if (!draft) return "No draft is loaded.";
    const result = buildDraftFromJson(text, draft);
    if (!result.ok) return result.error;
    setDraft(result.draft);
    setSelectedNodeId(null);
    setFrames([]);
    setStatus("Applied workflow JSON.");
    return null;
  }, [draft]);

  // Collapses any pending (debounced-but-not-yet-captured) edit into the history stack so
  // undo/redo always operate on a consistent baseline.
  const flushPendingHistory = useCallback(() => {
    if (!draft) return;
    const signature = getDraftSignature(draft);
    if (signature === lastCapturedSignatureRef.current) return;
    const previous = lastCapturedDraftRef.current;
    if (previous) historyRef.current = pushSnapshot(historyRef.current, previous);
    lastCapturedDraftRef.current = cloneWorkflowDraftForUndo(draft);
    lastCapturedSignatureRef.current = signature;
  }, [draft]);

  const restoreDraftSnapshot = useCallback((snapshot: WorkflowDraft) => {
    restoringRef.current = true;
    lastCapturedDraftRef.current = cloneWorkflowDraftForUndo(snapshot);
    lastCapturedSignatureRef.current = getDraftSignature(snapshot);
    setDraft(snapshot);
    setSelectedNodeId(null);
    setFrames([]);
    setHistoryVersion(version => version + 1);
  }, []);

  const undo = useCallback(() => {
    if (!draft) return;
    flushPendingHistory();
    const step = undoHistory(historyRef.current, draft);
    if (!step) return;
    historyRef.current = step.history;
    restoreDraftSnapshot(step.snapshot);
  }, [draft, flushPendingHistory, restoreDraftSnapshot]);

  const redo = useCallback(() => {
    if (!draft) return;
    flushPendingHistory();
    const step = redoHistory(historyRef.current, draft);
    if (!step) return;
    historyRef.current = step.history;
    restoreDraftSnapshot(step.snapshot);
  }, [draft, flushPendingHistory, restoreDraftSnapshot]);

  const { canUndoNow, canRedoNow } = useMemo(() => {
    void historyVersion; // re-evaluate when the history stack mutates
    const hasPendingEdit = !!draft && !!lastCapturedDraftRef.current && getDraftSignature(draft) !== lastCapturedSignatureRef.current;
    return {
      canUndoNow: canUndo(historyRef.current) || hasPendingEdit,
      canRedoNow: canRedo(historyRef.current) && !hasPendingEdit
    };
  }, [draft, historyVersion]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (canvasView !== "designer") return;
      if (!(event.metaKey || event.ctrlKey)) return;
      const target = event.target as HTMLElement | null;
      if (target && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName))) return;
      const key = event.key.toLowerCase();
      if (key === "z" && !event.shiftKey) {
        event.preventDefault();
        undo();
      } else if ((key === "z" && event.shiftKey) || key === "y") {
        event.preventDefault();
        redo();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [canvasView, undo, redo]);

  const save = async () => {
    if (!draft || busy) return;
    setOperation("saving");
    setStatus("Saving...");
    try {
      await saveDraft(draft, "Saved");
    } catch {
      // saveDraft surfaces the error in the editor alert.
    } finally {
      setOperation("idle");
    }
  };

  const promoteAndPublish = async () => {
    if (!draft || busy) return;
    setOperation("promoting");
    setStatus("Promoting...");
    try {
      const promoted = await promoteDraft(context, draft.id);
      const published = await publishVersion(context, promoted.versionId);
      setPublishedArtifactId(published.artifactId);
      setStatus(`Published ${published.artifactVersion}`);
      await load();
    } catch (e) {
      setStatus("");
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setOperation("idle");
    }
  };

  const run = async () => {
    if (!draft?.state.rootActivity || busy) return;
    const draftSnapshot = draft;
    const draftSignature = getDraftSignature(draftSnapshot);
    setTestRun(null);
    setStatus("Preparing test run...");
    try {
      setOperation("testRunPreparing");
      setStatus("Preparing test run...");
      const snapshotId = createDraftSnapshotId(draftSnapshot);

      setOperation("testRunStarting");
      setStatus("Starting test run...");
      const nextTestRun = await startWorkflowDraftTestRun(context, {
        definitionId: draftSnapshot.definitionId,
        snapshotId,
        state: draftSnapshot.state
      });
      setTestRun({ draftSignature, view: nextTestRun });
      setActiveRightPanelId("runtime");
      setInspectorCollapsed(false);
      setStatus(isRejectedTestRun(nextTestRun) ? "Test run rejected" : "Test run dispatched");
    } catch (e) {
      setStatus("");
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setOperation("idle");
    }
  };

  const onNodesChange = (changes: NodeChange[]) => {
    const allowedChanges = isUnsupportedDesigner
      ? changes.filter(change => change.type === "select")
      : changes;
    if (allowedChanges.length === 0) return;
    setNodes(current => applyNodeChanges(allowedChanges, current));
  };
  const onEdgesChange = (changes: EdgeChange[]) => {
    if (isUnsupportedDesigner) return;
    setEdges(current => applyEdgeChanges(changes, current) as WorkflowEdge[]);
  };
  const isValidConnection = (connection: Connection | Edge) => {
    if (!connection.source || !connection.target) return false;
    if (connection.source === connection.target) return false;
    if (!isFlowchartDesigner) return false;
    return !connection.targetHandle;
  };
  const onConnect = (connection: Connection) => {
    if (!draft?.state.rootActivity || !scope || !isFlowchartDesigner) return;
    if (!isValidConnection(connection)) return;

    const nextEdge = createWorkflowEdge(connection.source, connection.target, connection.sourceHandle ?? "Done", connection.targetHandle ?? undefined);
    const nextEdges = addEdge(nextEdge, edges) as WorkflowEdge[];
    setEdges(nextEdges);
    commitCanvas(nodes, nextEdges);
  };
  const commitLayout = () => {
    commitCanvas(nodes, edges);
  };

  const canAutoLayout = !isUnsupportedDesigner && nodes.length > 0;
  const autoLayout = useCallback(() => {
    if (isUnsupportedDesigner || nodes.length === 0) return;
    const mode = scope?.slot.mode === "sequence" ? "sequence" : "flowchart";
    const positions = computeAutoLayout(nodes, edges, mode);
    const nextNodes = nodes.map(node => {
      const position = positions.get(node.id);
      return position ? { ...node, position } : node;
    });
    setNodes(nextNodes);
    commitCanvas(nextNodes, edges);
    window.requestAnimationFrame(() => reactFlowInstance?.fitView({ padding: 0.2 }));
    setStatus("Rearranged the canvas.");
  }, [edges, nodes, scope, isUnsupportedDesigner, commitCanvas, reactFlowInstance]);

  const onConnectStart: OnConnectStart = (_event, params) => {
    if (!params.nodeId || params.handleType === "target") {
      connectSourceRef.current = null;
      return;
    }

    connectSourceRef.current = {
      nodeId: params.nodeId,
      handleId: params.handleId ?? null
    };
  };

  const onConnectEnd: OnConnectEnd = (event, connectionState) => {
    const source = resolveConnectEndSource(connectSourceRef.current, connectionState);
    connectSourceRef.current = null;
    if (!source || !isFlowchartDesigner) return;
    if (connectionState.toNode || connectionState.toHandle) return;

    if (isConnectEndOverExistingWorkflowNode(event)) return;

    const point = clientPointFromEvent(event);
    setConnectMenu({
      kind: "fromPort",
      sourceNodeId: source.nodeId,
      sourceHandleId: source.handleId,
      clientX: point.x,
      clientY: point.y
    });
  };

  const onReconnect: OnReconnect<WorkflowEdge> = (oldEdge, newConnection) => {
    if (!isFlowchartDesigner) return;
    if (!isValidConnection(newConnection)) return;
    const nextEdges = reconnectEdge(oldEdge, {
      ...newConnection,
      sourceHandle: newConnection.sourceHandle ?? "Done",
      targetHandle: newConnection.targetHandle ?? undefined
    }, edges, { shouldReplaceId: false }) as WorkflowEdge[];
    setEdges(nextEdges);
    commitCanvas(nodes, nextEdges);
  };

  const onNodesDelete = (deletedNodes: Node<WorkflowNodeData>[]) => {
    if (isUnsupportedDesigner) return;
    if (deletedNodes.length === 0) return;
    const deletedIds = new Set(deletedNodes.map(node => node.id));
    const nextNodes = nodes.filter(node => !deletedIds.has(node.id));
    const nextEdges = edges.filter(edge => !deletedIds.has(edge.source) && !deletedIds.has(edge.target));
    setNodes(nextNodes);
    setEdges(nextEdges);
    if (selectedNodeId && deletedIds.has(selectedNodeId)) setSelectedNodeId(null);
    commitCanvas(nextNodes, nextEdges);
  };

  const onEdgesDelete = (deletedEdges: WorkflowEdge[]) => {
    if (isUnsupportedDesigner) return;
    if (deletedEdges.length === 0) return;
    const deletedIds = new Set(deletedEdges.map(edge => edge.id));
    const nextEdges = edges.filter(edge => !deletedIds.has(edge.id));
    setEdges(nextEdges);
    commitCanvas(nodes, nextEdges);
  };

  const deleteEdge = useCallback((edgeId: string) => {
    if (isUnsupportedDesigner) return;
    const nextEdges = edges.filter(edge => edge.id !== edgeId);
    setEdges(nextEdges);
    commitCanvas(nodes, nextEdges);
  }, [commitCanvas, edges, isUnsupportedDesigner, nodes]);

  const requestInsertActivity = useCallback((edgeId: string, clientX: number, clientY: number) => {
    if (!isFlowchartDesigner) return;
    setConnectMenu({ kind: "spliceEdge", edgeId, clientX, clientY });
  }, [isFlowchartDesigner]);

  const onConnectMenuPick = (activity: ActivityCatalogItem) => {
    const menu = connectMenu;
    if (!menu) return;
    setConnectMenu(null);

    const fallbackPosition = toCanvasPosition(menu.clientX, menu.clientY) ?? { x: 0, y: 0 };
    if (menu.kind === "fromEmpty") {
      const placed = createCanvasActivity(activity, fallbackPosition);
      const clearedNodes = nodes.map(node => node.selected ? { ...node, selected: false } : node);
      const nextNodes = [...clearedNodes, placed.node];
      setNodes(nextNodes);
      setSelectedNodeId(placed.node.id);
      commitCanvas(nextNodes, edges, [placed.activityNode]);
      return;
    }

    if (menu.kind === "fromPort") {
      const sourceNode = nodes.find(node => node.id === menu.sourceNodeId);
      const position = sourceNode ? rightOf(sourceNode) : fallbackPosition;
      const placed = createCanvasActivity(activity, position);
      const clearedNodes = nodes.map(node => node.selected ? { ...node, selected: false } : node);
      const nextNodes = [...clearedNodes, placed.node];
      const nextEdges = [...edges, createWorkflowEdge(menu.sourceNodeId, placed.node.id, menu.sourceHandleId ?? "Done")];
      setNodes(nextNodes);
      setEdges(nextEdges);
      setSelectedNodeId(placed.node.id);
      commitCanvas(nextNodes, nextEdges, [placed.activityNode]);
      return;
    }

    const edge = edges.find(candidate => candidate.id === menu.edgeId);
    if (edge) spliceActivityIntoEdge(activity, edge, fallbackPosition);
  };

  const edgeActions = useMemo<WorkflowEdgeActions>(() => ({
    highlightedEdgeId,
    deleteEdge,
    requestInsertActivity
  }), [deleteEdge, highlightedEdgeId, requestInsertActivity]);

  const enterSlot = (node: ActivityNode, slotId: string, label: string) => {
    setFrames(current => [...current, { ownerNodeId: node.nodeId, slotId, label }]);
    setSelectedNodeId(null);
  };

  const updateSelectedActivity = useCallback((activity: ActivityNode) => {
    setDraft(current => {
      const rootActivity = current?.state.rootActivity;
      if (!current || !rootActivity) return current;
      return {
        ...current,
        state: {
          ...current.state,
          rootActivity: updateActivity(rootActivity, activity.nodeId, () => activity)
        }
      };
    });
  }, []);

  const togglePaletteCategory = (category: string) => {
    setExpandedPaletteCategories(current => {
      const next = new Set(current);
      if (next.has(category)) {
        next.delete(category);
      } else {
        next.add(category);
      }
      return next;
    });
  };

  const toggleSidePanelCollapsed = (side: WorkflowSidePanel) => {
    setMaximizedSidePanel(current => current === side ? null : current);
    if (side === "palette") {
      setPaletteCollapsed(current => !current);
    } else {
      setInspectorCollapsed(current => !current);
    }
  };

  const toggleSidePanelMaximized = (side: WorkflowSidePanel) => {
    if (side === "palette") {
      setPaletteCollapsed(false);
    } else {
      setInspectorCollapsed(false);
    }

    setMaximizedSidePanel(current => current === side ? null : side);
  };

  const resizeSidePanel = (side: WorkflowSidePanel, delta: number) => {
    setMaximizedSidePanel(null);
    if (side === "palette") {
      setPaletteCollapsed(false);
      setPaletteWidth(current => clamp(current + delta, minPaletteWidth, maxPaletteWidth));
    } else {
      setInspectorCollapsed(false);
      setInspectorWidth(current => clamp(current + delta, minInspectorWidth, maxInspectorWidth));
    }
  };

  const startSidePanelResize = (side: WorkflowSidePanel, event: React.PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    setMaximizedSidePanel(null);
    if (side === "palette") {
      setPaletteCollapsed(false);
    } else {
      setInspectorCollapsed(false);
    }

    const startX = event.clientX;
    const startWidth = side === "palette" ? paletteWidth : inspectorWidth;
    const min = side === "palette" ? minPaletteWidth : minInspectorWidth;
    const max = side === "palette" ? maxPaletteWidth : maxInspectorWidth;

    document.body.classList.add("wf-side-panel-resizing");

    const handlePointerMove = (moveEvent: PointerEvent) => {
      const delta = side === "palette"
        ? moveEvent.clientX - startX
        : startX - moveEvent.clientX;
      const nextWidth = clamp(startWidth + delta, min, max);

      if (side === "palette") {
        setPaletteWidth(nextWidth);
      } else {
        setInspectorWidth(nextWidth);
      }
    };

    const stopResize = () => {
      document.body.classList.remove("wf-side-panel-resizing");
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("pointerup", stopResize);
      window.removeEventListener("pointercancel", stopResize);
    };

    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("pointerup", stopResize);
    window.addEventListener("pointercancel", stopResize);
  };

  const handleSidePanelResizeKeyDown = (side: WorkflowSidePanel, event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "ArrowLeft") {
      event.preventDefault();
      resizeSidePanel(side, side === "palette" ? -sidePanelResizeStep : sidePanelResizeStep);
    } else if (event.key === "ArrowRight") {
      event.preventDefault();
      resizeSidePanel(side, side === "palette" ? sidePanelResizeStep : -sidePanelResizeStep);
    } else if (event.key === "Home") {
      event.preventDefault();
      if (side === "palette") setPaletteWidth(minPaletteWidth);
      else setInspectorWidth(minInspectorWidth);
    } else if (event.key === "End") {
      event.preventDefault();
      if (side === "palette") setPaletteWidth(maxPaletteWidth);
      else setInspectorWidth(maxInspectorWidth);
    }
  };

  if (!details || !draft) {
    return <div className="wf-empty">{error || "Loading workflow editor..."}</div>;
  }

  const editorBodyClassName = [
    "wf-editor-body",
    paletteCollapsed ? "palette-collapsed" : "",
    inspectorCollapsed ? "inspector-collapsed" : "",
    maximizedSidePanel === "palette" ? "palette-maximized" : "",
    maximizedSidePanel === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  const editorBodyStyle = {
    "--wf-palette-width": `${paletteCollapsed ? collapsedSidePanelWidth : paletteWidth}px`,
    "--wf-inspector-width": `${inspectorCollapsed ? collapsedSidePanelWidth : inspectorWidth}px`
  } as React.CSSProperties;
  const paletteExpanded = !paletteCollapsed && maximizedSidePanel !== "inspector";
  const inspectorExpanded = !inspectorCollapsed && maximizedSidePanel !== "palette";
  const renderedTestRun = testRun?.draftSignature === getDraftSignature(draft)
    ? testRun.view
    : null;
  const visibleStatus = renderedTestRun && status.startsWith("Test run") ? "" : status;
  const openWorkflowRun = (workflowExecutionId: string) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(workflowExecutionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };
  const panelContext: WorkflowDesignerPanelContext = {
    definition: details.definition,
    draft,
    selectedActivity: selectedNode,
    selectedActivityDescriptor: selectedDescriptor,
    selectedActivitySlots: selectedSlots,
    catalog,
    currentScopeOwner: scopeOwner,
    frames
  };
  const contributedPanelTabs = workflowDesignerPanels
    .map(panel => {
      const ContributedPanel = panel.component;
      return {
        id: panel.id,
        title: panel.title,
        side: panel.side,
        order: panel.order ?? 500,
        icon: null,
        render: () => <ContributedPanel context={panelContext} />
      };
    });
  const leftPanelTabs: WorkflowEditorPanelTab[] = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: <Boxes size={15} />,
      render: renderActivitiesPanel
    },
    ...contributedPanelTabs.filter(tab => tab.side === "left")
  ].sort(compareWorkflowPanelTabs);
  const rightPanelTabs: WorkflowEditorPanelTab[] = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: <ListTree size={15} />,
      render: renderInspectorPanel
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: <Play size={15} />,
      render: () => <WorkflowRuntimePanel testRun={renderedTestRun} onOpenRun={openWorkflowRun} />
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: <Package size={15} />,
      render: () => (
        <WorkflowArtifactsPanel
          context={context}
          ai={ai}
          definitionId={details.definition.id}
          publishedArtifactId={publishedArtifactId}
        />
      )
    },
    ...contributedPanelTabs.filter(tab => tab.side === "right")
  ].sort(compareWorkflowPanelTabs);
  const activeLeftPanel = leftPanelTabs.find(tab => tab.id === activeLeftPanelId) ?? leftPanelTabs[0];
  const activeRightPanel = rightPanelTabs.find(tab => tab.id === activeRightPanelId) ?? rightPanelTabs[0];
  const canvasViewTabs: WorkflowEditorPanelTab[] = [
    { id: "designer", title: "Designer", order: 0, icon: <WorkflowIcon size={14} />, render: () => null },
    { id: "code", title: "Code", order: 1, icon: <Code2 size={14} />, render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: <SlidersHorizontal size={14} />, render: () => null }
  ];

  function renderActivitiesPanel() {
    const searching = paletteSearch.trim().length > 0;
    return (
      <div className="wf-palette-body">
        <label className="wf-palette-search">
          <Search size={14} aria-hidden="true" />
          <input
            type="search"
            value={paletteSearch}
            placeholder="Search activities"
            aria-label="Search activity palette"
            onChange={event => setPaletteSearch(event.target.value)}
          />
        </label>
        <div className="wf-palette-list" role="tree" aria-label="Available activities">
          {filteredPaletteGroups.length === 0 ? (
            <p className="wf-muted wf-palette-empty">No matching activities.</p>
          ) : filteredPaletteGroups.map(group => {
          const expanded = searching || expandedPaletteCategories.has(group.category);
          return (
            <div className="wf-palette-category" key={group.category}>
              <button
                type="button"
                className="wf-palette-category-toggle"
                role="treeitem"
                aria-expanded={expanded}
                onClick={() => togglePaletteCategory(group.category)}>
                {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                <span>{group.category}</span>
                <small>{group.activities.length}</small>
              </button>
              {expanded ? (
                <div className="wf-palette-activities" role="group">
                  {group.activities.map(activity => {
                    const description = activity.description?.trim();
                    const descriptionId = description ? `wf-palette-description-${activity.activityVersionId}` : undefined;
                    const displayName = getActivityDisplay(activity);
                    const icon = resolveActivityIcon(activity);
                    return (
                      <button
                        type="button"
                        className="wf-palette-activity"
                        role="treeitem"
                        key={activity.activityVersionId}
                        draggable
                        title={description || getActivityDisplay(activity)}
                        aria-describedby={descriptionId}
                        onClick={() => onPaletteClick(activity)}
                        onDragStart={event => onPaletteDragStart(event, activity)}
                        onDragEnd={event => onPaletteDragEnd(event, activity)}
                        onPointerDown={event => onPalettePointerDown(event, activity)}
                      >
                        <span className="wf-activity-icon" data-icon={icon} aria-hidden="true">
                          {renderActivityIcon(icon)}
                        </span>
                        <span className="wf-palette-activity-text">
                          <strong>{displayName}</strong>
                          {description ? <small id={descriptionId}>{description}</small> : null}
                        </span>
                        <GripVertical className="wf-palette-activity-grip" size={14} aria-hidden="true" />
                      </button>
                    );
                  })}
                </div>
              ) : null}
            </div>
          );
        })}
        </div>
      </div>
    );
  }

  function renderInspectorPanel() {
    return selectedNode ? (
      <div className="wf-inspector-content">
        <h3>{nodes.find(node => node.id === selectedNode.nodeId)?.data.label ?? selectedNode.nodeId}</h3>
        <dl>
          <dt>Node ID</dt>
          <dd>{selectedNode.nodeId}</dd>
          <dt>Activity type</dt>
          <dd>{selectedDescriptor?.typeName ?? catalogByVersion.get(selectedNode.activityVersionId)?.activityTypeKey ?? "Unknown"}</dd>
          <dt>Activity version</dt>
          <dd>{selectedNode.activityVersionId}</dd>
        </dl>
        {selectedNodeAvailability ? (
          <div className="wf-availability-notice">
            <AlertTriangle size={14} />
            <span>No longer available for new use · {getAvailabilityStateLabel(selectedNodeAvailability.state)}</span>
          </div>
        ) : null}
        <ActivityPropertiesPanel
          activity={selectedNode}
          descriptor={selectedDescriptor}
          editors={propertyEditors}
          expressionEditors={expressionEditors}
          expressionDescriptors={expressionDescriptors}
          descriptorStatus={descriptorStatus}
          onChange={updateSelectedActivity}
        />
        {selectedSlots.length > 0 ? (
          <div className="wf-slot-list">
            <span>Embedded slots</span>
            {selectedSlots.map(slot => (
              <button type="button" key={slot.id} onClick={() => enterSlot(selectedNode, slot.id, `${nodes.find(node => node.id === selectedNode.nodeId)?.data.label ?? selectedNode.nodeId} / ${slot.label}`)}>
                {slot.label}
                <small>{slot.activities.length} activit{slot.activities.length === 1 ? "y" : "ies"}</small>
              </button>
            ))}
          </div>
        ) : <p className="wf-muted">This activity does not expose embedded child slots.</p>}
      </div>
    ) : <p className="wf-muted">Select an activity to inspect properties and embedded slots.</p>;
  }

  return (
    <section className="wf-editor">
      <div className="wf-editor-top">
        <button type="button" className="wf-link-button" onClick={onBack}>Definitions</button>
        <ChevronRight size={14} />
        <strong>{details.definition.name}</strong>
        <span className="wf-chip">Draft</span>
        {visibleStatus ? <span className="wf-status"><Check size={13} /> {visibleStatus}</span> : null}
        <div className="wf-editor-actions">
          <div className="wf-canvas-tools" role="group" aria-label="Canvas tools">
            <button
              type="button"
              className="wf-icon-button"
              aria-label="Undo"
              title="Undo (Ctrl+Z)"
              disabled={!canUndoNow}
              onClick={undo}>
              <Undo2 size={16} />
            </button>
            <button
              type="button"
              className="wf-icon-button"
              aria-label="Redo"
              title="Redo (Ctrl+Shift+Z)"
              disabled={!canRedoNow}
              onClick={redo}>
              <Redo2 size={16} />
            </button>
            <button
              type="button"
              className="wf-icon-button"
              aria-label="Auto-layout"
              title="Auto-layout the canvas"
              disabled={!canAutoLayout}
              onClick={autoLayout}>
              <Network size={16} />
            </button>
          </div>
          <label className="wf-autosave-toggle">
            <input className="wf-autosave-switch-input" type="checkbox" checked={autosaveEnabled} onChange={event => setAutosaveEnabled(event.target.checked)} />
            <span>Autosave</span>
          </label>
          {findRisksAction ? (
            <button type="button" onClick={() => dispatchAiAction(ai, findRisksAction, { definition: details.definition, draft })}><Sparkles size={15} /> Risks</button>
          ) : null}
          {proposeUpdateAction ? (
            <button type="button" onClick={() => dispatchAiAction(ai, proposeUpdateAction, { definition: details.definition, draft })}><Sparkles size={15} /> Propose</button>
          ) : null}
          <button type="button" title="Export workflow as JSON" onClick={exportJson}><Download size={15} /> Export</button>
          <button type="button" disabled={busy} onClick={() => void save()}><Save size={15} /> Save</button>
          <button type="button" disabled={busy} onClick={() => void promoteAndPublish()}><GitBranch size={15} /> Promote</button>
          {renderedTestRun ? (
            <TestRunStatus
              testRun={renderedTestRun}
              onOpenDetails={() => {
                setActiveRightPanelId("runtime");
                setInspectorCollapsed(false);
              }}
            />
          ) : null}
          <button
            type="button"
            disabled={!canRunTest}
            title={draft.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running"}
            onClick={() => void run()}>
            <Play size={15} /> Run
          </button>
        </div>
      </div>

      {error ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}

      <div className={editorBodyClassName} style={editorBodyStyle}>
        <aside className="wf-palette" aria-label="Activities panel">
          <div className="wf-panel-title">
            <PanelTabList
              label="Activities panel tabs"
              tabs={leftPanelTabs}
              activeTabId={activeLeftPanel.id}
              onSelect={setActiveLeftPanelId}
            />
            <span className="wf-panel-actions">
              <button
                type="button"
                className="wf-panel-action-button"
                aria-label={paletteCollapsed ? "Expand activities panel" : "Collapse activities panel"}
                title={paletteCollapsed ? "Expand" : "Collapse"}
                onClick={() => toggleSidePanelCollapsed("palette")}
              >
                {paletteCollapsed ? <ChevronRight size={14} /> : <ChevronLeft size={14} />}
              </button>
              {!paletteCollapsed ? (
                <button
                  type="button"
                  className="wf-panel-action-button"
                  aria-label={maximizedSidePanel === "palette" ? "Restore activities panel" : "Maximize activities panel"}
                  title={maximizedSidePanel === "palette" ? "Restore" : "Maximize"}
                  onClick={() => toggleSidePanelMaximized("palette")}
                >
                  {maximizedSidePanel === "palette" ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
                </button>
              ) : null}
            </span>
          </div>
          {paletteExpanded ? activeLeftPanel.render() : null}
        </aside>

        {paletteExpanded && !maximizedSidePanel ? (
          <div
            className="wf-side-resize-handle left"
            role="separator"
            aria-label="Resize activities panel"
            aria-orientation="vertical"
            aria-valuemin={minPaletteWidth}
            aria-valuemax={maxPaletteWidth}
            aria-valuenow={paletteWidth}
            tabIndex={0}
            onPointerDown={event => startSidePanelResize("palette", event)}
            onKeyDown={event => handleSidePanelResizeKeyDown("palette", event)}
          />
        ) : <div className="wf-side-resize-spacer" />}

        <main className="wf-canvas-shell">
          <div className="wf-canvas-tabs">
            <PanelTabList
              label="Editor view tabs"
              tabs={canvasViewTabs}
              activeTabId={canvasView}
              onSelect={tabId => setCanvasView(tabId as CanvasView)}
            />
          </div>
          {canvasView === "code" ? (
            <WorkflowCodeView draft={draft} onApply={applyEditedJson} />
          ) : canvasView === "properties" ? (
            <WorkflowPropertiesView details={details} draft={draft} />
          ) : (
          <>
          <div className="wf-breadcrumb">
            <button type="button" onClick={() => { setFrames([]); setSelectedNodeId(null); }}>Root</button>
            {frames.map((frame, index) => (
              <React.Fragment key={`${frame.ownerNodeId}-${frame.slotId}-${index}`}>
                <ChevronRight size={13} />
                <button type="button" onClick={() => { setFrames(frames.slice(0, index + 1)); setSelectedNodeId(null); }}>{frame.label}</button>
              </React.Fragment>
            ))}
          </div>
          <div className="wf-canvas" ref={canvasRef} onDragOver={onCanvasDragOver} onDragLeave={onCanvasDragLeave} onDrop={onCanvasDrop}>
            <WorkflowEdgeActionsContext.Provider value={edgeActions}>
              <WorkflowNodeAvailabilityContext.Provider value={availabilityLookup}>
              <ReactFlow
                nodes={nodes}
                edges={edges}
                nodeTypes={nodeTypes}
                edgeTypes={edgeTypes}
                onInit={setReactFlowInstance}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onNodesDelete={onNodesDelete}
                onEdgesDelete={onEdgesDelete}
                onConnect={onConnect}
                onConnectStart={isFlowchartDesigner ? onConnectStart : undefined}
                onConnectEnd={isFlowchartDesigner ? onConnectEnd : undefined}
                onReconnect={isFlowchartDesigner ? onReconnect : undefined}
                isValidConnection={isValidConnection}
                onDragOver={onCanvasDragOver}
                onDragLeave={onCanvasDragLeave}
                onDrop={onCanvasDrop}
                onPaneClick={() => setSelectedNodeId(null)}
                onNodeClick={(_, node) => setSelectedNodeId(node.id)}
                onNodeDragStop={isUnsupportedDesigner ? undefined : commitLayout}
                fitView
                minZoom={0.2}
                maxZoom={1.8}
                nodesConnectable={isFlowchartDesigner}
                nodesDraggable={!isUnsupportedDesigner}
                selectionOnDrag
                multiSelectionKeyCode={["Shift", "Meta", "Control"]}
                deleteKeyCode={isUnsupportedDesigner ? null : ["Backspace", "Delete"]}
                panActivationKeyCode={null}
                defaultEdgeOptions={{ type: "workflow" }}
              >
                <Background gap={18} size={1} />
                <Controls />
                <MiniMap pannable zoomable />
              </ReactFlow>
              </WorkflowNodeAvailabilityContext.Provider>
            </WorkflowEdgeActionsContext.Provider>
            {isFlowchartDesigner && nodes.length === 0 ? (
              <button type="button" className="wf-empty-canvas-add" onClick={() => openEmptyConnectMenu()}>
                <Plus size={15} /> Add activity
              </button>
            ) : null}
            {connectMenu ? (
              <ConnectMenu
                clientX={connectMenu.clientX}
                clientY={connectMenu.clientY}
                activities={catalog}
                onPick={onConnectMenuPick}
                onClose={() => setConnectMenu(null)}
              />
            ) : null}
          </div>
          <ValidationPanel draft={draft} />
          </>
          )}
        </main>

        {inspectorExpanded && !maximizedSidePanel ? (
          <div
            className="wf-side-resize-handle right"
            role="separator"
            aria-label="Resize inspector panel"
            aria-orientation="vertical"
            aria-valuemin={minInspectorWidth}
            aria-valuemax={maxInspectorWidth}
            aria-valuenow={inspectorWidth}
            tabIndex={0}
            onPointerDown={event => startSidePanelResize("inspector", event)}
            onKeyDown={event => handleSidePanelResizeKeyDown("inspector", event)}
          />
        ) : <div className="wf-side-resize-spacer" />}

        <aside className="wf-inspector" aria-label="Inspector panel">
          <div className="wf-panel-title">
            <PanelTabList
              label="Inspector panel tabs"
              tabs={rightPanelTabs}
              activeTabId={activeRightPanel.id}
              onSelect={setActiveRightPanelId}
            />
            <span className="wf-panel-actions">
              <button
                type="button"
                className="wf-panel-action-button"
                aria-label={inspectorCollapsed ? "Expand inspector panel" : "Collapse inspector panel"}
                title={inspectorCollapsed ? "Expand" : "Collapse"}
                onClick={() => toggleSidePanelCollapsed("inspector")}
              >
                {inspectorCollapsed ? <ChevronLeft size={14} /> : <ChevronRight size={14} />}
              </button>
              {!inspectorCollapsed ? (
                <button
                  type="button"
                  className="wf-panel-action-button"
                  aria-label={maximizedSidePanel === "inspector" ? "Restore inspector panel" : "Maximize inspector panel"}
                  title={maximizedSidePanel === "inspector" ? "Restore" : "Maximize"}
                  onClick={() => toggleSidePanelMaximized("inspector")}
                >
                  {maximizedSidePanel === "inspector" ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
                </button>
              ) : null}
            </span>
          </div>
          {inspectorExpanded ? activeRightPanel.render() : null}
        </aside>
      </div>
    </section>
  );
}

function PanelTabList({
  label,
  tabs,
  activeTabId,
  onSelect
}: {
  label: string;
  tabs: WorkflowEditorPanelTab[];
  activeTabId: string;
  onSelect(tabId: string): void;
}) {
  return (
    <div className="wf-panel-tab-list" role="tablist" aria-label={label}>
      {tabs.map(tab => (
        <button
          type="button"
          role="tab"
          aria-selected={tab.id === activeTabId}
          className={tab.id === activeTabId ? "active" : ""}
          key={tab.id}
          title={tab.title}
          onClick={() => onSelect(tab.id)}
        >
          {tab.icon ? <span className="wf-panel-tab-icon" aria-hidden="true">{tab.icon}</span> : null}
          <span>{tab.title}</span>
        </button>
      ))}
    </div>
  );
}

function compareWorkflowPanelTabs(left: WorkflowEditorPanelTab, right: WorkflowEditorPanelTab) {
  return left.order - right.order || left.title.localeCompare(right.title);
}

function WorkflowActivityNode({ data, selected }: NodeProps) {
  const nodeData = data as WorkflowNodeData;
  const runtime = nodeData.runtime;
  const showFlowPorts = !nodeData.suppressFlowPorts;
  const sourcePorts = showFlowPorts
    ? nodeData.sourcePorts.length > 0 ? nodeData.sourcePorts : [{ name: "Done", displayName: "Done" }]
    : [];
  const subtitle = formatNodeSubtitle(nodeData);
  const availabilityLookup = React.useContext(WorkflowNodeAvailabilityContext);
  const availability = availabilityLookup?.({ activityVersionId: nodeData.activityVersionId, activityTypeKey: nodeData.activityTypeKey }) ?? null;
  return (
    <div
      className={["wf-node", selected ? "selected" : "", runtime ? "wf-node-runtime" : "", runtime?.hasBlockingIncident ? "faulted" : "", availability ? "wf-node-unavailable" : ""].filter(Boolean).join(" ")}
      data-icon={nodeData.icon ?? "activity"}
    >
      {showFlowPorts && nodeData.acceptsInbound ? <Handle type="target" position={Position.Left} /> : null}
      {availability ? (
        <span className="wf-node-availability" title={`No longer available for new use · ${getAvailabilityStateLabel(availability.state)}`}>
          <AlertTriangle size={13} />
        </span>
      ) : null}
      <div className="wf-node-content">
        <span className="wf-node-icon" aria-hidden="true">{renderActivityIcon(nodeData.icon)}</span>
        <span className="wf-node-copy">
          <strong>{nodeData.label}</strong>
          {subtitle ? <small>{subtitle}</small> : null}
        </span>
      </div>
      {nodeData.childSlots.length > 0 ? (
        <span className="wf-node-slot-badge">{nodeData.childSlots.length} slot{nodeData.childSlots.length === 1 ? "" : "s"}</span>
      ) : null}
      {runtime ? (
        <div className="wf-node-runtime-strip">
          {runtime.status ? <WorkflowStatusBadge status={runtime.status} subStatus={runtime.subStatus} /> : null}
          {runtime.incidentCount > 0 ? <span className="wf-node-runtime-count">{runtime.incidentCount} incident{runtime.incidentCount === 1 ? "" : "s"}</span> : null}
          {runtime.faultCount > 0 ? <span className="wf-node-runtime-count">{runtime.faultCount} faults</span> : null}
        </div>
      ) : null}
      {sourcePorts.map((port, index) => {
        const top = `${((index + 1) / (sourcePorts.length + 1)) * 100}%`;
        return (
          <React.Fragment key={port.name}>
            <span className="wf-node-port-label" style={{ top }}>{port.displayName}</span>
            <Handle type="source" position={Position.Right} id={port.name} style={{ top }} />
          </React.Fragment>
        );
      })}
    </div>
  );
}

function formatNodeSubtitle(nodeData: WorkflowNodeData) {
  const category = nodeData.category?.trim();
  const executionType = nodeData.executionType?.trim();
  const parts = [category, executionType].filter((part): part is string => !!part);
  return parts.join(" · ");
}

function WorkflowFlowEdge(props: EdgeProps<WorkflowEdge>) {
  const {
    id,
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    markerEnd,
    style,
    label,
    labelStyle
  } = props;
  const actions = React.useContext(WorkflowEdgeActionsContext);
  const [hovered, setHovered] = useState(false);
  const [path, labelX, labelY] = getSmoothStepPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition });
  const isHighlighted = actions?.highlightedEdgeId === id;

  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        markerEnd={markerEnd}
        style={{
          ...style,
          strokeWidth: isHighlighted ? 2.5 : style?.strokeWidth
        }}
        label={label}
        labelX={labelX}
        labelY={labelY}
        labelStyle={labelStyle}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
      />
      {actions ? (
        <EdgeLabelRenderer>
          <div
            className={["wf-edge-actions", hovered ? "visible" : "", isHighlighted ? "highlighted" : ""].filter(Boolean).join(" ")}
            style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)` }}
            onMouseEnter={() => setHovered(true)}
            onMouseLeave={() => setHovered(false)}
          >
            <button type="button" aria-label="Insert activity into connection" title="Insert activity" onClick={event => actions.requestInsertActivity(id, event.clientX, event.clientY)}>
              <Plus size={12} />
            </button>
            <button type="button" aria-label="Delete connection" title="Delete connection" onClick={() => actions.deleteEdge(id)}>
              <Trash2 size={12} />
            </button>
          </div>
        </EdgeLabelRenderer>
      ) : null}
    </>
  );
}

function ConnectMenu({ clientX, clientY, activities, onPick, onClose }: { clientX: number; clientY: number; activities: ActivityCatalogItem[]; onPick(activity: ActivityCatalogItem): void; onClose(): void }) {
  const [search, setSearch] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    const browsable = activities.filter(isActivityBrowsable);
    if (!term) return browsable;
    return browsable.filter(activity =>
      getActivityDisplay(activity).toLowerCase().includes(term) ||
      activity.activityTypeKey.toLowerCase().includes(term) ||
      (activity.category ?? "").toLowerCase().includes(term) ||
      (activity.description ?? "").toLowerCase().includes(term));
  }, [activities, search]);

  const groups = useMemo(() => groupActivityPalette(filtered), [filtered]);
  const flatActivities = useMemo(() => groups.flatMap(group => group.activities), [groups]);

  useEffect(() => {
    requestAnimationFrame(() => inputRef.current?.focus());
  }, []);

  useEffect(() => {
    const onMouseDown = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as globalThis.Node)) onClose();
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };

    document.addEventListener("mousedown", onMouseDown, true);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onMouseDown, true);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [onClose]);

  const onInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActiveIndex(index => Math.min(index + 1, flatActivities.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveIndex(index => Math.max(index - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      const activity = flatActivities[activeIndex];
      if (activity) onPick(activity);
    }
  };

  const left = Math.max(8, Math.min(clientX + 4, window.innerWidth - 328));
  const top = Math.max(8, Math.min(clientY + 4, window.innerHeight - 360));
  let itemIndex = -1;

  return (
    <div ref={containerRef} className="wf-connect-menu" style={{ left, top }} onMouseDown={event => event.stopPropagation()} onClick={event => event.stopPropagation()}>
      <input
        ref={inputRef}
        type="search"
        value={search}
        placeholder="Search activities..."
        aria-label="Search activities"
        onChange={event => {
          setSearch(event.target.value);
          setActiveIndex(0);
        }}
        onKeyDown={onInputKeyDown}
      />
      <div className="wf-connect-menu-list" role="listbox" aria-label="Activity picker">
        {groups.length === 0 ? <p>No matching activities.</p> : groups.map(group => (
          <section key={group.category}>
            <h4>{group.category}</h4>
            {group.activities.map(activity => {
              itemIndex += 1;
              const currentIndex = itemIndex;
              const active = currentIndex === activeIndex;
              return (
                <button
                  type="button"
                  role="option"
                  aria-selected={active}
                  className={active ? "active" : ""}
                  key={activity.activityVersionId}
                  onMouseEnter={() => setActiveIndex(currentIndex)}
                  onClick={() => onPick(activity)}
                >
                  <strong>{getActivityDisplay(activity)}</strong>
                  <small>{activity.category || activity.activityTypeKey}</small>
                </button>
              );
            })}
          </section>
        ))}
      </div>
    </div>
  );
}

function ValidationPanel({ draft }: { draft: WorkflowDraft }) {
  if (!draft.validationErrors.length) {
    return <div className="wf-validation ok"><Check size={14} /> No validation errors</div>;
  }

  return (
    <div className="wf-validation">
      <AlertCircle size={14} />
      {draft.validationErrors.length} validation issue{draft.validationErrors.length === 1 ? "" : "s"}
    </div>
  );
}

function TestRunStatus({
  testRun,
  onOpenDetails
}: {
  testRun: WorkflowTestRunView;
  onOpenDetails(): void;
}) {
  const rejected = isRejectedTestRun(testRun);
  return (
    <div className="wf-test-run-status" data-state={rejected ? "rejected" : "accepted"}>
      <button
        type="button"
        className="wf-test-run-trigger"
        onClick={onOpenDetails}
      >
        {rejected ? <AlertCircle size={16} /> : <Check size={16} />}
        {rejected ? "Test run rejected" : "Test run dispatched"}
      </button>
    </div>
  );
}

function WorkflowRuntimePanel({ testRun, onOpenRun }: {
  testRun: WorkflowTestRunView | null;
  onOpenRun(workflowExecutionId: string): void;
}) {
  if (!testRun) {
    return (
      <div className="wf-runtime-panel">
        <div className="wf-empty">Run the draft to see Runtime Evidence.</div>
      </div>
    );
  }

  const rejected = isRejectedTestRun(testRun);
  const workflowExecutionId = testRun.workflowExecutionId;
  return (
    <div className="wf-runtime-panel">
      <section className="wf-runtime-card" data-state={rejected ? "rejected" : "accepted"}>
        <header>
          <div>
            <span>Latest Test Run</span>
            <h3>{rejected ? "Rejected by the server" : "Transient run accepted"}</h3>
          </div>
          <WorkflowStatusBadge status={testRun.status} subStatus={testRun.commandDispatchStatus ?? undefined} />
        </header>
        <p>Ephemeral - not saved, promoted, or published.</p>
        {rejected && testRun.reason ? <div className="wf-runtime-reason"><AlertCircle size={14} /> {testRun.reason}</div> : null}
        <dl className="wf-runtime-meta">
          <div><dt>Dispatch</dt><dd title={testRun.commandDispatchStatus ?? testRun.status}>{testRun.commandDispatchStatus ?? testRun.status}</dd></div>
          <div><dt>Test Run</dt><dd title={testRun.testRunId}>{testRun.testRunId}</dd></div>
          <div><dt>Artifact</dt><dd title={testRun.artifactId ?? "None"}>{testRun.artifactId ?? "None"}</dd></div>
          <div>
            <dt>Run / Instance</dt>
            <dd title={workflowExecutionId ?? "None"}>
              {workflowExecutionId ? (
                <button type="button" onClick={() => onOpenRun(workflowExecutionId)}>{workflowExecutionId}</button>
              ) : "None"}
            </dd>
          </div>
          <div><dt>Activities</dt><dd>{formatEvidenceCount(testRun.activityCount, "activity")}</dd></div>
          <div><dt>Incidents</dt><dd>{formatEvidenceCount(testRun.incidentCount, "incident")}</dd></div>
          <div><dt>Expires</dt><dd title={testRun.expiresAt ? formatDate(testRun.expiresAt) : "None"}>{testRun.expiresAt ? formatDate(testRun.expiresAt) : "None"}</dd></div>
        </dl>
      </section>
    </div>
  );
}

function formatEvidenceCount(count: number | null | undefined, label: string) {
  if (typeof count !== "number") return "Available on linked Run";
  return `${count} ${label}${count === 1 ? "" : "s"}`;
}

function createNodeId(activity: ActivityCatalogItem) {
  const base = getActivityDisplay(activity).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity";
  return `${base}-${crypto.randomUUID().slice(0, 8)}`;
}

function rightOf(node: Node) {
  return { x: node.position.x + 280, y: node.position.y };
}

function midpointBetween(source: Node, target: Node) {
  return {
    x: Math.round((source.position.x + target.position.x) / 2),
    y: Math.round((source.position.y + target.position.y) / 2)
  };
}

function clientPointFromEvent(event: MouseEvent | TouchEvent) {
  if ("changedTouches" in event && event.changedTouches.length > 0) {
    return { x: event.changedTouches[0].clientX, y: event.changedTouches[0].clientY };
  }

  return { x: (event as MouseEvent).clientX, y: (event as MouseEvent).clientY };
}

export function isConnectEndOverExistingWorkflowNode(event: MouseEvent | TouchEvent) {
  const point = clientPointFromEvent(event);
  const releaseTarget = document.elementFromPoint?.(point.x, point.y) as HTMLElement | null | undefined;
  const target = releaseTarget ?? (event.target as HTMLElement | null);
  return !!target?.closest(".react-flow__handle, .react-flow__node");
}

export function resolveConnectEndSource(
  currentSource: WorkflowConnectSource | null,
  connectionState: { fromNode?: { id?: string | null } | null; fromHandle?: { id?: string | null } | null }
): WorkflowConnectSource | null {
  if (currentSource) return currentSource;
  const nodeId = connectionState.fromNode?.id;
  return nodeId ? { nodeId, handleId: connectionState.fromHandle?.id ?? null } : null;
}

function getDraftSignature(draft: WorkflowDraft) {
  return JSON.stringify({ state: draft.state, layout: draft.layout });
}

function getDraftRevision(draft: WorkflowDraft) {
  return hashString(getDraftSignature(draft));
}

function collectWorkflowContextActivities(activity: ActivityNode | null | undefined, catalogByVersion: Map<string, ActivityCatalogItem>, result: Array<{ id: string; type: string; displayName?: string }> = []) {
  if (!activity) return result;

  const catalogItem = catalogByVersion.get(activity.activityVersionId);
  result.push({
    id: activity.nodeId,
    type: catalogItem?.activityTypeKey ?? activity.activityVersionId,
    displayName: catalogItem ? getActivityDisplay(catalogItem) : undefined
  });

  for (const slot of getChildSlots(activity)) {
    for (const child of slot.activities) collectWorkflowContextActivities(child, catalogByVersion, result);
  }

  return result;
}

function collectWorkflowContextConnections(
  activity: ActivityNode | null | undefined,
  result: Array<{ source: string; target: string; sourcePort?: string; targetPort?: string }> = []
) {
  if (!activity) return result;

  // flowchartEdges decodes the flowchart structure payload into source/target edges; reuse it so the
  // agent sees the same wiring the designer renders.
  for (const edge of flowchartEdges(activity)) {
    result.push({ source: edge.source, target: edge.target, sourcePort: edge.sourceHandle ?? undefined, targetPort: edge.targetHandle ?? undefined });
  }

  for (const slot of getChildSlots(activity)) {
    for (const child of slot.activities) collectWorkflowContextConnections(child, result);
  }

  return result;
}

function cloneWorkflowDraftForUndo(draft: WorkflowDraft) {
  return typeof structuredClone === "function"
    ? structuredClone(draft)
    : JSON.parse(JSON.stringify(draft)) as WorkflowDraft;
}

function createDraftSnapshotId(draft: WorkflowDraft) {
  return `${draft.id}-${hashString(JSON.stringify(draft.state))}`;
}

function hashString(value: string) {
  let hash = 2166136261;
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }

  return (hash >>> 0).toString(16).padStart(8, "0");
}

function isRejectedTestRun(testRun: WorkflowTestRunView) {
  return testRun.status.toLowerCase() === "rejected";
}

function formatWorkflowVersionLoadError(error: string) {
  try {
    const payload = JSON.parse(error) as { error?: unknown };
    if (typeof payload.error === "string") return payload.error;
  } catch {
    // Fall through to the original message when the server did not return the standard error shape.
  }

  return error;
}

function formatWorkflowRunLoadError(error: unknown, workflowExecutionId: string) {
  const message = error instanceof Error ? error.message : String(error);
  if (isNotFoundError(error, message)) return `Run ${workflowExecutionId} was not found.`;
  return message;
}

function isNotFoundError(error: unknown, message: string) {
  const responseStatus = typeof error === "object" && error
    ? (error as { response?: { status?: unknown }; status?: unknown }).response?.status
      ?? (error as { response?: { status?: unknown }; status?: unknown }).status
    : undefined;
  if (responseStatus === 404 || /\b404\b/.test(message)) return true;

  try {
    const payload = JSON.parse(message) as { error?: unknown; title?: unknown; detail?: unknown };
    return [payload.error, payload.title, payload.detail]
      .some(value => typeof value === "string" && /not found/i.test(value));
  } catch {
    return /not found/i.test(message);
  }
}

