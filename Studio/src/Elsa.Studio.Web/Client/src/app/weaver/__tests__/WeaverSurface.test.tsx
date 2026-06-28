import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
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
      providers: [{ providerId: "deterministic-test", isAvailable: true, status: "ok", providerKind: "provider-sdk-binding", supportedOperations: ["chat"], riskProfile: "read-only", metadata: {} }],
      policy: { contextVisibility: true, requiresApprovalForMutations: true }
    })),
    createSession: vi.fn(async () => ({ sessionId: "agt_1", status: "active", contextAttachments: [] })),
    sendMessage: vi.fn(async () => ({ messageId: "msg_1", status: "pending", streamUrl: "/_elsa/agent/sessions/agt_1/stream" })),
    approveProposal: vi.fn(async proposalId => ({ proposalId, approvalStatus: "approved" })),
    denyProposal: vi.fn(async proposalId => ({ proposalId, approvalStatus: "denied" })),
    executeProposal: vi.fn(async proposalId => ({ proposalId, approvalStatus: "executed" })),
    submitFeedback: vi.fn(),
    cancelTurn: vi.fn()
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
