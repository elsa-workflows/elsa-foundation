import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { browseActivityExecutions, getActivityExecutionInspection, getActivityExecutionLayout, type ActivityExecutionBrowsePage } from "../api/runtime";
import { findExecutableGraphNodeId, findExecutableNodeFacts, type ExecutableActivityGraph } from "../executableGraph";
import { findActivityNode, type ActivityCatalogLookup } from "../workflowAdapter";
import type { ActivityExecutionInspection, ActivityExecutionLayout, ActivityExecutionStateSummary, ActivityNode } from "../workflowTypes";

export interface ExecutionScopeEntry { execution: ActivityExecutionStateSummary; label: string }

function isStructuralDescendant(
  root: ActivityNode | null | undefined,
  ancestorNodeId: string | null | undefined,
  targetNodeId: string | null | undefined,
  catalog?: ActivityCatalogLookup
): boolean {
  if (!root || !ancestorNodeId || !targetNodeId || ancestorNodeId === targetNodeId) return false;
  const ancestor = findActivityNode(root, ancestorNodeId, catalog);
  if (!ancestor) return false;
  return Boolean(findActivityNode(ancestor, targetNodeId, catalog));
}

function isStructuralAncestorOrSelf(
  root: ActivityNode | null | undefined,
  ancestorNodeId: string | null | undefined,
  targetNodeId: string | null | undefined,
  catalog?: ActivityCatalogLookup
): boolean {
  if (!root || !ancestorNodeId || !targetNodeId) return false;
  if (ancestorNodeId === targetNodeId) return true;
  return isStructuralDescendant(root, ancestorNodeId, targetNodeId, catalog);
}

/** Keeps the longest execution-scope prefix whose node contains (or is) the target node in the outer graph. */
export function trimExecutionScopesForTarget(
  scopes: readonly ExecutionScopeEntry[],
  root: ActivityNode | null | undefined,
  graph: ExecutableActivityGraph | null | undefined,
  catalog: ActivityCatalogLookup,
  targetNodeId: string | null | undefined
): ExecutionScopeEntry[] {
  if (scopes.length === 0 || !root || !targetNodeId || !findActivityNode(root, targetNodeId, catalog)) {
    return [...scopes];
  }
  for (let index = scopes.length - 1; index >= 0; index--) {
    const scopeNodeId = findExecutableGraphNodeId(graph, scopes[index].execution) ?? scopes[index].execution.authoredActivityId;
    if (isStructuralAncestorOrSelf(root, scopeNodeId, targetNodeId, catalog)) {
      return scopes.slice(0, index + 1);
    }
  }
  return [];
}

/** Resolves the deepest recorded scope execution that is a strict ancestor of the inspected node. */
export function resolveExecutionScopeAncestorId(
  scopes: readonly ExecutionScopeEntry[],
  root: ActivityNode | null | undefined,
  graph: ExecutableActivityGraph | null | undefined,
  catalog: ActivityCatalogLookup,
  targetNodeId: string | null | undefined,
  exactExecutionId: string | undefined
): string | undefined {
  if (scopes.length === 0) return undefined;
  if (exactExecutionId) {
    const exactIndex = scopes.findIndex(scope => scope.execution.activityExecutionId === exactExecutionId);
    if (exactIndex >= 0) return scopes[exactIndex - 1]?.execution.activityExecutionId;
  }
  if (!root || !targetNodeId || !findActivityNode(root, targetNodeId, catalog)) {
    return scopes.at(-1)?.execution.activityExecutionId;
  }
  for (let index = scopes.length - 1; index >= 0; index--) {
    const scopeNodeId = findExecutableGraphNodeId(graph, scopes[index].execution) ?? scopes[index].execution.authoredActivityId;
    if (isStructuralDescendant(root, scopeNodeId, targetNodeId, catalog)) {
      return scopes[index].execution.activityExecutionId;
    }
  }
  return undefined;
}

/** Finds the innermost scope entry matching an executable node ID, along with its index. */
export function findMatchingExecutionScope(
  scopes: readonly ExecutionScopeEntry[],
  executableNodeId: string | null | undefined
): { index: number; entry: ExecutionScopeEntry } | undefined {
  if (!executableNodeId) return undefined;
  for (let index = scopes.length - 1; index >= 0; index--) {
    if (scopes[index].execution.executableNodeId === executableNodeId) {
      return { index, entry: scopes[index] };
    }
  }
  return undefined;
}

