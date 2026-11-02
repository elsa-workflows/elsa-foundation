import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext, type StudioAgentPreparedInvocation, type StudioBackendSession } from "../../../sdk";
import { AgentApiError, type AgentClient } from "../../agent/agentClient";
import type { subscribeAgentSessionStream } from "../../agent/agentStream";
import type { AgentRecordedMessage, AgentRecordedProposal, AgentSessionDetailResponse, AgentSessionPolicy, AgentStreamEvent } from "../../agent/agentTypes";
import { WeaverSurface } from "../WeaverSurface";
import { weaverSessionScope } from "../weaverSessionHint";

// Preview (#3441 T020): reload, dock reopen, a second tab and a foreign session. Reopening reads recorded state only.
afterEach(() => { localStorage.clear(); document.body.innerHTML = ""; });

describe("Weaver session recovery", () => {
  it("reopens the remembered session after a reload with its history, acknowledged mode and pending review, without opening the stream", async () => {
    const server = fakeServer();
    const first = mount(server, "ada");
    await flush();
    typeAndSend(first.container, "Hide the status card");
    await flush();
    const sessionId = server.lastSessionId();
    expect(first.subscribe).toHaveBeenCalledTimes(1);
    first.unmount();
    // The server recorded a review that the reload interrupted.
    server.addProposal(sessionId, reviewProposal("prop_1", "awaiting-approval"));
    server.setPolicy(sessionId, policy("full-auto", 2));

    const reloaded = mount(server, "ada");
    await flush();
    expect(server.client.listSessions).toHaveBeenCalledWith({ workspace: "studio:primary", take: 20 });
    expect(server.client.getSession).toHaveBeenLastCalledWith(sessionId);
    expect(server.client.listProposals).toHaveBeenLastCalledWith(sessionId);
    expect(reloaded.subscribe).not.toHaveBeenCalled();
    expect(server.client.createSession).toHaveBeenCalledTimes(1);
    expect(reloaded.container.textContent).toContain("Hide the status card");
    const proposal = reloaded.container.querySelector(".agent-proposal");
    expect(proposal?.getAttribute("data-status")).toBe("awaiting-approval");
    expect(reloaded.container.querySelector(".weaver-auto-apply")?.getAttribute("data-autonomy-state")).toBe("acknowledged");
    expect(reloaded.container.querySelector<HTMLButtonElement>('[aria-label="Auto-apply mode"]')?.textContent).toBe("Autopilot");
    // The pending review blocks a new prompt until it is decided.
    expect(reloaded.container.querySelector<HTMLTextAreaElement>("textarea")?.disabled).toBe(true);
    reloaded.unmount();
  });

  it("reopens the same session when the dock closes and opens again on the same page", async () => {
    const server = fakeServer();
    const api = readyApi("ada");
    const first = mount(server, "ada", api);
    await flush();
    typeAndSend(first.container, "Explain this screen");
    await flush();
    first.emit({ type: "message-completed", messageId: "msg_1" });
    await flush();
    const sessionId = server.lastSessionId();
    first.unmount();
    const reopened = mount(server, "ada", api);
    await flush();
    expect(server.client.getSession).toHaveBeenLastCalledWith(sessionId);
    expect(reopened.subscribe).not.toHaveBeenCalled();
    expect(reopened.container.textContent).toContain("Explain this screen");
    expect(server.client.createSession).toHaveBeenCalledTimes(1);
    reopened.unmount();
  });

  it("shows each recorded outcome with only its explicit actions", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [
      message("m1", "user", "First", "cancelled", "interrupted"),
      message("m2", "assistant", "Partial", "failed"),
      message("m3", "user", "Second", "cancelled", "user-stop"),
      message("m4", "user", "Third", "cancelled", "withdrawn"),
      message("m5", "user", "Fourth", "cancelled"),
      message("m6", "user", "Not yet", "pending")
    ], [
      { ...clientProposal("prop_u", "uncertain"), title: "Uncertain move" },
      { ...clientProposal("prop_r", "uncertain"), title: "Resolved move", ownerResolution: { claim: "not-applied", actorId: "ada" } },
      { ...reviewProposal("prop_e", "expired"), title: "Expired change" },
      { ...reviewProposal("prop_f", "failed"), title: "Refused change" }
    ]);
    remember(sessionId);
    const view = mount(server, "ada");
    await flush();
    const text = view.container.textContent ?? "";
    expect(text).toContain("Interrupted. The turn stopped when its connection or the server ended. Nothing was sent again.");
    expect(text).toContain("Not completed. This partial answer was not accepted.");
    expect(text).toContain("Stopped by you.");
    expect(text).toContain("Withdrawn before it started.");
    expect(text).toContain("Cancelled. The turn's stream closed before it finished.");
    expect(text).toContain("Not started.");
    expect(messageItem(view.container, "m1")?.getAttribute("data-status-reason")).toBe("interrupted");
    expect(buttonsIn(messageItem(view.container, "m6"))).toEqual(["Start", "Withdraw"]);
    expect(buttonsIn(messageItem(view.container, "m1"))).toEqual([]);
    const uncertain = proposalByTitle(view.container, "Uncertain move");
    expect(uncertain?.getAttribute("data-status")).toBe("uncertain");
    expect(buttonsIn(uncertain?.querySelector('[aria-label="Resolve uncertain change"]'))).toEqual(["It was applied", "It was not applied", "I can't tell"]);
    const resolved = proposalByTitle(view.container, "Resolved move");
    expect(resolved?.textContent).toContain("You stated that this change was not applied. This is your statement, not a receipt");
    expect(resolved?.querySelector('[aria-label="Resolve uncertain change"]')).toBeNull();
    expect(proposalByTitle(view.container, "Expired change")?.getAttribute("data-status")).toBe("expired");
    expect(proposalByTitle(view.container, "Refused change")?.getAttribute("data-status")).toBe("failed");
    expect(view.subscribe).not.toHaveBeenCalled();
    expect(sessionId).toBe(server.lastSessionId());
    view.unmount();
  });

  it("starts a not-started prompt once through the stream only when asked, and never sends it again", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [message("m1", "user", "Not yet", "pending")]);
    remember(sessionId);
    const view = mount(server, "ada");
    await flush();
    expect(view.subscribe).not.toHaveBeenCalled();
    click(buttonIn(messageItem(view.container, "m1"), "Start"));
    expect(view.subscribe).toHaveBeenCalledTimes(1);
    expect(view.subscribe.mock.calls[0][1]).toBe(`/_elsa/agent/sessions/${sessionId}/stream`);
    view.emit({ type: "provider-notice", code: "agent.provider.github_copilot.conversation_reset", message: "GitHub Copilot could not resume this session's earlier conversation." });
    view.emit({ type: "message-delta", messageId: "m1:reply", content: "Done." });
    view.emit({ type: "message-completed", messageId: "m1:reply" });
    await flush();
    expect(server.client.sendMessage).not.toHaveBeenCalled();
    expect(view.container.querySelector('[data-notice="agent.provider.github_copilot.conversation_reset"]')?.textContent).toContain("could not resume");
    expect(view.container.textContent).toContain("Done.");
    expect(buttonsIn(messageItem(view.container, "m1"))).toEqual([]);
    view.unmount();
  });

  it("withdraws a not-started prompt, and rereads the session when it already started elsewhere", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [message("m1", "user", "First", "pending")]);
    remember(sessionId);
    const view = mount(server, "ada");
    await flush();
    click(buttonIn(messageItem(view.container, "m1"), "Withdraw"));
    await flush();
    expect(server.client.withdrawMessage).toHaveBeenCalledWith(sessionId, "m1");
    expect(messageItem(view.container, "m1")?.getAttribute("data-status-reason")).toBe("withdrawn");
    expect(view.subscribe).not.toHaveBeenCalled();
    view.unmount();

    const raced = fakeServer();
    const racedId = raced.seed("ada", [message("m1", "user", "First", "pending")]);
    remember(racedId);
    const second = mount(raced, "ada");
    await flush();
    // Another tab started it after this one read it.
    raced.setMessages(racedId, [message("m1", "user", "First", "completed"), message("m2", "assistant", "Answer", "completed")]);
    click(buttonIn(messageItem(second.container, "m1"), "Withdraw"));
    await flush();
    expect(second.container.textContent).toContain("This prompt already started, so it was not withdrawn.");
    expect(second.container.textContent).toContain("Answer");
    second.unmount();
  });

  it("records the owner's resolution of an uncertain client operation and keeps a conflicting one unrecorded", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [], [clientProposal("prop_u", "uncertain")]);
    remember(sessionId);
    const view = mount(server, "ada");
    await flush();
    click(buttonIn(view.container.querySelector('[aria-label="Resolve uncertain change"]'), "It was not applied"));
    await flush();
    expect(server.client.resolveClientInvocation).toHaveBeenCalledWith(sessionId, "inv_prop_u", "not-applied");
    expect(view.container.textContent).toContain("You stated that this change was not applied.");
    expect(server.client.acknowledgeClient).not.toHaveBeenCalled();
    view.unmount();

    const conflicted = fakeServer();
    const conflictedId = conflicted.seed("ada", [], [clientProposal("prop_u", "uncertain")]);
    remember(conflictedId);
    const second = mount(conflicted, "ada");
    await flush();
    conflicted.resolve(conflictedId, "prop_u", "applied");
    click(buttonIn(second.container.querySelector('[aria-label="Resolve uncertain change"]'), "It was not applied"));
    await flush();
    expect(second.container.textContent).toContain("A different statement about this operation is already recorded");
    expect(second.container.textContent).toContain("You stated that this change was applied.");
    second.unmount();
  });

  it("lets a second tab reopen the session without refusing or marking the first tab's client operation", async () => {
    const server = fakeServer();
    let finishDelivery!: () => void;
    const tabA = readyApi("ada");
    tabA.agent.toolContracts.add({ id: "sample_change", moduleId: "Sample", slotId: "sample", displayName: "Change", description: "Change", surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], contractVersion: "1", executionLocation: "clientWorkingState",
      executeClient: () => new Promise(resolve => { finishDelivery = () => resolve({ outcome: "applied", summary: "Moved." }); }) });
    const sessionId = server.seed("ada", [], [clientProposal("prop_c", "approved")]);
    remember(sessionId);
    const a = mount(server, "ada", tabA);
    await flush();
    server.prepareClientDelivery(sessionId, "prop_c", tabA.agent.clientId);
    click(buttonIn(proposalByTitle(a.container, "Move activity"), "Apply"));
    await flush();
    expect(server.proposal(sessionId, "prop_c").status).toBe("awaiting-client");

    const b = mount(server, "ada");
    await flush();
    const seenByB = proposalByTitle(b.container, "Move activity");
    expect(seenByB?.getAttribute("data-status")).toBe("awaiting-client");
    expect(seenByB?.textContent).toContain("Waiting for the Studio tab or page that started this change.");
    expect([...(seenByB?.querySelectorAll<HTMLButtonElement>("button") ?? [])].every(button => button.disabled)).toBe(true);
    expect(b.subscribe).not.toHaveBeenCalled();

    finishDelivery();
    await flush();
    expect(server.client.acknowledgeClient).toHaveBeenCalledTimes(1);
    expect(server.client.acknowledgeClient).toHaveBeenCalledWith(sessionId, "inv_prop_c", expect.objectContaining({ outcome: "applied", client: { clientId: tabA.agent.clientId, generationId: "generation" } }));
    expect(server.client.resolveClientInvocation).not.toHaveBeenCalled();
    expect(server.proposal(sessionId, "prop_c").status).toBe("executed");
    a.unmount(); b.unmount();
  });

  it.each(["reloaded page", "closed editor"])("refuses an approved client change after recovery with zero effect when its unsaved target is gone (%s)", async cause => {
    const server = fakeServer();
    const api = readyApi("ada");
    const execute = vi.fn(async () => ({ outcome: "applied" as const, summary: "Moved." }));
    api.agent.toolContracts.add({ id: "sample_change", moduleId: "Sample", slotId: "sample", displayName: "Change", description: "Change", surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], contractVersion: "1", executionLocation: "clientWorkingState",
      executeClient: execute, clientTargetAvailability: () => ({ status: "unavailable", reason: "The editor that held this unsaved draft was closed." }) });
    const sessionId = server.seed("ada", [], [clientProposal("prop_c", "approved")]);
    // A reload gives the page a new client identity; a closed editor keeps the identity but loses its unsaved target.
    if (cause === "closed editor") server.prepareClientDelivery(sessionId, "prop_c", api.agent.clientId);
    remember(sessionId);
    const view = mount(server, "ada", api);
    await flush();
    click(buttonIn(proposalByTitle(view.container, "Move activity"), "Apply"));
    await flush();
    expect(execute).not.toHaveBeenCalled();
    expect(server.client.acknowledgeClient).toHaveBeenCalledWith(sessionId, "inv_prop_c", expect.objectContaining({
      outcome: "refused",
      summary: cause === "closed editor" ? "The editor that held this unsaved draft was closed." : "The originating Studio context is unavailable."
    }));
    expect(server.proposal(sessionId, "prop_c").status).toBe("failed");
    expect(proposalByTitle(view.container, "Move activity")?.getAttribute("data-status")).toBe("failed");
    view.unmount();
  });

  it("never lists or opens another user's session and shows the shared not-found outcome", async () => {
    const server = fakeServer();
    const foreign = server.seed("grace", [message("g1", "user", "Grace's secret prompt", "completed")]);
    const view = mount(server, "ada");
    await flush();
    expect(view.container.querySelector('[aria-label="Recent conversations"]')).toBeNull();
    expect(server.client.getSession).not.toHaveBeenCalled();
    // A hint can never select another owner's session; the server's shared outcome is shown with nothing disclosed.
    localStorage.setItem(weaverSessionScope(readyApi("ada"))!.storageKey, foreign);
    view.unmount();
    const reopened = mount(server, "ada");
    await flush();
    expect(server.client.getSession).not.toHaveBeenCalled();
    expect(localStorage.getItem(weaverSessionScope(readyApi("ada"))!.storageKey)).toBeNull();
    reopened.unmount();

    const unlisted = fakeServer({ listUnsupported: true });
    const other = unlisted.seed("grace", [message("g1", "user", "Grace's secret prompt", "completed")]);
    localStorage.setItem(weaverSessionScope(readyApi("ada"))!.storageKey, other);
    const fallback = mount(unlisted, "ada");
    await flush();
    expect(unlisted.client.getSession).toHaveBeenCalledWith(other);
    expect(fallback.container.textContent).not.toContain("Grace's secret prompt");
    expect(localStorage.getItem(weaverSessionScope(readyApi("ada"))!.storageKey)).toBeNull();
    fallback.unmount();
  });

  it("reopens a listed session on request and explains a foreign one as not available", async () => {
    const server = fakeServer();
    server.seed("ada", [message("m1", "user", "Earlier question", "completed")]);
    const view = mount(server, "ada");
    await flush();
    // No remembered session: the list is offered, nothing is opened.
    expect(server.client.getSession).not.toHaveBeenCalled();
    const offered = view.container.querySelector('[aria-label="Recent conversations"] button');
    expect(offered?.textContent).toContain("Studio assistant");
    click(offered);
    await flush();
    expect(view.container.textContent).toContain("Earlier question");
    expect(view.subscribe).not.toHaveBeenCalled();
    view.unmount();

    const other = fakeServer();
    const foreign = other.seed("grace", [message("g1", "user", "Grace's secret prompt", "completed")]);
    // Even a list entry naming another owner's session (a stale or forged entry) opens nothing of it.
    other.client.listSessions.mockResolvedValueOnce({ items: [{ id: foreign, status: "active", updatedAt: "2026-11-02T10:00:00Z", pendingReviewCount: 0, title: "Requested" }] });
    const probe = mount(other, "ada");
    await flush();
    click(probe.container.querySelector(`[data-session-id="${foreign}"]`));
    await flush();
    expect(other.client.getSession).toHaveBeenCalledWith(foreign);
    expect(probe.container.textContent).toContain("This conversation is not available.");
    expect(probe.container.textContent).not.toContain("Grace's secret prompt");
    probe.unmount();
  });

  it("keeps the session when another server process owns it and retries only on request", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [message("m1", "user", "Earlier question", "completed")]);
    localStorage.setItem(weaverSessionScope(readyApi("ada"))!.storageKey, sessionId);
    server.client.getSession.mockRejectedValueOnce(new AgentApiError(409, "Another Studio host owns this session.", "agent.session.owned_elsewhere", null, 12));
    const view = mount(server, "ada");
    await flush();
    expect(view.container.querySelector('[data-kind="owned-elsewhere"]')?.textContent).toContain("Try again in about 12 seconds.");
    expect(localStorage.getItem(weaverSessionScope(readyApi("ada"))!.storageKey)).toBe(sessionId);
    expect(server.client.getSession).toHaveBeenCalledTimes(1);
    click(buttonIn(view.container.querySelector('[data-kind="owned-elsewhere"]'), "Try again"));
    await flush();
    expect(server.client.getSession).toHaveBeenCalledTimes(2);
    expect(view.container.textContent).toContain("Earlier question");
    expect(view.subscribe).not.toHaveBeenCalled();
    view.unmount();
  });

  it("scopes the remembered session to the user and backend, and forgets it on stop and new conversation", async () => {
    const ada = weaverSessionScope(readyApi("ada"))!;
    expect(weaverSessionScope(readyApi("grace"))!.storageKey).not.toBe(ada.storageKey);
    expect(weaverSessionScope(readyApi("ada", "secondary"))!.storageKey).not.toBe(ada.storageKey);
    expect(weaverSessionScope(createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://studio.example/") }))).toBeNull();

    const server = fakeServer();
    const sessionId = server.seed("ada", [message("m1", "user", "Earlier question", "completed")]);
    localStorage.setItem(ada.storageKey, sessionId);
    const view = mount(server, "ada");
    await flush();
    expect(view.container.textContent).toContain("Earlier question");
    click(buttonIn(view.container, "New conversation"));
    await flush();
    expect(localStorage.getItem(ada.storageKey)).toBeNull();
    expect(view.container.textContent).not.toContain("Earlier question");
    expect(server.calls()).toEqual([]);
    view.unmount();
  });

  it("keeps working when browser storage throws", async () => {
    const server = fakeServer();
    const getItem = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("denied"); });
    const setItem = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("denied"); });
    try {
      const view = mount(server, "ada");
      await flush();
      typeAndSend(view.container, "Still works");
      await flush();
      expect(server.client.sendMessage).toHaveBeenCalledTimes(1);
      view.unmount();
    } finally { getItem.mockRestore(); setItem.mockRestore(); }
  });
});

