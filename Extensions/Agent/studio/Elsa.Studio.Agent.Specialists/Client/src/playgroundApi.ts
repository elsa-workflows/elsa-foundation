import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";

// The playground adds no execution path: runs start, are polled and are cancelled through the generic draft Test Run
// routes (capability elsa.api.publishing v1), and the specialist evidence beside them comes from
// elsa.api.agent-specialist-inspection v1.
const draftPath = (draftId: string) => `/design/activities/drafts/${encodeURIComponent(draftId)}`;
const startPath = (draftId: string) => `/publishing/activity-drafts/${encodeURIComponent(draftId)}/test-runs`;
const cancelPath = (testRunId: string) => `/publishing/activity-test-runs/${encodeURIComponent(testRunId)}/cancel`;
const evidencePath = (testRunId: string) => `/agent/specialists/test-runs/${encodeURIComponent(testRunId)}/evidence`;

/** The invocation-controls input a continuity-enabled specialist contract carries (SpecialistInvocationControls.InputKey). */
export const optionsInputKey = "$specialist.options";

export interface PlaygroundInput {
  referenceKey: string;
  name: string;
  type: { alias: string; collectionKind?: string };
  isRequired: boolean;
  isNullable: boolean;
  displayName?: string | null;
  description?: string | null;
}

export interface PlaygroundDraft {
  draftId: string;
  definitionId: string;
  revision: number;
  contract: { inputs: PlaygroundInput[] };
  provider: { providerKey: string; schemaVersion: string; payload?: { allowContinuity?: boolean } | null };
}

export interface ConversationReference {
  conversationId: string;
  revision: number;
}

export type TurnStatus = "Prepared" | "Claimed" | "Dispatching" | "Accepted" | "Failed" | "OutcomeUnknown" | "Cancelled";
export type ApplicationStatus = "NotAccepted" | "Pending" | "SettlementPending" | "Applied" | "NotApplied";

export interface InvocationEvidence {
  workflowExecutionId: string;
  activityExecutionId: string;
  specialist: { definitionId: string; definitionVersionId: string; version: string; templateHash: string; artifactId: string; artifactHash: string; allowContinuity: boolean };
  model: { providerKey: string; connectionName: string; modelId: string };
  limits: { maximumModelCalls: number; timeoutMilliseconds: number; maximumOutputTokens?: number | null };
  activityStatus: string;
  fault?: { code: string; message?: string | null } | null;
  output?: { captureMode: string; value?: unknown } | null;
  turn?: {
    status: TurnStatus;
    failureCode?: string | null;
    application: ApplicationStatus;
    deadline: string;
    maximumModelCalls: number;
    modelRequests: Array<{ id: string; reservedAt: string; maximumOutputTokens: number; providerKey: string; configuredModelId: string }>;
    usage?: { observedAt: string; usageAvailable: boolean; inputTokens?: number | null; outputTokens?: number | null; totalTokens?: number | null; observedModelId?: string | null } | null;
    conversation?: { conversationId: string; baseRevision: number; currentRevision?: number | null; outcomeUnknown: boolean; turnActive: boolean } | null;
    contentPurgedAt?: string | null;
  } | null;
}

export interface PlaygroundEvidence {
  testRunId: string;
  draftId: string;
  draftRevision: number;
  artifactId?: string | null;
  workflowExecutionId: string;
  status: string;
  invocation?: InvocationEvidence | null;
  /** False when the caller may not see captured values; the result's value is then left out. */
  valuesPermitted?: boolean;
}

export function getDraft(context: StudioEndpointContext, draftId: string, signal?: AbortSignal) {
  return context.http.getJson<PlaygroundDraft>(draftPath(draftId), { signal });
}

/** True when the saved draft offers explicit continuation: a schema 2 manifest that allows it, with the controls input. */
export function allowsContinuity(draft: PlaygroundDraft) {
  return draft.provider.payload?.allowContinuity === true && draft.contract.inputs.some(input => input.referenceKey === optionsInputKey);
}

/** The business inputs an author supplies; the invocation controls are the playground's own conversation choice. */
export function businessInputs(draft: PlaygroundDraft) {
  return draft.contract.inputs.filter(input => input.referenceKey !== optionsInputKey);
}

/**
 * Starts one run of the exact saved revision. Fresh is the default; continuation names one conversation and the revision
 * it expects. The generic Test Run binds only the inputs it is given, so the controls are always sent, empty when fresh.
 */
export function startPlaygroundRun(context: StudioEndpointContext, draft: PlaygroundDraft, values: Record<string, unknown>,
  continuation: ConversationReference | null, idempotencyKey: string) {
  const inputs: Record<string, { state: "Present"; value: unknown }> = {};
  for (const input of businessInputs(draft)) if (input.referenceKey in values) inputs[input.referenceKey] = { state: "Present", value: values[input.referenceKey] };
  if (draft.contract.inputs.some(input => input.referenceKey === optionsInputKey))
    inputs[optionsInputKey] = { state: "Present", value: continuation ? { conversationId: continuation.conversationId, revision: continuation.revision } : {} };
  return context.http.postJson<{ testRunId: string; status: string; reason?: string | null;
    failure?: { code: string; message: string; diagnostics?: Array<{ code: string; message: string }> } | null }>(
    startPath(draft.draftId), { expectedRevision: draft.revision, idempotencyKey, inputs });
}

export function getEvidence(context: StudioEndpointContext, testRunId: string, signal?: AbortSignal) {
  return context.http.getJson<PlaygroundEvidence>(evidencePath(testRunId), { signal });
}

export function cancelRun(context: StudioEndpointContext, testRunId: string) {
  return context.http.postJson<{ status: string; cancellation?: { status: string; reason?: string | null } }>(cancelPath(testRunId), {});
}

export function newIdempotencyKey() {
  return `playground-${globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`}`;
}

/** Terminal generic Test Run statuses; a dispatch-ambiguous run keeps polling until the server reconciles it. */
export function isTerminal(status: string) {
  return ["completed", "finished", "faulted", "failed", "cancelled", "canceled", "validationrejected", "dispatchrejected", "expired"].includes(status.toLowerCase());
}

/** The conversation an accepted turn advanced, which a later run of the same saved revision may continue. */
export function acceptedConversation(evidence: PlaygroundEvidence): ConversationReference | null {
  const turn = evidence.invocation?.turn;
  const conversation = turn?.conversation;
  if (turn?.status !== "Accepted" || !conversation || conversation.currentRevision == null) return null;
  return { conversationId: conversation.conversationId, revision: conversation.currentRevision };
}

const invocationEvidencePath = (workflowExecutionId: string, activityExecutionId: string) =>
  `/agent/specialists/invocations/${encodeURIComponent(workflowExecutionId)}/${encodeURIComponent(activityExecutionId)}/evidence`;

export interface InvocationEvidenceView {
  invocation: InvocationEvidence;
  /** False when the caller may not see captured values; the result's value is then left out. */
  valuesPermitted: boolean;
}

/** One runtime occurrence's evidence; a run the caller may not see or inspect reads as not found (404). */
export function getInvocationEvidence(context: StudioEndpointContext, workflowExecutionId: string, activityExecutionId: string, signal?: AbortSignal) {
  return context.http.getJson<InvocationEvidenceView>(invocationEvidencePath(workflowExecutionId, activityExecutionId), { signal });
}
