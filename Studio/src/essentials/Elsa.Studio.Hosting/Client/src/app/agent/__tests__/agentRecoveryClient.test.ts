import { describe, expect, it, vi } from "vitest";
import { classifyRecoveryFailure, createAgentClient, getAgentRetryAfterSeconds } from "../agentClient";
import { createHttpClient, StudioHttpError, type StudioEndpointContext } from "../../../sdk";

// Preview (#3441): the recovery reads and actions of the Weaver session API.
describe("agent recovery client", () => {
  it("lists only the caller's sessions through the owner-scoped route with workspace and cursor", async () => {
    const context = stub();
    context.http.getJson = vi.fn(async () => ({ data: { items: [
      { id: "agt_2", title: "Studio assistant", status: "active", updatedAt: "2026-11-02T10:00:00Z", expiresAt: "2026-11-03T10:00:00Z", pendingReviewCount: 1, recoveryState: null },
      { id: "agt_1", status: "expired", updatedAt: "2026-11-01T10:00:00Z", pendingReviewCount: 0 }
    ], next: "cursor-1" } })) as typeof context.http.getJson;
    const page = await createAgentClient(context).listSessions({ workspace: "studio:acme", take: 2, before: "cursor-0" });
    expect(context.http.getJson).toHaveBeenCalledWith("/_elsa/agent/sessions?workspace=studio%3Aacme&take=2&before=cursor-0");
    expect(page).toEqual({ next: "cursor-1", items: [
      { id: "agt_2", title: "Studio assistant", status: "active", updatedAt: "2026-11-02T10:00:00Z", expiresAt: "2026-11-03T10:00:00Z", pendingReviewCount: 1 },
      { id: "agt_1", status: "expired", updatedAt: "2026-11-01T10:00:00Z", pendingReviewCount: 0 }
    ] });
    context.http.getJson = vi.fn(async () => ({ data: { items: [] } })) as typeof context.http.getJson;
    expect(await createAgentClient(context).listSessions()).toEqual({ items: [] });
    expect(context.http.getJson).toHaveBeenCalledWith("/_elsa/agent/sessions");
  });

  it("sends the workspace and the continued session only when given", async () => {
    const context = stub();
    const request = { mode: "build" as const, activeSurface: { route: "/" }, clientContext: { studioVersion: "1", sdkVersion: "1", moduleIds: [] } };
    const client = createAgentClient(context);
    await client.createSession({ ...request, workspace: "studio:acme", continuedFrom: "agt_1" });
    await client.createSession(request);
    const bodies = (context.http.postJson as ReturnType<typeof vi.fn>).mock.calls.map(call => call[1]);
    expect(bodies[0]).toMatchObject({ workspace: "studio:acme", continuedFrom: "agt_1" });
    expect(bodies[1]).not.toHaveProperty("workspace");
    expect(bodies[1]).not.toHaveProperty("continuedFrom");
  });

  it("reads recorded proposals and messages with their recovery reasons and the owner's statement", async () => {
    const context = stub();
    const prepared = { invocationId: "inv_1", sessionId: "agt_1", expiresAt: "2026-11-02T10:05:00Z", executionLocation: "clientWorkingState", target: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" } };
    context.http.getJson = vi.fn(async (path: string) => path.endsWith("/proposals")
      ? { data: [{ id: "prop_1", title: "Change", summary: "Change one", risk: "reviewRequired", status: "uncertain", baseRevision: "r1", preparedInvocation: prepared, resourceTarget: prepared.target, ownerResolution: { claim: "notApplied", actorId: "ada", resolvedAt: "2026-11-02T10:06:00Z" } },
        { id: "prop_2", title: "Other", summary: "Other", status: "awaitingClient", result: null }] }
      : { data: { sessionId: "agt_1", status: "active", title: "Studio assistant", messages: [
        { id: "m1", role: "user", content: "Go", status: "cancelled", statusReason: "interrupted" },
        { id: "m2", role: "user", content: "Again", status: "pending" },
        { id: "m3", role: "user", content: "Odd", status: "cancelled", statusReason: "something-else" }] } }) as typeof context.http.getJson;
    const client = createAgentClient(context);
    const proposals = await client.listProposals("agt_1");
    expect(context.http.getJson).toHaveBeenCalledWith("/_elsa/agent/sessions/agt_1/proposals");
    expect(proposals[0]).toMatchObject({ id: "prop_1", status: "uncertain", baseRevision: "r1", expiresAt: prepared.expiresAt, preparedInvocation: prepared, ownerResolution: { claim: "not-applied", actorId: "ada", resolvedAt: "2026-11-02T10:06:00Z" } });
    expect(proposals[1]).toMatchObject({ id: "prop_2", status: "awaiting-client" });
    expect(proposals[1].ownerResolution).toBeUndefined();
    expect(proposals[1].result).toBeUndefined();
    const detail = await client.getSession("agt_1");
    expect(detail.title).toBe("Studio assistant");
    expect(detail.messages).toEqual([
      expect.objectContaining({ id: "m1", content: "Go", status: "cancelled", statusReason: "interrupted" }),
      expect.objectContaining({ id: "m2", status: "pending" }),
      expect.not.objectContaining({ statusReason: expect.anything() })
    ]);
  });

  it("withdraws a not-started prompt and resolves an uncertain operation on their routes", async () => {
    const context = stub();
    context.http.postJson = vi.fn(async (path: string) => path.endsWith("/withdraw")
      ? { data: { id: "m2", role: "user", content: "Again", status: "cancelled", statusReason: "withdrawn" } }
      : { data: { id: "prop_1", status: "uncertain", ownerResolution: { claim: "applied", actorId: "ada" } } }) as typeof context.http.postJson;
    const client = createAgentClient(context);
    expect(await client.withdrawMessage("agt 1", "m2")).toMatchObject({ id: "m2", status: "cancelled", statusReason: "withdrawn" });
    expect(context.http.postJson).toHaveBeenLastCalledWith("/_elsa/agent/sessions/agt%201/messages/m2/withdraw", {});
    expect(await client.resolveClientInvocation("agt 1", "inv_1", "not-applied")).toMatchObject({ id: "prop_1", ownerResolution: { claim: "applied" } });
    expect(context.http.postJson).toHaveBeenLastCalledWith("/_elsa/agent/sessions/agt%201/client-invocations/inv_1/resolution", { claim: "notApplied" });
    await client.resolveClientInvocation("agt_1", "inv_1", "unknown");
    expect(context.http.postJson).toHaveBeenLastCalledWith("/_elsa/agent/sessions/agt_1/client-invocations/inv_1/resolution", { claim: "unknown" });
  });

  it.each([
    ["agent.session.owned_elsewhere", 409, "owned-elsewhere"],
    ["agent.session.ownership_lost", 409, "ownership-lost"],
    ["agent.session.unavailable_state", 409, "unavailable"],
    ["agent.session.query_unsupported", 409, "query-unsupported"],
    ["agent.message.not_pending", 409, "not-pending"],
    ["agent.invocation.not_uncertain", 409, "not-uncertain"],
    ["agent.invocation.resolution_conflict", 409, "resolution-conflict"],
    ["agent.invocation.resolution_invalid", 400, "invalid"],
    ["agent.proposal.conflict", 409, "proposal-conflict"],
    ["agent.proposal.policy_stale", 409, "policy-stale"],
    ["agent.session.not_found", 404, "not-found"],
    ["agent.session.forbidden", 403, "not-found"],
    ["agent.message.not_found", 404, "not-found"],
    ["agent.invocation.not_found", 404, "not-found"]
  ] as const)("maps %s on HTTP and wrapped refusals to %s", async (code, status, failure) => {
    for (const transport of ["http", "wrapped"]) {
      const context = stub();
      const envelope = { error: { code, message: `Refused: ${code}`, statusCode: status } };
      context.http.postJson = vi.fn(async () => { if (transport === "http") throw new StudioHttpError(status, envelope.error.message, null, envelope); return envelope; }) as typeof context.http.postJson;
      const error = await createAgentClient(context).withdrawMessage("agt_1", "m1").catch(caught => caught);
      expect(error).toMatchObject({ name: "AgentApiError", code, status, message: envelope.error.message });
      expect(classifyRecoveryFailure(error)).toBe(failure);
    }
  });

  it("classifies uncoded and transport failures without inventing a refusal", () => {
    expect(classifyRecoveryFailure(new Error("network"))).toBe("lost");
    expect(classifyRecoveryFailure(new StudioHttpError(503, "down"))).toBe("lost");
    expect(classifyRecoveryFailure(new StudioHttpError(404, "missing"))).toBe("not-found");
    expect(classifyRecoveryFailure(new StudioHttpError(403, "forbidden"))).toBe("not-found");
    expect(classifyRecoveryFailure(new StudioHttpError(409, "other", null, { error: { code: "agent.other" } }))).toBe("refused");
  });

  it("carries a retryable owner refusal's Retry-After hint from the transport", async () => {
    const fetch = vi.fn(async () => new Response(JSON.stringify({ error: { code: "agent.session.owned_elsewhere", message: "Another Studio host owns this session.", statusCode: 409 } }),
      { status: 409, headers: { "Content-Type": "application/json", "Retry-After": "17" } }));
    const context = { baseUrl: "https://foundation.example/", http: createHttpClient("https://foundation.example/", { fetch }) } as StudioEndpointContext;
    const error = await createAgentClient(context).listProposals("agt_1").catch(caught => caught);
    expect(error).toMatchObject({ name: "AgentApiError", code: "agent.session.owned_elsewhere", status: 409 });
    expect(classifyRecoveryFailure(error)).toBe("owned-elsewhere");
    expect(getAgentRetryAfterSeconds(error)).toBe(17);
    fetch.mockImplementationOnce(async () => new Response("{}", { status: 409, headers: { "Content-Type": "application/json", "Retry-After": "soon" } }));
    expect(getAgentRetryAfterSeconds(await createAgentClient(context).listProposals("agt_1").catch(caught => caught))).toBeUndefined();
  });
});

function stub(): StudioEndpointContext {
  return {
    baseUrl: "https://foundation.example/",
    http: {
      getJson: vi.fn(async () => ({ data: {} })),
      postJson: vi.fn(async () => ({ data: { id: "agt_01", status: "active" } }))
    }
  } as unknown as StudioEndpointContext;
}
