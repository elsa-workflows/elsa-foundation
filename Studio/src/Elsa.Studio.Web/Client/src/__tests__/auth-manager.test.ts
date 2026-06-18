import { afterEach, describe, expect, it, vi } from "vitest";
import {
  AuthConfigurationError,
  createAuthProviderManager,
  createOidcAuthAdapter,
  type AuthBootstrap,
  type AuthCapabilities,
  type AuthProviderAdapter,
  type AuthSession
} from "../auth";

describe("auth provider manager", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("selects the backend default provider without exposing adapters to modules", async () => {
    const oidcAdapter = stubAdapter("entra", "external-oidc", authenticatedSession("alice"));
    const localAdapter = stubAdapter("builtin", "openiddict", authenticatedSession("local"));
    const manager = createAuthProviderManager({
      bootstrap: async () => bootstrap("entra"),
      capabilities: async () => capabilities(),
      adapters: [localAdapter, oidcAdapter]
    });

    const session = await manager.initialize();

    expect(session).toMatchObject({ subject: "alice", provider: { id: "entra", kind: "external-oidc" } });
    expect(oidcAdapter.initialize).toHaveBeenCalledTimes(1);
    expect(localAdapter.initialize).not.toHaveBeenCalled();
  });

  it("routes login to an explicitly selected provider", async () => {
    const oidcAdapter = stubAdapter("entra", "external-oidc", authenticatedSession("alice"));
    const localAdapter = stubAdapter("builtin", "openiddict", authenticatedSession("local"));
    const manager = createAuthProviderManager({
      bootstrap: async () => bootstrap("entra"),
      capabilities: async () => capabilities(),
      adapters: [localAdapter, oidcAdapter]
    });

    await manager.login({ providerId: "builtin", returnUrl: "/studio/" });

    expect(localAdapter.login).toHaveBeenCalledWith({ providerId: "builtin", returnUrl: "/studio/" });
    expect(oidcAdapter.login).not.toHaveBeenCalled();
  });

  it("fails fast when backend selects a provider without a registered adapter", async () => {
    const manager = createAuthProviderManager({
      bootstrap: async () => bootstrap("missing"),
      capabilities: async () => capabilities(),
      adapters: [stubAdapter("entra", "external-oidc", authenticatedSession("alice"))]
    });

    await expect(manager.initialize()).rejects.toBeInstanceOf(AuthConfigurationError);
  });
});

describe("redirect OIDC auth adapter", () => {
  it("starts sign-in through the backend challenge endpoint", async () => {
    const assign = vi.fn();
    const adapter = createOidcAuthAdapter({
      id: "entra",
      challenge: { type: "redirect", loginPath: "/_elsa/identity/challenge/entra" },
      location: {
        assign,
        href: "https://studio.example/workflows",
        origin: "https://studio.example"
      }
    });

    await adapter.login();

    expect(assign).toHaveBeenCalledWith("https://studio.example/_elsa/identity/challenge/entra?returnUrl=https%3A%2F%2Fstudio.example%2Fworkflows");
  });

  it("probes session state with credentials and normalizes missing arrays", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({
      status: "authenticated",
      subject: "alice"
    }), { status: 200 }));
    const adapter = createOidcAuthAdapter({
      id: "entra",
      fetch: fetchMock,
      sessionEndpoint: "/session"
    });

    const session = await adapter.initialize();

    expect(session).toMatchObject({ status: "authenticated", subject: "alice", roles: [], permissions: [] });
    expect(fetchMock.mock.calls[0]).toMatchObject(["/session", { credentials: "include", cache: "no-store" }]);
  });
});

function stubAdapter(id: string, kind: string, session: AuthSession): AuthProviderAdapter {
  return {
    id,
    kind,
    initialize: vi.fn(async () => session),
    login: vi.fn(async () => undefined),
    handleCallback: vi.fn(async () => session),
    logout: vi.fn(async () => undefined),
    getAccessToken: vi.fn(async () => "token"),
    refresh: vi.fn(async () => session)
  };
}

function authenticatedSession(subject: string): AuthSession {
  return {
    status: "authenticated",
    subject,
    roles: ["operator"],
    permissions: ["workflows.read"],
    provider: { id: subject === "alice" ? "entra" : "builtin", kind: subject === "alice" ? "external-oidc" : "openiddict" }
  };
}

function bootstrap(defaultProviderId: string): AuthBootstrap {
  return {
    ownershipMode: "foundation-owned",
    providers: [
      { id: "entra", kind: "external-oidc", enabled: true, isDefault: defaultProviderId === "entra" },
      { id: "builtin", kind: "openiddict", enabled: true, isDefault: defaultProviderId === "builtin" },
      { id: "missing", kind: "external-oidc", enabled: true, isDefault: defaultProviderId === "missing" }
    ]
  };
}

function capabilities(): AuthCapabilities {
  return {
    ownershipMode: "foundation-owned",
    userAuthority: "foundation",
    roleAuthority: "foundation",
    applicationAuthority: "foundation",
    capabilities: {
      supportsLocalUserManagement: true,
      supportsLocalRoleManagement: true,
      supportsApplicationManagement: true,
      supportsGroupSync: false,
      supportsTokenIssuance: true,
      supportsRefresh: true,
      supportsRevocation: true
    },
    providers: []
  };
}
