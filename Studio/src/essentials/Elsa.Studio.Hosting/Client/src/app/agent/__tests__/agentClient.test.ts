import { describe, expect, it, vi } from "vitest";
import { AgentApiError, classifyAutonomyFailure, createAgentClient, POLICY_CHANGED } from "../agentClient";
import { StudioHttpError, type StudioEndpointContext } from "../../../sdk";

describe("agent client", () => {
  it("transports the per-message surface and maps only server-admitted context metadata and opaque payload", async () => {
    const context = stubContext();
    const admitted = { id: "owner-context", moduleId: "Sample", schemaVersion: "1", source: "Sample", sourceId: "one", label: "Owner snapshot", contentType: "sample", sensitivity: "Internal", scope: "screen", origin: "AuthoritativeRead", summary: "Authorized read", target: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r2" }, content: { opaque: { value: 42 } }, references: { owner: "one" }, agentScopes: ["sample-read"] };
    context.http.postJson = vi.fn(async () => ({ data: { message: { id: "message", status: "Pending", context: [admitted] }, warnings: [] } })) as typeof context.http.postJson;
    const activeSurface = { route: "/sample/one", resourceType: "sample", resourceId: "one", selection: { opaqueSelection: "selected" } };
    const result = await createAgentClient(context).sendMessage("session", { message: "Inspect", mode: "explain", activeSurface, contextAttachments: [] });
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/sessions/session/messages", expect.objectContaining({ activeSurface }));
    expect(result.contextAttachments).toEqual([expect.objectContaining({ ...admitted, sensitivity: "internal", origin: "authoritativeRead" })]);
  });

  it("fails closed on missing admitted context and strips redacted content from the server response", async () => {
    const context = stubContext();
    context.http.postJson = vi.fn()
      .mockResolvedValueOnce({ data: { message: { id: "message", status: "Pending" } } })
      .mockResolvedValueOnce({ data: { message: { id: "redacted-message", status: "Pending", context: [{ id: "redacted", label: "Redacted context", source: "Sample", contentType: "sample", scope: "redacted", sensitivity: "SecretRedacted", content: { excluded: "do not display" }, target: { resourceId: "excluded" }, references: { excluded: "private reference" } }] } } });
    const client = createAgentClient(context);
    expect((await client.sendMessage("session", { message: "Inspect", mode: "explain", contextAttachments: [] })).contextAttachments).toEqual([]);
    const result = await client.sendMessage("session", { message: "Inspect redaction", mode: "explain", contextAttachments: [] });
    expect(result.contextAttachments).toEqual([expect.objectContaining({ sensitivity: "secret-redacted", content: undefined, target: null, references: {} })]);
    expect(JSON.stringify(result.contextAttachments)).not.toContain("do not display");
  });

  it.each((["approveProposal", "denyProposal", "executeProposal"] as const)
    .flatMap(decision => ["http", "wrapped"].map(transport => ({ decision, transport }))))("preserves authoritative terminal $decision data on $transport refusal", async ({ decision, transport }) => {
    const context = stubContext();
    const envelope = { error: { code: "agent.proposal.expired", message: "The reviewed invocation expired.", statusCode: 409 }, data: { proposalId: "proposal", executed: false, status: "expired", resourceType: "sample", resourceId: "one", summary: "Nothing was applied.", clientInvocation: null, toolResult: null } };
    context.http.postJson = async <T>() => { if (transport === "http") throw new StudioHttpError(409, envelope.error.message, null, envelope); return envelope as T; };
    await expect(createAgentClient(context)[decision]("proposal", { revision: "r1" })).rejects.toMatchObject({ status: 409, code: "agent.proposal.expired", message: envelope.error.message, terminalOutcome: { proposalId: "proposal", approvalStatus: "expired", executed: false, toolResult: null } });
  });

  it.each([{ status: "approved", proposalId: "proposal" }, { status: "expired", proposalId: "another-proposal" }])("does not treat nonterminal or unrelated data as a terminal outcome: %j", async data => {
    const context = stubContext(); const error = new StudioHttpError(409, "Refresh the review before retrying.", null, { error: { code: "agent.conflict", message: "Refresh the review before retrying." }, data: { ...data, executed: false } });
    context.http.postJson = vi.fn(async () => { throw error; });
    await expect(createAgentClient(context).executeProposal("proposal", { revision: "r1" })).rejects.toBe(error);
    expect(error).not.toHaveProperty("terminalOutcome");
  });

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
      contextAttachments: [expect.objectContaining({ id: "ctx_01", contentType: "workflow.definition", summary: "[secret redacted]" })]
    }));
    expect(context.http.postJson).not.toHaveBeenCalledWith(expect.any(String), expect.objectContaining({
      contextAttachments: [expect.objectContaining({ summary: expect.stringContaining("super-secret") })]
    }));
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/proposals/prop_01/approve", { actorId: "studio", revision: "1", comment: undefined });
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/proposals/prop_01/deny", { actorId: "studio", revision: undefined, comment: "Not now", reason: "Not now" });
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/proposals/prop_01/execute", { actorId: "studio", revision: "1", comment: undefined });
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/feedback", { sessionId: "agt_01", messageId: "msg_01", rating: 1, actorId: "studio" });
  });

  it("forwards a structured workflow graph as the attachment content", async () => {
    const context = stubContext();
    const client = createAgentClient(context);
    const graph = {
      workflowId: "wf-1",
      revision: "rev-7",
      activities: [
        { id: "WriteLine1", type: "Elsa.Workflows.WriteLine", displayName: "Write Line" },
        { id: "WriteLine2", type: "Elsa.Workflows.WriteLine", displayName: "Write Line" }
      ],
      connections: [{ source: "WriteLine1", target: "WriteLine2", sourcePort: "Done" }]
    };

    await client.sendMessage("agt_01", {
      message: "Add a third write line after the second.",
      mode: "build",
      contextAttachments: [{
        id: "workflow:wf-1",
        source: "workflow",
        sourceId: "wf-1",
        label: "Hello World",
        contentType: "workflow.definition",
        content: graph,
        references: { revision: "rev-7" },
        sensitivity: "internal",
        scope: "screen"
      }]
    });

    const messageCall = (context.http.postJson as ReturnType<typeof vi.fn>).mock.calls.find(call => String(call[0]).endsWith("/messages"));
    const attachment = (messageCall?.[1] as { contextAttachments: Array<Record<string, unknown>> }).contextAttachments[0];
    // The live graph must reach the backend's attachment content so the agent sees real node ids.
    expect(attachment.content).toEqual(graph);
    expect((attachment.references as Record<string, string>).revision).toBe("rev-7");
  });

  it("maps the autonomy policy and forwards the requested mode", async () => {
    const context = stubContext();
    context.http.getJson = vi.fn(async () => ({
      data: {
        enabled: true,
        capabilities: [],
        provider: { providerId: "deterministic-test", isAvailable: true, status: "available" },
        policy: {
          contextVisibility: true,
          defaultAutonomyMode: "auto-read-only",
          maxAutonomyMode: "auto-read-only",
          allowedAutonomyModes: ["manual", "auto-read-only"]
        }
      }
    }));
    const client = createAgentClient(context);

    const bootstrap = await client.bootstrap();
    expect(bootstrap.policy).toMatchObject({
      defaultAutonomyMode: "auto-read-only",
      maxAutonomyMode: "auto-read-only",
      allowedAutonomyModes: ["manual", "auto-read-only"]
    });

    await client.createSession({
      mode: "build",
      autonomyMode: "full-auto",
      activeSurface: { route: "/workflows/order" },
      clientContext: { studioVersion: "1.0.0", sdkVersion: "1.0.0", moduleIds: [] }
    });

    // The client forwards the requested mode verbatim; the server is the authoritative ceiling.
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/sessions", expect.objectContaining({ autonomyMode: "full-auto" }));
  });

  it("derives allowed autonomy modes from the ceiling when the backend omits them", async () => {
    const context = stubContext();
    context.http.getJson = vi.fn(async () => ({
      data: {
        enabled: true,
        capabilities: [],
        provider: { providerId: "deterministic-test", isAvailable: true, status: "available" },
        policy: { contextVisibility: true, maxAutonomyMode: "full-auto" }
      }
    }));

    const bootstrap = await createAgentClient(context).bootstrap();

    expect(bootstrap.policy.allowedAutonomyModes).toEqual(["manual", "auto-read-only", "full-auto"]);
    expect(bootstrap.policy.defaultAutonomyMode).toBe("full-auto");
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
  it("retains a generated delivery ID across retries and exposes duplicate/admission warnings", async () => {
    const context = stubContext();
    context.http.postJson = vi.fn(async () => ({ data: { message: { id: "message", status: "completed" }, warnings: [{ code: "agent.message.duplicate", message: "Already accepted.", target: "delivery" }] } })) as typeof context.http.postJson;
    const client = createAgentClient(context);
    const request = { message: "Inspect", mode: "explain" as const, contextAttachments: [] };
    const first = await client.sendMessage("session", request); await client.sendMessage("session", request);
    const calls = (context.http.postJson as ReturnType<typeof vi.fn>).mock.calls;
    expect(calls[0][1].requestId).toBeTruthy(); expect(calls[1][1].requestId).toBe(calls[0][1].requestId);
    expect(first).toMatchObject({ status: "completed", warnings: [{ code: "agent.message.duplicate", target: "delivery" }] });
  });

  it("sends one conditional autonomy-only update through the selected backend and maps the acknowledged policy", async () => {
    const context = stubContext();
    context.http.putJson = vi.fn(async () => ({ data: { autonomyMode: "fullAuto", maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"], revision: 7 } })) as typeof context.http.putJson;
    const policy = await createAgentClient(context).updateAutonomy("agt/01", { autonomyMode: "full-auto", expectedRevision: 6 });
    // Exactly the intent and its precondition: no owner, actor, tenant or unrelated policy fields travel.
    expect(vi.mocked(context.http.putJson).mock.calls).toEqual([["/_elsa/agent/sessions/agt%2F01/autonomy", { autonomyMode: "full-auto", expectedRevision: 6 }]]);
    expect(policy).toEqual({ autonomyMode: "full-auto", maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"], revision: 7 });
  });

  it.each([
    [409, "agent.session.policy_stale", "stale"],
    [409, "agent.session.busy", "busy"],
    [409, "agent.session.closed", "closed"],
    [409, "agent.session.autonomy_unsupported", "unsupported"],
    [400, "agent.session.autonomy_invalid", "invalid"],
    [404, "agent.session.not_found", "not-found"],
    [403, "agent.forbidden", "refused"]
  ] as const)("classifies a %s %s autonomy refusal as %s for both HTTP and wrapped envelopes", async (status, code, kind) => {
    const envelope = { data: null, error: { code, message: `Refused: ${code}`, statusCode: status } };
    for (const transport of ["http", "wrapped"]) {
      const context = stubContext();
      context.http.putJson = (async () => { if (transport === "http") throw new StudioHttpError(status, "HTTP failure", null, envelope); return envelope; }) as typeof context.http.putJson;
      const error = await createAgentClient(context).updateAutonomy("agt_01", { autonomyMode: "manual", expectedRevision: 1 }).catch(caught => caught);
      expect(error).toBeInstanceOf(AgentApiError); expect(error).toMatchObject({ status, code, message: `Refused: ${code}` });
      expect(classifyAutonomyFailure(error)).toBe(kind);
    }
  });

  it.each([
    ["a network failure", new TypeError("Failed to fetch")],
    ["a gateway failure without a refusal code", new StudioHttpError(502, "Bad gateway")],
    ["an unreadable success body", new StudioHttpError(200, "Expected JSON")]
  ])("treats %s as a possibly committed lost response", async (_name, failure) => {
    const context = stubContext();
    context.http.putJson = vi.fn(async () => { throw failure; }) as typeof context.http.putJson;
    const error = await createAgentClient(context).updateAutonomy("agt_01", { autonomyMode: "manual", expectedRevision: 1 }).catch(caught => caught);
    expect(classifyAutonomyFailure(error)).toBe("lost");
  });

  it("refuses an acknowledgement without a readable policy instead of assuming one", async () => {
    const context = stubContext();
    context.http.putJson = vi.fn(async () => ({ data: { autonomyMode: "full-auto" } })) as typeof context.http.putJson;
    const error = await createAgentClient(context).updateAutonomy("agt_01", { autonomyMode: "full-auto", expectedRevision: 1 }).catch(caught => caught);
    expect(error).toBeInstanceOf(StudioHttpError); expect(classifyAutonomyFailure(error)).toBe("lost");
  });

  it("reads the created and detailed session policy and accepted revisions without inventing defaults", async () => {
    const context = stubContext();
    const policy = { autonomyMode: "AutoReadOnly", maxAutonomyMode: "AutoReadOnly", allowedAutonomyModes: ["Manual", "AutoReadOnly"], revision: 3 };
    context.http.postJson = vi.fn(async () => ({ data: { sessionId: "agt_01", status: "active", policy } })) as typeof context.http.postJson;
    context.http.getJson = vi.fn(async () => ({ data: { sessionId: "agt_01", status: "active", policy, messages: [{ id: "m1", role: "user", content: "x", status: "completed", acceptedPolicyRevision: 2 }, { id: "m2", role: "assistant", content: "y", status: "completed", acceptedPolicyRevision: null }] } })) as typeof context.http.getJson;
    const client = createAgentClient(context);
    const request = { mode: "build" as const, activeSurface: { route: "/" }, clientContext: { studioVersion: "1", sdkVersion: "1", moduleIds: [] } };
    const expected = { autonomyMode: "auto-read-only", maxAutonomyMode: "auto-read-only", allowedAutonomyModes: ["manual", "auto-read-only"], revision: 3 };
    expect((await client.createSession(request)).policy).toEqual(expected);
    const detail = await client.getSession("agt_01");
    expect(context.http.getJson).toHaveBeenCalledWith("/_elsa/agent/sessions/agt_01");
    expect(detail.policy).toEqual(expected);
    expect(detail.messages).toEqual([expect.objectContaining({ id: "m1", acceptedPolicyRevision: 2 }), expect.objectContaining({ id: "m2", acceptedPolicyRevision: undefined })]);
    context.http.postJson = vi.fn(async () => ({ data: { sessionId: "agt_02", status: "active" } })) as typeof context.http.postJson;
    expect(await client.createSession(request)).not.toHaveProperty("policy");
  });

  it("maps a message's accepted policy and surfaces a policy-changed admission as a coded refusal", async () => {
    const context = stubContext();
    context.http.postJson = vi.fn()
      .mockResolvedValueOnce({ data: { message: { id: "m1", status: "Pending", acceptedPolicy: { policy: { autonomyMode: "fullAuto", maxAutonomyMode: "fullAuto" }, revision: 4 } }, warnings: [] } })
      .mockRejectedValueOnce(new StudioHttpError(409, "Conflict", null, { error: { code: POLICY_CHANGED, message: "The session policy changed.", statusCode: 409 } }))
      .mockResolvedValueOnce({ error: { code: POLICY_CHANGED, message: "The session policy changed.", statusCode: 409 } })
      .mockRejectedValueOnce(new StudioHttpError(409, "Other conflict", null, { error: { code: "agent.other" } })) as typeof context.http.postJson;
    const client = createAgentClient(context);
    const request = () => ({ message: "Resize", mode: "build" as const, contextAttachments: [] });
    expect((await client.sendMessage("agt_01", request())).acceptedPolicy).toEqual({ autonomyMode: "full-auto", revision: 4 });
    await expect(client.sendMessage("agt_01", request())).rejects.toMatchObject({ name: "AgentApiError", status: 409, code: POLICY_CHANGED });
    await expect(client.sendMessage("agt_01", request())).rejects.toMatchObject({ name: "AgentApiError", code: POLICY_CHANGED });
    // Unrelated refusals keep their historical shape.
    await expect(client.sendMessage("agt_01", request())).rejects.toMatchObject({ name: "StudioHttpError", message: "Other conflict" });
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
