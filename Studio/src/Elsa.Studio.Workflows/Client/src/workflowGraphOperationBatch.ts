import type { ActivityCatalogItem, ActivityNode, DesignMetadataRecord, WorkflowDraft } from "./workflowTypes";
import { createActivityNode } from "./workflowAdapter";
import { camelize } from "./activityProperties";

export const workflowGraphOperationApplyEvent = "elsa-studio:apply-workflow-graph-operation-batch";
export const workflowGraphOperationUndoEvent = "elsa-studio:undo-workflow-graph-operation-batch";

export interface WorkflowGraphOperationBatch {
  schemaVersion: string;
  workflowDefinitionId: string;
  baseRevision?: string | null;
  operations: WorkflowGraphOperation[];
  metadata?: Record<string, unknown>;
}

export interface WorkflowGraphOperation {
  id: string;
  kind: string | number;
  parameters: Record<string, unknown>;
  temporaryReferences?: string[];
  summary?: string | null;
}

export interface WorkflowGraphOperationBatchApplyResult {
  draft: WorkflowDraft;
  appliedCount: number;
  finalActivityIds: string[];
  temporaryReferences: Record<string, string>;
  summary: string;
}

const operationKinds = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];

export function applyWorkflowGraphOperationBatch(
  draft: WorkflowDraft,
  batch: WorkflowGraphOperationBatch,
  catalog: ActivityCatalogItem[]
): WorkflowGraphOperationBatchApplyResult {
  if (!Array.isArray(batch.operations)) throw new Error("Weaver batch does not contain operations.");

  const nextDraft = clone(draft);
  const knownIds = collectActivityIds(nextDraft.state.rootActivity);
  const temporaryReferences = new Map<string, string>();
  const addedActivities = new Map<string, ActivityNode>();
  const finalActivityIds: string[] = [];

  for (const operation of batch.operations) {
    const kind = normalizeOperationKind(operation.kind);
    const parameters = operation.parameters ?? {};

    if (kind === "add-activity") {
      const requestedId = readString(parameters.activityId) ?? operation.temporaryReferences?.[0];
      const nodeId = createFinalNodeId(requestedId ?? readString(parameters.displayName) ?? readString(parameters.activityType) ?? "weaver-activity", knownIds);
      const activity = createActivity(operation, nodeId, catalog);
      addedActivities.set(nodeId, activity);
      finalActivityIds.push(nodeId);
      if (requestedId) temporaryReferences.set(requestedId, nodeId);
      if (nextDraft.state.rootActivity) appendActivityToRoot(nextDraft.state.rootActivity, activity);
      const position = isRecord(parameters.position) ? readPosition(parameters.position, { x: 280, y: 160 }) : null;
      if (position) nextDraft.layout = upsertLayout(nextDraft.layout, nodeId, position);
      continue;
    }

    if (kind === "set-root") {
      const activity = findOperationActivity(nextDraft, parameters.activityId, temporaryReferences, addedActivities);
      if (!activity) throw new Error("Weaver batch referenced an unknown root activity.");
      nextDraft.state.rootActivity = activity;
      continue;
    }

    if (kind === "set-designer-position") {
      const activityId = resolveActivityId(parameters.activityId, temporaryReferences);
      if (!activityId || !findActivity(nextDraft.state.rootActivity, activityId)) throw new Error("Weaver batch referenced an unknown activity position.");
      nextDraft.layout = upsertLayout(nextDraft.layout, activityId, readPosition(parameters, { x: 280, y: 160 }));
      continue;
    }

    if (kind === "set-activity-property") {
      const activity = findOperationActivity(nextDraft, parameters.activityId, temporaryReferences, addedActivities);
      if (!activity) throw new Error("Weaver batch referenced an unknown activity property target.");
      setActivityProperty(activity, readString(parameters.propertyName) ?? "Value", parameters.value ?? "");
      continue;
    }

    if (kind === "update-activity") {
      const activity = findOperationActivity(nextDraft, parameters.activityId, temporaryReferences, addedActivities);
      if (!activity) throw new Error("Weaver batch referenced an unknown activity update target.");
      const patch = isRecord(parameters.patch) ? parameters.patch : parameters;
      Object.assign(activity, patch);
      continue;
    }

    if (kind === "remove-activity") {
      const activityId = resolveActivityId(parameters.activityId, temporaryReferences);
      if (!activityId) throw new Error("Weaver batch referenced an unknown activity remove target.");
      nextDraft.state.rootActivity = removeActivity(nextDraft.state.rootActivity, activityId);
      nextDraft.layout = nextDraft.layout.filter(record => record.nodeId !== activityId);
      continue;
    }

    if (kind === "connect-activities") {
      connectActivities(nextDraft, parameters, temporaryReferences);
      continue;
    }

    if (kind === "disconnect-activities") {
      disconnectActivities(nextDraft, parameters, temporaryReferences);
      continue;
    }

    throw new Error(`Weaver batch operation '${String(operation.kind || "unknown")}' is not supported by this designer apply path.`);
  }

  if (!nextDraft.state.rootActivity) throw new Error("Weaver batch did not produce a root activity.");

  nextDraft.sourceVersionId = null;
  return {
    draft: nextDraft,
    appliedCount: batch.operations.length,
    finalActivityIds,
    temporaryReferences: Object.fromEntries(temporaryReferences),
    summary: `Applied ${batch.operations.length} workflow operation${batch.operations.length === 1 ? "" : "s"} to the working draft.`
  };
}

