import { StudioHttpError, createStudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, getApiCapability, resolveCapabilityLink } from "./capabilities";

// The runtime alteration API (spec 141), as Studio's operator actions use it (spec 260): one run, one alteration per
// plan. Every route comes from the runtime capability's links, which a host advertises only when it composed durable
// plan state and the orchestration service.

export const alterationRelations = {
  plans: "workflow-alteration-plans",
  plan: "workflow-alteration-plan",
  jobsPage: "workflow-alteration-plan-jobs-page",
  job: "workflow-alteration-job"
} as const;

export const retryFaultedActivityKind = "RetryFaultedActivity";
export const decideInterventionKind = "DecideIntervention";

export type AlterationPlanStatus =
  | "CapturingTargets" | "Queued" | "Running" | "Cancelling" | "Completed" | "CompletedWithFailures" | "Failed" | "Cancelled";
export type AlterationJobStatus = "Pending" | "Running" | "Succeeded" | "Failed" | "Cancelled";

export const terminalAlterationPlanStatuses: ReadonlySet<string> = new Set(["Completed", "CompletedWithFailures", "Failed", "Cancelled"]);

export interface AlterationFailure {
  code: string;
  message?: string | null;
}

export interface AlterationPlanSubmission {
  planId: string;
  status: string;
  /** `Replayed` when the same key and body were submitted before: the existing plan, nothing runs twice. */
  submissionDisposition: "Accepted" | "Replayed" | string;
  createdAt: string;
}

export interface AlterationPlan {
  planId: string;
  status: AlterationPlanStatus | string;
  failure?: AlterationFailure | null;
  counts?: { succeeded?: number; failed?: number; cancelled?: number; pending?: number; running?: number } | null;
}

export interface AlterationJobSummary {
  jobId: string;
  planId: string;
  workflowExecutionId: string;
  status: AlterationJobStatus | string;
  failure?: AlterationFailure | null;
}

export interface AlterationJobPage {
  items: AlterationJobSummary[];
  nextCursor?: string | null;
  hasNext?: boolean;
}

export interface AlterationOutcome {
  ordinal: number;
  kind: string;
  schemaVersion: number;
  status: "Succeeded" | "Failed" | "Skipped" | string;
  code: string;
  message?: string | null;
}

export interface AlterationJob extends AlterationJobSummary {
  submittedBy?: { subject: string; displayName?: string | null } | null;
  outcomes: AlterationOutcome[];
}

export interface AlterationPlanRequest {
  target: { executionIds: string[] };
  alterations: Array<{ kind: string; schemaVersion: number; payload: Record<string, string> }>;
}

/** Why a submission or a read failed over HTTP, with the alteration problem code when the server sent one. */
export class AlterationRequestError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string | null,
    message: string,
    public readonly retryAfterSeconds: number | null = null
  ) {
    super(message);
    this.name = "AlterationRequestError";
  }
}

/** Whether the host advertises every route an operator action needs. */
export async function supportsOperatorAlterations(context: StudioEndpointContext) {
  const capability = await getApiCapability(context, capabilityIds.runtime);
  if (!capability || capability.contractVersion !== "1") return false;
  const relations = new Set(capability.links.map(link => link.rel));
  return Object.values(alterationRelations).every(relation => relations.has(relation));
}

export function retryFaultedActivityPlan(workflowExecutionId: string, sourceActivityExecutionId: string, reason?: string | null): AlterationPlanRequest {
  const trimmed = reason?.trim();
  return {
    target: { executionIds: [workflowExecutionId] },
    alterations: [{
      kind: retryFaultedActivityKind,
      schemaVersion: 1,
      payload: { sourceActivityExecutionId, ...(trimmed ? { reason: trimmed } : {}) }
    }]
  };
}

export function decideInterventionPlan(workflowExecutionId: string, incidentId: string, decision: string, reason: string): AlterationPlanRequest {
  return {
    target: { executionIds: [workflowExecutionId] },
    alterations: [{ kind: decideInterventionKind, schemaVersion: 1, payload: { incidentId, decision, reason: reason.trim() } }]
  };
}

/**
 * Submits one plan with its idempotency key. The same key and body return the existing plan as `Replayed`; the same key
 * with another body is a 409. Prefers the raw transport so a 429's `Retry-After` header is readable.
 */
