import React from "react";
import {
  AuthProvider,
  RequireAuth,
  createAuthenticatedHttpClient,
  createBackendAuthProviderManager,
  createEndpointContext,
  createSignalRAccessTokenFactory,
  type AuthProviderManager,
  type StudioEndpointContext
} from "../../sdk";
import { isStudioAuthEnabled, type StudioRuntimeConfig } from "../runtime";

/**
 * Builds the user-auth provider manager for the shell, or returns null when no provider is configured.
 *
 * A null manager is the anonymous/no-provider contract: the shell boots without a login and backend
 * requests attach no bearer token. When configured, capability negotiation and the redirect login flow
 * are driven by {@link createBackendAuthProviderManager} against the backend's `/_elsa/identity/*` routes.
 */
export function createStudioAuthManager(config: StudioRuntimeConfig, baseUrl: string): AuthProviderManager | null {
  if (!isStudioAuthEnabled(config)) {
    return null;
  }

  return createBackendAuthProviderManager({
    baseUrl,
    tokenEndpoint: config.auth?.tokenEndpoint,
    refreshEndpoint: config.auth?.refreshEndpoint
  });
}

/**
 * Mounts {@link AuthProvider} and gates the tree behind {@link RequireAuth} when a provider is configured.
 * When `manager` is null the children render unguarded so a dev/no-provider deployment keeps booting.
 */
export function StudioAuthBoundary({
  manager,
  children
}: {
  manager: AuthProviderManager | null;
  children: React.ReactNode;
}) {
  if (!manager) {
    return <>{children}</>;
  }

  return (
    <AuthProvider manager={manager}>
      <RequireAuth fallback={<StudioSigningIn />}>{children}</RequireAuth>
    </AuthProvider>
  );
}

/**
 * Builds an endpoint context whose HTTP client attaches bearer tokens (and refresh-retries on 401) when a
 * user session exists. When `manager` is null it falls back to the plain SDK client, preserving the
 * anonymous path. `headers` (e.g. the #183 management-key header) is composed into every request either
 * way, so endpoint-auth keeps working alongside user auth.
 *
 * `managementKey` decides the hub credential (`accessTokenFactory`) and applies ONLY to the host
 * management surface. Precedence: the management key wins over the user-JWT factory. The host's built-in
 * gate validates the API key alone, so the JWT is never the right credential for it; deployments that
 * rebind that policy to real identity do not hand a management key to the SPA, so key-absent falls through
 * to the existing manager-based factory (unchanged behavior). This only steers non-HTTP transports
 * (SignalR hubs) — host REST calls keep attaching the user JWT via the authenticated HTTP client, which
 * reads the token from the manager independently of `accessTokenFactory`. The backend context must NOT
 * receive a management key here: it would leak the shell secret cross-origin (#215 finding 2).
 */
export function createStudioEndpointContext(
  baseUrl: string,
  manager: AuthProviderManager | null,
  headers?: HeadersInit,
  managementKey?: string
): StudioEndpointContext {
  const accessTokenFactory = createHubCredentialFactory(manager, managementKey);

  if (!manager) {
    return { ...createEndpointContext(baseUrl, { headers }), accessTokenFactory };
  }

  return {
    baseUrl,
    headers,
    http: createAuthenticatedHttpClient(baseUrl, manager, { defaultHeaders: headers }),
    accessTokenFactory
  };
}

/**
 * Resolves the SignalR hub credential per the precedence documented on {@link createStudioEndpointContext}:
 * management key first, then the manager-JWT factory, else none.
 */
function createHubCredentialFactory(
  manager: AuthProviderManager | null,
  managementKey?: string
): (() => Promise<string>) | undefined {
  if (managementKey) {
    return () => Promise.resolve(managementKey);
  }

  return manager ? createSignalRAccessTokenFactory(manager) : undefined;
}

function StudioSigningIn() {
  return (
    <div className="studio-shell studio-auth-signing-in" role="status" aria-live="polite">
      <div className="empty-state">Signing in…</div>
    </div>
  );
}
