import { readStructureDesignFacet, type StructureDesignSlotDescriptor } from "./workflowAdapter";
import type {
  ActivityCatalogItem,
  ActivityNode,
  ActivityNodeStructure,
  WorkflowExecutableChildSlot,
  WorkflowExecutableInputBinding,
  WorkflowExecutableNode
} from "./workflowTypes";

// Adapts the Execution Material node tree served by GET /executables/{artifactId} into the authored
// ActivityNode shape the canvas machinery (buildCanvas, getChildSlots, resolveScope, slot navigation)
// already understands, so the Executable Inspector reuses the Runs-page read-only canvas wholesale.
//
// Node ids use the authored activity id: the Layout Sidecar is a publish-time copy of the definition
// layout keyed by authored node ids, so this is what makes the sidecar geometry land on the right nodes.
//
// The wire tree carries only the structure KIND (the payload — including flowchart connections — is
// deliberately not on the wire), so the synthesized structure holds child slots but no connections:
// a flowchart artifact renders its nodes at their sidecar positions without edges. If the detail
// contract later projects connections, flowchartEdges() picks them up from the payload automatically.

// Per-node inspection facts the canvas cannot carry through ActivityNode: catalog availability
// (ghost nodes), the runtime ids, and the input-binding summaries for the node details panel.
export interface ExecutableGraphNodeFacts {
  executableNodeId: string;
  authoredActivityId: string;
  activityType: string;
  activityTypeVersion: string;
  structureKind: string | null;
  available: boolean;
  inputBindings: WorkflowExecutableInputBinding[];
}

export interface ExecutableActivityGraph {
  root: ActivityNode;
  factsByNodeId: Map<string, ExecutableGraphNodeFacts>;
}

export function buildExecutableActivityGraph(root: WorkflowExecutableNode, catalog: ActivityCatalogItem[]): ExecutableActivityGraph {
  const catalogByType = new Map<string, ActivityCatalogItem[]>();
  for (const item of catalog) {
    catalogByType.set(item.activityTypeKey, [...(catalogByType.get(item.activityTypeKey) ?? []), item]);
  }

  const factsByNodeId = new Map<string, ExecutableGraphNodeFacts>();

  const adapt = (node: WorkflowExecutableNode): ActivityNode => {
    const catalogItem = resolveExecutableCatalogItem(node, catalogByType);
    const nodeId = node.authoredActivityId || node.executableNodeId;
    factsByNodeId.set(nodeId, {
      executableNodeId: node.executableNodeId,
      authoredActivityId: node.authoredActivityId,
      activityType: node.activityType,
      activityTypeVersion: node.activityTypeVersion,
      structureKind: node.structureKind ?? null,
      available: !!catalogItem,
      inputBindings: node.inputBindings ?? []
    });

    return {
      nodeId,
      // A catalog miss gets a synthetic version id no catalog can resolve, so downstream lookups
      // treat the node as unknown; the canvas layer relabels it and marks it as a ghost.
      activityVersionId: catalogItem?.activityVersionId ?? ghostActivityVersionId(node),
      inputs: [],
      outputs: [],
      structure: synthesizeStructure(node, catalogItem, adapt)
    };
  };

  return { root: adapt(root), factsByNodeId };
}

// True when the graph node was minted for a catalog miss ("not available in this environment").
export function isGhostFact(fact: ExecutableGraphNodeFacts | undefined): boolean {
  return !!fact && !fact.available;
}

// Bare presentational name for a ghost node: the short activity type name, since no catalog display
// name exists in this environment.
export function ghostNodeLabel(activityType: string): string {
  return activityType.split(".").filter(Boolean).at(-1) ?? activityType;
}

// Prefers the exact (type, version) catalog row; falls back to any version of the same type — the
// same fidelity execution has (ADR 0039 note: rendering resolves from the live catalog).
function resolveExecutableCatalogItem(node: WorkflowExecutableNode, catalogByType: Map<string, ActivityCatalogItem[]>) {
  const candidates = catalogByType.get(node.activityType) ?? [];
  return candidates.find(item => item.version === node.activityTypeVersion) ?? candidates[0];
}

function ghostActivityVersionId(node: WorkflowExecutableNode) {
  return `executable-missing:${node.activityType}@${node.activityTypeVersion}`;
}

// Rebuilds an authored-style structure from the wire slots. When the environment catalog carries the
// activity's structure design facet and every wire slot matches a plain facet slot by contract name,
// the payload is keyed by the facet's properties so getChildSlots resolves through the facet exactly
// as it does for authored definitions. Otherwise a generic payload keyed by slot-derived property
// names is used, which getChildSlots renders through its generic fallback.
function synthesizeStructure(
  node: WorkflowExecutableNode,
  catalogItem: ActivityCatalogItem | undefined,
  adapt: (child: WorkflowExecutableNode) => ActivityNode
): ActivityNodeStructure | null {
  const slots = node.childSlots ?? [];
  if (slots.length === 0) return null;

  const facet = readStructureDesignFacet(catalogItem);
  const facetMatch = facet && (!node.structureKind || facet.kind === node.structureKind)
    ? matchFacetSlots(slots, facet.payload.slots)
    : null;

  if (facet && facetMatch) {
    const payload: Record<string, unknown> = { ...facet.payload.initialPayload };
    for (const { slot, descriptor } of facetMatch) {
      const activities = slot.activities.map(adapt);
      payload[descriptor.property] = descriptor.cardinality === "single" ? activities[0] ?? null : activities;
    }
    return { kind: facet.kind, schemaVersion: facet.schemaVersion, payload };
  }

  const payload: Record<string, unknown> = {};
  for (const slot of slots) {
    payload[uniqueSlotProperty(payload, slotProperty(slot.name))] = slot.activities.map(adapt);
  }

  return {
    kind: node.structureKind ?? `executable:${node.activityType}`,
    schemaVersion: "1.0.0",
    payload
  };
}

// Pairs every wire slot with the facet slot descriptor sharing its contract name. Collection-backed
// descriptors (e.g. Switch cases) key their payload by item, which the wire slot name alone cannot
// reconstruct — those fall back to the generic payload.
function matchFacetSlots(slots: WorkflowExecutableChildSlot[], descriptors: StructureDesignSlotDescriptor[]) {
  const matches: { slot: WorkflowExecutableChildSlot; descriptor: StructureDesignSlotDescriptor }[] = [];
  for (const slot of slots) {
    const descriptor = descriptors.find(candidate => candidate.name === slot.name);
    if (!descriptor || descriptor.collectionProperty || descriptor.childProperty) return null;
    matches.push({ slot, descriptor });
  }

  return matches;
}

// "Sequence.Activities" -> "activities", "ForEach.Body" -> "body": the camelCased last segment of the
// contract slot name, so canvas-mode structure kinds resolve their primary slot (getChildSlots reads
// payload.activities for sequence/flowchart kinds) and generic labels stay readable.
function slotProperty(name: string) {
  const segment = name.split(".").filter(Boolean).at(-1) ?? name;
  return segment.charAt(0).toLowerCase() + segment.slice(1);
}

function uniqueSlotProperty(payload: Record<string, unknown>, property: string) {
  if (!(property in payload)) return property;
  let index = 2;
  while (`${property}${index}` in payload) index += 1;
  return `${property}${index}`;
}
