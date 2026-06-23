import type { StudioEndpointContext } from "../../sdk";
import type {
  AgentActionProposal,
  AgentBootstrapResponse,
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
  submitFeedback(sessionId: string, messageId: string, request: AgentFeedbackRequest): Promise<void>;
}

export function createAgentClient(context: StudioEndpointContext): AgentClient {
  let providerId: string | undefined;

  return {
    async bootstrap() {
      const response = unwrap(await context.http.getJson<AgentApiResponse<BackendAgentBootstrapResponse>>("/_elsa/agent/bootstrap"));
      providerId = response.providers?.find(provider => provider.isAvailable)?.providerId;
      return mapBootstrap(response);
    },
    async createSession(request) {
      return mapSession(unwrap(await context.http.postJson<AgentApiResponse<BackendAgentSession>>("/_elsa/agent/sessions", {
        conversationId: request.activeSurface.resourceId ?? request.activeSurface.route,
        ...(providerId ? { providerId } : {}),
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
      const accepted = unwrap(await context.http.postJson<AgentApiResponse<BackendAgentMessageAcceptedResponse>>(`/_elsa/agent/sessions/${encodeURIComponent(sessionId)}/messages`, {
        role: "user",
        content: request.message,
        capabilityId: request.capabilityId,
        contextAttachments: request.contextAttachments.map(mapContextAttachment)
      }));

      return {
        messageId: accepted.message.id,
        status: "pending",
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
      return {
        proposalId: result.proposalId,
        approvalStatus: result.executed ? "executed" : "failed",
        result: {
          resourceType: "agent-proposal",
          resourceId: result.proposalId,
          summary: result.message
        }
      };
    },
    async submitFeedback(sessionId, messageId, request) {
      unwrap(await context.http.postJson<AgentApiResponse<BackendAgentFeedback>>("/_elsa/agent/feedback", {
        sessionId,
        messageId,
        rating: request.rating === "positive" ? 1 : -1,
        comment: request.comment,
        actorId: "studio"
      }));
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
  providers?: BackendAgentProviderDiagnostics[];
}

interface BackendAgentCapability {
  id: string;
  displayName: string;
  description: string;
  requiresApproval?: boolean;
}

interface BackendAgentProviderDiagnostics {
  providerId: string;
  isAvailable: boolean;
  status: string;
}

interface BackendAgentSession {
  id: string;
  status: string | number;
}

interface BackendAgentMessageAcceptedResponse {
  message: BackendAgentMessage;
}

interface BackendAgentMessage {
  id: string;
}

interface BackendAgentActionProposal {
  id: string;
  status: string | number;
  approvedAt?: string | null;
}

interface BackendAgentProposalExecutionResult {
  proposalId: string;
  executed: boolean;
  message: string;
}

interface BackendAgentFeedback {
  id: string;
}

function unwrap<T>(response: AgentApiResponse<T>): T {
  if (response.error) {
    throw new Error(response.error.message || response.error.code);
  }

  if (response.data === undefined || response.data === null) {
    throw new Error("Agent backend returned an empty response.");
  }

  return response.data;
}

function mapBootstrap(response: BackendAgentBootstrapResponse): AgentBootstrapResponse {
  const providerStatus = response.providerStatus ?? (response.providers?.some(provider => provider.isAvailable) ? "available" : "unavailable");
  return {
    enabled: response.enabled ?? providerStatus === "available",
    providerStatus,
    modes: response.modes ?? ["explain", "troubleshoot", "build"],
    capabilities: (response.capabilities ?? []).map(capability => ({
      id: capability.id,
      displayName: capability.displayName,
      description: capability.description,
      kind: capability.requiresApproval ? "proposal" : "answer",
      risk: capability.requiresApproval ? "review-required" : "read-only",
      surfaces: ["*"]
    })),
    policy: response.policy ?? { contextVisibility: true, requiresApprovalForMutations: true }
  };
}

function mapSession(session: BackendAgentSession): AgentCreateSessionResponse {
  return {
    sessionId: session.id,
    status: mapSessionStatus(session.status),
    contextAttachments: []
  };
}

function mapContextAttachment(attachment: AgentMessageRequest["contextAttachments"][number]) {
  const secretRedacted = attachment.sensitivity === "secret-redacted";
  return {
    id: attachment.id,
    kind: attachment.contentType,
    displayName: attachment.label,
    sensitivity: mapSensitivity(attachment.sensitivity),
    summary: secretRedacted ? "[secret redacted]" : typeof attachment.content === "string" ? attachment.content : attachment.label,
    references: {
      source: attachment.source,
      sourceId: attachment.sourceId ?? "",
      scope: attachment.scope
    }
  };
}

function mapProposalDecision(proposal: BackendAgentActionProposal): AgentProposalDecisionResponse {
  return {
    proposalId: proposal.id,
    approvalStatus: mapProposalStatus(proposal.status),
    approvedAt: proposal.approvedAt ?? undefined
  };
}

function mapSessionStatus(status: string | number): AgentCreateSessionResponse["status"] {
  const value = normalizeEnum(status);
  if (value === "failed" || status === 2) return "failed";
  if (value === "archived" || status === 1) return "completed";
  return "active";
}

function mapProposalStatus(status: string | number): AgentActionProposal["status"] {
  const value = normalizeEnum(status);
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

function normalizeEnum(value: string | number) {
  return String(value).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