type Recorded = { owner: string; detail: AgentSessionDetailResponse; proposals: AgentRecordedProposal[] };

function fakeServer(options: { listUnsupported?: boolean } = {}) {
  const sessions = new Map<string, Recorded>();
  const writes: string[] = [];
  let next = 0;
  const notFound = () => new AgentApiError(404, "Agent session was not found.", "agent.session.not_found", null);
  const owned = (id: string) => { const recorded = sessions.get(id); if (!recorded || recorded.owner !== "ada") throw notFound(); return recorded; };
  const seed = (owner: string, messages: AgentRecordedMessage[] = [], proposals: AgentRecordedProposal[] = []) => {
    const id = `agt_${++next}`;
    sessions.set(id, { owner, detail: { sessionId: id, status: "active", title: "Studio assistant", policy: policy("manual", 1), messages }, proposals });
    return id;
  };
  const client = {
    bootstrap: vi.fn<AgentClient["bootstrap"]>(async () => ({
      enabled: true, providerStatus: "available", modes: ["troubleshoot"],
      capabilities: [{ id: "weaver.test", displayName: "Weaver test", description: "", kind: "answer", risk: "read-only", surfaces: ["*"], requiredPermissions: [] }],
      provider: { providerId: "deterministic", isAvailable: true, status: "ok", providerKind: "provider-sdk-binding", supportedOperations: ["chat"], riskProfile: "read-only", metadata: {} },
      policy: { contextVisibility: true, defaultAutonomyMode: "manual", maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"] }
    })),
    createSession: vi.fn<AgentClient["createSession"]>(async request => {
      const id = seed("ada"); writes.push("create");
      expect(request.workspace).toBe("studio:primary");
      return { sessionId: id, status: "active", contextAttachments: [], policy: policy("manual", 1) };
    }),
    getSession: vi.fn<AgentClient["getSession"]>(async id => structuredClone(owned(id).detail)),
    updateAutonomy: vi.fn<AgentClient["updateAutonomy"]>(),
    sendMessage: vi.fn<AgentClient["sendMessage"]>(async (id, request) => {
      owned(id).detail.messages.push(message(`msg_${owned(id).detail.messages.length + 1}`, "user", request.message, "pending")); writes.push("send");
      return { messageId: "msg_1", status: "pending", streamUrl: `/_elsa/agent/sessions/${id}/stream` };
    }),
    approveProposal: vi.fn<AgentClient["approveProposal"]>(async proposalId => ({ proposalId, approvalStatus: "approved" })),
    denyProposal: vi.fn<AgentClient["denyProposal"]>(async proposalId => ({ proposalId, approvalStatus: "denied" })),
    executeProposal: vi.fn<AgentClient["executeProposal"]>(async proposalId => {
      const [id, recorded] = [...sessions].find(([, item]) => item.proposals.some(p => p.id === proposalId))!;
      const proposal = recorded.proposals.find(p => p.id === proposalId)!;
      proposal.status = "awaiting-client"; writes.push(`execute:${id}`);
      return { proposalId, approvalStatus: "awaiting-client", clientInvocation: proposal.preparedInvocation };
    }),
    acknowledgeClient: vi.fn<AgentClient["acknowledgeClient"]>(async (id, invocationId, acknowledgement) => {
      const proposal = owned(id).proposals.find(p => p.preparedInvocation?.invocationId === invocationId)!;
      proposal.status = acknowledgement.outcome === "applied" ? "executed" : acknowledgement.outcome === "refused" ? "failed" : "uncertain";
      return { proposalId: proposal.id, approvalStatus: proposal.status };
    }),
    submitFeedback: vi.fn<AgentClient["submitFeedback"]>(async () => {}),
    cancelTurn: vi.fn<AgentClient["cancelTurn"]>(async () => {}),
    listSessions: vi.fn<AgentClient["listSessions"]>(async () => {
      if (options.listUnsupported) throw new AgentApiError(409, "Unsupported.", "agent.session.query_unsupported", null);
      return { items: [...sessions].filter(([, item]) => item.owner === "ada").reverse().map(([id, item]) => ({ id, title: item.detail.title, status: item.detail.status, updatedAt: "2026-11-02T10:00:00Z", pendingReviewCount: item.proposals.filter(p => p.status === "awaiting-approval").length })) };
    }),
    listProposals: vi.fn<AgentClient["listProposals"]>(async id => structuredClone(owned(id).proposals)),
    withdrawMessage: vi.fn<AgentClient["withdrawMessage"]>(async (id, messageId) => {
      const recorded = owned(id).detail.messages.find(item => item.id === messageId);
      if (!recorded) throw new AgentApiError(404, "Not found.", "agent.message.not_found", null);
      if (recorded.status !== "pending") throw new AgentApiError(409, "Not pending.", "agent.message.not_pending", null);
      Object.assign(recorded, { status: "cancelled", statusReason: "withdrawn" }); writes.push("withdraw");
      return structuredClone(recorded);
    }),
    resolveClientInvocation: vi.fn<AgentClient["resolveClientInvocation"]>(async (id, invocationId, claim) => {
      const proposal = owned(id).proposals.find(p => p.preparedInvocation?.invocationId === invocationId);
      if (!proposal) throw new AgentApiError(404, "Not found.", "agent.invocation.not_found", null);
      if (proposal.status !== "uncertain") throw new AgentApiError(409, "Not uncertain.", "agent.invocation.not_uncertain", null);
      if (proposal.ownerResolution && proposal.ownerResolution.claim !== claim) throw new AgentApiError(409, "Conflict.", "agent.invocation.resolution_conflict", null);
      proposal.ownerResolution = { claim, actorId: "ada" }; writes.push("resolve");
      return structuredClone(proposal);
    })
  };
  return {
    client,
    seed,
    calls: () => [...writes],
    lastSessionId: () => `agt_${next}`,
    proposal: (id: string, proposalId: string) => sessions.get(id)!.proposals.find(p => p.id === proposalId)!,
    addProposal: (id: string, proposal: AgentRecordedProposal) => sessions.get(id)!.proposals.push(proposal),
    setPolicy: (id: string, value: AgentSessionPolicy) => { sessions.get(id)!.detail.policy = value; },
    setMessages: (id: string, messages: AgentRecordedMessage[]) => { sessions.get(id)!.detail.messages = messages; },
    resolve: (id: string, proposalId: string, claim: "applied") => { sessions.get(id)!.proposals.find(p => p.id === proposalId)!.ownerResolution = { claim, actorId: "ada" }; },
    prepareClientDelivery: (id: string, proposalId: string, clientId: string) => {
      const proposal = sessions.get(id)!.proposals.find(p => p.id === proposalId)!;
      proposal.preparedInvocation = { ...proposal.preparedInvocation!, client: { clientId, generationId: "generation" } };
    }
  };
}

function policy(autonomyMode: AgentSessionPolicy["autonomyMode"], revision: number): AgentSessionPolicy {
  return { autonomyMode, maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"], revision };
}

function message(id: string, role: string, content: string, status: string, statusReason?: AgentRecordedMessage["statusReason"]): AgentRecordedMessage {
  return { id, role, content, status, ...(statusReason ? { statusReason } : {}) };
}

function reviewProposal(id: string, status: AgentRecordedProposal["status"]): AgentRecordedProposal {
  return { id, status, title: "Change sample", summary: "Change the sample.", risk: "review-required", baseRevision: "r1", resourceTarget: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" } };
}

function clientProposal(id: string, status: AgentRecordedProposal["status"]): AgentRecordedProposal {
  const prepared: StudioAgentPreparedInvocation = { invocationId: `inv_${id}`, sessionId: "agt_1", turnId: "turn", toolCallId: "call", toolName: "sample_change", toolContractVersion: "1", moduleId: "Sample", actorId: "ada", tenantId: "tenant", target: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" }, arguments: { value: 2 }, approvalDigest: "digest", risk: "reviewRequired", preview: { typeId: "sample.preview", schemaVersion: "1", data: { value: 2 } }, executionLocation: "clientWorkingState", requiresApproval: true, client: { clientId: "original-tab", generationId: "generation" }, expiresAt: new Date(Date.now() + 60000).toISOString() };
  return { id, status, title: "Move activity", summary: "Move the activity.", risk: "review-required", baseRevision: "r1", preparedInvocation: prepared, resourceTarget: prepared.target! };
}

function readyApi(subject: string, profile = "primary") {
  const backendSession = {
    key: `${profile}-${Math.random()}`,
    profile: { id: profile, name: profile, baseUrl: `https://${profile}.example/`, environment: "Development", source: "configuration" },
    context: createEndpointContext(`https://${profile}.example/`),
    identity: { status: "authenticated", subject, tenantId: "tenant", roles: [], permissions: [] },
    capabilities: null,
    pendingWrites: 0,
    disposed: false,
    refreshCapabilities: async () => { throw new Error("unused"); }
  } as unknown as StudioBackendSession;
  const api = createStudioRegistry({ hostVersion: "1.0.0", sdkVersion: "1.0.0", ...createEndpointContext("https://studio.example/") }, { backendBaseUrl: `https://${profile}.example/`, backendSession });
  api.agent.capabilities.add({ id: "weaver.test", displayName: "Weaver test", description: "Test capability.", kind: "answer", risk: "read-only", surfaces: ["*"] });
  return api;
}

function mount(server: ReturnType<typeof fakeServer>, subject: string, api = readyApi(subject)) {
  const streams: ((event: AgentStreamEvent) => void)[] = [];
  const subscribe = vi.fn(((_context, _url, emit) => { streams.push(emit); return { close() {} }; }) as typeof subscribeAgentSessionStream);
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={server.client as AgentClient} subscribeStream={subscribe} />));
  return {
    container,
    subscribe,
    emit: (event: AgentStreamEvent) => flushSync(() => streams[streams.length - 1](event)),
    unmount: () => { flushSync(() => root.unmount()); container.remove(); }
  };
}

function typeAndSend(container: HTMLElement, value: string) {
  const textarea = container.querySelector<HTMLTextAreaElement>("textarea")!;
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!;
    setter.call(textarea, value);
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
  });
  flushSync(() => container.querySelector<HTMLButtonElement>('[aria-label="Send message"]')!.click());
}

function click(element: Element | null | undefined) {
  if (!element) throw new Error("Missing element.");
  flushSync(() => (element as HTMLElement).click());
}

function remember(sessionId: string) {
  localStorage.setItem(weaverSessionScope(readyApi("ada"))!.storageKey, sessionId);
}

function messageItem(container: HTMLElement, id: string) {
  return container.querySelector<HTMLElement>(`.weaver-message[data-message-id="${id}"]`);
}

function proposalByTitle(container: HTMLElement, title: string) {
  return [...container.querySelectorAll<HTMLElement>(".agent-proposal")].find(item => item.querySelector("strong")?.textContent === title) ?? null;
}

function buttonIn(container: Element | null | undefined, text: string) {
  return [...(container?.querySelectorAll<HTMLButtonElement>("button") ?? [])].find(button => button.textContent?.trim() === text) ?? null;
}

function buttonsIn(container: Element | null | undefined) {
  return [...(container?.querySelectorAll<HTMLButtonElement>("button") ?? [])].map(button => button.textContent?.trim());
}

async function flush() {
  for (let i = 0; i < 8; i++) await new Promise(resolve => setTimeout(resolve, 0));
}
