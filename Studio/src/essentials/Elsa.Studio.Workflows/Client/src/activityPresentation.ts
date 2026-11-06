import type {
  ActivityCatalogItem,
  ActivityPresentationRecord
} from "./workflowTypes";
import { getActivityDisplay } from "./activityDisplay";
import { humanizeTypeName, shortTypeName } from "./workflowFormatting";

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
    const nodeId = record.nodeId;
    if (!nodeId?.trim()) continue;
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
  const authored = normalizeActivityPresentationValue(
    presentation?.displayName,
    activityDisplayNameMaxLength
  );
  return (authored && !isTechnicalActivityLabel(authored) ? authored : undefined)
    ?? (catalogItem ? getActivityDisplay(catalogItem) : friendlyShortActivityType(technicalActivityType));
}

/**
 * True when a presentation label or summary is an identity token (GUID, long hex, or a
 * version+build hash such as `1.0.0+e26c11bd…` / `0+e26c11bd…`) rather than a human name.
 */
export function isTechnicalActivityLabel(value: string | null | undefined) {
  const text = unwrapQuotedLabel(value);
  if (!text) return false;
  if (guidIdentityPattern.test(text)) return true;
  if (hexIdentityPattern.test(text)) return true;
  return versionBuildHashPattern.test(text);
}

function friendlyShortActivityType(value: string) {
  const short = shortTypeName(value) ?? value;
  if (short && !isTechnicalActivityLabel(short)) return short;
  const humanized = humanizeTypeName(value);
  if (humanized && !isTechnicalActivityLabel(humanized)) return humanized;
  return "Activity";
}

function unwrapQuotedLabel(value: string | null | undefined) {
  const trimmed = value?.trim() ?? "";
  const quoted = /^"(.*)"$/.exec(trimmed);
  return (quoted ? quoted[1] : trimmed).trim();
}

const guidIdentityPattern = /^\{?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}?$/i;
const hexIdentityPattern = /^[0-9a-f]{16,}$/i;
const versionBuildHashPattern = /^(?:\d+\.)*\d+\+[0-9a-f]{12,}$/i;
