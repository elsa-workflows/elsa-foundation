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

interface WorkflowCanvasParams {
  // Editor document reads. Scope/owner are resolved by the caller (they feed the inspector too), so the
  // canvas consumes them rather than recomputing.
  draft: WorkflowDraft | null;
  scope: CanvasScope | null;
  scopeOwner: ActivityNode | null;
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
  catalog,
  catalogByVersion,
  isUnsupportedDesigner,
  isFlowchartDesigner,
  canAddActivitiesToCanvas,
  selectedNodeId,
  editDraft,
  editDraftAndSelect,
  select,
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
  const pointerDragRef = useRef<{
    activity: ActivityCatalogItem;
    startX: number;
    startY: number;
    dragging: boolean;
  } | null>(null);
  const nativePaletteDragRef = useRef<{ activityVersionId: string; handledDrop: boolean } | null>(null);
  const suppressPaletteClickRef = useRef(false);

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

  const addActivity = useCallback((activity: ActivityCatalogItem, position?: XYPosition) => {
    if (draft?.state.rootActivity && isUnsupportedDesigner) {
      return;
    }

    const next = createActivityNode(activity, createNodeId(activity));
    if (!draft?.state.rootActivity) {
      editDraftAndSelect(
        ({ draft: current }) => current ? { ...current, state: { ...current.state, rootActivity: next } } : null,
        next.nodeId
      );
      return;
    }

    if (!scope) {
      const childSlot = getChildSlots(next)[0];
      if (!childSlot) {
        setStatus("");
        setError("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }

      editDraftAndSelect(({ draft: current }) => {
        if (!current?.state.rootActivity) return null;

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
      }, draft.state.rootActivity.nodeId);
      setError("");
      setStatus(`Wrapped root in ${getActivityDisplay(activity)}`);
      return;
    }

    editDraftAndSelect(({ draft: current, frames: currentFrames }) => {
      if (!current?.state.rootActivity) return null;

      const currentScope = resolveScope(current.state.rootActivity, currentFrames);
      if (!currentScope) return null;

      const updatedRoot = updateScopeActivities(current.state.rootActivity, currentFrames, [...currentScope.slot.activities, next]);
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
    }, next.nodeId);
  }, [draft?.state.rootActivity, isUnsupportedDesigner, scope, editDraftAndSelect, setError, setStatus]);

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

    editDraft(({ draft: current, frames: currentFrames }) => {
      if (!current) return null;

      const nextLayout = updateLayout(current.layout, nextNodes);
      const rootActivity = current.state.rootActivity;
      if (!rootActivity) return { ...current, layout: nextLayout };

      const currentScope = resolveScope(rootActivity, currentFrames);
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
          rootActivity: updateScopeOwner(rootActivity, currentFrames, nextOwner)
        }
      };
    });
  }, [isUnsupportedDesigner, editDraft]);

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
    onPaletteClick,
    onPaletteDragStart,
    onPaletteDragEnd,
    onPalettePointerDown
  };
}
