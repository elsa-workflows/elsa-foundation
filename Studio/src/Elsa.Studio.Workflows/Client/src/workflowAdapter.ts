import type { Edge, Node } from "@xyflow/react";
import type { ActivityCatalogItem, ActivityNode, ActivityNodeStructure, DesignMetadataRecord } from "./workflowTypes";

export const sequenceStructureKind = "elsa.sequence.structure";
export const flowchartStructureKind = "elsa.flowchart.structure";

export interface WorkflowNodeData extends Record<string, unknown> {
  label: string;
  activityVersionId: string;
  activityTypeKey?: string;
  childSlots: ChildSlot[];
}

export interface ChildSlot {
  id: string;
  label: string;
  property: string;
  mode: "sequence" | "flowchart" | "generic";
  activities: ActivityNode[];
}

export interface CanvasScope {
  owner: ActivityNode;
  slot: ChildSlot;
}

export interface ScopeFrame {
  ownerNodeId: string;
  slotId: string;
  label: string;
}

export function resolveScope(root: ActivityNode | null | undefined, frames: ScopeFrame[]): CanvasScope | null {
  if (!root) return null;

  let owner = root;
  let slot = getChildSlots(owner)[0];
  if (!slot) return null;

  for (const frame of frames) {
    const frameSlot = getChildSlots(owner).find(candidate => candidate.id === frame.slotId);
    if (!frameSlot) return null;
    const nextOwner = frameSlot.activities.find(activity => activity.nodeId === frame.ownerNodeId);
    if (!nextOwner) return null;
    owner = nextOwner;
    slot = getChildSlots(owner)[0];
    if (!slot) return null;
  }

  return { owner, slot };
}

export function getChildSlots(activity: ActivityNode): ChildSlot[] {
  const structure = activity.structure;
  if (!structure || !structure.payload || typeof structure.payload !== "object") return [];

  const payload = structure.payload;
  const mode = getStructureMode(structure);
  const activities = readActivityArray(payload.activities);
  if (activities) {
    return [{
      id: `${structure.kind}:activities`,
      label: getDefaultSlotLabel(structure),
      property: "activities",
      mode,
      activities
    }];
  }

  return Object.entries(payload)
    .filter(([, value]) => readActivityArray(value))
    .map(([property, value]) => ({
      id: `${structure.kind}:${property}`,
      label: toDisplayLabel(property),
      property,
      mode: "generic",
      activities: readActivityArray(value) ?? []
    }));
}

export function buildCanvas(scope: CanvasScope, catalog: ActivityCatalogItem[], layout: DesignMetadataRecord[]) {
  const catalogByVersion = new Map(catalog.map(activity => [activity.activityVersionId, activity]));
  const layoutByNodeId = new Map(layout.map(record => [record.nodeId, record]));
  const nodes: Node<WorkflowNodeData>[] = scope.slot.activities.map((activity, index) => {
    const catalogItem = catalogByVersion.get(activity.activityVersionId);
    const position = layoutByNodeId.get(activity.nodeId) ?? defaultPosition(scope.slot.mode, index);
    return {
      id: activity.nodeId,
      type: "workflowActivity",
      position: { x: position.x, y: position.y },
      data: {
        label: catalogItem?.displayName ?? activity.activityVersionId,
        activityVersionId: activity.activityVersionId,
        activityTypeKey: catalogItem?.activityTypeKey,
        childSlots: getChildSlots(activity)
      }
    };
  });

  return {
    nodes,
    edges: scope.slot.mode === "flowchart" ? flowchartEdges(scope.owner) : buildEdges(scope.slot, nodes)
  };
}

export function updateScopeActivities(root: ActivityNode, frames: ScopeFrame[], activities: ActivityNode[]): ActivityNode {
  if (frames.length === 0) {
    const slot = getChildSlots(root)[0];
    return slot ? replaceSlotActivities(root, slot, activities) : root;
  }

  const [frame, ...rest] = frames;
  const slot = getChildSlots(root).find(candidate => candidate.id === frame.slotId);
  if (!slot) return root;

  const updatedActivities = slot.activities.map(activity =>
    activity.nodeId === frame.ownerNodeId ? updateScopeActivities(activity, rest, activities) : activity);

  return replaceSlotActivities(root, slot, updatedActivities);
}

export function updateScopeOwner(root: ActivityNode, frames: ScopeFrame[], owner: ActivityNode): ActivityNode {
  if (frames.length === 0) return owner;

  const [frame, ...rest] = frames;
  const slot = getChildSlots(root).find(candidate => candidate.id === frame.slotId);
  if (!slot) return root;

  const updatedActivities = slot.activities.map(activity =>
    activity.nodeId === frame.ownerNodeId ? updateScopeOwner(activity, rest, owner) : activity);

  return replaceSlotActivities(root, slot, updatedActivities);
}

export function replaceSlotActivities(activity: ActivityNode, slot: ChildSlot, activities: ActivityNode[]): ActivityNode {
  if (!activity.structure) return activity;

  return {
    ...activity,
    structure: {
      ...activity.structure,
      payload: {
        ...activity.structure.payload,
        [slot.property]: activities
      }
    }
  };
}

