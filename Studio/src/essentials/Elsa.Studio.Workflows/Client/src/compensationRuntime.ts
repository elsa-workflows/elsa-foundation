import { isCompensationScope, readCompensationSteps } from "./compensationScope";
import { getChildSlots } from "./workflowAdapter";
import type { ActivityExecutionStateSummary, ActivityNode, ActivityStateView, IncidentStateSummary } from "./workflowTypes";

// What a run says about one compensation scope, derived from the executions and incidents the run view already
// loads (spec 250). Forward and handler executions are matched by their authored node ids within the scope's
// latest execution, so no compensation-specific endpoint is needed.

export const compensationSchedulingCause = "compensation";
export const compensatedOutcome = "Compensated";
// Incident evidence the scope leaves for M2 states (spec 251): an uncertain forward outcome and an intervention wait.
const forwardOutcomeKey = "compensation.forwardOutcome";
const maxAttemptsKey = "compensation.maxAttempts";
const structuralInterventionSource = "StructuralIntervention";
// Spec 260 (M4): what a wait offers, who asked for it, and what an operator decided, as the runtime records them.
const interventionDecisionsKey = "runtime.interventionDecisions";
const interventionRequestedByKey = "runtime.interventionRequestedBy";
const interventionDecisionKey = "runtime.interventionDecision";
const alterationSubmittedByKey = "runtime.alteration.submittedBy";
const alterationReasonKey = "runtime.alteration.reason";
export const operatorRetryActionKind = "OperatorRetry";
export const operatorDecisionActionKind = "OperatorDecision";
export const waitForInterventionActionKind = "WaitForIntervention";
/** The decisions a compensation scope offers when it waits for an operator (spec 260 FR-407). */
export const compensationDecisions = {
  resolveEffectAbsent: "compensation.resolve.effect-absent",
  resolveEffectAccepted: "compensation.resolve.effect-accepted",
  abandon: "compensation.abandon"
} as const;
/** The read-only journal a live scope's state view carries (spec 260 FR-414). */
export const compensationJournalViewKind = "compensation.journal";
export const compensationJournalViewVersion = 1;
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
  | "Confirmed" | "HandedOver"
  // Spec 260: a handler running again after a failed attempt, an entry an operator settled without running it, and an
  // entry an abandoned recovery left as it was.
  | "Retrying" | "ResolvedAbsent" | "ResolvedKept" | "NotUndone";

/** A scope occurrence as the run view names it: the scope's execution and, inside a loop, its iteration. */
export interface CompensationScopeReference {
  scopeNodeId: string;
  label: string;
  activityExecutionId?: string;
  iterationId?: string;
}
export type CompensationScopeRunPhase =
  | "NotLoaded" | "NotStarted" | "Running" | "Compensating" | "AwaitingIntervention" | "Completed" | "Compensated" | "Faulted" | "Cancelled"
  // Spec 260: an operator stopped the recovery; the scope faulted with compensation.scope.abandoned.
  | "Abandoned";

/**
 * What an operator can do about a row that waits (spec 260): the waiting incident, the activity a retry runs again, the
 * decisions the wait offers, and whether the wait is on a handler out of attempts or on a forward step with no handler.
 * Studio still shows the actions only when the run says the caller may manage it.
 */
export interface CompensationRowActions {
  incidentId: string;
  /** The faulted activity a retry runs again: the incident's activity, else the row's latest attempt or step. */
  sourceActivityExecutionId?: string;
  decisions: string[];
  target: "handler" | "step";
}

