import {
  activityDisplayNameMaxLength,
  indexActivityPresentation,
  resolveActivityLabel
} from "../activityPresentation";
import { flowchartStructureKind } from "../flowchartStartNode";
import {
  collectActivityNodeIds,
  replaceSlotActivities,
  resolveScope,
  updateScopeOwner,
  type ActivityCatalogLookup,
  type ScopeFrame
} from "../workflowAdapter";
import type {
  ActivityNode,
  ActivityPresentationRecord,
  DesignMetadataRecord,
  WorkflowDraft
} from "../workflowTypes";

export const duplicatePositionOffset = { x: 40, y: 40 };

export interface ActivityClipboardSnapshot {
  activities: ActivityNode[];
  siblingConnections: Record<string, unknown>[];
  layout: DesignMetadataRecord[];
  presentation: ActivityPresentationRecord[];
}

export interface ActivityDuplicatePlan {
  activities: ActivityNode[];
  siblingConnections: Record<string, unknown>[];
  layout: DesignMetadataRecord[];
  presentation: ActivityPresentationRecord[];
  selectedNodeIds: string[];
  idMap: Map<string, string>;
}

export type ActivityClipboardShortcut = "copy" | "paste" | "duplicate";

export function matchActivityClipboardShortcut(event: {
  key: string;
  metaKey: boolean;
  ctrlKey: boolean;
  altKey: boolean;
  shiftKey: boolean;
}): ActivityClipboardShortcut | null {
  if (!(event.metaKey || event.ctrlKey) || event.altKey || event.shiftKey) return null;
  const key = event.key.toLowerCase();
  if (key === "c") return "copy";
  if (key === "v") return "paste";
  if (key === "d") return "duplicate";
  return null;
}

export function allocateActivityNodeId(
  prefix: string,
  usedIds: Set<string>,
  createSuffix: () => string = () => crypto.randomUUID().slice(0, 8)
) {
  const base = prefix.replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity";
  let next = `${base}-${createSuffix()}`;
  while (usedIds.has(next)) next = `${base}-${createSuffix()}`;
  usedIds.add(next);
  return next;
}

export function uniqueCopiedDisplayName(name: string, taken: Set<string>) {
  const base = name.trim() || "Activity";
  const assign = (candidate: string) => {
    const clipped = candidate.slice(0, activityDisplayNameMaxLength);
    taken.add(clipped.toLowerCase());
    return clipped;
  };
  const first = `${base} (copy)`;
  if (!taken.has(first.toLowerCase())) return assign(first);
  let index = 2;
  while (taken.has(`${base} (copy ${index})`.toLowerCase())) index += 1;
  return assign(`${base} (copy ${index})`);
}

export function snapshotSelectedActivities(options: {
  selectedNodeIds: string[];
  scopeActivities: ActivityNode[];
  scopeOwner: ActivityNode | null;
  catalog: ActivityCatalogLookup;
  layout: DesignMetadataRecord[];
  presentation: ActivityPresentationRecord[];
}): ActivityClipboardSnapshot | null {
  const selected = new Set(options.selectedNodeIds);
  const activities = options.scopeActivities.filter(activity => selected.has(activity.nodeId));
  if (activities.length === 0) return null;

  const copiedIds = new Set<string>();
  for (const activity of activities) {
    for (const nodeId of collectActivityNodeIds(activity, options.catalog)) copiedIds.add(nodeId);
  }

  return {
    activities: activities.map(activity => cloneValue(activity)),
    siblingConnections: readFlowchartConnections(options.scopeOwner)
      .filter(connection => {
        const source = readConnectionNodeId(connection, "source");
        const target = readConnectionNodeId(connection, "target");
        return !!source && !!target && selected.has(source) && selected.has(target);
      })
      .map(connection => cloneValue(connection)),
    layout: options.layout.filter(record => copiedIds.has(record.nodeId)).map(record => ({ ...record })),
    presentation: options.presentation.filter(record => copiedIds.has(record.nodeId)).map(record => ({ ...record }))
  };
}

