import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { type Edge, type Node, type XYPosition } from "@xyflow/react";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import { formatActivitySummary } from "../activitySummary";
import {
  buildCanvas,
  buildUnsupportedActivityCanvas,
  collectActivityNodeIds,
  createActivityNode,
  createWorkflowEdge,
  getActivityDisplay,
  resolveScope,
  updateLayout,
  updateScopeActivities,
  updateScopeOwner,
  syncCanvasToScope,
  withFlowchartConnections,
  type CanvasScope,
  type WorkflowNodeData
} from "../workflowAdapter";
import {
  buildBpmnCanvas,
  collectRemovedGraphNodeIds,
  createBpmnBoundNode,
  createBpmnFlowEdge,
  createBpmnShapeNode,
  syncBpmnCanvasToScope,
  type BpmnNodeData
} from "../bpmn/bpmnAdapter";
import type { BpmnShapeDescriptor } from "../bpmn/bpmnTypes";
import { removeActivityPresentation } from "../activityPresentation";
import { createNodeId } from "./editorHelpers";
import type { WorkflowEdge, WorkflowErrorInput } from "./editorTypes";
import type { WorkflowDraftRecipe } from "./workflowDocument";
import { planActivityDrop } from "./addActivityRouting";
import type { ScopeFrame } from "../workflowAdapter";
import { observeReusableActivity } from "../reusableActivityObservability";
import {
  useGraphCanvasInteractions,
  type GraphCanvasCommitOptions,
  type GraphCanvasMode,
  type GraphCanvasPlacement
} from "../graph-authoring/useGraphCanvasInteractions";

const rootScopeViewportKey = "root";

function getScopeViewportKey(frames: ScopeFrame[]) {
  if (frames.length === 0) return rootScopeViewportKey;
  return frames.map(frame => `${frame.ownerNodeId}:${frame.slotId}`).join("/");
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
  isBpmnDesigner: boolean;
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
  setError(value: WorkflowErrorInput): void;
}

