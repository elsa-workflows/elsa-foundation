import { describe, expect, it, vi } from "vitest";
import { createAuthenticatedHttpClient, createSignalRAccessTokenFactory, withAuthenticatedSignalROptions, type AuthSession } from "../auth";
import { describeApiError, tryExtractValidationErrors } from "../sdk";

describe("authenticated HTTP transport", () => {
  it("attaches bearer tokens through the provider manager", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const auth = stubAuth("token-1");
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock });

    await client.getJson("/_elsa/workflows");

    const init = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(fetchMock.mock.calls[0]?.[0]).toBe("https://foundation.example/_elsa/workflows");
    expect(new Headers(init.headers).get("Authorization")).toBe("Bearer token-1");
  });

  it("merges configured default headers before adding bearer tokens", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const auth = stubAuth("token-1");
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, {
      fetch: fetchMock,
      defaultHeaders: { "X-Elsa-Tenant": "tenant-a", Authorization: "Basic ignored" }
    });

    await client.getJson("/_elsa/workflows", { headers: { "X-Request": "request-a" } });

    const headers = new Headers((fetchMock.mock.calls[0]?.[1] as RequestInit).headers);
    expect(headers.get("X-Elsa-Tenant")).toBe("tenant-a");
    expect(headers.get("X-Request")).toBe("request-a");
    expect(headers.get("Authorization")).toBe("Bearer token-1");
  });

  it("surfaces problem details messages from failed responses", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({
      errors: { name: ["Name is required."] }
    }), { status: 400, headers: { "content-type": "application/problem+json" } }));
    const auth = stubAuth("token-1");
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock });

    await expect(client.getJson("/_elsa/workflows")).rejects.toMatchObject({ message: "Name is required." });
  });

  it("refreshes once on unauthorized responses and retries with the new token", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response("expired", { status: 401 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const auth = stubAuth("token-1", "token-2");
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock });

    await expect(client.getJson("/_elsa/workflows")).resolves.toEqual({ ok: true });

    expect(auth.refresh).toHaveBeenCalledTimes(1);
    const retryInit = fetchMock.mock.calls[1]?.[1] as RequestInit;
    expect(new Headers(retryInit.headers).get("Authorization")).toBe("Bearer token-2");
  });

  it("refreshes a missing bearer before a Dashboard Run Health request, preventing a login document from being parsed as JSON", async () => {
    let token: string | null = null;
    const auth = {
      getAccessToken: vi.fn(async () => token),
      refresh: vi.fn(async (): Promise<AuthSession> => {
        token = "token-2";
        return { status: "authenticated", subject: "alice", roles: [], permissions: [], provider: { id: "builtin", kind: "openiddict" } };
      })
    };
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(typeof input === "string" ? input : input.toString());
      const authorization = new Headers(init?.headers).get("Authorization");

      if (url.pathname === "/_elsa/workflows/dashboard/runs") {
        return authorization === "Bearer token-2"
          ? new Response(JSON.stringify({ status: "ready", includeTestRuns: true }), { status: 200 })
          : new Response("<html>sign in</html>", { status: 200, headers: { "content-type": "text/html" } });
      }

      throw new Error(`Unexpected request ${url}`);
    });
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock, requireAuthorization: true });

    await expect(client.getJson("/_elsa/workflows/dashboard/runs?includeTestRuns=true"))
      .resolves.toEqual({ status: "ready", includeTestRuns: true });

    expect(auth.refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(new Headers((fetchMock.mock.calls[0]?.[1] as RequestInit).headers).get("Authorization")).toBe("Bearer token-2");
  });

  it("fails closed without contacting a protected endpoint when preflight refresh does not issue a bearer", async () => {
    const fetchMock = vi.fn(async () => new Response("<html>sign in</html>", { status: 200, headers: { "content-type": "text/html" } }));
    const auth = {
      getAccessToken: vi.fn(async () => null),
      refresh: vi.fn(async (): Promise<AuthSession> => ({ status: "anonymous", roles: [], permissions: [] }))
    };
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock, requireAuthorization: true });

    await expect(client.getJson("/_elsa/workflows/dashboard/runs")).rejects.toMatchObject({ status: 401 });

    expect(auth.refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("keeps public requests bearer-less when a preflight refresh finds no session", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ public: true }), { status: 200 }));
    const auth = {
      getAccessToken: vi.fn(async () => null),
      refresh: vi.fn(async (): Promise<AuthSession> => ({ status: "anonymous", roles: [], permissions: [] }))
    };
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock });

    await expect(client.getJson("/_elsa/public/status")).resolves.toEqual({ public: true });

    expect(auth.refresh).toHaveBeenCalledTimes(1);
    expect(new Headers((fetchMock.mock.calls[0]?.[1] as RequestInit).headers).get("Authorization")).toBeNull();
  });

  it("preserves a caller-supplied authorization header without refreshing the provider", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const auth = {
      getAccessToken: vi.fn(async () => null),
      refresh: vi.fn(async (): Promise<AuthSession> => ({ status: "anonymous", roles: [], permissions: [] }))
    };
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock, requireAuthorization: true });

    await expect(client.getJson("/_elsa/partner", { headers: { Authorization: "Bearer caller-token" } })).resolves.toEqual({ ok: true });

    expect(auth.refresh).not.toHaveBeenCalled();
    expect(new Headers((fetchMock.mock.calls[0]?.[1] as RequestInit).headers).get("Authorization")).toBe("Bearer caller-token");
  });

  it.each(["", "   "])("does not treat a blank caller authorization header as a credential (%j)", async authorization => {
    const fetchMock = vi.fn(async () => new Response("<html>sign in</html>", { status: 200, headers: { "content-type": "text/html" } }));
    const auth = {
      getAccessToken: vi.fn(async () => null),
      refresh: vi.fn(async (): Promise<AuthSession> => ({ status: "anonymous", roles: [], permissions: [] }))
    };
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock, requireAuthorization: true });

    await expect(client.getJson("/_elsa/workflows/dashboard/runs", { headers: { Authorization: authorization } })).rejects.toMatchObject({ status: 401 });

    expect(auth.refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("does not preflight refresh when unauthorized retries are disabled", async () => {
    const fetchMock = vi.fn(async () => new Response("unauthorized", { status: 401 }));
    const auth = {
      getAccessToken: vi.fn(async () => null),
      refresh: vi.fn(async (): Promise<AuthSession> => ({ status: "authenticated", subject: "alice", roles: [], permissions: [] }))
    };
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, {
      fetch: fetchMock,
      refreshOnUnauthorized: false,
      requireAuthorization: true
    });

    await expect(client.getJson("/_elsa/workflows")).rejects.toMatchObject({ status: 401 });

    expect(auth.refresh).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(new Headers((fetchMock.mock.calls[0]?.[1] as RequestInit).headers).get("Authorization")).toBeNull();
  });

  it("shares one refresh across concurrent unauthorized responses for the same origin", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response("expired", { status: 401 }))
      .mockResolvedValueOnce(new Response("expired", { status: 401 }))
      .mockImplementation(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const auth = stubAuth("token-1", "token-2");
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock });

    await expect(Promise.all([
      client.getJson("/_elsa/workflows"),
      client.postJson("/_elsa/workflows", { name: "Workflow" })
    ])).resolves.toEqual([{ ok: true }, { ok: true }]);

    expect(auth.refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(4);
    const retryHeaders = [fetchMock.mock.calls[2]?.[1], fetchMock.mock.calls[3]?.[1]]
      .map(init => new Headers((init as RequestInit).headers).get("Authorization"));
    expect(retryHeaders).toEqual(["Bearer token-2", "Bearer token-2"]);
  });

  it("shares one preflight refresh across concurrent bearer-less requests to the same origin", async () => {
    let token: string | null = null;
    let releaseRefresh: (() => void) | undefined;
    let signalRefreshStarted: (() => void) | undefined;
    const refreshStarted = new Promise<void>(resolve => { signalRefreshStarted = resolve; });
    const refreshReleased = new Promise<void>(resolve => { releaseRefresh = resolve; });
    const auth = {
      getAccessToken: vi.fn(async () => token),
      refresh: vi.fn(async (): Promise<AuthSession> => {
        signalRefreshStarted?.();
        await refreshReleased;
        token = "token-2";
        return { status: "authenticated", subject: "alice", roles: [], permissions: [] };
      })
    };
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock });

    const requests = Promise.all([client.getJson("/_elsa/workflows"), client.getJson("/_elsa/workflows/dashboard/runs")]);
    await refreshStarted;
    expect(auth.refresh).toHaveBeenCalledTimes(1);
    releaseRefresh?.();

    await expect(requests).resolves.toEqual([{ ok: true }, { ok: true }]);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls.map(([, init]) => new Headers((init as RequestInit).headers).get("Authorization")))
      .toEqual(["Bearer token-2", "Bearer token-2"]);
  });

  it("does not share a same-origin refresh between distinct auth managers", async () => {
    let tokenA: string | null = null;
    let tokenB: string | null = null;
    const authA = {
      getAccessToken: vi.fn(async () => tokenA),
      refresh: vi.fn(async (): Promise<AuthSession> => {
        tokenA = "token-a";
        return { status: "authenticated", subject: "alice", roles: [], permissions: [] };
      })
    };
    const authB = {
      getAccessToken: vi.fn(async () => tokenB),
      refresh: vi.fn(async (): Promise<AuthSession> => {
        tokenB = "token-b";
        return { status: "authenticated", subject: "bob", roles: [], permissions: [] };
      })
    };
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const clientA = createAuthenticatedHttpClient("https://foundation.example/", authA, { fetch: fetchMock });
    const clientB = createAuthenticatedHttpClient("https://foundation.example/", authB, { fetch: fetchMock });

    await expect(Promise.all([clientA.getJson("/_elsa/workflows"), clientB.getJson("/_elsa/workflows")]))
      .resolves.toEqual([{ ok: true }, { ok: true }]);

    expect(authA.refresh).toHaveBeenCalledTimes(1);
    expect(authB.refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls.map(([, init]) => new Headers((init as RequestInit).headers).get("Authorization")))
      .toEqual(["Bearer token-a", "Bearer token-b"]);
  });

  it("uses shared problem-details parsing for authenticated request failures", async () => {
    const fetchMock = vi.fn(async () => new Response(
      JSON.stringify({ errors: { Name: ["Name is required."] } }),
      { status: 422, headers: { "content-type": "application/json" } }));
    const auth = stubAuth("token-1");
    const client = createAuthenticatedHttpClient("https://foundation.example/", auth, { fetch: fetchMock });

    const error = await client.postJson("/_elsa/workflows", {}).catch(e => e);

    await expect(describeApiError(error)).resolves.toContain("Name is required.");
    await expect(tryExtractValidationErrors(error)).resolves.toEqual({ Name: ["Name is required."] });
  });
});

