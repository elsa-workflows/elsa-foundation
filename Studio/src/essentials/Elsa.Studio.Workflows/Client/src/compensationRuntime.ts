import { isCompensationScope, readCompensationSteps } from "./compensationScope";
import { getChildSlots } from "./workflowAdapter";
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
// Spec 258 (M3): the activities that confirm a scope's work or ask it to compensate, and the scope input that makes
// a completing scope confirm instead of handing its entries to the enclosing scope.
export const compensationScopeActivityType = "Elsa.Activities.Compensation.Activities.CompensationScope";
export const confirmActivityType = "Elsa.Activities.Compensation.Activities.Confirm";
export const compensateActivityType = "Elsa.Activities.Compensation.Activities.Compensate";
const confirmOnCompleteInput = "confirmOnComplete";
const targetScopeInput = "scope";

export type CompensationForwardState = "NotLoaded" | "NotRun" | "Running" | "Completed" | "Uncertain" | "Faulted" | "Cancelled";
export type CompensationRecoveryState =
  | "NotLoaded" | "NotApplicable" | "NotNeeded" | "Registered" | "Kept" | "Pending" | "Running" | "Compensated" | "Failed" | "NeedsAction"
  // Spec 258: confirmed work is never undone; a completed inner scope's work stays undoable by the enclosing scope.
  | "Confirmed" | "HandedOver";

/** A scope occurrence as the run view names it: the scope's execution and, inside a loop, its iteration. */
export interface CompensationScopeReference {
  scopeNodeId: string;
  label: string;
  activityExecutionId?: string;
  iterationId?: string;
}
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
    /** The Confirm (or the scope itself, with confirm on complete) that made the entry Confirmed. */
    confirmedBy?: string;
    /** The enclosing scope that can still undo the entry, when the state is HandedOver. */
    handedOverTo?: CompensationScopeReference;
  };
  /** Present on an entry this scope received from a completed inner scope: where the work was done. */
  origin?: CompensationScopeReference;
}

export interface CompensationScopeOccurrence {
  activityExecutionId: string;
  label: string;
  status: string;
  iterationId?: string;
}

export interface CompensationRecoveryCause {
  /** `uncertain` when the step could not tell whether its effect happened; `explicit` when a Compensate asked for it. */
  kind: "fault" | "uncertain" | "explicit";
  step: string;
  activityExecutionId: string;
  incidentId?: string;
  failureType?: string;
  message?: string;
  /** The Compensate activity's execution, when the kind is explicit. */
  requestedBy?: string;
}

export interface CompensationScopeRuntime {
  scopeActivityExecutionId?: string;
  phase: CompensationScopeRunPhase;
  rows: CompensationRowRuntime[];
  cause?: CompensationRecoveryCause;
  /** Entries handed over by completed inner scopes (spec 258), in the order this scope received them. */
  received?: CompensationRowRuntime[];
  /** Every occurrence of a scope that ran more than once, oldest first, with the shown one selected. */
  occurrences?: CompensationScopeOccurrence[];
  /** The run's activity list was a bounded page, so a missing execution may exist on a later page. */
  incomplete?: boolean;
}

export function isCompensationHandlerExecution(activity: Pick<ActivityExecutionStateSummary, "provenance">): boolean {
  return activity.provenance?.schedulingCause === compensationSchedulingCause;
}

