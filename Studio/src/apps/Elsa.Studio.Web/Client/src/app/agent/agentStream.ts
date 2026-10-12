import { withDefaultHeaders, type StudioEndpointContext } from "../../sdk";
import type { AgentActionProposalPayload, AgentClarificationRequest, AgentStreamEvent, WorkflowGraphOperationBatch } from "./agentTypes";

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
    const response = await request(new URL(streamUrl, context.baseUrl).toString(), await withStreamHeaders(context, {
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

async function withStreamHeaders(context: StudioEndpointContext, init: RequestInit) {
  const withStaticHeaders = withDefaultHeaders(context.headers, init);
  const token = await context.accessTokenFactory?.();
  if (!token) return withStaticHeaders;

  const headers = new Headers(withStaticHeaders.headers);
  headers.set("Authorization", `Bearer ${token}`);
  return { ...withStaticHeaders, credentials: withStaticHeaders.credentials ?? "include", headers };
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
  const sseFrame: SseFrame = createSseFrame();

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
  let value: unknown;
  try {
    value = JSON.parse(json);
  } catch {
    return null;
  }

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
  const type = normalizeEventName(readField(event, "type") ?? readField(event, "kind"), agentStreamEventNames);
  const resultKind = normalizeEventName(readField(event, "resultKind"), agentResultKindNames);
  const data = readObject<Record<string, unknown>>(event, "data");
  const payload = readObject<Record<string, unknown>>(event, "payload") ?? readObject<Record<string, unknown>>(data, "payload");
  const content = readString(event, "content") ?? readString(data, "content") ?? "";

  if (resultKind === "workflow-graph-operation-batch" || type === "workflow-graph-operation-batch-created" || type === "workflow-graph-operation-batch") {
    const batch = normalizeWorkflowBatch(payload ?? readObject<Record<string, unknown>>(event, "workflowGraphOperationBatch") ?? readObject<Record<string, unknown>>(data, "workflowGraphOperationBatch"));
    return batch ? { type: "workflow-batch-created", messageId, batch } : null;
  }

  if (resultKind === "clarification" || type === "clarification-requested" || type === "clarification") {
    const clarification = normalizeClarification(payload ?? data ?? event, messageId);
    return { type: "clarification-requested", messageId, clarification };
  }

  switch (type) {
    case "message-started":
    case "started":
    case "conversation.started": {
      // The orchestrator's turn-level Started event carries a turn id (for cancellation); a provider's
      // bare Started does not.
      const turnId = readString(payload, "turnId");
      if (turnId) {
        return { type: "turn-started", turnId, maxSteps: readNumber(payload, "maxSteps") ?? 0 };
      }
      return { type: "message-started", messageId, role: "assistant" };
    }
    case "step-started":
      return { type: "step-started", stepIndex: readNumber(payload, "stepIndex") ?? 0, maxSteps: readNumber(payload, "maxSteps") ?? 0 };
    case "step-completed":
      return { type: "step-completed", stepIndex: readNumber(payload, "stepIndex") ?? 0, maxSteps: readNumber(payload, "maxSteps") ?? 0 };
    case "tool-call-requested":
      return {
        type: "tool-call-requested",
        toolCallId: readString(payload, "toolCallId") ?? readString(event, "proposalId") ?? messageId,
        toolName: readString(payload, "toolName") ?? "tool",
        arguments: readString(payload, "arguments") ?? "",
        requiresApproval: readBoolean(payload, "requiresApproval") ?? false
      };
    case "tool-call-started":
      return {
        type: "tool-call-started",
        toolCallId: readString(payload, "toolCallId") ?? readString(event, "proposalId") ?? messageId,
        toolName: readString(payload, "toolName") ?? "tool"
      };
    case "tool-call-completed":
      return {
        type: "tool-call-completed",
        toolCallId: readString(payload, "toolCallId") ?? readString(event, "proposalId") ?? messageId,
        toolName: readString(payload, "toolName") ?? "tool",
        succeeded: readBoolean(payload, "succeeded") ?? false,
        summary: readString(payload, "summary") ?? ""
      };
    case "plan-updated":
      return { type: "plan-updated", steps: normalizePlanSteps(payload) };
    case "progress":
      return { type: "progress", label: content || readString(payload, "label") || "Working…" };
    case "turn-cancelled":
      return { type: "turn-cancelled", turnId: readString(payload, "turnId") ?? messageId };
    case "message-delta":
    case "assistant.delta":
      if (resultKind === "error") {
        return { type: "error", message: content || "Weaver could not complete the turn." };
      }
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
      const error = readObject<Record<string, unknown>>(event, "error") ?? readObject<Record<string, unknown>>(data, "error");
      return { type: "error", message: readString(error, "message") ?? (content || "Agent stream failed.") };
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

const agentStreamEventNames = [
  "started",
  "message-delta",
  "tool-approval-requested",
  "proposal-created",
  "completed",
  "error",
  "clarification-requested",
  "workflow-graph-operation-batch-created",
  "step-started",
  "step-completed",
  "tool-call-requested",
  "tool-call-started",
  "tool-call-completed",
  "plan-updated",
  "progress",
  "turn-cancelled"
];

const agentResultKindNames = [
  "message",
  "clarification",
  "workflow-graph-operation-batch",
  "proposal",
  "error"
];

const workflowOperationKindNames = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];

function normalizeEventName(value: unknown, numericNames?: string[]) {
  if (typeof value === "number") {
    return numericNames?.[value] ?? "";
  }

  return String(value ?? "").replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

function readString(source: Record<string, unknown> | undefined, key: string) {
  const value = readField(source, key);
  return typeof value === "string" ? value : undefined;
}

function readNumber(source: Record<string, unknown> | undefined, key: string) {
  const value = readField(source, key);
  return typeof value === "number" ? value : undefined;
}

function readBoolean(source: Record<string, unknown> | undefined, key: string) {
  const value = readField(source, key);
  return typeof value === "boolean" ? value : undefined;
}

function normalizePlanSteps(payload: Record<string, unknown> | undefined) {
  return (readArray(payload, "steps") ?? [])
    .filter((step): step is Record<string, unknown> => !!step && typeof step === "object")
    .map(step => ({
      id: readString(step, "id") ?? "",
      title: readString(step, "title") ?? "",
      status: readString(step, "status") ?? "pending"
    }));
}

function readObject<T extends object>(source: Record<string, unknown> | undefined, key: string) {
  const value = readField(source, key);
  return value && typeof value === "object" ? value as T : undefined;
}

function readArray(source: Record<string, unknown> | undefined, key: string) {
  const value = readField(source, key);
  return Array.isArray(value) ? value : undefined;
}

function readField(source: Record<string, unknown> | undefined, key: string) {
  if (!source) return undefined;
  if (key in source) return source[key];
  const lowered = key.toLowerCase();
  const match = Object.keys(source).find(candidate => candidate.toLowerCase() === lowered);
  return match ? source[match] : undefined;
}

function normalizeWorkflowBatch(value: Record<string, unknown> | undefined): WorkflowGraphOperationBatch | null {
  if (!value) return null;

  const operations = (readArray(value, "operations") ?? [])
    .filter((operation): operation is Record<string, unknown> => !!operation && typeof operation === "object")
    .map(normalizeWorkflowOperation);

  return {
    schemaVersion: readString(value, "schemaVersion") ?? "unknown",
    workflowDefinitionId: readString(value, "workflowDefinitionId") ?? readString(value, "workflowId") ?? "active-draft",
    baseRevision: readString(value, "baseRevision") ?? null,
    operations,
    metadata: readObject<Record<string, unknown>>(value, "metadata") ?? {}
  };
}

function normalizeWorkflowOperation(value: Record<string, unknown>) {
  return {
    id: readString(value, "id") ?? `operation-${Date.now()}`,
    kind: normalizeEventName(readField(value, "kind"), workflowOperationKindNames),
    parameters: readObject<Record<string, unknown>>(value, "parameters") ?? {},
    temporaryReferences: (readArray(value, "temporaryReferences") ?? []).filter((item): item is string => typeof item === "string"),
    summary: readString(value, "summary") ?? null
  };
}

function normalizeClarification(value: Record<string, unknown>, messageId: string): AgentClarificationRequest {
  const choices = (readArray(value, "choices") ?? readArray(value, "options") ?? [])
    .map(choice => typeof choice === "string" ? choice : isRecord(choice) ? readString(choice, "label") ?? readString(choice, "value") : undefined)
    .filter((choice): choice is string => !!choice);

  return {
    id: readString(value, "id") ?? `clarification-${messageId}`,
    prompt: readString(value, "prompt") ?? readString(value, "question") ?? readString(value, "content") ?? "Weaver needs clarification before continuing.",
    choices,
    metadata: readObject<Record<string, unknown>>(value, "metadata")
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