function createActivity(operation: WorkflowGraphOperation, nodeId: string, catalog: ActivityCatalogItem[]): ActivityNode {
  const parameters = operation.parameters ?? {};
  const activityVersionId = readString(parameters.activityVersionId) ?? readString(parameters.activityType) ?? "Elsa.Workflows.Activity";
  const catalogItem = catalog.find(activity =>
    activity.activityVersionId === activityVersionId ||
    activity.activityTypeKey === activityVersionId ||
    activity.displayName === readString(parameters.displayName));
  if (catalogItem) return createActivityNode(catalogItem, nodeId);

  return {
    nodeId,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId,
    inputs: [],
    outputs: [],
    ...(readString(parameters.displayName) ? { displayName: readString(parameters.displayName) } : {}),
    designer: { position: readPosition(parameters.position, { x: 280, y: 160 }) }
  };
}

function appendActivityToRoot(root: ActivityNode, activity: ActivityNode) {
  if (root.nodeId === activity.nodeId) return;
  const slot = getActivityArray(root);
  if (slot && !slot.some(item => item.nodeId === activity.nodeId)) {
    slot.push(activity);
  }
}

function connectActivities(draft: WorkflowDraft, parameters: Record<string, unknown>, references: Map<string, string>) {
  const root = draft.state.rootActivity;
  if (!root?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");

  const sourceId = resolveActivityId(parameters.sourceActivityId ?? parameters.sourceId ?? parameters.from, references);
  const targetId = resolveActivityId(parameters.targetActivityId ?? parameters.targetId ?? parameters.to, references);
  if (!sourceId || !targetId) throw new Error("Weaver batch connection is missing source or target activity.");

  const payload = root.structure.payload;
  const connections = Array.isArray(payload.connections) ? payload.connections : [];
  const id = readString(parameters.connectionId) ?? `flow-${sourceId}-${targetId}`;
  payload.connections = [
    ...connections.filter(connection => !isRecord(connection) || connection.id !== id),
    {
      id,
      source: { nodeId: sourceId, port: readString(parameters.outcome) ?? readString(parameters.sourcePort) ?? "Done" },
      target: { nodeId: targetId }
    }
  ];
}

function disconnectActivities(draft: WorkflowDraft, parameters: Record<string, unknown>, references: Map<string, string>) {
  const root = draft.state.rootActivity;
  const connections = root?.structure?.payload.connections;
  if (!Array.isArray(connections)) return;

  const connectionId = readString(parameters.connectionId);
  const sourceId = resolveActivityId(parameters.sourceActivityId ?? parameters.sourceId ?? parameters.from, references);
  const targetId = resolveActivityId(parameters.targetActivityId ?? parameters.targetId ?? parameters.to, references);
  root!.structure!.payload.connections = connections.filter(connection => {
    if (!isRecord(connection)) return true;
    if (connectionId && connection.id === connectionId) return false;
    const source = isRecord(connection.source) ? connection.source.nodeId : undefined;
    const target = isRecord(connection.target) ? connection.target.nodeId : undefined;
    return source !== sourceId || target !== targetId;
  });
}

function setActivityProperty(activity: ActivityNode, propertyName: string, value: unknown) {
  // A structured value (collection or object) must be authored as an Object expression so the backend
  // JSON-deserializes it into the target type; a scalar rides the Literal path. Same rule toWireArgument
  // in activityInputWire enforces at the wire boundary, applied here so the in-memory model is correct
  // at the source instead of relying on the boundary to retrofit it.
  const structured = isRecord(value);
  activity[camelize(propertyName)] = {
    typeName: typeof value === "string" ? "String" : "Object",
    expression: { type: structured ? "Object" : "Literal", value }
  };
}

function findOperationActivity(draft: WorkflowDraft, activityId: unknown, references: Map<string, string>, added: Map<string, ActivityNode>) {
  const id = resolveActivityId(activityId, references);
  return id ? findActivity(draft.state.rootActivity, id) ?? added.get(id) ?? null : null;
}

function resolveActivityId(value: unknown, references: Map<string, string>) {
  const id = readString(value);
  return id ? references.get(id) ?? id : null;
}

function findActivity(activity: ActivityNode | null | undefined, nodeId: string): ActivityNode | null {
  if (!activity) return null;
  if (activity.nodeId === nodeId) return activity;
  for (const child of getChildActivities(activity)) {
    const found = findActivity(child, nodeId);
    if (found) return found;
  }
  return null;
}

function removeActivity(activity: ActivityNode | null | undefined, nodeId: string): ActivityNode | null {
  if (!activity) return null;
  if (activity.nodeId === nodeId) return null;
  const children = getActivityArray(activity);
  if (children) {
    const nextChildren = children
      .map(child => removeActivity(child, nodeId))
      .filter((child): child is ActivityNode => !!child);
    children.splice(0, children.length, ...nextChildren);
  }
  return activity;
}

function collectActivityIds(activity: ActivityNode | null | undefined, ids = new Set<string>()) {
  if (!activity) return ids;
  ids.add(activity.nodeId);
  for (const child of getChildActivities(activity)) collectActivityIds(child, ids);
  return ids;
}

function getChildActivities(activity: ActivityNode) {
  return getActivityArray(activity) ?? [];
}

function getActivityArray(activity: ActivityNode) {
  const payload = activity.structure?.payload;
  return Array.isArray(payload?.activities) ? payload.activities as ActivityNode[] : null;
}

function upsertLayout(layout: DesignMetadataRecord[], nodeId: string, position: { x: number; y: number }) {
  return [
    ...layout.filter(record => record.nodeId !== nodeId),
    { nodeId, x: position.x, y: position.y }
  ];
}

function readPosition(value: unknown, fallback: { x: number; y: number }) {
  const record = isRecord(value) ? value : {};
  const x = Number(record.x);
  const y = Number(record.y);
  return {
    x: Number.isFinite(x) ? Math.max(40, Math.round(x)) : fallback.x,
    y: Number.isFinite(y) ? Math.max(40, Math.round(y)) : fallback.y
  };
}

function createFinalNodeId(value: string, knownIds: Set<string>) {
  const base = value
    .replace(/^temp:/, "")
    .replace(/[^a-zA-Z0-9_-]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .toLowerCase() || "weaver-activity";
  let next = base;
  let index = 2;
  while (knownIds.has(next)) {
    next = `${base}-${index}`;
    index += 1;
  }
  knownIds.add(next);
  return next;
}

function normalizeOperationKind(kind: string | number) {
  if (typeof kind === "number") return operationKinds[kind] ?? "";
  return kind.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

function readString(value: unknown) {
  return typeof value === "string" && value.trim() ? value : null;
}

function clone<T>(value: T): T {
  return typeof structuredClone === "function"
    ? structuredClone(value)
    : JSON.parse(JSON.stringify(value)) as T;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
