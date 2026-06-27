import type { StudioAgentRisk, StudioAgentToolInvocationMode } from "../../sdk";
import type {
  AgentProposalAuditState,
  AgentResourceTarget,
  AgentToolInvocationOutcome,
  AgentToolInvocationPolicy,
  AgentToolPolicyResult
} from "./agentTypes";

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

export function evaluateToolInvocationPolicy(
  request: AgentToolInvocationRequest,
  context: AgentToolInvocationPolicyContext
): AgentToolPolicyDecision {
  const deniedReason = getDeniedReason(request, context);
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

function getDeniedReason(request: AgentToolInvocationRequest, context: AgentToolInvocationPolicyContext) {
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
