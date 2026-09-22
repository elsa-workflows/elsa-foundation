import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AuthContext } from "../auth/AuthContext";
import type { AuthContextValue, AuthSession } from "../auth/types";
import { useHostControlAccess, type HostControlAccess } from "../app/useHostControlAccess";

// Locks the proactive host-control access derivation (#249, ADR 0037): the server-side bridge gate is authoritative,
// but the frontend hides/disables affordances the user can't use. The key subtlety is the demo/auth-disabled posture —
// an anonymous session must NOT be held to its (empty) permission set, or the demo shell would lose every affordance.

describe("useHostControlAccess", () => {
  afterEach(() => vi.restoreAllMocks());

  it("grants full access for an anonymous (demo / auth-disabled) session regardless of permissions", () => {
    const access = readAccess(session("anonymous", []));

    expect(access).toEqual({
      canReadModuleManagement: true,
      canManageModuleManagement: true,
      canReadExtensionBuilder: true
    });
  });

  it("grants full access when rendered without an AuthProvider", () => {
    const access = readAccess(undefined);

    expect(access.canReadModuleManagement).toBe(true);
    expect(access.canManageModuleManagement).toBe(true);
    expect(access.canReadExtensionBuilder).toBe(true);
  });

  it("denies every host-control action for an authenticated session with no host-control permissions", () => {
    const access = readAccess(session("authenticated", []));

    expect(access).toEqual({
      canReadModuleManagement: false,
      canManageModuleManagement: false,
      canReadExtensionBuilder: false
    });
  });

  it("grants module read (not manage, not extension builder) to a module-management.read holder", () => {
    const access = readAccess(session("authenticated", ["module-management.read"]));

    expect(access.canReadModuleManagement).toBe(true);
    expect(access.canManageModuleManagement).toBe(false);
    expect(access.canReadExtensionBuilder).toBe(false);
  });

  it("treats module-management.manage as satisfying read too", () => {
    const access = readAccess(session("authenticated", ["module-management.manage"]));

    expect(access.canReadModuleManagement).toBe(true);
    expect(access.canManageModuleManagement).toBe(true);
  });

  it("gates extension builder independently of module-management permissions", () => {
    const moduleOnly = readAccess(session("authenticated", ["module-management.manage"]));
    const ebReader = readAccess(session("authenticated", ["extension-builder.read"]));

    expect(moduleOnly.canReadExtensionBuilder).toBe(false);
    expect(ebReader.canReadExtensionBuilder).toBe(true);
    expect(ebReader.canReadModuleManagement).toBe(false);
  });
});

function session(status: AuthSession["status"], permissions: string[]): AuthSession {
  return { status, roles: [], permissions };
}

// Renders a probe that captures the hook's return, wrapped in a real AuthContext.Provider (or none when session is
// undefined) so the derivation runs against the actual context the app uses.
function readAccess(sessionValue: AuthSession | undefined): HostControlAccess {
  let captured: HostControlAccess | null = null;

  function Probe() {
    captured = useHostControlAccess();
    return null;
  }

  const container = document.createElement("div");
  const root = createRoot(container);
  const tree = sessionValue
    ? <AuthContext.Provider value={{ session: sessionValue } as AuthContextValue}><Probe /></AuthContext.Provider>
    : <Probe />;

  flushSync(() => root.render(tree));
  flushSync(() => root.unmount());

  if (!captured) throw new Error("useHostControlAccess did not produce a value.");
  return captured;
}
