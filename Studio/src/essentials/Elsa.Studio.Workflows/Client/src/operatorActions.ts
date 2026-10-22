import {
  AlterationRequestError,
  alterationJobFailureCode,
  decideInterventionPlan,
  retryFaultedActivityPlan,
  terminalAlterationPlanStatuses,
  type AlterationJob,
  type AlterationJobPage,
  type AlterationPlan,
  type AlterationPlanRequest,
  type AlterationPlanSubmission
} from "./api/alterations";
import {
  compensationDecisions,
  interventionDecisionLabel,
  ordinalLabel,
  type CompensationRowRuntime,
  type CompensationScopeRuntime
} from "./compensationRuntime";
import type { WorkflowInstanceDetails, WorkflowInstancePermissions } from "./workflowTypes";

// Spec 260 contracts/studio-interaction.md: what each operator action says before it is confirmed, how its reason is
// checked, how Studio follows the plan it submits, and how refusals and HTTP errors read.

export type OperatorActionKind = "retry" | "resolve" | "abandon" | "decide";

export interface OperatorActionOutcomeChoice {
  decision: string;
  label: string;
  description: string;
}

/** Everything the confirmation dialog shows and submits for one action. */
export interface OperatorActionModel {
  kind: OperatorActionKind;
  title: string;
  confirmLabel: string;
  tone: "default" | "danger";
  /** The target block, one line each: scope › step › handler, then attempt, incident and run. */
  target: string[];
  /** The consequence block: only the lines that apply to this target. */
  effect: string[];
  /** Abandon: the entries that stay not undone, listed under the first effect line. */
  notUndone?: string[];
  /** Effect lines that follow the list. */
  effectAfter?: string[];
  /** Resolve: the outcome choice. */
  outcomes?: OperatorActionOutcomeChoice[];
  /** Resolve opened from an offered decision starts with that outcome chosen. */
  initialDecision?: string;
  reasonRequired: boolean;
  workflowExecutionId: string;
  incidentId: string;
  /** The scope occurrence the action is about, so Studio refreshes its journal while applying. */
  scopeActivityExecutionId?: string;
  /** Retry: the faulted activity to run again. */
  sourceActivityExecutionId?: string;
  /** Abandon or another offered decision: the fixed decision code. */
  decision?: string;
  /** "Retry submitted", "Resolve submitted", … */
  submittedLabel: string;
}

export const maxOperatorReasonLength = 500;
// The characters .NET's char.IsControl rejects: C0, DEL and C1.
function hasControlCharacter(text: string) {
  for (let index = 0; index < text.length; index++) {
    const code = text.charCodeAt(index);
    if (code <= 0x1f || (code >= 0x7f && code <= 0x9f)) return true;
  }
  return false;
}

/** The reason rule the runtime applies: trimmed, at most 500 characters, no control characters, required except for a retry. */
export function validateOperatorReason(reason: string, required: boolean): string | null {
  const text = reason.trim();
  if (!text) return required ? "Enter a reason." : null;
  if (text.length > maxOperatorReasonLength) return `Use at most ${maxOperatorReasonLength} characters (now ${text.length}).`;
  if (hasControlCharacter(text)) return "Remove line breaks and other control characters.";
  return null;
}

export function mintIdempotencyKey(random: () => string = defaultRandomId): string {
  return `studio-${random()}`;
}

function defaultRandomId() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") return crypto.randomUUID();
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

/** The plan body for a confirmed action. */
export function operatorActionPlan(model: OperatorActionModel, form: { reason: string; decision?: string | null }): AlterationPlanRequest {
  if (model.kind === "retry") {
    if (!model.sourceActivityExecutionId) throw new Error("A retry needs the faulted activity to run again.");
    return retryFaultedActivityPlan(model.workflowExecutionId, model.sourceActivityExecutionId, form.reason);
  }
  const decision = model.kind === "resolve" ? form.decision : model.decision;
  if (!decision) throw new Error("Choose an outcome.");
  return decideInterventionPlan(model.workflowExecutionId, model.incidentId, decision, form.reason);
}

