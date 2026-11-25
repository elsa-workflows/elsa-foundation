import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

// Keep the hub connected while each host supplies its own recent lines.
vi.mock("@microsoft/signalr", async importOriginal => {
  const actual = await importOriginal<typeof import("@microsoft/signalr")>();
  class TestHubConnectionBuilder {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    build() {
      return {
        state: actual.HubConnectionState.Connected,
        onreconnecting() {},
        onreconnected() {},
        onclose() {},
        start: async () => {},
        stream: () => ({ subscribe: () => ({ dispose() {} }) }),
        stop: async () => {}
      };
    }
  }
  return { ...actual, HubConnectionBuilder: TestHubConnectionBuilder };
});

const { ConsoleStreamPage, ConsoleStreamPanel, register } = await import("../module");

describe("console host switch", () => {
  it.each([["page", ConsoleStreamPage], ["docked panel", ConsoleStreamPanel]] as const)("starts the %s on Server and preserves Pause when switching to Studio", async (_, View) => {
    const hostGetJson = vi.fn(async (url: string) => url.endsWith("/sources") ? [] : {
      items: [{ id: "studio-line", text: "Studio replay after host switch." }]
    });
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

    const hostButtons = Array.from(container.querySelectorAll<HTMLButtonElement>("[aria-label='Console host'] button"));
    const pressed = hostButtons.filter(button => button.getAttribute("aria-pressed") === "true").map(button => button.textContent);
    expect(pressed).toEqual(["Server"]);
    expect(container.textContent).toContain("Server console");
    expect(backendGetJson).toHaveBeenCalledWith("/_elsa/server/diagnostics/console-logs/sources");
    expect(hostGetJson).not.toHaveBeenCalled();

    const pause = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Pause")!;
    const studio = hostButtons.find(button => button.textContent === "Studio")!;
    flushSync(() => pause.click());
    flushSync(() => studio.click());
    await vi.waitFor(() => expect(container.textContent).toContain("1 buffered while paused"));
    expect(pause.getAttribute("aria-pressed")).toBe("true");
    expect(container.textContent).not.toContain("Studio replay after host switch.");
    flushSync(() => pause.click());
    expect(container.textContent).toContain("Studio replay after host switch.");

    flushSync(() => root.unmount());
  });
});
