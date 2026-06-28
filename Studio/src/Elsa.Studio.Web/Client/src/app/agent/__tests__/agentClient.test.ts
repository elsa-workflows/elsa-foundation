import { describe, expect, it, vi } from "vitest";
import { createAgentClient } from "../agentClient";
import type { StudioEndpointContext } from "../../../sdk";

describe("agent client", () => {
  it("uses provider-agnostic backend endpoints", async () => {
    const context = stubContext();
    const client = createAgentClient(context);

    await client.bootstrap();
    await client.createSession({
      mode: "troubleshoot",
      activeSurface: { route: "/workflows/order" },
      clientContext: { studioVersion: "1.0.0", sdkVersion: "1.0.0", moduleIds: [] }
    });
    await client.sendMessage("agt_01", {
      message: "Explain this workflow.",
      mode: "explain",
      capabilityId: "workflow.explain",
      contextAttachments: [{
        id: "ctx_01",
        source: "workflow",
        label: "Order workflow",
        contentType: "workflow.definition",
        content: "connectionString=super-secret",
        sensitivity: "secret-redacted",
        scope: "workflow"
      }]
    });
    await client.approveProposal("prop_01", { revision: "1" });
    await client.denyProposal("prop_01", { comment: "Not now" });
    await client.executeProposal("prop_01", { revision: "1" });
    await client.submitFeedback("agt_01", "msg_01", { rating: "positive" });

    expect(context.http.getJson).toHaveBeenCalledWith("/_elsa/agent/bootstrap");
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/sessions", expect.objectContaining({
      metadata: expect.objectContaining({ mode: "troubleshoot", route: "/workflows/order" })
    }));
    // The client never chooses a provider; the server binds the session to the single active harness.
    const sessionCall = (context.http.postJson as ReturnType<typeof vi.fn>).mock.calls.find(call => call[0] === "/_elsa/agent/sessions");
    expect(sessionCall?.[1]).not.toHaveProperty("providerId");
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/sessions/agt_01/messages", expect.objectContaining({
      role: "user",
      content: "Explain this workflow.",
      capabilityId: "workflow.explain",
      contextAttachments: [expect.objectContaining({ id: "ctx_01", kind: "workflow.definition", summary: "[secret redacted]" })]
    }));
    expect(context.http.postJson).not.toHaveBeenCalledWith(expect.any(String), expect.objectContaining({
      contextAttachments: [expect.objectContaining({ summary: expect.stringContaining("super-secret") })]
    }));
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/proposals/prop_01/approve", { actorId: "studio", revision: "1", comment: undefined });
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/proposals/prop_01/deny", { actorId: "studio", revision: undefined, comment: "Not now", reason: "Not now" });
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/proposals/prop_01/execute", { actorId: "studio", revision: "1", comment: undefined });
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/feedback", { sessionId: "agt_01", messageId: "msg_01", rating: 1, actorId: "studio" });
  });

  it("surfaces wrapped feedback errors", async () => {
    const context = stubContext();
    context.http.postJson = vi.fn(async url => {
      if (url === "/_elsa/agent/feedback") return { error: { code: "feedback_failed", message: "Feedback failed." } };
      return { data: { id: "ignored" } };
    });
    const client = createAgentClient(context);

    await expect(client.submitFeedback("agt_01", "msg_01", { rating: "negative" })).rejects.toThrow("Feedback failed.");
  });

  it("maps PascalCase backend contract responses", async () => {
    const context = stubPascalCaseContext();
    const client = createAgentClient(context);

    const bootstrap = await client.bootstrap();
    const session = await client.createSession({
      mode: "build",
      activeSurface: { route: "/workflows/order", resourceId: "workflow-1" },
      clientContext: { studioVersion: "1.0.0", sdkVersion: "1.0.0", moduleIds: ["workflows"] }
    });
    const message = await client.sendMessage("agt_01", {
      message: "Add email activity.",
      mode: "build",
      contextAttachments: []
    });
    const approval = await client.approveProposal("prop_01", {});
    const execution = await client.executeProposal("prop_01", {});

    expect(bootstrap.providerStatus).toBe("available");
    expect(bootstrap.provider).toMatchObject({
      providerId: "deterministic-workflow-authoring",
      isAvailable: true,
      providerKind: "agent-harness-provider",
      riskProfile: "sandboxed-execution",
      supportedOperations: ["chat", "tool-approval"]
    });
    expect(bootstrap.capabilities[0]).toMatchObject({
      id: "workflow.author",
      kind: "proposal",
      risk: "review-required"
    });
    expect(session).toMatchObject({ sessionId: "agt_01", status: "active" });
    expect(message).toMatchObject({ messageId: "msg_01", status: "pending" });
    expect(approval).toMatchObject({ proposalId: "prop_01", approvalStatus: "approved" });
    expect(execution).toMatchObject({ proposalId: "prop_01", approvalStatus: "executed" });
  });
});

function stubContext(): StudioEndpointContext {
  return {
    baseUrl: "https://foundation.example/",
    http: {
      getJson: vi.fn(async () => ({ data: { capabilities: [], provider: { providerId: "deterministic-test", isAvailable: true, status: "available" } } })),
      postJson: vi.fn(async url => {
        if (url === "/_elsa/agent/sessions") return { data: { id: "agt_01", status: "active" } };
        if (url.endsWith("/messages")) return { data: { message: { id: "msg_01" }, warnings: [] } };
        if (url.endsWith("/execute")) return { data: { proposalId: "prop_01", executed: true, message: "Executed." } };
        if (url === "/_elsa/agent/feedback") return { data: { id: "feedback_01" } };
        return { data: { id: "prop_01", status: "approved" } };
      })
    }
  };
}

function stubPascalCaseContext(): StudioEndpointContext {
  return {
    baseUrl: "https://foundation.example/",
    http: {
      getJson: vi.fn(async () => ({
        Data: {
          Enabled: true,
          ProviderStatus: "Available",
          Provider: {
            ProviderId: "deterministic-workflow-authoring",
            IsAvailable: true,
            Status: "Available",
            ProviderKind: 1,
            SupportedOperations: [0, 2],
            RiskProfile: 2
          },
          Capabilities: [{
            Id: "workflow.author",
            DisplayName: "Workflow authoring",
            Description: "Author workflows.",
            Kind: 3,
            Risk: 1,
            Surfaces: ["workflow.definition"]
          }]
        }
      })),
      postJson: vi.fn(async url => {
        if (url === "/_elsa/agent/sessions") return { Data: { SessionId: "agt_01", Status: "Active" } };
        if (url.endsWith("/messages")) return { Data: { Message: { Id: "msg_01" }, Warnings: [] } };
        if (url.endsWith("/execute")) return { Data: { ProposalId: "prop_01", Executed: true, Message: "Executed." } };
        return { Data: { ProposalId: "prop_01", ApprovalStatus: "Approved", ApprovedAt: "2026-06-25T21:00:00Z" } };
      })
    }
  };
}