/** The run's permission signal; a host that omits it is read as "may not manage" (fail closed, FR-416). */
export function readWorkflowInstancePermissions(details: Pick<WorkflowInstanceDetails, "permissions"> | null | undefined): WorkflowInstancePermissions {
  const permissions = details?.permissions;
  return { canManage: permissions?.canManage === true, canInspectValues: permissions?.canInspectValues === true };
}

// ----- What each action says -----

export interface OperatorActionScopeContext {
  workflowExecutionId: string;
  runtime: CompensationScopeRuntime;
  /** The authored label of a node, such as a handler's activity. */
  labelOf(nodeId: string | undefined): string | undefined;
}

const quote = (value: string) => `"${value}"`;

function scopeName(runtime: CompensationScopeRuntime) {
  return runtime.scopeLabel || "this scope";
}

function handlerName(row: CompensationRowRuntime, context: OperatorActionScopeContext) {
  return context.labelOf(row.handlerNodeId) ?? `the compensation of ${row.step}`;
}

// What recovery runs next after this row, when the journal says (remaining is in recovery order): the handler's name
// and the step it undoes, which is the name an operator knows.
function nextAfter(row: CompensationRowRuntime, context: OperatorActionScopeContext): string | null | undefined {
  const remaining = context.runtime.remaining;
  if (!remaining) return undefined;
  const next = remaining.find(item => item.step !== row.step);
  if (!next) return null;
  const handler = context.labelOf(next.handlerNodeId);
  return handler && handler !== next.step
    ? `${quote(handler)}, the compensation of ${quote(next.step)}`
    : `the compensation of ${quote(next.step)}`;
}

function attemptLine(row: CompensationRowRuntime, actions: NonNullable<CompensationRowRuntime["actions"]>, workflowExecutionId: string) {
  const attempts = row.recovery.attempts;
  const count = attempts?.count ?? (actions.target === "handler" ? 1 : 0);
  const parts: string[] = [];
  if (actions.target === "handler") {
    const limit = attempts?.max !== undefined && count >= attempts.max ? ` (limit ${attempts.max} reached)` : "";
    parts.push(`Attempt ${count + 1}${limit}`);
  }
  parts.push(`incident ${actions.incidentId}`, `run ${workflowExecutionId}`);
  return parts.join(" · ");
}

export function retryActionModel(row: CompensationRowRuntime, context: OperatorActionScopeContext): OperatorActionModel {
  const actions = requireActions(row);
  const { runtime, workflowExecutionId } = context;
  const handler = actions.target === "handler";
  const handlerLabel = handlerName(row, context);
  const next = nextAfter(row, context);
  const key = row.operationKey ? ` with the same operation key (${handler ? `${row.operationKey}/compensate` : row.operationKey})` : " with the same operation key";
  const effect = handler
    ? [
      `Runs ${quote(handlerLabel)} again${key}.`,
      `${next ? `If it succeeds, recovery continues with ${next}.` : next === null ? "If it succeeds, the scope finishes its recovery." : "If it succeeds, recovery continues."} If it fails, the scope waits again.`
    ]
    : [
      `Runs ${quote(row.step)} again${key}.`,
      "If it completes, the scope carries on with the next step. If it fails, the scope handles that failure as it did the first time."
    ];
  return {
    kind: "retry",
    title: handler ? `Retry the compensation of ${quote(row.step)}?` : `Retry ${quote(row.step)}?`,
    confirmLabel: "Retry",
    tone: "default",
    target: [
      `Scope ${quote(scopeName(runtime))} › step ${quote(row.step)}${handler ? ` › compensation ${quote(handlerLabel)}` : ""}`,
      attemptLine(row, actions, workflowExecutionId)
    ],
    effect,
    reasonRequired: false,
    workflowExecutionId,
    incidentId: actions.incidentId,
    scopeActivityExecutionId: runtime.scopeActivityExecutionId,
    sourceActivityExecutionId: actions.sourceActivityExecutionId,
    submittedLabel: "Retry submitted"
  };
}

