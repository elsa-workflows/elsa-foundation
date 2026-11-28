import { flowchartEdges, getChildSlots, readFlowchartStartNodeId } from "./workflowAdapter";
import { getHttpEndpointResponseMode } from "./httpEndpointResponseMode";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";

export interface HttpEndpointResponseWarning {
  endpoint: ActivityNode;
  responseActivity: ActivityNode;
}

/**
 * Find a WriteHttpResponse node reachable after this endpoint through the authored Sequence or
 * Flowchart topology. Composite children are traversed, and a child-scope exit continues to the
 * container's authored successor. Unknown designer topologies stay unknown rather than warning.
 */
export function findHttpEndpointResponseWarning(
  root: ActivityNode | null | undefined,
  endpointNodeId: string,
  catalog: ActivityCatalogItem[]
): HttpEndpointResponseWarning | null {
  if (!root) return null;

  const catalogByVersion = new Map(catalog.map(item => [item.activityVersionId, item]));
  const activities = new Map<string, ActivityNode>();
  const adjacency = new Map<string, Set<string>>();
  const scopes: Array<{ owner: ActivityNode; mode: string; children: ActivityNode[] }> = [];

  const visit = (activity: ActivityNode) => {
    if (activities.has(activity.nodeId)) return;
    activities.set(activity.nodeId, activity);
    const item = catalogByVersion.get(activity.activityVersionId);
    for (const slot of getChildSlots(activity, item)) {
      scopes.push({ owner: activity, mode: slot.mode, children: slot.activities });
      slot.activities.forEach(visit);
    }
  };
  visit(root);

  for (const { owner, mode, children } of scopes) {
    const childIds = new Set(children.map(child => child.nodeId));
    if (mode === "sequence") {
      for (let index = 0; index < children.length - 1; index++) {
        addEdge(adjacency, children[index].nodeId, children[index + 1].nodeId);
      }
    } else if (mode === "flowchart") {
      for (const edge of flowchartEdges(owner)) {
        if (childIds.has(edge.source) && childIds.has(edge.target)) addEdge(adjacency, edge.source, edge.target);
      }
    }
  }

  const authoredSuccessors = new Map(
    [...adjacency].map(([nodeId, targets]) => [nodeId, new Set(targets)])
  );

  // Enter child scopes when their composite activity is reached. When a child scope can complete,
  // continue through that composite's authored successors in its containing scope.
  for (const { owner } of scopes) {
    const catalogItem = catalogByVersion.get(owner.activityVersionId);
    for (const slot of getChildSlots(owner, catalogItem)) {
      if (slot.activities.length === 0 || (slot.mode !== "sequence" && slot.mode !== "flowchart")) continue;

      for (const entry of getScopeEntries(owner, slot.mode, slot.activities, authoredSuccessors)) {
        addEdge(adjacency, owner.nodeId, entry.nodeId);
      }

      const childIds = new Set(slot.activities.map(child => child.nodeId));
      const exits = slot.activities.filter(child => !hasNeighbor(authoredSuccessors, child.nodeId, childIds));
      const ownerSuccessors = authoredSuccessors.get(owner.nodeId) ?? new Set<string>();
      for (const exit of exits) {
        for (const successor of ownerSuccessors) addEdge(adjacency, exit.nodeId, successor);
      }
    }
  }

  const endpoint = activities.get(endpointNodeId);
  const endpointCatalog = endpoint ? catalogByVersion.get(endpoint.activityVersionId) : undefined;
  if (!endpoint || getHttpEndpointResponseMode(endpoint, endpointCatalog) !== "Async") return null;

  const reachable = new Set<string>();
  const pending = [endpoint.nodeId];
  while (pending.length > 0) {
    const current = pending.pop()!;
    if (reachable.has(current)) continue;
    reachable.add(current);
    for (const next of adjacency.get(current) ?? []) pending.push(next);
  }

  const responseActivity = [...reachable]
    .map(nodeId => activities.get(nodeId))
    .find(activity => activity && isWriteHttpResponse(activity, catalogByVersion.get(activity.activityVersionId)));
  return responseActivity ? { endpoint, responseActivity } : null;
}

function getScopeEntries(
  owner: ActivityNode,
  mode: string,
  children: ActivityNode[],
  adjacency: Map<string, Set<string>>
): ActivityNode[] {
  if (mode === "sequence") return children.slice(0, 1);

  const startNodeId = readFlowchartStartNodeId(owner);
  if (startNodeId) {
    const explicitStart = children.find(child => child.nodeId === startNodeId);
    if (explicitStart) return [explicitStart];
  }

  const childIds = new Set(children.map(child => child.nodeId));
  const inbound = new Set<string>();
  for (const child of children) {
    for (const target of adjacency.get(child.nodeId) ?? []) {
      if (childIds.has(target)) inbound.add(target);
    }
  }
  return children.filter(child => !inbound.has(child.nodeId));
}

function hasNeighbor(adjacency: Map<string, Set<string>>, nodeId: string, allowed: Set<string>) {
  return [...(adjacency.get(nodeId) ?? [])].some(target => allowed.has(target));
}

function addEdge(adjacency: Map<string, Set<string>>, source: string, target: string) {
  const targets = adjacency.get(source) ?? new Set<string>();
  targets.add(target);
  adjacency.set(source, targets);
}

function isWriteHttpResponse(activity: ActivityNode, catalogItem: ActivityCatalogItem | undefined) {
  return catalogItem?.activityTypeKey.endsWith(".WriteHttpResponse") ?? false;
}
