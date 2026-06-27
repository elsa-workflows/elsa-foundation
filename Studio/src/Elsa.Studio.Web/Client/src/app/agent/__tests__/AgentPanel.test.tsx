import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
import { AgentPanel } from "../AgentPanel";
import type { AgentClient } from "../agentClient";
import type { AgentBootstrapResponse, AgentStreamEvent } from "../agentTypes";

describe("AgentPanel", () => {
  it("disables input until backend policy bootstrap completes", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    const client = stubClient();
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={client} onClose={() => {}} />);

    expect(container.querySelector<HTMLTextAreaElement>("#studio-agent-composer")?.disabled).toBe(true);

    unmount();
  });

  it("sends contextual prompts and renders streamed answers", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    api.agent.contextProviders.add({
      id: "screen.context",
      displayName: "Screen",
      surfaces: ["*"],
      sensitivity: "internal",
      collect: async () => [{
        id: "ctx_screen",
        source: "studio-shell",
        label: "Overview",
        contentType: "summary",
        sensitivity: "internal",
        scope: "screen"
      }]
    });
    api.agent.capabilities.add({
      id: "studio.explain",
      displayName: "Explain",
      description: "Explain the active screen.",
      kind: "answer",
      risk: "read-only",
      surfaces: ["*"]
    });
    api.agent.promptStarters.add({
      id: "studio.prompt",
      label: "Explain screen",
      prompt: "Explain this screen.",
      surfaces: ["*"],
      requiredCapabilities: ["studio.explain"]
    });
    const client = stubClient();
    const subscribeStream = vi.fn((_context, _streamUrl, onEvent: (event: AgentStreamEvent) => void) => {
      onEvent({ type: "message-started", messageId: "msg_01", role: "assistant" });
      onEvent({ type: "message-delta", messageId: "msg_01", content: "This screen shows Studio status." });
      onEvent({ type: "message-completed", messageId: "msg_01" });
      return { close() {} };
    });
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={client} subscribeStream={subscribeStream} onClose={() => {}} />);

    await waitForText(container, "Explain screen");
    clickButton(container, "Explain screen");
    await flushPromises();
    await flushPromises();

    expect(client.createSession).toHaveBeenCalledWith(expect.objectContaining({ activeSurface: { route: "/" }, mode: "explain" }));
    expect(client.sendMessage).toHaveBeenCalledWith("agt_01", expect.objectContaining({
      message: "Explain this screen.",
      mode: "explain",
      capabilityId: "studio.explain",
      contextAttachments: [expect.objectContaining({ id: "ctx_screen" })]
    }));
    expect(container.textContent).toContain("Overview");
    expect(container.textContent).toContain("This screen shows Studio status.");
    expect(subscribeStream).toHaveBeenCalledWith(
      expect.any(Object),
      "/_elsa/agent/sessions/agt_01/stream",
      expect.any(Function),
      expect.any(Function),
      { defaultMessageId: "msg_01" });

    unmount();
  });

  it("keeps input disabled while the assistant stream is still running", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    api.agent.capabilities.add({
      id: "studio.explain",
      displayName: "Explain",
      description: "Explain the active screen.",
      kind: "answer",
      risk: "read-only",
      surfaces: ["*"]
    });
    api.agent.promptStarters.add({
      id: "studio.prompt",
      label: "Explain screen",
      prompt: "Explain this screen.",
      surfaces: ["*"],
      requiredCapabilities: ["studio.explain"]
    });
    const client = stubClient();
    let emitStreamEvent: ((event: AgentStreamEvent) => void) | undefined;
    const subscribeStream = vi.fn((_context, _streamUrl, onEvent: (event: AgentStreamEvent) => void) => {
      emitStreamEvent = onEvent;
      onEvent({ type: "message-started", messageId: "msg_01", role: "assistant" });
      onEvent({ type: "message-delta", messageId: "msg_01", content: "Streaming..." });
      return { close() {} };
    });
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={client} subscribeStream={subscribeStream} onClose={() => {}} />);

    await waitForText(container, "Explain screen");
    clickButton(container, "Explain screen");
    await flushPromises();
    await flushPromises();
    clickButton(container, "Explain screen");

    expect(container.querySelector<HTMLTextAreaElement>("#studio-agent-composer")?.disabled).toBe(true);
    expect(client.sendMessage).toHaveBeenCalledTimes(1);

    emitStreamEvent?.({ type: "message-completed", messageId: "msg_01" });
    await flushPromises();

    expect(container.querySelector<HTMLTextAreaElement>("#studio-agent-composer")?.disabled).toBe(false);

    unmount();
  });

  it("shows disabled state when the backend reports the agent unavailable", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    const client = stubClient({
      enabled: false,
      providerStatus: "unavailable",
      modes: [],
      capabilities: [],
      policy: { contextVisibility: true, requiresApprovalForMutations: true }
    });
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={client} onClose={() => {}} />);

    await waitForText(container, "Weaver features are disabled or unavailable.");

    expect(container.textContent).toContain("Weaver features are disabled or unavailable.");
    expect(container.querySelector<HTMLTextAreaElement>("#studio-agent-composer")?.disabled).toBe(true);

    unmount();
  });

  it("hides prompt starters for capabilities not allowed by backend bootstrap", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    api.agent.capabilities.add({
      id: "workflow.propose-change",
      displayName: "Propose change",
      description: "Create workflow proposal.",
      kind: "proposal",
      risk: "review-required",
      surfaces: ["*"]
    });
    api.agent.promptStarters.add({
      id: "workflow.prompt",
      label: "Improve workflow",
      prompt: "Suggest workflow changes.",
      surfaces: ["*"],
      requiredCapabilities: ["workflow.propose-change"]
    });
    const client = stubClient({
      enabled: true,
      providerStatus: "available",
      modes: ["explain"],
      capabilities: [],
      policy: { contextVisibility: true, requiresApprovalForMutations: true }
    });
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={client} onClose={() => {}} />);

    await flushPromises();

    expect(container.textContent).not.toContain("Improve workflow");
    expect(container.querySelector<HTMLTextAreaElement>("#studio-agent-composer")?.disabled).toBe(true);

    unmount();
  });

  it("renders proposal review when the stream only sends a proposal id", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    api.agent.promptStarters.add({
      id: "workflow.prompt",
      label: "Improve workflow",
      prompt: "Suggest workflow changes.",
      surfaces: ["*"],
      requiredCapabilities: ["workflow.propose-change"]
    });
    api.agent.capabilities.add({
      id: "workflow.explain",
      displayName: "Explain",
      description: "Explain workflow.",
      kind: "answer",
      risk: "read-only",
      surfaces: ["*"]
    });
    api.agent.capabilities.add({
      id: "workflow.propose-change",
      displayName: "Propose change",
      description: "Create workflow proposal.",
      kind: "proposal",
      risk: "review-required",
      surfaces: ["*"]
    });
    const client = stubClient();
    const subscribeStream = vi.fn((_context, _streamUrl, onEvent: (event: AgentStreamEvent) => void) => {
      onEvent({ type: "proposal-created", proposalId: "prop_01", messageId: "msg_01" });
      onEvent({ type: "message-completed", messageId: "msg_01" });
      return { close() {} };
    });
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={client} subscribeStream={subscribeStream} onClose={() => {}} />);

    await flushPromises();
    clickButton(container, "Improve workflow");
    await flushPromises();
    await flushPromises();

    expect(container.textContent).toContain("Weaver proposal");
    expect(container.textContent).toContain("Approve");
    expect(Array.from(container.querySelectorAll("button")).find(button => button.textContent?.includes("Approve"))?.disabled).toBe(true);
    expect(client.sendMessage).toHaveBeenCalledWith("agt_01", expect.objectContaining({ capabilityId: "workflow.propose-change" }));

    unmount();
  });

  it("normalizes partial streamed proposal payloads before rendering actions", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    api.agent.promptStarters.add({
      id: "workflow.prompt",
      label: "Create proposal",
      prompt: "Create a proposal.",
      surfaces: ["*"],
      requiredCapabilities: ["workflow.propose-change"]
    });
    api.agent.capabilities.add({
      id: "workflow.propose-change",
      displayName: "Propose change",
      description: "Create workflow proposal.",
      kind: "proposal",
      risk: "review-required",
      surfaces: ["*"]
    });
    const client = stubClient();
    const subscribeStream = vi.fn((_context, _streamUrl, onEvent: (event: AgentStreamEvent) => void) => {
      onEvent({
        type: "proposal-created",
        proposalId: "prop_01",
        messageId: "msg_01",
        proposal: { title: "Add timeout", summary: "Adds timeout handling.", baseRevision: "rev_01", operations: [{ op: "add-activity" }] }
      });
      onEvent({ type: "message-completed", messageId: "msg_01" });
      return { close() {} };
    });
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={client} subscribeStream={subscribeStream} onClose={() => {}} />);

    await flushPromises();
    clickButton(container, "Create proposal");
    await flushPromises();
    await flushPromises();
    clickButton(container, "Approve");
    clickButton(container, "Approve");

    expect(container.textContent).toContain("Add timeout");
    expect(client.approveProposal).toHaveBeenCalledTimes(1);
    expect(client.approveProposal).toHaveBeenCalledWith("prop_01", { revision: "rev_01" });

    unmount();
  });

  it("publishes session indicator state for pending proposals", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    registerProposalPrompt(api);
    const client = stubClient(policyBootstrap({ actorId: "studio-user" }));
    const onSessionIndicatorChange = vi.fn();
    const subscribeStream = vi.fn((_context, _streamUrl, onEvent: (event: AgentStreamEvent) => void) => {
      onEvent({
        type: "proposal-created",
        proposalId: "prop_01",
        messageId: "msg_01",
        proposal: {
          title: "Rename workflow",
          summary: "Renames the workflow.",
          status: "awaiting-approval",
          baseRevision: "rev_01",
          toolId: "workflow.rename",
          invocationMode: "proposal",
          resourceTarget: { resourceType: "workflow-definition", resourceId: "wf_01" }
        }
      });
      onEvent({ type: "message-completed", messageId: "msg_01" });
      return { close() {} };
    });
    const { container, unmount } = render(
      <AgentPanel
        api={api}
        surface={{ route: "/" }}
        client={client}
        subscribeStream={subscribeStream}
        onClose={() => {}}
        onSessionIndicatorChange={onSessionIndicatorChange}
      />
    );

    await flushPromises();
    clickButton(container, "Create proposal");
    await flushPromises();
    await flushPromises();

    expect(onSessionIndicatorChange).toHaveBeenCalledWith([expect.objectContaining({
      id: "agt_01",
      status: "waiting",
      pendingProposals: 1
    })]);

    unmount();
  });

  it("blocks proposal execution when Host Policy denies the tool", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    registerProposalPrompt(api);
    const client = stubClient(policyBootstrap({ actorId: "studio-user", deniedToolIds: ["workflow.rename"] }));
    const subscribeStream = vi.fn((_context, _streamUrl, onEvent: (event: AgentStreamEvent) => void) => {
      onEvent({
        type: "proposal-created",
        proposalId: "prop_01",
        messageId: "msg_01",
        proposal: {
          title: "Rename workflow",
          summary: "Renames the workflow.",
          status: "approved",
          baseRevision: "rev_01",
          toolId: "workflow.rename",
          invocationMode: "proposal",
          resourceTarget: { resourceType: "workflow-definition", resourceId: "wf_01", displayName: "Order workflow" }
        }
      });
      onEvent({ type: "message-completed", messageId: "msg_01" });
      return { close() {} };
    });
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={client} subscribeStream={subscribeStream} onClose={() => {}} />);

    await flushPromises();
    clickButton(container, "Create proposal");
    await flushPromises();
    await flushPromises();
    clickButton(container, "Apply");
    await flushPromises();

    expect(client.executeProposal).not.toHaveBeenCalled();
    expect(container.textContent).toContain("Host Policy denied tool workflow.rename.");
    expect(container.textContent).toContain("denied");
    expect(container.textContent).toContain("studio-user");

    unmount();
  });

  it("records failed audit outcome when proposal execution fails", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    registerProposalPrompt(api);
    const client = stubClient(policyBootstrap({ actorId: "studio-user" }));
    client.executeProposal = vi.fn(async () => {
      throw new Error("Backend failed.");
    });
    const subscribeStream = vi.fn((_context, _streamUrl, onEvent: (event: AgentStreamEvent) => void) => {
      onEvent({
        type: "proposal-created",
        proposalId: "prop_01",
        messageId: "msg_01",
        proposal: {
          title: "Rename workflow",
          summary: "Renames the workflow.",
          status: "approved",
          baseRevision: "rev_01",
          toolId: "workflow.rename",
          invocationMode: "proposal",
          resourceTarget: { resourceType: "workflow-definition", resourceId: "wf_01", displayName: "Order workflow" }
        }
      });
      onEvent({ type: "message-completed", messageId: "msg_01" });
      return { close() {} };
    });
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={client} subscribeStream={subscribeStream} onClose={() => {}} />);

    await flushPromises();
    clickButton(container, "Create proposal");
    await flushPromises();
    await flushPromises();
    clickButton(container, "Apply");
    await flushPromises();
    await flushPromises();

    expect(client.executeProposal).toHaveBeenCalledWith("prop_01", { revision: "rev_01" });
    expect(container.textContent).toContain("Backend failed.");
    expect(container.textContent).toContain("failed");
    expect(container.textContent).toContain("workflow.rename");
    expect(container.textContent).toContain("allowed");

    unmount();
  });
});

