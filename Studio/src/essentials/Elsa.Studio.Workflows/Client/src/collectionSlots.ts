import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";
import { readStructureDesignFacet, type StructureDesignFacet } from "./workflowAdapter";

export function getCollectionSlots(activity: ActivityNode, catalog?: ActivityCatalogItem | null): CollectionSlot[] {
  return collectionSlotsFromFacet(activity, readStructureDesignFacet(catalog));
}

/** A collection with one exact label and one child per item; multi-child designers keep ownership. */
export interface CollectionSlot {
  kind: string;
  schemaVersion: string;
  property: string;
  childProperty: string;
  labelProperty: string;
  displayName: string;
  itemName: string;
}

export function collectionSlotsFromFacet(activity: ActivityNode, facet: StructureDesignFacet | null): CollectionSlot[] {
  if (!facet || facet.kind !== activity.structure?.kind || facet.schemaVersion !== activity.structure.schemaVersion) return [];
  return facet.payload.slots.flatMap(slot => {
    if (slot.cardinality !== "single" || !slot.collectionProperty || !slot.childProperty || !slot.labelProperty) return [];
    if (facet.payload.slots.filter(candidate => candidate.collectionProperty === slot.collectionProperty).length !== 1) return [];
    const collection: CollectionSlot = {
      kind: facet.kind, schemaVersion: facet.schemaVersion,
      property: slot.collectionProperty, childProperty: slot.childProperty, labelProperty: slot.labelProperty,
      displayName: slot.displayName,
      itemName: slot.collectionProperty === "cases" ? "case" : slot.collectionProperty === "branches" ? "branch" : "item"
    };
    return collectionSlotItems(activity, collection) ? [collection] : [];
  });
}

export function collectionSlotItems(activity: ActivityNode, collection: CollectionSlot): Record<string, unknown>[] | null {
  if (activity.structure?.kind !== collection.kind || activity.structure.schemaVersion !== collection.schemaVersion) return null;
  const items = activity.structure.payload[collection.property];
  return Array.isArray(items) && items.every(item => isRecord(item) && typeof item[collection.labelProperty] === "string"
    && (item[collection.childProperty] == null || isActivity(item[collection.childProperty]))) ? items : null;
}

export function addCollectionItem(activity: ActivityNode, collection: CollectionSlot): ActivityNode {
  const items = collectionSlotItems(activity, collection);
  if (!items) return activity;
  const noun = collection.itemName[0].toUpperCase() + collection.itemName.slice(1);
  let ordinal = 1;
  while (items.some(item => item[collection.labelProperty] === `${noun} ${ordinal}`)) ordinal++;
  return replaceItems(activity, collection, [...items, { [collection.labelProperty]: `${noun} ${ordinal}`, [collection.childProperty]: null }]);
}

export function renameCollectionItem(activity: ActivityNode, collection: CollectionSlot, index: number, previousLabel: string, label: string): ActivityNode {
  const items = collectionSlotItems(activity, collection);
  if (!items || items[index]?.[collection.labelProperty] !== previousLabel || label === previousLabel
    || items.some((item, position) => position !== index && item[collection.labelProperty] === label)) return activity;
  return replaceItems(activity, collection, items.map((item, position) => position === index ? { ...item, [collection.labelProperty]: label } : item));
}

export function removeCollectionItem(activity: ActivityNode, collection: CollectionSlot, index: number, previousLabel: string): ActivityNode {
  const items = collectionSlotItems(activity, collection);
  if (!items || items[index]?.[collection.labelProperty] !== previousLabel) return activity;
  return replaceItems(activity, collection, items.filter((_, position) => position !== index));
}

function replaceItems(activity: ActivityNode, collection: CollectionSlot, items: Record<string, unknown>[]): ActivityNode {
  return { ...activity, structure: { ...activity.structure!, payload: { ...activity.structure!.payload, [collection.property]: items } } };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isActivity(value: unknown): boolean {
  return isRecord(value) && typeof value.nodeId === "string" && typeof value.activityVersionId === "string";
}
