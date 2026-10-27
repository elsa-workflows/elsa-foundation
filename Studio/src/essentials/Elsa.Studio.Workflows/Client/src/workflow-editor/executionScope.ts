import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { browseActivityExecutions, getActivityExecutionInspection, getActivityExecutionLayout } from "../api/runtime";
import { findExecutableNodeFacts, type ExecutableActivityGraph } from "../executableGraph";
import type { ActivityExecutionInspection, ActivityExecutionLayout, ActivityExecutionStateSummary } from "../workflowTypes";

export interface ExecutionScopeEntry { execution: ActivityExecutionStateSummary; label: string }

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
    label?: string;
    labelFor(execution: ActivityExecutionStateSummary): string;
    signal: AbortSignal;
    isCurrent?(): boolean;
  }) {
  const { graph, signal } = options;
  const check = () => { if (signal.aborted || options.isCurrent?.() === false) throw new DOMException("Obsolete execution selection", "AbortError"); };
  check();
  if (execution.workflowExecutionId !== workflowExecutionId) throw new Error("Execution context identity did not match.");
  let includeSelectedScope = options.includeSelf;
  if (options.includeSelf && !findExecutableNodeFacts(graph, execution)) {
    const children = await browseActivityExecutions(context, workflowExecutionId, { parentActivityExecutionId: execution.activityExecutionId, limit: 1 }, signal);
    check();
    includeSelectedScope = children.totalCount > 0;
  }
  const scopes: ExecutionScopeEntry[] = [];
  const activities: ActivityExecutionStateSummary[] = [];
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
    if (!layout && inspection.boundary?.layoutAvailable) {
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
      const entry = siblings.items.find(value => value.execution.activityExecutionId === item.activityExecutionId
        && value.execution.workflowExecutionId === workflowExecutionId && value.execution.executableNodeId === item.executableNodeId);
      if (entry?.iterationPosition) label += ` · Iteration ${entry.iterationPosition}`;
    }
    if (includeSelectedScope || id !== execution.activityExecutionId) scopes.unshift({ execution: item, label });
    id = inspection.provenance.parentActivityExecutionId;
  }
  check();
  if (!findExecutableNodeFacts(graph, execution) && !layout) throw new Error("Pinned boundary structure is unavailable.");
  return { scopes, activities, layout };
}
