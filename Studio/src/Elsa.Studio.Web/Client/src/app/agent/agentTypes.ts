import type {
  StudioAgentCapabilityContribution,
  StudioAgentContextAttachment,
  StudioAgentMode,
  StudioAgentRisk,
  StudioAgentSurface
} from "../../sdk";

export type AgentProviderStatus = "available" | "unavailable" | "disabled" | "degraded";
export type AgentSessionStatus = "active" | "completed" | "cancelled" | "failed" | "expired";
export type AgentMessageRole = "user" | "assistant" | "system" | "tool" | "progress" | "error";
export type AgentMessageStatus = "pending" | "streaming" | "completed" | "failed" | "cancelled";
export type AgentProposalStatus = "draft" | "awaiting-approval" | "approved" | "denied" | "edited" | "expired" | "executed" | "failed" | "cancelled";
export type AgentStreamEventType = "message-started" | "message-delta" | "context-used" | "proposal-created" | "progress" | "message-completed" | "error";

export interface AgentBootstrapResponse {
  enabled: boolean;
  providerStatus: AgentProviderStatus;
  modes: StudioAgentMode[];
  capabilities: StudioAgentCapabilityContribution[];
  policy: {
    contextVisibility: boolean;
    requiresApprovalForMutations: boolean;
    retentionLabel?: string;
  };
}

export interface AgentCreateSessionRequest {
  mode: StudioAgentMode;
  activeSurface: StudioAgentSurface;
  clientContext: {
    studioVersion: string;
    sdkVersion: string;
    moduleIds: string[];
  };
}

export interface AgentCreateSessionResponse {
  sessionId: string;
  status: AgentSessionStatus;
  title?: string;
  contextAttachments: StudioAgentContextAttachment[];
}

export interface AgentMessageRequest {
  message: string;
  mode: StudioAgentMode;
  contextAttachments: StudioAgentContextAttachment[];
  capabilityId?: string;
}

export interface AgentMessageResponse {
  messageId: string;
  status: AgentMessageStatus;
  streamUrl: string;
}

export interface AgentMessageViewModel {
  id: string;
  role: AgentMessageRole;
  content: string;
  status: AgentMessageStatus;
}

export interface AgentActionProposal {
  id: string;
  title: string;
  summary: string;
  risk: Exclude<StudioAgentRisk, "read-only">;
  status: AgentProposalStatus;
  revision?: string;
  reviewReady?: boolean;
  operations?: AgentProposalOperation[];
  risks?: string[];
  rollback?: string;
}

export interface AgentProposalOperation {
  op: string;
  [key: string]: unknown;
}

export type AgentActionProposalPayload = Partial<AgentActionProposal> & {
  baseRevision?: string;
};

export type AgentStreamEvent =
  | { type: "message-started"; messageId: string; role: AgentMessageRole }
  | { type: "message-delta"; messageId: string; content: string }
  | { type: "context-used"; messageId: string; attachmentIds: string[] }
  | { type: "proposal-created"; proposalId: string; messageId: string; proposal?: AgentActionProposalPayload }
  | { type: "progress"; label: string; percent?: number }
  | { type: "message-completed"; messageId: string }
  | { type: "error"; message: string };

export interface AgentFeedbackRequest {
  rating: "positive" | "negative";
  comment?: string;
}

export interface AgentProposalDecisionRequest {
  revision?: string;
  comment?: string;
}

export interface AgentProposalDecisionResponse {
  proposalId: string;
  approvalStatus: AgentProposalStatus;
  approvedAt?: string;
  result?: {
    resourceType: string;
    resourceId: string;
    summary: string;
  };
}
