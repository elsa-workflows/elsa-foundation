import { readCompensationSteps } from "./compensationScope";
import type { ActivityExecutionStateSummary, ActivityNode, IncidentStateSummary } from "./workflowTypes";

// What a run says about one compensation scope, derived from the executions and incidents the run view already
// loads (spec 250). Forward and handler executions are matched by their authored node ids within the scope's
// latest execution, so no compensation-specific endpoint is needed.

export const compensationSchedulingCause = "compensation";
export const compensatedOutcome = "Compensated";
// Incident evidence the scope leaves for M2 states (spec 251): an uncertain forward outcome and an intervention wait.
const forwardOutcomeKey = "compensation.forwardOutcome";
const maxAttemptsKey = "compensation.maxAttempts";
const structuralInterventionSource = "StructuralIntervention";

export type CompensationForwardState = "NotLoaded" | "NotRun" | "Running" | "Completed" | "Uncertain" | "Faulted" | "Cancelled";
export type CompensationRecoveryState = "NotLoaded" | "NotApplicable" | "NotNeeded" | "Registered" | "Kept" | "Pending" | "Running" | "Compensated" | "Failed" | "NeedsAction";
export type CompensationScopeRunPhase = "NotLoaded" | "NotStarted" | "Running" | "Compensating" | "AwaitingIntervention" | "Completed" | "Compensated" | "Faulted" | "Cancelled";

export interface CompensationRowRuntime {
  step: string;
  forward: { state: CompensationForwardState; activityExecutionId?: string };
  recovery: {
    state: CompensationRecoveryState;
    activityExecutionId?: string;
    order?: number;
    /** How many times the handler ran, and the scope's limit when the run data names it. */
    attempts?: { count: number; max?: number };
    /** The blocking incident an operator must act on, when the state is NeedsAction. */
    incidentId?: string;
  };
}

export interface CompensationRecoveryCause {
  /** `uncertain` when the step could not tell whether its effect happened. */
  kind: "fault" | "uncertain";
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
  // Recovery order ranks entries by their handler's first attempt, so retries do not shift it.
  const firstAttempts = handlerExecutions.filter((activity, index, all) => all.findIndex(other => nodeKey(other) === nodeKey(activity)) === index);
  const handlerOrder = new Map(firstAttempts.map((activity, index) => [activity.activityExecutionId, index + 1]));

  const forwardOf = (step: (typeof steps)[number]) => step.activity
    ? latest(children.filter(activity => matchesNode(activity, step.activity!.nodeId) && !isCompensationHandlerExecution(activity)))
    : undefined;
  const forwardFaulted = steps.flatMap((step, index) => {
    const forward = forwardOf(step);
    return forward && forward.status === "Faulted" ? [{ index, forward }] : [];
  });
  const knownPhase = runPhase(scopeExecution, handlerExecutions.length > 0 || forwardFaulted.length > 0);
  // A running scope with no visible recovery may be recovering on a later page.
  const phase0 = !complete && (!scopeExecution || knownPhase === "Running") ? "NotLoaded" : knownPhase;
  const recovering = phase0 === "Compensating" || phase0 === "Compensated" || (phase0 === "Faulted" && handlerExecutions.length > 0);
  // Only a scope that completed normally or was cancelled settles that no handler ran; otherwise a handler missing from
  // a partial page may simply not be loaded.
  const handlerMayBeUnloaded = !complete && phase0 !== "Completed" && phase0 !== "Cancelled";
  // An ended scope schedules nothing more, so a handler that has not run never will: the step is kept, not pending.
  const ended = phase0 === "Completed" || phase0 === "Cancelled" || phase0 === "Compensated" || phase0 === "Faulted";

  const rows = steps.map((step): CompensationRowRuntime => {
    const forward = forwardOf(step);
    const forwardIncident = forward ? incidentOf(forward, incidents) : undefined;
    const forwardState: CompensationForwardState = !forward && !complete
      ? "NotLoaded"
      : forward?.status === "Faulted" && isUncertain(forwardIncident) ? "Uncertain" : forwardStateOf(forward);
    // Every run of the handler, an operator retry's successor included, oldest first.
    const attempts = step.compensation
      ? handlerExecutions.filter(activity => matchesNode(activity, step.compensation!.nodeId))
      : [];
    const handler = attempts.at(-1);
    const handlerIncident = handler?.status === "Faulted" ? incidentOf(handler, incidents) : undefined;
    const waitingIncident = isIntervention(handlerIncident) ? handlerIncident
      : !step.compensation && forwardState === "Uncertain" && isIntervention(forwardIncident) ? forwardIncident
      : undefined;
    const max = attempts.map(attempt => maxAttemptsOf(incidentOf(attempt, incidents))).find(value => value !== undefined);

    return {
      step: step.name,
      forward: { state: forwardState, activityExecutionId: forward?.activityExecutionId },
      recovery: {
        state: waitingIncident
          ? "NeedsAction"
          : (forwardState === "Completed" || forwardState === "Uncertain") && step.compensation && !handler && handlerMayBeUnloaded
            ? "NotLoaded"
            : recoveryStateOf(forwardState, !!step.compensation, handler, handlerIncident, phase0 === "Compensating", ended),
        activityExecutionId: handler?.activityExecutionId,
        order: handler ? handlerOrder.get(attempts[0].activityExecutionId) : undefined,
        ...(attempts.length > 1 || max !== undefined ? { attempts: { count: attempts.length, ...(max !== undefined ? { max } : {}) } } : {}),
        ...(waitingIncident ? { incidentId: waitingIncident.incidentId } : {})
      }
    };
  });

