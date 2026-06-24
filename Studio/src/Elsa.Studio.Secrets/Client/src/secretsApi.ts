import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { CreateSecretRequest, ListSecretsResponse, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata, SecretPickerResponse } from "./secretTypes";

const basePath = "/_elsa/secrets";

export async function listSecrets(context: StudioEndpointContext, search = "") {
  const parameters = new URLSearchParams({ activeOnly: "false", pageSize: "100" });
  if (search.trim()) parameters.set("search", search.trim());
  return context.http.getJson<ListSecretsResponse>(`${basePath}?${parameters.toString()}`);
}

export async function getSecret(context: StudioEndpointContext, name: string) {
  return context.http.getJson<SecretMetadata>(`${basePath}/${encodeURIComponent(name)}`);
}

export async function createSecret(context: StudioEndpointContext, request: CreateSecretRequest) {
  return context.http.postJson<SecretMetadata>(basePath, request);
}

export async function rotateSecret(context: StudioEndpointContext, name: string, request: RotateSecretRequest) {
  return context.http.postJson<SecretMetadata>(`${basePath}/${encodeURIComponent(name)}/rotate`, request);
}

export async function testSecret(context: StudioEndpointContext, name: string) {
  return context.http.postJson<{ succeeded: boolean; code: string; message?: string }>(`${basePath}/${encodeURIComponent(name)}/test`, {});
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

export async function pickSecrets(context: StudioEndpointContext, search = "", typeNames: string[] = []) {
  return context.http.postJson<SecretPickerResponse>(`${basePath}/picker`, { search, typeNames, activeOnly: true });
}
