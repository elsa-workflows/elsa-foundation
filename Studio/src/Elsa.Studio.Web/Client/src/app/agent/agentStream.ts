import { withDefaultHeaders, type StudioEndpointContext } from "../../sdk";
import type { AgentActionProposalPayload, AgentStreamEvent } from "./agentTypes";

export interface AgentStreamSubscription {
  close(): void;
}

export interface AgentStreamOptions {
  fetch?: typeof fetch;
  defaultMessageId?: string;
}

export function subscribeAgentSessionStream(
  context: StudioEndpointContext,
  streamUrl: string,
  onEvent: (event: AgentStreamEvent) => void,
  onError: (error: Error) => void,
  options: AgentStreamOptions = {}
): AgentStreamSubscription {
  const controller = new AbortController();
  const request = options.fetch ?? fetch;

  void readStream(context, streamUrl, request, controller.signal, onEvent, onError, options.defaultMessageId);

  return {
    close() {
      controller.abort();
    }
  };
}

async function readStream(
  context: StudioEndpointContext,
  streamUrl: string,
  request: typeof fetch,
  signal: AbortSignal,
  onEvent: (event: AgentStreamEvent) => void,
  onError: (error: Error) => void,
  defaultMessageId?: string
) {
  try {
    const response = await request(new URL(streamUrl, context.baseUrl).toString(), withDefaultHeaders(context.headers, {
      cache: "no-store",
      headers: { Accept: "text/event-stream, application/x-ndjson, application/json" },
      signal
    }));

    if (!response.ok) {
      throw new Error(await response.text() || `Agent stream failed with ${response.status}.`);
    }

    if (!response.body) {
      return;
    }

    await parseEventStream(response.body, onEvent, signal, defaultMessageId);
  } catch (error) {
    if (!signal.aborted) {
      onError(error instanceof Error ? error : new Error(String(error)));
    }
  }
}

async function parseEventStream(
  body: ReadableStream<Uint8Array>,
  onEvent: (event: AgentStreamEvent) => void,
  signal: AbortSignal,
  defaultMessageId?: string
) {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let sseFrame: SseFrame = createSseFrame();

  while (!signal.aborted) {
    const { value, done } = await reader.read();
    if (done) {
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split(/\r?\n/);
    buffer = lines.pop() ?? "";

    for (const line of lines) {
      const event = parseEventLine(line, defaultMessageId, sseFrame);
      if (event) {
        onEvent(event);
      }
    }
  }

  const finalEvent = parseEventLine(buffer, defaultMessageId, sseFrame) ?? dispatchSseFrame(sseFrame, defaultMessageId);
  if (finalEvent) {
    onEvent(finalEvent);
  }
}

interface SseFrame {
  eventName?: string;
  dataLines: string[];
}

function createSseFrame(): SseFrame {
  return { dataLines: [] };
}

function parseEventLine(line: string, defaultMessageId: string | undefined, sseFrame: SseFrame): AgentStreamEvent | null {
  const trimmed = line.trim();
  if (!trimmed) {
    return dispatchSseFrame(sseFrame, defaultMessageId);
  }

  if (trimmed.startsWith(":")) {
    return null;
  }

  if (trimmed.startsWith("event:")) {
    sseFrame.eventName = trimmed.slice("event:".length).trim();
    return null;
  }

  if (trimmed.startsWith("id:") || trimmed.startsWith("retry:")) {
    return null;
  }

  if (trimmed.startsWith("data:")) {
    const data = trimmed.slice("data:".length).trim();
    if (!data) {
      return null;
    }

    sseFrame.dataLines.push(data);
    return null;
  }

  return parseJsonEvent(trimmed, defaultMessageId);
}

function dispatchSseFrame(frame: SseFrame, defaultMessageId?: string): AgentStreamEvent | null {
  if (frame.dataLines.length === 0) {
    frame.eventName = undefined;
    return null;
  }

  const json = frame.dataLines.join("\n");
  const eventName = frame.eventName;
  frame.eventName = undefined;
  frame.dataLines = [];
  return parseJsonEvent(json, defaultMessageId, eventName);
}

function parseJsonEvent(json: string, defaultMessageId?: string, eventName?: string): AgentStreamEvent | null {
  const value = JSON.parse(json);
  if (eventName && value && typeof value === "object" && !("type" in value) && !("kind" in value)) {
    return normalizeStreamEvent({ type: eventName, data: value }, defaultMessageId);
  }

  return normalizeStreamEvent(value, defaultMessageId);
}

function normalizeStreamEvent(value: unknown, defaultMessageId?: string): AgentStreamEvent | null {
  if (!value || typeof value !== "object") {
    return null;
  }

  const event = value as Record<string, unknown>;
  const messageId = readString(event, "messageId") ?? defaultMessageId ?? readString(event, "id") ?? `agent-${Date.now()}`;
  const type = normalizeEventName(event.type ?? event.kind);
  const data = event.data && typeof event.data === "object" ? event.data as Record<string, unknown> : undefined;
  const content = readString(event, "content") ?? readString(data, "content") ?? "";

  switch (type) {
    case "message-started":
    case "started":
    case "conversation.started":
      return { type: "message-started", messageId, role: "assistant" };
    case "message-delta":
    case "assistant.delta":
      return { type: "message-delta", messageId, content };
    case "proposal-created":
    case "proposal.created":
      return {
        type: "proposal-created",
        proposalId: readString(event, "proposalId") ?? readString(data, "proposalId") ?? messageId,
        messageId,
        proposal: readObject<AgentActionProposalPayload>(event, "proposal") ?? readObject<AgentActionProposalPayload>(data, "proposal")
      };
    case "message-completed":
    case "completed":
    case "assistant.completed":
    case "conversation.completed":
      return { type: "message-completed", messageId };
    case "error": {
      const error = event.error && typeof event.error === "object" ? event.error as Record<string, unknown> : undefined;
      return { type: "error", message: String(error?.message ?? content ?? "Agent stream failed.") };
    }
    case "conversation.error":
    case "conversation.failed":
      return { type: "error", message: content || readString(data, "message") || "Weaver could not complete the turn." };
    case "tool.started":
    case "tool.progress":
    case "tool.result":
    case "tool.completed":
      return { type: "progress", label: readString(data, "summary") ?? readString(data, "toolName") ?? readString(data, "name") ?? "Weaver tool activity" };
    default:
      return null;
  }
}

function normalizeEventName(value: unknown) {
  if (typeof value === "number") {
    return ["started", "message-delta", "tool-approval-requested", "proposal-created", "completed", "error"][value] ?? "";
  }

  return String(value ?? "").replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

function readString(source: Record<string, unknown> | undefined, key: string) {
  const value = source?.[key];
  return typeof value === "string" ? value : undefined;
}

function readObject<T extends object>(source: Record<string, unknown> | undefined, key: string) {
  const value = source?.[key];
  return value && typeof value === "object" ? value as T : undefined;
}
