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
 */
export function createStudioEndpointContext(
  baseUrl: string,
  manager: AuthProviderManager | null,
  headers?: HeadersInit
): StudioEndpointContext {
  if (!manager) {
    return createEndpointContext(baseUrl, { headers });
  }

  return {
    baseUrl,
    headers,
    http: createAuthenticatedHttpClient(baseUrl, manager, { defaultHeaders: headers }),
    // Expose a SignalR access-token factory so transports that can't use the HTTP client (e.g. hub
    // connections in modules) attach the same bearer token. Built from the tested SignalR auth transport.
    accessTokenFactory: createSignalRAccessTokenFactory(manager)
  };
}

function StudioSigningIn() {
  return (
    <div className="studio-shell studio-auth-signing-in" role="status" aria-live="polite">
      <div className="empty-state">Signing in…</div>
    </div>
  );
}
