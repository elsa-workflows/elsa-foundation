import { describe, expect, it } from "vitest";
import type { StudioAgentResourceTarget, StudioAgentToolContractContribution } from "../../../sdk";
import {
  createToolInvocationAudit,
  evaluateAgentToolInvocation,
  evaluateToolInvocationPolicy,
  type AgentToolInvocationRequest
} from "../agentToolPolicy";

const target: StudioAgentResourceTarget = {
  resourceType: "workflow-definition",
  resourceId: "order-flow",
  displayName: "Order flow"
};

const baseRequest: AgentToolInvocationRequest = {
  toolId: "workflow.inspect",
  moduleId: "workflows",
  sessionId: "agt_01",
  invocationMode: "read-only",
  risk: "read-only",
  resourceTarget: {
    resourceType: "workflow-definition",
    resourceId: "wf_01",
    displayName: "Order workflow"
  },
  requiredPermissions: ["workflows.read"]
};

const baseContext = {
  actor: "studio-user",
  sessionId: "agt_01",
  permissions: ["workflows.read", "workflows.write"],
  hostPolicy: {
    contextVisibility: true,
    requiresApprovalForMutations: false,
    allowDirectToolInvocations: true
  },
  now: () => new Date("2026-06-27T00:00:00.000Z")
};

