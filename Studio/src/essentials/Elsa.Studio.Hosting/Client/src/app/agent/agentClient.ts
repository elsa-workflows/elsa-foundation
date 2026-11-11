// The transport uses the public SDK bundle; its error class must have the same runtime identity.
import { StudioHttpError } from "@elsa-workflows/studio-sdk";
import type { StudioWeaverPreparedInvocation, StudioWeaverToolResult, StudioEndpointContext } from "../../sdk";
import type {
  AgentClientAcknowledgement,
  AgentAcceptedPolicy,
  AgentActionProposal,
  AgentAutonomyMode,
  AgentAutonomyUpdateRequest,
  AgentBootstrapResponse,
  AgentProviderStatus,
  AgentProviderDiagnostics,
  AgentCreateSessionRequest,
  AgentCreateSessionResponse,
  AgentFeedbackRequest,
  AgentMessageRequest,
  AgentMessageResponse,
  AgentProposalDecisionRequest,
  AgentProposalDecisionResponse,
  AgentMessageStatusReason,
  AgentOwnerResolution,
  AgentOwnerResolutionClaim,
  AgentRecordedMessage,
  AgentRecordedProposal,
  AgentSessionDetailResponse,
  AgentSessionLimit,
  AgentSessionListQuery,
  AgentSessionListResponse,
  AgentSessionPolicy,
  AgentSessionSummary
} from "./agentTypes";

export interface AgentClient {
  bootstrap(): Promise<AgentBootstrapResponse>;
  createSession(request: AgentCreateSessionRequest): Promise<AgentCreateSessionResponse>;
  /** Reads the authoritative session detail, including its acknowledged policy when the backend owns one. */
  getSession(sessionId: string): Promise<AgentSessionDetailResponse>;
  /**
   * Preview (#3440): one conditional autonomy-only change. Resolves with the acknowledged policy;
   * refusals reject with an {@link AgentApiError} carrying the server's code, and carry no policy data.
   */
  updateAutonomy(sessionId: string, request: AgentAutonomyUpdateRequest): Promise<AgentSessionPolicy>;
  sendMessage(sessionId: string, request: AgentMessageRequest): Promise<AgentMessageResponse>;
  approveProposal(proposalId: string, request: AgentProposalDecisionRequest): Promise<AgentProposalDecisionResponse>;
  denyProposal(proposalId: string, request: AgentProposalDecisionRequest): Promise<AgentProposalDecisionResponse>;
  executeProposal(proposalId: string, request: AgentProposalDecisionRequest): Promise<AgentProposalDecisionResponse>;
  acknowledgeClient(sessionId: string, invocationId: string, request: AgentClientAcknowledgement): Promise<AgentProposalDecisionResponse>;
  submitFeedback(sessionId: string, messageId: string, request: AgentFeedbackRequest): Promise<void>;
  cancelTurn(sessionId: string, turnId: string): Promise<void>;
  /** Preview (#3441): the caller's own sessions, newest first; the server scopes them to the authenticated owner. */
  listSessions(query?: AgentSessionListQuery): Promise<AgentSessionListResponse>;
  /** Preview (#3441): the session's recorded proposals with their statuses, previews and deadlines. */
  listProposals(sessionId: string): Promise<AgentRecordedProposal[]>;
  /** Preview (#3441): withdraws a prompt that never started; the server refuses it once it started. */
  withdrawMessage(sessionId: string, messageId: string): Promise<AgentRecordedMessage>;
  /** Preview (#3441): records the owner's statement about an uncertain client operation. It is not a receipt. */
  resolveClientInvocation(sessionId: string, invocationId: string, claim: AgentOwnerResolutionClaim): Promise<AgentRecordedProposal>;
}

/** An HTTP refusal with the authenticated server's authoritative terminal operation outcome. */
export class AgentExecutionError extends StudioHttpError {
  constructor(status: number, message: string, public readonly code: string, public readonly terminalOutcome: AgentProposalDecisionResponse, payload: unknown) {
    super(status, message, null, payload);
    this.name = "AgentExecutionError";
  }
}

/** An HTTP refusal that carries the Weaver API's error code (for example `weaver.session.policy_stale`). */
export class AgentApiError extends StudioHttpError {
  constructor(status: number, message: string, public readonly code: string, payload: unknown, retryAfterSeconds: number | null = null) {
    super(status, message, null, payload, retryAfterSeconds);
    this.name = "AgentApiError";
  }
}

/**
 * Preview (#3441): how a recovery read or action ended when it did not succeed. Nothing was applied in any refused case.
 * - `owned-elsewhere`: another Studio host process holds the session's live lease; retry after the hint.
 * - `ownership-lost`: this host lost the session during the call; read it again.
 * - `unavailable`: the recorded state is unreadable or failed verification.
 * - `query-unsupported`: the backend cannot list sessions; reopen by identity instead.
 * - `not-pending`: the prompt already started, or a turn is running.
 * - `not-uncertain`: the operation is not uncertain, so it cannot be resolved.
 * - `resolution-conflict`: a different owner statement is already recorded.
 * - `proposal-conflict`: the operation changed after it was read; read it again and decide again.
 * - `policy-stale`: the session policy changed since the operation was approved.
 * - `not-found`: the shared not-found or forbidden outcome; nothing about the session is disclosed.
 * - `lost`: no authoritative answer arrived; the action may or may not have been recorded.
 */
