import type {
  StudioAgentCapabilityContribution,
  StudioAgentContextAttachment,
  StudioAgentMode,
  StudioAgentRisk,
  StudioAgentSurface,
  StudioAgentToolInvocationMode
} from "../../sdk";

export type AgentProviderStatus = "available" | "unavailable" | "disabled" | "degraded";
export type AgentProviderKind = "provider-sdk-binding" | "agent-harness-provider" | string;
export type AgentProviderOperation = "chat" | "streaming" | "tool-approval" | "run-status" | "artifacts" | "skills" | "memory" | "file-upload" | string;
export type AgentProviderRiskProfile = "read-only" | "review-required" | "sandboxed-execution" | "privileged-execution" | string;
export type AgentSessionStatus = "active" | "completed" | "cancelled" | "failed" | "expired";
export type AgentMessageRole = "user" | "assistant" | "system" | "tool" | "progress" | "error";
export type AgentMessageStatus = "pending" | "streaming" | "completed" | "failed" | "cancelled";
export type AgentProposalStatus = "draft" | "awaiting-approval" | "approved" | "denied" | "edited" | "expired" | "executed" | "failed" | "cancelled";
export type AgentStreamEventType = "message-started" | "message-delta" | "context-used" | "clarification-requested" | "workflow-batch-created" | "proposal-created" | "progress" | "message-completed" | "error";

export interface AgentBootstrapResponse {
  enabled: boolean;
  providerStatus: AgentProviderStatus;
  modes: StudioAgentMode[];
  capabilities: StudioAgentCapabilityContribution[];
  providers: AgentProviderDiagnostics[];
  policy: {
    contextVisibility: boolean;
    requiresApprovalForMutations: boolean;
    retentionLabel?: string;
    actorId?: string;
    permissions?: string[];
    allowedToolIds?: string[];
    deniedToolIds?: string[];
    allowDirectToolInvocations?: boolean;
    allowPrivilegedToolInvocations?: boolean;
  };
}

export interface AgentProviderDiagnostics {
  providerId: string;
  isAvailable: boolean;
  status: string;
  providerKind: AgentProviderKind;
  supportedOperations: AgentProviderOperation[];
  riskProfile: AgentProviderRiskProfile;
  metadata: Record<string, string>;
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
  toolId?: string;
  moduleId?: string;
  invocationMode?: StudioAgentToolInvocationMode;
  requiredPermissions?: string[];
  policy?: AgentToolInvocationPolicy;
  resourceTarget?: AgentResourceTarget;
  disabledReason?: string;
  isLoading?: boolean;
  error?: string;
  audit?: AgentProposalAuditState;
  resultRendererId?: string;
  resultType?: string;
  result?: unknown;
  operations?: AgentProposalOperation[];
  risks?: string[];
  rollback?: string;
}

export interface AgentResourceTarget {
  resourceType: string;
  resourceId?: string;
  displayName?: string;
  moduleId?: string;
  route?: string;
  summary?: string;
}

export interface AgentProposalAuditState {
  state: string;
  outcome?: string;
  actor?: string;
  sessionId?: string;
  toolId?: string;
  target?: AgentResourceTarget;
  risk?: StudioAgentRisk;
  invocationMode?: StudioAgentToolInvocationMode;
  policyResult?: AgentToolPolicyResult;
  recordedAt?: string;
}

export type AgentToolPolicyResult = "allowed" | "denied" | "proposal-required";
export type AgentToolInvocationOutcome = "allowed" | "denied" | "failed" | "proposal-created" | "executed";

export interface AgentToolInvocationPolicy {
  allowedToolIds?: string[];
  deniedToolIds?: string[];
  allowedInvocationModes?: StudioAgentToolInvocationMode[];
  deniedInvocationModes?: StudioAgentToolInvocationMode[];
}

export interface AgentProposalOperation {
  op: string;
  [key: string]: unknown;
}

export type AgentActionProposalPayload = Partial<AgentActionProposal> & {
  baseRevision?: string;
};

export interface AgentClarificationRequest {
  id: string;
  prompt: string;
  choices?: string[];
  metadata?: Record<string, unknown>;
}

export interface WorkflowGraphOperationBatch {
  schemaVersion: string;
  workflowDefinitionId: string;
  baseRevision?: string | null;
  operations: WorkflowGraphOperation[];
  metadata?: Record<string, unknown>;
}

export interface WorkflowGraphOperation {
  id: string;
  kind: string;
  parameters: Record<string, unknown>;
  temporaryReferences: string[];
  summary?: string | null;
}

export interface WorkflowGraphOperationBatchApplyResult {
  appliedCount: number;
  finalActivityIds: string[];
  temporaryReferences: Record<string, string>;
  summary: string;
  undoToken?: string;
}

export type AgentStreamEvent =
  | { type: "message-started"; messageId: string; role: AgentMessageRole }
  | { type: "message-delta"; messageId: string; content: string }
  | { type: "context-used"; messageId: string; attachmentIds: string[] }
  | { type: "clarification-requested"; messageId: string; clarification: AgentClarificationRequest }
  | { type: "workflow-batch-created"; messageId: string; batch: WorkflowGraphOperationBatch }
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
