import React from "react";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioAiPromptRequest, StudioContributionRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getWeaverCapabilities, listWeaverTools, parseSseChunk, streamWeaverChat } from "../weaverClient";
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

  it("maps current agent bootstrap responses to Weaver capabilities and tools", async () => {
    const context = agentContext();

    await expect(getWeaverCapabilities(context)).resolves.toMatchObject({
      streaming: true,
      conversationPersistence: true,
      proposalReview: true,
      agents: [{ name: "deterministic-workflow-authoring" }]
    });
    await expect(listWeaverTools(context, "deterministic-workflow-authoring")).resolves.toEqual([
      expect.objectContaining({ name: "deterministic-workflow-authoring.chat", mutability: "read-only" }),
      expect.objectContaining({ name: "deterministic-workflow-authoring.streaming", mutability: "read-only" }),
      expect.objectContaining({ name: "deterministic-workflow-authoring.tool-approval", mutability: "proposal" })
    ]);
  });

  it("streams chat through current agent session endpoints", async () => {
    const context = agentContext();
    const events: unknown[] = [];
    vi.stubGlobal("fetch", vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({ Kind: 0, Id: "evt_1" })}\n\n`,
      `data: ${JSON.stringify({ Kind: 1, Id: "evt_1", Content: "Hello from agent." })}\n\n`,
      `data: ${JSON.stringify({ Kind: 4, Id: "evt_1" })}\n\n`
    ]))));

    await streamWeaverChat(context, { message: "Hello", agent: "deterministic-workflow-authoring" }, event => events.push(event));

    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/sessions", expect.objectContaining({
      providerId: "deterministic-workflow-authoring"
    }));
    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/sessions/agt_01/messages", expect.objectContaining({
      content: "Hello",
      message: "Hello"
    }));
    expect(fetch).toHaveBeenCalledWith("https://server.example/_elsa/agent/sessions/agt_01/stream", expect.objectContaining({
      headers: { Accept: "text/event-stream" }
    }));
    expect(events).toEqual([
      expect.objectContaining({ type: "assistant.delta", conversationId: "agt_01", data: { content: "Hello from agent." } }),
      expect.objectContaining({ type: "conversation.completed", conversationId: "agt_01" })
    ]);
  });

  it("normalizes workflow prompt attachments to backend policy context kinds", async () => {
    const context = agentContext();
    vi.stubGlobal("fetch", vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({ Kind: 4, Id: "evt_1" })}\n\n`
    ]))));

    await streamWeaverChat(context, {
      message: "Explain this workflow executable.",
      agent: "deterministic-workflow-authoring",
      attachments: [{
        kind: "workflow-executable",
        referenceId: "artifact-1",
        metadata: {
          artifactId: "artifact-1",
          definitionId: "definition-1",
          sourceId: "version-1"
        }
      }]
    }, () => {});

    expect(context.http.postJson).toHaveBeenCalledWith("/_elsa/agent/sessions/agt_01/messages", expect.objectContaining({
      contextAttachments: [expect.objectContaining({
        id: "workflow.execution:artifact-1",
        kind: "workflow.execution",
        references: expect.objectContaining({
          source: "workflow",
          sourceId: "artifact-1",
          workflowDefinitionId: "definition-1",
          workflowVersionId: "version-1"
        })
      })]
    }));
  });

  it("automatically submits enqueued prompt actions", async () => {
    const backend = agentContext();
    vi.stubGlobal("fetch", vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({ Kind: 0, Id: "evt_1" })}\n\n`,
      `data: ${JSON.stringify({ Kind: 1, Id: "evt_1", Content: "Use a concise workflow name." })}\n\n`,
      `data: ${JSON.stringify({ Kind: 4, Id: "evt_1" })}\n\n`
    ]))));
    const api = testApi(backend);
    register(api);
    const Panel = api.panels.list()[0].component;
    const host = document.createElement("div");
    const root = createRoot(host);

    root.render(<Panel />);
    await waitFor(() => expect(host.textContent).toContain("1 agent(s)"));

    api.ai.dispatchPrompt({
      message: "Suggest a workflow name.",
      mode: "enqueue",
      source: { label: "Suggest workflow metadata" }
    });

    await waitFor(() => expect(backend.http.postJson).toHaveBeenCalledWith("/_elsa/agent/sessions/agt_01/messages", expect.objectContaining({
      content: "Suggest a workflow name.",
      message: "Suggest a workflow name."
    })));
    await waitFor(() => expect(host.textContent).toContain("Use a concise workflow name."));

    root.unmount();
  });

  it("keeps prompt actions dispatched before the Weaver surface mounts", async () => {
    const backend = agentContext();
    vi.stubGlobal("fetch", vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({ Kind: 0, Id: "evt_1" })}\n\n`,
      `data: ${JSON.stringify({ Kind: 1, Id: "evt_1", Content: "Executable summary." })}\n\n`,
      `data: ${JSON.stringify({ Kind: 4, Id: "evt_1" })}\n\n`
    ]))));
    const api = testApi(backend);
    register(api);

    api.ai.dispatchPrompt({
      message: "Explain this executable.",
      mode: "enqueue",
      source: { label: "Explain executable" }
    });

    const Panel = api.panels.list()[0].component;
    const host = document.createElement("div");
    const root = createRoot(host);
    root.render(<Panel />);

    await waitFor(() => expect(backend.http.postJson).toHaveBeenCalledWith("/_elsa/agent/sessions/agt_01/messages", expect.objectContaining({
      content: "Explain this executable.",
      message: "Explain this executable."
    })));
    await waitFor(() => expect(host.textContent).toContain("Executable summary."));

    root.unmount();
  });
});

