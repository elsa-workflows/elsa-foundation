import type {
  ActivityCatalogItem,
  ActivityPresentationRecord
} from "./workflowTypes";
import { getActivityDisplay } from "./activityDisplay";

export const activityDisplayNameMaxLength = 200;
export const activityDescriptionMaxLength = 2_000;

export function normalizeActivityPresentationValue(
  value: string | null | undefined,
  maximumLength: number
) {
  const normalized = value?.trim();
  if (!normalized) return undefined;
  return normalized.slice(0, maximumLength);
}

export function normalizeActivityPresentation(
  records: ActivityPresentationRecord[] | null | undefined
): ActivityPresentationRecord[] {
  const normalized = new Map<string, ActivityPresentationRecord>();
  for (const record of records ?? []) {
    const nodeId = record.nodeId?.trim();
    if (!nodeId) continue;
    const displayName = normalizeActivityPresentationValue(
      record.displayName,
      activityDisplayNameMaxLength
    );
    const description = normalizeActivityPresentationValue(
      record.description,
      activityDescriptionMaxLength
    );
    if (!displayName && !description) {
      normalized.delete(nodeId);
      continue;
    }
    normalized.set(nodeId, { nodeId, displayName, description });
  }
  return [...normalized.values()];
}

export function updateActivityPresentation(
  records: ActivityPresentationRecord[],
  nodeId: string,
  patch: Pick<ActivityPresentationRecord, "displayName" | "description">
) {
  const displayName = patch.displayName?.slice(0, activityDisplayNameMaxLength);
  const description = patch.description?.slice(0, activityDescriptionMaxLength);
  const remaining = records.filter(record => record.nodeId !== nodeId);
  return displayName?.trim() || description?.trim()
    ? [...remaining, { nodeId, displayName, description }]
    : remaining;
}

export function removeActivityPresentation(
  records: ActivityPresentationRecord[],
  nodeIds: Iterable<string>
) {
  const removed = new Set(nodeIds);
  return records.filter(record => !removed.has(record.nodeId));
}

export function copyActivityPresentation(
  records: ActivityPresentationRecord[],
  sourceNodeId: string,
  targetNodeId: string
) {
  const source = records.find(record => record.nodeId === sourceNodeId);
  return source
    ? updateActivityPresentation(records, targetNodeId, source)
    : records;
}

export function indexActivityPresentation(
  records: ActivityPresentationRecord[] | null | undefined
) {
  return new Map(normalizeActivityPresentation(records).map(record => [record.nodeId, record]));
}

export function resolveActivityLabel(
  presentation: ActivityPresentationRecord | null | undefined,
  catalogItem: ActivityCatalogItem | null | undefined,
  technicalActivityType: string
) {
  return normalizeActivityPresentationValue(
    presentation?.displayName,
    activityDisplayNameMaxLength
  ) ?? (catalogItem ? getActivityDisplay(catalogItem) : shortActivityType(technicalActivityType));
}

function shortActivityType(value: string) {
  return value.split(".").filter(Boolean).at(-1) ?? value;
}
