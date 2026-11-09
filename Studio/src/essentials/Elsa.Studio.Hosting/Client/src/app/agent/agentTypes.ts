import type {
  StudioAgentCapabilityContribution,
  StudioAgentContextAttachment,
  StudioAgentContributionDiagnostic,
  StudioAgentMode,
  StudioAgentRisk,
  StudioAgentSurface,
  StudioAgentClientAcknowledgement,
  StudioAgentPreparedInvocation,
  StudioAgentResourceTarget,
  StudioAgentToolResult,
  StudioAgentPromptRequest,
  StudioAgentToolInvocationMode
} from "../../sdk";

export type AgentProviderStatus = "available" | "unavailable" | "disabled" | "degraded";
export type AgentAutonomyMode = "manual" | "auto-read-only" | "full-auto";
export type AgentProviderKind = "provider-sdk-binding" | "agent-harness-provider" | string;
export type AgentProviderOperation = "chat" | "streaming" | "tool-approval" | "run-status" | "artifacts" | "skills" | "memory" | "file-upload" | string;
export type AgentProviderRiskProfile = "read-only" | "review-required" | "sandboxed-execution" | "privileged-execution" | string;
export type AgentSessionStatus = "active" | "completed" | "cancelled" | "failed" | "expired";
export type AgentMessageRole = "user" | "assistant" | "system" | "tool" | "progress" | "error";
export type AgentMessageStatus = "pending" | "streaming" | "completed" | "failed" | "cancelled";
export type AgentProposalStatus = "draft" | "awaiting-approval" | "approved" | "denied" | "edited" | "expired" | "executed" | "failed" | "cancelled" | "awaiting-client" | "uncertain" | "executing";
export type AgentStreamEventType = "provider-notice" | "turn-started" | "message-started" | "message-delta" | "context-used" | "context-admitted" | "clarification-requested" | "proposal-created" | "review-requested" | "progress" | "step-started" | "step-completed" | "tool-call-requested" | "tool-call-started" | "tool-call-completed" | "plan-updated" | "message-completed" | "turn-cancelled" | "error";

export interface AgentPlanStep {
  id: string;
  title: string;
  status: string;
}

/**
 * Preview (#3441): what a server restart does to this backend's sessions. `durable`: they are recovered;
 * `processLifetime`: they survive reloads while the server runs, and a restart ends them; `unsupported`: this
 * composition cannot recover them.
 */
export type AgentRecoveryMode = "durable" | "processLifetime" | "unsupported";

