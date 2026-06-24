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
import { AlertCircle, Boxes, Check, ChevronDown, ChevronLeft, ChevronRight, GitBranch, GripVertical, ListTree, Maximize2, Minimize2, Play, Plus, RotateCcw, Save, Search, Sparkles, Terminal, Trash2, Zap } from "lucide-react";
import type { ElsaStudioModuleApi, StudioActivityDescriptor, StudioActivityPropertyEditorContribution, StudioAiContributionApi, StudioAiPromptActionContribution, StudioEndpointContext, StudioExpressionDescriptor, StudioWorkflowDesignerPanelContribution } from "@elsa-workflows/studio-sdk";
import {
  createDefinition,
  deleteDefinition,
  deleteDefinitionPermanently,
  getDefinition,
  getWorkflowInstance,
  listActivities,
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
import type { ActivityCatalogItem, ActivityExecutionStateSummary, ActivityNode, DefinitionListState, IncidentStateSummary, WorkflowDefinitionDetails, WorkflowDraft, WorkflowExecutableSummary, WorkflowInstanceDetails, WorkflowInstanceSummary, WorkflowTestRunView } from "./workflowTypes";
import {
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
  type ScopeFrame,
  type WorkflowEdgeData,
  type WorkflowNodeData
} from "./workflowAdapter";
import { ActivityPropertiesPanel } from "./ActivityPropertiesPanel";
import "./styles.css";

const nodeTypes = { workflowActivity: WorkflowActivityNode };
const edgeTypes = { workflow: WorkflowFlowEdge };
const activityDragDataType = "application/x-elsa-activity-version-id";
const pointerDragThreshold = 6;
const autosaveDelayMs = 1200;
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

type WorkflowEdge = Edge<WorkflowEdgeData>;
type WorkflowEditorOperation = "idle" | "saving" | "promoting" | "testRunPreparing" | "testRunStarting";
interface WorkflowTestRunState {
  draftSignature: string;
  view: WorkflowTestRunView;
}

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

export function register(api: ElsaStudioModuleApi) {
  api.featureAreas.add({
    id: "workflows",
    title: "Workflows",
    description: "Design, publish and run workflow definitions and inspect instances.",
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
        { title: "Instances", path: "/workflows/instances", iconColor: "#0ea5e9" }
      ]
    },
    routes: [
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => <WorkflowManagementPage context={api.backend} ai={api.ai} propertyEditors={api.propertyEditors.list()} workflowDesignerPanels={api.workflowDesigner.panels.list()} />
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
        label: "Workflow instances",
        component: () => <WorkflowInstancesPage context={api.backend} ai={api.ai} />
      }
    ]
  });
}