export const resolveOutcomes: OperatorActionOutcomeChoice[] = [
  {
    decision: compensationDecisions.resolveEffectAbsent,
    label: "Effect absent or undone",
    description: "The effect does not exist: it never happened, or it was undone outside Elsa."
  },
  {
    decision: compensationDecisions.resolveEffectAccepted,
    label: "Effect present, keep it",
    description: "The effect exists and stays."
  }
];

export function resolveActionModel(row: CompensationRowRuntime, context: OperatorActionScopeContext, initialDecision?: string): OperatorActionModel {
  const actions = requireActions(row);
  const { runtime, workflowExecutionId } = context;
  const outcomes = resolveOutcomes.filter(outcome => actions.decisions.includes(outcome.decision));
  const next = nextAfter(row, context);
  const effect = actions.target === "handler"
    ? [next ? `Recovery continues with ${next}.` : next === null ? "The scope finishes its recovery." : "Recovery continues with the next step to undo."]
    : [
      ...(outcomes.some(outcome => outcome.decision === compensationDecisions.resolveEffectAbsent)
        ? ["Effect absent: the completed steps are undone, newest first."] : []),
      ...(outcomes.some(outcome => outcome.decision === compensationDecisions.resolveEffectAccepted)
        ? ["Effect present: the scope carries on with the next step."] : [])
    ];
  return {
    kind: "resolve",
    title: `Resolve ${quote(row.step)} without running it?`,
    confirmLabel: "Resolve",
    tone: "default",
    target: [
      `Scope ${quote(scopeName(runtime))} › step ${quote(row.step)}`,
      `incident ${actions.incidentId} · run ${workflowExecutionId}`
    ],
    effect: [...effect, "The entry is never undone afterwards."],
    outcomes,
    ...(initialDecision && outcomes.some(outcome => outcome.decision === initialDecision) ? { initialDecision } : {}),
    reasonRequired: true,
    workflowExecutionId,
    incidentId: actions.incidentId,
    scopeActivityExecutionId: runtime.scopeActivityExecutionId,
    submittedLabel: "Resolve submitted"
  };
}

/** The row an abandon decides on: the one waiting with the abandon decision offered. */
export function abandonableRow(runtime: CompensationScopeRuntime): CompensationRowRuntime | undefined {
  if (runtime.phase !== "AwaitingIntervention") return undefined;
  return [...runtime.rows, ...(runtime.received ?? [])].find(row => row.actions?.decisions.includes(compensationDecisions.abandon));
}

const notUndoneLabels: Partial<Record<CompensationRowRuntime["recovery"]["state"], string>> = {
  NeedsAction: "undo failed",
  Failed: "undo failed",
  Pending: "waiting to undo",
  Registered: "can be undone",
  Running: "undoing",
  Retrying: "undoing",
  RetryScheduled: "retry scheduled"
};

export function abandonActionModel(context: OperatorActionScopeContext): OperatorActionModel {
  const { runtime, workflowExecutionId } = context;
  const row = abandonableRow(runtime);
  if (!row?.actions) throw new Error("This scope does not wait for a decision.");
  const name = scopeName(runtime);
  const notUndone = [...runtime.rows, ...(runtime.received ?? [])].flatMap(candidate => {
    const reason = candidate.recovery.state === "NeedsAction" && candidate.actions?.target === "step"
      ? "uncertain"
      : notUndoneLabels[candidate.recovery.state];
    return reason ? [`${candidate.step} (${reason})`] : [];
  });
  const enclosing = runtime.enclosingScope?.label;
  return {
    kind: "abandon",
    title: `Abandon recovery of scope ${quote(name)}?`,
    confirmLabel: "Abandon recovery",
    tone: "danger",
    target: [
      `Scope ${quote(name)}${runtime.scopeActivityExecutionId ? ` (occurrence ${runtime.scopeActivityExecutionId})` : ""} · waiting on incident ${row.actions.incidentId} · run ${workflowExecutionId}`
    ],
    effect: [notUndone.length > 0 ? "No further compensation runs. These stay not undone:" : "No further compensation runs."],
    notUndone,
    effectAfter: [
      "The scope fails with compensation.scope.abandoned.",
      enclosing
        ? `Inside ${quote(enclosing)}, that scope decides next.`
        : "The run's incident strategy decides next; by default the run fails."
    ],
    reasonRequired: true,
    workflowExecutionId,
    incidentId: row.actions.incidentId,
    scopeActivityExecutionId: runtime.scopeActivityExecutionId,
    decision: compensationDecisions.abandon,
    submittedLabel: "Abandon submitted"
  };
}

