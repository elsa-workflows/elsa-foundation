import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ReactFlow,
  Background,
  Controls,
  Handle,
  MiniMap,
  Position,
  addEdge,
  applyEdgeChanges,
  applyNodeChanges,
  type Connection,
  type Edge,
  type EdgeChange,
  type Node,
  type NodeChange,
  type NodeProps,
  type ReactFlowInstance,
  type XYPosition
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { AlertCircle, Boxes, Check, ChevronRight, GitBranch, ListTree, Play, Plus, RotateCcw, Save, Search, Trash2 } from "lucide-react";
import type { ElsaStudioModuleApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  createDefinition,
  deleteDefinition,
  deleteDefinitionPermanently,
  getDefinition,
  listActivities,
  listDefinitions,
  promoteDraft,
  publishVersion,
  restoreDefinition,
  runExecutable,
  updateDraft
} from "./workflowsApi";
import type { ActivityCatalogItem, ActivityNode, DefinitionListState, WorkflowDefinitionDetails, WorkflowDraft } from "./workflowTypes";
import {
  buildCanvas,
  createActivityNode,
  getActivityDisplay,
  getChildSlots,
  resolveScope,
  updateLayout,
  updateScopeActivities,
  updateScopeOwner,
  syncCanvasToScope,
  withFlowchartConnections,
  type ScopeFrame,
  type WorkflowNodeData
} from "./workflowAdapter";
import "./styles.css";

const nodeTypes = { workflowActivity: WorkflowActivityNode };
const activityDragDataType = "application/x-elsa-activity-version-id";
const pointerDragThreshold = 6;
const autosaveDelayMs = 1200;

export function register(api: ElsaStudioModuleApi) {
  api.navigation.add({
    id: "workflows",
    label: "Workflows",
    path: "/workflows/definitions",
    order: 20,
    iconColor: "#0ea5e9"
  });

  api.routes.add({
    id: "workflows-definitions",
    path: "/workflows/definitions",
    label: "Workflow definitions",
    component: () => <WorkflowManagementPage context={api.backend} />
  });
}

function WorkflowManagementPage({ context }: { context: StudioEndpointContext }) {
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
    ? <WorkflowEditor context={context} definitionId={definitionId} onBack={() => openDefinition(null)} />
    : <WorkflowDefinitions context={context} onOpen={openDefinition} />;
}

function readDefinitionIdFromUrl() {
  return new URLSearchParams(window.location.search).get("definition");
}

