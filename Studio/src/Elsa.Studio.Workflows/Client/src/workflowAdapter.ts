import type { Edge, Node, XYPosition } from "@xyflow/react";
import type { ActivityCatalogItem, ActivityExecutionStateSummary, ActivityNode, ActivityNodeStructure, DesignMetadataRecord, IncidentStateSummary } from "./workflowTypes";

export const sequenceStructureKind = "elsa.sequence.structure";
export const flowchartStructureKind = "elsa.flowchart.structure";

export interface WorkflowNodeData extends Record<string, unknown> {
  label: string;
  activityVersionId: string;
  activityTypeKey?: string;
  category?: string;
  executionType?: string;
  icon?: WorkflowNodeIcon;
  childSlots: ChildSlot[];
  acceptsInbound: boolean;
  sourcePorts: WorkflowPortDescriptor[];
  suppressFlowPorts?: boolean;
  runtime?: WorkflowRuntimeNodeOverlay;
}

export type WorkflowNodeIcon = "activity" | "flowchart" | "sequence" | "terminal" | "runtime" | "trigger";

export interface WorkflowPortDescriptor {
  name: string;
  displayName: string;
}

export interface WorkflowEdgeData extends Record<string, unknown> {
  vertices?: XYPosition[];
}

export interface WorkflowRuntimeNodeOverlay {
  status?: string;
  subStatus?: string | null;
  activityExecutionId?: string;
  faultCount: number;
  incidentCount: number;
  hasBlockingIncident: boolean;
  selected: boolean;
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

export type WorkflowDesignerSupport = "flowchart" | "sequence" | "unsupported";

export function resolveScopeOwner(root: ActivityNode | null | undefined, frames: ScopeFrame[]): ActivityNode | null {
  if (!root) return null;

  let owner = root;
  for (const frame of frames) {
    const slot = getChildSlots(owner).find(candidate => candidate.id === frame.slotId);
    if (!slot) return null;
    const nextOwner = slot.activities.find(activity => activity.nodeId === frame.ownerNodeId);
    if (!nextOwner) return null;
    owner = nextOwner;
  }

  return owner;
}

// Finds the scope frames that bring `nodeId` into view (i.e. into the resolved scope's primary slot),
// so a diagnostic can navigate the designer to a node anywhere in the tree. `labelFor` supplies the
// breadcrumb label for each descended container. Returns null when the node is not reachable through
// primary slots (matching how the canvas surfaces nodes via resolveScope).
export function findNodeScopePath(
  root: ActivityNode | null | undefined,
  nodeId: string,
  labelFor: (activity: ActivityNode) => string = activity => activity.nodeId
): ScopeFrame[] | null {
  if (!root) return null;
  // The root scope itself is reached with no frames.
  if (root.nodeId === nodeId) return [];

  const visit = (owner: ActivityNode, frames: ScopeFrame[]): ScopeFrame[] | null => {
    const slots = getChildSlots(owner);
    // A node is "landable" only when it sits in its parent's primary slot (matching how resolveScope
    // surfaces nodes on the canvas); descent still walks every slot to reach deeper primary slots.
    if (slots[0]?.activities.some(activity => activity.nodeId === nodeId)) return frames;

    for (const slot of slots) {
      for (const activity of slot.activities) {
        const found = visit(activity, [...frames, { ownerNodeId: activity.nodeId, slotId: slot.id, label: labelFor(activity) }]);
        if (found) return found;
      }
    }

    return null;
  };

  return visit(root, []);
}

export function resolveScope(root: ActivityNode | null | undefined, frames: ScopeFrame[]): CanvasScope | null {
  const owner = resolveScopeOwner(root, frames);
  if (!owner) return null;

  const slot = getChildSlots(owner)[0];
  if (!slot) return null;

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
    return createWorkflowNode(activity, catalogItem, { x: position.x, y: position.y });
  });

  return {
    nodes,
    edges: scope.slot.mode === "flowchart" ? flowchartEdges(scope.owner) : buildEdges(scope.slot, nodes)
  };
}

export function buildUnsupportedActivityCanvas(activity: ActivityNode, catalog: ActivityCatalogItem[], layout: DesignMetadataRecord[]) {
  const catalogItem = catalog.find(candidate => candidate.activityVersionId === activity.activityVersionId);
  const position = layout.find(record => record.nodeId === activity.nodeId) ?? { x: 0, y: 0 };

  return {
    nodes: [createWorkflowNode(activity, catalogItem, { x: position.x, y: position.y }, {
      connectable: false,
      deletable: false,
      draggable: false,
      suppressFlowPorts: true
    })],
    edges: [] satisfies Edge[]
  };
}

