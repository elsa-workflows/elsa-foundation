import { createAuthProviderManager, type AuthProviderManagerOptions } from "./AuthProviderManager";
import { createRedirectAuthAdapter } from "./adapters/redirect";
import type { AuthBootstrap, AuthCapabilities, AuthProviderManager } from "./types";

export interface BackendAuthProviderManagerOptions {
  baseUrl?: string;
  fetch?: typeof fetch;
  isCallback?: AuthProviderManagerOptions["isCallback"];
}

export function createBackendAuthProviderManager(options: BackendAuthProviderManagerOptions = {}): AuthProviderManager {
  const baseUrl = options.baseUrl ?? window.location.origin;
  const request = options.fetch ?? fetch;

  return createAuthProviderManager({
    bootstrap: () => readJson<AuthBootstrap>(request, baseUrl, "/_elsa/identity/bootstrap"),
    capabilities: () => readJson<AuthCapabilities>(request, baseUrl, "/_elsa/identity/capabilities"),
    isCallback: options.isCallback,
    adapterFactory: provider => createRedirectAuthAdapter({
      id: provider.id,
      kind: provider.kind,
      baseUrl,
      challenge: provider.challenge,
      fetch: request
    })
  });
}

async function readJson<T>(request: typeof fetch, baseUrl: string, path: string) {
  const response = await request(new URL(path, baseUrl).toString(), {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" }
  });

  if (!response.ok) {
    throw new Error(`Auth discovery request failed with ${response.status}.`);
  }

  return await response.json() as T;
}
