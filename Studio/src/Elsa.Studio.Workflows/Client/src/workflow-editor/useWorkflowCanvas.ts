import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  addEdge,
  applyEdgeChanges,
  applyNodeChanges,
  reconnectEdge,
  type Connection,
  type Edge,
  type EdgeChange,
  type Node,
  type NodeChange,
  type OnConnectEnd,
  type OnConnectStart,
  type OnReconnect,
  type ReactFlowInstance,
  type Viewport,
  type XYPosition
} from "@xyflow/react";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import {
  buildCanvas,
  buildUnsupportedActivityCanvas,
  createActivityNode,
  createWorkflowEdge,
  getActivityDisplay,
  getActivitySourcePorts,
  getChildSlots,
  resolveActivityIcon,
  resolveScope,
  spliceWorkflowEdge,
  updateLayout,
  updateScopeActivities,
  updateScopeOwner,
  syncCanvasToScope,
  withFlowchartConnections,
  type CanvasScope,
  type WorkflowNodeData
} from "../workflowAdapter";
import { computeAutoLayout } from "../workflowLayout";
import { activityDragDataType, pointerDragThreshold } from "./constants";
import {
  clientPointFromEvent,
  createNodeId,
  isConnectEndOverExistingWorkflowNode,
  midpointBetween,
  resolveConnectEndSource,
  rightOf
} from "./editorHelpers";
import type { ConnectMenuState, WorkflowConnectSource, WorkflowEdge } from "./editorTypes";
import type { WorkflowEdgeActions } from "./contexts";
import type { WorkflowDraftRecipe } from "./workflowDocument";
import { planActivityDrop } from "./addActivityRouting";
import type { ScopeFrame } from "../workflowAdapter";

const rootScopeViewportKey = "root";

function getScopeViewportKey(frames: ScopeFrame[]) {
  if (frames.length === 0) return rootScopeViewportKey;
  return frames.map(frame => `${frame.ownerNodeId}:${frame.slotId}`).join("/");
}

function sameNodeIds(nodes: Node<WorkflowNodeData>[], nodeIds: string[]) {
  return nodes.length === nodeIds.length && nodes.every((node, index) => node.id === nodeIds[index]);
}

interface WorkflowCanvasParams {
  // Editor document reads. Scope/owner are resolved by the caller (they feed the inspector too), so the
  // canvas consumes them rather than recomputing.
  draft: WorkflowDraft | null;
  scope: CanvasScope | null;
  scopeOwner: ActivityNode | null;
  // Live scope breadcrumb. A non-empty path means the user is *inside* a slot, so drop routing must never
  // fall back to mutating the workflow root — it either writes into the resolved slot or resets on a stale
  // frame. The canvas reads it (rather than only `scope`) to make that guard explicit.
  frames: ScopeFrame[];
  catalog: ActivityCatalogItem[];
  catalogByVersion: Map<string, ActivityCatalogItem>;
  isUnsupportedDesigner: boolean;
  isFlowchartDesigner: boolean;
  canAddActivitiesToCanvas: boolean;
  selectedNodeId: string | null;
  // Document mutations (from useWorkflowDocument). The canvas never touches the reducer directly.
  editDraft(recipe: WorkflowDraftRecipe): void;
  editDraftAndSelect(recipe: WorkflowDraftRecipe, selectedNodeId: string | null): void;
  select(selectedNodeId: string | null): void;
  // Recover from a stale/broken frame path by returning to the workflow root.
  resetToRoot(): void;
  // Transient status/error messaging owned by WorkflowEditor.
  setStatus(value: string): void;
  setError(value: string): void;
}