  // A scope that is still running but blocked on an operator waits for intervention (spec 251 OD2).
  const phase: CompensationScopeRunPhase = (phase0 === "Running" || phase0 === "Compensating") && rows.some(row => row.recovery.state === "NeedsAction")
    ? "AwaitingIntervention"
    : phase0;

  const faulted = forwardFaulted[0];
  const cause = faulted && (recovering || phase === "AwaitingIntervention")
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
    case "Uncertain": return "Uncertain";
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
    case "NeedsAction": return "Needs action";
  }
}

export function scopePhaseLabel(phase: CompensationScopeRunPhase): string {
  switch (phase) {
    case "NotLoaded": return "Not loaded";
    case "NotStarted": return "Not started";
    case "Running": return "Running steps";
    case "Compensating": return "Compensating";
    case "AwaitingIntervention": return "Needs action";
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
  handlerIncident: IncidentStateSummary | undefined,
  compensating: boolean,
  // The scope ended without running this handler. M1 cannot undo it afterwards; late compensation is M3.
  ended: boolean
): CompensationRecoveryState {
  if (forward === "NotLoaded") return "NotLoaded";
  // An uncertain step may have had its effect, so it is undone like a completed one (spec 251 FR-207).
  if (forward !== "Completed" && forward !== "Uncertain") return "NotApplicable";
  if (!hasHandler) return "NotNeeded";
  if (handler?.status === "Completed") return "Compensated";
  // A faulted attempt the scope absorbed is retried next; one it left blocking failed.
  if (handler?.status === "Faulted") return handlerIncident?.resolutionOutcome?.actionKind === "AbsorbFault" && !ended ? "Pending" : "Failed";
  if (handler) return "Running";
  if (compensating) return "Pending";
  return ended ? "Kept" : "Registered";
}

function causeOf(step: string, forward: ActivityExecutionStateSummary, incidents: IncidentStateSummary[]): CompensationRecoveryCause {
  const incident = incidentOf(forward, incidents);
  return {
    kind: isUncertain(incident) ? "uncertain" : "fault",
    step,
    activityExecutionId: forward.activityExecutionId,
    incidentId: incident?.incidentId,
    failureType: incident?.failureType || undefined,
    message: incident?.message || undefined
  };
}

// The same association the run view uses: the incident's own execution id, or the runtime metadata copy of it. The
// newest incident wins, so a retried attempt's earlier incident does not mask its latest one.
function incidentOf(activity: ActivityExecutionStateSummary, incidents: IncidentStateSummary[]): IncidentStateSummary | undefined {
  return incidents
    .filter(candidate => (candidate.activityExecutionId?.trim() || candidate.metadata?.["runtime.activityExecutionId"]?.trim()) === activity.activityExecutionId)
    .reduce<IncidentStateSummary | undefined>((current, candidate) => !current || candidate.createdAt >= current.createdAt ? candidate : current, undefined);
}

function isUncertain(incident: IncidentStateSummary | undefined): boolean {
  return incident?.resolutionOutcome?.metadata?.[forwardOutcomeKey] === "unknown";
}

function isIntervention(incident: IncidentStateSummary | undefined): incident is IncidentStateSummary {
  return !!incident && incident.isBlocking && incident.resolutionOutcome?.systemSource === structuralInterventionSource;
}

function maxAttemptsOf(incident: IncidentStateSummary | undefined): number | undefined {
  const value = Number(incident?.resolutionOutcome?.metadata?.[maxAttemptsKey]);
  return Number.isInteger(value) && value > 0 ? value : undefined;
}

// Run lists carry the parent inside the scheduling provenance; single-execution reads also carry it top-level.
function parentOf(activity: ActivityExecutionStateSummary): string | null | undefined {
  return activity.parentActivityExecutionId ?? activity.provenance?.parentActivityExecutionId;
}

function nodeKey(activity: ActivityExecutionStateSummary): string | undefined {
  return activity.authoredActivityId ?? activity.executableNodeId ?? undefined;
}

function matchesNode(activity: ActivityExecutionStateSummary, nodeId: string): boolean {
  return activity.authoredActivityId === nodeId || activity.executableNodeId === nodeId;
}

function latest(activities: ActivityExecutionStateSummary[]): ActivityExecutionStateSummary | undefined {
  return activities.reduce<ActivityExecutionStateSummary | undefined>(
    (current, candidate) => !current || (candidate.scheduledAt ?? "") >= (current.scheduledAt ?? "") ? candidate : current,
    undefined);
}
