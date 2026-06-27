import type {
  StudioAgentResourceTarget,
  StudioAgentRisk,
  StudioAgentToolContractContribution,
  StudioAgentToolInvocationMode
} from "../../sdk";
import type {
  AgentToolInvocationOutcome,
  AgentProposalAuditState,
  AgentResourceTarget,
  AgentToolInvocationPolicy,
  AgentToolPolicyResult
} from "./agentTypes";

export interface AgentToolPolicy {
  readonly allowedToolIds?: readonly string[];
  readonly deniedToolIds?: readonly string[];
  readonly allowedInvocationModes?: readonly StudioAgentToolInvocationMode[];
  readonly deniedInvocationModes?: readonly StudioAgentToolInvocationMode[];
  readonly directAllowedRisks?: readonly StudioAgentRisk[];
  readonly allowPrivilegedTools?: boolean;
}

export interface AgentToolInvocationEvaluationRequest {
  readonly actorId: string;
  readonly sessionId: string;
  readonly tool: StudioAgentToolContractContribution;
  readonly resourceTarget: StudioAgentResourceTarget;
  readonly invocationMode: StudioAgentToolInvocationMode;
  readonly permissions?: readonly string[];
  readonly hostPolicy?: AgentToolPolicy;
  readonly modulePolicy?: AgentToolPolicy;
  readonly failure?: string;
}

export interface AgentToolInvocationAuditRecord {
  readonly actorId: string;
  readonly sessionId: string;
  readonly toolId: string;
  readonly resourceTarget: StudioAgentResourceTarget;
  readonly risk: StudioAgentRisk;
  readonly invocationMode: StudioAgentToolInvocationMode;
  readonly policyResult: AgentToolPolicyResult;
  readonly outcome: AgentToolInvocationOutcome;
  readonly reason?: string;
  readonly recordedAt: string;
}

export interface AgentToolInvocationEvaluation {
  readonly allowed: boolean;
  readonly policyResult: AgentToolPolicyResult;
  readonly outcome: AgentToolInvocationOutcome;
  readonly disabledReason?: string;
  readonly audit: AgentToolInvocationAuditRecord;
}

export interface AgentToolInvocationRequest {
  toolId: string;
  moduleId?: string;
  sessionId?: string;
  invocationMode: StudioAgentToolInvocationMode;
  risk: StudioAgentRisk;
  resourceTarget?: AgentResourceTarget;
  requiredPermissions?: readonly string[];
}

export interface AgentToolInvocationPolicyContext {
  actor: string;
  sessionId?: string | null;
  permissions?: readonly string[];
  hostPolicy: AgentToolInvocationPolicy & {
    requiresApprovalForMutations?: boolean;
    allowDirectToolInvocations?: boolean;
    allowPrivilegedToolInvocations?: boolean;
  };
  modulePolicy?: AgentToolInvocationPolicy;
  now?: () => Date;
}

export interface AgentToolPolicyDecision {
  allowed: boolean;
  policyResult: AgentToolPolicyResult;
  outcome: AgentToolInvocationOutcome;
  reason?: string;
  audit: AgentProposalAuditState;
}

export function evaluateAgentToolInvocation(request: AgentToolInvocationEvaluationRequest): AgentToolInvocationEvaluation {
  const denialReason = getDenialReason(request);
  if (denialReason) {
    return createEvaluation(request, "denied", "denied", denialReason);
  }

  if (request.invocationMode === "direct" && !canInvokeDirectly(request.tool.risk, request.hostPolicy, request.modulePolicy)) {
    return request.tool.invocationModes.includes("proposal")
      ? createEvaluation(request, "requires-proposal", "proposal-created", "Policy requires a proposal before this tool can mutate the target.")
      : createEvaluation(request, "denied", "denied", "Policy does not allow direct invocation for this tool risk.");
  }

  if (request.failure) {
    return createEvaluation(request, request.invocationMode === "proposal" ? "requires-proposal" : "allowed", "failed", request.failure);
  }

  if (request.invocationMode === "proposal") {
    return createEvaluation(request, "requires-proposal", "proposal-created");
  }

  return createEvaluation(request, "allowed", "allowed");
}

