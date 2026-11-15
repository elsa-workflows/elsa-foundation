import { getChildSlots, sequenceStructureKind, flowchartStructureKind } from "../workflowAdapter";
import { indexActivityPresentation, resolveActivityLabel } from "../activityPresentation";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import type { NativeBlockDocument } from "./nativeBlocks";

interface NodePosition { node: ActivityNode; parent: string | null }
export interface NativeBlockPreview {
  containers: { id: string; name: string; kind: string; children: number; change: "add" | "remove" }[];
  movedSteps: string[];
  addedSteps: string[];
  addedVariables: string[];
  removedConnections: string[];
  addedConnections: string[];
}

/** Human-readable projection of the exact proposal. The full authored snapshots remain available for inspection. */
export function describeNativeBlockChange(before: NativeBlockDocument, after: NativeBlockDocument, catalog: ActivityCatalogItem[]): NativeBlockPreview {
  function index(document: NativeBlockDocument) {
    const nodes = new Map<string, NodePosition>();
    const variables = new Map<string, string>();
    const connections = new Map<string, string>();
    const presentation = indexActivityPresentation(document.activityPresentation);
    const items = new Map(catalog.map(item => [item.activityVersionId, item]));
    const label = (id: string) => {
      const node = nodes.get(id)?.node;
      return resolveActivityLabel(presentation.get(id), node && items.get(node.activityVersionId), id);
    };
    const visit = (node: ActivityNode, parent: string | null) => {
      if (nodes.has(node.nodeId)) return;
      nodes.set(node.nodeId, { node, parent });
      for (const slot of getChildSlots(node, catalog)) for (const child of slot.activities) visit(child, node.nodeId);
    };
    visit(document.rootActivity, null);
    for (const { node } of nodes.values()) {
      const payload = node.structure?.payload;
      if (Array.isArray(payload?.variables)) for (const variable of payload.variables) {
        if (variable && typeof variable === "object") {
          const value = variable as Record<string, unknown>;
          const key = typeof value.id === "string" ? value.id : typeof value.name === "string" ? value.name : JSON.stringify(value);
          variables.set(`${node.nodeId}:${key}`, `${String(value.name ?? key)} in ${label(node.nodeId)}`);
        }
      }
      if (Array.isArray(payload?.connections)) for (const connection of payload.connections) {
        if (!connection || typeof connection !== "object") continue;
        const value = connection as { source?: { nodeId?: string; port?: string }; target?: { nodeId?: string } };
        if (!value.source?.nodeId || !value.target?.nodeId) continue;
        connections.set(JSON.stringify([node.nodeId, connection]), `${label(value.source.nodeId)} (${value.source.port || "Done"}) → ${label(value.target.nodeId)} in ${label(node.nodeId)}`);
      }
    }
    return { nodes, variables, connections, label };
  }
  const old = index(before), next = index(after);
  const containers: NativeBlockPreview["containers"] = [];
  for (const [source, other, change] of [[next, old, "add"], [old, next, "remove"]] as const) {
    for (const [id, { node }] of source.nodes) {
      if (other.nodes.has(id)) continue;
      const kind = node.structure?.kind === sequenceStructureKind ? "Sequence" : node.structure?.kind === flowchartStructureKind ? "Flowchart" : null;
      if (kind) containers.push({ id, name: source.label(id), kind, children: getChildSlots(node, catalog).reduce((sum, slot) => sum + slot.activities.length, 0), change });
    }
  }
  return {
    containers,
    movedSteps: [...next.nodes].filter(([id, value]) => old.nodes.has(id) && old.nodes.get(id)!.parent !== value.parent).map(([id, value]) => `${next.label(id)} → ${value.parent ? next.label(value.parent) : "root"}`),
    addedSteps: [...next.nodes].filter(([id]) => !old.nodes.has(id) && !containers.some(container => container.change === "add" && container.id === id)).map(([id]) => next.label(id)),
    addedVariables: [...next.variables].filter(([id]) => !old.variables.has(id)).map(([, label]) => label),
    removedConnections: [...old.connections].filter(([id]) => !next.connections.has(id)).map(([, label]) => label),
    addedConnections: [...next.connections].filter(([id]) => !old.connections.has(id)).map(([, label]) => label)
  };
}
