import { createHttpClient, type StudioHttpClient } from "../../sdk";
import type { AuthProviderManager } from "../types";

export interface AuthenticatedHttpClientOptions {
  /** Default headers applied to every request before per-request RequestInit.headers are merged. */
  defaultHeaders?: HeadersInit;
  /** Alias for defaultHeaders retained for callers that pass fetch-like client options. */
  headers?: HeadersInit;
  refreshOnUnauthorized?: boolean;
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

const refreshInFlight = new Map<string, Promise<boolean>>();

function createAuthenticatedFetch(
  auth: Pick<AuthProviderManager, "getAccessToken" | "refresh">,
  options: AuthenticatedHttpClientOptions
): typeof fetch {
  const request = options.fetch ?? fetch;

  return (async (input: RequestInfo | URL, init?: RequestInit) => {
    const requestUrl = typeof input === "string" ? input : input.toString();
    const firstResponse = await request(requestUrl, await withBearerToken(auth, init));
    if (firstResponse.status !== 401 || options.refreshOnUnauthorized === false) {
      return firstResponse;
    }

    const refreshed = await refreshOnce(requestUrl, auth);
    if (!refreshed) {
      return new Response("Authentication required.", { status: 401 });
    }

    return request(requestUrl, await withBearerToken(auth, init));
  }) as typeof fetch;
}

async function refreshOnce(requestUrl: string, auth: Pick<AuthProviderManager, "refresh">) {
  const key = new URL(requestUrl).origin;
  const existing = refreshInFlight.get(key);
  if (existing) {
    return existing;
  }

  const refresh = auth.refresh()
    .then(session => session.status === "authenticated")
    .finally(() => refreshInFlight.delete(key));
  refreshInFlight.set(key, refresh);
  return refresh;
}

async function withBearerToken(auth: Pick<AuthProviderManager, "getAccessToken">, init?: RequestInit): Promise<RequestInit> {
  const headers = new Headers(init?.headers);
  const token = await auth.getAccessToken();
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  return {
    ...init,
    credentials: init?.credentials ?? "include",
    headers
  };
}
