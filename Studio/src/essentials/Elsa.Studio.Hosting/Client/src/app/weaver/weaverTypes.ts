import type {
  StudioWeaverCapabilityContribution,
  StudioWeaverContextAttachment,
  StudioWeaverContributionDiagnostic,
  StudioWeaverMode,
  StudioWeaverRisk,
  StudioWeaverSurface,
  StudioWeaverClientAcknowledgement,
  StudioWeaverPreparedInvocation,
  StudioWeaverResourceTarget,
  StudioWeaverToolResult,
  StudioWeaverPromptRequest,
  StudioWeaverToolInvocationMode
} from "../../sdk";

export type WeaverProviderStatus = "available" | "unavailable" | "disabled" | "degraded";
export type WeaverAutonomyMode = "manual" | "auto-read-only" | "full-auto";
export type WeaverProviderKind = "provider-sdk-binding" | "weaver-harness-provider" | string;
export type WeaverProviderOperation = "chat" | "streaming" | "tool-approval" | "run-status" | "artifacts" | "skills" | "memory" | "file-upload" | string;
export type WeaverProviderRiskProfile = "read-only" | "review-required" | "sandboxed-execution" | "privileged-execution" | string;
export type WeaverSessionStatus = "active" | "completed" | "cancelled" | "failed" | "expired";
export type WeaverMessageRole = "user" | "assistant" | "system" | "tool" | "progress" | "error";
export type WeaverMessageStatus = "pending" | "streaming" | "completed" | "failed" | "cancelled";
export type WeaverProposalStatus = "draft" | "awaiting-approval" | "approved" | "denied" | "edited" | "expired" | "executed" | "failed" | "cancelled" | "awaiting-client" | "uncertain" | "executing";
export type WeaverStreamEventType = "provider-notice" | "turn-started" | "message-started" | "message-delta" | "context-used" | "context-admitted" | "clarification-requested" | "proposal-created" | "review-requested" | "progress" | "step-started" | "step-completed" | "tool-call-requested" | "tool-call-started" | "tool-call-completed" | "plan-updated" | "message-completed" | "turn-cancelled" | "error";

export interface WeaverPlanStep {
  id: string;
  title: string;
  status: string;
}

/**
 * Preview (#3441): what a server restart does to this backend's sessions. `durable`: they are recovered;
 * `processLifetime`: they survive reloads while the server runs, and a restart ends them; `unsupported`: this
 * composition cannot recover them.
 */
export type WeaverRecoveryMode = "durable" | "processLifetime" | "unsupported";

export interface WeaverBootstrapResponse {
  enabled: boolean;
  /** Preview (#3441): absent from a server that does not report it. */
  recovery?: WeaverRecoveryMode;
  providerStatus: WeaverProviderStatus;
  modes: StudioWeaverMode[];
  capabilities: StudioWeaverCapabilityContribution[];
  provider?: WeaverProviderDiagnostics;
  policy: {
    contextVisibility: boolean;
    /** The autonomy mode the deployment defaults a new session to. */
    defaultAutonomyMode: WeaverAutonomyMode;
    /** The autonomy ceiling: a session may request any mode up to (and including) this one. */
    maxAutonomyMode: WeaverAutonomyMode;
    /** The modes the client may offer, derived from the ceiling (most restrictive first). */
    allowedAutonomyModes: WeaverAutonomyMode[];
    retentionLabel?: string;
    actorId?: string;
    permissions?: string[];
    allowedToolIds?: string[];
    deniedToolIds?: string[];
    allowDirectToolInvocations?: boolean;
    allowPrivilegedToolInvocations?: boolean;
  };
}

export interface WeaverProviderDiagnostics {
  providerId: string;
  isAvailable: boolean;
  status: string;
  providerKind: WeaverProviderKind;
  supportedOperations: WeaverProviderOperation[];
  riskProfile: WeaverProviderRiskProfile;
  metadata: Record<string, string>;
}

export interface WeaverCreateSessionRequest {
  mode: StudioWeaverMode;
  /** Preview (#3441): the Studio backend workspace; it only narrows the owner's session list. */
  workspace?: string;
  /** Preview (#3441): the session this one continues after a limit. Only the link is carried over. */
  continuedFrom?: string;
  /** The autonomy mode to request; the server clamps it to the deployment ceiling. Omit to take the default. */
  autonomyMode?: WeaverAutonomyMode;
  activeSurface: StudioWeaverSurface;
  clientContext: {
    studioVersion: string;
    sdkVersion: string;
    moduleIds: string[];
  };
}