export type AgentRecoveryFailure = "owned-elsewhere" | "ownership-lost" | "unavailable" | "query-unsupported" | "not-pending" | "not-uncertain"
  | "resolution-conflict" | "proposal-conflict" | "policy-stale" | "not-found" | "invalid" | "lost" | "refused";

const RECOVERY_FAILURES: Record<string, AgentRecoveryFailure> = {
  "weaver.session.owned_elsewhere": "owned-elsewhere",
  "weaver.session.ownership_lost": "ownership-lost",
  "weaver.session.unavailable_state": "unavailable",
  "weaver.session.query_unsupported": "query-unsupported",
  "weaver.message.not_pending": "not-pending",
  "weaver.invocation.not_uncertain": "not-uncertain",
  "weaver.invocation.resolution_conflict": "resolution-conflict",
  "weaver.invocation.resolution_invalid": "invalid",
  "weaver.proposal.conflict": "proposal-conflict",
  "weaver.proposal.policy_stale": "policy-stale",
  "weaver.session.not_found": "not-found",
  "weaver.session.forbidden": "not-found",
  "weaver.message.not_found": "not-found",
  "weaver.invocation.not_found": "not-found"
};

/** Classifies a failed recovery request; the shared 404 and 403 are one outcome, so a foreign session discloses nothing. */
export function classifyRecoveryFailure(error: unknown): AgentRecoveryFailure {
  if (!(error instanceof StudioHttpError)) return "lost";
  const code = getAgentErrorCode(error);
  if (code && RECOVERY_FAILURES[code]) return RECOVERY_FAILURES[code];
  if (error.status === 404 || error.status === 403) return "not-found";
  if (error.status === 400) return "invalid";
  if (error.status === 0 || error.status === 408 || error.status >= 500) return "lost";
  return "refused";
}

/** The retry hint of a retryable refusal, in whole seconds, when the server sent one. */
export function getAgentRetryAfterSeconds(error: unknown): number | undefined {
  return error instanceof StudioHttpError && typeof error.retryAfterSeconds === "number" ? error.retryAfterSeconds : undefined;
}

/** How an autonomy update request ended when it did not return an acknowledged policy. */
export type AgentAutonomyFailure = "lost" | "stale" | "busy" | "closed" | "unsupported" | "invalid" | "not-found" | "refused";

const AUTONOMY_FAILURES: Record<string, AgentAutonomyFailure> = {
  "weaver.session.policy_stale": "stale",
  "weaver.session.busy": "busy",
  "weaver.session.closed": "closed",
  "weaver.session.autonomy_unsupported": "unsupported",
  "weaver.session.autonomy_invalid": "invalid"
};

/**
 * Classifies a failed autonomy update. Only a transport failure, or a response that carries no
 * authoritative refusal (5xx, timeout, unreadable body), is "lost": the change may have committed.
 */
export function classifyAutonomyFailure(error: unknown): AgentAutonomyFailure {
  if (!(error instanceof StudioHttpError)) return "lost";
  const code = error instanceof AgentApiError ? error.code : readString(readRecord(error.payload, "error"), "code");
  if (code && AUTONOMY_FAILURES[code]) return AUTONOMY_FAILURES[code];
  if (error.status === 404) return "not-found";
  if (error.status === 400) return "invalid";
  if (error.status === 0 || error.status === 408 || error.status >= 500 || (error.status >= 200 && error.status < 300)) return "lost";
  return "refused";
}

/** Prompt admission lost to a committed policy change; the prompt was not accepted and is never resent automatically. */
export const POLICY_CHANGED = "weaver.message.policy_changed";

/** The error code a refused Agent request carried, when it carried one. */
export function getAgentErrorCode(error: unknown): string | undefined {
  if (error instanceof AgentApiError || error instanceof AgentExecutionError) return error.code;
  if (error instanceof StudioHttpError) return readString(readRecord(error.payload, "error"), "code");
  return undefined;
}

function toAgentApiError(error: unknown): unknown {
  if (!(error instanceof StudioHttpError) || error instanceof AgentApiError) return error;
  const failure = readRecord(error.payload, "error");
  const code = readString(failure, "code");
  return code ? new AgentApiError(error.status, readString(failure, "message") ?? error.message, code, error.payload, error.retryAfterSeconds) : error;
}

/** Unwraps an envelope, turning a wrapped (status 200) refusal into the same coded error as an HTTP refusal. */
function unwrapCoded<T>(envelope: AgentApiResponse<T>): T {
  const failure = readRecord(envelope, "error");
  const code = readString(failure, "code");
  if (failure && code) {
    const status = readField(failure, "statusCode");
    throw new AgentApiError(typeof status === "number" ? status : 409, readString(failure, "message") ?? code, code, envelope);
  }
  return unwrap(envelope);
}

