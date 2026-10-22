import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ConnectionDescriptorsResponse,
  ConnectionMetadata,
  ConnectionPickerRequest,
  ConnectionPickerResponse,
  ConnectionSecretPickerMetadata,
  ConnectionTestResponse,
  CreateConnectionRequest,
  ListConnectionsResponse
} from "./connectionTypes";

const basePath = "/connections";
const secretPickerPath = "/secrets/picker";

export interface ConnectionSecretPickerResult {
  items: ConnectionSecretPickerMetadata[];
  capped: boolean;
}

export async function listConnections(context: StudioEndpointContext, search = "") {
  const parameters = new URLSearchParams({ pageSize: "100" });
  if (search.trim()) parameters.set("search", search.trim());
  return context.http.getJson<ListConnectionsResponse>(`${basePath}?${parameters.toString()}`);
}

export async function getConnectionDescriptors(context: StudioEndpointContext) {
  return context.http.getJson<ConnectionDescriptorsResponse>(`${basePath}/descriptors`);
}

export async function createConnection(context: StudioEndpointContext, request: CreateConnectionRequest) {
  return context.http.postJson<ConnectionMetadata>(basePath, request);
}

/**
 * Uses the tenant-scoped, metadata-only picker endpoint. Keep this DTO and its
 * runtime allowlist local to Connections instead of importing the Secrets UI.
 */
export async function pickConnectionSecrets(context: StudioEndpointContext, search = ""): Promise<ConnectionSecretPickerResult> {
  const response = await context.http.postJson<{ items?: unknown }>(secretPickerPath, {
    search: search.trim(),
    typeNames: ["text"],
    storeNames: [],
    activeOnly: true
  });
  const rawItems = Array.isArray(response?.items) ? response.items : [];
  return {
    items: allowlistConnectionSecretMetadata(rawItems),
    capped: rawItems.length >= 50
  };
}

export function allowlistConnectionSecretMetadata(items: unknown[], now = Date.now()): ConnectionSecretPickerMetadata[] {
  return items.flatMap(item => {
    if (!item || typeof item !== "object") return [];
    const value = item as Record<string, unknown>;
    const status = typeof value.status === "string" ? value.status : "";
    if (typeof value.name !== "string" || !value.name.trim()
      || value.typeName !== "text" || status.toLowerCase() !== "active") return [];

    const expiresAt = typeof value.expiresAt === "string" ? value.expiresAt : null;
    if (expiresAt) {
      const expiry = Date.parse(expiresAt);
      if (!Number.isFinite(expiry) || expiry <= now) return [];
    }

    return [{
      name: value.name,
      displayName: typeof value.displayName === "string" ? value.displayName : null,
      typeName: "text",
      scope: typeof value.scope === "string" ? value.scope : null,
      status: "Active",
      expiresAt
    }];
  });
}

export async function testConnection(context: StudioEndpointContext, name: string) {
  return context.http.postJson<ConnectionTestResponse>(`${basePath}/${encodeURIComponent(name)}/test`, {});
}

export async function revokeConnection(context: StudioEndpointContext, name: string) {
  return context.http.postJson<ConnectionMetadata>(`${basePath}/${encodeURIComponent(name)}/revoke`, {});
}

export async function deleteConnection(context: StudioEndpointContext, name: string) {
  await context.http.deleteJson<unknown>(`${basePath}/${encodeURIComponent(name)}`);
}

export async function pickConnections(context: StudioEndpointContext, request: ConnectionPickerRequest | string = {}) {
  const normalized = typeof request === "string" ? { search: request } : request;
  return context.http.postJson<ConnectionPickerResponse>(`${basePath}/picker`, {
    search: normalized.search ?? "",
    typeName: normalized.typeName ?? ""
  });
}