export interface WeaverCreateSessionResponse {
  sessionId: string;
  status: WeaverSessionStatus;
  title?: string;
  contextAttachments: StudioWeaverContextAttachment[];
  /**
   * Preview (#3440): the server-acknowledged session policy. Absent only when the backend
   * composition cannot read it back; such a session cannot change its autonomy.
   */
  policy?: WeaverSessionPolicy;
}

/** Preview (#3440): a session's acknowledged effective autonomy, its ceiling and the owner's revision. */
export interface WeaverSessionPolicy {
  autonomyMode: WeaverAutonomyMode;
  maxAutonomyMode: WeaverAutonomyMode;
  allowedAutonomyModes: WeaverAutonomyMode[];
  revision: number;
}

/** Preview (#3440): a conditional autonomy-only change against the last acknowledged revision. */
export interface WeaverAutonomyUpdateRequest {
  autonomyMode: WeaverAutonomyMode;
  expectedRevision: number;
}

/** The policy that admitted a message; a later selector value never relabels it. */
export interface WeaverAcceptedPolicy {
  autonomyMode?: WeaverAutonomyMode;
  revision: number;
}

export interface WeaverSessionDetailResponse {
  sessionId: string;
  status: WeaverSessionStatus;
  title?: string;
  policy?: WeaverSessionPolicy;
  messages: WeaverRecordedMessage[];
  /** Preview (#3441): the documented session limit this session reached; a reopened session offers its continuation. */
  limit?: WeaverSessionLimit;
}

/** Preview (#3441): which documented session limit a disclosure-budget refusal reached. */
export type WeaverSessionLimit = "snapshots" | "bytes";

/** Preview (#3441): the code of the documented session limit outcome. */
export const SESSION_LIMIT_REACHED = "weaver.context.disclosure_budget_exceeded";

/** Preview (#3441): why a cancelled message stopped. A dropped stream records no reason. */
export type WeaverMessageStatusReason = "interrupted" | "withdrawn" | "user-stop";

/** A message as the session owner recorded it. */
export interface WeaverRecordedMessage {
  id: string;
  role: string;
  status: string;
  content?: string;
  statusReason?: WeaverMessageStatusReason;
  acceptedPolicyRevision?: number;
  /** Preview (#3441): the public policy the owner recorded this message as accepted under, when the server sends it. */
  acceptedPolicy?: WeaverAcceptedPolicy;
}

/** Preview (#3441): an owner-scoped session list query; actor and tenant come from the authenticated caller. */
export interface WeaverSessionListQuery {
  workspace?: string;
  take?: number;
  before?: string;
}

/** Preview (#3441): one of the caller's own sessions. */
export interface WeaverSessionSummary {
  id: string;
  title?: string;
  status: WeaverSessionStatus;
  updatedAt: string;
  expiresAt?: string;
  pendingReviewCount: number;
  recoveryState?: string;
}

export interface WeaverSessionListResponse {
  items: WeaverSessionSummary[];
  /** The cursor of the next older page, absent on the last page. */
  next?: string;
}

/** Preview (#3441): the owner's statement about an uncertain client operation. It is never a receipt. */
export type WeaverOwnerResolutionClaim = "applied" | "not-applied" | "unknown";

export interface WeaverOwnerResolution {
  claim: WeaverOwnerResolutionClaim;
  actorId?: string;
  resolvedAt?: string;
}

export interface WeaverMessageRequest {
  requestId?: string;
  source?: StudioWeaverPromptRequest["source"];
  promptMode?: StudioWeaverPromptRequest["mode"];
  message: string;
  mode: StudioWeaverMode;
  activeSurface?: StudioWeaverSurface;
  contextAttachments: StudioWeaverContextAttachment[];
  capabilityId?: string;
}

export interface WeaverMessageResponse {
  messageId: string;
  status: WeaverMessageStatus;
  streamUrl: string;
  /** Sanitized evidence admitted for this message; absent responses fail closed in presentation. */
  contextAttachments?: StudioWeaverContextAttachment[];
  warnings?: { code: string; message: string; target?: string }[];
  /** The policy the owning store stamped on the accepted message, when it owns policy revisions. */
  acceptedPolicy?: WeaverAcceptedPolicy;
}

export interface WeaverMessageViewModel {
  id: string;
  role: WeaverMessageRole;
  content: string;
  status: WeaverMessageStatus;
  /** Preview (#3441): why a recorded cancelled message stopped. */
  statusReason?: WeaverMessageStatusReason;
  /** Preview (#3441): the code of a provider or session notice this entry shows. */
  noticeCode?: string;
  /** The acknowledged policy that admitted the turn this message belongs to, when known. */
  acceptedPolicy?: WeaverAcceptedPolicy;
}

