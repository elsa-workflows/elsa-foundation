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