export function syncCanvasToScope(scope: CanvasScope, nodes: Node<WorkflowNodeData>[], edges: Edge[]) {
  const existing = new Map(scope.slot.activities.map(activity => [activity.nodeId, activity]));
  const activities = nodes.map(node => existing.get(node.id)).filter((activity): activity is ActivityNode => !!activity);

  if (scope.slot.mode === "sequence") {
    activities.sort((left, right) => {
      const leftNode = nodes.find(node => node.id === left.nodeId);
      const rightNode = nodes.find(node => node.id === right.nodeId);
      return (leftNode?.position.x ?? 0) - (rightNode?.position.x ?? 0);
    });
  }

  return replaceSlotActivities(scope.owner, scope.slot, activities);
}

export function withFlowchartConnections(owner: ActivityNode, edges: Edge[]) {
  return {
    ...owner,
    structure: updateFlowchartConnections(owner.structure, edges)
  };
}

export function updateLayout(layout: DesignMetadataRecord[], nodes: Node[]) {
  const byNodeId = new Map(layout.map(record => [record.nodeId, record]));
  for (const node of nodes) {
    byNodeId.set(node.id, {
      ...(byNodeId.get(node.id) ?? { nodeId: node.id }),
      nodeId: node.id,
      x: Math.round(node.position.x),
      y: Math.round(node.position.y)
    });
  }
  return [...byNodeId.values()];
}

export function createActivityNode(activity: ActivityCatalogItem, nodeId: string): ActivityNode {
  return {
    nodeId,
    activityVersionId: activity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: createStructureForActivity(activity)
  };
}

export function getActivityDisplay(activity: ActivityCatalogItem) {
  return activity.displayName || activity.activityTypeKey.split(".").at(-1) || activity.activityTypeKey;
}

function createStructureForActivity(activity: ActivityCatalogItem): ActivityNodeStructure | null {
  if (activity.activityTypeKey.endsWith(".Sequence") || activity.displayName === "Sequence") {
    return {
      kind: sequenceStructureKind,
      schemaVersion: "1.0.0",
      payload: { activities: [] }
    };
  }

  if (activity.activityTypeKey.endsWith(".Flowchart") || activity.displayName === "Flowchart") {
    return {
      kind: flowchartStructureKind,
      schemaVersion: "1.0.0",
      payload: {
        activities: [],
        connections: [],
        startNodeId: null,
        nodeMetadata: {},
        connectionMetadata: {}
      }
    };
  }

  return null;
}

function updateFlowchartConnections(structure: ActivityNodeStructure | null | undefined, edges: Edge[]) {
  if (!structure) return structure ?? null;

  return {
    ...structure,
    payload: {
      ...structure.payload,
      connections: edges.map(edge => ({
        id: edge.id,
        source: { nodeId: edge.source, port: edge.sourceHandle ?? "Done" },
        target: { nodeId: edge.target, port: edge.targetHandle ?? "Done" }
      }))
    }
  };
}

function buildEdges(slot: ChildSlot, nodes: Node<WorkflowNodeData>[]): Edge[] {
  if (slot.mode === "sequence") {
    return nodes.slice(0, -1).map((node, index) => ({
      id: `sequence-${node.id}-${nodes[index + 1].id}`,
      source: node.id,
      target: nodes[index + 1].id,
      type: "smoothstep",
      animated: false
    }));
  }

  return [];
}

export function flowchartEdges(owner: ActivityNode): Edge[] {
  if (owner.structure?.kind !== flowchartStructureKind) return [];
  const connections = owner.structure.payload.connections;
  if (!Array.isArray(connections)) return [];

  return connections
    .map((connection, index) => {
      if (!connection || typeof connection !== "object") return null;
      const source = (connection as { source?: { nodeId?: string; port?: string } }).source;
      const target = (connection as { target?: { nodeId?: string; port?: string } }).target;
      if (!source?.nodeId || !target?.nodeId) return null;
      return {
        id: typeof (connection as { id?: unknown }).id === "string" ? String((connection as { id?: unknown }).id) : `flow-${index}-${source.nodeId}-${target.nodeId}`,
        source: source.nodeId,
        target: target.nodeId,
        sourceHandle: source.port,
        targetHandle: target.port,
        type: "smoothstep",
        label: source.port && source.port !== "Done" ? source.port : undefined
      } satisfies Edge;
    })
    .filter((edge): edge is Edge => !!edge);
}

function readActivityArray(value: unknown): ActivityNode[] | null {
  if (!Array.isArray(value)) return null;
  return value.filter(isActivityNode);
}

function isActivityNode(value: unknown): value is ActivityNode {
  return typeof value === "object" &&
    value !== null &&
    typeof (value as ActivityNode).nodeId === "string" &&
    typeof (value as ActivityNode).activityVersionId === "string";
}

function getStructureMode(structure: ActivityNodeStructure): ChildSlot["mode"] {
  if (structure.kind === sequenceStructureKind) return "sequence";
  if (structure.kind === flowchartStructureKind) return "flowchart";
  return "generic";
}

function getDefaultSlotLabel(structure: ActivityNodeStructure) {
  if (structure.kind === sequenceStructureKind) return "Activities";
  if (structure.kind === flowchartStructureKind) return "Activities";
  return "Activities";
}

function defaultPosition(mode: ChildSlot["mode"], index: number) {
  if (mode === "sequence") return { nodeId: "", x: index * 280, y: 0 };
  return { nodeId: "", x: (index % 4) * 280, y: Math.floor(index / 4) * 150 };
}

function toDisplayLabel(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/[-_]/g, " ")
    .replace(/^\w/, char => char.toUpperCase());
}
