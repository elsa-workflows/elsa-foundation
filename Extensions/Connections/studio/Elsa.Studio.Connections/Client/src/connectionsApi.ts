import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ConnectionDescriptorsResponse,
  ConnectionMetadata,
  ConnectionPickerRequest,
  ConnectionPickerResponse,
  ConnectionTestResponse,
  CreateConnectionRequest,
  ListConnectionsResponse
} from "./connectionTypes";

const basePath = "/connections";

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
