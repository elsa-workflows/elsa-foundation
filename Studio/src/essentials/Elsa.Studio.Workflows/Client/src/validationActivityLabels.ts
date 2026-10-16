import { getActivityDisplay } from "./activityDisplay";
import {
  indexActivityPresentation,
  normalizeActivityPresentationValue,
  activityDisplayNameMaxLength
} from "./activityPresentation";
import type {
  ActivityCatalogItem,
  ActivityNode,
  ActivityPresentationRecord,
  ValidationError
} from "./workflowTypes";
import { parseValidationErrorPath } from "./validationDiagnostics";

export interface ValidationActivityLabels {
  labelFor(nodeId: string): string;
  humanizeMessage(message: string, fallbackNodeId?: string | null): HumanizedUserMessage;
}

export interface HumanizedUserMessage {
  message: string;
  code?: string;
  nodeId?: string;
}

export interface ValidationActivityLabelSource {
  root?: ActivityNode | null;
  presentation?: ActivityPresentationRecord[] | null;
  catalogByVersion?: ReadonlyMap<string, ActivityCatalogItem>;
}

const nodeIdSuffixPattern = /^(.+)-([0-9a-f]{8})$/i;
const quotedNodePattern = /(?:activity(?: node)?|executable node)\s+'([^']+)'/i;
const requiredInputOnActivityPattern = /^Required input '([^']+)' on activity '([^']+)' is missing or empty\.?$/i;
const vfActOmitPattern = /^VF-ACT-003:\s+Activity node '([^']+)' omits required input '([^']+)', which has no pinned default\.\s+Every published input must lower to exactly one canonical binding\.?$/i;
const vfActDuplicatePattern = /^VF-ACT-003:\s+Activity node '([^']+)' declares duplicate input '([^']+)'\.\s+Every published input must lower to exactly one canonical binding\.?$/i;
const vfActMaterializePattern = /^VF-ACT-003:\s+Required input '([^']+)' on executable node '([^']+)' cannot materialize as absent\.?$/i;
const vfActAbsentPattern = /^VF-ACT-003:\s+Input '([^']+)' on executable node '([^']+)' is absent after binding normalization\.?$/i;
const vfActPrefixPattern = /^(VF-ACT-\d+):\s+/;

/**
 * Canvas-aligned labels for designer validation and test-run rejection copy (issue #2698).
 * Authored Details display names win; otherwise the catalog type, with a short index when two
 * unnamed activities share a type. Node ids stay off the sentence and belong on the tooltip / Go to.
 */
export function createValidationActivityLabels(source: ValidationActivityLabelSource = {}): ValidationActivityLabels {
  const activities = collectActivities(source.root ?? null);
  const presentationByNodeId = indexActivityPresentation(source.presentation);
  const catalog = source.catalogByVersion;
  const typeCounts = new Map<string, string[]>();
  const labels = new Map<string, string>();
  const inputLookup = new Map<string, ActivityCatalogItem>();

  for (const activity of activities) {
    const catalogItem = catalog?.get(activity.activityVersionId);
    if (catalogItem) inputLookup.set(activity.nodeId, catalogItem);
    const authored = normalizeActivityPresentationValue(
      presentationByNodeId.get(activity.nodeId)?.displayName,
      activityDisplayNameMaxLength
    );
    const typeLabel = resolveTypeLabel(activity, catalogItem);
    if (authored) {
      labels.set(activity.nodeId, formatValidationActivityLabel(authored, typeLabel));
      continue;
    }
    const unnamed = typeCounts.get(typeLabel) ?? [];
    unnamed.push(activity.nodeId);
    typeCounts.set(typeLabel, unnamed);
  }

  for (const [typeLabel, nodeIds] of typeCounts) {
    nodeIds.forEach((nodeId, index) => {
      labels.set(nodeId, formatValidationActivityLabel(undefined, typeLabel, index + 1, nodeIds.length));
    });
  }

  const labelFor = (nodeId: string) => labels.get(nodeId)
    ?? formatValidationActivityLabel(undefined, typeLabelFromNodeId(nodeId) ?? nodeId);

  const inputNameFor = (nodeId: string | undefined, key: string) => resolveInputLabel(nodeId ? inputLookup.get(nodeId) : undefined, key);

  return {
    labelFor,
    humanizeMessage(message, fallbackNodeId) {
      return humanizeUserFacingMessage(message, labelFor, inputNameFor, fallbackNodeId);
    }
  };
}

export function formatValidationActivityLabel(
  displayName: string | null | undefined,
  typeLabel: string,
  unnamedIndex = 1,
  unnamedCount = 1
) {
  const authored = displayName?.trim();
  if (authored) return `"${authored}" (${typeLabel})`;
  if (unnamedCount > 1) return `${typeLabel} #${unnamedIndex}`;
  return typeLabel;
}

export function humanizeValidationErrors(
  errors: ValidationError[],
  labels: ValidationActivityLabels
): ValidationError[] {
  return errors.map(error => {
    const nodeId = parseValidationErrorPath(error.path).nodeId;
    const humanized = labels.humanizeMessage(error.message ?? "", nodeId);
    if (humanized.message === (error.message ?? "") && !humanized.code) return error;
    return {
      ...error,
      message: humanized.message,
      ...(humanized.code ? { code: error.code ?? humanized.code } : {})
    };
  });
}

function humanizeUserFacingMessage(
  message: string,
  labelFor: (nodeId: string) => string,
  inputNameFor: (nodeId: string | undefined, key: string) => string,
  fallbackNodeId?: string | null
): HumanizedUserMessage {
  const trimmed = message.trim();
  if (!trimmed) return { message };

  const omit = vfActOmitPattern.exec(trimmed);
  if (omit) {
    return requiredInputMessage(labelFor(omit[1]), inputNameFor(omit[1], omit[2]), omit[1], "VF-ACT-003");
  }
  const materialize = vfActMaterializePattern.exec(trimmed);
  if (materialize) {
    return requiredInputMessage(labelFor(materialize[2]), inputNameFor(materialize[2], materialize[1]), materialize[2], "VF-ACT-003");
  }
  const absent = vfActAbsentPattern.exec(trimmed);
  if (absent) {
    return requiredInputMessage(labelFor(absent[2]), inputNameFor(absent[2], absent[1]), absent[2], "VF-ACT-003");
  }
  const duplicate = vfActDuplicatePattern.exec(trimmed);
  if (duplicate) {
    return {
      message: `${labelFor(duplicate[1])} declares the input "${inputNameFor(duplicate[1], duplicate[2])}" more than once.`,
      code: "VF-ACT-003",
      nodeId: duplicate[1]
    };
  }

  const required = requiredInputOnActivityPattern.exec(trimmed);
  if (required) {
    return requiredInputMessage(labelFor(required[2]), inputNameFor(required[2], required[1]), required[2]);
  }

  let next = trimmed;
  const quoted = quotedNodePattern.exec(next);
  const nodeId = quoted?.[1] ?? fallbackNodeId ?? undefined;
  if (quoted) {
    const label = labelFor(quoted[1]);
    next = next.replace(quoted[0], label);
  }
  if (nodeId && next.includes(`'${nodeId}'`)) next = next.replaceAll(`'${nodeId}'`, labelFor(nodeId));
  if (nodeId && next.includes(`"${nodeId}"`)) next = next.replaceAll(`"${nodeId}"`, labelFor(nodeId));

  const codeMatch = vfActPrefixPattern.exec(next);
  if (codeMatch) {
    next = next.slice(codeMatch[0].length).replace(/\s+Every published input must lower to exactly one canonical binding\.?$/i, "").trim();
    return { message: next, code: codeMatch[1], nodeId };
  }

  return { message: next, nodeId };
}

function requiredInputMessage(activityLabel: string, inputLabel: string, nodeId: string, code?: string): HumanizedUserMessage {
  return {
    message: `Required input "${inputLabel}" on ${activityLabel} is empty.`,
    code,
    nodeId
  };
}

function resolveTypeLabel(activity: ActivityNode, catalogItem: ActivityCatalogItem | undefined) {
  if (catalogItem) return getActivityDisplay(catalogItem);
  return typeLabelFromNodeId(activity.nodeId) ?? activity.nodeId;
}

function typeLabelFromNodeId(nodeId: string) {
  const match = nodeIdSuffixPattern.exec(nodeId);
  if (!match) return null;
  return humanizeActivityTypeName(match[1]);
}

function humanizeActivityTypeName(name: string) {
  return name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/[-_]+/g, " ")
    .replace(/\b\w/g, letter => letter.toUpperCase())
    .trim();
}

function resolveInputLabel(catalogItem: ActivityCatalogItem | undefined, key: string) {
  const needle = key.trim().toLowerCase();
  for (const raw of catalogItem?.inputs ?? []) {
    if (!raw || typeof raw !== "object") continue;
    const input = raw as Record<string, unknown>;
    const referenceKey = readString(input.referenceKey);
    const name = readString(input.name);
    const displayName = readString(input.displayName);
    if ([referenceKey, name, displayName].some(value => value.toLowerCase() === needle)) {
      return displayName || name || titleCaseInput(key);
    }
  }
  return titleCaseInput(key);
}

function titleCaseInput(key: string) {
  if (/^[a-z][a-zA-Z0-9]*$/.test(key)) return key.charAt(0).toUpperCase() + key.slice(1);
  return key;
}

function readString(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function collectActivities(root: ActivityNode | null) {
  const activities: ActivityNode[] = [];
  const seen = new Set<object>();
  const walk = (node: ActivityNode | null) => {
    if (!node || seen.has(node)) return;
    seen.add(node);
    activities.push(node);
    const payload = node.structure?.payload;
    if (!payload) return;
    for (const value of Object.values(payload)) {
      if (Array.isArray(value)) value.filter(isActivityNode).forEach(walk);
      else if (isActivityNode(value)) walk(value);
    }
  };
  walk(root);
  return activities;
}

function isActivityNode(value: unknown): value is ActivityNode {
  return typeof value === "object" && value !== null
    && typeof (value as ActivityNode).nodeId === "string"
    && typeof (value as ActivityNode).activityVersionId === "string";
}