export interface AgentBootstrapResponse {
  enabled: boolean;
  /** Preview (#3441): absent from a server that does not report it. */
  recovery?: AgentRecoveryMode;
  providerStatus: AgentProviderStatus;
  modes: StudioAgentMode[];
  capabilities: StudioAgentCapabilityContribution[];
  provider?: AgentProviderDiagnostics;
  policy: {
    contextVisibility: boolean;
    /** The autonomy mode the deployment defaults a new session to. */
    defaultAutonomyMode: AgentAutonomyMode;
    /** The autonomy ceiling: a session may request any mode up to (and including) this one. */
    maxAutonomyMode: AgentAutonomyMode;
    /** The modes the client may offer, derived from the ceiling (most restrictive first). */
    allowedAutonomyModes: AgentAutonomyMode[];
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
  /** Preview (#3441): the Studio backend workspace; it only narrows the owner's session list. */
  workspace?: string;
  /** Preview (#3441): the session this one continues after a limit. Only the link is carried over. */
  continuedFrom?: string;
  /** The autonomy mode to request; the server clamps it to the deployment ceiling. Omit to take the default. */
  autonomyMode?: AgentAutonomyMode;
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
  /**
   * Preview (#3440): the server-acknowledged session policy. Absent only when the backend
   * composition cannot read it back; such a session cannot change its autonomy.
   */
  policy?: AgentSessionPolicy;
}

/** Preview (#3440): a session's acknowledged effective autonomy, its ceiling and the owner's revision. */
export interface AgentSessionPolicy {
  autonomyMode: AgentAutonomyMode;
  maxAutonomyMode: AgentAutonomyMode;
  allowedAutonomyModes: AgentAutonomyMode[];
  revision: number;
}

/** Preview (#3440): a conditional autonomy-only change against the last acknowledged revision. */
export interface AgentAutonomyUpdateRequest {
  autonomyMode: AgentAutonomyMode;
  expectedRevision: number;
}

/** The policy that admitted a message; a later selector value never relabels it. */
export interface AgentAcceptedPolicy {
  autonomyMode?: AgentAutonomyMode;
  revision: number;
}

export interface AgentSessionDetailResponse {
  sessionId: string;
  status: AgentSessionStatus;
  title?: string;
  policy?: AgentSessionPolicy;
  messages: AgentRecordedMessage[];
  /** Preview (#3441): the documented session limit this session reached; a reopened session offers its continuation. */
  limit?: AgentSessionLimit;
}

/** Preview (#3441): which documented session limit a disclosure-budget refusal reached. */
export type AgentSessionLimit = "snapshots" | "bytes";

/** Preview (#3441): the code of the documented session limit outcome. */
export const SESSION_LIMIT_REACHED = "weaver.context.disclosure_budget_exceeded";

/** Preview (#3441): why a cancelled message stopped. A dropped stream records no reason. */
export type AgentMessageStatusReason = "interrupted" | "withdrawn" | "user-stop";

/** A message as the session owner recorded it. */
export interface AgentRecordedMessage {
  id: string;
  role: string;
  status: string;
  content?: string;
  statusReason?: AgentMessageStatusReason;
  acceptedPolicyRevision?: number;
  /** Preview (#3441): the public policy the owner recorded this message as accepted under, when the server sends it. */
  acceptedPolicy?: AgentAcceptedPolicy;
}

/** Preview (#3441): an owner-scoped session list query; actor and tenant come from the authenticated caller. */
export interface AgentSessionListQuery {
  workspace?: string;
  take?: number;
  before?: string;
}

/** Preview (#3441): one of the caller's own sessions. */
export interface AgentSessionSummary {
  id: string;
  title?: string;
  status: AgentSessionStatus;
  updatedAt: string;
  expiresAt?: string;
  pendingReviewCount: number;
  recoveryState?: string;
}

export interface AgentSessionListResponse {
  items: AgentSessionSummary[];
  /** The cursor of the next older page, absent on the last page. */
  next?: string;
}

/** Preview (#3441): the owner's statement about an uncertain client operation. It is never a receipt. */
export type AgentOwnerResolutionClaim = "applied" | "not-applied" | "unknown";

export interface AgentOwnerResolution {
  claim: AgentOwnerResolutionClaim;
  actorId?: string;
  resolvedAt?: string;
}

export interface AgentMessageRequest {
  requestId?: string;
  source?: StudioAgentPromptRequest["source"];
  promptMode?: StudioAgentPromptRequest["mode"];
  message: string;
  mode: StudioAgentMode;
  activeSurface?: StudioAgentSurface;
  contextAttachments: StudioAgentContextAttachment[];
  capabilityId?: string;
}

export interface AgentMessageResponse {
  messageId: string;
  status: AgentMessageStatus;
  streamUrl: string;
  /** Sanitized evidence admitted for this message; absent responses fail closed in presentation. */
  contextAttachments?: StudioAgentContextAttachment[];
  warnings?: { code: string; message: string; target?: string }[];
  /** The policy the owning store stamped on the accepted message, when it owns policy revisions. */
  acceptedPolicy?: AgentAcceptedPolicy;
}

export interface AgentMessageViewModel {
  id: string;
  role: AgentMessageRole;
  content: string;
  status: AgentMessageStatus;
  /** Preview (#3441): why a recorded cancelled message stopped. */
  statusReason?: AgentMessageStatusReason;
  /** Preview (#3441): the code of a provider or session notice this entry shows. */
  noticeCode?: string;
  /** The acknowledged policy that admitted the turn this message belongs to, when known. */
  acceptedPolicy?: AgentAcceptedPolicy;
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
  confirmationPending?: boolean;
  audit?: AgentProposalAuditState;
  preparedInvocation?: StudioAgentPreparedInvocation;
  toolResult?: StudioAgentToolResult;
  resultRendererId?: string;
  resultType?: string;
  result?: unknown;
  operations?: AgentProposalOperation[];
  risks?: string[];
  rollback?: string;
  /** Preview (#3441): the owner's recorded statement about this uncertain client operation. */
  ownerResolution?: AgentOwnerResolution;
  /** Preview (#3441): when the prepared operation's lifetime ends. */
  expiresAt?: string;
}

export type AgentResourceTarget = StudioAgentResourceTarget;

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

export type AgentToolPolicyResult = "allowed" | "denied" | "requires-proposal" | "proposal-required";
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

/** Preview (#3441): a session's recorded proposal; its status is the owner's, normalized. */
export type AgentRecordedProposal = AgentActionProposalPayload & { id: string; status: AgentProposalStatus };

export interface AgentClarificationRequest {
  id: string;
  prompt: string;
  choices?: string[];
  metadata?: Record<string, unknown>;
}

export type AgentStreamEvent =
  | { type: "review-requested"; messageId: string }
  | { type: "module-result"; messageId: string; result: StudioAgentToolResult }
  | { type: "turn-started"; turnId: string; maxSteps: number }
  | { type: "message-started"; messageId: string; role: AgentMessageRole }
  | { type: "message-delta"; messageId: string; content: string }
  | { type: "context-used"; messageId: string; attachmentIds: string[] }
  | { type: "context-admitted"; attachments: StudioAgentContextAttachment[]; diagnostics: StudioAgentContributionDiagnostic[] }
  | { type: "clarification-requested"; messageId: string; clarification: AgentClarificationRequest }
  | { type: "proposal-created"; proposalId: string; messageId: string; proposal?: AgentActionProposalPayload }
  | { type: "progress"; label: string; percent?: number }
  | { type: "step-started"; stepIndex: number; maxSteps: number }
  | { type: "step-completed"; stepIndex: number; maxSteps: number }
  | { type: "tool-call-requested"; toolCallId: string; toolName: string; arguments: string; requiresApproval: boolean }
  | { type: "tool-call-started"; toolCallId: string; toolName: string }
  | { type: "tool-call-completed"; toolCallId: string; toolName: string; succeeded: boolean; summary: string }
  | { type: "plan-updated"; steps: AgentPlanStep[] }
  | { type: "message-completed"; messageId: string }
  | { type: "turn-cancelled"; turnId: string }
  | { type: "provider-notice"; code: string; message: string }
  | { type: "error"; message: string; code?: string; limit?: AgentSessionLimit };

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
  executed?: boolean;
  clientInvocation?: StudioAgentPreparedInvocation;
  toolResult?: StudioAgentToolResult;
  result?: { resourceType: string; resourceId: string; summary: string };
}

export type AgentClientAcknowledgement = StudioAgentClientAcknowledgement;
