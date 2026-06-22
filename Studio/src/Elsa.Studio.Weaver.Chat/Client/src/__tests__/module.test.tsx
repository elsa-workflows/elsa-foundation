import React from "react";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioAiPromptRequest, StudioContributionRegistry } from "@elsa-workflows/studio-sdk";
import { parseSseChunk } from "../weaverClient";
import { register } from "../module";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("weaver chat module", () => {
  it("registers optional navigation, route, panel, and surface contributions", () => {
    const api = testApi();

    register(api);

    expect(api.navigation.list()).toEqual([expect.objectContaining({ id: "weaver", path: "/weaver" })]);
    expect(api.routes.list()).toEqual([expect.objectContaining({ id: "weaver-chat", path: "/weaver" })]);
    expect(api.panels.list()).toEqual([expect.objectContaining({ id: "weaver-chat", title: "Weaver" })]);
    expect(api.ai.surfaces.list()).toEqual([expect.objectContaining({ id: "weaver-chat", placement: "panel" })]);
  });

  it("renders unavailable state when backend capabilities are missing", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("Not found", { status: 404 })));
    const api = testApi();
    register(api);
    const Route = api.routes.list()[0].component;
    const host = document.createElement("div");
    const root = createRoot(host);

    root.render(<Route />);
    await waitFor(() => expect(host.textContent).toContain("Enable Elsa Server AI capabilities"));

    root.unmount();
  });

  it("parses server-sent chat events", () => {
    const event = parseSseChunk("event: assistant.delta\ndata: {\"type\":\"assistant.delta\",\"conversationId\":\"c1\",\"sequence\":2,\"timestamp\":\"2026-06-23T00:00:00Z\",\"data\":{\"content\":\"Hi\"}}");

    expect(event).toMatchObject({
      type: "assistant.delta",
      conversationId: "c1",
      data: { content: "Hi" }
    });
  });
});

function testApi(): ElsaStudioModuleApi {
  return {
    backend: {
      baseUrl: "https://server.example/",
      http: {
        getJson: async url => {
          const response = await fetch(new URL(url, "https://server.example/"));
          if (!response.ok) throw new Error(await response.text());
          return await response.json();
        },
        postJson: vi.fn()
      }
    },
    navigation: registry(),
    routes: registry(),
    panels: registry(),
    ai: {
      surfaces: registry(),
      contextProviders: registry(),
      promptActions: registry(),
      tools: registry(),
      proposalRenderers: registry(),
      dispatchPrompt: vi.fn(),
      onPrompt(listener: (request: StudioAiPromptRequest) => void) {
        void listener;
        return () => {};
      }
    }
  } as unknown as ElsaStudioModuleApi;
}

function registry<T>(): StudioContributionRegistry<T> {
  const items: T[] = [];
  return {
    add: item => items.push(item),
    list: () => [...items]
  };
}

async function waitFor(assertion: () => void) {
  for (let attempt = 0; attempt < 20; attempt++) {
    try {
      assertion();
      return;
    } catch {
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }

  assertion();
}