export async function submitAlterationPlan(
  context: StudioEndpointContext,
  request: AlterationPlanRequest,
  idempotencyKey: string
): Promise<AlterationPlanSubmission> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, alterationRelations.plans);
  const headers = { "Idempotency-Key": idempotencyKey };
  try {
    if (context.http.requestResponse) {
      const response = await context.http.requestResponse(path, {
        method: "POST",
        headers: { ...headers, "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify(request)
      });
      if (!response.ok) throw await alterationErrorFromResponse(response);
      return readSubmission(await response.json());
    }
    return readSubmission(await context.http.postJson<unknown>(path, request, { headers }));
  } catch (error) {
    throw toAlterationRequestError(error);
  }
}

export async function getAlterationPlan(context: StudioEndpointContext, planId: string): Promise<AlterationPlan> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, alterationRelations.plan, { planId });
  return read(() => context.http.getJson<AlterationPlan>(path));
}

export async function pageAlterationJobs(context: StudioEndpointContext, planId: string, take = 10): Promise<AlterationJobPage> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, alterationRelations.jobsPage, { planId });
  const page = await read(() => context.http.getJson<Partial<AlterationJobPage>>(`${path}?take=${take}`));
  return { items: Array.isArray(page?.items) ? page.items : [], nextCursor: page?.nextCursor ?? null, hasNext: page?.hasNext ?? false };
}

export async function getAlterationJob(context: StudioEndpointContext, planId: string, jobId: string): Promise<AlterationJob> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, alterationRelations.job, { planId, jobId });
  const job = await read(() => context.http.getJson<AlterationJob>(path));
  return { ...job, outcomes: Array.isArray(job?.outcomes) ? job.outcomes : [] };
}

/** The refusal code a finished job carries: its first failed outcome, else its safe failure. */
export function alterationJobFailureCode(job: Pick<AlterationJob, "outcomes" | "failure">): string | null {
  return job.outcomes.find(outcome => outcome.status === "Failed")?.code ?? job.failure?.code ?? null;
}

async function read<T>(load: () => Promise<T>): Promise<T> {
  try {
    return await load();
  } catch (error) {
    throw toAlterationRequestError(error);
  }
}

function readSubmission(value: unknown): AlterationPlanSubmission {
  const record = value as Partial<AlterationPlanSubmission> | null;
  if (!record || typeof record.planId !== "string" || !record.planId) {
    throw new AlterationRequestError(0, null, "Runtime returned an invalid alteration submission.");
  }
  return {
    planId: record.planId,
    status: String(record.status ?? ""),
    submissionDisposition: String(record.submissionDisposition ?? "Accepted"),
    createdAt: String(record.createdAt ?? "")
  };
}

async function alterationErrorFromResponse(response: Response) {
  const retryAfter = parseRetryAfter(response.headers.get("Retry-After"));
  const error = await createStudioHttpError(response);
  return new AlterationRequestError(error.status, problemCode(error.payload), error.message, retryAfter);
}

function toAlterationRequestError(error: unknown): unknown {
  if (error instanceof AlterationRequestError) return error;
  if (error instanceof StudioHttpError) return new AlterationRequestError(error.status, problemCode(error.payload), error.message);
  const status = typeof error === "object" && error !== null && "status" in error ? Number((error as { status?: unknown }).status) : NaN;
  if (Number.isInteger(status) && status > 0) {
    const payload = (error as { payload?: unknown }).payload;
    return new AlterationRequestError(status, problemCode(payload), error instanceof Error ? error.message : `Request failed with ${status}.`);
  }
  return error;
}

function problemCode(payload: unknown): string | null {
  if (!payload || typeof payload !== "object") return null;
  const code = (payload as { code?: unknown }).code;
  return typeof code === "string" && code ? code : null;
}

function parseRetryAfter(value: string | null): number | null {
  if (!value) return null;
  const seconds = Number(value.trim());
  if (Number.isFinite(seconds) && seconds >= 0) return Math.ceil(seconds);
  const date = Date.parse(value);
  return Number.isNaN(date) ? null : Math.max(0, Math.ceil((date - Date.now()) / 1000));
}