/** One operator action on the scope, read from the incident it resolved (spec 260 OD14). */
export interface CompensationOperatorHistoryEntry {
  incidentId: string;
  kind: "Retry" | "ResolvedAbsent" | "ResolvedKept" | "Abandoned" | "Decision";
  /** The decision code, for a decision. */
  decision?: string;
  actor?: string;
  appliedAt?: string;
  reason?: string;
  /** The step the action was about, when Studio can tell. */
  step?: string;
  /** A decision the scope never applied, because it was cancelled before the decision reached it. */
  notApplied?: boolean;
}

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
  /** The handler's authored node, when the step has one. */
  handlerNodeId?: string;
  /** The journal entry and its operation key, when the row comes from the scope's journal (spec 260). */
  entryId?: string;
  operationKey?: string;
  /** Present while the row waits for an operator. */
  actions?: CompensationRowActions;
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
  /** The journal withheld the failure message because the caller may not see captured values (spec 260 FR-415). */
  messageWithheld?: boolean;
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
  /** Spec 260: whether the rows come from the scope's journal or are derived from run data (M1–M3). */
  source?: "journal" | "derived";
  /** Why a derived view has no journal: the scope completed, so its journal is no longer kept, or it is unavailable. */
  journalUnavailable?: "StateCleared" | "Unavailable";
  /** The scope's own name, for action labels. */
  scopeLabel?: string;
  /** The scope that decides next if this one faults (an abandoned recovery). */
  enclosingScope?: CompensationScopeReference;
  /** The steps still to undo, in recovery order, from the journal. */
  remaining?: Array<{ step: string; handlerNodeId?: string }>;
  /** Operator actions on this occurrence, newest first, read from incidents. */
  history?: CompensationOperatorHistoryEntry[];
}

/** The journal of one live scope, `compensation.journal` version 1 (spec 260 contracts/journal-and-permissions.md). */
export interface CompensationJournal {
  phase: string;
  stateVersion?: number;
  cause?: {
    kind: string;
    step: string;
    activityExecutionId: string;
    incidentId?: string | null;
    failureType?: string | null;
    message?: string | null;
    messageWithheld?: boolean;
    requestedBy?: string | null;
    target?: string | null;
  } | null;
  entries: CompensationJournalEntry[];
  remaining: string[];
  confirmations?: Array<{ activityExecutionId: string; entryIds: string[] }>;
  handOvers?: { received: string[]; inProgress: Array<{ originScope: string; batches: number[] }> } | null;
  abandonment?: { incidentId: string; entryId: string; fromPhase: string } | null;
}

export interface CompensationJournalEntry {
  id: string;
  step: string;
  ordinal: number;
  status: "Registered" | "NoHandler" | "Compensating" | "Compensated" | "Uncertain" | "Failed" | "Confirmed" | "ResolvedManually" | string;
  forwardOutcome: "Completed" | "Uncertain" | string;
  operationKey: string;
  forward: { activityExecutionId: string; iterationId?: string | null };
  handler?: { executableNodeId: string; remaining: boolean } | null;
  attempts: Array<{ number: number; activityExecutionId?: string | null; incidentId?: string | null; result: string }>;
  waiting?: { incidentId: string; decisions: string[] } | null;
  resolution?: { outcome: "EffectAbsent" | "EffectAccepted" | string; incidentId: string } | null;
  origin?: { scopeActivityExecutionId: string; entryId: string } | null;
  confirmedBy?: string | null;
}

/**
 * What the run view knows about a scope occurrence's journal: the state view the runtime returned, `loading` while it is
 * being read, or `unavailable` when the host cannot serve it or the read failed.
 */
export type CompensationJournalSource = ActivityStateView | "loading" | "unavailable";

/** The journal inside a state view, when the view is the compensation journal this Studio reads. */
export function readCompensationJournal(view: ActivityStateView | null | undefined): CompensationJournal | null {
  if (!view?.available || view.viewKind !== compensationJournalViewKind || view.viewVersion !== compensationJournalViewVersion) return null;
  const journal = view.view as Partial<CompensationJournal> | null | undefined;
  if (!journal || typeof journal !== "object" || typeof journal.phase !== "string" || !Array.isArray(journal.entries)) return null;
  return {
    ...journal,
    phase: journal.phase,
    entries: journal.entries.map(entry => ({ ...entry, attempts: Array.isArray(entry.attempts) ? entry.attempts : [] })),
    remaining: Array.isArray(journal.remaining) ? journal.remaining : []
  };
}

export function isCompensationHandlerExecution(activity: Pick<ActivityExecutionStateSummary, "provenance">): boolean {
  return activity.provenance?.schedulingCause === compensationSchedulingCause;
}

