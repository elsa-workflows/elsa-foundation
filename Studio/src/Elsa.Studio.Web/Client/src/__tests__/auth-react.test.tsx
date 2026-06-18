import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  AuthGuard,
  AuthProvider,
  RequireAuth,
  useAuthCapabilities,
  useAuthSession,
  usePermissions,
  type AuthCapabilities,
  type AuthProviderManager,
  type AuthSession
} from "../auth";

afterEach(() => {
  document.body.innerHTML = "";
});

describe("auth React SDK", () => {
  it("exposes normalized session, permissions, and capabilities through hooks", async () => {
    const manager = stubManager(authenticatedSession());
    const { container, unmount } = renderWithAuth(manager, <AuthProbe />);

    await flushPromises();

    expect(container.textContent).toContain("Alice");
    expect(container.textContent).toContain("can-read");
    expect(container.textContent).toContain("foundation-owned");

    await unmount();
  });

  it("gates presentation without making Studio the authorization source of truth", async () => {
    const manager = stubManager(authenticatedSession());
    const { container, unmount } = renderWithAuth(manager, (
      <>
        <AuthGuard requires="workflows.read" fallback={<span>hidden-read</span>}>
          <span>shown-read</span>
        </AuthGuard>
        <AuthGuard requires="workflows.write" fallback={<span>hidden-write</span>}>
          <span>shown-write</span>
        </AuthGuard>
      </>
    ));

    await flushPromises();

    expect(container.textContent).toContain("shown-read");
    expect(container.textContent).toContain("hidden-write");
    expect(container.textContent).not.toContain("shown-write");

    await unmount();
  });

  it("starts login from route guards when the session is anonymous", async () => {
    const manager = stubManager({ status: "anonymous", roles: [], permissions: [] });
    const { container, unmount } = renderWithAuth(manager, (
      <RequireAuth fallback={<span>signing-in</span>}>
        <span>protected</span>
      </RequireAuth>
    ));

    await flushPromises();

    expect(container.textContent).toContain("signing-in");
    expect(manager.login).toHaveBeenCalledTimes(1);

    await unmount();
  });
});

function AuthProbe() {
  const session = useAuthSession();
  const permissions = usePermissions();
  const capabilities = useAuthCapabilities();

  return (
    <span>
      {session.displayName}
      {permissions.has("workflows.read") ? " can-read" : " cannot-read"}
      {capabilities?.ownershipMode}
    </span>
  );
}

function renderWithAuth(manager: AuthProviderManager, children: React.ReactNode) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(<AuthProvider manager={manager}>{children}</AuthProvider>);
  });

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function stubManager(session: AuthSession): AuthProviderManager {
  let currentSession = session;

  return {
    getSession: vi.fn(() => currentSession),
    getCapabilities: vi.fn(async () => capabilities()),
    initialize: vi.fn(async () => currentSession),
    login: vi.fn(async () => undefined),
    handleCallback: vi.fn(async () => currentSession),
    logout: vi.fn(async () => {
      currentSession = { status: "anonymous", roles: [], permissions: [] };
    }),
    getAccessToken: vi.fn(async () => "token"),
    refresh: vi.fn(async () => currentSession)
  };
}

function authenticatedSession(): AuthSession {
  return {
    status: "authenticated",
    subject: "alice",
    displayName: "Alice",
    roles: ["operator"],
    permissions: ["workflows.read"],
    provider: { id: "entra", kind: "external-oidc" }
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
    providers: [
      { id: "entra", kind: "external-oidc", enabled: true, isDefault: true }
    ]
  };
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}