export function evaluateToolInvocationPolicy(
  request: AgentToolInvocationRequest,
  context: AgentToolInvocationPolicyContext
): AgentToolPolicyDecision {
  const deniedReason = getLegacyDeniedReason(request, context);
  if (deniedReason) {
    return createDecision(request, context, false, "denied", "denied", deniedReason);
  }

  switch (request.invocationMode) {
    case "read-only":
      return request.risk === "read-only"
        ? createDecision(request, context, true, "allowed", "allowed")
        : createDecision(request, context, false, "denied", "denied", "Read-only invocations cannot apply mutating results.");
    case "direct":
      if (context.hostPolicy.requiresApprovalForMutations || context.hostPolicy.allowDirectToolInvocations === false) {
        return createDecision(request, context, false, "proposal-required", "proposal-created", "Host Policy requires a reviewed proposal before this tool can mutate a resource.");
      }
      return createDecision(request, context, true, "allowed", "allowed");
    case "proposal":
      return createDecision(request, context, true, "allowed", "allowed");
    case "privileged":
      return context.hostPolicy.allowPrivilegedToolInvocations === true
        ? createDecision(request, context, true, "allowed", "allowed")
        : createDecision(request, context, false, "denied", "denied", "Host Policy denied privileged tool invocation.");
  }
}

export function createToolInvocationAudit(
  request: AgentToolInvocationRequest,
  context: AgentToolInvocationPolicyContext,
  policyResult: AgentToolPolicyResult,
  outcome: AgentToolInvocationOutcome,
  reason?: string
): AgentProposalAuditState {
  return {
    state: outcome,
    outcome: reason ?? outcome,
    actor: context.actor,
    sessionId: context.sessionId ?? request.sessionId,
    toolId: request.toolId,
    target: request.resourceTarget,
    risk: request.risk,
    invocationMode: request.invocationMode,
    policyResult,
    recordedAt: (context.now?.() ?? new Date()).toISOString()
  };
}

function getDenialReason(request: AgentToolInvocationEvaluationRequest) {
  const policyDenial = getPolicyDenialReason(request.tool.id, request.invocationMode, request.hostPolicy, "Host Policy")
    ?? getPolicyDenialReason(request.tool.id, request.invocationMode, request.modulePolicy, "Module Policy");
  if (policyDenial) {
    return policyDenial;
  }

  if (!request.tool.invocationModes.includes(request.invocationMode)) {
    return `Tool does not support ${request.invocationMode} invocation.`;
  }

  if (!hasRequiredPermissions(request.tool.requiredPermissions, request.permissions)) {
    return "Actor is missing one or more required tool permissions.";
  }

  if (request.invocationMode === "read-only" && request.tool.risk !== "read-only") {
    return "Read-only invocation cannot run a mutating tool.";
  }

  if (request.invocationMode === "privileged" && (!request.hostPolicy?.allowPrivilegedTools || !request.modulePolicy?.allowPrivilegedTools)) {
    return "Privileged invocation requires Host Policy and Module Policy approval.";
  }

  return null;
}

function getPolicyDenialReason(
  toolId: string,
  invocationMode: StudioAgentToolInvocationMode,
  policy: AgentToolPolicy | undefined,
  policyName: string
) {
  if (!policy) {
    return null;
  }

  if (policy.deniedToolIds?.includes(toolId)) {
    return `${policyName} denies this tool.`;
  }

  if (policy.allowedToolIds && !policy.allowedToolIds.includes(toolId)) {
    return `${policyName} does not allow this tool.`;
  }

  if (policy.deniedInvocationModes?.includes(invocationMode)) {
    return `${policyName} denies ${invocationMode} invocation.`;
  }

  if (policy.allowedInvocationModes && !policy.allowedInvocationModes.includes(invocationMode)) {
    return `${policyName} does not allow ${invocationMode} invocation.`;
  }

  return null;
}

