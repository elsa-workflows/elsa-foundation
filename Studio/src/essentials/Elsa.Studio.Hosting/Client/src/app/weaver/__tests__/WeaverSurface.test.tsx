import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext, StudioHttpError, type StudioAgentPreparedInvocation } from "../../../sdk";
import { AgentExecutionError, createAgentClient, type AgentClient } from "../../agent/agentClient";
import type { AgentStreamEvent } from "../../agent/agentTypes";
import { WeaverSurface } from "../WeaverSurface";

describe("WeaverSurface", () => {
  it("starts a fresh conversation after Stop when the original accepted receipt leaves its session busy", async () => {
    const fixture = await pendingConfirmationFixture(false);
    fixture.client.createSession = vi.fn<AgentClient["createSession"]>(async () => ({ sessionId: "fresh-session", status: "active", contextAttachments: [] }));
    fixture.client.sendMessage = vi.fn<AgentClient["sendMessage"]>(async session => {
      if (session === "agt_1") throw new StudioHttpError(409, "The original paused session remains busy.");
      return { messageId: "fresh-message", status: "pending", streamUrl: `/_elsa/agent/sessions/${session}/stream` };
    });
    try {
      flushSync(() => buttonByText(fixture.container, "Stop")!.click()); await flushPromises();
      fixture.resolveAcknowledgement(); await flushPromises();
      expect(fixture.streams()).toBe(1); expect(fixture.container.querySelector('[data-status="executed"]')).toBeTruthy();
      typeAndSend(fixture.container, "A fresh request"); await flushPromises();
      expect(fixture.client.createSession).toHaveBeenCalledTimes(1);
      expect(fixture.client.sendMessage).toHaveBeenCalledWith("fresh-session", expect.any(Object));
      expect(fixture.client.acknowledgeClient).toHaveBeenCalledWith("agt_1", "i", expect.objectContaining({ outcome: "applied" }));
      expect(fixture.mutate).toHaveBeenCalledTimes(1); expect(fixture.container.textContent).toContain("next prompt starts a new conversation");
    } finally { fixture.unmount(); }
  });

  it("does not reattach a superseded createSession response after Stop and a fresh conversation", async () => {
    const api = readyApi(); const client = readyClient(); let stale!: (value: Awaited<ReturnType<AgentClient["createSession"]>>) => void;
    client.createSession = vi.fn<AgentClient["createSession"]>().mockImplementationOnce(() => new Promise(resolve => { stale = resolve; })).mockResolvedValue({ sessionId: "fresh-session", status: "active", contextAttachments: [] });
    const subscribeStream = ((_context, _url, emit) => { emit({ type: "message-completed", messageId: "msg_1" }); return { close() {} }; }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={client} subscribeStream={subscribeStream} />);
    try {
      await flushPromises(); typeAndSend(container, "First request"); await flushPromises(); flushSync(() => buttonByText(container, "Stop")!.click()); await flushPromises();
      typeAndSend(container, "Second request"); await flushPromises();
      stale({ sessionId: "stopped-session", status: "active", contextAttachments: [] }); await flushPromises();
      typeAndSend(container, "Third request"); await flushPromises();
      expect(client.createSession).toHaveBeenCalledTimes(2); expect(vi.mocked(client.sendMessage).mock.calls.map(([session]) => session)).toEqual(["fresh-session", "fresh-session"]);
    } finally { unmount(); }
  });

  it("retains a late automatic server receipt after Stop without reopening its cancelled turn", async () => {
    const api = readyApi(); const client = readyClient(); const invocation = preparedInvocation(api);
    invocation.executionLocation = "server"; invocation.client = null; invocation.requiresApproval = false;
    client.bootstrap = vi.fn<AgentClient["bootstrap"]>(async () => ({ ...await readyClient().bootstrap(), policy: { ...(await readyClient().bootstrap()).policy, defaultAutonomyMode: "full-auto" } }));
    let deliver!: (value: Awaited<ReturnType<AgentClient["executeProposal"]>>) => void;
    client.executeProposal = vi.fn<AgentClient["executeProposal"]>(() => new Promise(resolve => { deliver = resolve; }));
    const results: unknown[] = []; api.agent.onPromptResult(value => results.push(value)); let streams = 0;
    const subscribeStream = ((_context, _url, emit) => {
      if (++streams === 1) { emit({ type: "turn-started", turnId: "turn", maxSteps: 1 }); emit({ type: "proposal-created", proposalId: "proposal", messageId: "msg_1", proposal: { title: "Change", summary: "Review change.", revision: "r1", resourceTarget: invocation.target!, preparedInvocation: invocation } }); emit({ type: "review-requested", messageId: "msg_1" }); }
      else emit({ type: "message-completed", messageId: "msg_1" });
      return { close() {} };
    }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "auto", requestId: "auto-request", message: "Change" }} />);
    try {
      await flushPromises(); expect(client.executeProposal).toHaveBeenCalledTimes(1); flushSync(() => buttonByText(container, "Stop")!.click()); await flushPromises();
      const payload = { typeId: "sample.result", schemaVersion: "1", data: { value: 2 } };
      deliver({ proposalId: "proposal", approvalStatus: "executed", executed: true, toolResult: { toolCallId: "call", toolName: "sample_change", succeeded: true, content: "Owner committed.", payload, receipt: { invocationId: "i", before: invocation.target!, after: { ...invocation.target!, revision: "r2" }, scope: "serverPersisted", evidence: payload } } }); await flushPromises();
      expect(streams).toBe(1); expect(container.querySelector('[data-status="executed"]')).toBeTruthy(); expect(container.textContent).toContain("r2");
      expect(results).toEqual([expect.objectContaining({ requestId: "auto-request", status: "cancelled" })]);
    } finally { unmount(); }
  });

  it("refuses a late client202 after Stop before dispatch without replacing a newer turn", async () => {
    const api = readyApi(); const client = readyClient(); const invocation = preparedInvocation(api);
    const mutate = vi.fn(async () => ({ outcome: "applied" as const, summary: "Must not run." }));
    api.agent.toolContracts.add({ id: "sample_change", slotId: "sample", moduleId: "Sample", displayName: "Change", description: "Change", surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], executionLocation: "clientWorkingState", contractVersion: "1", executeClient: mutate });
    let deliver!: (value: Awaited<ReturnType<AgentClient["executeProposal"]>>) => void;
    client.executeProposal = vi.fn<AgentClient["executeProposal"]>(() => new Promise(resolve => { deliver = resolve; }));
    client.acknowledgeClient = vi.fn<AgentClient["acknowledgeClient"]>(async () => ({ proposalId: "proposal", approvalStatus: "failed", executed: false }));
    const results: unknown[] = []; api.agent.onPromptResult(value => results.push(value)); let streams = 0;
    const subscribeStream = ((_context, _url, emit) => {
      if (++streams === 1) { emit({ type: "turn-started", turnId: "turn", maxSteps: 1 }); emit({ type: "proposal-created", proposalId: "proposal", messageId: "msg_1", proposal: { title: "Change", summary: "Review change.", revision: "r1", resourceTarget: invocation.target!, preparedInvocation: invocation } }); emit({ type: "review-requested", messageId: "msg_1" }); }
      else emit({ type: "message-started", messageId: "new", role: "assistant" });
      return { close() {} };
    }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "late", requestId: "late-request", message: "Change" }} />);
    try {
      await flushPromises(); flushSync(() => buttonByText(container, "Approve")!.click()); await flushPromises(); flushSync(() => buttonByText(container, "Apply")!.click()); await flushPromises();
      const stop = buttonByText(container, "Stop")!; expect(stop).toBeTruthy(); flushSync(() => stop.click()); await flushPromises();
      typeAndSend(container, "New request"); await flushPromises();
      deliver({ proposalId: "proposal", approvalStatus: "awaiting-client", executed: false, clientInvocation: invocation }); await flushPromises();
      expect(mutate).not.toHaveBeenCalled(); expect(client.acknowledgeClient).toHaveBeenCalledWith("agt_1", "i", expect.objectContaining({ outcome: "refused" }));
      expect(streams).toBe(2); expect(container.querySelector(".weaver-surface")?.getAttribute("aria-busy")).toBe("true");
      expect(results).toEqual([expect.objectContaining({ requestId: "late-request", status: "cancelled" })]);
    } finally { unmount(); }
  });

  it.each(["module", "ack"])("admits a late %s observation after Stop without reopening or hijacking a newer turn", async phase => {
    const fixture = await pendingConfirmationFixture(phase === "module");
    try {
      flushSync(() => buttonByText(fixture.container, "Stop")!.click()); await flushPromises();
      typeAndSend(fixture.container, "A new request"); await flushPromises(); expect(fixture.client.sendMessage).toHaveBeenCalledTimes(2);
      if (phase === "module") { fixture.resolveObservation(); await flushPromises(); }
      expect(fixture.client.acknowledgeClient).toHaveBeenCalledTimes(1);
      fixture.rejectAcknowledgement(); await flushPromises();
      expect(fixture.streams()).toBe(2); expect(fixture.container.querySelector(".weaver-surface")?.getAttribute("aria-busy")).toBe("true");
      fixture.finishNewTurn(); await flushPromises();
      flushSync(() => buttonByText(fixture.container, "Retry confirmation")!.click()); await flushPromises();
      expect(fixture.mutate).toHaveBeenCalledTimes(1); expect(fixture.streams()).toBe(2);
      expect(fixture.results).toEqual([expect.objectContaining({ requestId: "pending-request", status: "cancelled" })]);
    } finally { fixture.unmount(); }
  });

  it("settles an unmounted pending confirmation truthfully once without a late stream reopen", async () => {
    const fixture = await pendingConfirmationFixture(false);
    fixture.unmount(); fixture.resolveAcknowledgement(); await flushPromises();
    expect(fixture.results).toEqual([expect.objectContaining({ requestId: "pending-request", status: "failed", text: expect.stringContaining("may already have been applied") })]);
    expect(fixture.streams()).toBe(1); expect(fixture.mutate).toHaveBeenCalledTimes(1);
  });

  it("admits a generic uncertain acknowledgement for a noncloneable module observation and drains", async () => {
    const fixture = await pendingConfirmationFixture(false, true);
    try {
      expect(fixture.client.acknowledgeClient).toHaveBeenCalledWith("agt_1", "i", expect.objectContaining({ outcome: "uncertain", client: expect.any(Object), approvalDigest: "digest" }));
      const acknowledgement = vi.mocked(fixture.client.acknowledgeClient).mock.calls[0][2];
      expect(acknowledgement).not.toHaveProperty("result"); expect(acknowledgement).not.toHaveProperty("after");
      expect(fixture.container.querySelector('[data-status="uncertain"]')).toBeTruthy(); expect(fixture.container.querySelector('[data-succeeded="true"]')).toBeNull();
      expect(fixture.streams()).toBe(2); typeAndSend(fixture.container, "Inspect"); await flushPromises(); expect(fixture.client.sendMessage).toHaveBeenCalledTimes(2);
    } finally { fixture.unmount(); }
  });
  it.each(["approve", "deny", "autonomy-approve"])("drains terminal expiry from %s without executing or stranding the caller", async action => {
    const api = readyApi(); const client = readyClient(); const invocation = preparedInvocation(api);
    invocation.executionLocation = "server"; invocation.client = null; invocation.requiresApproval = action !== "autonomy-approve";
    if (action === "autonomy-approve") client.bootstrap = vi.fn<AgentClient["bootstrap"]>(async () => ({ ...await readyClient().bootstrap(), policy: { ...(await readyClient().bootstrap()).policy, defaultAutonomyMode: "full-auto" } }));
    const failure = new AgentExecutionError(409, "The reviewed invocation expired.", "agent.proposal.expired", { proposalId: "proposal", approvalStatus: "expired", executed: false }, {});
    if (action === "deny") client.denyProposal = vi.fn(async () => { throw failure; });
    else client.approveProposal = vi.fn(async () => { throw failure; });
    const results: unknown[] = []; api.agent.onPromptResult(value => results.push(value)); let streams = 0;
    const subscribeStream = ((_context, _url, emit) => {
      if (++streams === 1) { emit({ type: "proposal-created", proposalId: "proposal", messageId: "msg_1", proposal: { title: "Review resource", summary: "Review the resource change.", revision: "r1", resourceTarget: invocation.target!, preparedInvocation: invocation } }); emit({ type: "review-requested", messageId: "msg_1" }); }
      else emit({ type: "message-completed", messageId: "msg_1" });
      return { close() {} };
    }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "expiry", requestId: "expiry-request", message: "Review resource" }} />);
    await flushPromises();
    if (action !== "autonomy-approve") { flushSync(() => buttonByText(container, action === "deny" ? "Deny" : "Approve")!.click()); await flushPromises(); }
    expect(container.querySelector('[data-status="expired"]')).toBeTruthy(); expect(client.executeProposal).not.toHaveBeenCalled();
    expect(results).toEqual([expect.objectContaining({ requestId: "expiry-request", status: "failed" })]); expect(streams).toBe(2);
    typeAndSend(container, "Inspect current resource"); await flushPromises(); expect(client.sendMessage).toHaveBeenCalledTimes(2); expect(results).toHaveLength(1);
    unmount();
  });

  it.each([1, 2])("retries an accepted acknowledgement lost %s times using only the exact observed receipt", async losses => {
    const api = readyApi(); const client = readyClient(); const invocation = preparedInvocation(api);
    const observed = { outcome: "applied" as const, summary: "Observed value 2.", after: { ...invocation.target!, revision: "r2" }, result: { typeId: "sample.result", schemaVersion: "1", data: { value: 2 } } };
    const mutate = vi.fn(async () => observed);
    api.agent.toolContracts.add({ id: "sample_change", slotId: "sample", moduleId: "Sample", displayName: "Change", description: "Change sample", surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], executionLocation: "clientWorkingState", contractVersion: "1", executeClient: mutate });
    client.executeProposal = vi.fn<AgentClient["executeProposal"]>(async () => ({ proposalId: "proposal", approvalStatus: "awaiting-client", executed: false, clientInvocation: invocation }));
    const receipts: string[] = []; let acceptedReceipt: string | null = null;
    client.acknowledgeClient = vi.fn<AgentClient["acknowledgeClient"]>(async (_session, _id, receipt) => {
      const wire = JSON.stringify(receipt); receipts.push(wire); acceptedReceipt ??= wire; expect(wire).toBe(acceptedReceipt);
      if (receipts.length <= losses) throw new Error("Response lost after the server accepted this exact receipt.");
      return { proposalId: "proposal", approvalStatus: "executed", executed: true, toolResult: { toolCallId: "call", toolName: "sample_change", succeeded: true, content: "Recorded owning receipt.", payload: observed.result } };
    });
    const results: unknown[] = []; api.agent.onPromptResult(value => results.push(value)); let streams = 0;
    const subscribeStream = ((_context, _url, emit) => {
      if (++streams === 1) { emit({ type: "proposal-created", proposalId: "proposal", messageId: "msg_1", proposal: { title: "Change resource", summary: "Review resource change.", revision: "r1", resourceTarget: invocation.target!, preparedInvocation: invocation } }); emit({ type: "review-requested", messageId: "msg_1" }); }
      else emit({ type: "message-completed", messageId: "msg_1" });
      return { close() {} };
    }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "lost", requestId: "lost-request", message: "Change resource" }} />);
    await flushPromises(); flushSync(() => buttonByText(container, "Approve")!.click()); await flushPromises(); flushSync(() => buttonByText(container, "Apply")!.click()); await flushPromises();
    for (let attempt = 0; attempt < losses; attempt++) {
      expect(results).toEqual([]); expect(container.querySelector<HTMLTextAreaElement>("textarea")!.disabled).toBe(true);
      const retry = buttonByText(container, "Retry confirmation")!; expect(retry).toBeTruthy(); expect(retry.disabled).toBe(false);
      flushSync(() => retry.click()); await flushPromises();
    }
    expect(mutate).toHaveBeenCalledTimes(1); expect(client.executeProposal).toHaveBeenCalledTimes(1); expect(receipts).toHaveLength(losses + 1);
    expect(container.querySelector('[data-status="executed"]')).toBeTruthy(); expect(results).toEqual([expect.objectContaining({ requestId: "lost-request", status: "completed" })]);
    expect(streams).toBe(2); expect(client.sendMessage).toHaveBeenCalledTimes(1);
    typeAndSend(container, "Inspect current resource"); await flushPromises(); expect(client.sendMessage).toHaveBeenCalledTimes(2); expect(results).toHaveLength(1);
    unmount();
  });
  it.each(["expired", "failed"])("drains an authoritative server %s refusal and releases the original caller without replay", async status => {
    const api = readyApi(); const client = readyClient(); const invocation = preparedInvocation(api); invocation.executionLocation = "server"; invocation.client = null;
    const results: unknown[] = []; api.agent.onPromptResult(value => results.push(value));
    const toolResult = status === "failed" ? { toolCallId: "call", toolName: "sample_change", succeeded: false, content: "The owning resource revision changed.", payload: { typeId: "agent.lifecycle.refused", schemaVersion: "1", data: { applied: false } } } : null;
    const summary = status === "expired" ? "The reviewed invocation expired." : "The owning resource revision changed.";
    const envelope = { error: { code: `agent.execution.${status}`, message: summary, statusCode: 409 }, data: { proposalId: "proposal", executed: false, status, resourceType: "sample", resourceId: "one", summary, clientInvocation: null, toolResult } };
    client.executeProposal = createAgentClient({ ...api.backend, http: { ...api.backend.http, postJson: vi.fn(async () => { throw new StudioHttpError(409, summary, null, envelope); }) } }).executeProposal;
    let streams = 0;
    const subscribeStream = ((_context, _url, emit) => {
      if (++streams === 1) {
        emit({ type: "proposal-created", proposalId: "proposal", messageId: "msg_1", proposal: { title: "Change sample", summary: "Reviewed server effect.", revision: "r1", resourceTarget: invocation.target!, preparedInvocation: invocation } });
        emit({ type: "review-requested", messageId: "msg_1" });
      } else { if (toolResult) emit({ type: "module-result", messageId: "msg_1", result: toolResult }); emit({ type: "message-completed", messageId: "msg_1" }); }
      return { close() {} };
    }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "delivery", requestId: "refused-request", message: "Change sample" }} />);
    await flushPromises(); flushSync(() => buttonByText(container, "Approve")!.click()); await flushPromises();
    flushSync(() => buttonByText(container, "Apply")!.click()); await flushPromises();
    expect(container.querySelector(`[data-status="${status}"]`)).toBeTruthy(); expect(container.textContent).toContain(summary);
    expect(results).toEqual([expect.objectContaining({ requestId: "refused-request", status: "failed" })]); expect(streams).toBe(2); expect(client.sendMessage).toHaveBeenCalledTimes(1);
    expect(container.querySelector('[data-succeeded="true"]')).toBeNull();
    typeAndSend(container, "Inspect the latest resource"); await flushPromises(); expect(client.sendMessage).toHaveBeenCalledTimes(2);
    expect(results).toHaveLength(1); unmount();
  });

  it("streams an agentic turn and renders the answer as markdown without an update loop", async () => {
    const api = readyApi();
    const indicatorCalls: number[] = [];
    const client = readyClient();
    const events: AgentStreamEvent[] = [
      { type: "turn-started", turnId: "turn_1", maxSteps: 8 },
      { type: "step-started", stepIndex: 0, maxSteps: 8 },
      { type: "message-delta", messageId: "msg_1", content: "## Done\n\nApplied **changes**." },
      { type: "step-completed", stepIndex: 0, maxSteps: 8 },
      { type: "message-completed", messageId: "msg_1" }
    ];
    const subscribeStream = ((_ctx, _url, onEvent) => {
      for (const event of events) onEvent(event);
      return { close: () => {} };
    }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;

    const { container, unmount } = render(
      <WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={client} subscribeStream={subscribeStream} onSessionIndicatorChange={sessions => indicatorCalls.push(sessions.length)} />
    );

    await flushPromises();
    flushSync(() => container.querySelector<HTMLButtonElement>(".weaver-send")?.click());
    typeAndSend(container, "Add an activity");
    await flushPromises();

    expect(container.querySelector(".weaver-markdown h2")?.textContent).toBe("Done");
    expect(container.querySelector(".weaver-markdown strong")?.textContent).toBe("changes");
    // A render loop would push hundreds of indicator updates; a healthy run settles quickly.
    expect(indicatorCalls.length).toBeLessThan(20);

    unmount();
  });

  it("does not let a stopped late duplicate message response finish a fresh conversation", async () => {
    const api = readyApi();
    const client = readyClient();
    let resolveSend: ((value: Awaited<ReturnType<AgentClient["sendMessage"]>>) => void) | undefined;
    client.createSession = vi.fn<AgentClient["createSession"]>().mockResolvedValueOnce({ sessionId: "stopped-session", status: "active", contextAttachments: [] }).mockResolvedValue({ sessionId: "fresh-session", status: "active", contextAttachments: [] });
    client.sendMessage = vi.fn<AgentClient["sendMessage"]>().mockImplementationOnce(() => new Promise(resolve => { resolveSend = resolve; })).mockResolvedValue({ messageId: "fresh-message", status: "pending", streamUrl: "/_elsa/agent/sessions/fresh-session/stream" });
    const subscribeStream = vi.fn(() => ({ close: () => {} })) as unknown as typeof import("../../agent/agentStream").subscribeAgentSessionStream;

    const { container, unmount } = render(
      <WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={client} subscribeStream={subscribeStream} />
    );

    await flushPromises();
    typeAndSend(container, "Add an activity");
    await flushPromises();

    // The turn is parked awaiting sendMessage; stop, then let the send resolve.
    flushSync(() => container.querySelector<HTMLButtonElement>(".weaver-stop")?.click());
    typeAndSend(container, "Fresh request"); await flushPromises();
    resolveSend?.({ messageId: "msg_1", status: "completed", streamUrl: "/_elsa/agent/sessions/stopped-session/stream", warnings: [{ code: "agent.message.duplicate", message: "Already completed." }] });
    await flushPromises();

    expect(subscribeStream).toHaveBeenCalledTimes(1);
    expect(container.querySelector(".weaver-surface")?.getAttribute("aria-busy")).toBe("true");
    expect(container.textContent).not.toContain("already accepted");

    unmount();
  });

  it("cancels the turn server-side when stopped before the turn id is known", async () => {
    const api = readyApi();
    const client = readyClient();
    client.createSession = vi.fn<AgentClient["createSession"]>().mockResolvedValueOnce({ sessionId: "stopped-session", status: "active", contextAttachments: [] }).mockResolvedValue({ sessionId: "fresh-session", status: "active", contextAttachments: [] });
    let emit: ((event: AgentStreamEvent) => void) | undefined;
    const subscribeStream = ((_ctx, _url, onEvent) => { emit = onEvent; return { close: () => {} }; }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;

    const { container, unmount } = render(
      <WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={client} subscribeStream={subscribeStream} />
    );

    await flushPromises();
    typeAndSend(container, "Add an activity");
    await flushPromises();

    // Stream is open but no turn-started yet: stop, then let the turn identify itself.
    flushSync(() => container.querySelector<HTMLButtonElement>(".weaver-stop")?.click());
    const stoppedEmit = emit;
    typeAndSend(container, "A fresh request"); await flushPromises();
    flushSync(() => stoppedEmit?.({ type: "turn-started", turnId: "turn_9", maxSteps: 8 }));
    flushSync(() => stoppedEmit?.({ type: "message-completed", messageId: "stopped-message" }));
    await flushPromises();

    expect(client.cancelTurn).toHaveBeenCalledWith("stopped-session", "turn_9");
    expect(client.cancelTurn).not.toHaveBeenCalledWith("fresh-session", "turn_9");
    expect(container.querySelector(".weaver-surface")?.getAttribute("aria-busy")).toBe("true");

    unmount();
  });

  it("only offers autonomy modes up to the deployment ceiling", async () => {
    const api = readyApi();
    const client = readyClient();
    // Lower the ceiling so Full auto must not be offered.
    client.bootstrap = vi.fn(async () => ({
      ...(await readyClient().bootstrap()),
      policy: { contextVisibility: true, defaultAutonomyMode: "auto-read-only", maxAutonomyMode: "auto-read-only", allowedAutonomyModes: ["manual", "auto-read-only"] }
    })) as AgentClient["bootstrap"];

    const { container, unmount } = render(
      <WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={client} />
    );
    await flushPromises();

    flushSync(() => buttonByLabel(container, "Auto-apply mode")?.click());

    const options = [...container.querySelectorAll<HTMLElement>(".weaver-mode-option")]
      .map(option => option.textContent ?? "");
    expect(options).toHaveLength(2);
    expect(options[0]).toContain("Manual review");
    expect(options[1]).toContain("Auto-apply low-risk");
    expect(options.join(" ")).not.toContain("Autopilot");

    unmount();
  });

  it("routes a correlated prompt's completed answer back via onPromptResult", async () => {
    const api = readyApi();
    const client = readyClient();
    const events: AgentStreamEvent[] = [
      { type: "turn-started", turnId: "turn_1", maxSteps: 8 },
      { type: "message-delta", messageId: "msg_1", content: "Here you go.\n```json\n{\"name\":\"Order Approval\"}\n```" },
      { type: "message-completed", messageId: "msg_1" }
    ];
    const subscribeStream = ((_ctx, _url, onEvent) => {
      for (const event of events) onEvent(event);
      return { close: () => {} };
    }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;

    const results: Array<{ requestId: string; status: string; text: string; autoApply?: boolean }> = [];
    api.ai.onPromptResult(result => results.push(result));

    const { unmount } = render(
      <WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "p1", message: "name it", requestId: "req-1", mode: "enqueue", source: { moduleId: "Sample", actionId: "name" }, attachments: [{ id: "explicit", source: "Sample", label: "Exact selection", contentType: "sample", sensitivity: "internal", scope: "selection", content: { value: 42 } }] }} />
    );
    await flushPromises();

    expect(client.sendMessage).toHaveBeenCalledWith("agt_1", expect.objectContaining({ requestId: "req-1", promptMode: "enqueue", source: { moduleId: "Sample", actionId: "name" }, contextAttachments: [expect.objectContaining({ id: "explicit", content: { value: 42 } })] }));
    expect(results).toHaveLength(1);
    expect(results[0]).toMatchObject({ requestId: "req-1", status: "completed", autoApply: false });
    expect(results[0].text).toContain("Order Approval");

    unmount();
  });

  it("forwards the selected autonomy mode when the session is created", async () => {
    const api = readyApi();
    const client = readyClient(); // ceiling is full-auto
    const subscribeStream = vi.fn(() => ({ close: () => {} })) as unknown as typeof import("../../agent/agentStream").subscribeAgentSessionStream;

    const { container, unmount } = render(
      <WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={client} subscribeStream={subscribeStream} />
    );
    await flushPromises();

    flushSync(() => buttonByLabel(container, "Auto-apply mode")?.click());
    flushSync(() => buttonByText(container, "Autopilot")?.click());

    typeAndSend(container, "Add an activity");
    await flushPromises();

    expect(client.createSession).toHaveBeenCalledWith(expect.objectContaining({ autonomyMode: "full-auto" }));

    unmount();
  });
  it("requires local review even in FullAuto and waits for observed acknowledgement before resuming", async () => {
    const api = readyApi(); const client = readyClient();
    const results: unknown[] = []; api.agent.onPromptResult(result => results.push(result));
    client.bootstrap = vi.fn(async () => ({ ...await readyClient().bootstrap(), policy: { ... (await readyClient().bootstrap()).policy, defaultAutonomyMode: "full-auto" } }));
    const invocation = preparedInvocation(api);
    let observe!: (value: { outcome: "applied"; summary: string; after: typeof invocation.target; result: { typeId: string; schemaVersion: string; data: { value: number } } }) => void;
    const execute = vi.fn(() => new Promise(resolve => { observe = resolve; })) as never;
    api.agent.toolContracts.add({ id: "sample_change", slotId: "sample", moduleId: "Sample", displayName: "Change", description: "Change sample", surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], executionLocation: "clientWorkingState", contractVersion: "1", executeClient: execute });
    client.executeProposal = vi.fn(async () => ({ proposalId: "proposal", approvalStatus: "awaiting-client", executed: false, clientInvocation: invocation }));
    client.acknowledgeClient = vi.fn(async () => ({ proposalId: "proposal", approvalStatus: "executed", executed: true, toolResult: { toolCallId: "call", toolName: "sample_change", succeeded: true, content: "Observed sample value 2.", payload: { typeId: "sample.result", schemaVersion: "1", data: { value: 2 } } } }));
    let streams = 0;
    const subscribeStream = vi.fn((_ctx, _url, emit) => {
      if (++streams === 1) {
        emit({ type: "proposal-created", proposalId: "proposal", messageId: "msg", proposal: { title: "Change sample", summary: "Set sample value to 2.", revision: "r1", resourceTarget: invocation.target!, preparedInvocation: invocation } });
        emit({ type: "review-requested", messageId: "msg" });
      } else emit({ type: "message-completed", messageId: "msg" });
      return { close() {} };
    }) as unknown as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "delivery", requestId: "original-request", message: "Change sample" }} />);
    await flushPromises();
    expect(client.approveProposal).not.toHaveBeenCalled(); expect(execute).not.toHaveBeenCalled();
    expect(buttonByText(container, "Approve")!.disabled).toBe(false); expect(results).toEqual([]);
    flushSync(() => buttonByText(container, "Approve")!.click()); await flushPromises();
    flushSync(() => buttonByText(container, "Apply")!.click()); await flushPromises();
    expect(container.querySelector('[data-status="awaiting-client"]')).toBeTruthy(); expect(client.acknowledgeClient).not.toHaveBeenCalled();
    observe({ outcome: "applied", summary: "Observed sample value 2.", after: { ...invocation.target!, revision: "r2" }, result: { typeId: "sample.result", schemaVersion: "1", data: { value: 2 } } });
    await flushPromises();
    expect(client.acknowledgeClient).toHaveBeenCalledWith("agt_1", "i", expect.objectContaining({ approvalDigest: "digest", outcome: "applied", after: expect.objectContaining({ revision: "r2" }) }));
    expect(container.querySelector('[data-status="executed"]')).toBeTruthy(); expect(streams).toBe(2); expect(client.sendMessage).toHaveBeenCalledTimes(1);
    expect(subscribeStream).toHaveBeenLastCalledWith(api.backend, "/_elsa/agent/sessions/agt_1/stream", expect.any(Function), expect.any(Function), { defaultMessageId: "msg_1" });
    expect(results).toEqual([expect.objectContaining({ requestId: "original-request", status: "completed" })]);
    unmount();
  });

  it.each(["outcome-stream", "acknowledgement"])("settles a retained caller once after terminal %s failure without replay", async failure => {
    const api = readyApi(); const client = readyClient(); const invocation = preparedInvocation(api);
    const results: unknown[] = []; api.agent.onPromptResult(result => results.push(result));
    const toolResult = { toolCallId: "call", toolName: "sample_change", succeeded: true, content: "Observed sample value.", payload: { typeId: "sample.result", schemaVersion: "1", data: { value: 2 } } };
    api.agent.toolContracts.add({ id: "sample_change", slotId: "sample", moduleId: "Sample", displayName: "Change", description: "Change sample", surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], executionLocation: "clientWorkingState", contractVersion: "1", executeClient: async () => ({ outcome: "applied", summary: "Observed.", after: { ...invocation.target!, revision: "r2" }, result: toolResult.payload }) });
    client.executeProposal = vi.fn(async () => failure === "acknowledgement" ? { proposalId: "proposal", approvalStatus: "awaiting-client", executed: false, clientInvocation: invocation } : { proposalId: "proposal", approvalStatus: "executed", executed: true, toolResult });
    client.acknowledgeClient = vi.fn(async () => { throw new Error("Acknowledgement transport lost."); });
    let streams = 0;
    const subscribeStream = ((_context, _url, emit, failed) => {
      if (++streams === 1) {
        emit({ type: "proposal-created", proposalId: "proposal", messageId: "msg", proposal: { title: "Change", summary: "Change sample.", revision: "r1", resourceTarget: invocation.target!, preparedInvocation: invocation } });
        emit({ type: "review-requested", messageId: "msg" });
      } else { failed(new Error("Outcome transport lost.")); failed(new Error("Repeated terminal notification.")); }
      return { close() {} };
    }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "delivery", requestId: "terminal-request", message: "Change sample" }} />);
    await flushPromises(); flushSync(() => buttonByText(container, "Approve")!.click()); await flushPromises();
    flushSync(() => buttonByText(container, "Apply")!.click()); await flushPromises();
    if (failure === "acknowledgement") { expect(results).toEqual([]); expect(buttonByText(container, "Retry confirmation")).toBeTruthy(); expect(container.querySelector<HTMLTextAreaElement>("textarea")!.disabled).toBe(true); }
    else expect(results).toEqual([expect.objectContaining({ requestId: "terminal-request", status: "failed" })]);
    expect(client.sendMessage).toHaveBeenCalledTimes(1); expect(client.executeProposal).toHaveBeenCalledTimes(1);
    expect(container.querySelector(".weaver-surface")?.getAttribute("aria-busy")).toBe("false");
    if (failure === "acknowledgement") expect(container.querySelector('[data-status="uncertain"]')).toBeTruthy();
    unmount();
  });

  it("does not open another stream for a completed duplicate delivery", async () => {
    const api = readyApi(); const client = readyClient();
    client.sendMessage = vi.fn(async () => ({ messageId: "existing", status: "completed", streamUrl: "/stream", warnings: [{ code: "agent.message.duplicate", message: "Already accepted." }] }));
    const subscribeStream = vi.fn(() => ({ close() {} })) as unknown as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={client} subscribeStream={subscribeStream} />);
    await flushPromises(); typeAndSend(container, "Inspect"); await flushPromises();
    expect(subscribeStream).not.toHaveBeenCalled(); expect(container.textContent).toContain("already accepted"); unmount();
  });

  it("honors server review in FullAuto and continues a denial without executing or settling twice", async () => {
    const api = readyApi(); const client = readyClient();
    const results: unknown[] = []; api.agent.onPromptResult(result => results.push(result));
    client.bootstrap = vi.fn(async () => ({ ...await readyClient().bootstrap(), policy: { ... (await readyClient().bootstrap()).policy, defaultAutonomyMode: "full-auto" } }));
    const invocation = preparedInvocation(api); invocation.executionLocation = "server"; invocation.client = null;
    let streams = 0;
    const subscribeStream = ((_context, _url, emit) => {
      if (++streams === 1) {
        emit({ type: "proposal-created", proposalId: "review", messageId: "msg", proposal: { title: "Server change", summary: "The module requires explicit review.", revision: "r1", resourceTarget: invocation.target!, preparedInvocation: invocation } });
        emit({ type: "review-requested", messageId: "msg" });
      } else emit({ type: "message-completed", messageId: "msg" });
      return { close() {} };
    }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "delivery", requestId: "denied-request", message: "Propose server change" }} />);
    await flushPromises();
    expect(client.approveProposal).not.toHaveBeenCalled(); expect(client.executeProposal).not.toHaveBeenCalled();
    expect(container.querySelector('[data-status="awaiting-approval"]')).toBeTruthy(); expect(results).toEqual([]);
    flushSync(() => buttonByText(container, "Deny")!.click()); await flushPromises();
    expect(streams).toBe(2); expect(client.sendMessage).toHaveBeenCalledTimes(1); expect(client.executeProposal).not.toHaveBeenCalled();
    expect(results).toEqual([expect.objectContaining({ requestId: "denied-request", status: "cancelled" })]);
    expect(container.querySelector('[data-status="denied"]')).toBeTruthy(); unmount();
  });

});

