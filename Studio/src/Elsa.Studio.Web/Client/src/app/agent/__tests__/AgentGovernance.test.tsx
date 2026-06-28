import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
import { AgentContextChips } from "../AgentContextChips";
import type { AgentClient } from "../agentClient";
import { WeaverSurface } from "../../weaver/WeaverSurface";

describe("agent governance UI", () => {
  it("keeps Studio usable while showing policy-disabled assistant state", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");
    const { container, unmount } = render(<WeaverSurface api={api} surface={{ route: "/" }} variant="dock" client={disabledClient()} onClose={() => {}} />);

    await waitForText(container, "Weaver is unavailable.");

    expect(container.textContent).toContain("Studio remains fully usable.");
    const composer = container.querySelector<HTMLTextAreaElement>("textarea");
    expect(composer?.disabled).toBe(true);

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
      providers: [],
      policy: { contextVisibility: true, requiresApprovalForMutations: true }
    })),
    createSession: vi.fn(),
    sendMessage: vi.fn(),
    approveProposal: vi.fn(),
    denyProposal: vi.fn(),
    executeProposal: vi.fn(),
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
  await new Promise(resolve => setTimeout(resolve, 0));
}

async function waitForText(container: Element, text: string) {
  for (let i = 0; i < 20; i++) {
    if (container.textContent?.includes(text)) return;
    await flushPromises();
  }

  throw new Error(`Timed out waiting for text: ${text}`);
}
