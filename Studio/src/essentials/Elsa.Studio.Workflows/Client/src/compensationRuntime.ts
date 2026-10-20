import { readCompensationSteps } from "./compensationScope";
import type { ActivityExecutionStateSummary, ActivityNode, IncidentStateSummary } from "./workflowTypes";

// What a run says about one compensation scope, derived from the executions and incidents the run view already
// loads (spec 250). Forward and handler executions are matched by their authored node ids within the scope's
// latest execution, so no compensation-specific endpoint is needed.

export const compensationSchedulingCause = "compensation";
export const compensatedOutcome = "Compensated";

export type CompensationForwardState = "NotLoaded" | "NotRun" | "Running" | "Completed" | "Faulted" | "Cancelled";
export type CompensationRecoveryState = "NotLoaded" | "NotApplicable" | "NotNeeded" | "Registered" | "Kept" | "Pending" | "Running" | "Compensated" | "Failed";
export type CompensationScopeRunPhase = "NotLoaded" | "NotStarted" | "Running" | "Compensating" | "Completed" | "Compensated" | "Faulted" | "Cancelled";

export interface CompensationRowRuntime {
  step: string;
  forward: { state: CompensationForwardState; activityExecutionId?: string };
  recovery: { state: CompensationRecoveryState; activityExecutionId?: string; order?: number };
}

export interface CompensationRecoveryCause {
  step: string;
  activityExecutionId: string;
  incidentId?: string;
  failureType?: string;
  message?: string;
}

export interface CompensationScopeRuntime {
  scopeActivityExecutionId?: string;
  phase: CompensationScopeRunPhase;
  rows: CompensationRowRuntime[];
  cause?: CompensationRecoveryCause;
  /** The run's activity list was a bounded page, so a missing execution may exist on a later page. */
  incomplete?: boolean;
}

export function isCompensationHandlerExecution(activity: Pick<ActivityExecutionStateSummary, "provenance">): boolean {
  return activity.provenance?.schedulingCause === compensationSchedulingCause;
}

// When `complete` is false the activities are one bounded page of the run: an execution that is not on it is
// reported as not loaded, never as not run, so a partial page cannot claim the scope or a step did not happen.
export function deriveCompensationRuntime(
  scope: ActivityNode,
  activities: ActivityExecutionStateSummary[],
  incidents: IncidentStateSummary[],
  { complete = true, selectedActivityExecutionId }: { complete?: boolean; selectedActivityExecutionId?: string | null } = {}
): CompensationScopeRuntime {
  const steps = readCompensationSteps(scope);
  // A scope inside a loop runs more than once: show the occurrence the operator selected (the scope itself, or one of
  // its steps or handlers), and the latest one otherwise.
  const scopeExecutions = activities.filter(activity => matchesNode(activity, scope.nodeId));
  const selected = selectedActivityExecutionId ? activities.find(activity => activity.activityExecutionId === selectedActivityExecutionId) : undefined;
  const scopeExecution = (selected && scopeExecutions.find(execution =>
      execution.activityExecutionId === selected.activityExecutionId || execution.activityExecutionId === parentOf(selected)))
    ?? latest(scopeExecutions);
  const children = scopeExecution
    ? activities.filter(activity => parentOf(activity) === scopeExecution.activityExecutionId)
    : [];

  const handlerExecutions = children
    .filter(isCompensationHandlerExecution)
    .sort((left, right) => (left.scheduledAt ?? "").localeCompare(right.scheduledAt ?? ""));
  const handlerOrder = new Map(handlerExecutions.map((activity, index) => [activity.activityExecutionId, index + 1]));

  const forwardFaulted = steps.flatMap((step, index) => {
    const forward = step.activity ? latest(children.filter(activity => matchesNode(activity, step.activity!.nodeId) && !isCompensationHandlerExecution(activity))) : undefined;
    return forward && forward.status === "Faulted" ? [{ index, forward }] : [];
  });
  const knownPhase = runPhase(scopeExecution, handlerExecutions.length > 0 || forwardFaulted.length > 0);
  // A running scope with no visible recovery may be recovering on a later page.
  const phase = !complete && (!scopeExecution || knownPhase === "Running") ? "NotLoaded" : knownPhase;
  const recovering = phase === "Compensating" || phase === "Compensated" || (phase === "Faulted" && handlerExecutions.length > 0);
  // Only a scope that completed normally or was cancelled settles that no handler ran; otherwise a handler missing from
  // a partial page may simply not be loaded.
  const handlerMayBeUnloaded = !complete && phase !== "Completed" && phase !== "Cancelled";

  const rows = steps.map((step): CompensationRowRuntime => {
    const forward = step.activity
      ? latest(children.filter(activity => matchesNode(activity, step.activity!.nodeId) && !isCompensationHandlerExecution(activity)))
      : undefined;
    const forwardState = !forward && !complete ? "NotLoaded" : forwardStateOf(forward);
    const handler = step.compensation
      ? latest(handlerExecutions.filter(activity => matchesNode(activity, step.compensation!.nodeId)))
      : undefined;

    return {
      step: step.name,
      forward: { state: forwardState, activityExecutionId: forward?.activityExecutionId },
      recovery: {
        state: forwardState === "Completed" && step.compensation && !handler && handlerMayBeUnloaded
          ? "NotLoaded"
          : recoveryStateOf(forwardState, !!step.compensation, handler, recovering, phase === "Completed" || phase === "Cancelled"),
        activityExecutionId: handler?.activityExecutionId,
        order: handler ? handlerOrder.get(handler.activityExecutionId) : undefined
      }
    };
  });

  const faulted = forwardFaulted[0];
  const cause = faulted && recovering
    ? causeOf(steps[faulted.index].name, faulted.forward, incidents)
    : undefined;

  return { scopeActivityExecutionId: scopeExecution?.activityExecutionId, phase, rows, cause, ...(complete ? {} : { incomplete: true }) };
}