// When `complete` is false the activities are one bounded page of the run: an execution that is not on it is
// reported as not loaded, never as not run, so a partial page cannot claim the scope or a step did not happen.
// `root` is the run's workflow graph: with it, a scope also shows the entries inner scopes handed it, Confirm and
// Compensate requests, and which enclosing scope can still undo its own work (spec 258).
// `journal` is what the run view read of the shown occurrence's journal (spec 260): when it holds the journal, the rows
// come from it, and the derivation from run data is the fallback, marked as derived.
export function deriveCompensationRuntime(
  scope: ActivityNode,
  activities: ActivityExecutionStateSummary[],
  incidents: IncidentStateSummary[],
  { complete = true, selectedActivityExecutionId, root, journal }: {
    complete?: boolean;
    selectedActivityExecutionId?: string | null;
    root?: ActivityNode | null;
    journal?: CompensationJournalSource;
  } = {}
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
    // An operator decision replaces the wait's outcome, so a step that waited for one reads as uncertain from the decision:
    // only an uncertain step waits for an operator (spec 260).
    const forwardDecided = forward?.status === "Faulted" && isDecision(forwardIncident);
    const forwardState: CompensationForwardState = !forward && !complete
      ? "NotLoaded"
      : forward?.status === "Faulted" && isUncertain(forwardIncident) || stopped || forwardDecided ? "Uncertain" : forwardStateOf(forward);
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
    state = settledState(state, attempts.length, handler?.status === "Faulted" ? handlerIncident : !step.compensation && forwardDecided ? forwardIncident : undefined, phase0);

    return {
      step: step.name,
      forward: { state: forwardState, activityExecutionId: forward?.activityExecutionId },
      ...(step.compensation ? { handlerNodeId: step.compensation.nodeId } : {}),
      ...(waitingIncident ? { actions: actionsOf(waitingIncident, handler && waitingIncident === handlerIncident ? "handler" : "step", handler ?? forward) } : {}),
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
      state = settledState(state, attempts.length, handlerIncident, phase0);
      return {
        step: entry.step,
        forward: { state: "Completed", activityExecutionId: entry.forward.activityExecutionId },
        handlerNodeId: entry.handlerNodeId,
        ...(waitingIncident ? { actions: actionsOf(waitingIncident, "handler", handler) } : {}),
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

  // A scope that is still running but blocked on an operator waits for intervention (spec 251 OD2). A scope that faulted
  // after an operator abandoned its recovery is abandoned, and what it had not undone stays not undone (spec 260).
  const allRows = [...rows, ...received];
  const abandoned = !!scopeExecution && phase0 === "Faulted" && incidents.some(incident => isDecision(incident)
    && incident.resolutionOutcome?.metadata?.[interventionDecisionKey] === compensationDecisions.abandon
    && incident.resolutionOutcome.metadata[interventionRequestedByKey] === scopeExecution.activityExecutionId);
  const phase: CompensationScopeRunPhase = abandoned
    ? "Abandoned"
    : (phase0 === "Running" || phase0 === "Compensating") && allRows.some(row => row.recovery.state === "NeedsAction")
      ? "AwaitingIntervention"
      : phase0;
  if (abandoned) {
    for (const row of allRows) {
      if (notUndoneStates.has(row.recovery.state) || (row.recovery.state === "NotNeeded" && row.forward.state === "Uncertain")) {
        row.recovery = { ...row.recovery, state: "NotUndone" };
      }
    }
  }

  const faulted = forwardFaulted[0];
  const cause = faulted && (recovering || phase === "AwaitingIntervention" || phase === "Abandoned")
    ? causeOf(steps[faulted.index].name, faulted.forward, incidents)
    : request && (recovering || phase === "AwaitingIntervention" || phase === "Abandoned")
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

  const derived: CompensationScopeRuntime = {
    scopeActivityExecutionId: scopeExecution?.activityExecutionId,
    phase,
    rows,
    cause,
    ...(received.length > 0 ? { received } : {}),
    ...(occurrences ? { occurrences } : {}),
    ...(complete ? {} : { incomplete: true }),
    source: "derived",
    scopeLabel: designLabel(scope),
    ...(scopeExecution && run.enclosingScopeOf(scopeExecution) ? { enclosingScope: run.enclosingScopeOf(scopeExecution) } : {})
  };
  // A completed scope keeps no journal (spec 258 R1): its rows are always derived.
  const view = typeof journal === "object" && scopeExecution && journal.activityExecutionId === scopeExecution.activityExecutionId ? journal : undefined;
  const entries = readCompensationJournal(view);
  const runtime: CompensationScopeRuntime = entries && view
    ? applyJournal(derived, entries, view.activityStatus, run, incidents)
    : {
      ...derived,
      ...(scopeExecution && (scopeExecution.status === "Completed" || view?.unavailableReason === "StateCleared")
        ? { journalUnavailable: "StateCleared" as const }
        : journal === "unavailable" || view ? { journalUnavailable: "Unavailable" as const } : {})
    };
  const history = scopeExecution ? operatorHistoryOf(scopeExecution, runtime, steps, run, incidents, entries) : [];
  return history.length > 0 ? { ...runtime, history } : runtime;
}

// Recovery states an abandoned scope leaves as they were: anything not undone, settled or confirmed.
const notUndoneStates: ReadonlySet<CompensationRecoveryState> = new Set(["Pending", "Failed", "NeedsAction", "Registered", "Kept", "Running", "Retrying"]);

// A running handler after a failed attempt is a retry; an entry an operator resolved reads as resolved, unless the scope
// was cancelled before the decision reached it (spec 260).
function settledState(
  state: CompensationRecoveryState,
  attempts: number,
  incident: IncidentStateSummary | undefined,
  phase: CompensationScopeRunPhase
): CompensationRecoveryState {
  if (state === "Running" && attempts > 1) return "Retrying";
  if (!isDecision(incident) || phase === "Cancelled") return state;
  const decision = incident.resolutionOutcome?.metadata?.[interventionDecisionKey];
  return decision === compensationDecisions.resolveEffectAbsent ? "ResolvedAbsent"
    : decision === compensationDecisions.resolveEffectAccepted ? "ResolvedKept"
      : state;
}

function actionsOf(
  incident: IncidentStateSummary,
  target: CompensationRowActions["target"],
  attempt: ActivityExecutionStateSummary | undefined
): CompensationRowActions {
  const source = incidentActivityExecutionId(incident) ?? attempt?.activityExecutionId;
  return {
    incidentId: incident.incidentId,
    ...(source ? { sourceActivityExecutionId: source } : {}),
    decisions: offeredDecisions(incident),
    target
  };
}

/** The decision codes a waiting incident's outcome offers; a wait from before spec 260 offers none. */
export function offeredDecisions(incident: Pick<IncidentStateSummary, "resolutionOutcome"> | undefined): string[] {
  const raw = incident?.resolutionOutcome?.metadata?.[interventionDecisionsKey];
  if (!raw) return [];
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((code): code is string => typeof code === "string" && code.length > 0) : [];
  } catch {
    return [];
  }
}

/** The structural activity that asked for the wait (or that an operator decided for). */
export function interventionRequesterOf(incident: Pick<IncidentStateSummary, "resolutionOutcome"> | undefined): string | undefined {
  return incident?.resolutionOutcome?.metadata?.[interventionRequestedByKey]?.trim() || undefined;
}

/** Whether an incident waits for an operator: blocking, with a wait-for-intervention outcome. */
export function isWaitingForIntervention(incident: IncidentStateSummary | undefined): incident is IncidentStateSummary {
  return !!incident && incident.isBlocking && incident.resolutionOutcome?.actionKind === waitForInterventionActionKind;
}

function incidentActivityExecutionId(incident: IncidentStateSummary): string | undefined {
  return incident.activityExecutionId?.trim() || incident.metadata?.["runtime.activityExecutionId"]?.trim() || undefined;
}

function isDecision(incident: IncidentStateSummary | undefined): incident is IncidentStateSummary {
  return incident?.resolutionOutcome?.actionKind === operatorDecisionActionKind;
}

// Spec 260: the journal is the source of truth for a live scope. The derivation still places each entry on its authored
// row (by the forward occurrence, else by step name) and supplies what the journal does not carry: recovery order, the
// handler's attempt limit and where a handed-over entry came from.
function applyJournal(
  derived: CompensationScopeRuntime,
  journal: CompensationJournal,
  activityStatus: string,
  run: RunIndex,
  incidents: IncidentStateSummary[]
): CompensationScopeRuntime {
  const phase = journalPhase(journal.phase, activityStatus);
  const own = journal.entries.filter(entry => !entry.origin);
  const used = new Set<string>();
  const rows = derived.rows.map(row => {
    const entry = own.find(candidate => !used.has(candidate.id) && !!row.forward.activityExecutionId && candidate.forward.activityExecutionId === row.forward.activityExecutionId)
      ?? own.find(candidate => !used.has(candidate.id) && candidate.step === row.step);
    if (!entry) return { ...row, actions: undefined };
    used.add(entry.id);
    return journalRow(row, entry, phase, incidents);
  });
  const receivedRows = journal.entries.filter(entry => !!entry.origin).map(entry => {
    const base = derived.received?.find(row => row.forward.activityExecutionId === entry.forward.activityExecutionId);
    const origin = base?.origin ?? originOf(entry.origin!.scopeActivityExecutionId, run);
    return journalRow({
      step: entry.step,
      forward: { state: "Completed", activityExecutionId: entry.forward.activityExecutionId },
      recovery: { state: "NotLoaded", ...(base?.recovery.order ? { order: base.recovery.order } : {}), ...(base?.recovery.attempts ? { attempts: base.recovery.attempts } : {}) },
      origin,
      handlerNodeId: base?.handlerNodeId ?? entry.handler?.executableNodeId
    }, entry, phase, incidents);
  });
  const all = [...rows, ...receivedRows];
  const byEntry = new Map(all.filter(row => row.entryId).map(row => [row.entryId!, row]));
  const cause = journal.cause;
  // An operator acted and the scope has not seen it yet: it no longer waits on anyone.
  const shownPhase: CompensationScopeRunPhase = phase === "AwaitingIntervention" && !all.some(row => row.recovery.state === "NeedsAction")
    ? derived.phase === "Running" ? "Running" : "Compensating"
    : phase;
  return {
    ...derived,
    phase: shownPhase,
    rows,
    ...(receivedRows.length > 0 ? { received: receivedRows } : { received: undefined }),
    cause: cause ? {
      kind: cause.kind === "uncertain" ? "uncertain" : cause.kind === "fault" ? "fault" : "explicit",
      step: cause.step,
      activityExecutionId: cause.activityExecutionId,
      ...(cause.incidentId ? { incidentId: cause.incidentId } : {}),
      ...(cause.failureType ? { failureType: cause.failureType } : {}),
      ...(cause.message ? { message: cause.message } : {}),
      ...(cause.messageWithheld ? { messageWithheld: true } : {}),
      ...(cause.requestedBy ? { requestedBy: cause.requestedBy } : {})
    } : undefined,
    remaining: journal.remaining.flatMap(id => {
      const entry = journal.entries.find(candidate => candidate.id === id);
      if (!entry) return [];
      const handlerNodeId = byEntry.get(id)?.handlerNodeId ?? entry.handler?.executableNodeId;
      return [{ step: entry.step, ...(handlerNodeId ? { handlerNodeId } : {}) }];
    }),
    source: "journal",
    journalUnavailable: undefined
  };
}

function journalRow(row: CompensationRowRuntime, entry: CompensationJournalEntry, phase: CompensationScopeRunPhase, incidents: IncidentStateSummary[]): CompensationRowRuntime {
  const cancelled = phase === "Cancelled";
  const waitingIncident = entry.waiting ? incidents.find(candidate => candidate.incidentId === entry.waiting!.incidentId) : undefined;
  // The scope clears the wait only when the retried handler finishes or the decision reaches it. Until then the incident
  // already says an operator acted, and the row shows what run data says: the retry running, or the decision taken.
  if (entry.waiting && !cancelled && waitingIncident && !isWaitingForIntervention(waitingIncident)) {
    return { ...row, entryId: entry.id, operationKey: entry.operationKey, actions: undefined };
  }
  const waiting = entry.waiting && !cancelled ? entry.waiting : undefined;
  const state = journalRecoveryState(entry, phase);
  const max = row.recovery.attempts?.max;
  const attempts = entry.attempts.length > 1 || max !== undefined ? { count: entry.attempts.length, ...(max !== undefined ? { max } : {}) } : undefined;
  const latestAttempt = entry.attempts.at(-1)?.activityExecutionId ?? undefined;
  const source = (waitingIncident ? incidentActivityExecutionId(waitingIncident) : undefined) ?? latestAttempt ?? entry.forward.activityExecutionId;
  const forwardState: CompensationForwardState = entry.forwardOutcome === "Uncertain"
    ? "Uncertain"
    : row.forward.state === "NotLoaded" || row.forward.state === "NotRun" ? "Completed" : row.forward.state;
  return {
    ...row,
    forward: { ...row.forward, state: forwardState, activityExecutionId: entry.forward.activityExecutionId },
    entryId: entry.id,
    operationKey: entry.operationKey,
    actions: waiting ? { incidentId: waiting.incidentId, sourceActivityExecutionId: source, decisions: waiting.decisions ?? [], target: entry.attempts.length > 0 ? "handler" : "step" } : undefined,
    recovery: {
      state,
      ...(latestAttempt ?? row.recovery.activityExecutionId ? { activityExecutionId: latestAttempt ?? row.recovery.activityExecutionId } : {}),
      ...(row.recovery.order && entry.attempts.length > 0 ? { order: row.recovery.order } : {}),
      ...(attempts ? { attempts } : {}),
      ...(waiting ? { incidentId: waiting.incidentId } : {}),
      ...(state === "Confirmed" && entry.confirmedBy ? { confirmedBy: entry.confirmedBy } : {})
    }
  };
}

function journalPhase(phase: string, activityStatus: string): CompensationScopeRunPhase {
  if (phase === "Abandoned") return "Abandoned";
  if (activityStatus === "Cancelled") return "Cancelled";
  if (activityStatus === "Faulted") return "Faulted";
  switch (phase) {
    case "Compensating":
    case "AwaitingIntervention":
    case "Completed":
    case "Compensated":
      return phase;
    default:
      return "Running";
  }
}

function journalRecoveryState(entry: CompensationJournalEntry, phase: CompensationScopeRunPhase): CompensationRecoveryState {
  switch (entry.status) {
    case "ResolvedManually": return entry.resolution?.outcome === "EffectAccepted" ? "ResolvedKept" : "ResolvedAbsent";
    case "Compensated": return "Compensated";
    case "Confirmed": return "Confirmed";
    case "NoHandler": return "NotNeeded";
  }
  if (phase === "Abandoned") return "NotUndone";
  const ended = phase === "Cancelled" || phase === "Faulted" || phase === "Completed" || phase === "Compensated";
  if (entry.waiting && !ended) return "NeedsAction";
  switch (entry.status) {
    case "Compensating": return entry.attempts.length > 1 ? "Retrying" : "Running";
    case "Failed": return "Failed";
    default:
      if (!entry.handler) return ended ? "Kept" : "NotNeeded";
      if (ended) return "Kept";
      return phase === "Running" ? "Registered" : "Pending";
  }
}

function originOf(scopeActivityExecutionId: string, run: RunIndex): CompensationScopeReference {
  const scope = run.execution(scopeActivityExecutionId);
  return scope ? run.reference(scope) : { scopeNodeId: "", label: "an inner scope", activityExecutionId: scopeActivityExecutionId };
}

// Operator history (spec 260 OD14): the run's incidents an operator resolved. A retry belongs to the occurrence that is
// the nearest scope of the retried activity (a step or handler of this scope, or a descendant of one); a decision belongs
// to the scope that asked for it.
function operatorHistoryOf(
  scopeExecution: ActivityExecutionStateSummary,
  runtime: CompensationScopeRuntime,
  steps: ReturnType<typeof readCompensationSteps>,
  run: RunIndex,
  incidents: IncidentStateSummary[],
  journal: CompensationJournal | null
): CompensationOperatorHistoryEntry[] {
  const scopeId = scopeExecution.activityExecutionId;
  const ownNodes = new Set(steps.flatMap(step => [step.activity?.nodeId, step.compensation?.nodeId]).filter((id): id is string => !!id));
  const stepOf = (activityExecutionId: string | undefined, nodeId: string | null | undefined): string | undefined => {
    const activity = activityExecutionId ? run.execution(activityExecutionId) : undefined;
    const child = activity ? run.childOf(activity, scopeId) : undefined;
    const key = child ? nodeKey(child) : nodeId ?? undefined;
    const step = steps.find(candidate => candidate.activity?.nodeId === key || candidate.compensation?.nodeId === key
      || (!!child && (candidate.activity?.nodeId === child.executableNodeId || candidate.compensation?.nodeId === child.executableNodeId)));
    return step?.name ?? (runtime.received ?? []).find(row => row.recovery.activityExecutionId === activityExecutionId)?.step;
  };
  return incidents.flatMap((incident): CompensationOperatorHistoryEntry[] => {
    const outcome = incident.resolutionOutcome;
    const metadata = outcome?.metadata ?? {};
    const activityExecutionId = incidentActivityExecutionId(incident);
    let belongs: boolean;
    if (outcome?.actionKind === operatorRetryActionKind) {
      const activity = activityExecutionId ? run.execution(activityExecutionId) : undefined;
      belongs = activity
        ? run.nearestScope(activity)?.activityExecutionId === scopeId
        : !runtime.occurrences && !!incident.executableNodeId && ownNodes.has(incident.executableNodeId);
    } else if (outcome?.actionKind === operatorDecisionActionKind) {
      belongs = metadata[interventionRequestedByKey] === scopeId;
    } else {
      return [];
    }
    if (!belongs) return [];
    const decision = outcome.actionKind === operatorDecisionActionKind ? metadata[interventionDecisionKey] : undefined;
    const kind: CompensationOperatorHistoryEntry["kind"] = !decision ? "Retry"
      : decision === compensationDecisions.resolveEffectAbsent ? "ResolvedAbsent"
        : decision === compensationDecisions.resolveEffectAccepted ? "ResolvedKept"
          : decision === compensationDecisions.abandon ? "Abandoned"
            : "Decision";
    // The scope acks a decision it can no longer apply; its journal still shows the entry waiting on that incident.
    const notApplied = !!decision && runtime.phase === "Cancelled" && !!journal?.entries.some(entry => entry.waiting?.incidentId === incident.incidentId);
    const step = stepOf(activityExecutionId, incident.executableNodeId);
    return [{
      incidentId: incident.incidentId,
      kind,
      ...(decision ? { decision } : {}),
      ...(metadata[alterationSubmittedByKey] ? { actor: metadata[alterationSubmittedByKey] } : {}),
      ...(outcome.appliedAt ?? incident.resolvedAt ? { appliedAt: (outcome.appliedAt ?? incident.resolvedAt)! } : {}),
      ...(metadata[alterationReasonKey] ? { reason: metadata[alterationReasonKey] } : {}),
      ...(step ? { step } : {}),
      ...(notApplied ? { notApplied: true } : {})
    }];
  }).sort((left, right) => (right.appliedAt ?? "").localeCompare(left.appliedAt ?? ""));
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
    case "Retrying": return "Retrying";
    case "ResolvedAbsent": return "Resolved: effect absent";
    case "ResolvedKept": return "Resolved: effect kept";
    case "NotUndone": return "Not undone (recovery abandoned)";
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
    case "Abandoned": return "Abandoned";
  }
}

