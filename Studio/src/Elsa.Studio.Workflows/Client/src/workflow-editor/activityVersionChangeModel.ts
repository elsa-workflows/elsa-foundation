import type { ActivityContract, ActivityDefinitionVersionView } from "../activityDefinitionTypes";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";
import type { ActivityVersionDiffView } from "../api/activityVersionChange";
import { getDraftRevision } from "./editorHelpers";

export type ActivityVersionChangeScope = "occurrence" | "matching";

export interface ActivityVersionChangePrecondition {
  draftId: string;
  draftRevision: string;
  occurrenceId: string;
  fromVersionId: string;
}

export interface ActivityVersionChangeImpact {
  occurrenceIds: string[];
  preservedBindingKeys: string[];
  unresolvedBindingKeys: string[];
  preservedOutcomeKeys: string[];
  unresolvedOutcomeKeys: string[];
}

export function createActivityVersionChangePrecondition(
  draft: WorkflowDraft,
  occurrence: ActivityNode
): ActivityVersionChangePrecondition {
  return {
    draftId: draft.id,
    draftRevision: getDraftRevision(draft),
    occurrenceId: occurrence.nodeId,
    fromVersionId: occurrence.activityVersionId
  };
}

export function validateActivityVersionChangePrecondition(
  draft: WorkflowDraft,
  precondition: ActivityVersionChangePrecondition
): string | null {
  if (draft.id !== precondition.draftId) return "A different workflow draft is now open. Review the version change again.";
  if (getDraftRevision(draft) !== precondition.draftRevision) {
    return "The workflow draft changed after this review. Your local work was kept; refresh the review before applying.";
  }
  const occurrence = findActivityOccurrence(draft.state.rootActivity, precondition.occurrenceId);
  if (!occurrence || occurrence.activityVersionId !== precondition.fromVersionId) {
    return "The selected occurrence changed after this review. Your local work was kept; refresh the review before applying.";
  }
  return null;
}

export function analyzeActivityVersionChange(
  draft: WorkflowDraft,
  occurrence: ActivityNode,
  current: ActivityDefinitionVersionView,
  target: ActivityDefinitionVersionView,
  diff: ActivityVersionDiffView,
  scope: ActivityVersionChangeScope
): ActivityVersionChangeImpact {
  const root = draft.state.rootActivity;
  const occurrenceIds = scope === "matching"
    ? collectActivityOccurrences(root, occurrence.activityVersionId).map(item => item.nodeId)
    : [occurrence.nodeId];
  const boundKeys = collectAuthoredInputKeys(occurrence, current.contract);
  const targetInputKeys = contractKeys(target.contract, "inputs");
  const connectedOutcomeKeys = collectConnectedOutcomeKeys(root, new Set(occurrenceIds));
  const targetOutcomeKeys = contractKeys(target.contract, "outcomes");
  const incompatibleInputKeys = incompatibleMemberKeys(diff, "input");
  const incompatibleOutcomeKeys = incompatibleMemberKeys(diff, "outcome");

  return {
    occurrenceIds,
    preservedBindingKeys: boundKeys.filter(key => targetInputKeys.has(key) && !incompatibleInputKeys.has(key)),
    unresolvedBindingKeys: boundKeys.filter(key => !targetInputKeys.has(key) || incompatibleInputKeys.has(key)),
    preservedOutcomeKeys: connectedOutcomeKeys.filter(key => targetOutcomeKeys.has(key) && !incompatibleOutcomeKeys.has(key)),
    unresolvedOutcomeKeys: connectedOutcomeKeys.filter(key => !targetOutcomeKeys.has(key) || incompatibleOutcomeKeys.has(key))
  };
}

export function applyActivityVersionChange(
  draft: WorkflowDraft,
  occurrenceId: string,
  fromVersionId: string,
  toVersionId: string,
  scope: ActivityVersionChangeScope
): WorkflowDraft {
  const root = draft.state.rootActivity;
  if (!root) return draft;
  const targetIds = new Set(scope === "matching"
    ? collectActivityOccurrences(root, fromVersionId).map(item => item.nodeId)
    : [occurrenceId]);
  const nextRoot = mapActivityTree(root, activity =>
    targetIds.has(activity.nodeId) && activity.activityVersionId === fromVersionId
      ? { ...activity, activityVersionId: toVersionId }
      : activity);
  if (nextRoot === root) return draft;
  return {
    ...draft,
    state: {
      ...draft.state,
      rootActivity: nextRoot
    }
  };
}

