import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { getChildSlots, readStructureDesignFacet, sequenceStructureKind, type ActivityCatalogLookup, type ChildSlot } from "../workflowAdapter";
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
  if (facet) return facet.kind === structure.kind && facet.schemaVersion === structure.schemaVersion ? getChildSlots(node, catalog) : null;
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
