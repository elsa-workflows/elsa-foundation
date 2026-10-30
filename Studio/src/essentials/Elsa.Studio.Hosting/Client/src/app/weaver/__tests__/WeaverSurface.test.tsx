import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext, type StudioAgentPreparedInvocation } from "../../../sdk";
import type { AgentClient } from "../../agent/agentClient";
import type { AgentStreamEvent } from "../../agent/agentTypes";
import { WeaverSurface } from "../WeaverSurface";

describe("WeaverSurface", () => {
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

  it("does not re-subscribe a stream when stopped while the message is still sending", async () => {
    const api = readyApi();
    const client = readyClient();
    let resolveSend: ((value: { messageId: string; status: "pending"; streamUrl: string }) => void) | undefined;
    client.sendMessage = vi.fn(() => new Promise(resolve => { resolveSend = resolve; })) as AgentClient["sendMessage"];
    const subscribeStream = vi.fn(() => ({ close: () => {} })) as unknown as typeof import("../../agent/agentStream").subscribeAgentSessionStream;

    const { container, unmount } = render(
      <WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={client} subscribeStream={subscribeStream} />
    );

    await flushPromises();
    typeAndSend(container, "Add an activity");
    await flushPromises();

    // The turn is parked awaiting sendMessage; stop, then let the send resolve.
    flushSync(() => container.querySelector<HTMLButtonElement>(".weaver-stop")?.click());
    resolveSend?.({ messageId: "msg_1", status: "pending", streamUrl: "/_elsa/agent/sessions/agt_1/stream" });
    await flushPromises();

    expect(subscribeStream).not.toHaveBeenCalled();
    expect(container.querySelector(".weaver-send")).toBeTruthy();

    unmount();
  });

  it("cancels the turn server-side when stopped before the turn id is known", async () => {
    const api = readyApi();
    const client = readyClient();
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
    flushSync(() => emit?.({ type: "turn-started", turnId: "turn_9", maxSteps: 8 }));
    await flushPromises();

    expect(client.cancelTurn).toHaveBeenCalledWith("agt_1", "turn_9");

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
    expect(results).toEqual([expect.objectContaining({ requestId: "terminal-request", status: "failed" })]);
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