export function applyRuntimeOverlays(
  nodes: Node<WorkflowNodeData>[],
  activities: ActivityExecutionStateSummary[],
  incidents: IncidentStateSummary[],
  selectedEvidenceId: string | null = null
) {
  const activityByExecutionId = new Map(activities.map(activity => [activity.activityExecutionId, activity]));
  const activitiesByNodeId = groupBy(activities, activity => activity.authoredActivityId || activity.executableNodeId);
  const incidentsByNodeId = groupBy(incidents, incident => {
    if (incident.executableNodeId) return incident.executableNodeId;
    return incident.activityExecutionId ? activityByExecutionId.get(incident.activityExecutionId)?.authoredActivityId ?? "" : "";
  });

  return nodes.map(node => {
    const nodeActivities = activitiesByNodeId.get(node.id) ?? [];
    const nodeIncidents = incidentsByNodeId.get(node.id) ?? [];
    if (nodeActivities.length === 0 && nodeIncidents.length === 0) return node;

    const latestActivity = latestActivityExecution(nodeActivities);
    const selected = selectedEvidenceId === node.id ||
      nodeActivities.some(activity => activity.activityExecutionId === selectedEvidenceId) ||
      nodeIncidents.some(incident => incident.incidentId === selectedEvidenceId);
    const runtime: WorkflowRuntimeNodeOverlay = {
      status: latestActivity?.status,
      subStatus: latestActivity?.subStatus,
      activityExecutionId: latestActivity?.activityExecutionId,
      faultCount: nodeActivities.reduce((sum, activity) => sum + activity.faultCount + activity.aggregateFaultCount, 0),
      incidentCount: nodeIncidents.length,
      hasBlockingIncident: nodeIncidents.some(incident => incident.isBlocking),
      selected
    };

    return {
      ...node,
      selected,
      className: selected ? "wf-runtime-node-selected" : node.className,
      data: {
        ...node.data,
        runtime
      }
    };
  });
}