export function planActivityDuplicate(options: {
  snapshot: ActivityClipboardSnapshot;
  draft: WorkflowDraft;
  frames: ScopeFrame[];
  catalog: ActivityCatalogLookup;
  allocateId?: (prefix: string, usedIds: Set<string>) => string;
  offset?: { x: number; y: number };
}): ActivityDuplicatePlan | null {
  const scope = resolveScope(options.draft.state.rootActivity, options.frames, options.catalog);
  if (!scope || scope.slot.cardinality !== "many") return null;
  if (options.snapshot.activities.length === 0) return null;

  const usedIds = options.draft.state.rootActivity
    ? collectActivityNodeIds(options.draft.state.rootActivity, options.catalog)
    : new Set<string>();
  for (const connection of readFlowchartConnections(scope.owner)) {
    const connectionId = readString(connection, "id");
    if (connectionId) usedIds.add(connectionId);
  }
  collectStructureIdentityIds(options.draft.state.rootActivity, options.catalog, usedIds);

  const allocateId = options.allocateId ?? allocateActivityNodeId;
  const offset = options.offset ?? duplicatePositionOffset;
  const topLevelIds = new Set(options.snapshot.activities.map(activity => activity.nodeId));
  const idMap = new Map<string, string>();
  const activities = options.snapshot.activities.map(activity => {
    const remapped = remapActivityTree(activity, options.catalog, usedIds, allocateId);
    for (const [from, to] of remapped.idMap) idMap.set(from, to);
    return remapped.activity;
  });

  const takenNames = collectDisplayNames(options.draft.state.rootActivity, options.draft.activityPresentation, options.catalog);
  const presentationBySource = indexActivityPresentation(options.snapshot.presentation);
  const presentation: ActivityPresentationRecord[] = [];
  for (const [from, to] of idMap) {
    const sourceActivity = findCopiedActivity(options.snapshot.activities, from, options.catalog);
    if (!sourceActivity) continue;
    const sourcePresentation = presentationBySource.get(from);
    if (!topLevelIds.has(from)) {
      if (sourcePresentation) presentation.push({ ...sourcePresentation, nodeId: to });
      continue;
    }
    const catalogItem = resolveCatalogItem(sourceActivity, options.catalog);
    const label = resolveActivityLabel(
      sourcePresentation,
      catalogItem,
      catalogItem?.activityTypeKey ?? sourceActivity.activityVersionId
    );
    presentation.push({
      nodeId: to,
      displayName: uniqueCopiedDisplayName(label, takenNames),
      description: sourcePresentation?.description
    });
  }

  const layoutBySource = new Map(options.snapshot.layout.map(record => [record.nodeId, record]));
  const layout = [...idMap.entries()].flatMap(([from, to]) => {
    const source = layoutBySource.get(from);
    if (!source) return [];
    const isTopLevel = topLevelIds.has(from);
    return [{
      ...source,
      nodeId: to,
      x: Math.round(source.x + (isTopLevel ? offset.x : 0)),
      y: Math.round(source.y + (isTopLevel ? offset.y : 0))
    }];
  });

  const siblingConnections = options.snapshot.siblingConnections.map(connection =>
    rewriteConnection(connection, idMap, usedIds, allocateId));

  return {
    activities,
    siblingConnections,
    layout,
    presentation,
    selectedNodeIds: options.snapshot.activities
      .map(activity => idMap.get(activity.nodeId))
      .filter((nodeId): nodeId is string => !!nodeId),
    idMap
  };
}

