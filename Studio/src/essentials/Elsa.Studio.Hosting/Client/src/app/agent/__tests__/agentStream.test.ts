import { describe, expect, it, vi } from "vitest";
import { createEndpointContext } from "../../../sdk";
import { subscribeAgentSessionStream } from "../agentStream";
import type { AgentStreamEvent } from "../agentTypes";

describe("agent stream", () => {
  it("attaches endpoint access tokens to stream requests", async () => {
    const fetch = vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({ type: "message-completed", messageId: "msg_01" })}\n\n`
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      {
        ...createEndpointContext("https://foundation.example/", { headers: { "X-Elsa-Tenant": "tenant-a" } }),
        accessTokenFactory: vi.fn(async () => "access-token-1")
      },
      "/_elsa/agent/sessions/agt_01/stream",
      () => {},
      error => { throw error; },
      { fetch: fetch as typeof globalThis.fetch });
    await flushPromises();

    const init = fetch.mock.calls[0]?.[1] as RequestInit;
    const headers = new Headers(init.headers);
    expect(headers.get("Accept")).toBe("text/event-stream, application/x-ndjson, application/json");
    expect(headers.get("Authorization")).toBe("Bearer access-token-1");
    expect(headers.get("X-Elsa-Tenant")).toBe("tenant-a");
    expect(init.credentials).toBe("include");
  });

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

  it("normalizes typed workflow graph operation batch events with numeric enums", async () => {
    const events: AgentStreamEvent[] = [];
    const fetch = vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({
        Id: "evt_batch",
        Kind: 7,
        ResultKind: 2,
        Payload: {
          SchemaVersion: "elsa.workflow-graph-operation-batch.v1",
          WorkflowDefinitionId: "workflow-1",
          BaseRevision: "rev-1",
          Operations: [
            {
              Id: "op-add",
              Kind: 0,
              Parameters: { activityId: "temp:activity:email", activityType: "Elsa.Email.SendEmail" },
              TemporaryReferences: ["temp:activity:email"],
              Summary: "Add email."
            }
          ],
          Metadata: { source: "deterministic-workflow-authoring" }
        }
      })}\n\n`
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      createEndpointContext("https://foundation.example/"),
      "/_elsa/agent/sessions/agt_01/stream",
      event => events.push(event),
      error => { throw error; },
      { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "msg_01" });
    await flushPromises();

    expect(events).toEqual([
      {
        type: "workflow-batch-created",
        messageId: "msg_01",
        batch: {
          schemaVersion: "elsa.workflow-graph-operation-batch.v1",
          workflowDefinitionId: "workflow-1",
          baseRevision: "rev-1",
          operations: [
            {
              id: "op-add",
              kind: "add-activity",
              parameters: { activityId: "temp:activity:email", activityType: "Elsa.Email.SendEmail" },
              temporaryReferences: ["temp:activity:email"],
              summary: "Add email."
            }
          ],
          metadata: { source: "deterministic-workflow-authoring" }
        }
      }
    ]);
  });

  it("normalizes PascalCase typed error events", async () => {
    const events: AgentStreamEvent[] = [];
    const fetch = vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({
        Id: "evt_error",
        Kind: 5,
        ResultKind: 4,
        Error: { Message: "Workflow draft is stale." }
      })}\n\n`
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      createEndpointContext("https://foundation.example/"),
      "/_elsa/agent/sessions/agt_01/stream",
      event => events.push(event),
      error => { throw error; },
      { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "msg_01" });
    await flushPromises();

    expect(events).toEqual([
      { type: "error", message: "Workflow draft is stale." }
    ]);
  });

  it("ignores malformed stream frames without failing the subscription", async () => {
    const events: AgentStreamEvent[] = [];
    const errors: Error[] = [];
    const fetch = vi.fn(async () => new Response(streamFrom([
      "not json\n",
      `data: ${JSON.stringify({ type: "message-delta", messageId: "msg_01", content: "Hello" })}\n\n`,
      "data: {\"type\":\"message-delta\"\n\n",
      `data: ${JSON.stringify({ type: "message-completed", messageId: "msg_01" })}\n\n`,
      "{\"type\":\"message-delta\""
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      createEndpointContext("https://foundation.example/"),
      "/_elsa/agent/sessions/agt_01/stream",
      event => events.push(event),
      error => errors.push(error),
      { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "msg_01" });
    await flushPromises();

    expect(errors).toEqual([]);
    expect(events).toEqual([
      { type: "message-delta", messageId: "msg_01", content: "Hello" },
      { type: "message-completed", messageId: "msg_01" }
    ]);
  });

  it("normalizes the agentic turn loop events with numeric enums and PascalCase payloads", async () => {
    const events: AgentStreamEvent[] = [];
    const fetch = vi.fn(async () => new Response(streamFrom([
      `data: ${JSON.stringify({ Id: "evt_00", Kind: 0, Payload: { TurnId: "turn_01", MaxSteps: 8 } })}\n\n`,
      `data: ${JSON.stringify({ Id: "evt_01", Kind: 8, Payload: { StepIndex: 0, MaxSteps: 8 } })}\n\n`,
      `data: ${JSON.stringify({ Id: "evt_02", Kind: 10, ProposalId: "call-1", Payload: { ToolCallId: "call-1", ToolName: "echo", Arguments: "{}", Risk: 0, RequiresApproval: false } })}\n\n`,
      `data: ${JSON.stringify({ Id: "evt_03", Kind: 11, ProposalId: "call-1", Payload: { ToolCallId: "call-1", ToolName: "echo", Arguments: "{}", Risk: 0, RequiresApproval: false } })}\n\n`,
      `data: ${JSON.stringify({ Id: "evt_04", Kind: 12, ProposalId: "call-1", Payload: { ToolCallId: "call-1", ToolName: "echo", Succeeded: true, Summary: "echo:hello" } })}\n\n`,
      `data: ${JSON.stringify({ Id: "evt_05", Kind: 14, Content: "Incorporating follow-up: more", Payload: { StepIndex: 1, MaxSteps: 8 } })}\n\n`,
      `data: ${JSON.stringify({ Id: "evt_06", Kind: 9, Payload: { StepIndex: 0, MaxSteps: 8 } })}\n\n`,
      `data: ${JSON.stringify({ Id: "evt_07", Kind: 15, Payload: { TurnId: "turn_01", MaxSteps: 8 } })}\n\n`
    ]), { status: 200 }));

    subscribeAgentSessionStream(
      createEndpointContext("https://foundation.example/"),
      "/_elsa/agent/sessions/agt_01/stream",
      event => events.push(event),
      error => { throw error; },
      { fetch: fetch as typeof globalThis.fetch, defaultMessageId: "msg_01" });
    await flushPromises();

    expect(events).toEqual([
      { type: "turn-started", turnId: "turn_01", maxSteps: 8 },
      { type: "step-started", stepIndex: 0, maxSteps: 8 },
      { type: "tool-call-requested", toolCallId: "call-1", toolName: "echo", arguments: "{}", requiresApproval: false },
      { type: "tool-call-started", toolCallId: "call-1", toolName: "echo" },
      { type: "tool-call-completed", toolCallId: "call-1", toolName: "echo", succeeded: true, summary: "echo:hello" },
      { type: "progress", label: "Incorporating follow-up: more" },
      { type: "step-completed", stepIndex: 0, maxSteps: 8 },
      { type: "turn-cancelled", turnId: "turn_01" }
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