/** Returns whether two execution-scope chains contain the same execution IDs and labels in order. */
export function sameExecutionScopes(
  left: readonly ExecutionScopeEntry[],
  right: readonly ExecutionScopeEntry[]
): boolean {
  return left.length === right.length && left.every((item, index) =>
    item.execution.activityExecutionId === right[index].execution.activityExecutionId
    && item.label === right[index].label);
}

/**
 * Synchronously resolves the execution-scope chain when the target sits in the outer graph and its
 * ancestry is already known from active scopes or loaded execution summaries (with no unlabelled
 * loop-iteration ancestors). Returns null when an asynchronous server lookup is still required.
 */
export function resolveKnownExecutionScope(
  workflowExecutionId: string,
  execution: ActivityExecutionStateSummary,
  options: {
    graph: ExecutableActivityGraph | null | undefined;
    includeSelf: boolean;
    label?: string;
    currentScopes: readonly ExecutionScopeEntry[];
    knownActivities: readonly ActivityExecutionStateSummary[];
    labelFor(execution: ActivityExecutionStateSummary): string;
  }
): ExecutionScopeEntry[] | null {
  if (execution.workflowExecutionId !== workflowExecutionId) return null;
  if (!findExecutableNodeFacts(options.graph, execution)) return null;
  if (options.includeSelf) {
    const index = options.currentScopes.findIndex(scope => scope.execution.activityExecutionId === execution.activityExecutionId);
    if (index >= 0 && (!options.label || options.currentScopes[index].label === options.label)) {
      return options.currentScopes.slice(0, index + 1);
    }
    return null;
  }
  if (execution.parentActivityExecutionId === undefined) return null;
  if (execution.parentActivityExecutionId === null) return [];

  const knownById = new Map<string, ActivityExecutionStateSummary>();
  for (const item of options.knownActivities) {
    if (item.workflowExecutionId === workflowExecutionId) knownById.set(item.activityExecutionId, item);
  }
  const visited = new Set<string>([execution.activityExecutionId]);
  const tail: ExecutionScopeEntry[] = [];
  let id: string | null | undefined = execution.parentActivityExecutionId;
  while (id) {
    if (visited.has(id) || visited.size >= 256) return null;
    visited.add(id);
    const activeIndex = options.currentScopes.findIndex(scope => scope.execution.activityExecutionId === id);
    if (activeIndex >= 0) return [...options.currentScopes.slice(0, activeIndex + 1), ...tail];
    const item = knownById.get(id);
    if (!item || item.parentActivityExecutionId === undefined || item.iterationId) return null;
    tail.unshift({ execution: item, label: options.labelFor(item) });
    id = item.parentActivityExecutionId;
  }
  return tail;
}

export type IncidentActivityInspection = Pick<ActivityExecutionInspection,
  | "activityExecutionId"
  | "workflowExecutionId"
  | "executableNodeId"
  | "authoredActivityId"
  | "activityType"
  | "activityTypeVersion"
  | "status"
  | "subStatus"
  | "scheduledAt"
  | "startedAt"
  | "completedAt"
  | "bookmarks"
  | "incidents"
  | "metadata"
>;

export function activityExecutionSummaryFromInspection(inspection: IncidentActivityInspection): ActivityExecutionStateSummary {
  const bookmarks = Array.isArray(inspection.bookmarks) ? inspection.bookmarks : null;
  const incidents = Array.isArray(inspection.incidents) ? inspection.incidents : null;
  return {
    activityExecutionId: inspection.activityExecutionId,
    workflowExecutionId: inspection.workflowExecutionId,
    executableNodeId: inspection.executableNodeId,
    authoredActivityId: inspection.authoredActivityId,
    activityType: inspection.activityType,
    activityTypeVersion: inspection.activityTypeVersion,
    status: inspection.status,
    subStatus: inspection.subStatus,
    scheduledAt: inspection.scheduledAt,
    startedAt: inspection.startedAt,
    completedAt: inspection.completedAt,
    bookmarkCount: bookmarks?.length ?? null,
    bookmarkIds: bookmarks?.map(bookmark => bookmark.bookmarkId),
    incidentCount: incidents?.length ?? null,
    incidentIds: incidents?.map(incident => incident.incidentId),
    metadata: inspection.metadata
  };
}