export function createAgentClient(context: StudioEndpointContext): AgentClient {
  const deliveryIds = new WeakMap<AgentMessageRequest, string>();

  async function postReviewedProposal<T>(proposalId: string, decision: "approve" | "deny" | "execute", request: Record<string, unknown>): Promise<T> {
    let envelope: AgentApiResponse<T>;
    try {
      envelope = await context.http.postJson<AgentApiResponse<T>>(`/_elsa/weaver/proposals/${encodeURIComponent(proposalId)}/${decision}`, request);
    } catch (error) {
      if (error instanceof StudioHttpError) throw terminalExecutionError(error.payload, proposalId, error.status) ?? error;
      throw error;
    }
    const terminal = terminalExecutionError(envelope, proposalId);
    if (terminal) throw terminal;
    return unwrap(envelope);
  }

  return {
    async bootstrap() {
      // A single agent harness is active server-side; the client never chooses among providers.
      return mapBootstrap(unwrap(await context.http.getJson<AgentApiResponse<BackendAgentBootstrapResponse>>("/_elsa/weaver/bootstrap")));
    },
    async createSession(request) {
      return mapSession(unwrap(await context.http.postJson<AgentApiResponse<BackendAgentSession>>("/_elsa/weaver/sessions", {
        conversationId: request.activeSurface.resourceId ?? request.activeSurface.route,
        mode: request.mode,
        autonomyMode: request.autonomyMode,
        activeSurface: request.activeSurface,
        clientContext: request.clientContext,
        ...(request.workspace ? { workspace: request.workspace } : {}),
        ...(request.continuedFrom ? { continuedFrom: request.continuedFrom } : {}),
        metadata: {
          mode: request.mode,
          route: request.activeSurface.route,
          resourceType: request.activeSurface.resourceType ?? "",
          resourceId: request.activeSurface.resourceId ?? "",
          studioVersion: request.clientContext.studioVersion,
          sdkVersion: request.clientContext.sdkVersion,
          moduleIds: request.clientContext.moduleIds.join(",")
        }
      })));
    },
    async getSession(sessionId) {
      let envelope: AgentApiResponse<BackendAgentSession>;
      try {
        envelope = await context.http.getJson<AgentApiResponse<BackendAgentSession>>(`/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}`);
      } catch (error) { throw toAgentApiError(error); }
      const detail = unwrapCoded(envelope);
      const title = readString(detail, "title");
      const limit = readSessionLimit(readString(detail, "limitReached"));
      return {
        sessionId: readString(detail, "sessionId") ?? readString(detail, "id") ?? sessionId,
        status: mapSessionStatus(readField(detail, "status")),
        ...(title ? { title } : {}),
        policy: mapSessionPolicy(readRecord(detail, "policy")),
        messages: readRecordArray(detail, "messages").map(mapRecordedMessage),
        ...(limit ? { limit } : {})
      };
    },
    async updateAutonomy(sessionId, request) {
      let envelope: AgentApiResponse<unknown>;
      try {
        // Only the requested mode and the last acknowledged revision are sent; every other policy field is the owner's.
        envelope = await context.http.putJson<AgentApiResponse<unknown>>(`/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}/autonomy`, {
          autonomyMode: request.autonomyMode,
          expectedRevision: request.expectedRevision
        });
      } catch (error) { throw toAgentApiError(error); }
      const data = unwrapCoded(envelope);
      const policy = mapSessionPolicy(isRecord(data) ? data : undefined);
      if (!policy) throw new StudioHttpError(200, "The server acknowledged the autonomy change without a readable policy.");
      return policy;
    },
    async sendMessage(sessionId, request) {
      let requestId = request.requestId ?? deliveryIds.get(request);
      if (!requestId) { requestId = globalThis.crypto?.randomUUID?.() ?? `message-${Date.now()}-${Math.random()}`; deliveryIds.set(request, requestId); }
      let envelope: AgentApiResponse<BackendAgentMessageAcceptedResponse>;
      try {
        envelope = await context.http.postJson<AgentApiResponse<BackendAgentMessageAcceptedResponse>>(`/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}/messages`, {
          role: "user",
          content: request.message,
          message: request.message,
          mode: request.mode,
          capabilityId: request.capabilityId,
          requestId,
          source: request.source,
          promptMode: request.promptMode,
          activeSurface: request.activeSurface,
          requestedCapabilities: request.capabilityId ? [request.capabilityId] : [],
          contextAttachments: request.contextAttachments.map(mapContextAttachment)
        });
      } catch (error) {
        // Only a policy conflict changes the thrown shape: callers read the session back and the user resends.
        const coded = toAgentApiError(error);
        throw coded instanceof AgentApiError && coded.code === POLICY_CHANGED ? coded : error;
      }
      const failure = readRecord(envelope, "error");
      // Keep the historical wrapped-error message, but carry a policy conflict's code so callers can read back.
      if (readString(failure, "code") === POLICY_CHANGED) unwrapCoded(envelope);
      const accepted = unwrap(envelope);

      return {
        messageId: getAcceptedMessageId(accepted),
        status: mapMessageStatus(readField(readRecord(accepted, "message"), "status")),
        contextAttachments: readRecordArray(readRecord(accepted, "message"), "context").map(mapAdmittedContextAttachment),
        warnings: readRecordArray(accepted, "warnings").map(item => ({ code: readString(item, "code") ?? "weaver.context", message: readString(item, "message") ?? "Context admission limitation.", target: readString(item, "target") })),
        streamUrl: `/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}/stream`,
        acceptedPolicy: mapAcceptedPolicy(readRecord(readRecord(accepted, "message"), "acceptedPolicy"))
      };
    },
    async approveProposal(proposalId, request) {
      return mapProposalDecision(await postReviewedProposal<BackendAgentActionProposal>(proposalId, "approve", {
        actorId: "studio",
        revision: request.revision,
        comment: request.comment
      }));
    },
    async denyProposal(proposalId, request) {
      return mapProposalDecision(await postReviewedProposal<BackendAgentActionProposal>(proposalId, "deny", {
        actorId: "studio",
        revision: request.revision,
        comment: request.comment,
        reason: request.comment
      }));
    },
    async executeProposal(proposalId, request) {
      return mapExecution(await postReviewedProposal<BackendAgentProposalExecutionResult>(proposalId, "execute", {
        actorId: "studio", revision: request.revision, comment: request.comment
      }), proposalId);
    },
    async acknowledgeClient(sessionId, invocationId, request) {
      const result = unwrap(await context.http.postJson<AgentApiResponse<BackendAgentProposalExecutionResult>>(
        `/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}/client-invocations/${encodeURIComponent(invocationId)}/acknowledgements`, request));
      return mapExecution(result, invocationId);
    },
    async submitFeedback(sessionId, messageId, request) {
      unwrap(await context.http.postJson<AgentApiResponse<BackendAgentFeedback>>("/_elsa/weaver/feedback", {
        sessionId,
        messageId,
        rating: request.rating === "positive" ? 1 : -1,
        comment: request.comment,
        actorId: "studio"
      }));
    },
    async cancelTurn(sessionId, turnId) {
      unwrap(await context.http.postJson<AgentApiResponse<unknown>>(`/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}/turns/${encodeURIComponent(turnId)}/cancel`, {}));
    },
    async listSessions(query = {}) {
      const parameters = new URLSearchParams();
      if (query.workspace) parameters.set("workspace", query.workspace);
      if (query.take !== undefined) parameters.set("take", String(query.take));
      if (query.before) parameters.set("before", query.before);
      const search = parameters.toString();
      const page = await readCoded<unknown>(() => context.http.getJson(`/_elsa/weaver/sessions${search ? `?${search}` : ""}`));
      const next = readString(page, "next");
      return { items: readRecordArray(page, "items").map(mapSessionSummary), ...(next ? { next } : {}) };
    },
    async listProposals(sessionId) {
      const proposals = await readCoded<unknown>(() => context.http.getJson(`/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}/proposals`));
      return (Array.isArray(proposals) ? proposals.filter(isRecord) : []).map(mapRecordedProposal);
    },
    async withdrawMessage(sessionId, messageId) {
      return mapRecordedMessage(await readCoded<Record<string, unknown>>(() => context.http.postJson(
        `/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}/messages/${encodeURIComponent(messageId)}/withdraw`, {})));
    },
    async resolveClientInvocation(sessionId, invocationId, claim) {
      return mapRecordedProposal(await readCoded<Record<string, unknown>>(() => context.http.postJson(
        `/_elsa/weaver/sessions/${encodeURIComponent(sessionId)}/client-invocations/${encodeURIComponent(invocationId)}/resolution`,
        { claim: claim === "not-applied" ? "notApplied" : claim })));
    }
  };
}

