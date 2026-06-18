import { describe, expect, it, vi } from "vitest";
import { createAuthenticatedHttpClient, createSignalRAccessTokenFactory, withAuthenticatedSignalROptions, type AuthSession } from "../auth";

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
