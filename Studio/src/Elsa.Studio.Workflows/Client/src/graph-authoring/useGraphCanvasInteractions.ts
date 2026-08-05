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
  type OnNodeDrag,
  type OnReconnect,
  type ReactFlowInstance,
  type Viewport,
  type XYPosition
} from "@xyflow/react";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import {
  buildSequenceEdges,
  createActivityNode,
  createWorkflowEdge,
  getActivityDisplay,
  getActivitySourcePorts,
  getChildSlots,
  resolveActivityIcon,
  type WorkflowNodeData
} from "../workflowAdapter";
import { computeAutoLayout } from "../workflowLayout";
import { activityDragDataType, pointerDragThreshold } from "../workflow-editor/constants";
import {
  createNodeId,
  insertSequenceNodeAfter,
  midpointBetween,
  rightOf
} from "../workflow-editor/editorHelpers";
import {
  clientPointFromEvent,
  isConnectEndOverExistingWorkflowNode,
  resolveConnectEndSource
} from "../workflow-editor/connectEndHelpers";
import type { ConnectMenuState, WorkflowConnectSource, WorkflowEdge } from "../workflow-editor/editorTypes";
import type { WorkflowEdgeActions } from "../workflow-editor/contexts";
import { decorateWorkflowCanvasElements } from "../workflow-editor/workflowAccessibility";

/**
 * Shape of the slot currently mirrored on the canvas. `unsupported` is a read-only projection of a
 * structure this host cannot edit; `none` means no slot resolved (leaf activity, or nothing chosen).
 */
export type GraphCanvasMode = "flowchart" | "sequence" | "bpmn" | "unsupported" | "none";

/** A canvas node paired with the ActivityNode it represents in the document. */
export interface GraphCanvasPlacement {
  activityNode: ActivityNode;
  node: Node<WorkflowNodeData>;
}

/**
 * Places an already-positioned node on the canvas — see `stampCanvasNode`. Named so a host can hold it
 * in a ref when its own placement path is constructed before the interaction layer.
 */
export type GraphCanvasStamp = (
  node: Node<WorkflowNodeData>,
  plan?: {
    createdActivities?: ActivityNode[];
    order?(cleared: Node<WorkflowNodeData>[]): Node<WorkflowNodeData>[];
    wire?(nextNodes: Node<WorkflowNodeData>[]): WorkflowEdge[];
  }
) => void;

export interface GraphCanvasCommitOptions {
  /**
   * Activities created on the canvas that are not yet in the document's slot. Hosts must thread these
   * into their `syncCanvasToScope` call or the new node is dropped on the next rebuild.
   */
  createdActivities?: ActivityNode[];
  /** Node ids whose per-node document side-tables (presentation, layout) should be dropped. */
  removedNodeIds?: Iterable<string>;
}

export interface GraphCanvasInteractionsParams {
  nodes: Node<WorkflowNodeData>[];
  edges: WorkflowEdge[];
  setNodes: React.Dispatch<React.SetStateAction<Node<WorkflowNodeData>[]>>;
  setEdges: React.Dispatch<React.SetStateAction<WorkflowEdge[]>>;
  mode: GraphCanvasMode;
  /**
   * Identity of the scope on the canvas. Viewport position is snapshotted per key, so descending into
   * a slot and coming back restores the pan/zoom the user left behind instead of re-fitting.
   */
  scopeKey: string;
  canAddActivities: boolean;
  selectedNodeId: string | null;
  catalogByVersion: Map<string, ActivityCatalogItem>;
  select(nodeId: string | null): void;
  /** Applies a canvas transaction to the host's document. */
  commitCanvas(
    nodes: Node<WorkflowNodeData>[],
    edges: WorkflowEdge[],
    options?: GraphCanvasCommitOptions
  ): void;
  /**
   * Routes a palette activity through the host's own document rules — the workflow editor may make it
   * the root or wrap the existing root; the Activity Definition graph always appends to the open slot.
   * Returns the created ActivityNode when one landed on the current canvas, else null.
   */
  placeActivity(activity: ActivityCatalogItem, position?: XYPosition): ActivityNode | null;
  /**
   * Builds the canvas node for an activity placed *directly* on the canvas (edge splice, connect menu),
   * bypassing `placeActivity` because the position and wiring are already decided. Defaults to the
   * catalog-driven `workflowActivity` node; BPMN scopes override it to stamp a bound element.
   */
  createPlacement?(activity: ActivityCatalogItem, position: XYPosition): GraphCanvasPlacement | null;
  /** Defaults to `createWorkflowEdge`; BPMN scopes override it with a plain sequence flow. */
  createEdge?(source: string, target: string, sourceHandle?: string | null, targetHandle?: string | null): WorkflowEdge;
  /** Expands deleted canvas nodes into the document node ids they own (nested children included). */
  resolveRemovedActivityNodeIds?(deleted: Node<WorkflowNodeData>[]): Iterable<string>;
  /** Transient, non-blocking status line. */
  onStatus?(message: string): void;
  /** Fired once per activity actually placed, for host telemetry. */
  onActivityPlaced?(activity: ActivityCatalogItem): void;
}

