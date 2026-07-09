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
import elsaLogo from "../../assets/images/icon.png";

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
 * anonymous path. `headers` is composed into every request either way, so endpoint-auth keeps working
 * alongside user auth.
 *
 * The SignalR hub credential (`accessTokenFactory`) is the manager-JWT factory when a provider is configured,
 * else none. After ADR 0037 / #248 the browser no longer holds a host management key, so there is no
 * management-key hub credential to prefer: Studio's own host-control surface (including the console-stream
 * hub) is gated by the Studio bridge user-session gate, which accepts the same backend-issued bearer the
 * JWT factory yields (or allows anonymously in demo mode).
 */
export function createStudioEndpointContext(
  baseUrl: string,
  manager: AuthProviderManager | null,
  headers?: HeadersInit
): StudioEndpointContext {
  const accessTokenFactory = manager ? createSignalRAccessTokenFactory(manager) : undefined;

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

function StudioSigningIn() {
  return (
    <div className="studio-shell studio-auth-signing-in" role="status" aria-live="polite" aria-busy="true">
      <div className="studio-auth-card">
        <span className="studio-auth-brand-mark" aria-hidden="true">
          <img src={elsaLogo} alt="" />
        </span>
        <span className="studio-auth-copy">
          <strong>Elsa Studio</strong>
          <span>Signing in…</span>
        </span>
        <span className="studio-auth-spinner" aria-hidden="true" />
      </div>
    </div>
  );
}
