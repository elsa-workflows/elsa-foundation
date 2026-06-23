import { describe, expect, it, vi } from "vitest";
import { createEndpointContext } from "../../../sdk";
import { subscribeAgentSessionStream } from "../agentStream";
import type { AgentStreamEvent } from "../agentTypes";

describe("agent stream", () => {
  it("normalizes backend SSE events", async () => {
    const events: AgentStreamEvent[] = [];
    const fetch = vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({ id: "evt_01", kind: "Started" })}\n\n`,
      `data: ${JSON.stringify({ id: "evt_02", kind: "MessageDelta", content: "Hello" })}\n\n`,
      `data: ${JSON.stringify({ id: "evt_03", kind: "Completed" })}\n\n`
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      createEndpointContext("https://foundation.example/"),
      "/_elsa/agent/sessions/agt_01/stream",
      event => events.push(event),
      error => { throw error; },
      { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "msg_01" });
    await flushPromises();

    expect(events).toEqual([
      { type: "message-started", messageId: "msg_01", role: "assistant" },
      { type: "message-delta", messageId: "msg_01", content: "Hello" },
      { type: "message-completed", messageId: "msg_01" }
    ]);
  });

  it("preserves documented Studio message identifiers", async () => {
    const events: AgentStreamEvent[] = [];
    const fetch = vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({ type: "message-delta", messageId: "msg_01", content: "Hello" })}\n\n`,
      `data: ${JSON.stringify({ type: "proposal-created", proposalId: "prop_01", messageId: "msg_01" })}\n\n`,
      `data: ${JSON.stringify({ type: "message-completed", messageId: "msg_01" })}\n\n`
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      createEndpointContext("https://foundation.example/"),
      "/_elsa/agent/sessions/agt_01/stream",
      event => events.push(event),
      error => { throw error; },
      { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "msg_01" });
    await flushPromises();

    expect(events).toEqual([
      { type: "message-delta", messageId: "msg_01", content: "Hello" },
      { type: "proposal-created", proposalId: "prop_01", messageId: "msg_01", proposal: undefined },
      { type: "message-completed", messageId: "msg_01" }
    ]);
  });

  it("normalizes Core Weaver stream events", async () => {
    const events: AgentStreamEvent[] = [];
    const fetch = vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({ id: "evt_01", type: "conversation.started", data: {} })}\n\n`,
      `data: ${JSON.stringify({ id: "evt_02", type: "assistant.delta", data: { content: "Hello" } })}\n\n`,
      `data: ${JSON.stringify({ id: "evt_03", type: "tool.result", data: { toolName: "workflow.getDefinition", summary: "Loaded workflow" } })}\n\n`,
      `data: ${JSON.stringify({ id: "evt_04", type: "conversation.completed", data: {} })}\n\n`
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      createEndpointContext("https://foundation.example/"),
      "/_elsa/agent/sessions/agt_01/stream",
      event => events.push(event),
      error => { throw error; },
      { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "msg_01" });
    await flushPromises();

    expect(events).toEqual([
      { type: "message-started", messageId: "msg_01", role: "assistant" },
      { type: "message-delta", messageId: "msg_01", content: "Hello" },
      { type: "progress", label: "Loaded workflow" },
      { type: "message-completed", messageId: "msg_01" }
    ]);
  });

  it("tolerates standard SSE event and id fields", async () => {
    const events: AgentStreamEvent[] = [];
    const fetch = vi.fn(async () => new Response(streamFrom([
      "event: assistant.delta\nid: evt_01\ndata: {\"content\":\"Hello\"}\n\n",
      "event: conversation.completed\nid: evt_02\ndata: {}\n\n"
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      createEndpointContext("https://foundation.example/"),
      "/_elsa/agent/sessions/agt_01/stream",
      event => events.push(event),
      error => { throw error; },
      { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "msg_01" });
    await flushPromises();

    expect(events).toEqual([
      { type: "message-delta", messageId: "msg_01", content: "Hello" },
      { type: "message-completed", messageId: "msg_01" }
    ]);
  });

  it("buffers multi-line data-only SSE frames until the frame separator", async () => {
    const events: AgentStreamEvent[] = [];
    const fetch = vi.fn(async () => new Response(streamFrom([
      "data: {\"type\":\"message-delta\",\n",
      "data: \"messageId\":\"msg_01\",\"content\":\"Hello\"}\n\n"
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      createEndpointContext("https://foundation.example/"),
      "/_elsa/agent/sessions/agt_01/stream",
      event => events.push(event),
      error => { throw error; },
      { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "msg_01" });
    await flushPromises();

    expect(events).toEqual([
      { type: "message-delta", messageId: "msg_01", content: "Hello" }
    ]);
  });
});

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

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}
