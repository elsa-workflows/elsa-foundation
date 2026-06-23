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
      providerId: "deterministic-test",
      metadata: expect.objectContaining({ mode: "troubleshoot", route: "/workflows/order" })
    }));
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
});

function stubContext(): StudioEndpointContext {
  return {
    baseUrl: "https://foundation.example/",
    http: {
      getJson: vi.fn(async () => ({ data: { capabilities: [], providers: [{ providerId: "deterministic-test", isAvailable: true, status: "available" }] } })),
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
