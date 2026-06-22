import { createStudioHttpError, StudioHttpError, type StudioHttpClient } from "../../sdk";
import type { AuthProviderManager } from "../types";

export interface AuthenticatedHttpClientOptions {
  refreshOnUnauthorized?: boolean;
  fetch?: typeof fetch;
}

export function createAuthenticatedHttpClient(
  baseUrl: string,
  auth: Pick<AuthProviderManager, "getAccessToken" | "refresh">,
  options: AuthenticatedHttpClientOptions = {}
): StudioHttpClient {
  return {
    requestJson<T>(url: string, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, auth, options, withJsonAccept(init));
    },
    getJson<T>(url: string, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, auth, options, withJsonAccept(init));
    },
    postJson<T>(url: string, body: unknown, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, auth, options, {
        ...init,
        method: "POST",
        headers: withJsonContentTypeAndAccept(init?.headers),
        body: JSON.stringify(body)
      });
    },
    putJson<T>(url: string, body: unknown, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, auth, options, {
        ...init,
        method: "PUT",
        headers: withJsonContentTypeAndAccept(init?.headers),
        body: JSON.stringify(body)
      });
    },
    deleteJson<T>(url: string, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, auth, options, withJsonAccept({
        ...init,
        method: "DELETE"
      }));
    },
    postForm<T>(url: string, body: FormData, init?: RequestInit) {
      return requestJson<T>(baseUrl, url, auth, options, withJsonAccept({
        ...init,
        method: "POST",
        body
      }));
    }
  };
}

const refreshInFlight = new Map<string, Promise<boolean>>();

async function requestJson<T>(
  baseUrl: string,
  url: string,
  auth: Pick<AuthProviderManager, "getAccessToken" | "refresh">,
  options: AuthenticatedHttpClientOptions,
  init?: RequestInit
) {
  const request = options.fetch ?? fetch;
  const requestUrl = new URL(url, baseUrl).toString();
  const firstResponse = await request(requestUrl, await withBearerToken(auth, init));
  const response = firstResponse.status === 401 && options.refreshOnUnauthorized !== false
    ? await retryAfterRefresh(request, requestUrl, auth, init)
    : firstResponse;

  if (!response.ok) {
    throw await createStudioHttpError(response);
  }

  const text = await response.text();
  if (!text.trim()) {
    return {} as T;
  }

  try {
    return JSON.parse(text) as T;
  } catch {
    throw new StudioHttpError(response.status, `Expected JSON from ${requestUrl}.`);
  }
}

async function retryAfterRefresh(
  request: typeof fetch,
  requestUrl: string,
  auth: Pick<AuthProviderManager, "getAccessToken" | "refresh">,
  init?: RequestInit
) {
  const refreshed = await refreshOnce(requestUrl, auth);
  if (!refreshed) {
    return new Response("Authentication required.", { status: 401 });
  }

  return request(requestUrl, await withBearerToken(auth, init));
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

function withJsonAccept(init?: RequestInit): RequestInit | undefined {
  const headers = new Headers(init?.headers);
  if (!headers.has("Accept")) {
    headers.set("Accept", "application/json");
  }

  return {
    ...init,
    cache: init?.cache ?? "no-store",
    headers
  };
}

function withJsonContentTypeAndAccept(headers?: HeadersInit) {
  const result = new Headers(headers);
  if (!result.has("Content-Type")) {
    result.set("Content-Type", "application/json");
  }
  if (!result.has("Accept")) {
    result.set("Accept", "application/json");
  }

  return result;
}
