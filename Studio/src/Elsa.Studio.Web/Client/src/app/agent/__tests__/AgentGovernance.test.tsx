import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
import { AgentContextChips } from "../AgentContextChips";
import { AgentPanel } from "../AgentPanel";
import type { AgentClient } from "../agentClient";

describe("agent governance UI", () => {
  it("keeps Studio usable while showing policy-disabled assistant state", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    const { container, unmount } = render(<AgentPanel api={api} surface={{ route: "/" }} client={disabledClient()} onClose={() => {}} />);

    await flushPromises();

    expect(container.textContent).toContain("Weaver features are disabled or unavailable.");
    expect(container.querySelector("#studio-agent-composer")).toHaveProperty("disabled", true);

    unmount();
  });

  it("labels redacted context attachments", () => {
    const { container, unmount } = render(<AgentContextChips attachments={[{
      id: "ctx_secret",
      source: "settings",
      label: "Connection string",
      contentType: "summary",
      sensitivity: "secret-redacted",
      scope: "screen"
    }]} />);

    expect(container.textContent).toContain("Connection string");
    expect(container.textContent).toContain("redacted");

    unmount();
  });
});

function disabledClient(): AgentClient {
  return {
    bootstrap: vi.fn(async () => ({
      enabled: false,
      providerStatus: "disabled",
      modes: [],
      capabilities: [],
      policy: { contextVisibility: true, requiresApprovalForMutations: true }
    })),
    createSession: vi.fn(),
    sendMessage: vi.fn(),
    approveProposal: vi.fn(),
    denyProposal: vi.fn(),
    executeProposal: vi.fn(),
    submitFeedback: vi.fn()
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
  await new Promise(resolve => setTimeout(resolve, 0));
}
