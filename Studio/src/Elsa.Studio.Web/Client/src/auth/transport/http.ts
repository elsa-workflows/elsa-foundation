import { readStudioHttpErrorMessage, StudioHttpError, withDefaultHeaders, type StudioHttpClient } from "../../sdk";
import type { AuthProviderManager } from "../types";

export interface AuthenticatedHttpClientOptions {
  /** Default headers applied to every request before per-request RequestInit.headers are merged. */
  defaultHeaders?: HeadersInit;
  /** Alias for defaultHeaders retained for callers that pass fetch-like client options. */
  headers?: HeadersInit;
  refreshOnUnauthorized?: boolean;
  fetch?: typeof fetch;
}

export function createAuthenticatedHttpClient(
  baseUrl: string,
  auth: Pick<AuthProviderManager, "getAccessToken" | "refresh">,
  options: AuthenticatedHttpClientOptions = {}
): StudioHttpClient {
  return {
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
    }
  };
}

async function requestJson<T>(
  baseUrl: string,
  url: string,
  auth: Pick<AuthProviderManager, "getAccessToken" | "refresh">,
  options: AuthenticatedHttpClientOptions,
  init?: RequestInit
) {
  const request = options.fetch ?? fetch;
  const requestUrl = new URL(url, baseUrl).toString();
  const firstResponse = await request(requestUrl, await withBearerToken(auth, withConfiguredDefaultHeaders(options, init)));
  const response = firstResponse.status === 401 && options.refreshOnUnauthorized !== false
    ? await retryAfterRefresh(request, requestUrl, auth, withConfiguredDefaultHeaders(options, init))
    : firstResponse;

  if (!response.ok) {
    throw new StudioHttpError(response.status, await readStudioHttpErrorMessage(response));
  }

  const text = await response.text();
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
  const refreshed = await auth.refresh();
  if (refreshed.status !== "authenticated") {
    return new Response("Authentication required.", { status: 401 });
  }

  return request(requestUrl, await withBearerToken(auth, init));
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

function withConfiguredDefaultHeaders(options: AuthenticatedHttpClientOptions, init?: RequestInit) {
  return withDefaultHeaders(options.defaultHeaders ?? options.headers, init);
}
