import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

// The view opens a SignalR hub on mount; a connection that never starts keeps the test on the initial render.
vi.mock("@microsoft/signalr", async importOriginal => {
  const actual = await importOriginal<typeof import("@microsoft/signalr")>();
  class PendingHubConnectionBuilder {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    build() {
      return {
        state: actual.HubConnectionState.Disconnected,
        onreconnecting() {},
        onreconnected() {},
        onclose() {},
        start: () => new Promise<void>(() => {}),
        stop: async () => {}
      };
    }
  }
  return { ...actual, HubConnectionBuilder: PendingHubConnectionBuilder };
});

const { ConsoleStreamPage, ConsoleStreamPanel, register } = await import("../module");

describe("console host switch", () => {
  it.each([["page", ConsoleStreamPage], ["docked panel", ConsoleStreamPanel]] as const)("opens the %s on the Server host", async (_, View) => {
    const hostGetJson = vi.fn(async () => []);
    const backendGetJson = vi.fn(async () => []);
    register({
      host: { baseUrl: "https://studio.example/", http: { getJson: hostGetJson } },
      backend: { baseUrl: "https://server.example/", http: { getJson: backendGetJson } },
      navigation: { add: () => {} },
      routes: { add: () => {} },
      panels: { add: () => {} }
    } as unknown as ElsaStudioModuleApi);

    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<View />));
    await new Promise(resolve => setTimeout(resolve, 0));

    const hostButtons = Array.from(container.querySelectorAll("[aria-label='Console host'] button"));
    const pressed = hostButtons.filter(button => button.getAttribute("aria-pressed") === "true").map(button => button.textContent);
    expect(pressed).toEqual(["Server"]);
    expect(container.textContent).toContain("Server console");
    expect(backendGetJson).toHaveBeenCalledWith("/_elsa/server/diagnostics/console-logs/sources");
    expect(hostGetJson).not.toHaveBeenCalled();

    flushSync(() => root.unmount());
  });
});