/** An action opened from the incident dialog for a wait Studio cannot place on a compensation scope. */
export function incidentActionModel(
  incident: { incidentId: string; workflowExecutionId: string; activityExecutionId?: string },
  activityLabel: string,
  action: "retry" | { decision: string },
  requesterActivityExecutionId?: string
): OperatorActionModel {
  const target = [`Activity ${quote(activityLabel)}`, `incident ${incident.incidentId} · run ${incident.workflowExecutionId}`];
  if (action === "retry") {
    return {
      kind: "retry",
      title: `Retry ${quote(activityLabel)}?`,
      confirmLabel: "Retry",
      tone: "default",
      target,
      effect: [`Runs ${quote(activityLabel)} again and resolves this incident. If it fails, the runtime handles the failure as it did the first time.`],
      reasonRequired: false,
      workflowExecutionId: incident.workflowExecutionId,
      incidentId: incident.incidentId,
      ...(requesterActivityExecutionId ? { scopeActivityExecutionId: requesterActivityExecutionId } : {}),
      sourceActivityExecutionId: incident.activityExecutionId,
      submittedLabel: "Retry submitted"
    };
  }
  const label = interventionDecisionLabel(action.decision);
  return {
    kind: "decide",
    title: `Apply ${quote(label)}?`,
    confirmLabel: "Apply decision",
    tone: "default",
    target,
    effect: [`Resolves this incident with the decision ${action.decision}. The activity that asked for the decision applies it.`],
    reasonRequired: true,
    workflowExecutionId: incident.workflowExecutionId,
    incidentId: incident.incidentId,
    ...(requesterActivityExecutionId ? { scopeActivityExecutionId: requesterActivityExecutionId } : {}),
    decision: action.decision,
    submittedLabel: "Decision submitted"
  };
}

function requireActions(row: CompensationRowRuntime) {
  if (!row.actions) throw new Error(`${row.step} does not wait for an operator.`);
  return row.actions;
}

/** A row's chip label, e.g. "Needs action (1st) · Attempt 3 of 3". Shared by the designer and the action buttons' names. */
export function rowRecoveryOrder(row: CompensationRowRuntime) {
  return row.recovery.order ? ` (${ordinalLabel(row.recovery.order)})` : "";
}

// ----- Refusals and errors -----

export type OperatorActionFailure = {
  message: string;
  /** A 403: Studio refreshes the run's permission signal. */
  refreshPermissions?: boolean;
  /** Someone else acted, or the run changed: Studio refreshes the run. */
  refreshRun?: boolean;
};

const nothingChanged = " Nothing was changed.";

/** The message for a refusal code a finished job carries (contracts/studio-interaction.md, error mapping). */
export function operatorRefusalMessage(code: string | null | undefined): string {
  switch (code) {
    case "NoBlockingIncident":
    case "InterventionNotPending":
    case "SourceActivityNotFaulted":
      return `Someone else already acted on this. The run has been refreshed.${nothingChanged}`;
    case "IncidentNotAwaitingIntervention":
      return `The runtime has not finished deciding what to do with this failure. Try again shortly.${nothingChanged}`;
    case "SourceWorkClaimed":
    case "RequesterWorkClaimed":
      return `The scope is busy. Try again in a moment.${nothingChanged}`;
    case "SourceParentNotRunning":
    case "RequesterNotRunning":
      return `The scope is no longer running, so it cannot be changed.${nothingChanged}`;
    case "DecisionNotOffered":
      return `This action is not available for this failure.${nothingChanged}`;
    case "WorkflowTerminal":
      return `The run has ended; it cannot be changed.${nothingChanged}`;
    case "TargetNotFound":
      return `This run is unavailable for operator actions.${nothingChanged}`;
    case "InvalidDecideInterventionPayload":
    case "InvalidRetryFaultedActivityPayload":
      return `Check the reason: 1 to 500 characters, no control characters.${nothingChanged}`;
    default:
      return `The action could not be applied (${code || "unknown"}).${nothingChanged}`;
  }
}