export function applyActivityDuplicatePlan(
  draft: WorkflowDraft,
  frames: ScopeFrame[],
  plan: ActivityDuplicatePlan,
  catalog: ActivityCatalogLookup
): WorkflowDraft | null {
  const rootActivity = draft.state.rootActivity;
  if (!rootActivity || plan.activities.length === 0) return null;
  const scope = resolveScope(rootActivity, frames, catalog);
  if (!scope || scope.slot.cardinality !== "many") return null;

  let nextOwner = replaceSlotActivities(scope.owner, scope.slot, [...scope.slot.activities, ...plan.activities]);
  if (plan.siblingConnections.length > 0 && nextOwner.structure?.kind === flowchartStructureKind) {
    const connections = [...readFlowchartConnections(nextOwner), ...plan.siblingConnections];
    nextOwner = {
      ...nextOwner,
      structure: nextOwner.structure
        ? {
            ...nextOwner.structure,
            payload: {
              ...nextOwner.structure.payload,
              connections
            }
          }
        : nextOwner.structure
    };
  }

  return {
    ...draft,
    layout: [...draft.layout, ...plan.layout],
    activityPresentation: [...(draft.activityPresentation ?? []), ...plan.presentation],
    state: {
      ...draft.state,
      rootActivity: updateScopeOwner(rootActivity, frames, nextOwner, catalog)
    }
  };
}

export function pasteActivitiesIntoDraft(options: {
  draft: WorkflowDraft;
  frames: ScopeFrame[];
  snapshot: ActivityClipboardSnapshot;
  catalog: ActivityCatalogLookup;
  allocateId?: (prefix: string, usedIds: Set<string>) => string;
  offset?: { x: number; y: number };
}) {
  const plan = planActivityDuplicate(options);
  if (!plan) return null;
  const nextDraft = applyActivityDuplicatePlan(options.draft, options.frames, plan, options.catalog);
  return nextDraft ? { draft: nextDraft, selectedNodeIds: plan.selectedNodeIds, idMap: plan.idMap } : null;
}

function remapActivityTree(
  activity: ActivityNode,
  catalog: ActivityCatalogLookup,
  usedIds: Set<string>,
  allocateId: (prefix: string, usedIds: Set<string>) => string
) {
  const clone = cloneValue(activity);
  const idMap = new Map<string, string>();
  for (const nodeId of collectActivityNodeIds(clone, catalog)) {
    idMap.set(nodeId, allocateId(nodeIdPrefix(nodeId), usedIds));
  }
  collectStructureIdentityIds(clone, catalog, new Set()).forEach(identity => {
    if (!idMap.has(identity)) idMap.set(identity, allocateId(nodeIdPrefix(identity), usedIds));
  });
  return { activity: rewriteActivityIds(clone, idMap), idMap };
}

function rewriteActivityIds(activity: ActivityNode, idMap: Map<string, string>): ActivityNode {
  const next: ActivityNode = {
    ...activity,
    nodeId: idMap.get(activity.nodeId) ?? activity.nodeId
  };
  if (!activity.structure) return next;
  return {
    ...next,
    structure: {
      ...activity.structure,
      payload: rewriteStructurePayload(activity.structure.payload, idMap)
    }
  };
}

function rewriteStructurePayload(
  payload: Record<string, unknown>,
  idMap: Map<string, string>
): Record<string, unknown> {
  const next: Record<string, unknown> = { ...payload };
  if (Array.isArray(next.activities)) {
    next.activities = next.activities.map(item => (isActivityNode(item) ? rewriteActivityIds(item, idMap) : item));
  }
  if (isActivityNode(next.body)) next.body = rewriteActivityIds(next.body, idMap);
  if (Array.isArray(next.connections)) {
    next.connections = next.connections.map(item =>
      isRecord(item) ? rewriteConnection(item, idMap) : item);
  }
  if (typeof next.startNodeId === "string" && idMap.has(next.startNodeId)) {
    next.startNodeId = idMap.get(next.startNodeId);
  }
  if (isRecord(next.nodeMetadata)) next.nodeMetadata = remapRecordKeys(next.nodeMetadata, idMap);
  if (isRecord(next.connectionMetadata)) next.connectionMetadata = remapRecordKeys(next.connectionMetadata, idMap);
  if (Array.isArray(next.elements)) {
    next.elements = next.elements.map(item => rewriteBpmnElement(item, idMap));
  }
  if (Array.isArray(next.sequenceFlows)) {
    next.sequenceFlows = next.sequenceFlows.map(item => rewriteBpmnFlow(item, idMap));
  }

  for (const [key, value] of Object.entries(next)) {
    if (ownedPayloadKeys.has(key)) continue;
    if (isActivityNode(value)) next[key] = rewriteActivityIds(value, idMap);
    else if (Array.isArray(value) && value.length > 0 && value.every(isActivityNode)) {
      next[key] = value.map(item => rewriteActivityIds(item, idMap));
    }
  }
  return next;
}