function stubClient(bootstrap = {
  enabled: true,
  providerStatus: "available" as const,
  modes: ["explain" as const],
  capabilities: [
    {
      id: "studio.explain",
      displayName: "Explain",
      description: "Explain the active screen.",
      kind: "answer" as const,
      risk: "read-only" as const,
      surfaces: ["*"]
    },
    {
      id: "workflow.explain",
      displayName: "Explain",
      description: "Explain workflow.",
      kind: "answer" as const,
      risk: "read-only" as const,
      surfaces: ["*"]
    },
    {
      id: "workflow.propose-change",
      displayName: "Propose change",
      description: "Create workflow proposal.",
      kind: "proposal" as const,
      risk: "review-required" as const,
      surfaces: ["*"]
    }
  ],
  policy: { contextVisibility: true, requiresApprovalForMutations: true }
}): AgentClient {
  return {
    bootstrap: vi.fn(async () => bootstrap),
    createSession: vi.fn(async () => ({
      sessionId: "agt_01",
      status: "active",
      contextAttachments: []
    })),
    sendMessage: vi.fn(async () => ({
      messageId: "msg_01",
      status: "pending",
      streamUrl: "/_elsa/agent/sessions/agt_01/stream"
    })),
    approveProposal: vi.fn(async proposalId => ({ proposalId, approvalStatus: "approved" })),
    denyProposal: vi.fn(async proposalId => ({ proposalId, approvalStatus: "denied" })),
    executeProposal: vi.fn(async proposalId => ({ proposalId, approvalStatus: "executed" })),
    submitFeedback: vi.fn()
  };
}