async function pendingConfirmationFixture(deferObservation: boolean, malformed = false) {
  const api = readyApi(); const client = readyClient(); const invocation = preparedInvocation(api);
  const observed = { outcome: "applied" as const, summary: "Observed.", after: { ...invocation.target!, revision: "r2" }, result: { typeId: "sample.result", schemaVersion: "1", data: malformed ? { invalid: () => {} } : { value: 2 } } };
  let observation!: (value: typeof observed) => void, accept!: (value: Awaited<ReturnType<AgentClient["acknowledgeClient"]>>) => void, reject!: (error: Error) => void;
  const mutate = vi.fn(() => deferObservation ? new Promise<typeof observed>(resolve => { observation = resolve; }) : Promise.resolve(observed));
  api.agent.toolContracts.add({ id: "sample_change", slotId: "sample", moduleId: "Sample", displayName: "Change", description: "Change", surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "review-required", invocationModes: ["proposal"], executionLocation: "clientWorkingState", contractVersion: "1", executeClient: mutate });
  client.executeProposal = vi.fn<AgentClient["executeProposal"]>(async () => ({ proposalId: "proposal", approvalStatus: "awaiting-client", executed: false, clientInvocation: invocation }));
  const recorded = { proposalId: "proposal", approvalStatus: "executed" as const, executed: true, toolResult: { toolCallId: "call", toolName: "sample_change", succeeded: true, content: "Recorded owner receipt.", payload: { typeId: "sample.result", schemaVersion: "1", data: { value: 2 } } } };
  client.acknowledgeClient = malformed ? vi.fn<AgentClient["acknowledgeClient"]>(async () => ({ proposalId: "proposal", approvalStatus: "uncertain", executed: false, toolResult: { toolCallId: "call", toolName: "sample_change", succeeded: false, content: "Application is uncertain." } })) : vi.fn<AgentClient["acknowledgeClient"]>().mockImplementationOnce(() => new Promise((resolve, failed) => { accept = resolve; reject = failed; })).mockResolvedValue(recorded);
  const results: unknown[] = []; api.agent.onPromptResult(value => results.push(value)); let streams = 0; let finishNew!: () => void;
  const subscribeStream = ((_context, _url, emit) => {
    if (++streams === 1) { emit({ type: "proposal-created", proposalId: "proposal", messageId: "msg_1", proposal: { title: "Change", summary: "Review change.", revision: "r1", resourceTarget: invocation.target!, preparedInvocation: invocation } }); emit({ type: "review-requested", messageId: "msg_1" }); }
    else if (malformed) emit({ type: "message-completed", messageId: "msg_1" });
    else { emit({ type: "message-started", messageId: "new", role: "assistant" }); finishNew = () => emit({ type: "message-completed", messageId: "new" }); }
    return { close() {} };
  }) as typeof import("../../agent/agentStream").subscribeAgentSessionStream;
  const mounted = render(<WeaverSurface api={api} surface={{ route: "/sample" }} variant="dock" client={client} subscribeStream={subscribeStream} incomingPrompt={{ id: "pending", requestId: "pending-request", message: "Change" }} />);
  await flushPromises(); flushSync(() => buttonByText(mounted.container, "Approve")!.click()); await flushPromises(); flushSync(() => buttonByText(mounted.container, "Apply")!.click()); await flushPromises();
  return { ...mounted, client, mutate, results, streams: () => streams, resolveObservation: () => observation(observed), rejectAcknowledgement: () => reject(new Error("Accepted response was lost.")), resolveAcknowledgement: () => accept(recorded), finishNewTurn: () => finishNew() };
}

