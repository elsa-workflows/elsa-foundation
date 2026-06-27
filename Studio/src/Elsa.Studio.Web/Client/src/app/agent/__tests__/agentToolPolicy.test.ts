import { describe, expect, it } from "vitest";
import { createToolInvocationAudit, evaluateToolInvocationPolicy, type AgentToolInvocationRequest } from "../agentToolPolicy";

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
  it("allows read-only and direct invocations when Host Policy and Module Policy allow them", () => {
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

  it("denies privileged and policy-blocked invocations", () => {
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

  it("creates failed audit outcomes with the required accountability fields", () => {
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
