import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";

/** The provider key agent definitions are authored with (Elsa.Agent.Core AgentRuntimeIds.ProviderKey). */
export const agentProviderKey = "elsa.agent";

// Generic activity design API (capability elsa.api.activity-design v1); this module adds no second catalog.
const definitionsPath = "/design/activities/definitions";
const authoringCapabilitiesPath = "/design/activities/authoring-capabilities";
// Agent authoring reads (capability elsa.api.agent v1) and the shared Connections picker.
const modelProvidersPath = "/agent/model-providers";
const modelCapabilitiesPath = "/agent/model-capabilities";
const connectionPickerPath = "/connections/picker";

export interface ActivityVersionReference {
  versionId: string;
  version: string;
  lifecycle: string;
  providerKey: string;
  providerSchemaVersion: string;
}

export interface ActivityActionAvailability {
  action: string;
  allowed: boolean;
  unavailableCode?: string | null;
}

export interface AgentRow {
  definition: {
    definitionId: string;
    activityTypeKey: string;
    category: string;
    displayName: string;
    description?: string | null;
    contentAuthority: { kind: string; authorityKey?: string | null; sourceId?: string | null };
  };
  lifecycle: {
    draftCount: number;
    versionCount: number;
    head?: ActivityVersionReference | null;
    recommendation?: ActivityVersionReference | null;
  };
  actions: ActivityActionAvailability[];
  updatedAt: string;
}

export interface AgentPage {
  items: AgentRow[];
  count: number;
  totalCount: number;
  hasMore: boolean;
  continuation?: string | null;
}

/** One page of agent definitions. Provider filtering, search, counting and the cursor are all server-side. */
export async function listAgents(context: StudioEndpointContext, request: { limit: number; cursor?: string | null; search?: string }, signal?: AbortSignal) {
  const parameters = new URLSearchParams({ limit: String(request.limit), sort: "identity-asc", providerKey: agentProviderKey });
  if (request.cursor) parameters.set("cursor", request.cursor);
  if (request.search?.trim()) parameters.set("search", request.search.trim());
  const page = await context.http.getJson<Partial<AgentPage>>(`${definitionsPath}?${parameters.toString()}`, { signal });
  return {
    items: Array.isArray(page?.items) ? page.items : [],
    count: page?.count ?? 0,
    totalCount: page?.totalCount ?? 0,
    hasMore: page?.hasMore === true,
    continuation: page?.continuation ?? null
  } satisfies AgentPage;
}

export interface AgentAuthoring {
  contractSchemaVersion: string;
  schemaVersions: string[];
  requiredOutcomes: Array<{ referenceKey: string; name: string; isEmitted: boolean; description?: string | null }>;
}

/** The authorable agent manifest schemas, or null when the caller cannot author agents here. */
export async function getAgentAuthoring(context: StudioEndpointContext, signal?: AbortSignal): Promise<AgentAuthoring | null> {
  const capabilities = await context.http.getJson<{
    contractSchemaVersions?: string[];
    providers?: Array<{ providerKey: string; manifestSchemas?: Array<{ schemaVersion: string; isAuthorable: boolean }>; requiredOutcomes?: AgentAuthoring["requiredOutcomes"] }>;
  }>(authoringCapabilitiesPath, { signal });
  const provider = capabilities?.providers?.find(item => item.providerKey === agentProviderKey);
  const contractSchemaVersion = capabilities?.contractSchemaVersions?.[0];
  const schemaVersions = (provider?.manifestSchemas ?? []).filter(item => item.isAuthorable).map(item => item.schemaVersion);
  if (!provider || !contractSchemaVersion || schemaVersions.length === 0) return null;
  return { contractSchemaVersion, schemaVersions, requiredOutcomes: provider.requiredOutcomes ?? [] };
}

export interface CreatedAgent {
  definition: { definitionId: string };
  draft: { draftId: string };
}

export function createAgent(context: StudioEndpointContext, request: {
  displayName: string;
  description: string | null;
  schemaVersion: string;
  payload: unknown;
  authoring: AgentAuthoring;
}) {
  return context.http.postJson<CreatedAgent>(definitionsPath, {
    category: "Agents",
    displayName: request.displayName,
    description: request.description,
    provider: { providerKey: agentProviderKey, schemaVersion: request.schemaVersion, payload: request.payload },
    contract: {
      contractSchemaVersion: request.authoring.contractSchemaVersion,
      inputs: [],
      outputs: [],
      outcomes: request.authoring.requiredOutcomes.map(outcome => ({ ...outcome, description: outcome.description ?? null }))
    },
    layout: []
  });
}

export interface ModelProvider {
  providerKey: string;
  connectionTypeName: string;
  displayName: string;
}

export async function listModelProviders(context: StudioEndpointContext, signal?: AbortSignal) {
  const response = await context.http.getJson<{ items?: ModelProvider[] }>(modelProvidersPath, { signal });
  return Array.isArray(response?.items) ? response.items : [];
}

export interface ModelConnection {
  name: string;
  displayName: string;
  status: string;
  modelId: string | null;
}

/** The picker answers at most this many connections per request, with no continuation; narrow it with a search. */
export const connectionPickerLimit = 50;

/** Ready-or-not connections of one model connection type; only non-secret settings are read. */
export async function listModelConnections(context: StudioEndpointContext, typeName: string, search: string, signal?: AbortSignal): Promise<ModelConnection[]> {
  const response = await context.http.postJson<{ items?: Array<{ name: string; displayName?: string; status?: string; settings?: Record<string, string> }> }>(
    connectionPickerPath, search.trim() ? { typeName, search: search.trim() } : { typeName }, { signal });
  return (Array.isArray(response?.items) ? response.items : []).map(item => ({
    name: item.name,
    displayName: item.displayName?.trim() || item.name,
    status: item.status ?? "",
    modelId: item.settings?.modelId?.trim() || null
  }));
}

export interface ModelCapabilities {
  settingsSchema: { properties?: Record<string, { type?: string; minimum?: number; maximum?: number }> } | null;
  supportsJsonSchema: boolean;
  maximumOutputTokens: number;
}

export function readModelCapabilities(context: StudioEndpointContext, model: { providerKey: string; connectionName: string; modelId: string }, signal?: AbortSignal) {
  return context.http.postJson<ModelCapabilities>(modelCapabilitiesPath, model, { signal });
}

/** Up to two active agent drafts of one definition: enough to tell a single draft from a choice among several. */
export async function listActiveAgentDrafts(context: StudioEndpointContext, definitionId: string, signal?: AbortSignal) {
  const parameters = new URLSearchParams({ limit: "2", providerKey: agentProviderKey, status: "Active" });
  const page = await context.http.getJson<{ items?: Array<{ draft: { draftId: string } }> }>(`${definitionsPath}/${encodeURIComponent(definitionId)}/drafts?${parameters.toString()}`, { signal });
  return (Array.isArray(page?.items) ? page.items : []).map(item => item.draft.draftId);
}
