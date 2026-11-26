import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createAuthenticatedHttpClient,
  createBackendAuthProviderManager,
  type AuthBootstrap,
  type AuthSession
} from "../auth";

// These tests exercise the *real* backend provider manager (not a stubbed AuthProviderManager) against a
// simulated identity backend, proving #208: the default manager now wires the token endpoint so a real
// bearer token is attached, and 401 triggers a refresh-retry.
describe("backend auth token endpoint wiring", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("mints a bearer token from the default /_elsa/identity/token endpoint", async () => {
    const backend = simulateIdentityBackend();
    const manager = createBackendAuthProviderManager({ baseUrl: "https://foundation.example/", fetch: backend.fetch });

    const token = await manager.getAccessToken();

    expect(token).toBe("access-1");
    expect(backend.tokenRequests).toEqual(["https://foundation.example/_elsa/identity/token"]);
  });

  it("honours a configured token endpoint override", async () => {
    const backend = simulateIdentityBackend({ tokenPath: "/custom/token" });
    const manager = createBackendAuthProviderManager({
      baseUrl: "https://foundation.example/",
      fetch: backend.fetch,
      tokenEndpoint: "/custom/token"
    });

    await expect(manager.getAccessToken()).resolves.toBe("access-1");
    expect(backend.tokenRequests).toEqual(["https://foundation.example/custom/token"]);
  });

  it("attaches the bearer token and refresh-retries once on a 401 from the backend", async () => {
    const backend = simulateIdentityBackend();
    const manager = createBackendAuthProviderManager({ baseUrl: "https://foundation.example/", fetch: backend.fetch });
    const client = createAuthenticatedHttpClient("https://foundation.example/", manager, { fetch: backend.fetch });

    // The protected resource 401s the first time (stale token) then succeeds after the refresh mints a new one.
    const result = await client.getJson<{ ok: boolean }>("/_elsa/protected");

    expect(result).toEqual({ ok: true });
    // Two attempts at the resource: the first Bearer is the stale token, the retry carries the rotated token.
    expect(backend.resourceAuthHeaders).toEqual(["Bearer access-1", "Bearer access-2"]);
    // The 401 drove a session re-probe (cookie refresh) between the two attempts.
    expect(backend.sessionRequests).toBe(1);
  });
});

interface SimulatedBackend {
  fetch: typeof fetch;
  tokenRequests: string[];
  resourceAuthHeaders: string[];
  sessionRequests: number;
}

function simulateIdentityBackend(options: { tokenPath?: string } = {}): SimulatedBackend {
  const tokenPath = options.tokenPath ?? "/_elsa/identity/token";
  const state = { tokenRequests: [] as string[], resourceAuthHeaders: [] as string[], sessionRequests: 0, issued: 0, resourceHits: 0 };

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const path = new URL(url).pathname;

    if (path === "/_elsa/identity/bootstrap") {
      return json<AuthBootstrap>({
        ownershipMode: "foundation-owned",
        providers: [{ id: "openiddict", kind: "openiddict", enabled: true, isDefault: true }]
      });
    }

    if (path === tokenPath) {
      state.tokenRequests.push(url);
      state.issued += 1;
      return json({ accessToken: `access-${state.issued}` });
    }

    if (path === "/_elsa/identity/session") {
      state.sessionRequests += 1;
      return json<AuthSession>({ status: "authenticated", subject: "alice", roles: [], permissions: [] });
    }

    if (path === "/_elsa/protected") {
      state.resourceHits += 1;
      state.resourceAuthHeaders.push(new Headers(init?.headers).get("Authorization") ?? "");
      // First hit is unauthorized (stale token); the retry after refresh succeeds.
      return state.resourceHits === 1
        ? new Response("", { status: 401 })
        : json({ ok: true });
    }

    throw new Error(`Unexpected request ${url}`);
  });

  vi.stubGlobal("fetch", fetchMock);

  return {
    fetch: fetchMock as unknown as typeof fetch,
    get tokenRequests() {
      return state.tokenRequests;
    },
    get resourceAuthHeaders() {
      return state.resourceAuthHeaders;
    },
    get sessionRequests() {
      return state.sessionRequests;
    }
  };
}

function json<T>(payload: T) {
  return new Response(JSON.stringify(payload), { status: 200, headers: { "content-type": "application/json" } });
}