/** Resolve recorded ancestry for both the run viewer and attached editor inspection. */
export async function loadExecutionScope(context: StudioEndpointContext, workflowExecutionId: string,
  execution: ActivityExecutionStateSummary, options: {
    graph: ExecutableActivityGraph | null | undefined;
    includeSelf: boolean;
    loadBoundaryLayout?: boolean;
    label?: string;
    labelFor(execution: ActivityExecutionStateSummary): string;
    signal: AbortSignal;
    isCurrent?(): boolean;
  }) {
  const { graph, signal } = options;
  const check = () => { if (signal.aborted || options.isCurrent?.() === false) throw new DOMException("Obsolete execution selection", "AbortError"); };
  check();
  if (execution.workflowExecutionId !== workflowExecutionId) throw new Error("Execution context identity did not match.");
  const inOuterGraph = Boolean(findExecutableNodeFacts(graph, execution));
  const shouldLoadBoundaryLayout = options.loadBoundaryLayout ?? !inOuterGraph;
  let includeSelectedScope = options.includeSelf;
  if (options.includeSelf && !inOuterGraph) {
    const children = await browseActivityExecutions(context, workflowExecutionId, { parentActivityExecutionId: execution.activityExecutionId, limit: 1 }, signal);
    check();
    includeSelectedScope = children.totalCount > 0;
  }
  const scopes: ExecutionScopeEntry[] = [];
  const activities: ActivityExecutionStateSummary[] = [];
  const inspections: ActivityExecutionInspection[] = [];
  const pages = new Map<string, ActivityExecutionBrowsePage>();
  let layout: ActivityExecutionLayout | null = null;
  let id: string | null | undefined = execution.activityExecutionId;
  const visited = new Set<string>();
  while (id) {
    check();
    if (visited.has(id) || visited.size >= 256) throw new Error("Recorded execution ancestry is incomplete or cyclic.");
    visited.add(id);
    const inspection = await getActivityExecutionInspection(context, workflowExecutionId, id, signal);
    check();
    if (inspection.activityExecutionId !== id || inspection.workflowExecutionId !== workflowExecutionId) throw new Error("Execution context identity did not match.");
    inspections.push(inspection);
    if (shouldLoadBoundaryLayout && !layout && inspection.boundary?.layoutAvailable) {
      layout = await getActivityExecutionLayout(context, workflowExecutionId, id, signal);
      check();
      if (layout.activityExecutionId !== id || layout.workflowExecutionId !== workflowExecutionId) throw new Error("Pinned boundary identity did not match.");
    }
    const item = { ...activityExecutionSummaryFromInspection(inspection), executionSequence: inspection.executionSequence,
      provenance: inspection.provenance, parentActivityExecutionId: inspection.provenance.parentActivityExecutionId,
      iterationId: inspection.provenance.iterationId };
    activities.push(item);
    let label = id === execution.activityExecutionId && options.label ? options.label : options.labelFor(item);
    if (item.iterationId && !(inspection.attempt?.attemptNumber && inspection.attempt.attemptNumber > 1) && !options.label) {
      const siblings = await browseActivityExecutions(context, workflowExecutionId, { executableNodeId: item.executableNodeId,
        parentActivityExecutionId: item.parentActivityExecutionId ?? undefined, anchorActivityExecutionId: item.activityExecutionId, limit: 1 }, signal);
      check();
      pages.set(item.activityExecutionId, siblings);
      const entry = siblings.items.find(value => value.execution.activityExecutionId === item.activityExecutionId
        && value.execution.workflowExecutionId === workflowExecutionId && value.execution.executableNodeId === item.executableNodeId);
      if (entry?.iterationPosition) label += ` · Iteration ${entry.iterationPosition}`;
    }
    if (includeSelectedScope || id !== execution.activityExecutionId) scopes.unshift({ execution: item, label });
    id = inspection.provenance.parentActivityExecutionId;
  }
  check();
  if (!inOuterGraph && !layout) throw new Error("Pinned boundary structure is unavailable.");
  return { scopes, activities, inspections, pages, layout };
}