function rewriteConnection(
  connection: Record<string, unknown>,
  idMap: Map<string, string>,
  usedIds?: Set<string>,
  allocateId?: (prefix: string, usedIds: Set<string>) => string
) {
  const next = { ...connection };
  const source = isRecord(connection.source) ? { ...connection.source } : null;
  const target = isRecord(connection.target) ? { ...connection.target } : null;
  if (source && typeof source.nodeId === "string") source.nodeId = idMap.get(source.nodeId) ?? source.nodeId;
  if (target && typeof target.nodeId === "string") target.nodeId = idMap.get(target.nodeId) ?? target.nodeId;
  if (source) next.source = source;
  if (target) next.target = target;
  const previousId = readString(connection, "id");
  if (previousId) {
    next.id = idMap.get(previousId)
      ?? (usedIds && allocateId ? allocateId("flow", usedIds) : `${previousId}-copy`);
  }
  return next;
}

function rewriteBpmnElement(value: unknown, idMap: Map<string, string>) {
  if (!isRecord(value)) return value;
  const next = { ...value };
  if (typeof next.elementId === "string") next.elementId = idMap.get(next.elementId) ?? next.elementId;
  if (typeof next.childNodeId === "string") next.childNodeId = idMap.get(next.childNodeId) ?? next.childNodeId;
  if (typeof next.defaultFlowId === "string") next.defaultFlowId = idMap.get(next.defaultFlowId) ?? next.defaultFlowId;
  return next;
}

function rewriteBpmnFlow(value: unknown, idMap: Map<string, string>) {
  if (!isRecord(value)) return value;
  const next = { ...value };
  if (typeof next.flowId === "string") next.flowId = idMap.get(next.flowId) ?? next.flowId;
  if (typeof next.sourceRef === "string") next.sourceRef = idMap.get(next.sourceRef) ?? next.sourceRef;
  if (typeof next.targetRef === "string") next.targetRef = idMap.get(next.targetRef) ?? next.targetRef;
  return next;
}

function collectStructureIdentityIds(
  activity: ActivityNode | null | undefined,
  catalog: ActivityCatalogLookup,
  result: Set<string>
) {
  if (!activity) return result;
  const payload = activity.structure?.payload;
  if (payload) {
    if (Array.isArray(payload.elements)) {
      for (const element of payload.elements) {
        if (isRecord(element) && typeof element.elementId === "string") result.add(element.elementId);
      }
    }
    if (Array.isArray(payload.sequenceFlows)) {
      for (const flow of payload.sequenceFlows) {
        if (isRecord(flow) && typeof flow.flowId === "string") result.add(flow.flowId);
      }
    }
    if (Array.isArray(payload.connections)) {
      for (const connection of payload.connections) {
        const connectionId = isRecord(connection) ? readString(connection, "id") : null;
        if (connectionId) result.add(connectionId);
      }
    }
  }
  for (const slot of activity.structure ? [{ activities: readNestedActivities(activity) }] : []) {
    for (const child of slot.activities) collectStructureIdentityIds(child, catalog, result);
  }
  return result;
}