describe("SignalR auth transport", () => {
  it("creates an accessTokenFactory backed by the provider manager", async () => {
    const auth = stubAuth("signalr-token");
    const factory = createSignalRAccessTokenFactory(auth);

    await expect(factory()).resolves.toBe("signalr-token");
  });

  it("preserves existing SignalR options while adding auth", async () => {
    const auth = stubAuth("signalr-token");
    const options = withAuthenticatedSignalROptions({ withCredentials: true }, auth);

    expect(options.withCredentials).toBe(true);
    await expect(options.accessTokenFactory()).resolves.toBe("signalr-token");
  });

  it("uses an existing SignalR accessTokenFactory when auth has no token", async () => {
    const auth = stubAuth();
    const fallbackAccessTokenFactory = vi.fn(async () => "hub-token");
    const options = withAuthenticatedSignalROptions({ accessTokenFactory: fallbackAccessTokenFactory }, auth);

    await expect(options.accessTokenFactory()).resolves.toBe("hub-token");
    expect(fallbackAccessTokenFactory).toHaveBeenCalledTimes(1);
  });

  it("preserves the receiver for an existing SignalR accessTokenFactory", async () => {
    const auth = stubAuth();
    const options = withAuthenticatedSignalROptions({
      hubToken: "hub-token",
      accessTokenFactory(this: { hubToken: string }) {
        return this.hubToken;
      }
    }, auth);

    await expect(options.accessTokenFactory()).resolves.toBe("hub-token");
  });
});

function stubAuth(...tokens: string[]) {
  let tokenIndex = 0;

  return {
    getAccessToken: vi.fn(async () => tokens[Math.min(tokenIndex, tokens.length - 1)] ?? null),
    refresh: vi.fn(async (): Promise<AuthSession> => {
      tokenIndex += 1;
      return {
        status: "authenticated",
        subject: "alice",
        roles: [],
        permissions: [],
        provider: { id: "entra", kind: "external-oidc" }
      };
    })
  };
}
