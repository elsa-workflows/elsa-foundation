import type { Edge, Node, XYPosition } from "@xyflow/react";
import type { ActivityCatalogItem, ActivityNode, DesignMetadataRecord } from "../workflowTypes";
import { getActivityDisplay, readStructureDesignFacet, resolveActivityIcon, type CanvasScope, type WorkflowNodeIcon } from "../workflowAdapter";
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
      boundActivity
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

  const elements = nodes
    .map(node => previousElementsById.get(node.id) ?? node.data?.element)
    .filter(isBpmnElement);

  const elementIds = new Set(elements.map(element => element.elementId));
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
    data: { element, label: "" }
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
      }
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