// When `complete` is false the activities are one bounded page of the run: an execution that is not on it is
// reported as not loaded, never as not run, so a partial page cannot claim the scope or a step did not happen.
// `root` is the run's workflow graph: with it, a scope also shows the entries inner scopes handed it, Confirm and
// Compensate requests, and which enclosing scope can still undo its own work (spec 258).
export function deriveCompensationRuntime(
  scope: ActivityNode,
  activities: ActivityExecutionStateSummary[],
  incidents: IncidentStateSummary[],
  { complete = true, selectedActivityExecutionId, root }: { complete?: boolean; selectedActivityExecutionId?: string | null; root?: ActivityNode | null } = {}
): CompensationScopeRuntime {
  const steps = readCompensationSteps(scope);
  const run = new RunIndex(root ?? scope, scope, activities);
  // A scope inside a loop runs more than once: show the occurrence the operator selected (the scope itself, or one of
  // its steps or handlers), and the latest one otherwise.
  const scopeExecutions = activities
    .filter(activity => matchesNode(activity, scope.nodeId))
    .sort((left, right) => (left.scheduledAt ?? "").localeCompare(right.scheduledAt ?? ""));
  const selected = selectedActivityExecutionId ? activities.find(activity => activity.activityExecutionId === selectedActivityExecutionId) : undefined;
  const scopeExecution = (selected && scopeExecutions.find(execution =>
      execution.activityExecutionId === selected.activityExecutionId || execution.activityExecutionId === parentOf(selected)
      || run.nearestScope(selected)?.activityExecutionId === execution.activityExecutionId))
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
  // A Compensate that targeted this occurrence started its recovery (spec 258 FR-311).
  const request = scopeExecution ? run.compensateRequests(scopeExecution, scope).at(0) : undefined;
  const knownPhase = runPhase(scopeExecution, handlerExecutions.length > 0 || forwardFaulted.length > 0 || !!request);
  // A running scope with no visible recovery may be recovering on a later page.
  const phase0 = !complete && (!scopeExecution || knownPhase === "Running") ? "NotLoaded" : knownPhase;
  const recovering = phase0 === "Compensating" || phase0 === "Compensated" || (phase0 === "Faulted" && (handlerExecutions.length > 0 || !!request));
  // Only a scope that completed normally or was cancelled settles that no handler ran; otherwise a handler missing from
  // a partial page may simply not be loaded.
  const handlerMayBeUnloaded = !complete && phase0 !== "Completed" && phase0 !== "Cancelled";
  // An ended scope schedules nothing more, so a handler that has not run never will: the step is kept, not pending.
  const ended = phase0 === "Completed" || phase0 === "Cancelled" || phase0 === "Compensated" || phase0 === "Faulted";
  const confirmations = scopeExecution ? run.confirmations(scopeExecution, scope) : [];
  // A scope that completed Done hands its open entries to the nearest enclosing scope, unless it confirms them itself.
  const handedOverTo = scopeExecution && phase0 === "Completed" && !confirmsOnComplete(scope) ? run.enclosingScopeOf(scopeExecution) : undefined;

  const rows = steps.map((step): CompensationRowRuntime => {
    const forward = forwardOf(step);
    const forwardIncident = forward ? incidentOf(forward, incidents) : undefined;
    // A step that a Compensate stopped may have had its effect, so it is uncertain (spec 258 R9).
    const stopped = !!request && forward?.status === "Cancelled";
    const forwardState: CompensationForwardState = !forward && !complete
      ? "NotLoaded"
      : forward?.status === "Faulted" && isUncertain(forwardIncident) || stopped ? "Uncertain" : forwardStateOf(forward);
    // Every run of the handler, an operator retry's successor included, oldest first.
    const ownAttempts = step.compensation
      ? handlerExecutions.filter(activity => matchesNode(activity, step.compensation!.nodeId))
      : [];
    // A handed-over entry is undone by the enclosing scope: its handler runs there, scheduled from this forward step.
    const attempts = ownAttempts.length > 0 || !forward || !step.compensation ? ownAttempts : run.handlersOf(forward, step.compensation.nodeId);
    const handler = attempts.at(-1);
    const handlerIncident = handler?.status === "Faulted" ? incidentOf(handler, incidents) : undefined;
    const waitingIncident = isIntervention(handlerIncident) ? handlerIncident
      : !step.compensation && forwardState === "Uncertain" && isIntervention(forwardIncident) ? forwardIncident
      : undefined;
    const max = attempts.map(attempt => maxAttemptsOf(incidentOf(attempt, incidents))).find(value => value !== undefined);
    const confirmedBy = forward && !handler ? confirmingOf(confirmations, forward.completedAt ?? forward.scheduledAt, phase0 === "Completed" && confirmsOnComplete(scope) ? scopeExecution : undefined) : undefined;
    const elsewhere = ownAttempts.length === 0 && attempts.length > 0;

    let state: CompensationRecoveryState = waitingIncident
      ? "NeedsAction"
      : (forwardState === "Completed" || forwardState === "Uncertain") && step.compensation && !handler && handlerMayBeUnloaded
        ? "NotLoaded"
        : recoveryStateOf(forwardState, !!step.compensation, handler, handlerIncident, phase0 === "Compensating", ended && !elsewhere);
    if ((state === "Registered" || state === "Kept") && confirmedBy) state = "Confirmed";
    else if (state === "Kept" && handedOverTo) state = "HandedOver";
    // A stopped step that nothing can undo stays as it is: the scope faults with uncertain-unhandled.
    else if (state === "NotNeeded" && stopped) state = "Kept";

    return {
      step: step.name,
      forward: { state: forwardState, activityExecutionId: forward?.activityExecutionId },
      recovery: {
        state,
        activityExecutionId: handler?.activityExecutionId,
        order: handler && !elsewhere ? handlerOrder.get(attempts[0].activityExecutionId) : undefined,
        ...(attempts.length > 1 || max !== undefined ? { attempts: { count: attempts.length, ...(max !== undefined ? { max } : {}) } } : {}),
        ...(waitingIncident ? { incidentId: waitingIncident.incidentId } : {}),
        ...(state === "Confirmed" && confirmedBy ? { confirmedBy: confirmedBy.activityExecutionId } : {}),
        ...(state === "HandedOver" && handedOverTo ? { handedOverTo } : {})
      }
    };
  });

  // Entries that completed inner scopes handed to this occurrence, undone here when its recovery runs.
  const received = scopeExecution
    ? run.receivedEntries(scopeExecution).map((entry): CompensationRowRuntime => {
      const attempts = handlerExecutions.filter(activity => matchesNode(activity, entry.handlerNodeId)
        && schedulerOf(activity) === entry.forward.activityExecutionId);
      const handler = attempts.at(-1);
      const handlerIncident = handler?.status === "Faulted" ? incidentOf(handler, incidents) : undefined;
      const waitingIncident = isIntervention(handlerIncident) ? handlerIncident : undefined;
      const max = attempts.map(attempt => maxAttemptsOf(incidentOf(attempt, incidents))).find(value => value !== undefined);
      const confirmedBy = !handler ? confirmingOf(confirmations, entry.receivedAt, phase0 === "Completed" && confirmsOnComplete(scope) ? scopeExecution : undefined) : undefined;
      let state: CompensationRecoveryState = waitingIncident
        ? "NeedsAction"
        : !handler && handlerMayBeUnloaded
          ? "NotLoaded"
          : recoveryStateOf("Completed", true, handler, handlerIncident, phase0 === "Compensating", ended);
      if ((state === "Registered" || state === "Kept") && confirmedBy) state = "Confirmed";
      else if (state === "Kept" && handedOverTo) state = "HandedOver";
      return {
        step: entry.step,
        forward: { state: "Completed", activityExecutionId: entry.forward.activityExecutionId },
        recovery: {
          state,
          activityExecutionId: handler?.activityExecutionId,
          order: handler ? handlerOrder.get(attempts[0].activityExecutionId) : undefined,
          ...(attempts.length > 1 || max !== undefined ? { attempts: { count: attempts.length, ...(max !== undefined ? { max } : {}) } } : {}),
          ...(waitingIncident ? { incidentId: waitingIncident.incidentId } : {}),
          ...(state === "Confirmed" && confirmedBy ? { confirmedBy: confirmedBy.activityExecutionId } : {}),
          ...(state === "HandedOver" && handedOverTo ? { handedOverTo } : {})
        },
        origin: entry.origin
      };
    })
    : [];

  // A scope that is still running but blocked on an operator waits for intervention (spec 251 OD2).
  const allRows = [...rows, ...received];
  const phase: CompensationScopeRunPhase = (phase0 === "Running" || phase0 === "Compensating") && allRows.some(row => row.recovery.state === "NeedsAction")
    ? "AwaitingIntervention"
    : phase0;

  const faulted = forwardFaulted[0];
  const cause = faulted && (recovering || phase === "AwaitingIntervention")
    ? causeOf(steps[faulted.index].name, faulted.forward, incidents)
    : request && (recovering || phase === "AwaitingIntervention")
      ? explicitCauseOf(request, steps.find(step => forwardOf(step)?.status === "Cancelled")?.name)
      : undefined;

  const occurrences = scopeExecutions.length > 1
    ? scopeExecutions.map((execution, index): CompensationScopeOccurrence => ({
      activityExecutionId: execution.activityExecutionId,
      label: `Occurrence ${index + 1}${iterationOf(execution) ? ` · iteration ${iterationOf(execution)}` : ""}`,
      status: execution.status,
      ...(iterationOf(execution) ? { iterationId: iterationOf(execution)! } : {})
    }))
    : undefined;

  return {
    scopeActivityExecutionId: scopeExecution?.activityExecutionId,
    phase,
    rows,
    cause,
    ...(received.length > 0 ? { received } : {}),
    ...(occurrences ? { occurrences } : {}),
    ...(complete ? {} : { incomplete: true })
  };
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
    case "Confirmed": return "Confirmed";
    case "HandedOver": return "Compensable by the enclosing scope";
  }
}