/** The message for a failed submission or read. */
export function operatorErrorMessage(error: unknown): OperatorActionFailure {
  if (error instanceof AlterationRequestError || hasStatus(error)) {
    const status = (error as { status: number }).status;
    const code = error instanceof AlterationRequestError ? error.code : null;
    if (status === 401) return { message: `Your session has expired. Sign in again to act on this run.${nothingChanged}` };
    if (status === 403) return { message: `You do not have permission to change this run.${nothingChanged}`, refreshPermissions: true };
    if (status === 404) return { message: "This run is unavailable or you do not have access to it.", refreshRun: true };
    if (status === 409 && code === "AlterationIdempotencyConflict") {
      return { message: `This action was already submitted with different details. Refresh and try again.${nothingChanged}` };
    }
    if (status === 429) {
      const seconds = error instanceof AlterationRequestError ? error.retryAfterSeconds : null;
      return { message: `The runtime is busy. Try again in ${seconds ?? "a few"} seconds.${nothingChanged}` };
    }
    return { message: `The action could not be applied (${code ?? `HTTP ${status}`}).${nothingChanged}` };
  }
  return { message: `The action could not be applied (${error instanceof Error ? error.message : String(error)}).` };
}

function hasStatus(error: unknown): boolean {
  return typeof error === "object" && error !== null && typeof (error as { status?: unknown }).status === "number"
    && (error as { status: number }).status > 0;
}

export const operatorPollingTimeoutMessage = "The action is still queued. Studio will keep checking while this page is open.";
export const operatorApplyingTimeoutMessage = "The runtime accepted the action, but the run does not show it yet. Refresh to check again.";

// ----- Following a plan -----

/** A submitted plan Studio is still following. Kept per run in session storage so a reload resumes it. */
export interface PendingOperatorAction {
  planId: string;
  kind: OperatorActionKind;
  incidentId: string;
  key: string;
  submittedLabel: string;
  scopeActivityExecutionId?: string;
}

export interface OperatorActionStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export function pendingOperatorActionKey(workflowExecutionId: string) {
  return `elsa.studio.operator-action:${workflowExecutionId}`;
}

function sessionStore(): OperatorActionStorage | null {
  try {
    return typeof sessionStorage === "undefined" ? null : sessionStorage;
  } catch {
    return null;
  }
}

export function readPendingOperatorAction(workflowExecutionId: string, storage: OperatorActionStorage | null = sessionStore()): PendingOperatorAction | null {
  try {
    const raw = storage?.getItem(pendingOperatorActionKey(workflowExecutionId));
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<PendingOperatorAction>;
    return typeof value.planId === "string" && typeof value.incidentId === "string" && typeof value.key === "string"
      ? {
        planId: value.planId,
        kind: (value.kind ?? "retry") as OperatorActionKind,
        incidentId: value.incidentId,
        key: value.key,
        submittedLabel: value.submittedLabel ?? "Action submitted",
        ...(value.scopeActivityExecutionId ? { scopeActivityExecutionId: value.scopeActivityExecutionId } : {})
      }
      : null;
  } catch {
    return null;
  }
}

export function storePendingOperatorAction(workflowExecutionId: string, pending: PendingOperatorAction, storage: OperatorActionStorage | null = sessionStore()) {
  try {
    storage?.setItem(pendingOperatorActionKey(workflowExecutionId), JSON.stringify(pending));
  } catch {
    // Without session storage a reload cannot resume polling; the plan still runs.
  }
}

