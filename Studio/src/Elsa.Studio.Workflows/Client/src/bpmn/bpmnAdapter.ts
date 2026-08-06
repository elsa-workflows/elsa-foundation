import type { Edge, Node, XYPosition } from "@xyflow/react";
import type { ActivityCatalogItem, ActivityNode, DesignMetadataRecord } from "../workflowTypes";
import { collectActivityNodeIds, getActivityDisplay, getChildSlots, readStructureDesignFacet, resolveActivityIcon, type ActivityCatalogLookup, type CanvasScope, type ChildSlot, type WorkflowNodeIcon } from "../workflowAdapter";
import {
  bpmnElementTypes,
  bpmnStructureKind,
  isActivityBearingElementType,
  type BpmnElement,
  type BpmnSequenceFlow,
  type BpmnShapeDescriptor
} from "./bpmnTypes";

// The React Flow node payload for a BPMN element. The canvas node id IS the elementId; when the
// element binds an Elsa child activity, `boundActivity` carries the display info and the underlying
// ActivityNode keeps living in the `Bpmn.Activities` slot (selection maps elementId → bound node).
export interface BpmnNodeData extends Record<string, unknown> {
  element: BpmnElement;
  label: string;
  boundActivity?: {
    nodeId: string;
    activityVersionId: string;
    activityTypeKey?: string;
    label: string;
    icon: WorkflowNodeIcon;
  };
  // The BOUND ACTIVITY's child slots, so a subprocess offers the same slot entry an ordinary node does
  // (empty for events, gateways, unbound tasks, and tasks bound to a leaf). Slot entry addresses
  // `boundActivity.nodeId`, never the element id — see NodeSlotBadges.
  childSlots: ChildSlot[];
}

export interface BpmnCanvas {
  nodes: Node<BpmnNodeData>[];
  edges: Edge[];
}

export function readBpmnElements(owner: ActivityNode): BpmnElement[] {
  if (owner.structure?.kind !== bpmnStructureKind) return [];
  const elements = owner.structure.payload.elements;
  return Array.isArray(elements) ? elements.filter(isBpmnElement) : [];
}

export function readBpmnSequenceFlows(owner: ActivityNode): BpmnSequenceFlow[] {
  if (owner.structure?.kind !== bpmnStructureKind) return [];
  const flows = owner.structure.payload.sequenceFlows;
  return Array.isArray(flows) ? flows.filter(isBpmnSequenceFlow) : [];
}

export function findBpmnElement(owner: ActivityNode | null | undefined, elementId: string | null | undefined): BpmnElement | null {
  if (!owner || !elementId) return null;
  return readBpmnElements(owner).find(element => element.elementId === elementId) ?? null;
}

export function buildBpmnCanvas(scope: CanvasScope, catalog: ActivityCatalogItem[], layout: DesignMetadataRecord[]): BpmnCanvas {
  const catalogByVersion = new Map(catalog.map(activity => [activity.activityVersionId, activity]));
  const activitiesByNodeId = new Map(scope.slot.activities.map(activity => [activity.nodeId, activity]));
  const layoutByNodeId = new Map(layout.map(record => [record.nodeId, record]));

  const nodes = readBpmnElements(scope.owner).map((element, index) => {
    const position = layoutByNodeId.get(element.elementId) ?? defaultBpmnPosition(index);
    return createBpmnNode(element, activitiesByNodeId, catalogByVersion, { x: position.x, y: position.y });
  });

  return { nodes, edges: bpmnEdges(scope.owner) };
}

export function bpmnEdges(owner: ActivityNode): Edge[] {
  return readBpmnSequenceFlows(owner).map(flow => ({
    id: flow.flowId,
    source: flow.sourceRef,
    target: flow.targetRef,
    type: "workflow",
    label: flow.conditionOutcome ?? (flow.isDefault ? "default" : undefined),
    data: {
      conditionOutcome: flow.conditionOutcome ?? undefined,
      isDefault: flow.isDefault === true || undefined
    }
  } satisfies Edge));
}

function createBpmnNode(
  element: BpmnElement,
  activitiesByNodeId: Map<string, ActivityNode>,
  catalogByVersion: Map<string, ActivityCatalogItem>,
  position: XYPosition
): Node<BpmnNodeData> {
  const boundActivityNode = element.childNodeId ? activitiesByNodeId.get(element.childNodeId) : undefined;
  const catalogItem = boundActivityNode ? catalogByVersion.get(boundActivityNode.activityVersionId) : undefined;
  const boundActivity = boundActivityNode
    ? {
        nodeId: boundActivityNode.nodeId,
        activityVersionId: boundActivityNode.activityVersionId,
        activityTypeKey: catalogItem?.activityTypeKey,
        label: catalogItem ? getActivityDisplay(catalogItem) : boundActivityNode.activityVersionId,
        icon: resolveActivityIcon(catalogItem)
      }
    : undefined;

  return {
    id: element.elementId,
    type: "bpmnElement",
    position,
    data: {
      element,
      label: element.name?.trim() || boundActivity?.label || "",
      boundActivity,
      childSlots: boundActivityNode ? getChildSlots(boundActivityNode, catalogByVersion) : []
    }
  };
}

