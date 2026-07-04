import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
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
import { AlertCircle, AlertTriangle, Boxes, Check, ChevronDown, ChevronLeft, ChevronRight, Code2, Download, GitBranch, GripVertical, ListTree, Maximize2, Minimize2, Network, Package, Play, Plus, Redo2, Save, Search, SlidersHorizontal, Sparkles, Undo2, Workflow as WorkflowIcon } from "lucide-react";
import type { StudioActivityDescriptor, StudioActivityPropertyEditorContribution, StudioAiContributionApi, StudioEndpointContext, StudioExpressionDescriptor, StudioExpressionEditorContribution, StudioWorkflowDesignerPanelContribution } from "@elsa-workflows/studio-sdk";
import {
  getDefinition,
  listActivities,
  listActivityAvailabilityDiagnostics,
  listActivityDescriptors,
  listExpressionDescriptors,
  fallbackExpressionDescriptors,
  promoteDraft,
  publishVersion,
  startWorkflowDraftTestRun,
  updateDefinitionMetadata,
  updateDraft,
  useScopedVariableAnalysis
} from "../api/workflows";
import type { ActivityAvailabilityDiagnostics, ActivityCatalogItem, ActivityNode, VariableDefinition, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { findActivityAvailabilityDiagnostic, getAvailabilityStateLabel } from "../activityAvailability";
import {
  buildCanvas,
  buildUnsupportedActivityCanvas,
  createActivityNode,
  createWorkflowEdge,
  findNodeScopePath,
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
  type WorkflowNodeData
} from "../workflowAdapter";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { buildExportPayload, downloadWorkflowJson, buildDraftFromJson } from "../workflowSerialization";
import { computeAutoLayout } from "../workflowLayout";
import { renderActivityIcon } from "../workflowFormatting";
import { WorkflowCodeView } from "../WorkflowCodeView";
import { ScopedVariablesEditor, WorkflowPropertiesView } from "../WorkflowPropertiesView";
import { readContainerVariables, shadowingWarningMap, supportsScopedVariables, writeContainerVariables } from "../scopedVariables";
import {
  applyWorkflowGraphOperationBatch,
  workflowGraphOperationApplyEvent,
  workflowGraphOperationUndoEvent,
  type WorkflowGraphOperationBatch
} from "../workflowGraphOperationBatch";
import { activityDragDataType, autosaveDelayMs, maxInspectorWidth, maxPaletteWidth, minInspectorWidth, minPaletteWidth, pointerDragThreshold } from "./constants";
import type { CanvasView, ConnectMenuState, WorkflowConnectSource, WorkflowDesignerPanelContext, WorkflowEdge, WorkflowEditorOperation, WorkflowEditorPanelTab, WorkflowNodeAvailabilityLookup, WorkflowTestRunState } from "./editorTypes";
import { WorkflowEdgeActionsContext, WorkflowNodeAvailabilityContext, type WorkflowEdgeActions } from "./contexts";
import {
  cloneWorkflowDraftForUndo,
  collectWorkflowContextActivities,
  collectWorkflowContextConnections,
  createDraftSnapshotId,
  createNodeId,
  clientPointFromEvent,
  dispatchAiAction,
  findAiAction,
  getDraftRevision,
  getDraftSignature,
  groupActivityPalette,
  indexActivityDescriptors,
  isConnectEndOverExistingWorkflowNode,
  isRejectedTestRun,
  midpointBetween,
  resolveActivityDescriptor,
  resolveConnectEndSource,
  rightOf
} from "./editorHelpers";
import { nodeTypes, edgeTypes, ConnectMenu } from "./graph";
import { PanelTabList, compareWorkflowPanelTabs } from "./PanelTabList";
import { ValidationPanel, TestRunStatus, WorkflowRuntimePanel } from "./editorPanels";
import { WorkflowArtifactsPanel } from "./WorkflowExecutables";
import { useSidePanelLayout } from "./useSidePanelLayout";
import { useDraftHistory } from "./useDraftHistory";

export function WorkflowEditor({
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
  const detailsRef = useRef<WorkflowDefinitionDetails | null>(null);
  const metaSaveTimeoutRef = useRef<number | null>(null);
  const pendingMetaRef = useRef<{ name?: string; description?: string }>({});
  useEffect(() => { detailsRef.current = details; }, [details]);
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
  const [activeLeftPanelId, setActiveLeftPanelId] = useState("activities");
  const [activeRightPanelId, setActiveRightPanelId] = useState("inspector");
  const [canvasView, setCanvasView] = useState<CanvasView>("designer");
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const connectSourceRef = useRef<WorkflowConnectSource | null>(null);
  const lastSavedDraftSignatureRef = useRef("");
  const saveRequestIdRef = useRef(0);
  const saveQueueRef = useRef<Promise<unknown>>(Promise.resolve());
  const workflowBatchUndoRef = useRef(new Map<string, WorkflowDraft>());
  const pointerDragRef = useRef<{
    activity: ActivityCatalogItem;
    startX: number;
    startY: number;
    dragging: boolean;
  } | null>(null);
  const nativePaletteDragRef = useRef<{ activityVersionId: string; handledDrop: boolean } | null>(null);
  const suppressPaletteClickRef = useRef(false);

  const {
    paletteWidth,
    inspectorWidth,
    paletteCollapsed,
    inspectorCollapsed,
    maximizedSidePanel,
    setInspectorCollapsed,
    paletteExpanded,
    inspectorExpanded,
    editorBodyClassName,
    editorBodyStyle,
    toggleSidePanelCollapsed,
    toggleSidePanelMaximized,
    startSidePanelResize,
    handleSidePanelResizeKeyDown
  } = useSidePanelLayout();

  const { resetHistory, undo, redo, canUndoNow, canRedoNow } = useDraftHistory({ draft, setDraft, setSelectedNodeId, setFrames });

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
  // Scope-aware variable visibility + shadowing for the selected activity (ADR-0027). Backed by a
  // pending design endpoint; degrades to status "unavailable" until it ships.
  const scopedVariableAnalysis = useScopedVariableAnalysis(context, draft?.state, selectedNodeId);
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
    resetHistory(nextDraft);
    setDraft(nextDraft);
    setCatalog(nextCatalog.activities ?? []);
    setActivityDescriptors(nextDescriptors.descriptors);
    setAvailabilityDiagnostics(nextAvailability);
    setExpressionDescriptors(nextExpressions.descriptors.length > 0 ? nextExpressions.descriptors : fallbackExpressionDescriptors);
    setDescriptorStatus(nextDescriptors.ok ? "ready" : "failed");
    setFrames([]);
    setSelectedNodeId(null);
  }, [context, definitionId, resetHistory]);

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

  const exportJson = useCallback(() => {
    if (!draft) return;
    const name = details?.definition.name;
    downloadWorkflowJson(buildExportPayload(draft, name), name);
    setStatus("Exported workflow as JSON.");
  }, [draft, details]);

  // Properties-tab edits (variables/inputs/outputs) flow through here. Routing them through
  // setDraft means autosave and undo/redo pick them up automatically — no separate save path.
  const updateDraftState = useCallback((producer: (state: WorkflowDraft["state"]) => WorkflowDraft["state"]) => {
    setDraft(current => current ? { ...current, state: producer(current.state) } : current);
  }, []);

  // Name/description live on the definition, not on draft state, so they persist through a dedicated
  // (debounced) metadata call rather than the draft autosave. The edit is reflected optimistically; a
  // failed save keeps the typed value and surfaces a status (the backend endpoint may not be live yet).
  // Sends any pending name/description edit now (used by the debounce timer AND on unmount, so an edit
  // isn't lost when the panel closes within the debounce window). Both fields are always sent — merging
  // the pending patch over the current definition — so a name-only edit can't drop the description
  // regardless of whether the backend does a partial or full-replace update.
  const flushDefinitionMeta = useCallback(() => {
    if (metaSaveTimeoutRef.current !== null) {
      window.clearTimeout(metaSaveTimeoutRef.current);
      metaSaveTimeoutRef.current = null;
    }
    const body = pendingMetaRef.current;
    pendingMetaRef.current = {};
    const definition = detailsRef.current?.definition;
    if (!definition || (body.name === undefined && body.description === undefined)) return;
    void updateDefinitionMetadata(context, definition.id, {
      name: body.name ?? definition.name,
      description: body.description ?? definition.description ?? null
    })
      // The endpoint returns the full details shape; take the refreshed summary and leave the live draft
      // (edited in the separate `draft` state) untouched.
      .then(saved => setDetails(prev => prev && prev.definition.id === saved.definition.id ? { ...prev, definition: saved.definition } : prev))
      .catch(() => setStatus("Couldn't save name/description."));
  }, [context]);

  const updateDefinitionMeta = useCallback((patch: { name?: string; description?: string }) => {
    setDetails(current => current ? { ...current, definition: { ...current.definition, ...patch } } : current);
    pendingMetaRef.current = { ...pendingMetaRef.current, ...patch };
    if (metaSaveTimeoutRef.current !== null) window.clearTimeout(metaSaveTimeoutRef.current);
    metaSaveTimeoutRef.current = window.setTimeout(flushDefinitionMeta, 800);
  }, [flushDefinitionMeta]);

  // Flush a pending name/description save on unmount (or when the target workflow changes) so a debounced
  // edit isn't discarded when the properties panel closes within the debounce window.
  useEffect(() => () => { flushDefinitionMeta(); }, [flushDefinitionMeta]);

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
    setStatus("Saving...");
    try {
      // Persist in-flight edits first: promotion snapshots the persisted draft, so without
      // this save the new version — and the post-promote load() — would revert to the last
      // stored state and lose unsaved changes.
      await saveDraft(draft, "Saved");
      setStatus("Promoting...");
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

  // Navigates the designer to the activity that owns an invalid scoped variable reference so the
  // author can deliberately re-pick a variable in its scope. We never auto-retarget (ADR-0027).
  const repairVariableReference = useCallback((nodeId: string | null) => {
    if (!nodeId) return;
    const currentRoot = draft?.state.rootActivity;
    if (!currentRoot) return;
    const path = findNodeScopePath(currentRoot, nodeId, activity => {
      const item = catalogByVersion.get(activity.activityVersionId);
      return item ? getActivityDisplay(item) : activity.nodeId;
    });
    if (!path) return;
    setCanvasView("designer");
    setFrames(path);
    setSelectedNodeId(nodeId);
    setInspectorCollapsed(false);
  }, [draft?.state.rootActivity, catalogByVersion, setInspectorCollapsed]);

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

  if (!details || !draft) {
    return <div className="wf-empty">{error || "Loading workflow editor..."}</div>;
  }

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
          visibleVariables={scopedVariableAnalysis.visibleVariables}
          scopeStatus={scopedVariableAnalysis.status}
          onChange={updateSelectedActivity}
        />
        {supportsScopedVariables(selectedNode) ? (
          <div className="wf-container-variables">
            <ScopedVariablesEditor
              context={context}
              variables={readContainerVariables(selectedNode)}
              title="Container variables"
              addLabel="Add container variable"
              emptyLabel="No container variables declared on this activity."
              warnings={shadowingWarningMap(scopedVariableAnalysis.shadowingWarnings, selectedNode.nodeId)}
              onChange={next => updateSelectedActivity(writeContainerVariables(selectedNode, next as VariableDefinition[]))}
            />
          </div>
        ) : null}
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
            <WorkflowPropertiesView details={details} draft={draft} context={context} onStateChange={updateDraftState} onDefinitionMetaChange={updateDefinitionMeta} />
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
          <ValidationPanel draft={draft} onRepair={repairVariableReference} />
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
