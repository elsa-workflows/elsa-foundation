import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext, type StudioAgentPreparedInvocation, type StudioBackendSession, type StudioEndpointContext } from "../../../sdk";
import { AgentApiError, createAgentClient, type AgentClient } from "../../agent/agentClient";
import { subscribeAgentSessionStream } from "../../agent/agentStream";
import type { AgentRecordedMessage, AgentRecordedProposal, AgentSessionDetailResponse, AgentSessionPolicy, AgentStreamEvent } from "../../agent/agentTypes";
import { WeaverSurface } from "../WeaverSurface";
import { weaverSessionScope } from "../weaverSessionHint";

// Preview (#3441 T020): reload, dock reopen, a second tab and a foreign session. Reopening reads recorded state only.
afterEach(() => { localStorage.clear(); document.body.innerHTML = ""; });

describe("Weaver session recovery", () => {
  it("reopens the remembered session after a reload with its history, acknowledged mode and pending review, without opening the stream", async () => {
    const server = fakeServer();
    const first = mount(server, "ada");
    await composerReady(first.container);
    typeAndSend(first.container, "Hide the status card");
    await settle(() => expect(first.subscribe).toHaveBeenCalledTimes(1));
    const sessionId = server.lastSessionId();
    first.unmount();
    // The server recorded a review that the reload interrupted.
    server.addProposal(sessionId, reviewProposal("prop_1", "awaiting-approval"));
    server.setPolicy(sessionId, policy("full-auto", 2));

    const reloaded = mount(server, "ada");
    await settle(() => expect(reloaded.container.querySelector(".agent-proposal")?.getAttribute("data-status")).toBe("awaiting-approval"));
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
    await composerReady(first.container);
    typeAndSend(first.container, "Explain this screen");
    await settle(() => expect(first.subscribe).toHaveBeenCalledTimes(1));
    first.emit({ type: "message-completed", messageId: "msg_1" });
    const sessionId = server.lastSessionId();
    first.unmount();
    const reopened = mount(server, "ada", api);
    await settle(() => expect(reopened.container.textContent).toContain("Explain this screen"));
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
    await settle(() => expect(proposalByTitle(view.container, "Refused change")).not.toBeNull());
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
    await settle(() => expect(buttonIn(messageItem(view.container, "m1"), "Start")?.disabled).toBe(false));
    expect(view.subscribe).not.toHaveBeenCalled();
    click(buttonIn(messageItem(view.container, "m1"), "Start"));
    expect(view.subscribe).toHaveBeenCalledTimes(1);
    expect(view.subscribe.mock.calls[0][1]).toBe(`/_elsa/weaver/sessions/${sessionId}/stream`);
    view.emit({ type: "provider-notice", code: "weaver.provider.github_copilot.conversation_reset", message: "GitHub Copilot could not resume this session's earlier conversation." });
    view.emit({ type: "message-delta", messageId: "m1:reply", content: "Done." });
    view.emit({ type: "message-completed", messageId: "m1:reply" });
    await settle(() => expect(view.container.textContent).toContain("Done."));
    expect(server.client.sendMessage).not.toHaveBeenCalled();
    expect(view.container.querySelector('[data-notice="weaver.provider.github_copilot.conversation_reset"]')?.textContent).toContain("could not resume");
    expect(view.container.textContent).toContain("Done.");
    expect(buttonsIn(messageItem(view.container, "m1"))).toEqual([]);
    view.unmount();
  });

  it("withdraws a not-started prompt, and rereads the session when it already started elsewhere", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [message("m1", "user", "First", "pending")]);
    remember(sessionId);
    const view = mount(server, "ada");
    await settle(() => expect(buttonIn(messageItem(view.container, "m1"), "Withdraw")?.disabled).toBe(false));
    click(buttonIn(messageItem(view.container, "m1"), "Withdraw"));
    await settle(() => expect(messageItem(view.container, "m1")?.getAttribute("data-status-reason")).toBe("withdrawn"));
    expect(server.client.withdrawMessage).toHaveBeenCalledWith(sessionId, "m1");
    expect(view.subscribe).not.toHaveBeenCalled();
    view.unmount();

    const raced = fakeServer();
    const racedId = raced.seed("ada", [message("m1", "user", "First", "pending")]);
    remember(racedId);
    const second = mount(raced, "ada");
    await settle(() => expect(buttonIn(messageItem(second.container, "m1"), "Withdraw")?.disabled).toBe(false));
    // Another tab started it after this one read it.
    raced.setMessages(racedId, [message("m1", "user", "First", "completed"), message("m2", "assistant", "Answer", "completed")]);
    click(buttonIn(messageItem(second.container, "m1"), "Withdraw"));
    await settle(() => expect(second.container.textContent).toContain("This prompt already started, so it was not withdrawn."));
    expect(second.container.textContent).toContain("Answer");
    second.unmount();
  });

  it("records the owner's resolution of an uncertain client operation and keeps a conflicting one unrecorded", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [], [clientProposal("prop_u", "uncertain")]);
    remember(sessionId);
    const view = mount(server, "ada");
    await settle(() => expect(resolveGroup(view.container)).not.toBeNull());
    click(buttonIn(resolveGroup(view.container), "It was not applied"));
    await settle(() => expect(view.container.textContent).toContain("You stated that this change was not applied."));
    expect(server.client.resolveClientInvocation).toHaveBeenCalledWith(sessionId, "inv_prop_u", "not-applied");
    expect(server.client.acknowledgeClient).not.toHaveBeenCalled();
    view.unmount();

    const conflicted = fakeServer();
    const conflictedId = conflicted.seed("ada", [], [clientProposal("prop_u", "uncertain")]);
    remember(conflictedId);
    const second = mount(conflicted, "ada");
    await settle(() => expect(resolveGroup(second.container)).not.toBeNull());
    conflicted.resolve(conflictedId, "prop_u", "applied");
    click(buttonIn(resolveGroup(second.container), "It was not applied"));
    await settle(() => expect(second.container.textContent).toContain("A different statement about this operation is already recorded"));
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
    await settle(() => expect(buttonIn(proposalByTitle(a.container, "Move activity"), "Apply")?.disabled).toBe(false));
    server.prepareClientDelivery(sessionId, "prop_c", tabA.agent.clientId);
    click(buttonIn(proposalByTitle(a.container, "Move activity"), "Apply"));
    await settle(() => expect(server.proposal(sessionId, "prop_c").status).toBe("awaiting-client"));

    const b = mount(server, "ada");
    await settle(() => expect(proposalByTitle(b.container, "Move activity")?.getAttribute("data-status")).toBe("awaiting-client"));
    const seenByB = proposalByTitle(b.container, "Move activity");
    expect(seenByB?.getAttribute("data-status")).toBe("awaiting-client");
    expect(seenByB?.textContent).toContain("Waiting for the Studio tab or page that started this change.");
    expect([...(seenByB?.querySelectorAll<HTMLButtonElement>("button") ?? [])].every(button => button.disabled)).toBe(true);
    expect(b.subscribe).not.toHaveBeenCalled();

    finishDelivery();
    await settle(() => expect(server.proposal(sessionId, "prop_c").status).toBe("executed"));
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
    await settle(() => expect(buttonIn(proposalByTitle(view.container, "Move activity"), "Apply")?.disabled).toBe(false));
    click(buttonIn(proposalByTitle(view.container, "Move activity"), "Apply"));
    await settle(() => expect(proposalByTitle(view.container, "Move activity")?.getAttribute("data-status")).toBe("failed"));
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
    await discovered(server);
    expect(view.container.querySelector('[aria-label="Recent conversations"]')).toBeNull();
    expect(server.client.getSession).not.toHaveBeenCalled();
    // A hint can never select another owner's session; the server's shared outcome is shown with nothing disclosed.
    localStorage.setItem(weaverSessionScope(readyApi("ada"))!.storageKey, foreign);
    view.unmount();
    const reopened = mount(server, "ada");
    await settle(() => expect(localStorage.getItem(weaverSessionScope(readyApi("ada"))!.storageKey)).toBeNull());
    expect(server.client.getSession).not.toHaveBeenCalled();
    reopened.unmount();

    const unlisted = fakeServer({ listUnsupported: true });
    const other = unlisted.seed("grace", [message("g1", "user", "Grace's secret prompt", "completed")]);
    localStorage.setItem(weaverSessionScope(readyApi("ada"))!.storageKey, other);
    const fallback = mount(unlisted, "ada");
    await settle(() => expect(localStorage.getItem(weaverSessionScope(readyApi("ada"))!.storageKey)).toBeNull());
    expect(unlisted.client.getSession).toHaveBeenCalledWith(other);
    expect(fallback.container.textContent).not.toContain("Grace's secret prompt");
    fallback.unmount();
  });

  it("reopens a listed session on request and explains a foreign one as not available", async () => {
    const server = fakeServer();
    server.seed("ada", [message("m1", "user", "Earlier question", "completed")]);
    const view = mount(server, "ada");
    await settle(() => expect(view.container.querySelector('[aria-label="Recent conversations"] button')).not.toBeNull());
    // No remembered session: the list is offered, nothing is opened.
    expect(server.client.getSession).not.toHaveBeenCalled();
    const offered = view.container.querySelector('[aria-label="Recent conversations"] button');
    expect(offered?.textContent).toContain("Studio assistant");
    click(offered);
    await settle(() => expect(view.container.textContent).toContain("Earlier question"));
    expect(view.subscribe).not.toHaveBeenCalled();
    view.unmount();

    const other = fakeServer();
    const foreign = other.seed("grace", [message("g1", "user", "Grace's secret prompt", "completed")]);
    // Even a list entry naming another owner's session (a stale or forged entry) opens nothing of it.
    other.client.listSessions.mockResolvedValueOnce({ items: [{ id: foreign, status: "active", updatedAt: "2026-11-02T10:00:00Z", pendingReviewCount: 0, title: "Requested" }] });
    const probe = mount(other, "ada");
    await settle(() => expect(probe.container.querySelector(`[data-session-id="${foreign}"]`)).not.toBeNull());
    click(probe.container.querySelector(`[data-session-id="${foreign}"]`));
    await settle(() => expect(probe.container.textContent).toContain("This conversation is not available."));
    expect(other.client.getSession).toHaveBeenCalledWith(foreign);
    expect(probe.container.textContent).not.toContain("Grace's secret prompt");
    probe.unmount();
  });

  it("keeps the session when another server process owns it and retries only on request", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [message("m1", "user", "Earlier question", "completed")]);
    localStorage.setItem(weaverSessionScope(readyApi("ada"))!.storageKey, sessionId);
    server.client.getSession.mockRejectedValueOnce(new AgentApiError(409, "Another Studio host owns this session.", "weaver.session.owned_elsewhere", null, 12));
    const view = mount(server, "ada");
    await settle(() => expect(view.container.querySelector('[data-kind="owned-elsewhere"]')?.textContent).toContain("Try again in about 12 seconds."));
    expect(localStorage.getItem(weaverSessionScope(readyApi("ada"))!.storageKey)).toBe(sessionId);
    expect(server.client.getSession).toHaveBeenCalledTimes(1);
    click(buttonIn(view.container.querySelector('[data-kind="owned-elsewhere"]'), "Try again"));
    await settle(() => expect(view.container.textContent).toContain("Earlier question"));
    expect(server.client.getSession).toHaveBeenCalledTimes(2);
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
    await settle(() => expect(view.container.textContent).toContain("Earlier question"));
    click(buttonIn(view.container, "New conversation"));
    await settle(() => expect(view.container.querySelector('[aria-label="Recent conversations"]')).not.toBeNull());
    expect(localStorage.getItem(ada.storageKey)).toBeNull();
    expect(view.container.textContent).not.toContain("Earlier question");
    expect(server.calls()).toEqual([]);
    view.unmount();
  });

  it.each([["snapshots", "64 distinct disclosure snapshots"], ["bytes", "8 MiB of recorded disclosure"]] as const)("shows the %s session limit in the server's words and continues in a new session that carries over only a link", async (limit, bound) => {
    const server = fakeServer();
    const view = mount(server, "ada");
    await composerReady(view.container);
    typeAndSend(view.container, "Keep going");
    await settle(() => expect(view.subscribe).toHaveBeenCalledTimes(1));
    const limited = server.lastSessionId();
    const message = `Session limit reached: this conversation has recorded ${bound}. Continue in a new session; nothing recorded here was discarded.`;
    view.emit({ type: "error", code: "weaver.context.disclosure_budget_exceeded", limit, message });
    const notice = view.container.querySelector('[data-kind="limit"]');
    // The bound comes from the server; Studio adds only what the continuation carries over.
    expect(notice?.getAttribute("data-limit")).toBe(limit);
    expect(notice?.textContent).toBe(`${message} It stays readable and nothing was discarded; continue in a new session, which carries over only a link to this one.Continue in a new session`);
    expect(server.client.createSession).toHaveBeenCalledTimes(1);
    click(buttonIn(notice, "Continue in a new session"));
    await settle(() => expect(view.container.textContent).toContain("Only a link to it was carried over"));
    expect(server.client.createSession).toHaveBeenCalledTimes(2);
    const request = server.client.createSession.mock.calls[1][0];
    expect(request).toMatchObject({ continuedFrom: limited, workspace: "studio:primary" });
    expect(Object.keys(request).sort()).toEqual(["activeSurface", "autonomyMode", "clientContext", "continuedFrom", "mode", "workspace"]);
    const continued = server.lastSessionId();
    expect(continued).not.toBe(limited);
    expect(view.container.textContent).not.toContain("Keep going");
    expect(view.container.textContent).toContain("Only a link to it was carried over");
    expect(localStorage.getItem(weaverSessionScope(readyApi("ada"))!.storageKey)).toBe(continued);
    expect(server.client.sendMessage).toHaveBeenCalledTimes(1);
    expect(view.container.querySelector('[data-kind="limit"]')).toBeNull();
    view.unmount();
  });

  it("treats a refusal that names no session limit as an ordinary error, without offering a continuation", async () => {
    const server = fakeServer();
    const view = mount(server, "ada");
    await composerReady(view.container);
    typeAndSend(view.container, "Attach the whole export");
    await settle(() => expect(view.subscribe).toHaveBeenCalledTimes(1));
    // A single disclosure larger than any session can record: the stream parser keeps only the session limits.
    view.emit({ type: "error", code: "weaver.context.disclosure_budget_exceeded", message: "This context is larger than one session can record." });
    await settle(() => expect(view.container.textContent).toContain("This context is larger than one session can record."));
    expect(view.container.querySelector('[data-kind="limit"]')).toBeNull();
    expect(buttonIn(view.container, "Continue in a new session")).toBeNull();
    view.unmount();
  });

  it("keeps working when browser storage throws", async () => {
    const server = fakeServer();
    const getItem = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("denied"); });
    const setItem = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("denied"); });
    try {
      const view = mount(server, "ada");
      await composerReady(view.container);
      typeAndSend(view.container, "Still works");
      await settle(() => expect(server.client.sendMessage).toHaveBeenCalledTimes(1));
      view.unmount();
    } finally { getItem.mockRestore(); setItem.mockRestore(); }
  });
});