function WorkflowManagementPage({
  context,
  ai,
  propertyEditors,
  workflowDesignerPanels
}: {
  context: StudioEndpointContext;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
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
    ? <WorkflowEditor context={context} definitionId={definitionId} ai={ai} propertyEditors={propertyEditors} workflowDesignerPanels={workflowDesignerPanels} onBack={() => openDefinition(null)} />
    : (
      <WorkflowsPageFrame activePath="/workflows/definitions" title="Definitions">
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

  return (
    <WorkflowsPageFrame activePath="/workflows/executables" title="Executables">
      <WorkflowExecutables context={context} ai={ai} definitionFilter={definitionFilter} />
    </WorkflowsPageFrame>
  );
}

function WorkflowInstancesPage({ context, ai }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
  return (
    <WorkflowsPageFrame activePath="/workflows/instances" title="Instances">
      <WorkflowInstances context={context} ai={ai} />
    </WorkflowsPageFrame>
  );
}

function WorkflowsPageFrame({ activePath, title, children }: { activePath: string; title: string; children: React.ReactNode }) {
  const navigate = (path: string) => {
    window.history.pushState({}, "", path);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };

  return (
    <section className="wf-page">
      <div className="wf-page-header">
        <div>
          <span className="wf-kicker">Workflow management</span>
          <h2>{title}</h2>
        </div>
      </div>
      <nav className="wf-section-tabs" aria-label="Workflow views">
        <a className={activePath === "/workflows/definitions" ? "active" : ""} href="/workflows/definitions" onClick={event => { event.preventDefault(); navigate("/workflows/definitions"); }}>Definitions</a>
        <a className={activePath === "/workflows/executables" ? "active" : ""} href="/workflows/executables" onClick={event => { event.preventDefault(); navigate("/workflows/executables"); }}>Executables</a>
        <a className={activePath === "/workflows/instances" ? "active" : ""} href="/workflows/instances" onClick={event => { event.preventDefault(); navigate("/workflows/instances"); }}>Instances</a>
      </nav>
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
    if (!window.confirm(`Delete workflow definition "${definition.name}"? You can restore it from the Deleted view.`)) return;
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
    if (!window.confirm(`Permanently delete workflow definition "${definition.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) return;
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

      {state === "failed" ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}
      {state !== "failed" && error ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}
      {status ? <div className="wf-status-line"><Check size={14} /> {status}</div> : null}
      {selectedDefinitionIds.size > 0 ? (
        <div className="wf-selection-bar" aria-live="polite">
          <span>{selectedDefinitionIds.size} selected</span>
          <button type="button" onClick={clearSelection}>Clear selection</button>
        </div>
      ) : null}
      {state === "loading" ? <div className="wf-empty">Loading workflow definitions...</div> : null}
      {state === "ready" && definitions.length === 0 ? <div className="wf-empty">No {listState} workflow definitions found.</div> : null}
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

function WorkflowExecutables({ context, ai, definitionFilter }: { context: StudioEndpointContext; ai: StudioAiContributionApi; definitionFilter: string | null }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [executables, setExecutables] = useState<WorkflowExecutableSummary[]>([]);
  const visibleExecutables = useMemo(
    () => definitionFilter
      ? executables.filter(executable => executable.definitionId === definitionFilter || executable.sourceId === definitionFilter)
      : executables,
    [definitionFilter, executables]
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
    setError("");
    try {
      await runExecutable(context, executable.artifactId);
      setStatus(`Started ${executable.artifactId}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  return (
    <>
      <div className="wf-toolbar">
        <button type="button" onClick={() => void load()}>Refresh</button>
        {definitionFilter ? <span className="wf-filter-chip">Definition {definitionFilter}</span> : null}
      </div>
      {state === "failed" ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}
      {status ? <div className="wf-status-line"><Check size={14} /> {status}</div> : null}
      {state === "loading" ? <div className="wf-empty">Loading workflow executables...</div> : null}
      {state === "ready" && visibleExecutables.length === 0 ? <div className="wf-empty">{definitionFilter ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one."}</div> : null}
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
              <span>
                <strong>{executable.artifactId}</strong>
                <small>{executable.artifactHash}</small>
              </span>
              <span>{executable.artifactVersion}</span>
              <span>{formatExecutableSource(executable)}</span>
              <span>{formatExecutableRoot(executable)}</span>
              <span>{formatDate(executable.publishedAt ?? executable.createdAt)}</span>
              <span className="wf-row-actions">
                <button type="button" onClick={() => void run(executable)}><Play size={13} /> Run</button>
                {explainExecutableAction ? (
                  <button type="button" onClick={() => dispatchAiAction(ai, explainExecutableAction, executable)}><Sparkles size={13} /> Explain</button>
                ) : null}
              </span>
            </div>
          ))}
        </div>
      ) : null}
    </>
  );
}

function WorkflowInstances({ context, ai }: { context: StudioEndpointContext; ai: StudioAiContributionApi }) {
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [instances, setInstances] = useState<WorkflowInstanceSummary[]>([]);
  const [selectedInstanceId, setSelectedInstanceId] = useState<string | null>(null);
  const [selectedDetails, setSelectedDetails] = useState<WorkflowInstanceDetails | null>(null);
  const [detailState, setDetailState] = useState<"idle" | "loading" | "ready" | "failed">("idle");
  const [detailError, setDetailError] = useState("");
  const instanceAction = findAiAction(ai, "weaver.workflows.explain-instance");

  const load = useCallback(async () => {
    setState("loading");
    setError("");
    try {
      const nextInstances = await listWorkflowInstances(context, { status: statusFilter || undefined, take: 100 });
      setInstances(nextInstances);
      setState("ready");
      setSelectedInstanceId(current => current && nextInstances.some(instance => instance.workflowExecutionId === current) ? current : nextInstances[0]?.workflowExecutionId ?? null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setInstances([]);
      setState("failed");
    }
  }, [context, statusFilter]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    let cancelled = false;
    setSelectedDetails(null);
    setDetailError("");

    if (!selectedInstanceId) {
      setDetailState("idle");
      return () => { cancelled = true; };
    }

    setDetailState("loading");
    void getWorkflowInstance(context, selectedInstanceId)
      .then(details => {
        if (cancelled) return;
        setSelectedDetails(details);
        setDetailState("ready");
      })
      .catch(e => {
        if (cancelled) return;
        setDetailError(e instanceof Error ? e.message : String(e));
        setDetailState("failed");
      });

    return () => { cancelled = true; };
  }, [context, selectedInstanceId]);

  const selectedSummary = instances.find(instance => instance.workflowExecutionId === selectedInstanceId) ?? null;

  return (
    <>
      <div className="wf-toolbar">
        <button type="button" onClick={() => void load()}>Refresh</button>
        <label className="wf-toolbar-field">
          <span>Status</span>
          <select aria-label="Workflow instance status" value={statusFilter} onChange={event => setStatusFilter(event.target.value)}>
            <option value="">All statuses</option>
            <option value="Pending">Pending</option>
            <option value="Running">Running</option>
            <option value="Suspended">Suspended</option>
            <option value="Completed">Completed</option>
            <option value="Faulted">Faulted</option>
            <option value="Cancelled">Cancelled</option>
          </select>
        </label>
      </div>
      {state === "failed" ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}
      {state === "loading" ? <div className="wf-empty">Loading workflow instances...</div> : null}
      {state === "ready" && instances.length === 0 ? <div className="wf-empty">No workflow instances found. Run a published workflow executable to create instance history.</div> : null}
      {state === "ready" && instances.length > 0 ? (
        <div className="wf-instance-workbench">
          <div className="wf-grid wf-instance-grid" role="table" aria-label="Workflow instances">
            <div className="wf-grid-head" role="row">
              <span>Instance</span>
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
                aria-label={`Inspect workflow instance ${instance.workflowExecutionId}`}
                aria-selected={instance.workflowExecutionId === selectedInstanceId}
                key={instance.workflowExecutionId}
                onClick={() => setSelectedInstanceId(instance.workflowExecutionId)}
              >
                <span>
                  <strong>{instance.workflowExecutionId}</strong>
                  <small>{instance.artifactId}</small>
                </span>
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
          <WorkflowInstanceInspector
            ai={ai}
            action={instanceAction}
            summary={selectedSummary}
            details={selectedDetails}
            state={detailState}
            error={detailError}
          />
        </div>
      ) : null}
    </>
  );
}

function WorkflowInstanceInspector({ ai, action, summary, details, state, error }: {
  ai: StudioAiContributionApi;
  action: StudioAiPromptActionContribution | undefined;
  summary: WorkflowInstanceSummary | null;
  details: WorkflowInstanceDetails | null;
  state: "idle" | "loading" | "ready" | "failed";
  error: string;
}) {
  if (!summary) {
    return <aside className="wf-instance-inspector"><div className="wf-empty">Select a workflow instance to inspect activity history.</div></aside>;
  }

  return (
    <aside className="wf-instance-inspector" aria-label="Workflow instance details">
      <header>
        <div>
          <span>Workflow instance</span>
          <h3>{summary.workflowExecutionId}</h3>
        </div>
        {action ? (
          <button type="button" onClick={() => dispatchAiAction(ai, action, details ?? summary)}>
            <Sparkles size={13} /> Explain
          </button>
        ) : null}
      </header>
      <dl className="wf-instance-meta">
        <dt>Status</dt>
        <dd><WorkflowStatusBadge status={summary.status} subStatus={summary.subStatus} /></dd>
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
      {state === "loading" ? <div className="wf-empty">Loading instance details...</div> : null}
      {state === "failed" ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}
      {state === "ready" && details ? (
        <>
          <WorkflowActivityHistory activities={details.activities} />
          <WorkflowIncidentList incidents={details.incidents} />
        </>
      ) : null}
    </aside>
  );
}

function WorkflowActivityHistory({ activities }: { activities: ActivityExecutionStateSummary[] }) {
  return (
    <section className="wf-instance-section">
      <h4>Activity history</h4>
      {activities.length === 0 ? <p>No activity executions recorded yet.</p> : null}
      {activities.length > 0 ? (
        <div className="wf-instance-activity-list">
          {activities.map(activity => (
            <div className="wf-instance-activity" key={activity.activityExecutionId}>
              <span><WorkflowStatusBadge status={activity.status} subStatus={activity.subStatus} /></span>
              <strong>{shortTypeName(activity.activityType) ?? activity.activityType}</strong>
              <small>{activity.activityExecutionId}</small>
              <time>{formatDate(activity.scheduledAt)}</time>
            </div>
          ))}
        </div>
      ) : null}
    </section>
  );
}

function WorkflowIncidentList({ incidents }: { incidents: IncidentStateSummary[] }) {
  return (
    <section className="wf-instance-section">
      <h4>Incidents</h4>
      {incidents.length === 0 ? <p>No incidents recorded.</p> : null}
      {incidents.map(incident => (
        <div className="wf-instance-incident" data-severity={incident.severity.toLowerCase()} key={incident.incidentId}>
          <strong>{incident.failureType}</strong>
          <span>{incident.status} · {incident.severity}</span>
          <p>{incident.message}</p>
        </div>
      ))}
    </section>
  );
}

function WorkflowStatusBadge({ status, subStatus }: { status: string; subStatus?: string | null }) {
  return <span className="wf-status-badge" data-status={status.toLowerCase()}>{subStatus ? `${status} · ${subStatus}` : status}</span>;
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
  if (prompt) ai.dispatchPrompt(prompt);
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

function formatExecutableSource(executable: WorkflowExecutableSummary) {
  if (executable.sourceKind || executable.sourceId || executable.sourceVersion) {
    return [executable.sourceKind, executable.sourceId, executable.sourceVersion].filter(Boolean).join(" / ");
  }

  return executable.definitionId;
}

function formatExecutableRoot(executable: WorkflowExecutableSummary) {
  const rootName = formatActivityTypeName(executable.rootActivityType);
  return rootName || executable.rootActivityType;
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

function shortTypeName(key: string | null | undefined) {
  return key?.split(".").filter(Boolean).at(-1);
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
  workflowDesignerPanels,
  onBack
}: {
  context: StudioEndpointContext;
  definitionId: string;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  workflowDesignerPanels: StudioWorkflowDesignerPanelContribution[];
  onBack(): void;
}) {
  const [details, setDetails] = useState<WorkflowDefinitionDetails | null>(null);
  const [draft, setDraft] = useState<WorkflowDraft | null>(null);
  const [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
  const [activityDescriptors, setActivityDescriptors] = useState<StudioActivityDescriptor[]>([]);
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
  const [paletteWidth, setPaletteWidth] = useState(() => readStoredNumber(workflowPaletteWidthStorageKey, defaultPaletteWidth, minPaletteWidth, maxPaletteWidth));
  const [inspectorWidth, setInspectorWidth] = useState(() => readStoredNumber(workflowInspectorWidthStorageKey, defaultInspectorWidth, minInspectorWidth, maxInspectorWidth));
  const [paletteCollapsed, setPaletteCollapsed] = useState(() => readStoredBoolean(workflowPaletteCollapsedStorageKey, false));
  const [inspectorCollapsed, setInspectorCollapsed] = useState(() => readStoredBoolean(workflowInspectorCollapsedStorageKey, false));
  const [maximizedSidePanel, setMaximizedSidePanel] = useState<WorkflowSidePanel | null>(readStoredMaximizedSide);
  const [activeLeftPanelId, setActiveLeftPanelId] = useState("activities");
  const [activeRightPanelId, setActiveRightPanelId] = useState("inspector");
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const connectSourceRef = useRef<{ nodeId: string; handleId: string | null } | null>(null);
  const lastSavedDraftSignatureRef = useRef("");
  const saveRequestIdRef = useRef(0);
  const saveQueueRef = useRef<Promise<unknown>>(Promise.resolve());
  const pointerDragRef = useRef<{
    activity: ActivityCatalogItem;
    startX: number;
    startY: number;
    dragging: boolean;
  } | null>(null);
  const suppressPaletteClickRef = useRef(false);

  const root = draft?.state.rootActivity ?? null;
  const catalogByVersion = useMemo(() => new Map(catalog.map(activity => [activity.activityVersionId, activity])), [catalog]);
  const descriptorsByType = useMemo(() => indexActivityDescriptors(activityDescriptors), [activityDescriptors]);
  const scopeOwner = useMemo(() => resolveScopeOwner(root, frames), [root, frames]);
  const designerSupport = getActivityDesignerSupport(scopeOwner, scopeOwner ? catalogByVersion.get(scopeOwner.activityVersionId) : undefined);
  const isUnsupportedDesigner = !!scopeOwner && designerSupport === "unsupported";
  const scope = useMemo(() => isUnsupportedDesigner ? null : resolveScope(root, frames), [root, frames, isUnsupportedDesigner]);
  const paletteGroups = useMemo(() => groupActivityPalette(catalog), [catalog]);
  const selectedNode = useMemo(() => {
    if (isUnsupportedDesigner && scopeOwner?.nodeId === selectedNodeId) return scopeOwner;
    return scope?.slot.activities.find(activity => activity.nodeId === selectedNodeId) ?? null;
  }, [isUnsupportedDesigner, scope, scopeOwner, selectedNodeId]);
  const selectedDescriptor = useMemo(
    () => selectedNode ? resolveActivityDescriptor(selectedNode, catalogByVersion, descriptorsByType) : null,
    [catalogByVersion, descriptorsByType, selectedNode]
  );
  const selectedSlots = selectedNode ? getChildSlots(selectedNode) : [];
  const isFlowchartDesigner = designerSupport === "flowchart" && scope?.slot.mode === "flowchart";
  const canAddActivitiesToCanvas = !root || !isUnsupportedDesigner;
  const busy = operation !== "idle";
  const canRunTest = !!draft?.state.rootActivity && !busy;
  const findRisksAction = findAiAction(ai, "weaver.workflows.find-draft-risks");
  const proposeUpdateAction = findAiAction(ai, "weaver.workflows.propose-update");

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
    const [nextDetails, nextCatalog, nextDescriptors, nextExpressions] = await Promise.all([
      getDefinition(context, definitionId),
      listActivities(context),
      listActivityDescriptors(context).then(
        descriptors => ({ ok: true as const, descriptors }),
        () => ({ ok: false as const, descriptors: [] as StudioActivityDescriptor[] })
      ),
      listExpressionDescriptors(context).then(
        descriptors => ({ ok: true as const, descriptors }),
        () => ({ ok: false as const, descriptors: fallbackExpressionDescriptors })
      )
    ]);
    const nextDraft = nextDetails.draft ?? null;
    setDetails(nextDetails);
    lastSavedDraftSignatureRef.current = nextDraft ? getDraftSignature(nextDraft) : "";
    setDraft(nextDraft);
    setCatalog(nextCatalog.activities ?? []);
    setActivityDescriptors(nextDescriptors.descriptors);
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
    event.dataTransfer.setData(activityDragDataType, activity.activityVersionId);
    event.dataTransfer.setData("text/plain", activity.activityVersionId);
    event.dataTransfer.effectAllowed = "copy";
  };

  const onPaletteDragEnd = (event: React.DragEvent<HTMLButtonElement>, activity: ActivityCatalogItem) => {
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
    if (!canAddActivitiesToCanvas) return;

    const activityVersionId = event.dataTransfer.getData(activityDragDataType) || event.dataTransfer.getData("text/plain");
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

  const onConnectEnd: OnConnectEnd = event => {
    const source = connectSourceRef.current;
    connectSourceRef.current = null;
    if (!source || !isFlowchartDesigner) return;

    const target = event.target as HTMLElement | null;
    if (target?.closest(".react-flow__handle, .react-flow__node")) return;

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
    ...contributedPanelTabs.filter(tab => tab.side === "right")
  ].sort(compareWorkflowPanelTabs);
  const activeLeftPanel = leftPanelTabs.find(tab => tab.id === activeLeftPanelId) ?? leftPanelTabs[0];
  const activeRightPanel = rightPanelTabs.find(tab => tab.id === activeRightPanelId) ?? rightPanelTabs[0];

  function renderActivitiesPanel() {
    return (
      <div className="wf-palette-list" role="tree" aria-label="Available activities">
        {paletteGroups.map(group => {
          const expanded = expandedPaletteCategories.has(group.category);
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
        <ActivityPropertiesPanel
          activity={selectedNode}
          descriptor={selectedDescriptor}
          editors={propertyEditors}
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
        {status ? <span className="wf-status"><Check size={13} /> {status}</span> : null}
        <div className="wf-editor-actions">
          <label className="wf-autosave-toggle">
            <input type="checkbox" checked={autosaveEnabled} onChange={event => setAutosaveEnabled(event.target.checked)} />
            <span>Autosave</span>
          </label>
          {findRisksAction ? (
            <button type="button" onClick={() => dispatchAiAction(ai, findRisksAction, { definition: details.definition, draft })}><Sparkles size={15} /> Risks</button>
          ) : null}
          {proposeUpdateAction ? (
            <button type="button" onClick={() => dispatchAiAction(ai, proposeUpdateAction, { definition: details.definition, draft })}><Sparkles size={15} /> Propose</button>
          ) : null}
          <button type="button" disabled={busy} onClick={() => void save()}><Save size={15} /> Save</button>
          <button type="button" disabled={busy} onClick={() => void promoteAndPublish()}><GitBranch size={15} /> Promote</button>
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
      {renderedTestRun ? <TestRunCapsule testRun={renderedTestRun} /> : null}

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
  const showFlowPorts = !nodeData.suppressFlowPorts;
  const sourcePorts = showFlowPorts
    ? nodeData.sourcePorts.length > 0 ? nodeData.sourcePorts : [{ name: "Done", displayName: "Done" }]
    : [];
  const subtitle = formatNodeSubtitle(nodeData);
  return (
    <div className={selected ? "wf-node selected" : "wf-node"} data-icon={nodeData.icon ?? "activity"}>
      {showFlowPorts && nodeData.acceptsInbound ? <Handle type="target" position={Position.Left} /> : null}
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

function renderActivityIcon(icon: WorkflowNodeData["icon"]) {
  switch (icon) {
    case "flowchart":
      return <GitBranch size={15} />;
    case "sequence":
      return <ListTree size={15} />;
    case "terminal":
      return <Terminal size={15} />;
    case "runtime":
      return <Play size={15} />;
    case "trigger":
      return <Zap size={15} />;
    default:
      return <Boxes size={15} />;
  }
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

function TestRunCapsule({ testRun }: { testRun: WorkflowTestRunView }) {
  const rejected = isRejectedTestRun(testRun);
  return (
    <section className="wf-test-run-capsule" data-state={rejected ? "rejected" : "accepted"} aria-live="polite">
      <div className="wf-test-run-heading">
        {rejected ? <AlertCircle size={16} /> : <Check size={16} />}
        <div>
          <strong>{rejected ? "Test run rejected" : "Test run dispatched"}</strong>
          <span>Ephemeral - not promoted</span>
        </div>
      </div>
      {rejected && testRun.reason ? <p>{testRun.reason}</p> : null}
      <dl>
        <div><dt>Status</dt><dd>{testRun.status}</dd></div>
        {testRun.commandDispatchStatus ? <div><dt>Dispatch</dt><dd>{testRun.commandDispatchStatus}</dd></div> : null}
        <div><dt>Test run</dt><dd>{testRun.testRunId}</dd></div>
        {testRun.artifactId ? <div><dt>Transient artifact</dt><dd>{testRun.artifactId}</dd></div> : null}
        {testRun.workflowExecutionId ? <div><dt>Execution</dt><dd>{testRun.workflowExecutionId}</dd></div> : null}
        {testRun.expiresAt ? <div><dt>Expires</dt><dd>{formatDate(testRun.expiresAt)}</dd></div> : null}
      </dl>
    </section>
  );
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

function getDraftSignature(draft: WorkflowDraft) {
  return JSON.stringify({ state: draft.state, layout: draft.layout });
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

function formatDate(value?: string | null) {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function formatDuration(start?: string | null, end?: string | null) {
  if (!start || !end) return "";

  const startTime = Date.parse(start);
  const endTime = Date.parse(end);
  if (Number.isNaN(startTime) || Number.isNaN(endTime) || endTime < startTime) return "";

  const totalSeconds = Math.round((endTime - startTime) / 1000);
  if (totalSeconds < 60) return `${totalSeconds}s`;

  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes < 60) return seconds ? `${minutes}m ${seconds}s` : `${minutes}m`;

  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  return remainingMinutes ? `${hours}h ${remainingMinutes}m` : `${hours}h`;
}