/** Names a decision a wait offers; a code no module labels is shown as it is (spec 260 R13). */
export function interventionDecisionLabel(code: string): string {
  switch (code) {
    case compensationDecisions.resolveEffectAbsent: return "Resolve: effect absent or undone";
    case compensationDecisions.resolveEffectAccepted: return "Resolve: effect present, keep it";
    case compensationDecisions.abandon: return "Abandon recovery";
    default: return code;
  }
}

/** How the operator history names an action: "Retry", "Resolved — effect absent", "Abandoned". */
export function operatorHistoryLabel(entry: Pick<CompensationOperatorHistoryEntry, "kind" | "decision">): string {
  switch (entry.kind) {
    case "Retry": return "Retry";
    case "ResolvedAbsent": return "Resolved — effect absent";
    case "ResolvedKept": return "Resolved — effect kept";
    case "Abandoned": return "Abandoned";
    default: return `Decision — ${entry.decision ? interventionDecisionLabel(entry.decision) : "unknown"}`;
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

  execution(activityExecutionId: string): ActivityExecutionStateSummary | undefined {
    return this.byId.get(activityExecutionId);
  }

  // The direct child of an occurrence on an execution's parent chain (the execution itself when it is one).
  childOf(activity: ActivityExecutionStateSummary, parentActivityExecutionId: string): ActivityExecutionStateSummary | undefined {
    return [activity, ...this.ancestors(activity)].find(candidate => parentOf(candidate) === parentActivityExecutionId);
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