function readNestedActivities(activity: ActivityNode): ActivityNode[] {
  const payload = activity.structure?.payload;
  if (!payload) return [];
  const nested: ActivityNode[] = [];
  if (Array.isArray(payload.activities)) nested.push(...payload.activities.filter(isActivityNode));
  if (isActivityNode(payload.body)) nested.push(payload.body);
  for (const [key, value] of Object.entries(payload)) {
    if (key === "activities" || key === "body") continue;
    if (isActivityNode(value)) nested.push(value);
    else if (Array.isArray(value)) nested.push(...value.filter(isActivityNode));
  }
  return nested;
}

function collectDisplayNames(
  root: ActivityNode | null | undefined,
  presentation: ActivityPresentationRecord[] | undefined,
  catalog: ActivityCatalogLookup
) {
  const names = new Set<string>();
  const presentationByNodeId = indexActivityPresentation(presentation);
  const visit = (activity: ActivityNode | null | undefined) => {
    if (!activity) return;
    const catalogItem = resolveCatalogItem(activity, catalog);
    const label = resolveActivityLabel(
      presentationByNodeId.get(activity.nodeId),
      catalogItem,
      catalogItem?.activityTypeKey ?? activity.activityVersionId
    );
    if (label) names.add(label.toLowerCase());
    for (const child of readNestedActivities(activity)) visit(child);
  };
  visit(root);
  return names;
}

function findCopiedActivity(
  activities: ActivityNode[],
  nodeId: string,
  catalog: ActivityCatalogLookup
): ActivityNode | null {
  for (const activity of activities) {
    if (activity.nodeId === nodeId) return activity;
    for (const child of collectActivityNodeIds(activity, catalog)) {
      if (child === nodeId) {
        const found = findInTree(activity, nodeId);
        if (found) return found;
      }
    }
  }
  return null;
}

function findInTree(activity: ActivityNode, nodeId: string): ActivityNode | null {
  if (activity.nodeId === nodeId) return activity;
  for (const child of readNestedActivities(activity)) {
    const found = findInTree(child, nodeId);
    if (found) return found;
  }
  return null;
}

function readFlowchartConnections(owner: ActivityNode | null | undefined): Record<string, unknown>[] {
  const connections = owner?.structure?.payload.connections;
  return Array.isArray(connections) ? connections.filter(isRecord) : [];
}

function readConnectionNodeId(connection: Record<string, unknown>, side: "source" | "target") {
  const endpoint = connection[side];
  return isRecord(endpoint) && typeof endpoint.nodeId === "string" ? endpoint.nodeId : null;
}

function remapRecordKeys(record: Record<string, unknown>, idMap: Map<string, string>) {
  return Object.fromEntries(Object.entries(record).map(([key, value]) => [idMap.get(key) ?? key, value]));
}

function resolveCatalogItem(activity: ActivityNode, catalog: ActivityCatalogLookup) {
  if (!catalog) return undefined;
  if (Array.isArray(catalog)) return catalog.find(item => item.activityVersionId === activity.activityVersionId);
  if (catalog instanceof Map) return catalog.get(activity.activityVersionId);
  return catalog.activityVersionId === activity.activityVersionId ? catalog : undefined;
}

function nodeIdPrefix(nodeId: string) {
  return nodeId.replace(/-[a-f0-9]{8}$/i, "") || nodeId;
}

function isActivityNode(value: unknown): value is ActivityNode {
  return isRecord(value) && typeof value.nodeId === "string" && typeof value.activityVersionId === "string";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}

function readString(record: Record<string, unknown>, key: string) {
  const value = record[key];
  return typeof value === "string" && value ? value : null;
}

function cloneValue<T>(value: T): T {
  return typeof structuredClone === "function"
    ? structuredClone(value)
    : JSON.parse(JSON.stringify(value)) as T;
}

const ownedPayloadKeys = new Set([
  "activities",
  "body",
  "connections",
  "elements",
  "sequenceFlows",
  "nodeMetadata",
  "connectionMetadata",
  "startNodeId"
]);