export function getActivityDesignerSupport(activity: ActivityNode | null | undefined, catalogItem?: ActivityCatalogItem): WorkflowDesignerSupport {
  if (activity?.structure?.kind === flowchartStructureKind || isFlowchartCatalogItem(catalogItem)) return "flowchart";
  if (activity?.structure?.kind === sequenceStructureKind || isSequenceCatalogItem(catalogItem)) return "sequence";
  return "unsupported";
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

export function updateActivity(root: ActivityNode, nodeId: string, update: (activity: ActivityNode) => ActivityNode): ActivityNode {
  if (root.nodeId === nodeId) return update(root);

  const slots = getChildSlots(root);
  if (slots.length === 0) return root;

  let changed = false;
  let next = root;
  for (const slot of slots) {
    const updatedActivities = slot.activities.map(activity => {
      const updated = updateActivity(activity, nodeId, update);
      if (updated !== activity) changed = true;
      return updated;
    });

    if (changed) next = replaceSlotActivities(next, slot, updatedActivities);
  }

  return changed ? next : root;
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

export function syncCanvasToScope(scope: CanvasScope, nodes: Node<WorkflowNodeData>[], edges: Edge[], additionalActivities: ActivityNode[] = []) {
  const existing = new Map(scope.slot.activities.map(activity => [activity.nodeId, activity]));
  for (const activity of additionalActivities) {
    existing.set(activity.nodeId, activity);
  }
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
  const shortName = activity.activityTypeKey.split(".").at(-1) || activity.activityTypeKey;
  const displayName = activity.displayName?.trim();
  if (!displayName || displayName === activity.activityTypeKey || displayName.includes(".")) {
    return humanizeActivityTypeName(shortName);
  }

  return displayName;
}

function createWorkflowNode(
  activity: ActivityNode,
  catalogItem: ActivityCatalogItem | undefined,
  position: XYPosition,
  options: { connectable?: boolean; deletable?: boolean; draggable?: boolean; suppressFlowPorts?: boolean } = {}
): Node<WorkflowNodeData> {
  return {
    id: activity.nodeId,
    type: "workflowActivity",
    position,
    connectable: options.connectable,
    deletable: options.deletable,
    draggable: options.draggable,
    data: {
      label: catalogItem ? getActivityDisplay(catalogItem) : activity.activityVersionId,
      activityVersionId: activity.activityVersionId,
      activityTypeKey: catalogItem?.activityTypeKey,
      category: catalogItem?.category,
      executionType: catalogItem?.executionType,
      icon: resolveActivityIcon(catalogItem),
      childSlots: getChildSlots(activity),
      acceptsInbound: activityAcceptsInbound(activity, catalogItem),
      sourcePorts: options.suppressFlowPorts ? [] : getActivitySourcePorts(activity, catalogItem),
      suppressFlowPorts: options.suppressFlowPorts
    }
  };
}

export function resolveActivityIcon(activity: ActivityCatalogItem | undefined): WorkflowNodeIcon {
  if (!activity) return "activity";

  const configuredIcon = normalizeWorkflowNodeIcon(activity.icon);
  if (configuredIcon) return configuredIcon;

  const typeKey = activity.activityTypeKey.toLowerCase();
  const displayName = getActivityDisplay(activity).toLowerCase();
  const category = activity.category?.toLowerCase() ?? "";
  const executionType = activity.executionType?.toLowerCase() ?? "";

  if (typeKey.endsWith(".flowchart") || displayName === "flowchart") return "flowchart";
  if (typeKey.endsWith(".sequence") || displayName === "sequence") return "sequence";
  if (typeKey.includes("writeline") || displayName.includes("write line")) return "terminal";
  if (category.includes("runtime")) return "runtime";
  if (executionType === "trigger") return "trigger";
  return "activity";
}

function normalizeWorkflowNodeIcon(icon: string | null | undefined): WorkflowNodeIcon | null {
  if (!icon) return null;

  const normalized = icon.trim().toLowerCase();
  if (["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(normalized)) {
    return normalized as WorkflowNodeIcon;
  }

  return null;
}

function isFlowchartCatalogItem(activity: ActivityCatalogItem | undefined) {
  return !!activity && (getActivityDisplay(activity) === "Flowchart" || activity.activityTypeKey.endsWith(".Flowchart"));
}

function isSequenceCatalogItem(activity: ActivityCatalogItem | undefined) {
  return !!activity && (getActivityDisplay(activity) === "Sequence" || activity.activityTypeKey.endsWith(".Sequence"));
}

function humanizeActivityTypeName(name: string) {
  return name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .trim();
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

function updateFlowchartConnections(structure: ActivityNodeStructure | null | undefined, edges: Edge<WorkflowEdgeData>[]) {
  if (!structure) return structure ?? null;
  const previousConnections = Array.isArray(structure.payload.connections) ? structure.payload.connections : [];
  const previousById = new Map<string, Record<string, unknown>>();

  for (const connection of previousConnections) {
    if (!isRecord(connection)) continue;
    const id = connection.id;
    if (typeof id === "string") previousById.set(id, connection);
  }

  return {
    ...structure,
    payload: {
      ...structure.payload,
      connections: edges.map(edge => {
        const previous = previousById.get(edge.id) ?? {};
        const vertices = edge.data?.vertices;
        const { vertices: _oldVertices, ...previousWithoutVertices } = previous;
        return {
          ...previousWithoutVertices,
          id: edge.id,
          source: { nodeId: edge.source, port: edge.sourceHandle ?? "Done" },
          target: edge.targetHandle ? { nodeId: edge.target, port: edge.targetHandle } : { nodeId: edge.target },
          ...(vertices?.length ? { vertices: vertices.map(vertex => ({ x: Math.round(vertex.x), y: Math.round(vertex.y) })) } : {})
        };
      })
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

export function flowchartEdges(owner: ActivityNode): Edge<WorkflowEdgeData>[] {
  if (owner.structure?.kind !== flowchartStructureKind) return [];
  const connections = owner.structure.payload.connections;
  if (!Array.isArray(connections)) return [];

  return connections
    .map((connection, index) => {
      if (!connection || typeof connection !== "object") return null;
      const source = (connection as { source?: { nodeId?: string; port?: string } }).source;
      const target = (connection as { target?: { nodeId?: string; port?: string } }).target;
      if (!source?.nodeId || !target?.nodeId) return null;
      const vertices = Array.isArray((connection as { vertices?: unknown }).vertices)
        ? (connection as { vertices: unknown[] }).vertices.filter(isPosition)
        : [];
      return {
        id: typeof (connection as { id?: unknown }).id === "string" ? String((connection as { id?: unknown }).id) : `flow-${index}-${source.nodeId}-${target.nodeId}`,
        source: source.nodeId,
        target: target.nodeId,
        sourceHandle: source.port,
        targetHandle: target.port && target.port !== "Done" ? target.port : undefined,
        type: "workflow",
        label: source.port && source.port !== "Done" ? source.port : undefined,
        data: vertices.length ? { vertices } : undefined
      } satisfies Edge<WorkflowEdgeData>;
    })
    .filter((edge): edge is Edge => !!edge);
}

export function getActivitySourcePorts(activity: ActivityNode, catalogItem?: ActivityCatalogItem): WorkflowPortDescriptor[] {
  const cases = readStringList((activity as { cases?: unknown }).cases);
  if (isFlowSwitch(activity, catalogItem) && cases.length > 0) {
    return [...cases.map(name => ({ name, displayName: name })), { name: "Default", displayName: "Default" }];
  }

  const descriptorPorts = [
    ...readFlowPorts(catalogItem?.designFacets),
    ...readFlowPorts((catalogItem as { ports?: unknown } | undefined)?.ports),
    ...readFlowPorts(catalogItem?.outputs)
  ];
  if (descriptorPorts.length > 0) return uniquePorts(descriptorPorts);

  const outcomes = readStringList((activity as { outcomes?: unknown }).outcomes);
  if (outcomes.length > 0) return outcomes.map(name => ({ name, displayName: name }));

  return [{ name: "Done", displayName: "Done" }];
}

export function activityAcceptsInbound(_activity: ActivityNode, catalogItem?: ActivityCatalogItem) {
  return String(catalogItem?.executionType ?? "").toLowerCase() !== "trigger";
}

export function createWorkflowEdge(source: string, target: string, sourceHandle?: string | null, targetHandle?: string | null): Edge<WorkflowEdgeData> {
  const port = sourceHandle ?? "Done";
  return {
    id: `flow-${source}-${target}-${port}-${crypto.randomUUID().slice(0, 8)}`,
    source,
    target,
    sourceHandle: port,
    targetHandle: targetHandle ?? undefined,
    type: "workflow",
    label: port !== "Done" ? port : undefined
  };
}

export function spliceWorkflowEdge(edges: Edge<WorkflowEdgeData>[], edge: Edge<WorkflowEdgeData>, newNodeId: string) {
  const first = createWorkflowEdge(edge.source, newNodeId, edge.sourceHandle ?? "Done", undefined);
  const second = createWorkflowEdge(newNodeId, edge.target, "Done", edge.targetHandle ?? undefined);
  return edges.filter(candidate => candidate.id !== edge.id).concat(first, second);
}

function readActivityArray(value: unknown): ActivityNode[] | null {
  if (!Array.isArray(value)) return null;
  return value.filter(isActivityNode);
}

function isFlowSwitch(activity: ActivityNode, catalogItem?: ActivityCatalogItem) {
  const key = catalogItem?.activityTypeKey ?? activity.activityVersionId;
  const name = catalogItem?.displayName ?? "";
  return key.endsWith(".FlowSwitch") || key === "FlowSwitch" || name === "FlowSwitch";
}

function readFlowPorts(value: unknown): WorkflowPortDescriptor[] {
  if (!Array.isArray(value)) return [];
  const ports: WorkflowPortDescriptor[] = [];

  for (const item of value) {
    if (!isRecord(item)) continue;
    if (Array.isArray(item.ports)) {
      ports.push(...readFlowPorts(item.ports));
      continue;
    }

    const type = typeof item.type === "string" ? item.type : typeof item.portType === "string" ? item.portType : "";
    const isBrowsable = item.isBrowsable !== false && item.browsable !== false;
    const name = typeof item.name === "string" ? item.name : typeof item.id === "string" ? item.id : "";
    if (isBrowsable && type.toLowerCase() === "flow" && name) {
      const displayName = typeof item.displayName === "string" ? item.displayName : name;
      ports.push({ name, displayName });
    }
  }

  return ports;
}

function uniquePorts(ports: WorkflowPortDescriptor[]) {
  const byName = new Map<string, WorkflowPortDescriptor>();
  for (const port of ports) {
    if (!byName.has(port.name)) byName.set(port.name, port);
  }
  return [...byName.values()];
}

function readStringList(value: unknown) {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string" && item.length > 0) : [];
}

function groupBy<T>(items: T[], getKey: (item: T) => string) {
  const groups = new Map<string, T[]>();
  for (const item of items) {
    const key = getKey(item);
    if (!key) continue;
    groups.set(key, [...(groups.get(key) ?? []), item]);
  }

  return groups;
}

function latestActivityExecution(activities: ActivityExecutionStateSummary[]) {
  return [...activities].sort((left, right) =>
    activityExecutionTime(right).localeCompare(activityExecutionTime(left)))[0];
}

function activityExecutionTime(activity: ActivityExecutionStateSummary) {
  return activity.completedAt ?? activity.startedAt ?? activity.scheduledAt;
}

function isPosition(value: unknown): value is XYPosition {
  return isRecord(value) && typeof value.x === "number" && typeof value.y === "number";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
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
