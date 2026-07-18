import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "../app/App";
import { createStudioAuthManager, createStudioEndpointContext, StudioAuthBoundary } from "../app/auth/studioAuth";
import type { AuthProviderManager, AuthSession, StudioModulesResponse } from "../sdk";

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = "";
  delete window.__ELSA_STUDIO_RUNTIME__;
});

describe("studio auth mounting", () => {
  it("boots anonymously and attaches no bearer token when no provider is configured", async () => {
    // No runtime auth config -> anonymous contract: the shell must boot with no login gate.
    const fetchMock = stubShellFetch();
    const { container, unmount } = await renderApp();

    expect(createStudioAuthManager({}, "https://studio.example")).toBeNull();
    expect(container.textContent).toContain("Dashboard");
    expect(container.textContent).not.toContain("Signing in");

    // Every request the shell issued must be unauthenticated.
    for (const call of fetchMock.mock.calls) {
      const headers = new Headers((call[1] as RequestInit | undefined)?.headers);
      expect(headers.get("Authorization")).toBeNull();
    }

    await unmount();
  });

  it("gates the shell behind RequireAuth while the session is anonymous, then reveals it once authenticated", async () => {
    let session: AuthSession = { status: "anonymous", roles: [], permissions: [] };
    const manager = stubManager(() => session);
    manager.login = vi.fn(async () => {
      session = { status: "authenticated", subject: "alice", displayName: "Alice", roles: [], permissions: [] };
    });
    stubShellFetch();

    const { container, flush, unmount } = renderBoundary(manager);

    // The guard shows the fallback (not the shell) and starts the login flow while the session is anonymous.
    expect(container.textContent).toContain("Signing in");
    expect(container.textContent).not.toContain("Dashboard");

    await flush();
    await flushPromises();
    expect(manager.login).toHaveBeenCalledTimes(1);

    // After login resolves to an authenticated session the guarded shell renders.
    expect(container.textContent).toContain("Dashboard");

    await unmount();
  });

  it("shows a retryable error when the authentication backend is unavailable", async () => {
    const manager = stubManager(() => ({ status: "unknown", roles: [], permissions: [] }));
    manager.initialize = vi.fn(async () => {
      throw new TypeError("Failed to fetch");
    });
    manager.login = vi.fn()
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValueOnce(undefined);
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    const { container, flush, unmount } = renderBoundary(manager);

    // The error UI needs two settled rejections (initialize, then the login the anonymous
    // session triggers), each followed by a passive-effect/render cycle whose scheduling
    // races a fixed flush count — poll instead of counting flushes.
    await vi.waitFor(async () => {
      await flush();
      expect(container.textContent).toContain("Unable to sign in");
    });
    expect(container.textContent).toContain("authentication service may be unavailable");
    expect(container.querySelector('[role="alert"]')).not.toBeNull();

    const retryButton = Array.from(container.querySelectorAll("button"))
      .find(button => button.textContent === "Try again");
    expect(retryButton).toBeDefined();

    flushSync(() => retryButton?.click());
    await vi.waitFor(async () => {
      await flush();
      expect(manager.login).toHaveBeenCalledTimes(2);
    });
    expect(container.textContent).toContain("Signing in");

    await unmount();
  });

  it("attaches a bearer token and never sends a management-key header on backend requests (#248)", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const manager = stubManager(() => ({ status: "authenticated", roles: [], permissions: [] }));
    manager.getAccessToken = vi.fn(async () => "access-token-1");

    // The authenticated client uses the global fetch; stub it so we can inspect the outgoing request.
    vi.stubGlobal("fetch", fetchMock);
    const context = createStudioEndpointContext("https://foundation.example/", manager);
    await context.http.getJson("/_elsa/module-management/registry");

    const headers = new Headers((fetchMock.mock.calls.at(-1)?.[1] as RequestInit).headers);
    expect(headers.get("Authorization")).toBe("Bearer access-token-1");
    // ADR 0037 / #248: the browser holds no host management key, so no request may carry the header.
    expect(headers.get("X-Elsa-Module-Management-Key")).toBeNull();
  });

  it("falls back to the plain SDK client (no auth) and sends no management-key header when no provider is configured", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const context = createStudioEndpointContext("https://foundation.example/", null);

    await context.http.getJson("/_elsa/module-management/registry");

    const headers = new Headers((fetchMock.mock.calls[0]?.[1] as RequestInit).headers);
    expect(headers.get("Authorization")).toBeNull();
    // The anonymous path carries no management-key header either (ADR 0037 / #248).
    expect(headers.get("X-Elsa-Module-Management-Key")).toBeNull();
  });

  it("exposes a SignalR access-token factory on the authenticated context and none on the anonymous one", async () => {
    const manager = stubManager(() => ({ status: "authenticated", roles: [], permissions: [] }));
    manager.getAccessToken = vi.fn(async () => "access-token-1");

    const authenticated = createStudioEndpointContext("https://foundation.example/", manager);
    expect(authenticated.accessTokenFactory).toBeTypeOf("function");
    await expect(authenticated.accessTokenFactory?.()).resolves.toBe("access-token-1");

    const anonymous = createStudioEndpointContext("https://foundation.example/", null);
    expect(anonymous.accessTokenFactory).toBeUndefined();
  });

  it("uses the manager JWT factory for the host hub credential (no browser management key after #248)", async () => {
    // ADR 0037 / #248: the browser holds no host management key, so the host hub credential is always the manager-based
    // JWT factory (the Studio bridge gate accepts that same backend-issued bearer as ?access_token= on the hub path).
    const manager = stubManager(() => ({ status: "authenticated", roles: [], permissions: [] }));
    manager.getAccessToken = vi.fn(async () => "access-token-1");

    const context = createStudioEndpointContext("https://studio.example/", manager);
    await expect(context.accessTokenFactory?.()).resolves.toBe("access-token-1");
  });

  it("gives the anonymous backend context no hub credential", () => {
    // The anonymous backend context has no user session, so there is no hub credential at all — and never a
    // management key, which the browser no longer holds after ADR 0037 / #248.
    const anonymousBackend = createStudioEndpointContext("https://backend.example/", null);
    expect(anonymousBackend.accessTokenFactory).toBeUndefined();
  });

  it("wires the configured token endpoint into the backend manager", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === "string" ? input : input.toString();
      if (url.endsWith("/_elsa/identity/bootstrap")) {
        return new Response(JSON.stringify({
          ownershipMode: "foundation-owned",
          providers: [{ id: "openiddict", kind: "openiddict", enabled: true, isDefault: true }]
        }), { status: 200 });
      }
      if (url.endsWith("/custom/token")) {
        return new Response(JSON.stringify({ accessToken: "custom-token" }), { status: 200 });
      }
      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    const manager = createStudioAuthManager(
      { auth: { enabled: true, tokenEndpoint: "/custom/token" } },
      "https://foundation.example/"
    );

    await expect(manager?.getAccessToken()).resolves.toBe("custom-token");
  });
});