/** Sends a recovery request; an HTTP or wrapped refusal becomes an {@link AgentApiError} with the server's code. */
async function readCoded<T>(send: () => Promise<AgentApiResponse<T>>): Promise<T> {
  let envelope: AgentApiResponse<T>;
  try { envelope = await send(); } catch (error) { throw toAgentApiError(error); }
  return unwrapCoded(envelope);
}

const MESSAGE_STATUS_REASONS: AgentMessageStatusReason[] = ["interrupted", "withdrawn", "user-stop"];

function mapRecordedMessage(message: Record<string, unknown>): AgentRecordedMessage {
  const revision = readField(message, "acceptedPolicyRevision");
  const reason = normalizeEnum(readField(message, "statusReason"));
  const content = readString(message, "content");
  const accepted = mapAcceptedPolicy(readRecord(message, "acceptedPolicy"));
  return {
    id: readString(message, "id") ?? "",
    role: normalizeEnum(readField(message, "role")),
    status: normalizeEnum(readField(message, "status")),
    ...(content !== undefined ? { content } : {}),
    ...(MESSAGE_STATUS_REASONS.includes(reason as AgentMessageStatusReason) ? { statusReason: reason as AgentMessageStatusReason } : {}),
    acceptedPolicyRevision: typeof revision === "number" ? revision : accepted?.revision,
    ...(accepted ? { acceptedPolicy: accepted } : {})
  };
}

function mapSessionSummary(summary: Record<string, unknown>): AgentSessionSummary {
  const title = readString(summary, "title");
  const expiresAt = readString(summary, "expiresAt");
  const recoveryState = readString(summary, "recoveryState");
  const pending = readField(summary, "pendingReviewCount");
  return {
    id: readString(summary, "id") ?? readString(summary, "sessionId") ?? "",
    status: mapSessionStatus(readField(summary, "status")),
    updatedAt: readString(summary, "updatedAt") ?? "",
    pendingReviewCount: typeof pending === "number" ? pending : 0,
    ...(title ? { title } : {}),
    ...(expiresAt ? { expiresAt } : {}),
    ...(recoveryState ? { recoveryState } : {})
  };
}