export function clearPendingOperatorAction(workflowExecutionId: string, storage: OperatorActionStorage | null = sessionStore()) {
  try {
    storage?.removeItem(pendingOperatorActionKey(workflowExecutionId));
  } catch {
    // Nothing to clear.
  }
}

export interface OperatorActionApi {
  submit(request: AlterationPlanRequest, key: string): Promise<AlterationPlanSubmission>;
  getPlan(planId: string): Promise<AlterationPlan>;
  pageJobs(planId: string): Promise<AlterationJobPage>;
  getJob(planId: string, jobId: string): Promise<AlterationJob>;
}

export interface OperatorPollingOptions {
  sleep(milliseconds: number): Promise<void>;
  now(): number;
  /** Stop following (the page closed or the run changed). */
  signal?: AbortSignal;
  /** Called once when the plan is still not done after the polling budget. */
  onStillQueued?(): void;
}

/** 1 s, backing off by a second up to 5 s. */
export function operatorPollDelay(attempt: number) {
  return Math.min(5000, 1000 * (attempt + 1));
}

export const operatorPollingBudgetMs = 120_000;
export const operatorApplyingBudgetMs = 60_000;

export type OperatorPlanResult =
  | { kind: "succeeded" }
  | { kind: "refused"; code: string | null }
  | { kind: "aborted" };

/**
 * Polls the plan to its end, then reads its job. A plan still running after the budget keeps being polled at 5 s while
 * the page is open, after one `onStillQueued` call.
 */
/** A read refused for sign-in, permission or a gone plan ends following; any other failure, such as a lost connection, is tried again. */
export function isTransientReadFailure(error: unknown): boolean {
  const status = (error as { status?: unknown } | null)?.status;
  return status !== 401 && status !== 403 && status !== 404;
}

export async function followOperatorPlan(api: OperatorActionApi, planId: string, options: OperatorPollingOptions): Promise<OperatorPlanResult> {
  const started = options.now();
  let warned = false;
  for (let attempt = 0; ; attempt++) {
    if (options.signal?.aborted) return { kind: "aborted" };
    try {
      const plan = await api.getPlan(planId);
      if (terminalAlterationPlanStatuses.has(plan.status)) {
        const page = await api.pageJobs(planId);
        const summary = page.items[0];
        if (!summary) return plan.status === "Completed" ? { kind: "succeeded" } : { kind: "refused", code: plan.failure?.code ?? null };
        const job = await api.getJob(planId, summary.jobId);
        return job.status === "Succeeded" ? { kind: "succeeded" } : { kind: "refused", code: alterationJobFailureCode(job) ?? plan.failure?.code ?? null };
      }
    } catch (error) {
      // A lost connection does not end the plan, also while reading its result: keep checking. A refused read
      // (sign-in, permission, gone) does.
      if (!isTransientReadFailure(error)) throw error;
      await options.sleep(operatorPollDelay(attempt));
      continue;
    }
    if (!warned && options.now() - started >= operatorPollingBudgetMs) {
      warned = true;
      options.onStillQueued?.();
    }
    await options.sleep(operatorPollDelay(attempt));
  }
}

/**
 * After the job succeeded the decision still has to reach the scope, in a later work item: refresh the run until it shows
 * the change. Returns false when the budget runs out first.
 */
export async function waitUntilApplied(
  refresh: () => Promise<boolean>,
  options: Omit<OperatorPollingOptions, "onStillQueued">,
  budgetMs = operatorApplyingBudgetMs
): Promise<boolean | "aborted"> {
  const started = options.now();
  for (let attempt = 0; ; attempt++) {
    if (options.signal?.aborted) return "aborted";
    try {
      if (await refresh()) return true;
    } catch (error) {
      // A lost connection while checking the run is not a result: keep checking until the budget runs out.
      if (!isTransientReadFailure(error)) throw error;
    }
    if (options.now() - started >= budgetMs) return false;
    await options.sleep(operatorPollDelay(attempt));
  }
}