export function forwardStateLabel(state: CompensationForwardState): string {
  switch (state) {
    case "NotLoaded": return "Not loaded";
    case "NotRun": return "Not run";
    case "Running": return "Running";
    case "Completed": return "Completed";
    case "Faulted": return "Faulted";
    case "Cancelled": return "Cancelled";
  }
}

export function recoveryStateLabel(state: CompensationRecoveryState): string {
  switch (state) {
    case "NotLoaded": return "Not loaded";
    case "NotApplicable": return "Not applicable";
    case "NotNeeded": return "Not needed";
    case "Registered": return "Can be undone";
    case "Kept": return "Kept";
    case "Pending": return "Waiting to undo";
    case "Running": return "Undoing";
    case "Compensated": return "Compensated";
    case "Failed": return "Undo failed";
  }
}

export function scopePhaseLabel(phase: CompensationScopeRunPhase): string {
  switch (phase) {
    case "NotLoaded": return "Not loaded";
    case "NotStarted": return "Not started";
    case "Running": return "Running steps";
    case "Compensating": return "Compensating";
    case "Completed": return "Completed";
    case "Compensated": return "Compensated";
    case "Faulted": return "Faulted";
    case "Cancelled": return "Cancelled";
  }
}

export function ordinalLabel(order: number): string {
  const lastTwo = order % 100;
  const suffix = lastTwo >= 11 && lastTwo <= 13 ? "th" : order % 10 === 1 ? "st" : order % 10 === 2 ? "nd" : order % 10 === 3 ? "rd" : "th";
  return `${order}${suffix}`;
}

function runPhase(scope: ActivityExecutionStateSummary | undefined, recoveryStarted: boolean): CompensationScopeRunPhase {
  if (!scope) return "NotStarted";
  switch (scope.status) {
    case "Completed": return scope.outcomeNames?.includes(compensatedOutcome) ? "Compensated" : "Completed";
    case "Faulted": return "Faulted";
    case "Cancelled": return "Cancelled";
    default: return recoveryStarted ? "Compensating" : "Running";
  }
}

function forwardStateOf(activity: ActivityExecutionStateSummary | undefined): CompensationForwardState {
  if (!activity) return "NotRun";
  if (activity.status === "Completed") return "Completed";
  if (activity.status === "Faulted") return "Faulted";
  if (activity.status === "Cancelled") return "Cancelled";
  return "Running";
}

function recoveryStateOf(
  forward: CompensationForwardState,
  hasHandler: boolean,
  handler: ActivityExecutionStateSummary | undefined,
  recovering: boolean,
  // The scope finished without recovery. M1 cannot undo it afterwards; late compensation is M3.
  finished: boolean
): CompensationRecoveryState {
  if (forward === "NotLoaded") return "NotLoaded";
  if (forward !== "Completed") return "NotApplicable";
  if (!hasHandler) return "NotNeeded";
  if (handler?.status === "Completed") return "Compensated";
  if (handler?.status === "Faulted") return "Failed";
  if (handler) return "Running";
  if (recovering) return "Pending";
  return finished ? "Kept" : "Registered";
}

function causeOf(step: string, forward: ActivityExecutionStateSummary, incidents: IncidentStateSummary[]): CompensationRecoveryCause {
  // The same association the run view uses: the incident's own execution id, or the runtime metadata copy of it.
  const incident = incidents.find(candidate =>
    (candidate.activityExecutionId?.trim() || candidate.metadata?.["runtime.activityExecutionId"]?.trim()) === forward.activityExecutionId);
  return {
    step,
    activityExecutionId: forward.activityExecutionId,
    incidentId: incident?.incidentId,
    failureType: incident?.failureType || undefined,
    message: incident?.message || undefined
  };
}

// Run lists carry the parent inside the scheduling provenance; single-execution reads also carry it top-level.
function parentOf(activity: ActivityExecutionStateSummary): string | null | undefined {
  return activity.parentActivityExecutionId ?? activity.provenance?.parentActivityExecutionId;
}

function matchesNode(activity: ActivityExecutionStateSummary, nodeId: string): boolean {
  return activity.authoredActivityId === nodeId || activity.executableNodeId === nodeId;
}

function latest(activities: ActivityExecutionStateSummary[]): ActivityExecutionStateSummary | undefined {
  return activities.reduce<ActivityExecutionStateSummary | undefined>(
    (current, candidate) => !current || (candidate.scheduledAt ?? "") >= (current.scheduledAt ?? "") ? candidate : current,
    undefined);
}