function mapOwnerResolution(resolution: Record<string, unknown> | undefined): AgentOwnerResolution | undefined {
  const claim = normalizeEnum(readField(resolution, "claim"));
  if (claim !== "applied" && claim !== "not-applied" && claim !== "unknown") return undefined;
  const actorId = readString(resolution, "actorId");
  const resolvedAt = readString(resolution, "resolvedAt");
  return { claim, ...(actorId ? { actorId } : {}), ...(resolvedAt ? { resolvedAt } : {}) };
}

/** A recorded proposal as the stream's proposal payload shape, so the surface normalizes both the same way. */
function mapRecordedProposal(proposal: Record<string, unknown>): AgentRecordedProposal {
  const prepared = readRecord(proposal, "preparedInvocation") as StudioWeaverPreparedInvocation | undefined;
  const ownerResolution = mapOwnerResolution(readRecord(proposal, "ownerResolution"));
  const result = readField(proposal, "result");
  const target = readRecord(proposal, "resourceTarget");
  return {
    ...(proposal as Partial<AgentRecordedProposal>),
    id: readString(proposal, "id") ?? readString(proposal, "proposalId") ?? "",
    status: mapProposalStatus(readField(proposal, "status")),
    title: readString(proposal, "title"),
    summary: readString(proposal, "summary"),
    baseRevision: readString(proposal, "baseRevision"),
    preparedInvocation: prepared,
    resourceTarget: target as AgentRecordedProposal["resourceTarget"],
    result: result === null ? undefined : result,
    expiresAt: prepared?.expiresAt,
    ownerResolution
  };
}

function terminalExecutionError(envelope: unknown, proposalId: string, status?: number): AgentExecutionError | null {
  const error = readRecord(envelope, "error"); const data = readRecord(envelope, "data");
  if (!error || !data || readString(data, "proposalId") !== proposalId || readBoolean(data, "executed") !== false) return null;
  const outcome = normalizeEnum(readField(data, "status"));
  if (!["expired", "failed", "denied", "cancelled"].includes(outcome)) return null;
  const errorStatus = readField(error, "statusCode");
  return new AgentExecutionError(status ?? (typeof errorStatus === "number" ? errorStatus : 409),
    readString(error, "message") ?? "The server refused the reviewed execution.", readString(error, "code") ?? "weaver.execution.refused",
    mapExecution(data as BackendAgentProposalExecutionResult, proposalId), envelope);
}

interface AgentApiResponse<T> {
  data?: T | null;
  error?: { code: string; message: string; statusCode?: number } | null;
}

interface BackendAgentBootstrapResponse {
  enabled?: boolean;
  providerStatus?: AgentBootstrapResponse["providerStatus"];
  modes?: AgentBootstrapResponse["modes"];
  capabilities?: BackendAgentCapability[];
  policy?: AgentBootstrapResponse["policy"];
  provider?: BackendAgentProviderDiagnostics | null;
}

interface BackendAgentCapability {
  id: string;
  moduleId?: string | null;
  displayName: string;
  description: string;
  kind?: string | number;
  risk?: string | number;
  surfaces?: string[];
  requiredPermissions?: string[];
  requiresApproval?: boolean;
}

interface BackendAgentProviderDiagnostics {
  providerId: string;
  isAvailable: boolean;
  status: string;
  providerKind?: string | number;
  kind?: string | number;
  supportedOperations?: Array<string | number>;
  riskProfile?: string | number;
  metadata?: Record<string, string>;
}

interface BackendAgentSession {
  id?: string;
  title?: string;
  sessionId?: string;
  status?: string | number;
  contextAttachments?: unknown[];
}

interface BackendAgentMessageAcceptedResponse {
  message?: BackendAgentMessage;
  messageId?: string;
}

interface BackendAgentMessage {
  id: string;
}

interface BackendAgentActionProposal {
  id?: string;
  proposalId?: string;
  status?: string | number;
  approvalStatus?: string | number;
  approvedAt?: string | null;
}

interface BackendAgentProposalExecutionResult {
  clientInvocation?: StudioWeaverPreparedInvocation;
  toolResult?: StudioWeaverToolResult;
  resourceType?: string;
  resourceId?: string;
  proposalId?: string;
  executed?: boolean;
  message?: string;
  summary?: string;
  status?: string | number;
  approvalStatus?: string | number;
}

interface BackendAgentFeedback {
  id: string;
}

function unwrap<T>(response: AgentApiResponse<T>): T {
  const error = readRecord(response, "error");
  if (error) {
    throw new Error(readString(error, "message") || readString(error, "code") || "Agent backend returned an error.");
  }

  const data = readField(response, "data");
  if (data === undefined || data === null) {
    throw new Error("Agent backend returned an empty response.");
  }

  return data as T;
}

function mapBootstrap(response: BackendAgentBootstrapResponse): AgentBootstrapResponse {
  const providerRecord = readRecord(response, "provider");
  const provider = providerRecord ? mapProviderDiagnostics(providerRecord) : undefined;
  const capabilities = readRecordArray(response, "capabilities");
  const providerStatus = mapProviderStatus(readField(response, "providerStatus"), provider);
  const recoveryValue = readString(response, "recovery");
  const recovery = recoveryValue === "durable" || recoveryValue === "processLifetime" || recoveryValue === "unsupported" ? recoveryValue : undefined;
  return {
    enabled: readBoolean(response, "enabled") ?? providerStatus === "available",
    providerStatus,
    modes: readStringArray(response, "modes") as AgentBootstrapResponse["modes"] ?? ["explain", "troubleshoot", "build"],
    capabilities: capabilities.map(mapCapability),
    provider,
    policy: mapPolicy(readRecord(response, "policy")),
    ...(recovery ? { recovery } : {})
  };
}

