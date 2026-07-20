import { createHttpClient, type StudioHttpClient } from "../../sdk";
import type { AuthProviderManager } from "../types";

export interface AuthenticatedHttpClientOptions {
  /** Default headers applied to every request before per-request RequestInit.headers are merged. */
  defaultHeaders?: HeadersInit;
  /** Alias for defaultHeaders retained for callers that pass fetch-like client options. */
  headers?: HeadersInit;
  /** Refresh before a bearer-less request and after a 401. Set false to send the original request unchanged. */
  refreshOnUnauthorized?: boolean;
  /** Short-circuit a request if an attempted refresh cannot issue a bearer. Use only for known protected endpoints. */
  requireAuthorization?: boolean;
  fetch?: typeof fetch;
}

/**
 * Decorates the SDK {@link createHttpClient} so backend requests attach a bearer token and transparently
 * refresh-and-retry on a 401. The six JSON verbs, JSON parsing, and problem-details error mapping all come
 * from the SDK client — this module only supplies an authenticating `fetch` so both stay in lockstep.
 */
export function createAuthenticatedHttpClient(
  baseUrl: string,
  auth: Pick<AuthProviderManager, "getAccessToken" | "refresh">,
  options: AuthenticatedHttpClientOptions = {}
): StudioHttpClient {
  return createHttpClient(baseUrl, {
    defaultHeaders: options.defaultHeaders ?? options.headers,
    applyTimeout: false,
    fetch: createAuthenticatedFetch(auth, options)
  });
}

const refreshInFlight = new WeakMap<Pick<AuthProviderManager, "refresh">, Map<string, Promise<boolean>>>();

function createAuthenticatedFetch(
  auth: Pick<AuthProviderManager, "getAccessToken" | "refresh">,
  options: AuthenticatedHttpClientOptions
): typeof fetch {
  const request = options.fetch ?? fetch;

  return (async (input: RequestInfo | URL, init?: RequestInit) => {
    const requestUrl = typeof input === "string" ? input : input.toString();
    const firstAttempt = await withBearerToken(requestUrl, auth, options, init);
    if (!firstAttempt.hasAuthorization && options.requireAuthorization === true && options.refreshOnUnauthorized !== false) {
      return authenticationRequiredResponse();
    }

    const firstResponse = await request(requestUrl, firstAttempt.init);
    if (firstResponse.status !== 401 || options.refreshOnUnauthorized === false) {
      return firstResponse;
    }

    const refreshed = await refreshOnce(requestUrl, auth);
    if (!refreshed) {
      return new Response("Authentication required.", { status: 401 });
    }

    const retryAttempt = await withBearerToken(requestUrl, auth, options, init, false);
    return retryAttempt.hasAuthorization
      ? request(requestUrl, retryAttempt.init)
      : authenticationRequiredResponse();
  }) as typeof fetch;
}

async function refreshOnce(requestUrl: string, auth: Pick<AuthProviderManager, "refresh">) {
  const key = new URL(requestUrl).origin;
  const refreshesByOrigin = refreshInFlight.get(auth) ?? new Map<string, Promise<boolean>>();
  refreshInFlight.set(auth, refreshesByOrigin);
  const existing = refreshesByOrigin.get(key);
  if (existing) {
    return existing;
  }

  const refresh = auth.refresh()
    .then(session => session.status === "authenticated")
    .finally(() => {
      refreshesByOrigin.delete(key);
      if (refreshesByOrigin.size === 0) {
        refreshInFlight.delete(auth);
      }
    });
  refreshesByOrigin.set(key, refresh);
  return refresh;
}

async function withBearerToken(
  requestUrl: string,
  auth: Pick<AuthProviderManager, "getAccessToken" | "refresh">,
  options: Pick<AuthenticatedHttpClientOptions, "refreshOnUnauthorized">,
  init?: RequestInit,
  refreshIfMissing = true
): Promise<{ init: RequestInit; hasAuthorization: boolean }> {
  const headers = new Headers(init?.headers);
  let token = await auth.getAccessToken();
  if (!token && !hasNonEmptyAuthorization(headers) && refreshIfMissing && options.refreshOnUnauthorized !== false) {
    await refreshOnce(requestUrl, auth);
    token = await auth.getAccessToken();
  }
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  return {
    init: {
      ...init,
      credentials: init?.credentials ?? "include",
      headers
    },
    hasAuthorization: hasNonEmptyAuthorization(headers)
  };
}

function authenticationRequiredResponse() {
  return new Response("Authentication required.", { status: 401 });
}

function hasNonEmptyAuthorization(headers: Headers) {
  return !!headers.get("Authorization")?.trim();
}