// Owns the React Flow mirror (nodes/edges) of the current scope and every interaction that mutates it:
// palette add / drag-drop, edge splicing, connect-menu wiring, deletes, reconnects, and auto-layout.
// The mirror is rebuilt from the committed draft whenever the scope changes; edits are applied to the
// local mirror for responsiveness and then committed back into the draft via the document reducer.
export function useWorkflowCanvas({
  draft,
  scope,
  scopeOwner,
  frames,
  catalog,
  catalogByVersion,
  isUnsupportedDesigner,
  isFlowchartDesigner,
  canAddActivitiesToCanvas,
  selectedNodeId,
  editDraft,
  editDraftAndSelect,
  select,
  resetToRoot,
  setStatus,
  setError
}: WorkflowCanvasParams) {
  const [nodes, setNodes] = useState<Node<WorkflowNodeData>[]>([]);
  const [edges, setEdges] = useState<WorkflowEdge[]>([]);
  const [reactFlowInstance, setReactFlowInstance] = useState<ReactFlowInstance<Node<WorkflowNodeData>, WorkflowEdge> | null>(null);
  const [connectMenu, setConnectMenu] = useState<ConnectMenuState | null>(null);
  const [highlightedEdgeId, setHighlightedEdgeId] = useState<string | null>(null);
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const connectSourceRef = useRef<WorkflowConnectSource | null>(null);
  const viewportSnapshotsRef = useRef(new Map<string, Viewport>());
  const visitedViewportScopesRef = useRef(new Set<string>());
  const pendingViewportScopeKeyRef = useRef<string | null>(null);
  const pendingViewportNodeIdsRef = useRef<string[]>([]);
  const pointerDragRef = useRef<{
    activity: ActivityCatalogItem;
    startX: number;
    startY: number;
    dragging: boolean;
  } | null>(null);
  const nativePaletteDragRef = useRef<{ activityVersionId: string; handledDrop: boolean } | null>(null);
  const suppressPaletteClickRef = useRef(false);
  const scopeViewportKey = useMemo(() => getScopeViewportKey(frames), [frames]);

  useEffect(() => {
    return () => {
      if (!reactFlowInstance) return;
      viewportSnapshotsRef.current.set(scopeViewportKey, reactFlowInstance.getViewport());
    };
  }, [reactFlowInstance, scopeViewportKey]);

  useEffect(() => {
    pendingViewportScopeKeyRef.current = scopeViewportKey;

    if (!scopeOwner) {
      pendingViewportNodeIdsRef.current = [];
      setNodes([]);
      setEdges([]);
      return;
    }

    const canvas = isUnsupportedDesigner
      ? buildUnsupportedActivityCanvas(scopeOwner, catalog, draft?.layout ?? [])
      : scope
        ? buildCanvas(scope, catalog, draft?.layout ?? [])
        : { nodes: [], edges: [] };
    pendingViewportNodeIdsRef.current = canvas.nodes.map(node => node.id);
    setNodes(canvas.nodes);
    setEdges(canvas.edges as WorkflowEdge[]);
  }, [catalog, draft?.layout, isUnsupportedDesigner, scope, scopeOwner, scopeViewportKey]);

  useEffect(() => {
    if (!reactFlowInstance) return;
    if (pendingViewportScopeKeyRef.current !== scopeViewportKey) return;
    if (!sameNodeIds(nodes, pendingViewportNodeIdsRef.current)) return;

    pendingViewportScopeKeyRef.current = null;
    const savedViewport = viewportSnapshotsRef.current.get(scopeViewportKey);
    const hasVisitedScope = visitedViewportScopesRef.current.has(scopeViewportKey);
    visitedViewportScopesRef.current.add(scopeViewportKey);

    window.requestAnimationFrame(() => {
      if (savedViewport) {
        reactFlowInstance.setViewport(savedViewport);
      } else if (!hasVisitedScope && nodes.length > 0) {
        reactFlowInstance.fitView({ padding: 0.2 });
      }
    });
  }, [nodes, reactFlowInstance, scopeViewportKey]);

  // Pins a node's canvas position into the layout records (removing any prior record for that node so a
  // moved node doesn't accumulate duplicates). No-ops when the drop had no cursor position (palette click).
  const pinLayout = useCallback((layout: WorkflowDraft["layout"], nodeId: string, position?: XYPosition) =>
    position
      ? [
          ...layout.filter(record => record.nodeId !== nodeId),
          { nodeId, x: Math.round(position.x), y: Math.round(position.y) }
        ]
      : layout, []);

  // Returns the created ActivityNode when the drop landed in the current scope's slot (so callers can
  // chain navigation, e.g. descending into a just-assigned container); null for every other outcome.
  const addActivity = useCallback((activity: ActivityCatalogItem, position?: XYPosition): ActivityNode | null => {
    if (draft?.state.rootActivity && isUnsupportedDesigner) {
      return null;
    }

    const next = createActivityNode(activity, createNodeId(activity));
    // Route the drop against the LIVE frame path. Being inside a slot (frames.length > 0) makes wrapping
    // or leaf-erroring the root impossible: a failed resolve there is a stale frame, not a bare root.
    const plan = planActivityDrop(draft?.state.rootActivity, frames, next, activity, catalogByVersion);

    if (plan.kind === "becomeRoot") {
      editDraftAndSelect(
        ({ draft: current }) => current ? { ...current, state: { ...current.state, rootActivity: next } } : null,
        next.nodeId
      );
      return null;
    }

    if (plan.kind === "leafError") {
      setStatus("");
      setError("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
      return null;
    }

    if (plan.kind === "staleFrames") {
      setStatus("");
      setError("This slot could not be resolved — returning to the workflow root.");
      resetToRoot();
      return null;
    }

    if (plan.kind === "wrapRoot") {
      // Keep the original root (now nested inside the new wrapper) selected — same as before the refactor.
      editDraftAndSelect(({ draft: current }) => {
        const existingRoot = current?.state.rootActivity;
        if (!existingRoot) return null;
        return {
          ...current,
          layout: pinLayout(current.layout, existingRoot.nodeId, position),
          state: { ...current.state, rootActivity: updateScopeActivities(next, [], [existingRoot], activity) }
        };
      }, draft?.state.rootActivity?.nodeId ?? null);
      setError("");
      setStatus(`Wrapped root in ${getActivityDisplay(activity)}`);
      return null;
    }

    // plan.kind === "addToSlot": append (many) or set (single) the dropped activity into the resolved slot.
    editDraftAndSelect(({ draft: current, frames: currentFrames }) => {
      if (!current?.state.rootActivity) return null;

      const currentScope = resolveScope(current.state.rootActivity, currentFrames, catalogByVersion);
      if (!currentScope) return null;

      const nextActivities = currentScope.slot.cardinality === "single"
        ? [next]
        : [...currentScope.slot.activities, next];
      const updatedRoot = updateScopeActivities(current.state.rootActivity, currentFrames, nextActivities, catalogByVersion);

      return {
        ...current,
        layout: pinLayout(current.layout, next.nodeId, position),
        state: { ...current.state, rootActivity: updatedRoot }
      };
    }, next.nodeId);

    // A single-cardinality slot that already held a different node just had its content overwritten;
    // surface it so the loss is visible (undo history covers recovery).
    if (plan.replacedActivity) {
      setError("");
      setStatus(`Replaced ${plan.slot.label} content`);
    }

    return next;
  }, [catalogByVersion, draft?.state.rootActivity, frames, isUnsupportedDesigner, editDraftAndSelect, resetToRoot, pinLayout, setError, setStatus]);

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
        childSlots: getChildSlots(activityNode, activity),
        acceptsInbound: String(activity.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: getActivitySourcePorts(activityNode, activity)
      }
    };

    return { activityNode, node };
  }, []);

  const commitCanvas = useCallback((nextNodes: Node<WorkflowNodeData>[], nextEdges: WorkflowEdge[], additionalActivities: ActivityNode[] = []) => {
    if (isUnsupportedDesigner) return;

    editDraft(({ draft: current, frames: currentFrames }) => {
      if (!current) return null;

      const nextLayout = updateLayout(current.layout, nextNodes);
      const rootActivity = current.state.rootActivity;
      if (!rootActivity) return { ...current, layout: nextLayout };

      const currentScope = resolveScope(rootActivity, currentFrames, catalogByVersion);
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
          rootActivity: updateScopeOwner(rootActivity, currentFrames, nextOwner, catalogByVersion)
        }
      };
    });
  }, [catalogByVersion, isUnsupportedDesigner, editDraft]);

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
    select(placed.node.id);
    commitCanvas(nextNodes, nextEdges, [placed.activityNode]);
  }, [commitCanvas, createCanvasActivity, edges, nodes, select]);

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

  // Opens the "pick any activity" menu for an empty canvas. Works on slot levels too (sequence bodies,
  // ForEach body, Switch cases): `onConnectMenuPick`'s `fromEmpty` branch commits through `commitCanvas`
  // which writes into whatever slot the current scope resolves to — no flowchart-only assumption. An
  // optional anchor lets the empty-slot picker open the menu next to its "Browse all activities…" button
  // instead of the canvas centre.
  const openEmptyConnectMenu = (anchor?: { clientX: number; clientY: number }) => {
    if (!canAddActivitiesToCanvas) return;

    if (anchor) {
      setConnectMenu({ kind: "fromEmpty", clientX: anchor.clientX, clientY: anchor.clientY });
      return;
    }

    const rect = canvasRef.current?.getBoundingClientRect();
    if (!rect) return;
    setConnectMenu({
      kind: "fromEmpty",
      clientX: rect.left + rect.width / 2,
      clientY: rect.top + rect.height / 2
    });
  };

  const onNodesChange = (changes: NodeChange<Node<WorkflowNodeData>>[]) => {
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
  }, [edges, nodes, scope, isUnsupportedDesigner, commitCanvas, reactFlowInstance, setStatus]);

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
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: newConnection.targetHandle ?? null
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
    if (selectedNodeId && deletedIds.has(selectedNodeId)) select(null);
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
      select(placed.node.id);
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
      select(placed.node.id);
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

  return {
    nodes,
    edges,
    canvasRef,
    setReactFlowInstance,
    connectMenu,
    setConnectMenu,
    edgeActions,
    onNodesChange,
    onEdgesChange,
    onNodesDelete,
    onEdgesDelete,
    isValidConnection,
    onConnect,
    onConnectStart,
    onConnectEnd,
    onReconnect,
    commitLayout,
    canAutoLayout,
    autoLayout,
    onCanvasDragOver,
    onCanvasDragLeave,
    onCanvasDrop,
    openEmptyConnectMenu,
    onConnectMenuPick,
    addActivity,
    onPaletteClick,
    onPaletteDragStart,
    onPaletteDragEnd,
    onPalettePointerDown
  };
}