const DEFAULT_AUTONOMY_MODE: AgentAutonomyMode = "auto-read-only";
const AUTONOMY_MODES: AgentAutonomyMode[] = ["manual", "auto-read-only", "full-auto"];

function mapPolicy(policy: Record<string, unknown> | undefined): AgentBootstrapResponse["policy"] {
  const maxAutonomyMode = mapAutonomyMode(readField(policy, "maxAutonomyMode")) ?? DEFAULT_AUTONOMY_MODE;
  const defaultAutonomyMode = clampAutonomyMode(mapAutonomyMode(readField(policy, "defaultAutonomyMode")) ?? maxAutonomyMode, maxAutonomyMode);
  const allowed = readStringArray(policy, "allowedAutonomyModes")
    ?.map(mapAutonomyMode)
    .filter((mode): mode is AgentAutonomyMode => mode !== undefined);
  return {
    ...(policy as AgentBootstrapResponse["policy"]),
    contextVisibility: readBoolean(policy, "contextVisibility") ?? true,
    defaultAutonomyMode,
    maxAutonomyMode,
    allowedAutonomyModes: allowed?.length ? allowed : allowedModesUpTo(maxAutonomyMode),
    retentionLabel: readString(policy, "retentionLabel")
  };
}

function mapAutonomyMode(value: unknown): AgentAutonomyMode | undefined {
  const normalized = normalizeEnum(value);
  return AUTONOMY_MODES.includes(normalized as AgentAutonomyMode) ? normalized as AgentAutonomyMode : undefined;
}

function clampAutonomyMode(requested: AgentAutonomyMode, ceiling: AgentAutonomyMode): AgentAutonomyMode {
  return AUTONOMY_MODES.indexOf(requested) <= AUTONOMY_MODES.indexOf(ceiling) ? requested : ceiling;
}

function allowedModesUpTo(ceiling: AgentAutonomyMode): AgentAutonomyMode[] {
  return AUTONOMY_MODES.slice(0, AUTONOMY_MODES.indexOf(ceiling) + 1);
}

function mapCapability(capability: Record<string, unknown>): AgentBootstrapResponse["capabilities"][number] {
  const id = readString(capability, "id") ?? "";
  const surfaces = readStringArray(capability, "surfaces");
  const requiresApproval = readBoolean(capability, "requiresApproval");
  return {
    id,
    moduleId: readString(capability, "moduleId"),
    displayName: readString(capability, "displayName") ?? (id || "Agent capability"),
    description: readString(capability, "description") ?? "",
    kind: mapCapabilityKind(readField(capability, "kind"), requiresApproval),
    risk: mapCapabilityRisk(readField(capability, "risk"), requiresApproval),
    surfaces: surfaces?.length ? surfaces : ["*"],
    requiredPermissions: readStringArray(capability, "requiredPermissions")
  };
}

function mapProviderStatus(status: unknown, provider: AgentProviderDiagnostics | undefined): AgentProviderStatus {
  const value = normalizeEnum(status);
  if (value === "available" || value === "unavailable" || value === "disabled" || value === "degraded") {
    return value;
  }

  return provider?.isAvailable ? "available" : "unavailable";
}

function mapSession(session: BackendAgentSession): AgentCreateSessionResponse {
  const policy = mapSessionPolicy(readRecord(session, "policy"));
  return {
    sessionId: readString(session, "sessionId") ?? readString(session, "id") ?? "",
    status: mapSessionStatus(readField(session, "status")),
    contextAttachments: [],
    ...(policy ? { policy } : {})
  };
}

/** Maps the additive session policy; an absent or unreadable one stays absent rather than defaulting. */
function mapSessionPolicy(policy: Record<string, unknown> | undefined): AgentSessionPolicy | undefined {
  const autonomyMode = mapAutonomyMode(readField(policy, "autonomyMode"));
  const revision = readField(policy, "revision");
  if (!autonomyMode || typeof revision !== "number" || !Number.isFinite(revision)) return undefined;
  const maxAutonomyMode = mapAutonomyMode(readField(policy, "maxAutonomyMode")) ?? autonomyMode;
  const allowed = readStringArray(policy, "allowedAutonomyModes")
    ?.map(mapAutonomyMode)
    .filter((mode): mode is AgentAutonomyMode => mode !== undefined);
  return { autonomyMode, maxAutonomyMode, allowedAutonomyModes: allowed?.length ? allowed : allowedModesUpTo(maxAutonomyMode), revision };
}

/**
 * The accepted message's public `acceptedPolicy` (the session-policy shape: mode, ceiling, allowed modes and
 * revision) as the mode and revision that admitted it. Private policy data never reaches the client.
 */
function mapAcceptedPolicy(accepted: Record<string, unknown> | undefined): AgentAcceptedPolicy | undefined {
  const revision = readField(accepted, "revision");
  if (typeof revision !== "number") return undefined;
  const autonomyMode = mapAutonomyMode(readField(accepted, "autonomyMode"));
  return autonomyMode ? { autonomyMode, revision } : { revision };
}