export function findActivityOccurrence(root: ActivityNode | null | undefined, nodeId: string): ActivityNode | null {
  if (!root) return null;
  if (root.nodeId === nodeId) return root;
  for (const child of directActivityChildren(root)) {
    const found = findActivityOccurrence(child, nodeId);
    if (found) return found;
  }
  return null;
}

export function collectActivityOccurrences(
  root: ActivityNode | null | undefined,
  versionId: string
): ActivityNode[] {
  if (!root) return [];
  const matches = root.activityVersionId === versionId ? [root] : [];
  for (const child of directActivityChildren(root)) {
    matches.push(...collectActivityOccurrences(child, versionId));
  }
  return matches;
}

function mapActivityTree(node: ActivityNode, update: (activity: ActivityNode) => ActivityNode): ActivityNode {
  const updated = update(node);
  if (!updated.structure) return updated;
  let changed = false;
  const payload = mapNestedValue(updated.structure.payload, child => {
    const next = mapActivityTree(child, update);
    if (next !== child) changed = true;
    return next;
  }) as Record<string, unknown>;
  return changed ? { ...updated, structure: { ...updated.structure, payload } } : updated;
}

function mapNestedValue(value: unknown, update: (activity: ActivityNode) => ActivityNode): unknown {
  if (isActivityNode(value)) return update(value);
  if (Array.isArray(value)) {
    let changed = false;
    const next = value.map(item => {
      const mapped = mapNestedValue(item, update);
      if (mapped !== item) changed = true;
      return mapped;
    });
    return changed ? next : value;
  }
  if (!isRecord(value)) return value;
  let changed = false;
  const next: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(value)) {
    const mapped = mapNestedValue(item, update);
    next[key] = mapped;
    if (mapped !== item) changed = true;
  }
  return changed ? next : value;
}

function directActivityChildren(node: ActivityNode): ActivityNode[] {
  const children: ActivityNode[] = [];
  visitNestedValue(node.structure?.payload, child => children.push(child));
  return children;
}

function visitNestedValue(value: unknown, visit: (activity: ActivityNode) => void): void {
  if (isActivityNode(value)) {
    visit(value);
    return;
  }
  if (Array.isArray(value)) {
    value.forEach(item => visitNestedValue(item, visit));
    return;
  }
  if (isRecord(value)) Object.values(value).forEach(item => visitNestedValue(item, visit));
}

function collectAuthoredInputKeys(occurrence: ActivityNode, sourceContract?: ActivityContract): string[] {
  const keys = new Set<string>();
  for (const input of occurrence.inputs ?? []) {
    if (isRecord(input) && typeof input.referenceKey === "string") keys.add(input.referenceKey);
  }
  for (const input of sourceContract?.inputs ?? []) {
    const propertyName = camelize(input.referenceKey);
    if (Object.prototype.hasOwnProperty.call(occurrence, propertyName)) keys.add(input.referenceKey);
  }
  return [...keys].sort();
}

function collectConnectedOutcomeKeys(root: ActivityNode | null | undefined, occurrenceIds: Set<string>): string[] {
  if (!root) return [];
  const keys = new Set<string>();
  walkActivities(root, activity => {
    const connections = activity.structure?.payload.connections;
    if (!Array.isArray(connections)) return;
    for (const connection of connections) {
      if (!isRecord(connection) || !isRecord(connection.source)) continue;
      const nodeId = connection.source.nodeId;
      const port = connection.source.port;
      if (typeof nodeId === "string" && occurrenceIds.has(nodeId) && typeof port === "string") keys.add(port);
    }
  });
  return [...keys].sort();
}

function walkActivities(root: ActivityNode, visit: (activity: ActivityNode) => void): void {
  visit(root);
  directActivityChildren(root).forEach(child => walkActivities(child, visit));
}

function incompatibleMemberKeys(diff: ActivityVersionDiffView, memberKind: "input" | "outcome") {
  return new Set(diff.changes
    .filter(change =>
      change.subject.memberKind?.toLowerCase() === memberKind
      && change.impact.toLowerCase() === "breaking"
      && Boolean(change.subject.referenceKey))
    .map(change => change.subject.referenceKey!));
}

function contractKeys(contract: ActivityContract, collection: "inputs" | "outcomes") {
  return new Set(contract[collection].map(member => member.referenceKey));
}

function camelize(value: string) {
  return value ? value.charAt(0).toLowerCase() + value.slice(1) : value;
}

function isActivityNode(value: unknown): value is ActivityNode {
  return isRecord(value)
    && typeof value.nodeId === "string"
    && typeof value.activityVersionId === "string";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