function registerProposalPrompt(api: ReturnType<typeof createStudioRegistry>) {
  api.agent.promptStarters.add({
    id: "workflow.prompt",
    label: "Create proposal",
    prompt: "Create a proposal.",
    surfaces: ["*"],
    requiredCapabilities: ["workflow.propose-change"]
  });
  api.agent.capabilities.add({
    id: "workflow.propose-change",
    displayName: "Propose change",
    description: "Create workflow proposal.",
    kind: "proposal",
    risk: "review-required",
    surfaces: ["*"]
  });
}

function policyBootstrap(policy: Partial<AgentBootstrapResponse["policy"]>) {
  return {
    enabled: true,
    providerStatus: "available" as const,
    modes: ["explain" as const],
    capabilities: [{
      id: "workflow.propose-change",
      displayName: "Propose change",
      description: "Create workflow proposal.",
      kind: "proposal" as const,
      risk: "review-required" as const,
      surfaces: ["*"]
    }],
    policy: {
      contextVisibility: true,
      requiresApprovalForMutations: true,
      ...policy
    }
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

function clickButton(container: HTMLElement, text: string) {
  const button = Array.from(container.querySelectorAll("button")).find(candidate => candidate.textContent?.includes(text));
  if (!button) {
    throw new Error(`Button not found: ${text}`);
  }

  button.click();
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}

async function waitForText(container: Element, text: string) {
  for (let i = 0; i < 20; i++) {
    if (container.textContent?.includes(text)) return;
    await flushPromises();
  }

  throw new Error(`Timed out waiting for text: ${text}`);
}
