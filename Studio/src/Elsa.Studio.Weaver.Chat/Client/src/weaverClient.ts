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
  const bootstrap = unwrap(await context.http.getJson<AgentApiResponse<AgentBootstrapResponse>>("/_elsa/agent/bootstrap"));
  return mapCapabilities(bootstrap);
}

export async function listWeaverTools(context: StudioEndpointContext, agent?: string | null) {
  const bootstrap = unwrap(await context.http.getJson<AgentApiResponse<AgentBootstrapResponse>>("/_elsa/agent/bootstrap"));
  return mapTools(bootstrap, agent);
}

export async function streamWeaverChat(
  context: StudioEndpointContext,
  request: WeaverChatRequest,
  onEvent: (event: WeaverStreamEvent) => void
) {
  const bootstrap = unwrap(await context.http.getJson<AgentApiResponse<AgentBootstrapResponse>>("/_elsa/agent/bootstrap"));
  const providerId = request.agent || bootstrap.providers.find(provider => provider.isAvailable)?.providerId;
  const sessionId = request.conversationId || await createAgentSession(context, providerId);

  await context.http.postJson<AgentApiResponse<AgentMessageAcceptedResponse>>(`/_elsa/agent/sessions/${encodeURIComponent(sessionId)}/messages`, {
    role: "user",
    content: request.message,
    message: request.message,
    mode: "explain",
    contextAttachments: (request.attachments ?? []).map(mapAttachment)
  });

  const response = await fetch(new URL(`/_elsa/agent/sessions/${encodeURIComponent(sessionId)}/stream`, context.baseUrl).toString(), withDefaultHeaders(context.headers, {
    headers: { "Accept": "text/event-stream" }
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
      if (event) onEvent({ ...event, conversationId: event.conversationId || sessionId });
    }
  }

  const finalEvent = parseSseChunk(buffer);
  if (finalEvent) onEvent({ ...finalEvent, conversationId: finalEvent.conversationId || sessionId });
}

export function parseSseChunk(chunk: string): WeaverStreamEvent | null {
  const dataLine = chunk
    .split("\n")
    .map(line => line.trim())
    .find(line => line.startsWith("data:"));

  if (!dataLine) return null;
  return normalizeStreamEvent(JSON.parse(dataLine.slice("data:".length).trim()));
}

function withDefaultHeaders(defaultHeaders: HeadersInit | undefined, init: RequestInit = {}): RequestInit {
  if (!defaultHeaders) return init;
  const headers = new Headers(defaultHeaders);
  new Headers(init.headers).forEach((value, key) => headers.set(key, value));
  return { ...init, headers };
}

interface AgentApiResponse<T> {
  data?: T | null;
  error?: { code?: string; message?: string } | null;
}

interface AgentBootstrapResponse {
  enabled?: boolean;
  providerStatus?: string;
  capabilities?: AgentCapability[];
  providers?: AgentProviderDiagnostics[];
  policy?: { requiresApprovalForMutations?: boolean };
}

interface AgentCapability {
  id: string;
  displayName: string;
  description?: string;
  risk?: string;
  requiresApproval?: boolean;
}

interface AgentProviderDiagnostics {
  providerId: string;
  isAvailable: boolean;
  status?: string;
  supportedOperations?: string[];
  riskProfile?: string;
}

interface AgentSessionResponse {
  sessionId?: string;
  id?: string;
}

interface AgentMessageAcceptedResponse {
  message?: { id?: string };
}

function unwrap<T>(response: AgentApiResponse<T>): T {
  if (response.error) throw new Error(response.error.message || response.error.code || "Weaver request failed.");
  if (response.data === undefined || response.data === null) throw new Error("Weaver backend returned an empty response.");
  return response.data;
}

function mapCapabilities(bootstrap: AgentBootstrapResponse): WeaverCapabilities {
  const providers = bootstrap.providers ?? [];
  const availableProviders = providers.filter(provider => provider.isAvailable);
  return {
    streaming: bootstrap.enabled !== false && bootstrap.providerStatus !== "unavailable" && availableProviders.length > 0,
    conversationPersistence: true,
    proposalReview: bootstrap.policy?.requiresApprovalForMutations ?? (bootstrap.capabilities ?? []).some(capability => capability.requiresApproval || capability.risk === "review-required"),
    supportedAttachmentKinds: ["workflow.definition", "workflow.instance", "workflow.execution", "workflow.diagnostics", "studio.context"],
    agents: availableProviders.map(provider => ({
      name: provider.providerId,
      displayName: provider.providerId,
      description: provider.status ?? "Weaver provider"
    }))
  };
}

function mapTools(bootstrap: AgentBootstrapResponse, agent?: string | null): WeaverTool[] {
  const providers = (bootstrap.providers ?? []).filter(provider => provider.isAvailable && (!agent || provider.providerId === agent));
  return providers.flatMap(provider => (provider.supportedOperations ?? []).map(operation => ({
    name: `${provider.providerId}.${operation}`,
    displayName: formatLabel(operation),
    description: `${formatLabel(operation)} support exposed by ${provider.providerId}.`,
    mutability: operation === "chat" || operation === "streaming" ? "read-only" : "proposal",
    dangerLevel: provider.riskProfile === "privileged-execution" ? "high" : "low",
    permissions: [],
    agentScopes: [provider.providerId],
    isEnabled: true
  })));
}

async function createAgentSession(context: StudioEndpointContext, providerId?: string) {
  const response = unwrap(await context.http.postJson<AgentApiResponse<AgentSessionResponse>>("/_elsa/agent/sessions", {
    conversationId: "weaver-chat",
    ...(providerId ? { providerId } : {}),
    mode: "explain",
    activeSurface: { route: "/weaver" },
    clientContext: { studioVersion: "studio", sdkVersion: "studio", moduleIds: ["Elsa.Studio.Weaver.Chat"] },
    metadata: { route: "/weaver", source: "weaver-chat" }
  }));
  const sessionId = response.sessionId ?? response.id;
  if (!sessionId) throw new Error("Weaver backend did not return an agent session.");
  return sessionId;
}

function mapAttachment(attachment: StudioAiContextAttachment) {
  const kind = normalizeAttachmentKind(attachment.kind);
  const references = buildAttachmentReferences(attachment, kind);
  const sourceId = references.sourceId || attachment.referenceId || "";

  return {
    id: attachment.id ?? `${kind}:${sourceId || attachment.scope || "context"}`,
    kind,
    displayName: formatLabel(kind),
    sensitivity: "public",
    summary: sourceId || attachment.scope || kind,
    references,
    metadata: attachment.metadata ?? {}
  };
}

function normalizeAttachmentKind(kind: string) {
  const normalized = kind.trim().toLowerCase();
  const aliases: Record<string, string> = {
    "workflow-definition": "workflow.definition",
    "workflow-create-draft": "workflow.definition",
    "workflow-executable": "workflow.execution",
    "workflow-instance": "workflow.instance",
    "workflow-diagnostics": "workflow.diagnostics",
    "studio-context": "studio.context"
  };
  return aliases[normalized] ?? normalized;
}

function buildAttachmentReferences(attachment: StudioAiContextAttachment, kind: string) {
  const metadata = attachment.metadata ?? {};
  const references: Record<string, string> = {
    source: kind.startsWith("workflow.") ? "workflow" : kind,
    sourceId: attachment.referenceId ?? readMetadataString(metadata, "artifactId") ?? readMetadataString(metadata, "definitionId") ?? readMetadataString(metadata, "id") ?? "",
    scope: attachment.scope ?? ""
  };

  addReference(references, "workflowDefinitionId", readMetadataString(metadata, "workflowDefinitionId") ?? readMetadataString(metadata, "definitionId"));
  addReference(references, "workflowVersionId", readMetadataString(metadata, "workflowVersionId") ?? readMetadataString(metadata, "definitionVersionId") ?? readMetadataString(metadata, "sourceId"));
  addReference(references, "artifactId", readMetadataString(metadata, "artifactId"));
  addReference(references, "workflowExecutionId", readMetadataString(metadata, "workflowExecutionId") ?? readMetadataString(metadata, "instanceId"));
  addReference(references, "selectedActivityId", attachment.activityId ?? readMetadataString(metadata, "selectedActivityId"));

  return references;
}

function addReference(references: Record<string, string>, key: string, value: string | undefined) {
  if (value?.trim()) references[key] = value;
}

function readMetadataString(metadata: Record<string, unknown>, key: string) {
  const value = metadata[key];
  return typeof value === "string" && value.trim() ? value : undefined;
}

function normalizeStreamEvent(value: WeaverStreamEvent | Record<string, unknown>): WeaverStreamEvent | null {
  if (!value || typeof value !== "object") return null;
  const event = value as Record<string, unknown>;
  const rawType = readField(event, "type") ?? readField(event, "kind");
  const type = normalizeAgentEventType(rawType);
  const error = readRecord(event, "error");
  const data = readRecord(event, "data");
  const content = readString(event, "content") ?? readString(data, "content") ?? readString(error, "message") ?? "";

  if (type === "assistant.delta") {
    return createStreamEvent("assistant.delta", event, { content });
  }

  if (type === "conversation.error") {
    return createStreamEvent("conversation.error", event, { content: content || "Weaver is unavailable for this request." });
  }

  if (type === "conversation.completed") {
    return createStreamEvent("conversation.completed", event, {});
  }

  if (type === "conversation.started") {
    return null;
  }

  return value as WeaverStreamEvent;
}

function createStreamEvent(type: string, source: Record<string, unknown>, data: Record<string, unknown>): WeaverStreamEvent {
  return {
    type,
    conversationId: readString(source, "conversationId") ?? "",
    sequence: Number(readField(source, "sequence") ?? 0),
    timestamp: readString(source, "timestamp") ?? readString(source, "createdAt") ?? new Date().toISOString(),
    data
  };
}

function normalizeAgentEventType(value: unknown) {
  if (typeof value === "number") {
    return ["conversation.started", "assistant.delta", "tool.approval", "proposal.created", "conversation.completed", "conversation.error"][value] ?? "";
  }

  const normalized = String(value ?? "").replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
  if (normalized === "message-delta") return "assistant.delta";
  if (normalized === "completed") return "conversation.completed";
  if (normalized === "error") return "conversation.error";
  return String(value ?? "");
}

function formatLabel(value: string) {
  return value.replace(/[-_.]+/g, " ").replace(/\b\w/g, letter => letter.toUpperCase());
}

function readString(source: Record<string, unknown> | undefined, key: string) {
  const value = readField(source, key);
  return typeof value === "string" ? value : undefined;
}

function readRecord(source: Record<string, unknown> | undefined, key: string) {
  const value = readField(source, key);
  return value && typeof value === "object" ? value as Record<string, unknown> : undefined;
}

function readField(source: Record<string, unknown> | undefined, key: string) {
  if (!source) return undefined;
  if (key in source) return source[key];
  const lowered = key.toLowerCase();
  const match = Object.keys(source).find(candidate => candidate.toLowerCase() === lowered);
  return match ? source[match] : undefined;
}