async function renderApp() {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<App />));
  await flushPromises();
  await flushPromises();

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function renderBoundary(manager: AuthProviderManager) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => {
    root.render(
      <StudioAuthBoundary manager={manager}>
        <div>Dashboard</div>
      </StudioAuthBoundary>
    );
  });

  return {
    container,
    // Let queued microtasks (initialize/login) settle and flush the resulting React state updates.
    flush: async () => {
      await flushPromises();
      flushSync(() => undefined);
    },
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function stubShellFetch() {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === "string" ? input : input.toString();
    if (url.includes("/_elsa/studio/modules")) {
      return new Response(JSON.stringify(emptyModules()), { status: 200 });
    }
    if (url.includes("/_elsa/module-management/registry")) {
      return new Response(JSON.stringify({ modules: [], diagnostics: [] }), { status: 200 });
    }
    // Backend health probe (origin root) and anything else.
    return new Response(JSON.stringify({ status: "Healthy" }), { status: 200, headers: { "content-type": "application/json" } });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function emptyModules(): StudioModulesResponse {
  return { hostVersion: "1.0.0", sdkVersion: "1.0.0", modules: [], diagnostics: [] };
}

function stubManager(getSession: () => AuthSession): AuthProviderManager {
  return {
    getSession: vi.fn(getSession),
    getCapabilities: vi.fn(async () => ({ ownershipMode: "foundation-owned", providers: [] })),
    initialize: vi.fn(async () => getSession()),
    login: vi.fn(async () => undefined),
    handleCallback: vi.fn(async () => getSession()),
    logout: vi.fn(async () => undefined),
    getAccessToken: vi.fn(async () => null),
    refresh: vi.fn(async () => getSession())
  };
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}
