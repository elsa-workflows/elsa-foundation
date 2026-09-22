import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  CreateSecretRequest,
  ListSecretsRequest,
  ListSecretsResponse,
  RotateSecretRequest,
  SecretDescriptorsResponse,
  SecretMetadata,
  SecretPickerRequest,
  SecretPickerResponse,
  SecretTestResponse,
  UpdateSecretRequest
} from "./secretTypes";

const basePath = "/_elsa/secrets";

export async function listSecrets(context: StudioEndpointContext, request: ListSecretsRequest | string = {}) {
  const normalized = typeof request === "string" ? { search: request } : request;
  const parameters = new URLSearchParams({
    activeOnly: String(normalized.activeOnly ?? false),
    pageSize: String(normalized.pageSize ?? 100)
  });
  const search = normalized.search ?? "";
  if (search.trim()) parameters.set("search", search.trim());
  return context.http.getJson<ListSecretsResponse>(`${basePath}?${parameters.toString()}`);
}

export async function getSecret(context: StudioEndpointContext, name: string) {
  return context.http.getJson<SecretMetadata>(`${basePath}/${encodeURIComponent(name)}`);
}

export async function createSecret(context: StudioEndpointContext, request: CreateSecretRequest) {
  return context.http.postJson<SecretMetadata>(basePath, request);
}

export async function updateSecret(context: StudioEndpointContext, name: string, request: UpdateSecretRequest) {
  return context.http.putJson<SecretMetadata>(`${basePath}/${encodeURIComponent(name)}`, request);
}

export async function rotateSecret(context: StudioEndpointContext, name: string, request: RotateSecretRequest) {
  return context.http.postJson<SecretMetadata>(`${basePath}/${encodeURIComponent(name)}/rotate`, request);
}

export async function testSecret(context: StudioEndpointContext, name: string) {
  return context.http.postJson<SecretTestResponse>(`${basePath}/${encodeURIComponent(name)}/test`, {});
}

export async function revokeSecret(context: StudioEndpointContext, name: string) {
  return context.http.postJson<SecretMetadata>(`${basePath}/${encodeURIComponent(name)}/revoke`, {});
}

export async function deleteSecret(context: StudioEndpointContext, name: string) {
  await context.http.deleteJson<unknown>(`${basePath}/${encodeURIComponent(name)}`);
}

export async function getSecretDescriptors(context: StudioEndpointContext) {
  return context.http.getJson<SecretDescriptorsResponse>(`${basePath}/descriptors`);
}

export async function pickSecrets(context: StudioEndpointContext, request: SecretPickerRequest | string = {}, typeNames: string[] = []) {
  const normalized = typeof request === "string" ? { search: request } : request;
  return context.http.postJson<SecretPickerResponse>(`${basePath}/picker`, {
    search: normalized.search ?? "",
    typeNames: normalized.typeNames ?? typeNames,
    storeNames: normalized.storeNames ?? [],
    ...(normalized.scope ? { scope: normalized.scope } : {}),
    activeOnly: normalized.activeOnly ?? true
  });
}