// #3440 autonomy rules hold across a recovery reread: a pending change is never dropped, and a mode label belongs to its session.
describe("Weaver recovery with session autonomy", () => {
  it("keeps a pending auto-apply change across a same-session reread and blocks prompts until it is acknowledged", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [], [clientProposal("prop_u", "uncertain")]);
    remember(sessionId);
    const view = mount(server, "ada");
    await settle(() => expect(resolveGroup(view.container)).not.toBeNull());
    // The statement is sent first; the mode change is chosen while it is still in flight.
    let refuseResolution!: (error: unknown) => void;
    server.client.resolveClientInvocation.mockImplementationOnce(() => new Promise((_, reject) => { refuseResolution = reject; }));
    click(buttonIn(resolveGroup(view.container), "It was not applied"));
    let acknowledge!: (value: AgentSessionPolicy) => void;
    server.client.updateAutonomy.mockImplementationOnce(() => new Promise(resolve => { acknowledge = resolve; }));
    chooseMode(view.container, "Autopilot");
    expect(autonomyState(view.container)).toBe("pending");
    expect(server.client.updateAutonomy).toHaveBeenCalledWith(sessionId, { autonomyMode: "full-auto", expectedRevision: 1 });

    // The statement is refused, so the session is reread; the reread still shows revision 1.
    refuseResolution(new AgentApiError(409, "Not uncertain.", "weaver.invocation.not_uncertain", null));
    await settle(() => expect(view.container.querySelector('[data-kind="action-refused"]')).not.toBeNull());
    expect(server.client.getSession).toHaveBeenCalledTimes(2);
    expect(autonomyState(view.container)).toBe("pending");
    expect(modeButton(view.container)?.textContent).toBe("Manual review");
    expect(view.container.querySelector<HTMLTextAreaElement>("textarea")?.disabled).toBe(true);

    // The change's own response still settles it.
    server.setPolicy(sessionId, policy("full-auto", 2));
    acknowledge(policy("full-auto", 2));
    await settle(() => expect(autonomyState(view.container)).toBe("acknowledged"));
    expect(modeButton(view.container)?.textContent).toBe("Autopilot");
    expect(view.container.querySelector<HTMLTextAreaElement>("textarea")?.disabled).toBe(false);
    view.unmount();
  });

  it("holds resolve, withdraw and Try again while an auto-apply change is unconfirmed", async () => {
    const server = fakeServer();
    const sessionId = server.seed("ada", [message("m1", "user", "Not yet", "pending")], [clientProposal("prop_u", "uncertain")]);
    remember(sessionId);
    const view = mount(server, "ada");
    await settle(() => expect(resolveGroup(view.container)).not.toBeNull());
    // A refused statement rereads the session; that reread finds the session busy elsewhere and offers Try again.
    server.client.resolveClientInvocation.mockRejectedValueOnce(new AgentApiError(409, "Not uncertain.", "weaver.invocation.not_uncertain", null));
    server.client.getSession.mockRejectedValueOnce(new AgentApiError(409, "Another Studio host owns this session.", "weaver.session.owned_elsewhere", null, 5));
    click(buttonIn(resolveGroup(view.container), "It was not applied"));
    await settle(() => expect(buttonIn(view.container.querySelector('[data-kind="owned-elsewhere"]'), "Try again")?.disabled).toBe(false));
    expect(buttonIn(messageItem(view.container, "m1"), "Withdraw")?.disabled).toBe(false);
    expect(buttonsIn(resolveGroup(view.container)).length).toBe(3);

    server.client.updateAutonomy.mockImplementationOnce(() => new Promise(() => {}));
    chooseMode(view.container, "Autopilot");
    expect(autonomyState(view.container)).toBe("pending");
    expect(buttonIn(view.container.querySelector('[data-kind="owned-elsewhere"]'), "Try again")?.disabled).toBe(true);
    expect(buttonIn(messageItem(view.container, "m1"), "Withdraw")?.disabled).toBe(true);
    expect([...resolveGroup(view.container)!.querySelectorAll<HTMLButtonElement>("button")].every(button => button.disabled)).toBe(true);
    view.unmount();
  });

  it("names each reopened message's mode from its own session or the server's record, never from another session's revision", async () => {
    const server = fakeServer();
    const a = server.seed("ada", [
      { ...message("a1", "assistant", "Answer from an older server", "completed"), acceptedPolicyRevision: 1 },
      { ...message("a2", "assistant", "Answer with a recorded policy", "completed"), acceptedPolicyRevision: 1, acceptedPolicy: { revision: 1, autonomyMode: "manual" } }
    ]);
    server.setPolicy(a, policy("auto-read-only", 2));
    const b = server.seed("ada", [{ ...message("b1", "assistant", "Answer in B", "completed"), acceptedPolicyRevision: 1 }]);
    server.setPolicy(b, policy("full-auto", 1));
    const view = mount(server, "ada");
    await settle(() => expect(view.container.querySelector(`[data-session-id="${b}"]`)).not.toBeNull());

    click(view.container.querySelector(`[data-session-id="${b}"]`));
    await settle(() => expect(policyLabel(view.container, "b1")).toBe("Accepted under Autopilot"));
    click(buttonIn(view.container, "New conversation"));
    await settle(() => expect(view.container.querySelector(`[data-session-id="${a}"]`)).not.toBeNull());
    click(view.container.querySelector(`[data-session-id="${a}"]`));
    await settle(() => expect(messageItem(view.container, "a1")).not.toBeNull());

    // B's revision 1 was Autopilot; A's revision 1 is A's own, and only the server's record names its mode.
    expect(policyLabel(view.container, "a1")).toBe("Accepted under policy revision 1");
    expect(policyLabel(view.container, "a2")).toBe("Accepted under Manual review");
    expect(modeButton(view.container)?.textContent).toBe("Auto-apply low-risk");
    view.unmount();
  });
});

