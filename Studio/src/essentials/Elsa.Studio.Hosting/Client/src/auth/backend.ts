import { createAuthProviderManager, type AuthProviderManagerOptions } from "./AuthProviderManager";
import { createRedirectAuthAdapter } from "./adapters/redirect";
import type { AuthBootstrap, AuthCapabilities, AuthProviderManager } from "./types";
import { resolveBackendPath } from "../sdk/backends";

/** Default endpoint the redirect adapter exchanges the session cookie for a bearer token at. */
export const defaultBackendTokenEndpoint = "/_elsa/identity/token";

export interface BackendAuthProviderManagerOptions {
  /** Resolve identity endpoints inside the complete selected backend shell URL. Legacy hosts retain root paths. */
  shellRelative?: boolean;
  baseUrl?: string;
  fetch?: typeof fetch;
  isCallback?: AuthProviderManagerOptions["isCallback"];
  getCallbackProviderId?: AuthProviderManagerOptions["getCallbackProviderId"];
  /**
   * Endpoint each provider's redirect adapter calls (with the session cookie) to mint a bearer token.
   * Defaults to {@link defaultBackendTokenEndpoint}. Without this the adapter's `getAccessToken` returns
   * null and backend requests attach no `Authorization` header.
   */
  tokenEndpoint?: string;
  /** Optional refresh endpoint; when omitted, refresh re-probes the session cookie (the cookie flow). */
  refreshEndpoint?: string | null;
  /** Supplies the client-held refresh token when a {@link refreshEndpoint} is configured. */
  getRefreshToken?: () => string | null | Promise<string | null>;
}

export function createBackendAuthProviderManager(options: BackendAuthProviderManagerOptions = {}): AuthProviderManager {
  const baseUrl = options.baseUrl ?? window.location.origin;
  const request = options.fetch ?? fetch;
  const tokenEndpoint = options.tokenEndpoint ?? defaultBackendTokenEndpoint;
  const path = (value: string) => options.shellRelative ? resolveBackendPath(baseUrl,value) : value;

  return createAuthProviderManager({
    bootstrap: () => readJson<AuthBootstrap>(request, baseUrl, path("/_elsa/identity/bootstrap")),
    capabilities: () => readJson<AuthCapabilities>(request, baseUrl, path("/_elsa/identity/capabilities")),
    isCallback: options.isCallback,
    getCallbackProviderId: options.getCallbackProviderId,
    adapterFactory: provider => createRedirectAuthAdapter({
      id: provider.id,
      kind: provider.kind,
      baseUrl,
      challenge: options.shellRelative && provider.challenge && "url" in provider.challenge && provider.challenge.url.startsWith("/")
        ? {...provider.challenge,url:path(provider.challenge.url)}
        : options.shellRelative && provider.challenge && "loginPath" in provider.challenge
          ? {...provider.challenge,loginPath:path(provider.challenge.loginPath)} : provider.challenge,
      sessionEndpoint: path("/_elsa/identity/session"),
      logoutEndpoint: path(`/_elsa/identity/logout/${encodeURIComponent(provider.id)}`),
      tokenEndpoint: path(tokenEndpoint),
      refreshEndpoint: options.refreshEndpoint ? path(options.refreshEndpoint) : options.refreshEndpoint,
      getRefreshToken: options.getRefreshToken,
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
