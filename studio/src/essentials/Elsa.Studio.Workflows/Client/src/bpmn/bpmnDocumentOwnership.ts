import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { getFacetChildSlots, readStructureDesignFacet, sequenceStructureKind, type ActivityCatalogLookup, type ChildSlot } from "../workflowAdapter";
import { bpmnStructureKind } from "./bpmnStructure";

export function bpmnNativeCatalogItem(node: ActivityNode, catalog: ActivityCatalogLookup): ActivityCatalogItem | undefined {
  return catalog instanceof Map ? catalog.get(node.activityVersionId) : Array.isArray(catalog)
    ? catalog.find(item => item.activityVersionId === node.activityVersionId)
    : catalog?.activityVersionId === node.activityVersionId ? catalog : undefined;
}

/** Null marks opaque ownership; an empty array means there are no declared child slots. */
export function qualifiedBpmnNativeChildSlots(node: ActivityNode, catalog?: ActivityCatalogLookup,
  options?: { includeReaderForests?: boolean }): ChildSlot[] | null {
  const structure = node.structure;
  if (!structure) return [];
  // A complete document always owns only this native forest, independently of catalog notation.
  if (options?.includeReaderForests && structure.kind === bpmnStructureKind && structure.schemaVersion === "2.0.0")
    return [activitiesSlot(node, "bpmn")];
  const facet = readStructureDesignFacet(bpmnNativeCatalogItem(node, catalog));
  if (facet) {
    if (facet.kind !== structure.kind || facet.schemaVersion !== structure.schemaVersion) return null;
    // Projection omits invalid collections/rows; physical ownership must reject that lost data.
    for (const descriptor of facet.payload.slots) {
      if (!descriptor.collectionProperty || !descriptor.childProperty) continue;
      const collection = structure.payload?.[descriptor.collectionProperty];
      if (!Array.isArray(collection))
        throw new Error(`Cannot inspect BPMN native ownership: declared collection '${descriptor.collectionProperty}' is malformed.`);
      for (const item of collection) if (!item || typeof item !== "object" || Array.isArray(item))
        throw new Error(`Cannot inspect BPMN native ownership: declared collection '${descriptor.collectionProperty}' contains a malformed row.`);
    }
    return getFacetChildSlots(structure, facet);
  }
  if (structure.schemaVersion === "1.0.0" && (structure.kind === sequenceStructureKind
    || options?.includeReaderForests && structure.kind === "elsa.sequence")) return [activitiesSlot(node, "sequence")];
  return null;
}

function activitiesSlot(node: ActivityNode, mode: ChildSlot["mode"]): ChildSlot {
  const value = node.structure!.payload?.activities;
  return { id: `${node.structure!.kind}:activities`, label: "Activities", property: "activities", cardinality: "many", mode,
    // Filter only the declared array. A missing/malformed array must never expose metadata slots.
    activities: Array.isArray(value) ? value.filter((child): child is ActivityNode => !!child && typeof child === "object"
      && typeof child.nodeId === "string" && typeof child.activityVersionId === "string") : [] };
}