function mapContextAttachment(attachment: AgentMessageRequest["contextAttachments"][number]) {
  const redacted = attachment.sensitivity === "secret-redacted";
  return {
    ...attachment,
    sensitivity: mapSensitivity(attachment.sensitivity),
    summary: redacted ? "[secret redacted]" : attachment.summary ?? (typeof attachment.content === "string" ? attachment.content : attachment.label),
    content: redacted ? undefined : attachment.content,
    references: attachment.references ?? {}
  };
}

/** Shared normalization for accepted-message and current-disclosure stream envelopes. */
export function mapAdmittedContextAttachment(attachment: Record<string, unknown>): AgentMessageRequest["contextAttachments"][number] {
  const sensitivities = ["public", "internal", "sensitive", "secret-redacted", "secret-redacted"] as const;
  const sensitivityNames: Record<string, AgentMessageRequest["contextAttachments"][number]["sensitivity"]> = { public: "public", internal: "internal", sensitive: "sensitive", confidential: "sensitive", "secret-redacted": "secret-redacted", secret: "secret-redacted" };
  const rawSensitivity = readField(attachment, "sensitivity");
  const sensitivity = typeof rawSensitivity === "number" ? sensitivities[rawSensitivity] ?? "secret-redacted"
    : sensitivityNames[normalizeEnum(rawSensitivity)] ?? "secret-redacted";
  const origins = ["clientSnapshot", "resourceData", "authoritativeRead", "installedGuidance"] as const;
  const rawOrigin = readField(attachment, "origin");
  const origin = typeof rawOrigin === "number" ? origins[rawOrigin]
    : origins.find(item => normalizeEnum(item) === normalizeEnum(rawOrigin));
  const redacted = sensitivity === "secret-redacted";
  const target = readRecord(attachment, "target");
  const client = readRecord(attachment, "client");
  return {
    id: readString(attachment, "id") ?? "",
    source: readString(attachment, "source") ?? "",
    sourceId: redacted ? undefined : readString(attachment, "sourceId"),
    label: readString(attachment, "label") ?? "Admitted context",
    contentType: readString(attachment, "contentType") ?? "",
    sensitivity,
    scope: readString(attachment, "scope") ?? "",
    summary: readString(attachment, "summary"),
    content: redacted ? undefined : readField(attachment, "content"),
    references: redacted ? {} : readRecord(attachment, "references") as Record<string, string> | undefined,
    moduleId: readString(attachment, "moduleId"),
    schemaVersion: readString(attachment, "schemaVersion"),
    target: redacted || !target ? null : { moduleId: readString(target, "moduleId") ?? "", resourceType: readString(target, "resourceType") ?? "", resourceId: readString(target, "resourceId") ?? null, revision: readString(target, "revision") ?? null, displayName: readString(target, "displayName"), route: readString(target, "route") },
    origin,
    client: redacted || !client ? null : { clientId: readString(client, "clientId") ?? "", generationId: readString(client, "generationId") ?? "" },
    agentScopes: redacted ? [] : readStringArray(attachment, "agentScopes")
  };
}

function mapExecution(result: BackendAgentProposalExecutionResult, fallbackId: string): AgentProposalDecisionResponse {
  return {
    proposalId: readString(result, "proposalId") ?? fallbackId,
    approvalStatus: getExecutionStatus(result),
    executed: readBoolean(result, "executed"),
    clientInvocation: readField(result, "clientInvocation") as StudioWeaverPreparedInvocation | undefined,
    toolResult: readField(result, "toolResult") as StudioWeaverToolResult | undefined,
    result: {
      resourceType: readString(result, "resourceType") ?? "agent-proposal",
      resourceId: readString(result, "resourceId") ?? readString(result, "proposalId") ?? fallbackId,
      summary: readString(result, "summary") ?? readString(result, "message") ?? "Execution outcome unavailable."
    }
  };
}

function mapProviderDiagnostics(provider: unknown): AgentProviderDiagnostics {
  return {
    providerId: readString(provider, "providerId") ?? "",
    isAvailable: readBoolean(provider, "isAvailable") ?? false,
    status: readString(provider, "status") ?? "unavailable",
    providerKind: mapProviderKind(readField(provider, "providerKind") ?? readField(provider, "kind")),
    supportedOperations: readEnumArray(provider, "supportedOperations").map(mapProviderOperation),
    riskProfile: mapProviderRiskProfile(readField(provider, "riskProfile")),
    metadata: readField(provider, "metadata") as Record<string, string> | undefined ?? {}
  };
}

function mapCapabilityKind(kind: unknown, requiresApproval?: boolean): AgentBootstrapResponse["capabilities"][number]["kind"] {
  const value = normalizeEnum(kind);
  if (value === "context" || kind === 1) return "context";
  if (value === "prompt-starter" || kind === 2) return "prompt-starter";
  if (value === "proposal" || kind === 3) return "proposal";
  if (value === "action" || kind === 4) return "action";
  return requiresApproval ? "proposal" : "answer";
}

function mapCapabilityRisk(risk: unknown, requiresApproval?: boolean): AgentBootstrapResponse["capabilities"][number]["risk"] {
  const value = normalizeEnum(risk);
  if (value === "review-required" || risk === 1) return "review-required";
  if (value === "destructive" || risk === 2) return "destructive";
  if (value === "admin" || risk === 3) return "admin";
  return requiresApproval ? "review-required" : "read-only";
}