/**
 * Every canvas interaction that is a pure function of (nodes, edges, slot mode) and a commit seam:
 * palette drag/drop routing, edge splicing, the connect menu, connect/reconnect, deletes, auto-layout,
 * viewport persistence, and canvas focus management.
 *
 * It deliberately owns no document. Hosts keep their own node/edge state and translate committed
 * transactions into their own envelope, which is what lets the workflow designer and Activity
 * Definition graph authoring share one interaction model instead of two drifting copies.
 */
export function useGraphCanvasInteractions({
  nodes,
  edges,
  setNodes,
  setEdges,
  mode,
  scopeKey,
  canAddActivities,
  selectedNodeId,
  catalogByVersion,
  select,
  commitCanvas,
  placeActivity,
  createPlacement,
  createEdge,
  resolveRemovedActivityNodeIds,
  onStatus,
  onActivityPlaced
}: GraphCanvasInteractionsParams) {
  const [reactFlowInstance, setReactFlowInstance] = useState<ReactFlowInstance<Node<WorkflowNodeData>, WorkflowEdge> | null>(null);
  const [connectMenu, setConnectMenu] = useState<ConnectMenuState | null>(null);
  const [highlightedEdgeId, setHighlightedEdgeId] = useState<string | null>(null);
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const connectSourceRef = useRef<WorkflowConnectSource | null>(null);
  const viewportSnapshotsRef = useRef(new Map<string, Viewport>());
  const visitedViewportScopesRef = useRef(new Set<string>());
  const settledViewportScopeKeyRef = useRef<string | null>(null);
  const staleScopeNodesRef = useRef<Node<WorkflowNodeData>[] | null>(null);
  const pointerDragRef = useRef<{
    activity: ActivityCatalogItem;
    startX: number;
    startY: number;
    dragging: boolean;
  } | null>(null);
  const nativePaletteDragRef = useRef<{ activityVersionId: string; handledDrop: boolean } | null>(null);
  const suppressPaletteClickRef = useRef(false);
  const pendingFocusNodeIdRef = useRef<string | null>(null);

  const isUnsupported = mode === "unsupported";
  // BPMN shares the free-connection graph editing model with flowcharts; sequence keeps its linear model.
  const isGraphMode = mode === "flowchart" || mode === "bpmn";
  const canCreateActivityFromPort = canAddActivities && !isUnsupported && (isGraphMode || mode === "sequence");

  const edgeFactory = useCallback(
    (source: string, target: string, sourceHandle?: string | null, targetHandle?: string | null) =>
      (createEdge ?? createWorkflowEdge)(source, target, sourceHandle, targetHandle) as WorkflowEdge,
    [createEdge]
  );

  const placementFactory = useCallback((activity: ActivityCatalogItem, position: XYPosition): GraphCanvasPlacement | null => {
    if (createPlacement) return createPlacement(activity, position);
    const activityNode = createActivityNode(activity, createNodeId(activity));
    return {
      activityNode,
      node: {
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
      }
    };
  }, [createPlacement]);

  useEffect(() => () => {
    if (!reactFlowInstance) return;
    viewportSnapshotsRef.current.set(scopeKey, reactFlowInstance.getViewport());
  }, [reactFlowInstance, scopeKey]);

  // Restore (or first-time fit) the viewport once the host has rebuilt `nodes` for the new scope.
  // The rebuild is asynchronous relative to the scope change — the host sees the new scope, then
  // pushes a fresh node array on a later commit — so acting on the scope change alone would restore
  // against the *previous* scope's nodes. Waiting for the node array's identity to change is the one
  // signal available here that does not require the host to announce its rebuild.
  useEffect(() => {
    if (settledViewportScopeKeyRef.current !== scopeKey) {
      settledViewportScopeKeyRef.current = scopeKey;
      staleScopeNodesRef.current = nodes;
      return;
    }
    if (staleScopeNodesRef.current === null || staleScopeNodesRef.current === nodes) return;
    staleScopeNodesRef.current = null;

    if (!reactFlowInstance) return;
    const savedViewport = viewportSnapshotsRef.current.get(scopeKey);
    const hasVisitedScope = visitedViewportScopesRef.current.has(scopeKey);
    visitedViewportScopesRef.current.add(scopeKey);

    window.requestAnimationFrame(() => {
      if (savedViewport) {
        reactFlowInstance.setViewport(savedViewport);
      } else if (!hasVisitedScope && nodes.length > 0) {
        reactFlowInstance.fitView({ padding: 0.2 });
      }
    });
  }, [nodes, reactFlowInstance, scopeKey]);

  // React Flow's keyboard selection is emitted as a node change, while the inspector is driven by the
  // host's selected node id. Keep both projections aligned so Enter/Space has the same result as a
  // pointer click and the accessible pressed state always describes what the inspector is showing.
  useEffect(() => {
    setNodes(current => {
      let changed = false;
      const next = current.map(node => {
        const selected = node.id === selectedNodeId;
        if (!!node.selected === selected) return node;
        changed = true;
        return { ...node, selected };
      });
      return changed ? next : current;
    });
  }, [selectedNodeId, setNodes]);

  const focusCanvasNode = useCallback((nodeId: string) => {
    const element = Array.from(canvasRef.current?.querySelectorAll<HTMLElement>(".react-flow__node") ?? [])
      .find(candidate => candidate.dataset.id === nodeId);
    if (!element) return false;
    element.focus({ preventScroll: true });
    return true;
  }, []);

  const queueCanvasNodeFocus = useCallback((nodeId: string) => {
    pendingFocusNodeIdRef.current = nodeId;
    window.requestAnimationFrame(() => {
      if (pendingFocusNodeIdRef.current !== nodeId || !focusCanvasNode(nodeId)) return;
      pendingFocusNodeIdRef.current = null;
    });
  }, [focusCanvasNode]);

  useEffect(() => {
    const nodeId = pendingFocusNodeIdRef.current;
    if (nodeId) queueCanvasNodeFocus(nodeId);
  }, [nodes, queueCanvasNodeFocus]);

  /**
   * Places an already-positioned node on the canvas, clearing the previous selection, selecting and
   * focusing the newcomer, and committing it with whatever edge rewiring the caller decided on.
   * `order` reshuffles the node list (sequence insertion); `wire` derives the next edges from the
   * resulting node order. `createdActivities` is what the host must thread into its sync call — a pure
   * BPMN shape is the one placement that carries none, since it binds no activity.
   */
  const stampCanvasNode = useCallback<GraphCanvasStamp>((node, plan) => {
    const cleared = nodes.map(candidate => candidate.selected ? { ...candidate, selected: false } : candidate);
    const nextNodes = plan?.order ? plan.order(cleared) : [...cleared, node];
    const nextEdges = plan?.wire ? plan.wire(nextNodes) : edges;
    setNodes(nextNodes);
    setEdges(nextEdges);
    select(node.id);
    commitCanvas(nextNodes, nextEdges, { createdActivities: plan?.createdActivities });
    queueCanvasNodeFocus(node.id);
  }, [commitCanvas, edges, nodes, queueCanvasNodeFocus, select, setEdges, setNodes]);

  const placeOnCanvas = useCallback((
    placement: GraphCanvasPlacement,
    plan?: {
      order?(cleared: Node<WorkflowNodeData>[]): Node<WorkflowNodeData>[];
      wire?(nextNodes: Node<WorkflowNodeData>[]): WorkflowEdge[];
    }
  ) => stampCanvasNode(placement.node, { ...plan, createdActivities: [placement.activityNode] }),
  [stampCanvasNode]);

  const toCanvasPosition = useCallback((clientX: number, clientY: number): XYPosition | null => {
    if (!canvasRef.current) return null;

    if (!reactFlowInstance) {
      const canvasRect = canvasRef.current.getBoundingClientRect();
      return { x: clientX - canvasRect.left, y: clientY - canvasRect.top };
    }

    return reactFlowInstance.screenToFlowPosition({ x: clientX, y: clientY });
  }, [reactFlowInstance]);

  const findEdgeUnderCursor = useCallback((clientX: number, clientY: number) => {
    // Optional call to match isConnectEndOverExistingWorkflowNode: hit-testing is a progressive
    // enhancement (edge splicing), never a precondition for the drop itself.
    const element = document.elementFromPoint?.(clientX, clientY) as HTMLElement | null | undefined;
    const edgeElement = element?.closest(".react-flow__edge") as HTMLElement | null;
    return edgeElement?.getAttribute("data-id") ?? null;
  }, []);

  const spliceActivityIntoEdge = useCallback((
    activity: ActivityCatalogItem,
    edge: WorkflowEdge,
    fallbackPosition: XYPosition
  ) => {
    const sourceNode = nodes.find(node => node.id === edge.source);
    const targetNode = nodes.find(node => node.id === edge.target);
    const position = sourceNode && targetNode
      ? midpointBetween(sourceNode, targetNode)
      : sourceNode
        ? rightOf(sourceNode)
        : fallbackPosition;

    const placement = placementFactory(activity, position);
    if (!placement) return;

    placeOnCanvas(placement, {
      wire: () => edges
        .filter(candidate => candidate.id !== edge.id)
        .concat(
          edgeFactory(edge.source, placement.node.id, edge.sourceHandle ?? "Done", undefined),
          edgeFactory(placement.node.id, edge.target, "Done", edge.targetHandle ?? undefined)
        )
    });
    onActivityPlaced?.(activity);
  }, [edgeFactory, edges, nodes, onActivityPlaced, placeOnCanvas, placementFactory]);

  const tryAddActivityAtClientPoint = useCallback((activity: ActivityCatalogItem, clientX: number, clientY: number) => {
    if (!canAddActivities) return false;
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

    if (isGraphMode) {
      const edgeId = findEdgeUnderCursor(clientX, clientY);
      const edge = edgeId ? edges.find(candidate => candidate.id === edgeId) : undefined;
      if (edge) {
        spliceActivityIntoEdge(activity, edge, position);
        return true;
      }
    }

    placeActivity(activity, position);
    return true;
  }, [canAddActivities, edges, findEdgeUnderCursor, isGraphMode, placeActivity, spliceActivityIntoEdge, toCanvasPosition]);

  // Pointer-based palette drag. Runs alongside the native HTML5 drag so a palette press that never
  // produces a dragstart (touch, or a browser that suppresses it) still lands an activity.
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
  }, [tryAddActivityAtClientPoint]);

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
    if (!canAddActivities) return;
    const added = placeActivity(activity);
    if (added) queueCanvasNodeFocus(added.nodeId);
  };

  const onCanvasDragOver = (event: React.DragEvent) => {
    if (!canAddActivities) {
      event.dataTransfer.dropEffect = "none";
      return;
    }

    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
    if (!isGraphMode) return;

    setHighlightedEdgeId(findEdgeUnderCursor(event.clientX, event.clientY));
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
    if (!canAddActivities) return;

    const activity = catalogByVersion.get(activityVersionId);
    if (!activity) return;

    tryAddActivityAtClientPoint(activity, event.clientX, event.clientY);
  };

  // Opens the "pick any activity" menu for an empty canvas. Works on slot levels too (sequence bodies,
  // ForEach body, Switch cases): the `fromEmpty` branch of `onConnectMenuPick` commits through
  // `commitCanvas`, which writes into whatever slot the host's current scope resolves to. An optional
  // anchor lets the empty-slot picker open the menu next to its own button instead of the canvas centre.
  const openEmptyConnectMenu = (anchor?: { clientX: number; clientY: number }) => {
    if (!canAddActivities) return;

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
    const allowedChanges = isUnsupported ? changes.filter(change => change.type === "select") : changes;
    if (allowedChanges.length === 0) return;
    setNodes(current => applyNodeChanges(allowedChanges, current));
    const selectionChanges = allowedChanges.filter(change => change.type === "select");
    const selectedChange = selectionChanges.find(change => change.selected);
    if (selectedChange && selectedChange.id !== selectedNodeId) {
      select(selectedChange.id);
    } else if (!selectedChange && selectedNodeId && selectionChanges.some(change => change.id === selectedNodeId && !change.selected)) {
      select(null);
    }
  };

  const onEdgesChange = (changes: EdgeChange<WorkflowEdge>[]) => {
    if (isUnsupported) return;
    setEdges(current => applyEdgeChanges(changes, current) as WorkflowEdge[]);
  };

  const isValidConnection = (connection: Connection | Edge) => {
    if (!connection.source || !connection.target) return false;
    if (connection.source === connection.target) return false;
    if (!isGraphMode) return false;
    return !connection.targetHandle;
  };

  const onConnect = (connection: Connection) => {
    if (!isGraphMode) return;
    if (!isValidConnection(connection)) return;

    const nextEdges = addEdge(
      edgeFactory(connection.source, connection.target, connection.sourceHandle ?? "Done", connection.targetHandle ?? undefined),
      edges
    ) as WorkflowEdge[];
    setEdges(nextEdges);
    commitCanvas(nodes, nextEdges);
  };

  /**
   * Persists node positions after a drag. React Flow settles the drag by calling `updateNodePositions`
   * and `onNodeDragStop` back to back in one synchronous block, so the `nodes` captured by this
   * closure has not re-rendered with the final position yet — the dragged nodes React Flow hands us
   * are the authority. Falls back to the mirror when invoked without them (plain layout commits).
   */
  const commitLayout: OnNodeDrag<Node<WorkflowNodeData>> = (_event, node, draggedNodes) => {
    const dragged = draggedNodes?.length ? draggedNodes : node ? [node] : [];
    if (dragged.length === 0) {
      commitCanvas(nodes, edges);
      return;
    }
    const positions = new Map(dragged.map(dragTarget => [dragTarget.id, dragTarget.position]));
    const nextNodes = nodes.map(current => {
      const position = positions.get(current.id);
      return position ? { ...current, position } : current;
    });
    setNodes(nextNodes);
    commitCanvas(nextNodes, edges);
  };

  const canAutoLayout = !isUnsupported && nodes.length > 0;
  const autoLayout = useCallback(() => {
    if (isUnsupported || nodes.length === 0) return;
    const positions = computeAutoLayout(nodes, edges, mode === "sequence" ? "sequence" : "flowchart");
    const nextNodes = nodes.map(node => {
      const position = positions.get(node.id);
      return position ? { ...node, position } : node;
    });
    setNodes(nextNodes);
    commitCanvas(nextNodes, edges);
    window.requestAnimationFrame(() => reactFlowInstance?.fitView({ padding: 0.2 }));
    onStatus?.("Rearranged the canvas.");
  }, [commitCanvas, edges, isUnsupported, mode, nodes, onStatus, reactFlowInstance, setNodes]);

  const onConnectStart: OnConnectStart = (_event, params) => {
    if (!params.nodeId || params.handleType === "target") {
      connectSourceRef.current = null;
      return;
    }

    connectSourceRef.current = { nodeId: params.nodeId, handleId: params.handleId ?? null };
  };

  const onConnectEnd: OnConnectEnd = (event, connectionState) => {
    const source = resolveConnectEndSource(connectSourceRef.current, connectionState);
    connectSourceRef.current = null;
    if (!source || !canCreateActivityFromPort) return;
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
    if (!isGraphMode) return;
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
    if (isUnsupported) return;
    if (deletedNodes.length === 0) return;
    const deletedIds = new Set(deletedNodes.map(node => node.id));
    // Always includes the canvas node ids themselves: per-node side tables (layout above all) are keyed
    // by canvas node id, which in a BPMN scope is the element id rather than the resolved activity id.
    const removedNodeIds = new Set([...deletedIds, ...(resolveRemovedActivityNodeIds?.(deletedNodes) ?? [])]);
    const nextNodes = nodes.filter(node => !deletedIds.has(node.id));
    const nextEdges = edges.filter(edge => !deletedIds.has(edge.source) && !deletedIds.has(edge.target));
    setNodes(nextNodes);
    setEdges(nextEdges);
    if (selectedNodeId && deletedIds.has(selectedNodeId)) {
      const deletedIndex = nodes.findIndex(node => node.id === selectedNodeId);
      const nextFocus = nextNodes[Math.min(Math.max(deletedIndex, 0), nextNodes.length - 1)];
      select(nextFocus?.id ?? null);
      if (nextFocus) queueCanvasNodeFocus(nextFocus.id);
    }
    commitCanvas(nextNodes, nextEdges, { removedNodeIds });
  };

  const onEdgesDelete = (deletedEdges: WorkflowEdge[]) => {
    if (isUnsupported) return;
    if (deletedEdges.length === 0) return;
    const deletedIds = new Set(deletedEdges.map(edge => edge.id));
    const nextEdges = edges.filter(edge => !deletedIds.has(edge.id));
    setEdges(nextEdges);
    commitCanvas(nodes, nextEdges);
    const nextFocus = deletedEdges[0]?.source;
    if (nextFocus) {
      select(nextFocus);
      queueCanvasNodeFocus(nextFocus);
    }
  };

  const deleteEdge = useCallback((edgeId: string) => {
    if (isUnsupported) return;
    const deletedEdge = edges.find(edge => edge.id === edgeId);
    const nextEdges = edges.filter(edge => edge.id !== edgeId);
    setEdges(nextEdges);
    commitCanvas(nodes, nextEdges);
    if (deletedEdge) {
      select(deletedEdge.source);
      queueCanvasNodeFocus(deletedEdge.source);
    }
  }, [commitCanvas, edges, isUnsupported, nodes, queueCanvasNodeFocus, select, setEdges]);

  const requestInsertActivity = useCallback((edgeId: string, clientX: number, clientY: number) => {
    if (!isGraphMode) return;
    setConnectMenu({ kind: "spliceEdge", edgeId, clientX, clientY });
  }, [isGraphMode]);

  const onConnectMenuPick = (activity: ActivityCatalogItem) => {
    const menu = connectMenu;
    if (!menu) return;
    setConnectMenu(null);

    const fallbackPosition = toCanvasPosition(menu.clientX, menu.clientY) ?? { x: 0, y: 0 };

    if (menu.kind === "fromEmpty") {
      const placement = placementFactory(activity, fallbackPosition);
      if (!placement) return;
      placeOnCanvas(placement);
      onActivityPlaced?.(activity);
      return;
    }

    if (menu.kind === "fromPort") {
      const sourceNode = nodes.find(node => node.id === menu.sourceNodeId);
      const position = sourceNode ? rightOf(sourceNode) : fallbackPosition;
      const placement = placementFactory(activity, position);
      if (!placement) return;

      placeOnCanvas(placement, mode === "sequence"
        // A sequence has no authored connections: reorder the nodes and rebuild the implicit chain.
        ? {
            order: cleared => insertSequenceNodeAfter(cleared, menu.sourceNodeId, placement.node),
            wire: nextNodes => buildSequenceEdges(nextNodes) as WorkflowEdge[]
          }
        : {
            wire: () => [...edges, edgeFactory(menu.sourceNodeId, placement.node.id, menu.sourceHandleId ?? "Done")]
          });
      onActivityPlaced?.(activity);
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

  const accessibleCanvas = useMemo(() => decorateWorkflowCanvasElements(nodes, edges), [edges, nodes]);

  return {
    accessibleNodes: accessibleCanvas.nodes,
    accessibleEdges: accessibleCanvas.edges,
    canvasRef,
    reactFlowInstance,
    setReactFlowInstance,
    canCreateActivityFromPort,
    connectMenu,
    setConnectMenu,
    edgeActions,
    queueCanvasNodeFocus,
    stampCanvasNode,
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