// Rebuilds the owner's structure payload from the canvas mirror: elements from the nodes (each node's
// `data.element` is the authored record — new nodes carry a freshly minted one), sequence flows from
// the edges (unknown authored props on an existing flow survive via merge), and the activities slot
// trimmed to the children still referenced by an element. The BPMN analog of
// syncCanvasToScope + withFlowchartConnections.
export function syncBpmnCanvasToScope(
  scope: CanvasScope,
  nodes: Node<BpmnNodeData>[],
  edges: Edge[],
  additionalActivities: ActivityNode[] = []
): ActivityNode {
  const owner = scope.owner;
  if (owner.structure?.kind !== bpmnStructureKind) return owner;

  const previousElementsById = new Map(readBpmnElements(owner).map(element => [element.elementId, element]));
  const previousFlowsById = new Map(readBpmnSequenceFlows(owner).map(flow => [flow.flowId, flow]));

  const retainedElements = nodes
    .map(node => previousElementsById.get(node.id) ?? node.data?.element)
    .filter(isBpmnElement);

  const elementIds = new Set(retainedElements.map(element => element.elementId));
  const sequenceFlows = edges
    .filter(edge => elementIds.has(edge.source) && elementIds.has(edge.target))
    .map(edge => {
      const previous = previousFlowsById.get(edge.id);
      const data = (edge.data ?? {}) as { conditionOutcome?: string; isDefault?: boolean };
      return {
        ...(previous ?? {}),
        flowId: edge.id,
        sourceRef: edge.source,
        targetRef: edge.target,
        conditionOutcome: previous?.conditionOutcome ?? data.conditionOutcome ?? null,
        isDefault: previous?.isDefault ?? data.isDefault ?? false
      } satisfies BpmnSequenceFlow;
    });

  // A retained element is carried forward verbatim, so a gateway whose default flow was just deleted
  // would keep pointing at a flowId that no longer exists. Drop the reference rather than persist a
  // process that names a sequence flow it no longer contains.
  const flowIds = new Set(sequenceFlows.map(flow => flow.flowId));
  const elements = retainedElements.map(element =>
    element.defaultFlowId && !flowIds.has(element.defaultFlowId)
      ? { ...element, defaultFlowId: null }
      : element);

  const referencedChildNodeIds = new Set(
    elements.map(element => element.childNodeId).filter((nodeId): nodeId is string => !!nodeId)
  );
  const existingActivities = new Map(scope.slot.activities.map(activity => [activity.nodeId, activity]));
  for (const activity of additionalActivities) existingActivities.set(activity.nodeId, activity);
  const activities = [...existingActivities.values()].filter(activity => referencedChildNodeIds.has(activity.nodeId));

  return {
    ...owner,
    structure: {
      ...owner.structure,
      payload: {
        ...owner.structure.payload,
        elements,
        sequenceFlows,
        activities
      }
    }
  };
}

export function createBpmnElementId(elementType: string) {
  return `${elementType}-${crypto.randomUUID().slice(0, 8)}`;
}

// Where the next shape or bound element lands when the author placed it from a palette rather than at a
// cursor position: a grid inset from the origin so it does not sit under the canvas toolbar.
export function nextBpmnPlacementPosition(placedCount: number): XYPosition {
  return { x: 120 + placedCount % 5 * 220, y: 120 + Math.floor(placedCount / 5) * 140 };
}

// Canvas node for a pure BPMN shape stamped from the shape palette (no bound activity).
export function createBpmnShapeNode(shape: BpmnShapeDescriptor, position: XYPosition): Node<BpmnNodeData> {
  const element: BpmnElement = {
    elementId: createBpmnElementId(shape.elementType),
    elementType: shape.elementType,
    ...(shape.eventDefinitions ? { eventDefinitions: shape.eventDefinitions } : {})
  };

  return {
    id: element.elementId,
    type: "bpmnElement",
    position,
    selected: true,
    data: { element, label: "", childSlots: [] }
  };
}

// Canvas node + ActivityNode pair for a catalog activity dropped into a BPMN scope: the activity
// becomes a slot child and the element binds it. Container activities land as subProcess elements,
// everything else as a task.
export function createBpmnBoundNode(
  catalogItem: ActivityCatalogItem,
  activityNode: ActivityNode,
  position: XYPosition
): Node<BpmnNodeData> {
  const elementType = isContainerCatalogItem(catalogItem) ? bpmnElementTypes.subProcess : bpmnElementTypes.task;
  const element: BpmnElement = {
    elementId: createBpmnElementId(elementType),
    elementType,
    childNodeId: activityNode.nodeId
  };

  return {
    id: element.elementId,
    type: "bpmnElement",
    position,
    selected: true,
    data: {
      element,
      label: getActivityDisplay(catalogItem),
      boundActivity: {
        nodeId: activityNode.nodeId,
        activityVersionId: activityNode.activityVersionId,
        activityTypeKey: catalogItem.activityTypeKey,
        label: getActivityDisplay(catalogItem),
        icon: resolveActivityIcon(catalogItem)
      },
      childSlots: getChildSlots(activityNode, catalogItem)
    }
  };
}