function canInvokeDirectly(
  risk: StudioAgentRisk,
  hostPolicy: AgentToolPolicy | undefined,
  modulePolicy: AgentToolPolicy | undefined
) {
  return getDirectAllowedRisks(hostPolicy).includes(risk) && getDirectAllowedRisks(modulePolicy).includes(risk);
}

function getDirectAllowedRisks(policy: AgentToolPolicy | undefined) {
  return policy?.directAllowedRisks ?? ["read-only"];
}

function hasRequiredPermissions(requiredPermissions: readonly string[] | undefined, permissions: readonly string[] | undefined) {
  if (!requiredPermissions?.length) {
    return true;
  }

  const granted = new Set(permissions ?? []);
  return requiredPermissions.every(permission => granted.has(permission));
}

function createEvaluation(
  request: AgentToolInvocationEvaluationRequest,
  policyResult: AgentToolPolicyResult,
  outcome: AgentToolInvocationOutcome,
  reason?: string
): AgentToolInvocationEvaluation {
  return {
    allowed: policyResult !== "denied",
    policyResult,
    outcome,
    disabledReason: policyResult === "denied" ? reason : undefined,
    audit: {
      actorId: request.actorId,
      sessionId: request.sessionId,
      toolId: request.tool.id,
      resourceTarget: request.resourceTarget,
      risk: request.tool.risk,
      invocationMode: request.invocationMode,
      policyResult,
      outcome,
      reason,
      recordedAt: new Date().toISOString()
    }
  };
}

function createDecision(
  request: AgentToolInvocationRequest,
  context: AgentToolInvocationPolicyContext,
  allowed: boolean,
  policyResult: AgentToolPolicyResult,
  outcome: AgentToolInvocationOutcome,
  reason?: string
): AgentToolPolicyDecision {
  return {
    allowed,
    policyResult,
    outcome,
    reason,
    audit: createToolInvocationAudit(request, context, policyResult, outcome, reason)
  };
}

function getLegacyDeniedReason(request: AgentToolInvocationRequest, context: AgentToolInvocationPolicyContext) {
  if (isDeniedTool(request.toolId, context.hostPolicy)) {
    return `Host Policy denied tool ${request.toolId}.`;
  }

  if (isDeniedTool(request.toolId, context.modulePolicy)) {
    return `Module Policy denied tool ${request.toolId}.`;
  }

  if (!isAllowedTool(request.toolId, context.hostPolicy) || !isAllowedTool(request.toolId, context.modulePolicy)) {
    return `Tool ${request.toolId} is not allowed by policy.`;
  }

  if (!isAllowedMode(request.invocationMode, context.hostPolicy)) {
    return `Host Policy denied ${request.invocationMode} invocation mode.`;
  }

  if (!isAllowedMode(request.invocationMode, context.modulePolicy)) {
    return `Module Policy denied ${request.invocationMode} invocation mode.`;
  }

  const missingPermission = request.requiredPermissions?.find(permission => !context.permissions?.includes(permission));
  if (missingPermission) {
    return `Missing permission ${missingPermission}.`;
  }

  return undefined;
}

function isDeniedTool(toolId: string, policy: AgentToolInvocationPolicy | undefined) {
  return policy?.deniedToolIds?.includes(toolId) === true;
}

function isAllowedTool(toolId: string, policy: AgentToolInvocationPolicy | undefined) {
  return !policy?.allowedToolIds || policy.allowedToolIds.includes(toolId);
}

function isAllowedMode(mode: StudioAgentToolInvocationMode, policy: AgentToolInvocationPolicy | undefined) {
  if (policy?.deniedInvocationModes?.includes(mode)) {
    return false;
  }

  return !policy?.allowedInvocationModes || policy.allowedInvocationModes.includes(mode);
}