function mapProviderKind(kind: unknown) {
  const value = normalizeEnum(kind);
  if (value === "provider-sdk-binding" || kind === 0) return "provider-sdk-binding";
  if (value === "agent-harness-provider" || kind === 1) return "agent-harness-provider";
  return value || "provider-sdk-binding";
}

function mapProviderOperation(operation: string | number) {
  const value = normalizeEnum(operation);
  const numeric = ["chat", "streaming", "tool-approval", "run-status", "artifacts", "skills", "memory", "file-upload"][Number(operation)];
  return numeric ?? value;
}

function mapProviderRiskProfile(riskProfile: unknown) {
  const value = normalizeEnum(riskProfile);
  if (value === "read-only" || riskProfile === 0) return "read-only";
  if (value === "review-required" || riskProfile === 1) return "review-required";
  if (value === "sandboxed-execution" || riskProfile === 2) return "sandboxed-execution";
  if (value === "privileged-execution" || riskProfile === 3) return "privileged-execution";
  return value || "read-only";
}

function mapProposalDecision(proposal: BackendAgentActionProposal): AgentProposalDecisionResponse {
  return {
    proposalId: readString(proposal, "proposalId") ?? readString(proposal, "id") ?? "",
    approvalStatus: mapProposalStatus(readField(proposal, "approvalStatus") ?? readField(proposal, "status")),
    approvedAt: readString(proposal, "approvedAt")
  };
}

function getExecutionStatus(result: BackendAgentProposalExecutionResult): AgentActionProposal["status"] {
  const status = normalizeEnum(readField(result, "status") ?? readField(result, "approvalStatus"));
  if (status === "awaiting-client" || status === "uncertain" || status === "cancelled" || status === "expired" || status === "failed" || status === "denied") return status;
  const executed = readBoolean(result, "executed");
  if (executed !== undefined) return executed ? "executed" : "failed";
  return mapProposalStatus(status);
}

function mapMessageStatus(status: unknown): AgentMessageResponse["status"] {
  if (typeof status === "number") return (["pending", "streaming", "completed", "failed", "cancelled"] as const)[status] ?? "failed";
  const value = normalizeEnum(status);
  return ["pending", "streaming", "completed", "failed", "cancelled"].includes(value) ? value as AgentMessageResponse["status"] : "pending";
}

function getAcceptedMessageId(accepted: BackendAgentMessageAcceptedResponse) {
  const message = readRecord(accepted, "message");
  const messageId = readString(message, "id") ?? readString(accepted, "messageId");
  if (!messageId) {
    throw new Error("Agent backend did not return an accepted message id.");
  }

  return messageId;
}

/** Preview (#3441): only the documented limits are kept; anything else is dropped. */
function readSessionLimit(value: string | undefined): AgentSessionLimit | undefined {
  return value === "snapshots" || value === "bytes" ? value : undefined;
}

function mapSessionStatus(status: unknown): AgentCreateSessionResponse["status"] {
  const value = normalizeEnum(status);
  if (value === "completed" || value === "cancelled" || value === "expired") return value;
  if (value === "failed" || status === 2) return "failed";
  if (value === "archived" || status === 1) return "completed";
  return "active";
}

function mapProposalStatus(status: unknown): AgentActionProposal["status"] {
  const value = normalizeEnum(status);
  if (["draft", "edited", "expired", "cancelled", "executing", "awaiting-client", "uncertain"].includes(value)) return value as AgentActionProposal["status"];
  if (value === "approved" || status === 1) return "approved";
  if (value === "denied" || status === 2) return "denied";
  if (value === "executed" || status === 3) return "executed";
  if (value === "failed" || status === 4) return "failed";
  return "awaiting-approval";
}

function mapSensitivity(sensitivity: AgentMessageRequest["contextAttachments"][number]["sensitivity"]) {
  if (sensitivity === "secret-redacted") return "secret";
  if (sensitivity === "sensitive") return "confidential";
  return sensitivity;
}

function normalizeEnum(value: unknown) {
  if (value === undefined || value === null) return "";
  return String(value).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

function readString(source: unknown, key: string) {
  const value = readField(source, key);
  return typeof value === "string" ? value : undefined;
}

function readBoolean(source: unknown, key: string) {
  const value = readField(source, key);
  return typeof value === "boolean" ? value : undefined;
}

function readStringArray(source: unknown, key: string) {
  const value = readField(source, key);
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : undefined;
}

function readEnumArray(source: unknown, key: string) {
  const value = readField(source, key);
  return Array.isArray(value) ? value.filter((item): item is string | number => typeof item === "string" || typeof item === "number") : [];
}

function readRecord(source: unknown, key: string) {
  const value = readField(source, key);
  return isRecord(value) ? value : undefined;
}

function readRecordArray(source: unknown, key: string) {
  const value = readField(source, key);
  return Array.isArray(value) ? value.filter(isRecord) : [];
}

function readField(source: unknown, key: string) {
  if (!isRecord(source)) return undefined;
  if (key in source) return source[key];
  const lowered = key.toLowerCase();
  const match = Object.keys(source).find(candidate => candidate.toLowerCase() === lowered);
  return match ? source[match] : undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