function typeAndSend(container: HTMLElement, value: string) {
  const textarea = container.querySelector("textarea")!;
  const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, "value")!.set!;
  flushSync(() => {
    setter.call(textarea, value);
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
  });
  flushSync(() => container.querySelector<HTMLButtonElement>(".weaver-send")?.click());
}

function buttonByLabel(container: HTMLElement, label: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")]
    .find(button => button.getAttribute("aria-label") === label) ?? null;
}

function buttonByText(container: HTMLElement, text: string) {
  return [...container.querySelectorAll<HTMLButtonElement>("button")]
    .find(button => button.textContent?.includes(text)) ?? null;
}

function readyApi() {
  const api = createStudioRegistry({
    hostVersion: "1.0.0",
    sdkVersion: "1.0.0",
    ...createEndpointContext("https://studio.example/")
  }, "https://foundation.example/");
  api.agent.capabilities.add({
    id: "weaver.test",
    displayName: "Weaver test",
    description: "Test capability.",
    kind: "answer",
    risk: "read-only",
    surfaces: ["*"]
  });
  return api;
}

function readyClient(): AgentClient {
  return {
    bootstrap: vi.fn(async () => ({
      enabled: true,
      providerStatus: "available",
      modes: ["troubleshoot"],
      capabilities: [{ id: "weaver.test", displayName: "Weaver test", description: "", kind: "answer", risk: "read-only", surfaces: ["*"], requiredPermissions: [] }],
      provider: { providerId: "github-copilot", isAvailable: true, status: "ok", providerKind: "provider-sdk-binding", supportedOperations: ["chat"], riskProfile: "read-only", metadata: {} },
      policy: { contextVisibility: true, defaultAutonomyMode: "auto-read-only", maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"] }
    })),
    createSession: vi.fn(async () => ({ sessionId: "agt_1", status: "active", contextAttachments: [] })),
    sendMessage: vi.fn(async () => ({ messageId: "msg_1", status: "pending", streamUrl: "/_elsa/agent/sessions/agt_1/stream" })),
    approveProposal: vi.fn(async proposalId => ({ proposalId, approvalStatus: "approved" })),
    denyProposal: vi.fn(async proposalId => ({ proposalId, approvalStatus: "denied" })),
    executeProposal: vi.fn(async proposalId => ({ proposalId, approvalStatus: "executed" })),
    acknowledgeClient: vi.fn(),
    submitFeedback: vi.fn(),
    cancelTurn: vi.fn(async () => {})
  };
}

function render(element: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(element));
  return {
    container,
    unmount: () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

async function flushPromises() {
  for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0));
}

function preparedInvocation(api: ReturnType<typeof readyApi>): StudioAgentPreparedInvocation {
  return { invocationId: "i", sessionId: "agt_1", turnId: "turn", toolCallId: "call", toolName: "sample_change", toolContractVersion: "1", moduleId: "Sample", actorId: "actor", tenantId: "tenant", target: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" }, arguments: { value: 2 }, approvalDigest: "digest", risk: "reviewRequired", preview: { typeId: "sample.preview", schemaVersion: "1", data: { value: 2 } }, executionLocation: "clientWorkingState", requiresApproval: true, client: { clientId: api.agent.clientId, generationId: "generation" }, expiresAt: new Date(Date.now() + 60000).toISOString() };
}
