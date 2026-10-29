import type { StudioAgentPreparedInvocation, StudioAgentToolResult, StudioEndpointContext } from "../../sdk";
import type {
  AgentClientAcknowledgement,
  AgentActionProposal,
  AgentAutonomyMode,
  AgentBootstrapResponse,
  AgentProviderStatus,
  AgentProviderDiagnostics,
  AgentCreateSessionRequest,
  AgentCreateSessionResponse,
  AgentFeedbackRequest,
  AgentMessageRequest,
  AgentMessageResponse,
  AgentProposalDecisionRequest,
  AgentProposalDecisionResponse
} from "./agentTypes";

export interface AgentClient {
  bootstrap(): Promise<AgentBootstrapResponse>;
  createSession(request: AgentCreateSessionRequest): Promise<AgentCreateSessionResponse>;
  sendMessage(sessionId: string, request: AgentMessageRequest): Promise<AgentMessageResponse>;
  approveProposal(proposalId: string, request: AgentProposalDecisionRequest): Promise<AgentProposalDecisionResponse>;
  denyProposal(proposalId: string, request: AgentProposalDecisionRequest): Promise<AgentProposalDecisionResponse>;
  executeProposal(proposalId: string, request: AgentProposalDecisionRequest): Promise<AgentProposalDecisionResponse>;
  acknowledgeClient(sessionId: string, invocationId: string, request: AgentClientAcknowledgement): Promise<AgentProposalDecisionResponse>;
  submitFeedback(sessionId: string, messageId: string, request: AgentFeedbackRequest): Promise<void>;
  cancelTurn(sessionId: string, turnId: string): Promise<void>;
}

export function createAgentClient(context: StudioEndpointContext): AgentClient {
  const deliveryIds = new WeakMap<AgentMessageRequest, string>();
  return {
    async bootstrap() {
      // A single agent harness is active server-side; the client never chooses among providers.
      return mapBootstrap(unwrap(await context.http.getJson<AgentApiResponse<BackendAgentBootstrapResponse>>("/_elsa/agent/bootstrap")));
    },
    async createSession(request) {
      return mapSession(unwrap(await context.http.postJson<AgentApiResponse<BackendAgentSession>>("/_elsa/agent/sessions", {
        conversationId: request.activeSurface.resourceId ?? request.activeSurface.route,
        mode: request.mode,
        autonomyMode: request.autonomyMode,
        activeSurface: request.activeSurface,
        clientContext: request.clientContext,
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
    async sendMessage(sessionId, request) {
      let requestId = request.requestId ?? deliveryIds.get(request);
      if (!requestId) { requestId = globalThis.crypto?.randomUUID?.() ?? `message-${Date.now()}-${Math.random()}`; deliveryIds.set(request, requestId); }
      const accepted = unwrap(await context.http.postJson<AgentApiResponse<BackendAgentMessageAcceptedResponse>>(`/_elsa/agent/sessions/${encodeURIComponent(sessionId)}/messages`, {
        role: "user",
        content: request.message,
        message: request.message,
        mode: request.mode,
        capabilityId: request.capabilityId,
        requestId,
        source: request.source,
        promptMode: request.promptMode,
        requestedCapabilities: request.capabilityId ? [request.capabilityId] : [],
        contextAttachments: request.contextAttachments.map(mapContextAttachment)
      }));

      return {
        messageId: getAcceptedMessageId(accepted),
        status: mapMessageStatus(readField(readRecord(accepted, "message"), "status")),
        warnings: readRecordArray(accepted, "warnings").map(item => ({ code: readString(item, "code") ?? "agent.context", message: readString(item, "message") ?? "Context admission limitation.", target: readString(item, "target") })),
        streamUrl: `/_elsa/agent/sessions/${encodeURIComponent(sessionId)}/stream`
      };
    },
    async approveProposal(proposalId, request) {
      return mapProposalDecision(unwrap(await context.http.postJson<AgentApiResponse<BackendAgentActionProposal>>(`/_elsa/agent/proposals/${encodeURIComponent(proposalId)}/approve`, {
        actorId: "studio",
        revision: request.revision,
        comment: request.comment
      })));
    },
    async denyProposal(proposalId, request) {
      return mapProposalDecision(unwrap(await context.http.postJson<AgentApiResponse<BackendAgentActionProposal>>(`/_elsa/agent/proposals/${encodeURIComponent(proposalId)}/deny`, {
        actorId: "studio",
        revision: request.revision,
        comment: request.comment,
        reason: request.comment
      })));
    },
    async executeProposal(proposalId, request) {
      const result = unwrap(await context.http.postJson<AgentApiResponse<BackendAgentProposalExecutionResult>>(`/_elsa/agent/proposals/${encodeURIComponent(proposalId)}/execute`, {
        actorId: "studio",
        revision: request.revision,
        comment: request.comment
      }));
      return mapExecution(result, proposalId);
    },
    async acknowledgeClient(sessionId, invocationId, request) {
      const result = unwrap(await context.http.postJson<AgentApiResponse<BackendAgentProposalExecutionResult>>(
        `/_elsa/agent/sessions/${encodeURIComponent(sessionId)}/client-invocations/${encodeURIComponent(invocationId)}/acknowledgements`, request));
      return mapExecution(result, invocationId);
    },
    async submitFeedback(sessionId, messageId, request) {
      unwrap(await context.http.postJson<AgentApiResponse<BackendAgentFeedback>>("/_elsa/agent/feedback", {
        sessionId,
        messageId,
        rating: request.rating === "positive" ? 1 : -1,
        comment: request.comment,
        actorId: "studio"
      }));
    },
    async cancelTurn(sessionId, turnId) {
      unwrap(await context.http.postJson<AgentApiResponse<unknown>>(`/_elsa/agent/sessions/${encodeURIComponent(sessionId)}/turns/${encodeURIComponent(turnId)}/cancel`, {}));
    }
  };
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
  clientInvocation?: StudioAgentPreparedInvocation;
  toolResult?: StudioAgentToolResult;
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
  return {
    enabled: readBoolean(response, "enabled") ?? providerStatus === "available",
    providerStatus,
    modes: readStringArray(response, "modes") as AgentBootstrapResponse["modes"] ?? ["explain", "troubleshoot", "build"],
    capabilities: capabilities.map(mapCapability),
    provider,
    policy: mapPolicy(readRecord(response, "policy"))
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
  return {
    sessionId: readString(session, "sessionId") ?? readString(session, "id") ?? "",
    status: mapSessionStatus(readField(session, "status")),
    contextAttachments: []
  };
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

function mapExecution(result: BackendAgentProposalExecutionResult, fallbackId: string): AgentProposalDecisionResponse {
  return {
    proposalId: readString(result, "proposalId") ?? fallbackId,
    approvalStatus: getExecutionStatus(result),
    executed: readBoolean(result, "executed"),
    clientInvocation: readField(result, "clientInvocation") as StudioAgentPreparedInvocation | undefined,
    toolResult: readField(result, "toolResult") as StudioAgentToolResult | undefined,
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
  if (status === "awaiting-client" || status === "uncertain" || status === "cancelled" || status === "expired") return status;
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

function mapSessionStatus(status: unknown): AgentCreateSessionResponse["status"] {
  const value = normalizeEnum(status);
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