describe("agent tool policy", () => {
  it("allows SDK read-only invocations and records audit context", () => {
    const result = evaluateAgentToolInvocation({
      actorId: "user-1",
      sessionId: "session-1",
      tool: tool({ id: "workflow.inspect", risk: "read-only", invocationModes: ["read-only"] }),
      resourceTarget: target,
      invocationMode: "read-only",
      permissions: ["workflows.read"]
    });

    expect(result).toMatchObject({
      allowed: true,
      policyResult: "allowed",
      outcome: "allowed",
      audit: {
        actorId: "user-1",
        sessionId: "session-1",
        toolId: "workflow.inspect",
        resourceTarget: target,
        risk: "read-only",
        invocationMode: "read-only",
        policyResult: "allowed",
        outcome: "allowed"
      }
    });
    expect(result.audit.recordedAt).toEqual(expect.any(String));
  });

  it("allows SDK direct invocation only when host and module policy allow the tool risk", () => {
    const directTool = tool({
      id: "workflow.repair",
      risk: "review-required",
      invocationModes: ["direct", "proposal"]
    });

    const denied = evaluateAgentToolInvocation({
      actorId: "user-1",
      sessionId: "session-1",
      tool: directTool,
      resourceTarget: target,
      invocationMode: "direct"
    });
    const allowed = evaluateAgentToolInvocation({
      actorId: "user-1",
      sessionId: "session-1",
      tool: directTool,
      resourceTarget: target,
      invocationMode: "direct",
      hostPolicy: { directAllowedRisks: ["read-only", "review-required"] },
      modulePolicy: { directAllowedRisks: ["read-only", "review-required"] }
    });

    expect(denied).toMatchObject({
      allowed: true,
      policyResult: "requires-proposal",
      outcome: "proposal-created"
    });
    expect(allowed).toMatchObject({
      allowed: true,
      policyResult: "allowed",
      outcome: "allowed"
    });
  });

  it("denies SDK host policy, missing permissions, and privileged invocations without dual approval", () => {
    const hostDenied = evaluateAgentToolInvocation({
      actorId: "user-1",
      sessionId: "session-1",
      tool: tool({ id: "workflow.delete", risk: "destructive", requiredPermissions: ["workflows.delete"], invocationModes: ["proposal"] }),
      resourceTarget: target,
      invocationMode: "proposal",
      permissions: ["workflows.delete"],
      hostPolicy: { deniedToolIds: ["workflow.delete"] }
    });
    const missingPermission = evaluateAgentToolInvocation({
      actorId: "user-1",
      sessionId: "session-1",
      tool: tool({ id: "workflow.delete", risk: "destructive", requiredPermissions: ["workflows.delete"], invocationModes: ["proposal"] }),
      resourceTarget: target,
      invocationMode: "proposal",
      permissions: []
    });
    const privilegedDenied = evaluateAgentToolInvocation({
      actorId: "admin-1",
      sessionId: "session-1",
      tool: tool({ id: "host.rotate-secret", risk: "admin", invocationModes: ["privileged"] }),
      resourceTarget: { resourceType: "secret", resourceId: "stripe-api-key" },
      invocationMode: "privileged",
      hostPolicy: { allowPrivilegedTools: true },
      modulePolicy: { allowPrivilegedTools: false }
    });

    expect(hostDenied).toMatchObject({
      allowed: false,
      policyResult: "denied",
      outcome: "denied",
      disabledReason: "Host Policy denies this tool."
    });
    expect(missingPermission.audit).toMatchObject({
      policyResult: "denied",
      outcome: "denied",
      reason: "Actor is missing one or more required tool permissions."
    });
    expect(privilegedDenied.disabledReason).toBe("Privileged invocation requires Host Policy and Module Policy approval.");
  });

  it("records SDK proposal and failed invocation outcomes", () => {
    const proposal = evaluateAgentToolInvocation({
      actorId: "user-1",
      sessionId: "session-1",
      tool: tool({ id: "workflow.repair", risk: "review-required", invocationModes: ["proposal"] }),
      resourceTarget: target,
      invocationMode: "proposal"
    });
    const failed = evaluateAgentToolInvocation({
      actorId: "user-1",
      sessionId: "session-1",
      tool: tool({ id: "workflow.inspect", risk: "read-only", invocationModes: ["read-only"] }),
      resourceTarget: target,
      invocationMode: "read-only",
      failure: "Tool backend timed out."
    });
    const failedProposal = evaluateAgentToolInvocation({
      actorId: "user-1",
      sessionId: "session-1",
      tool: tool({ id: "workflow.repair", risk: "review-required", invocationModes: ["proposal"] }),
      resourceTarget: target,
      invocationMode: "proposal",
      failure: "Proposal backend timed out."
    });

    expect(proposal).toMatchObject({
      allowed: true,
      policyResult: "requires-proposal",
      outcome: "proposal-created",
      audit: {
        policyResult: "requires-proposal",
        outcome: "proposal-created"
      }
    });
    expect(failed).toMatchObject({
      allowed: true,
      policyResult: "allowed",
      outcome: "failed",
      audit: {
        policyResult: "allowed",
        outcome: "failed",
        reason: "Tool backend timed out."
      }
    });
    expect(failedProposal).toMatchObject({
      allowed: true,
      policyResult: "requires-proposal",
      outcome: "failed",
      audit: {
        policyResult: "requires-proposal",
        outcome: "failed",
        reason: "Proposal backend timed out."
      }
    });
  });

  it("allows UI read-only and direct invocations when Host Policy and Module Policy allow them", () => {
    const readOnly = evaluateToolInvocationPolicy(baseRequest, baseContext);
    const direct = evaluateToolInvocationPolicy({
      ...baseRequest,
      toolId: "workflow.rename",
      invocationMode: "direct",
      risk: "review-required",
      requiredPermissions: ["workflows.write"]
    }, {
      ...baseContext,
      modulePolicy: { allowedInvocationModes: ["direct"] }
    });

    expect(readOnly.allowed).toBe(true);
    expect(readOnly.audit).toMatchObject({
      actor: "studio-user",
      sessionId: "agt_01",
      toolId: "workflow.inspect",
      target: baseRequest.resourceTarget,
      risk: "read-only",
      policyResult: "allowed",
      outcome: "allowed"
    });
    expect(direct.allowed).toBe(true);
    expect(direct.audit.invocationMode).toBe("direct");
  });

  it("requires a proposal when direct mutation is disabled by Host Policy", () => {
    const decision = evaluateToolInvocationPolicy({
      ...baseRequest,
      toolId: "workflow.rename",
      invocationMode: "direct",
      risk: "review-required",
      requiredPermissions: ["workflows.write"]
    }, {
      ...baseContext,
      hostPolicy: {
        contextVisibility: true,
        requiresApprovalForMutations: true,
        allowDirectToolInvocations: false
      }
    });

    expect(decision.allowed).toBe(false);
    expect(decision.policyResult).toBe("proposal-required");
    expect(decision.outcome).toBe("proposal-created");
    expect(decision.reason).toContain("reviewed proposal");
  });

  it("denies UI privileged and policy-blocked invocations", () => {
    const privileged = evaluateToolInvocationPolicy({
      ...baseRequest,
      toolId: "workflow.admin",
      invocationMode: "privileged",
      risk: "admin"
    }, baseContext);
    const denied = evaluateToolInvocationPolicy(baseRequest, {
      ...baseContext,
      hostPolicy: {
        contextVisibility: true,
        requiresApprovalForMutations: false,
        deniedToolIds: ["workflow.inspect"]
      }
    });

    expect(privileged.allowed).toBe(false);
    expect(privileged.reason).toContain("privileged");
    expect(denied.allowed).toBe(false);
    expect(denied.reason).toBe("Host Policy denied tool workflow.inspect.");
  });

  it("creates failed UI audit outcomes with the required accountability fields", () => {
    const audit = createToolInvocationAudit(baseRequest, baseContext, "allowed", "failed", "Backend failed.");

    expect(audit).toMatchObject({
      state: "failed",
      outcome: "Backend failed.",
      actor: "studio-user",
      sessionId: "agt_01",
      toolId: "workflow.inspect",
      target: baseRequest.resourceTarget,
      risk: "read-only",
      invocationMode: "read-only",
      policyResult: "allowed",
      recordedAt: "2026-06-27T00:00:00.000Z"
    });
  });
});

function tool(overrides: Partial<StudioAgentToolContractContribution>): StudioAgentToolContractContribution {
  return {
    id: "tool",
    slotId: "slot",
    displayName: "Tool",
    description: "Tool",
    surfaces: ["*"],
    inputSchema: {},
    resourceTargetSchema: {},
    resultSchema: {},
    risk: "read-only",
    invocationModes: ["read-only"],
    ...overrides
  };
}
