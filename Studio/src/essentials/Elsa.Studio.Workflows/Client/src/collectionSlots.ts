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
  itemIdProperty?: string;
  conditionProperty?: string;
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
      itemIdProperty: slot.itemIdProperty,
      conditionProperty: slot.conditionProperty,
      itemName: slot.collectionProperty === "cases" ? "case" : slot.collectionProperty === "branches" ? "branch" : "item"
    };
    return collectionSlotItems(activity, collection) ? [collection] : [];
  });
}

export function collectionSlotItems(activity: ActivityNode, collection: CollectionSlot): Record<string, unknown>[] | null {
  if (activity.structure?.kind !== collection.kind || activity.structure.schemaVersion !== collection.schemaVersion) return null;
  if (!isRecord(activity.structure.payload)) return null;
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
  const item: Record<string, unknown> = { [collection.labelProperty]: `${noun} ${ordinal}`, [collection.childProperty]: null };
  if (collection.itemIdProperty) item[collection.itemIdProperty] = crypto.randomUUID();
  if (collection.conditionProperty && collection.itemIdProperty) item[collection.conditionProperty] = {
    referenceKey: caseConditionInputKey(item[collection.itemIdProperty] as string), value: { expressionType: "Literal", value: false }
  };
  return replaceItems(activity, collection, [...items, item]);
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

export const caseConditionInputKey = (caseId: string) => `Case:${caseId}:Condition`;

export function setCollectionCondition(activity: ActivityNode, collection: CollectionSlot, caseId: string, state: unknown): ActivityNode {
  const items = collectionSlotItems(activity, collection);
  if (!items || !collection.itemIdProperty || !collection.conditionProperty) return activity;
  const matches = items.filter(item => item[collection.itemIdProperty!] === caseId);
  if (matches.length !== 1) return activity;
  // Container presence identifies a predicate, including a cleared/unbound predicate.
  const condition = { ...(isRecord(state) ? state : {}), referenceKey: caseConditionInputKey(caseId) };
  return replaceItems(activity, collection, items.map(item => item[collection.itemIdProperty!] === caseId
    ? { ...item, [collection.conditionProperty!]: condition } : item));
}

export function moveCollectionItem(activity: ActivityNode, collection: CollectionSlot, caseId: string, offset: -1 | 1): ActivityNode {
  const items = collectionSlotItems(activity, collection);
  if (!items || !collection.itemIdProperty) return activity;
  const index = items.findIndex(item => item[collection.itemIdProperty!] === caseId);
  const target = index + offset;
  if (index < 0 || target < 0 || target >= items.length || items.filter(item => item[collection.itemIdProperty!] === caseId).length !== 1) return activity;
  const reordered = [...items];
  [reordered[index], reordered[target]] = [reordered[target], reordered[index]];
  return replaceItems(activity, collection, reordered);
}

export function isPredicateOnlySwitch(activity: ActivityNode): boolean {
  if (activity.structure?.kind !== "elsa.switch.structure" || activity.structure.schemaVersion !== "2.0.0" || !isRecord(activity.structure.payload)) return false;
  const cases = activity.structure.payload.cases;
  return Array.isArray(cases) && cases.every(item => isRecord(item) && item.condition != null);
}

export function canUpgradeSwitch(activity: ActivityNode): boolean {
  if (activity.structure?.kind !== "elsa.switch.structure" || activity.structure.schemaVersion !== "1.0.0") return false;
  if (!isRecord(activity.structure.payload)) return false;
  const cases = activity.structure.payload.cases;
  if (!Array.isArray(cases) || !cases.every(item => isRecord(item) && typeof item.match === "string" && item.condition == null && item.caseId == null)) return false;
  if (isRecord(activity.Value) && isRecord(activity.Value.expression) && typeof activity.Value.expression.type === "string")
    return activity.Value.expression.type.toLowerCase() !== "secret";
  return !activity.inputs.some(input => isRecord(input) && input.referenceKey === "Value" && isRecord(input.value) && typeof input.value.expressionType === "string" && input.value.expressionType.toLowerCase() === "secret");
}

/** One document mutation retains authored Value and descendants; predicates are an explicit per-case choice. */
export function upgradeSwitch(activity: ActivityNode, target: ActivityCatalogItem): ActivityNode {
  const facet = readStructureDesignFacet(target);
  if (!canUpgradeSwitch(activity) || facet?.kind !== "elsa.switch.structure" || facet.schemaVersion !== "2.0.0") return activity;
  const cases = activity.structure!.payload.cases as Record<string, unknown>[];
  return { ...activity, activityVersionId: target.activityVersionId, structure: { ...activity.structure!, schemaVersion: facet.schemaVersion,
    payload: { ...activity.structure!.payload, cases: cases.map(item => ({ ...item, caseId: crypto.randomUUID() })) } } };
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