export interface WeaverActionProposal {
  id: string;
  title: string;
  summary: string;
  risk: Exclude<StudioWeaverRisk, "read-only">;
  status: WeaverProposalStatus;
  revision?: string;
  reviewReady?: boolean;
  toolId?: string;
  moduleId?: string;
  invocationMode?: StudioWeaverToolInvocationMode;
  requiredPermissions?: string[];
  policy?: WeaverToolInvocationPolicy;
  resourceTarget?: WeaverResourceTarget;
  disabledReason?: string;
  isLoading?: boolean;
  error?: string;
  confirmationPending?: boolean;
  audit?: WeaverProposalAuditState;
  preparedInvocation?: StudioWeaverPreparedInvocation;
  toolResult?: StudioWeaverToolResult;
  resultRendererId?: string;
  resultType?: string;
  result?: unknown;
  operations?: WeaverProposalOperation[];
  risks?: string[];
  rollback?: string;
  /** Preview (#3441): the owner's recorded statement about this uncertain client operation. */
  ownerResolution?: WeaverOwnerResolution;
  /** Preview (#3441): when the prepared operation's lifetime ends. */
  expiresAt?: string;
}

export type WeaverResourceTarget = StudioWeaverResourceTarget;

export interface WeaverProposalAuditState {
  state: string;
  outcome?: string;
  actor?: string;
  sessionId?: string;
  toolId?: string;
  target?: WeaverResourceTarget;
  risk?: StudioWeaverRisk;
  invocationMode?: StudioWeaverToolInvocationMode;
  policyResult?: WeaverToolPolicyResult;
  recordedAt?: string;
}

export type WeaverToolPolicyResult = "allowed" | "denied" | "requires-proposal" | "proposal-required";
export type WeaverToolInvocationOutcome = "allowed" | "denied" | "failed" | "proposal-created" | "executed";

export interface WeaverToolInvocationPolicy {
  allowedToolIds?: string[];
  deniedToolIds?: string[];
  allowedInvocationModes?: StudioWeaverToolInvocationMode[];
  deniedInvocationModes?: StudioWeaverToolInvocationMode[];
}
export interface WeaverProposalOperation {
  op: string;
  [key: string]: unknown;
}

export type WeaverActionProposalPayload = Partial<WeaverActionProposal> & {
  baseRevision?: string;
};

/** Preview (#3441): a session's recorded proposal; its status is the owner's, normalized. */
export type WeaverRecordedProposal = WeaverActionProposalPayload & { id: string; status: WeaverProposalStatus };

export interface WeaverClarificationRequest {
  id: string;
  prompt: string;
  choices?: string[];
  metadata?: Record<string, unknown>;
}

export type WeaverStreamEvent =
  | { type: "review-requested"; messageId: string }
  | { type: "module-result"; messageId: string; result: StudioWeaverToolResult }
  | { type: "turn-started"; turnId: string; maxSteps: number }
  | { type: "message-started"; messageId: string; role: WeaverMessageRole }
  | { type: "message-delta"; messageId: string; content: string }
  | { type: "context-used"; messageId: string; attachmentIds: string[] }
  | { type: "context-admitted"; attachments: StudioWeaverContextAttachment[]; diagnostics: StudioWeaverContributionDiagnostic[] }
  | { type: "clarification-requested"; messageId: string; clarification: WeaverClarificationRequest }
  | { type: "proposal-created"; proposalId: string; messageId: string; proposal?: WeaverActionProposalPayload }
  | { type: "progress"; label: string; percent?: number }
  | { type: "step-started"; stepIndex: number; maxSteps: number }
  | { type: "step-completed"; stepIndex: number; maxSteps: number }
  | { type: "tool-call-requested"; toolCallId: string; toolName: string; arguments: string; requiresApproval: boolean }
  | { type: "tool-call-started"; toolCallId: string; toolName: string }
  | { type: "tool-call-completed"; toolCallId: string; toolName: string; succeeded: boolean; summary: string }
  | { type: "plan-updated"; steps: WeaverPlanStep[] }
  | { type: "message-completed"; messageId: string }
  | { type: "turn-cancelled"; turnId: string }
  | { type: "provider-notice"; code: string; message: string }
  | { type: "error"; message: string; code?: string; limit?: WeaverSessionLimit };

export interface WeaverFeedbackRequest {
  rating: "positive" | "negative";
  comment?: string;
}

export interface WeaverProposalDecisionRequest {
  revision?: string;
  comment?: string;
}

export interface WeaverProposalDecisionResponse {
  proposalId: string;
  approvalStatus: WeaverProposalStatus;
  approvedAt?: string;
  executed?: boolean;
  clientInvocation?: StudioWeaverPreparedInvocation;
  toolResult?: StudioWeaverToolResult;
  result?: { resourceType: string; resourceId: string; summary: string };
}

export type WeaverClientAcknowledgement = StudioWeaverClientAcknowledgement;
