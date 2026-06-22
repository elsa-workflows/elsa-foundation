import type { StudioAiContextAttachment, StudioEndpointContext } from "@elsa-workflows/studio-sdk";

export interface WeaverAgentCapability {
  name: string;
  displayName: string;
  description: string;
}

export interface WeaverCapabilities {
  streaming: boolean;
  conversationPersistence: boolean;
  proposalReview: boolean;
  supportedAttachmentKinds: string[];
  agents: WeaverAgentCapability[];
}

export interface WeaverTool {
  name: string;
  displayName: string;
  description: string;
  mutability: string;
  dangerLevel: string;
  permissions: string[];
  agentScopes: string[];
  isEnabled: boolean;
}

export interface WeaverChatRequest {
  conversationId?: string | null;
  message: string;
  agent?: string | null;
  attachments?: StudioAiContextAttachment[];
}

export interface WeaverStreamEvent {
  type: string;
  conversationId: string;
  sequence: number;
  timestamp: string;
  data: Record<string, unknown>;
}

export async function getWeaverCapabilities(context: StudioEndpointContext) {
  return context.http.getJson<WeaverCapabilities>("/ai/capabilities");
}

export async function listWeaverTools(context: StudioEndpointContext, agent?: string | null) {
  const parameters = new URLSearchParams();
  if (agent) parameters.set("agent", agent);
  const query = parameters.toString();
  return context.http.getJson<WeaverTool[]>(`/ai/tools${query ? `?${query}` : ""}`);
}

export async function streamWeaverChat(
  context: StudioEndpointContext,
  request: WeaverChatRequest,
  onEvent: (event: WeaverStreamEvent) => void
) {
  const response = await fetch(new URL("/ai/chat", context.baseUrl).toString(), withDefaultHeaders(context.headers, {
    method: "POST",
    headers: {
      "Accept": "text/event-stream",
      "Content-Type": "application/json"
    },
    body: JSON.stringify({
      conversationId: request.conversationId || undefined,
      message: request.message,
      agent: request.agent || undefined,
      attachments: request.attachments ?? []
    })
  }));

  if (!response.ok) {
    throw new Error(await response.text() || `Weaver chat failed with ${response.status}.`);
  }

  if (!response.body) {
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const chunks = buffer.split("\n\n");
    buffer = chunks.pop() ?? "";
    for (const chunk of chunks) {
      const event = parseSseChunk(chunk);
      if (event) onEvent(event);
    }
  }

  const finalEvent = parseSseChunk(buffer);
  if (finalEvent) onEvent(finalEvent);
}

export function parseSseChunk(chunk: string): WeaverStreamEvent | null {
  const dataLine = chunk
    .split("\n")
    .map(line => line.trim())
    .find(line => line.startsWith("data:"));

  if (!dataLine) return null;
  return JSON.parse(dataLine.slice("data:".length).trim()) as WeaverStreamEvent;
}

function withDefaultHeaders(defaultHeaders: HeadersInit | undefined, init: RequestInit = {}): RequestInit {
  if (!defaultHeaders) return init;
  const headers = new Headers(defaultHeaders);
  new Headers(init.headers).forEach((value, key) => headers.set(key, value));
  return { ...init, headers };
}