// The row's recovery label, naming the scope that can still undo a handed-over entry.
export function recoveryLabel(recovery: CompensationRowRuntime["recovery"]): string {
  return recovery.state === "HandedOver" && recovery.handedOverTo
    ? `Compensable by ${recovery.handedOverTo.label}`
    : recoveryStateLabel(recovery.state);
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

// Spec 258: the run's scope occurrences, located through the workflow graph and the executions' parent chain.
interface ReceivedEntry {
  step: string;
  forward: ActivityExecutionStateSummary;
  handlerNodeId: string;
  origin: CompensationScopeReference;
  receivedAt: string;
}

class RunIndex {
  private readonly design = new Map<string, ActivityNode>();
  private readonly byId: Map<string, ActivityExecutionStateSummary>;

  constructor(root: ActivityNode, scope: ActivityNode, private readonly activities: ActivityExecutionStateSummary[]) {
    const visit = (node: ActivityNode | null | undefined) => {
      if (!node || this.design.has(node.nodeId)) return;
      this.design.set(node.nodeId, node);
      for (const slot of getChildSlots(node)) slot.activities.forEach(visit);
    };
    visit(root);
    visit(scope);
    this.byId = new Map(activities.map(activity => [activity.activityExecutionId, activity]));
  }

  isScope(activity: ActivityExecutionStateSummary): boolean {
    return activity.activityType === compensationScopeActivityType || isCompensationScope(this.designOf(activity));
  }

  designOf(activity: ActivityExecutionStateSummary): ActivityNode | undefined {
    return this.design.get(nodeKey(activity) ?? "") ?? this.design.get(activity.executableNodeId);
  }

  // The nearest compensation scope occurrence above an execution, the one a hand-over, Confirm or Compensate reaches.
  nearestScope(activity: ActivityExecutionStateSummary): ActivityExecutionStateSummary | undefined {
    return this.ancestors(activity).find(ancestor => this.isScope(ancestor));
  }

  enclosingScopeOf(scope: ActivityExecutionStateSummary): CompensationScopeReference | undefined {
    const enclosing = this.nearestScope(scope);
    return enclosing ? this.reference(enclosing) : undefined;
  }

  reference(scope: ActivityExecutionStateSummary): CompensationScopeReference {
    const node = this.designOf(scope);
    const iterationId = iterationOf(scope);
    return {
      scopeNodeId: nodeKey(scope) ?? scope.executableNodeId,
      label: node ? designLabel(node) : nodeKey(scope) ?? scope.executableNodeId,
      activityExecutionId: scope.activityExecutionId,
      ...(iterationId ? { iterationId } : {})
    };
  }

  // Confirms that reached this occurrence: the nearest scope of a Confirm, or the scope it names.
  confirmations(scope: ActivityExecutionStateSummary, scopeNode: ActivityNode): ActivityExecutionStateSummary[] {
    return this.requestsFor(scope, scopeNode, confirmActivityType).filter(activity => activity.status === "Completed");
  }

  compensateRequests(scope: ActivityExecutionStateSummary, scopeNode: ActivityNode): ActivityExecutionStateSummary[] {
    return this.requestsFor(scope, scopeNode, compensateActivityType);
  }

  // Every run of a handed-over entry's handler, wherever it ran: it is scheduled from the entry's forward step.
  handlersOf(forward: ActivityExecutionStateSummary, handlerNodeId: string): ActivityExecutionStateSummary[] {
    return this.activities
      .filter(activity => isCompensationHandlerExecution(activity) && matchesNode(activity, handlerNodeId) && schedulerOf(activity) === forward.activityExecutionId)
      .sort((left, right) => (left.scheduledAt ?? "").localeCompare(right.scheduledAt ?? ""));
  }

  // The open entries completed inner scopes handed to this occurrence, transitively, oldest hand-over first.
  receivedEntries(scope: ActivityExecutionStateSummary): ReceivedEntry[] {
    return this.activities
      .filter(activity => this.isScope(activity) && this.nearestScope(activity)?.activityExecutionId === scope.activityExecutionId)
      .filter(inner => inner.status === "Completed" && !inner.outcomeNames?.includes(compensatedOutcome))
      .sort((left, right) => (left.completedAt ?? left.scheduledAt ?? "").localeCompare(right.completedAt ?? right.scheduledAt ?? ""))
      .flatMap(inner => {
        const node = this.designOf(inner);
        if (!node || confirmsOnComplete(node)) return [];
        const receivedAt = inner.completedAt ?? inner.scheduledAt ?? "";
        const confirmations = this.confirmations(inner, node);
        const own = readCompensationSteps(node).flatMap((step): ReceivedEntry[] => {
          if (!step.activity || !step.compensation) return [];
          const forward = latest(this.activities.filter(activity => parentOf(activity) === inner.activityExecutionId
            && matchesNode(activity, step.activity!.nodeId) && !isCompensationHandlerExecution(activity)));
          if (!forward || forward.status !== "Completed") return [];
          return [{ step: step.name, forward, handlerNodeId: step.compensation.nodeId, origin: this.reference(inner), receivedAt: forward.completedAt ?? forward.scheduledAt ?? "" }];
        });
        return [...own, ...this.receivedEntries(inner)]
          .filter(entry => !confirmingOf(confirmations, entry.receivedAt))
          .map(entry => ({ ...entry, receivedAt }));
      });
  }

  private requestsFor(scope: ActivityExecutionStateSummary, scopeNode: ActivityNode, activityType: string): ActivityExecutionStateSummary[] {
    return this.activities
      .filter(activity => activity.activityType === activityType)
      .filter(activity => {
        const nearest = this.nearestScope(activity);
        if (nearest?.activityExecutionId === scope.activityExecutionId) return true;
        const target = literalInput(this.designOf(activity), targetScopeInput);
        return target === scopeNode.nodeId && this.ancestors(activity).some(ancestor => ancestor.activityExecutionId === scope.activityExecutionId);
      })
      .sort((left, right) => (left.scheduledAt ?? "").localeCompare(right.scheduledAt ?? ""));
  }

  private ancestors(activity: ActivityExecutionStateSummary): ActivityExecutionStateSummary[] {
    const result: ActivityExecutionStateSummary[] = [];
    const seen = new Set<string>([activity.activityExecutionId]);
    let parentId = parentOf(activity);
    while (parentId && !seen.has(parentId)) {
      seen.add(parentId);
      const parent = this.byId.get(parentId);
      if (!parent) break;
      result.push(parent);
      parentId = parentOf(parent);
    }
    return result;
  }
}

// The Confirm that confirmed an entry available at `availableAt`: the first one that completed after it. A scope
// that confirms on completion confirms everything it still holds.
function confirmingOf(
  confirmations: ActivityExecutionStateSummary[],
  availableAt: string | null | undefined,
  confirmedOnComplete?: ActivityExecutionStateSummary
): ActivityExecutionStateSummary | undefined {
  return confirmations.find(confirm => (availableAt ?? "") <= (confirm.completedAt ?? confirm.scheduledAt ?? "")) ?? confirmedOnComplete;
}

function confirmsOnComplete(scope: ActivityNode): boolean {
  const value = literalInput(scope, confirmOnCompleteInput);
  return value === true || value === "true" || value === "True";
}

function explicitCauseOf(request: ActivityExecutionStateSummary, stoppedStep: string | undefined): CompensationRecoveryCause {
  return { kind: "explicit", step: stoppedStep ?? "", activityExecutionId: request.activityExecutionId, requestedBy: request.activityExecutionId };
}

// A literal input value of a design node; an expression is resolved only at run time, so it reads as absent.
function literalInput(node: ActivityNode | undefined, referenceKey: string): unknown {
  const input = node?.inputs?.find((candidate): candidate is { referenceKey: string; value?: { expressionType?: string; value?: unknown } } =>
    !!candidate && typeof candidate === "object" && (candidate as { referenceKey?: unknown }).referenceKey === referenceKey);
  const value = input?.value;
  if (!value || (value.expressionType && value.expressionType.toLowerCase() !== "literal")) return undefined;
  return typeof value.value === "string" ? value.value.trim() || undefined : value.value;
}

function designLabel(node: ActivityNode): string {
  for (const key of ["displayName", "name", "description"]) {
    const value = node[key];
    if (typeof value === "string" && value.trim()) return value.trim();
  }
  return node.nodeId;
}

function schedulerOf(activity: ActivityExecutionStateSummary): string | null | undefined {
  return activity.schedulingActivityExecutionId ?? activity.provenance?.schedulingActivityExecutionId;
}

function iterationOf(activity: ActivityExecutionStateSummary): string | undefined {
  return activity.iterationId ?? activity.provenance?.iterationId ?? undefined;
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