// Immutably applies `patch` to one sequence flow of a BPMN owner's payload (flow-condition edits).
export function updateBpmnFlow(owner: ActivityNode, flowId: string, patch: Partial<BpmnSequenceFlow>): ActivityNode {
  if (owner.structure?.kind !== bpmnStructureKind) return owner;
  const sequenceFlows = readBpmnSequenceFlows(owner).map(flow =>
    flow.flowId === flowId ? { ...flow, ...patch, flowId: flow.flowId, sourceRef: flow.sourceRef, targetRef: flow.targetRef } : flow);

  return {
    ...owner,
    structure: {
      ...owner.structure,
      payload: { ...owner.structure.payload, sequenceFlows }
    }
  };
}

// Marks `flowId` as the BPMN default flow of `sourceElementId` (clearing siblings), or clears the
// default entirely when `flowId` is null. A default flow carries no condition, so its
// conditionOutcome is dropped.
export function updateBpmnDefaultFlow(owner: ActivityNode, sourceElementId: string, flowId: string | null): ActivityNode {
  if (owner.structure?.kind !== bpmnStructureKind) return owner;
  const sequenceFlows = readBpmnSequenceFlows(owner).map(flow => {
    if (flow.sourceRef !== sourceElementId) return flow;
    if (flow.flowId === flowId) return { ...flow, isDefault: true, conditionOutcome: null };
    return flow.isDefault ? { ...flow, isDefault: false } : flow;
  });

  const elements = readBpmnElements(owner).map(element =>
    element.elementId === sourceElementId ? { ...element, defaultFlowId: flowId } : element);

  return {
    ...owner,
    structure: {
      ...owner.structure,
      payload: { ...owner.structure.payload, sequenceFlows, elements }
    }
  };
}

// Immutably applies `patch` to one element of a BPMN owner's payload (inspector edits).
export function updateBpmnElement(owner: ActivityNode, elementId: string, patch: Partial<BpmnElement>): ActivityNode {
  if (owner.structure?.kind !== bpmnStructureKind) return owner;
  const elements = readBpmnElements(owner).map(element =>
    element.elementId === elementId ? { ...element, ...patch, elementId: element.elementId } : element);

  return {
    ...owner,
    structure: {
      ...owner.structure,
      payload: { ...owner.structure.payload, elements }
    }
  };
}

export function createBpmnFlowEdge(source: string, target: string): Edge {
  return {
    id: `flow-${crypto.randomUUID().slice(0, 8)}`,
    source,
    target,
    type: "workflow"
  };
}

// Per-node document side tables — layout above all — are keyed by CANVAS node id. Inside a BPMN scope
// that key is the elementId, which collectActivityNodeIds never yields because it walks ActivityNodes.
// Deleting a bound subprocess would therefore strand a layout record for every element nested in it.
export function collectBpmnElementIds(
  activity: ActivityNode,
  catalog: ActivityCatalogLookup,
  result: Set<string> = new Set()
) {
  for (const element of readBpmnElements(activity)) result.add(element.elementId);
  for (const slot of getChildSlots(activity, catalog)) {
    for (const child of slot.activities) collectBpmnElementIds(child, catalog, result);
  }
  return result;
}

/**
 * Expands the canvas nodes a delete removed into every document node id that goes with them: the
 * activity an element binds, that activity's nested activities, and the BPMN element ids of any BPMN
 * scope in the subtree. Shared by the workflow designer and Activity Definition graph authoring, which
 * differ only in where their slot activities come from.
 */
export function collectRemovedGraphNodeIds(
  deleted: Node<BpmnNodeData>[],
  slotActivities: ActivityNode[],
  catalog: ActivityCatalogLookup
): Set<string> {
  return deleted.reduce((result, node) => {
    const activityNodeId = node.data?.boundActivity?.nodeId ?? node.id;
    const activity = slotActivities.find(candidate => candidate.nodeId === activityNodeId);
    if (!activity) return result.add(activityNodeId);
    collectActivityNodeIds(activity, catalog, result);
    collectBpmnElementIds(activity, catalog, result);
    return result;
  }, new Set<string>());
}

function isContainerCatalogItem(catalogItem: ActivityCatalogItem) {
  if (catalogItem.containerStructure) return true;
  return !!readStructureDesignFacet(catalogItem);
}

function defaultBpmnPosition(index: number): XYPosition {
  return { x: (index % 5) * 220, y: Math.floor(index / 5) * 140 };
}

function isBpmnElement(value: unknown): value is BpmnElement {
  return typeof value === "object" && value !== null
    && typeof (value as BpmnElement).elementId === "string"
    && typeof (value as BpmnElement).elementType === "string";
}

function isBpmnSequenceFlow(value: unknown): value is BpmnSequenceFlow {
  return typeof value === "object" && value !== null
    && typeof (value as BpmnSequenceFlow).flowId === "string"
    && typeof (value as BpmnSequenceFlow).sourceRef === "string"
    && typeof (value as BpmnSequenceFlow).targetRef === "string";
}