function testApi(backend: StudioEndpointContext = {
  baseUrl: "https://server.example/",
  http: {
    getJson: async url => {
      const response = await fetch(new URL(url, "https://server.example/"));
      if (!response.ok) throw new Error(await response.text());
      return await response.json();
    },
    postJson: vi.fn()
  }
}): ElsaStudioModuleApi {
  const listeners = new Set<(request: StudioAiPromptRequest) => void>();

  return {
    backend,
    navigation: registry(),
    routes: registry(),
    panels: registry(),
    ai: {
      surfaces: registry(),
      contextProviders: registry(),
      promptActions: registry(),
      tools: registry(),
      proposalRenderers: registry(),
      dispatchPrompt(request: StudioAiPromptRequest) {
        for (const listener of listeners) {
          listener(request);
        }
      },
      onPrompt(listener: (request: StudioAiPromptRequest) => void) {
        listeners.add(listener);
        return () => listeners.delete(listener);
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

function agentContext(): StudioEndpointContext {
  return {
    baseUrl: "https://server.example/",
    http: {
      getJson: vi.fn(async () => agentBootstrap()),
      postJson: vi.fn(async url => {
        if (url === "/_elsa/agent/sessions") return { data: { sessionId: "agt_01", status: "active" } };
        return { data: { message: { id: "msg_01" } } };
      })
    }
  };
}

function agentBootstrap() {
  return {
    data: {
      enabled: true,
      providerStatus: "available",
      capabilities: [{ id: "workflow.propose-change", displayName: "Propose change", risk: "review-required", requiresApproval: true }],
      providers: [{
        providerId: "deterministic-workflow-authoring",
        isAvailable: true,
        status: "Available",
        supportedOperations: ["chat", "streaming", "tool-approval"],
        riskProfile: "review-required"
      }],
      policy: { requiresApprovalForMutations: true }
    },
    error: null
  };
}

function streamFrom(chunks: string[]) {
  const encoder = new TextEncoder();
  return new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) {
        controller.enqueue(encoder.encode(chunk));
      }

      controller.close();
    }
  });
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