// Owns the React Flow mirror (nodes/edges) of the current scope for a WorkflowDraft, plus everything
// about mutating it that is specific to that document: root routing (become/wrap/leaf-error), BPMN
// element binding, layout pinning, and activityPresentation cleanup.
//
// Every interaction that is NOT WorkflowDraft-specific — palette drag/drop, edge splicing, the connect
// menu, deletes, reconnects, auto-layout, viewport persistence — lives in useGraphCanvasInteractions
// and is shared with Activity Definition graph authoring.
export function useWorkflowCanvas({
  draft,
  scope,
  scopeOwner,
  frames,
  catalog,
  catalogByVersion,
  isUnsupportedDesigner,
  isBpmnDesigner,
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
  const scopeViewportKey = useMemo(() => getScopeViewportKey(frames), [frames]);
  const mode: GraphCanvasMode = isUnsupportedDesigner
    ? "unsupported"
    : scope?.slot.mode === "bpmn"
      ? "bpmn"
      : scope?.slot.mode === "flowchart"
        ? "flowchart"
        : scope?.slot.mode === "sequence"
          ? "sequence"
          : "none";

  useEffect(() => {
    if (!scopeOwner) {
      setNodes([]);
      setEdges([]);
      return;
    }

    const canvas = isUnsupportedDesigner
      ? buildUnsupportedActivityCanvas(
          scopeOwner,
          catalog,
          draft?.layout ?? [],
          formatActivitySummary,
          draft?.activityPresentation)
      : scope
        ? scope.slot.mode === "bpmn"
          ? buildBpmnCanvas(scope, catalog, draft?.layout ?? []) as unknown as { nodes: Node<WorkflowNodeData>[]; edges: Edge[] }
          : buildCanvas(
              scope,
              catalog,
              draft?.layout ?? [],
              formatActivitySummary,
              draft?.activityPresentation)
        : { nodes: [], edges: [] };
    setNodes(canvas.nodes.map(node => ({ ...node, selected: node.id === selectedNodeId })));
    setEdges(canvas.edges as WorkflowEdge[]);
    // `selectedNodeId` is read for the initial projection only; the shared interaction layer keeps it
    // in sync afterwards, and re-deriving the canvas on every selection would discard local edits.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catalog, draft?.activityPresentation, draft?.layout, isUnsupportedDesigner, scope, scopeOwner, scopeViewportKey]);

  // Pins a node's canvas position into the layout records (removing any prior record for that node so a
  // moved node doesn't accumulate duplicates). No-ops when the drop had no cursor position (palette click).
  const pinLayout = useCallback((layout: WorkflowDraft["layout"], nodeId: string, position?: XYPosition) =>
    position
      ? [
          ...layout.filter(record => record.nodeId !== nodeId),
          { nodeId, x: Math.round(position.x), y: Math.round(position.y) }
        ]
      : layout, []);

  const commitCanvas = useCallback((
    nextNodes: Node<WorkflowNodeData>[],
    nextEdges: WorkflowEdge[],
    options?: GraphCanvasCommitOptions
  ) => {
    if (isUnsupportedDesigner) return;

    editDraft(({ draft: current, frames: currentFrames }) => {
      if (!current) return null;

      const nextLayout = updateLayout(current.layout, nextNodes);
      const rootActivity = current.state.rootActivity;
      if (!rootActivity) return { ...current, layout: nextLayout };

      const currentScope = resolveScope(rootActivity, currentFrames, catalogByVersion);
      if (!currentScope) return { ...current, layout: nextLayout };

      const additionalActivities = options?.createdActivities ?? [];
      let nextOwner: ActivityNode;
      if (currentScope.slot.mode === "bpmn") {
        nextOwner = syncBpmnCanvasToScope(currentScope, nextNodes as unknown as Node<BpmnNodeData>[], nextEdges, additionalActivities);
      } else {
        const ownerWithActivities = syncCanvasToScope(currentScope, nextNodes, nextEdges, additionalActivities);
        nextOwner = currentScope.slot.mode === "flowchart"
          ? withFlowchartConnections(ownerWithActivities, nextEdges)
          : ownerWithActivities;
      }

      return {
        ...current,
        layout: nextLayout,
        activityPresentation: removeActivityPresentation(
          current.activityPresentation ?? [],
          options?.removedNodeIds ?? []),
        state: {
          ...current.state,
          rootActivity: updateScopeOwner(rootActivity, currentFrames, nextOwner, catalogByVersion)
        }
      };
    });
  }, [catalogByVersion, isUnsupportedDesigner, editDraft]);

  const observeReusablePlacement = useCallback((activity: ActivityCatalogItem) => {
    if (!activity.activityDefinitionVersionId) return;
    observeReusableActivity({ event: "placement", surface: "workflow-designer", outcome: "ready" });
  }, []);

  // In a BPMN scope a catalog placement creates a task/subProcess ELEMENT bound to a fresh ActivityNode:
  // the element becomes the canvas node (its id is the elementId) and the activity lands in the
  // Bpmn.Activities slot via the canvas sync.
  const createPlacement = useCallback((activity: ActivityCatalogItem, position: XYPosition): GraphCanvasPlacement | null => {
    if (!isBpmnDesigner) return null;
    const activityNode = createActivityNode(activity, createNodeId(activity));
    return {
      activityNode,
      node: createBpmnBoundNode(activity, activityNode, position) as unknown as Node<WorkflowNodeData>
    };
  }, [isBpmnDesigner]);

  // Focus is owned by the shared interaction layer, which is constructed below (it needs `addActivity`).
  // A ref breaks that cycle for the BPMN placement path.
  const queueCanvasNodeFocusRef = useRef<(nodeId: string) => void>(() => {});

  // BPMN placements cannot go through the document routing below: buildBpmnCanvas renders nodes from
  // the process ELEMENTS, so an activity added to the slot alone would be invisible and dropped by the
  // next syncBpmnCanvasToScope. Stamp the bound element onto the canvas and commit it instead.
  const addCatalogActivityToBpmn = useCallback((activity: ActivityCatalogItem, position?: XYPosition): ActivityNode | null => {
    const fallback: XYPosition = { x: 120 + (nodes.length % 5) * 220, y: 120 + Math.floor(nodes.length / 5) * 140 };
    const placement = createPlacement(activity, position ?? fallback);
    if (!placement) return null;
    const nextNodes = [...nodes.map(node => node.selected ? { ...node, selected: false } : node), placement.node];
    setNodes(nextNodes);
    select(placement.node.id);
    commitCanvas(nextNodes, edges, { createdActivities: [placement.activityNode] });
    queueCanvasNodeFocusRef.current(placement.node.id);
    return placement.activityNode;
  }, [commitCanvas, createPlacement, edges, nodes, select]);

  // Returns the created ActivityNode when it became the root or landed in the current scope's slot (so
  // callers can restore focus or chain navigation); null for wrapping and rejected/stale outcomes.
  const addActivity = useCallback((activity: ActivityCatalogItem, position?: XYPosition): ActivityNode | null => {
    if (draft?.state.rootActivity && isUnsupportedDesigner) {
      return null;
    }

    if (draft?.state.rootActivity && isBpmnDesigner) {
      return addCatalogActivityToBpmn(activity, position);
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
      observeReusablePlacement(activity);
      return next;
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
      observeReusablePlacement(activity);
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
      const displacedNodeIds = currentScope.slot.cardinality === "single"
        ? currentScope.slot.activities.flatMap(activity => [...collectActivityNodeIds(activity, catalogByVersion)])
        : [];

      return {
        ...current,
        activityPresentation: removeActivityPresentation(
          current.activityPresentation ?? [],
          displacedNodeIds),
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

    observeReusablePlacement(activity);
    return next;
  }, [addCatalogActivityToBpmn, catalogByVersion, draft?.state.rootActivity, frames, isBpmnDesigner, isUnsupportedDesigner, editDraftAndSelect, observeReusablePlacement, resetToRoot, pinLayout, setError, setStatus]);

  const resolveRemovedActivityNodeIds = useCallback((deletedNodes: Node<WorkflowNodeData>[]) =>
    collectRemovedGraphNodeIds(
      deletedNodes as unknown as Node<BpmnNodeData>[],
      scope?.slot.activities ?? [],
      catalogByVersion),
    [catalogByVersion, scope?.slot.activities]);

  const interactions = useGraphCanvasInteractions({
    nodes,
    edges,
    setNodes,
    setEdges,
    mode,
    scopeKey: scopeViewportKey,
    canAddActivities: canAddActivitiesToCanvas,
    selectedNodeId,
    catalogByVersion,
    select,
    commitCanvas,
    placeActivity: addActivity,
    createPlacement: isBpmnDesigner ? createPlacement : undefined,
    createEdge: isBpmnDesigner
      ? (source, target) => createBpmnFlowEdge(source, target) as WorkflowEdge
      : createWorkflowEdge,
    resolveRemovedActivityNodeIds,
    onStatus: setStatus,
    onActivityPlaced: observeReusablePlacement
  });
  queueCanvasNodeFocusRef.current = interactions.queueCanvasNodeFocus;

  // Stamps a pure BPMN shape (event/gateway/unbound task) from the shape palette onto the canvas.
  // Shapes carry no ActivityNode, so this is the one placement the shared layer cannot express.
  const addBpmnShape = useCallback((shape: BpmnShapeDescriptor, position?: XYPosition) => {
    if (!isBpmnDesigner) return;
    const fallback: XYPosition = { x: 120 + (nodes.length % 5) * 220, y: 120 + Math.floor(nodes.length / 5) * 140 };
    const placedNode = createBpmnShapeNode(shape, position ?? fallback) as unknown as Node<WorkflowNodeData>;
    const clearedNodes = nodes.map(node => node.selected ? { ...node, selected: false } : node);
    const nextNodes = [...clearedNodes, placedNode];
    setNodes(nextNodes);
    select(placedNode.id);
    commitCanvas(nextNodes, edges);
    queueCanvasNodeFocusRef.current(placedNode.id);
  }, [commitCanvas, edges, isBpmnDesigner, nodes, select]);

  const { accessibleNodes, accessibleEdges, ...rest } = interactions;

  return {
    ...rest,
    nodes: accessibleNodes,
    edges: accessibleEdges,
    addActivity,
    addBpmnShape
  };
}