// One journey through the real client mapping and stream parser, over server-shaped JSON and an SSE body.
describe("Weaver recovery through the real agent client", () => {
  it("lists, reopens and runs a session whose stream reports the session limit, all from wire shapes", async () => {
    const requests: string[] = [];
    const session = {
      id: "agt_wire", title: "Wire session", status: "active",
      policy: { autonomyMode: "auto-read-only", maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"], revision: 2 },
      messages: [
        { id: "w1", role: "user", content: "Earlier wire question", status: "completed", acceptedPolicyRevision: 1 },
        { id: "w2", role: "assistant", content: "Earlier wire answer", status: "completed", acceptedPolicyRevision: 1,
          acceptedPolicy: { autonomyMode: "full-auto", maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"], revision: 1 } }
      ]
    };
    const proposal = { id: "prop_wire", title: "Rename wire sample", summary: "Rename it.", risk: "reviewRequired", status: "expired", baseRevision: "r1",
      resourceTarget: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" } };
    const http = {
      async getJson(path: string) {
        requests.push(`GET ${path}`);
        if (path === "/_elsa/weaver/bootstrap") return { data: { enabled: true, providerStatus: "available", modes: ["explain"],
          capabilities: [{ id: "weaver.test", displayName: "Weaver test", description: "", kind: "answer", risk: "readOnly", surfaces: ["*"] }],
          provider: { providerId: "wire", isAvailable: true, status: "available", providerKind: "provider-sdk-binding", supportedOperations: ["chat"], riskProfile: "read-only", metadata: {} },
          policy: { contextVisibility: true, defaultAutonomyMode: "manual", maxAutonomyMode: "fullAuto", allowedAutonomyModes: ["manual", "autoReadOnly", "fullAuto"] } } };
        if (path.startsWith("/_elsa/weaver/sessions?")) return { data: { items: [{ id: session.id, title: session.title, status: "active", updatedAt: "2026-11-02T10:00:00Z", expiresAt: null, pendingReviewCount: 2, recoveryState: null }], next: null } };
        if (path === `/_elsa/weaver/sessions/${session.id}`) return { data: { sessionId: session.id, status: session.status, title: session.title, policy: session.policy, messages: session.messages } };
        if (path === `/_elsa/weaver/sessions/${session.id}/proposals`) return { data: [proposal] };
        throw new Error(`Unexpected GET ${path}`);
      },
      async postJson(path: string) {
        requests.push(`POST ${path}`);
        if (path === `/_elsa/weaver/sessions/${session.id}/messages`) return { data: { message: { id: "w3", status: "pending", context: [],
          acceptedPolicy: { autonomyMode: "auto-read-only", maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"], revision: 2 } }, warnings: [] } };
        throw new Error(`Unexpected POST ${path}`);
      },
      async putJson(path: string) { throw new Error(`Unexpected PUT ${path}`); }
    };
    const wire = { message: "Session limit reached: this conversation has recorded 8 MiB of recorded disclosure. Continue in a new session; nothing recorded here was discarded." };
    const fetch = vi.fn(async () => new Response(new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new TextEncoder().encode(`data: ${JSON.stringify({ kind: "error", error: { code: "weaver.context.disclosure_budget_exceeded", message: wire.message, statusCode: 409, limit: "bytes" } })}\n\n`));
        controller.close();
      }
    }), { status: 200 }));
    const api = readyApi("ada");
    const client = createAgentClient({ baseUrl: "https://primary.example/", http } as unknown as StudioEndpointContext);
    const subscribe: typeof subscribeAgentSessionStream = (context, url, onEvent, onError, options) => subscribeAgentSessionStream(context, url, onEvent, onError, { ...options, fetch: fetch as typeof globalThis.fetch });
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    flushSync(() => root.render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribe} />));

    // mapSessionSummary: the listed summary, with its review count and null optional fields.
    await settle(() => expect(container.querySelector(`[data-session-id="${session.id}"]`)?.textContent).toContain("Wire session"));
    expect(container.querySelector(`[data-session-id="${session.id}"]`)?.textContent).toContain("· 2 awaiting review");
    click(container.querySelector(`[data-session-id="${session.id}"]`));

    // mapRecordedMessage and mapRecordedProposal: history, the recorded accepted policy and the proposal status.
    await settle(() => expect(proposalByTitle(container, "Rename wire sample")?.getAttribute("data-status")).toBe("expired"));
    expect(container.textContent).toContain("Earlier wire question");
    expect(policyLabel(container, "w2")).toBe("Accepted under Autopilot");
    expect(modeButton(container)?.textContent).toBe("Auto-apply low-risk");
    expect(autonomyState(container)).toBe("acknowledged");

    // The stream's error code and limit, parsed from SSE, become the session limit notice.
    await composerReady(container);
    typeAndSend(container, "One more question");
    await settle(() => expect(container.querySelector('[data-kind="limit"]')).not.toBeNull());
    const notice = container.querySelector('[data-kind="limit"]');
    expect(notice?.getAttribute("data-limit")).toBe("bytes");
    expect(notice?.textContent).toContain(wire.message);
    expect(buttonIn(notice, "Continue in a new session")).not.toBeNull();
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(String((fetch.mock.calls[0] as unknown[])[0])).toBe(`https://primary.example/_elsa/weaver/sessions/${session.id}/stream`);
    expect(requests).toEqual([
      "GET /_elsa/weaver/bootstrap",
      "GET /_elsa/weaver/sessions?workspace=studio%3Aprimary&take=20",
      `GET /_elsa/weaver/sessions/${session.id}`,
      `GET /_elsa/weaver/sessions/${session.id}/proposals`,
      `POST /_elsa/weaver/sessions/${session.id}/messages`
    ]);
    flushSync(() => root.unmount());
    container.remove();
  });

  it.each([
    ["processLifetime", "Conversations survive a reload while this server runs; a server restart ends them."],
    ["unsupported", "This server cannot recover conversations: a server restart ends them."],
    ["durable", null],
    [undefined, null]
  ] as const)("states what a server restart does to conversations when bootstrap reports %s", async (recovery, note) => {
    const http = {
      async getJson(path: string) {
        if (path === "/_elsa/weaver/bootstrap") return { data: { enabled: true, providerStatus: "available", modes: ["explain"], ...(recovery ? { recovery } : {}),
          capabilities: [{ id: "weaver.test", displayName: "Weaver test", description: "", kind: "answer", risk: "readOnly", surfaces: ["*"] }],
          provider: { providerId: "wire", isAvailable: true, status: "available", providerKind: "provider-sdk-binding", supportedOperations: ["chat"], riskProfile: "read-only", metadata: {} },
          policy: { contextVisibility: true, defaultAutonomyMode: "manual", maxAutonomyMode: "fullAuto", allowedAutonomyModes: ["manual", "autoReadOnly", "fullAuto"] } } };
        if (path.startsWith("/_elsa/weaver/sessions?")) return { data: { items: [], next: null } };
        throw new Error(`Unexpected GET ${path}`);
      },
      async postJson(path: string) { throw new Error(`Unexpected POST ${path}`); },
      async putJson(path: string) { throw new Error(`Unexpected PUT ${path}`); }
    };
    const client = createAgentClient({ baseUrl: "https://primary.example/", http } as unknown as StudioEndpointContext);
    expect((await client.bootstrap()).recovery).toBe(recovery);
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    flushSync(() => root.render(<WeaverSurface api={readyApi("ada")} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={vi.fn()} />));

    await composerReady(container);
    expect(container.querySelector(".weaver-recovery-scope")?.textContent ?? null).toBe(note);
    flushSync(() => root.unmount());
    container.remove();
  });

  it("reopens a session the server records as limited and offers its continuation before any prompt", async () => {
    const requests: string[] = [];
    const id = "agt_limited";
    const policy = { autonomyMode: "manual", maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"], revision: 1 };
    const http = {
      async getJson(path: string) {
        requests.push(`GET ${path}`);
        if (path === "/_elsa/weaver/bootstrap") return { data: { enabled: true, providerStatus: "available", modes: ["explain"],
          capabilities: [{ id: "weaver.test", displayName: "Weaver test", description: "", kind: "answer", risk: "readOnly", surfaces: ["*"] }],
          provider: { providerId: "wire", isAvailable: true, status: "available", providerKind: "provider-sdk-binding", supportedOperations: ["chat"], riskProfile: "read-only", metadata: {} },
          policy: { contextVisibility: true, defaultAutonomyMode: "manual", maxAutonomyMode: "fullAuto", allowedAutonomyModes: ["manual", "autoReadOnly", "fullAuto"] } } };
        if (path.startsWith("/_elsa/weaver/sessions?")) return { data: { items: [{ id, title: "Limited session", status: "active", updatedAt: "2026-11-02T10:00:00Z", expiresAt: null, pendingReviewCount: 0, recoveryState: "limit-reached:snapshots" }], next: null } };
        if (path === `/_elsa/weaver/sessions/${id}`) return { data: { sessionId: id, status: "active", title: "Limited session", policy, limitReached: "snapshots",
          messages: [{ id: "l1", role: "user", content: "A long conversation", status: "completed", acceptedPolicyRevision: 1 }] } };
        if (path === `/_elsa/weaver/sessions/${id}/proposals`) return { data: [] };
        throw new Error(`Unexpected GET ${path}`);
      },
      async postJson(path: string) { throw new Error(`Unexpected POST ${path}`); },
      async putJson(path: string) { throw new Error(`Unexpected PUT ${path}`); }
    };
    const fetch = vi.fn(async () => new Response(null, { status: 500 }));
    const client = createAgentClient({ baseUrl: "https://primary.example/", http } as unknown as StudioEndpointContext);
    const subscribe: typeof subscribeAgentSessionStream = (context, url, onEvent, onError, options) => subscribeAgentSessionStream(context, url, onEvent, onError, { ...options, fetch: fetch as typeof globalThis.fetch });
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    flushSync(() => root.render(<WeaverSurface api={readyApi("ada")} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribe} />));

    await settle(() => expect(container.querySelector(`[data-session-id="${id}"]`)?.textContent).toContain("session limit reached"));
    click(container.querySelector(`[data-session-id="${id}"]`));

    // The recorded limit alone shows the notice and its continuation; no prompt is sent and no stream opens.
    await settle(() => expect(container.querySelector('[data-kind="limit"]')).not.toBeNull());
    const notice = container.querySelector('[data-kind="limit"]');
    expect(notice?.getAttribute("data-limit")).toBe("snapshots");
    expect(notice?.textContent).toContain("Session limit reached: this conversation has no room left for more context.");
    expect(buttonIn(notice, "Continue in a new session")).not.toBeNull();
    expect(container.textContent).toContain("A long conversation");
    expect(fetch).not.toHaveBeenCalled();
    expect(requests).toEqual([
      "GET /_elsa/weaver/bootstrap",
      "GET /_elsa/weaver/sessions?workspace=studio%3Aprimary&take=20",
      `GET /_elsa/weaver/sessions/${id}`,
      `GET /_elsa/weaver/sessions/${id}/proposals`
    ]);
    flushSync(() => root.unmount());
    container.remove();
  });
});

type Recorded = { owner: string; detail: AgentSessionDetailResponse; proposals: AgentRecordedProposal[] };

function fakeServer(options: { listUnsupported?: boolean } = {}) {
  const sessions = new Map<string, Recorded>();
  const writes: string[] = [];
  let next = 0;
  const notFound = () => new AgentApiError(404, "Agent session was not found.", "weaver.session.not_found", null);
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
      return { messageId: "msg_1", status: "pending", streamUrl: `/_elsa/weaver/sessions/${id}/stream` };
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
      if (options.listUnsupported) throw new AgentApiError(409, "Unsupported.", "weaver.session.query_unsupported", null);
      return { items: [...sessions].filter(([, item]) => item.owner === "ada").reverse().map(([id, item]) => ({ id, title: item.detail.title, status: item.detail.status, updatedAt: "2026-11-02T10:00:00Z", pendingReviewCount: item.proposals.filter(p => p.status === "awaiting-approval").length })) };
    }),
    listProposals: vi.fn<AgentClient["listProposals"]>(async id => structuredClone(owned(id).proposals)),
    withdrawMessage: vi.fn<AgentClient["withdrawMessage"]>(async (id, messageId) => {
      const recorded = owned(id).detail.messages.find(item => item.id === messageId);
      if (!recorded) throw new AgentApiError(404, "Not found.", "weaver.message.not_found", null);
      if (recorded.status !== "pending") throw new AgentApiError(409, "Not pending.", "weaver.message.not_pending", null);
      Object.assign(recorded, { status: "cancelled", statusReason: "withdrawn" }); writes.push("withdraw");
      return structuredClone(recorded);
    }),
    resolveClientInvocation: vi.fn<AgentClient["resolveClientInvocation"]>(async (id, invocationId, claim) => {
      const proposal = owned(id).proposals.find(p => p.preparedInvocation?.invocationId === invocationId);
      if (!proposal) throw new AgentApiError(404, "Not found.", "weaver.invocation.not_found", null);
      if (proposal.status !== "uncertain") throw new AgentApiError(409, "Not uncertain.", "weaver.invocation.not_uncertain", null);
      if (proposal.ownerResolution && proposal.ownerResolution.claim !== claim) throw new AgentApiError(409, "Conflict.", "weaver.invocation.resolution_conflict", null);
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

/** Waits for observable state instead of a fixed number of event-loop turns. */
function settle(assertion: () => void) {
  return vi.waitFor(assertion, { timeout: 2000, interval: 5 });
}

function composerReady(container: HTMLElement) {
  return settle(() => expect(container.querySelector<HTMLTextAreaElement>("textarea")?.disabled).toBe(false));
}

/** The owner's session list answered and the surface acted on it; used before asserting that nothing was opened. */
async function discovered(server: ReturnType<typeof fakeServer>) {
  await settle(() => expect(server.client.listSessions).toHaveBeenCalled());
  await server.client.listSessions.mock.results.at(-1)?.value;
  await new Promise(resolve => setTimeout(resolve, 0));
}

function modeButton(container: HTMLElement) {
  return container.querySelector<HTMLButtonElement>('button[aria-label="Auto-apply mode"]');
}

function autonomyState(container: HTMLElement) {
  return container.querySelector(".weaver-auto-apply")?.getAttribute("data-autonomy-state");
}

function chooseMode(container: HTMLElement, label: string) {
  click(modeButton(container));
  click([...container.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]')].find(item => item.querySelector("strong")?.textContent === label));
}

function policyLabel(container: HTMLElement, messageId: string) {
  return messageItem(container, messageId)?.querySelector(".weaver-message-policy")?.textContent ?? null;
}

function resolveGroup(container: HTMLElement) {
  return container.querySelector('[aria-label="Resolve uncertain change"]');
}
