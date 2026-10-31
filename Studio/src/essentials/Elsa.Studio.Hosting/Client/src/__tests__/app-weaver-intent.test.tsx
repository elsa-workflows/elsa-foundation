import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "../app/App";
import * as loader from "../app/loader";
import * as clients from "../app/agent/agentClient";
import * as streams from "../app/agent/agentStream";
import type { ElsaStudioModuleApi } from "../sdk";

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  delete window.__ELSA_STUDIO_RUNTIME__;
  document.body.innerHTML = "";
});

describe("shell-owned Weaver intent", () => {
  it.each(["same dock", "close/reopen", "registry reconciliation"])("does not repeat consumed intent after %s, and admits a new request", async transition => {
    const registries: ElsaStudioModuleApi[] = [];
    let releaseRegistry: (() => void) | undefined;
    vi.spyOn(loader, "loadStudioModules").mockImplementation(async (_modules, api) => {
      registries.push(api);
      api.agent.capabilities.add({ id: "sample.inspect", displayName: "Inspect", description: "Read the selected resource.", kind: "answer", risk: "read-only", surfaces: ["*"] });
      if (registries.length === 2) await new Promise<void>(resolve => { releaseRegistry = resolve; });
      return [];
    });
    const client: clients.AgentClient = {
      bootstrap: vi.fn<clients.AgentClient["bootstrap"]>(async () => ({ enabled: true, providerStatus: "available", modes: ["explain"], capabilities: [{ id: "sample.inspect", displayName: "Inspect", description: "Read.", kind: "answer", risk: "read-only", surfaces: ["*"], requiredPermissions: [] }], provider: { providerId: "protocol", isAvailable: true, status: "available", providerKind: "provider-sdk-binding", supportedOperations: ["chat"], riskProfile: "read-only", metadata: {} }, policy: { contextVisibility: true, defaultAutonomyMode: "auto-read-only", maxAutonomyMode: "full-auto", allowedAutonomyModes: ["manual", "auto-read-only", "full-auto"] } })),
      createSession: vi.fn<clients.AgentClient["createSession"]>(async () => ({ sessionId: crypto.randomUUID(), status: "active", contextAttachments: [] })),
      sendMessage: vi.fn<clients.AgentClient["sendMessage"]>(async session => ({ messageId: "message", status: "pending", streamUrl: `/_elsa/agent/sessions/${session}/stream` })),
      approveProposal: vi.fn(), denyProposal: vi.fn(), executeProposal: vi.fn(), acknowledgeClient: vi.fn(), submitFeedback: vi.fn(), cancelTurn: vi.fn()
    };
    vi.spyOn(clients, "createAgentClient").mockReturnValue(client);
    vi.spyOn(streams, "subscribeAgentSessionStream").mockImplementation((_context, _url, emit) => {
      emit({ type: "message-completed", messageId: "message" });
      return { close() {} };
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/_elsa/studio/modules")) return Response.json({ hostVersion: "1.0.0", sdkVersion: "1.0.0", modules: [], diagnostics: [] });
      if (url.includes("/_elsa/module-management/registry")) return Response.json({ modules: [], diagnostics: [] });
      return Response.json({ status: "Healthy" });
    }));
    const container = document.createElement("div"); document.body.appendChild(container);
    const root = createRoot(container);
    flushSync(() => root.render(<App />));
    try {
      await vi.waitFor(() => expect(registries).toHaveLength(1));
      await settle();
      flushSync(() => registries[0].agent.dispatchPrompt({ requestId: "first-request", message: "Inspect the selected resource.", mode: "enqueue" }));
      await vi.waitFor(() => expect(client.sendMessage).toHaveBeenCalledTimes(1));
      await settle();
      if (transition === "close/reopen") {
        flushSync(() => container.querySelector<HTMLButtonElement>('button[aria-label="Close assistant"]')!.click());
        expect(container.querySelector('[aria-label="Weaver assistant"]')).toBeNull();
        flushSync(() => container.querySelector<HTMLButtonElement>(".agent-launcher")!.click());
      } else if (transition === "registry reconciliation") {
        flushSync(() => window.dispatchEvent(new Event("elsa-studio:modules-changed")));
        await vi.waitFor(() => expect(releaseRegistry).toBeTypeOf("function"));
        await vi.waitFor(() => expect(container.querySelector('[aria-label="Weaver assistant"]')).toBeNull());
        releaseRegistry!();
      }
      await vi.waitFor(() => expect(container.querySelector('[aria-label="Weaver assistant"]')).not.toBeNull());
      await settle();
      expect(client.sendMessage).toHaveBeenCalledTimes(1);
      flushSync(() => registries.at(-1)!.agent.dispatchPrompt({ requestId: "fresh-request", message: "Inspect again on request.", mode: "enqueue" }));
      await vi.waitFor(() => expect(client.sendMessage).toHaveBeenCalledTimes(2));
      expect(vi.mocked(client.sendMessage).mock.calls.map(([, body]) => body.requestId)).toEqual(["first-request", "fresh-request"]);
    } finally {
      releaseRegistry?.();
      flushSync(() => root.unmount()); container.remove();
    }
  });
});

async function settle() {
  for (let index = 0; index < 12; index++) await new Promise(resolve => setTimeout(resolve, 0));
}