function WorkflowDefinitions({ context, onOpen }: { context: StudioEndpointContext; onOpen(id: string): void }) {
  const [search, setSearch] = useState("");
  const [listState, setListState] = useState<DefinitionListState>("active");
  const [state, setState] = useState<"loading" | "ready" | "failed">("loading");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [definitions, setDefinitions] = useState<WorkflowDefinitionDetails["definition"][]>([]);

  const load = useCallback(async () => {
    setState("loading");
    setError("");
    try {
      const response = await listDefinitions(context, search, listState);
      setDefinitions(response.definitions);
      setState("ready");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setState("failed");
    }
  }, [context, search, listState]);

  useEffect(() => {
    void load();
  }, [load]);

  const create = async (rootKind: "sequence" | "flowchart") => {
    const name = window.prompt("Workflow name", rootKind === "sequence" ? "Sequence workflow" : "Flowchart workflow");
    if (!name?.trim()) return;
    const details = await createDefinition(context, { name, description: null, rootKind });
    onOpen(details.definition.id);
  };

  const softDelete = async (definition: WorkflowDefinitionDetails["definition"]) => {
    if (!window.confirm(`Delete workflow definition "${definition.name}"? You can restore it from the Deleted view.`)) return;
    setStatus("");
    setError("");
    try {
      await deleteDefinition(context, definition.id);
      setStatus(`Deleted ${definition.name}`);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const restore = async (definition: WorkflowDefinitionDetails["definition"]) => {
    setStatus("");
    setError("");
    try {
      await restoreDefinition(context, definition.id);
      setStatus(`Restored ${definition.name}`);
      await load();
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
      setStatus(`Permanently deleted ${definition.name}`);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  return (
    <section className="wf-page">
      <div className="wf-page-header">
        <div>
          <span className="wf-kicker">Workflow management</span>
          <h2>Definitions</h2>
        </div>
        <div className="wf-actions">
          <button type="button" onClick={() => void create("sequence")}><Plus size={15} /> Sequence</button>
          <button type="button" onClick={() => void create("flowchart")}><GitBranch size={15} /> Flowchart</button>
        </div>
      </div>

      <div className="wf-toolbar">
        <div className="wf-segmented" role="tablist" aria-label="Definition state">
          <button type="button" className={listState === "active" ? "active" : ""} aria-selected={listState === "active"} onClick={() => setListState("active")}>Active</button>
          <button type="button" className={listState === "deleted" ? "active" : ""} aria-selected={listState === "deleted"} onClick={() => setListState("deleted")}>Deleted</button>
        </div>
        <label className="wf-search">
          <Search size={15} />
          <input value={search} onChange={event => setSearch(event.target.value)} placeholder="Search definitions" />
        </label>
        <button type="button" onClick={() => void load()}>Refresh</button>
      </div>

      {state === "failed" ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}
      {status ? <div className="wf-status-line"><Check size={14} /> {status}</div> : null}
      {state === "loading" ? <div className="wf-empty">Loading workflow definitions...</div> : null}
      {state === "ready" && definitions.length === 0 ? <div className="wf-empty">No {listState} workflow definitions found.</div> : null}
      {state === "ready" && definitions.length > 0 ? (
        <div className="wf-grid" role="table" aria-label="Workflow definitions">
          <div className="wf-grid-head" role="row">
            <span>Name</span>
            <span>Latest version</span>
            <span>{listState === "deleted" ? "Deleted" : "Draft"}</span>
            <span>Modified</span>
            <span>Actions</span>
          </div>
          {definitions.map(definition => (
            <div className="wf-grid-row" role="row" key={definition.id}>
              <span>
                <strong>{definition.name}</strong>
                <small>{definition.description || definition.id}</small>
              </span>
              <span>{definition.latestVersion ?? "No version"}</span>
              <span>{listState === "deleted" ? formatDate(definition.deletedAt) : (definition.draftId ? "Draft" : "None")}</span>
              <span>{formatDate(definition.lastModifiedAt)}</span>
              <span className="wf-row-actions">
                {listState === "active" ? (
                  <>
                    <button type="button" onClick={() => onOpen(definition.id)}>Open</button>
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
      ) : null}
    </section>
  );
}

function WorkflowEditor({ context, definitionId, onBack }: { context: StudioEndpointContext; definitionId: string; onBack(): void }) {
  const [details, setDetails] = useState<WorkflowDefinitionDetails | null>(null);
  const [draft, setDraft] = useState<WorkflowDraft | null>(null);
  const [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
  const [frames, setFrames] = useState<ScopeFrame[]>([]);
  const [nodes, setNodes] = useState<Node<WorkflowNodeData>[]>([]);
  const [edges, setEdges] = useState<Edge[]>([]);
  const [reactFlowInstance, setReactFlowInstance] = useState<ReactFlowInstance<Node<WorkflowNodeData>, Edge> | null>(null);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [autosaveEnabled, setAutosaveEnabled] = useState(false);
  const [publishedArtifactId, setPublishedArtifactId] = useState<string | null>(null);
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const lastSavedDraftSignatureRef = useRef("");
  const saveRequestIdRef = useRef(0);
  const pointerDragRef = useRef<{
    activity: ActivityCatalogItem;
    startX: number;
    startY: number;
    dragging: boolean;
  } | null>(null);
  const suppressPaletteClickRef = useRef(false);

  const root = draft?.state.rootActivity ?? null;
  const scope = useMemo(() => resolveScope(root, frames), [root, frames]);
  const catalogByVersion = useMemo(() => new Map(catalog.map(activity => [activity.activityVersionId, activity])), [catalog]);
  const selectedNode = useMemo(() => scope?.slot.activities.find(activity => activity.nodeId === selectedNodeId) ?? null, [scope, selectedNodeId]);
  const selectedSlots = selectedNode ? getChildSlots(selectedNode) : [];

  const load = useCallback(async () => {
    setError("");
    const [nextDetails, nextCatalog] = await Promise.all([
      getDefinition(context, definitionId),
      listActivities(context)
    ]);
    const nextDraft = nextDetails.draft ?? null;
    setDetails(nextDetails);
    lastSavedDraftSignatureRef.current = nextDraft ? getDraftSignature(nextDraft) : "";
    setDraft(nextDraft);
    setCatalog(nextCatalog.activities);
    setFrames([]);
    setSelectedNodeId(null);
  }, [context, definitionId]);

  useEffect(() => {
    void load().catch(e => setError(e instanceof Error ? e.message : String(e)));
  }, [load]);

  useEffect(() => {
    if (!scope) {
      setNodes([]);
      setEdges([]);
      return;
    }
    const canvas = buildCanvas(scope, catalog, draft?.layout ?? []);
    setNodes(canvas.nodes);
    setEdges(canvas.edges);
  }, [scope, catalog, draft?.layout]);

  const setRoot = (rootActivity: ActivityNode) => {
    setDraft(current => current ? { ...current, state: { ...current.state, rootActivity } } : current);
  };

  const addActivity = useCallback((activity: ActivityCatalogItem, position?: XYPosition) => {
    if (!draft?.state.rootActivity || !scope) return;
    const next = createActivityNode(activity, createNodeId(activity));
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
  }, [draft?.state.rootActivity, frames, scope]);

  const toCanvasPosition = useCallback((clientX: number, clientY: number): XYPosition | null => {
    if (!reactFlowInstance || !canvasRef.current) return null;

    const canvasRect = canvasRef.current.getBoundingClientRect();
    return reactFlowInstance.screenToFlowPosition({
      x: clientX - canvasRect.left,
      y: clientY - canvasRect.top
    });
  }, [reactFlowInstance]);

  const tryAddActivityAtClientPoint = useCallback((activity: ActivityCatalogItem, clientX: number, clientY: number) => {
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

    addActivity(activity, position);
    return true;
  }, [addActivity, toCanvasPosition]);

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
      if (!drag?.dragging || !reactFlowInstance || !canvasRef.current) return;

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
    addActivity(activity);
  };

  const onCanvasDragOver = (event: React.DragEvent) => {
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
  };

  const onCanvasDrop = (event: React.DragEvent) => {
    event.preventDefault();
    const activityVersionId = event.dataTransfer.getData(activityDragDataType) || event.dataTransfer.getData("text/plain");
    const activity = catalogByVersion.get(activityVersionId);
    if (!activity) return;

    tryAddActivityAtClientPoint(activity, event.clientX, event.clientY);
  };

  const saveDraft = useCallback(async (draftSnapshot: WorkflowDraft, savedStatus: string) => {
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
    } catch (e) {
      if (requestId === saveRequestIdRef.current) {
        setStatus("");
        setError(e instanceof Error ? e.message : String(e));
      }
    }
  }, [context]);

  useEffect(() => {
    if (!autosaveEnabled || !draft) return;

    const draftSignature = getDraftSignature(draft);
    if (draftSignature === lastSavedDraftSignatureRef.current) return;

    setStatus("Autosaving...");
    const timeoutId = window.setTimeout(() => {
      void saveDraft(draft, "Autosaved");
    }, autosaveDelayMs);

    return () => window.clearTimeout(timeoutId);
  }, [autosaveEnabled, draft, saveDraft]);

  const save = async () => {
    if (!draft) return;
    setStatus("Saving...");
    await saveDraft(draft, "Saved");
  };

  const promoteAndPublish = async () => {
    if (!draft) return;
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
    }
  };

  const run = async () => {
    if (!publishedArtifactId) return;
    setStatus("Running...");
    try {
      await runExecutable(context, publishedArtifactId);
      setStatus("Run dispatched");
    } catch (e) {
      setStatus("");
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const onNodesChange = (changes: NodeChange[]) => setNodes(current => applyNodeChanges(changes, current));
  const onEdgesChange = (changes: EdgeChange[]) => setEdges(current => applyEdgeChanges(changes, current));
  const onConnect = (connection: Connection) => {
    if (!draft?.state.rootActivity || !scope || scope.slot.mode !== "flowchart") return;
    const nextEdges = addEdge(connection, edges);
    const nextOwner = withFlowchartConnections(scope.owner, nextEdges);
    setEdges(nextEdges);
    setRoot(updateScopeOwner(draft.state.rootActivity, frames, nextOwner));
  };
  const commitLayout = () => {
    setDraft(current => {
      if (!current) return current;
      const nextLayout = updateLayout(current.layout, nodes);
      if (!current.state.rootActivity || !scope) return { ...current, layout: nextLayout };
      const nextOwner = syncCanvasToScope(scope, nodes, edges);
      return {
        ...current,
        layout: nextLayout,
        state: {
          ...current.state,
          rootActivity: updateScopeOwner(current.state.rootActivity, frames, nextOwner)
        }
      };
    });
  };

  const enterSlot = (node: ActivityNode, slotId: string, label: string) => {
    setFrames(current => [...current, { ownerNodeId: node.nodeId, slotId, label }]);
    setSelectedNodeId(null);
  };

  if (!details || !draft) {
    return <div className="wf-empty">{error || "Loading workflow editor..."}</div>;
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
          <button type="button" onClick={() => void save()}><Save size={15} /> Save</button>
          <button type="button" onClick={() => void promoteAndPublish()}><GitBranch size={15} /> Promote</button>
          <button type="button" disabled={!publishedArtifactId} onClick={() => void run()}><Play size={15} /> Run</button>
        </div>
      </div>

      {error ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}

      <div className="wf-editor-body">
        <aside className="wf-palette">
          <div className="wf-panel-title"><Boxes size={15} /> Activities</div>
          <div className="wf-palette-list">
            {catalog.map(activity => (
              <button
                type="button"
                key={activity.activityVersionId}
                draggable
                onClick={() => onPaletteClick(activity)}
                onDragStart={event => onPaletteDragStart(event, activity)}
                onDragEnd={event => onPaletteDragEnd(event, activity)}
                onPointerDown={event => onPalettePointerDown(event, activity)}
              >
                <strong>{getActivityDisplay(activity)}</strong>
                <small>{activity.category}</small>
              </button>
            ))}
          </div>
        </aside>

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
          <div className="wf-canvas" ref={canvasRef} onDragOver={onCanvasDragOver} onDrop={onCanvasDrop}>
            <ReactFlow
              nodes={nodes}
              edges={edges}
              nodeTypes={nodeTypes}
              onInit={setReactFlowInstance}
              onNodesChange={onNodesChange}
              onEdgesChange={onEdgesChange}
              onConnect={onConnect}
              onDragOver={onCanvasDragOver}
              onDrop={onCanvasDrop}
              onNodeClick={(_, node) => setSelectedNodeId(node.id)}
              onNodeDragStop={commitLayout}
              fitView
            >
              <Background gap={18} size={1} />
              <Controls />
              <MiniMap pannable zoomable />
            </ReactFlow>
          </div>
          <ValidationPanel draft={draft} />
        </main>

        <aside className="wf-inspector">
          <div className="wf-panel-title"><ListTree size={15} /> Inspector</div>
          {selectedNode ? (
            <div className="wf-inspector-content">
              <h3>{nodes.find(node => node.id === selectedNode.nodeId)?.data.label ?? selectedNode.nodeId}</h3>
              <dl>
                <dt>Node ID</dt>
                <dd>{selectedNode.nodeId}</dd>
                <dt>Activity version</dt>
                <dd>{selectedNode.activityVersionId}</dd>
              </dl>
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
          ) : <p className="wf-muted">Select an activity to inspect properties and embedded slots.</p>}
        </aside>
      </div>
    </section>
  );
}

function WorkflowActivityNode({ data, selected }: NodeProps) {
  const nodeData = data as WorkflowNodeData;
  return (
    <div className={selected ? "wf-node selected" : "wf-node"}>
      <Handle type="target" position={Position.Left} />
      <strong>{nodeData.label}</strong>
      <small>{nodeData.activityTypeKey ?? nodeData.activityVersionId}</small>
      {nodeData.childSlots.length > 0 ? <span>{nodeData.childSlots.length} embedded slot{nodeData.childSlots.length === 1 ? "" : "s"}</span> : null}
      <Handle type="source" position={Position.Right} />
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

function createNodeId(activity: ActivityCatalogItem) {
  const base = getActivityDisplay(activity).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity";
  return `${base}-${crypto.randomUUID().slice(0, 8)}`;
}

function getDraftSignature(draft: WorkflowDraft) {
  return JSON.stringify({ state: draft.state, layout: draft.layout });
}

function formatDate(value?: string | null) {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
