import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ShellFrame } from "../app/App";
import { StudioAuthBoundary } from "../app/auth/studioAuth";
import { ThemeProvider } from "../app/components/ThemeProvider";
import type { AuthProviderManager, AuthSession } from "../sdk";

/** Runs an update synchronously, then yields so the auth provider's async session work and effects settle. */
async function act(update: () => void) {
  flushSync(update);
  for (let tick = 0; tick < 3; tick++) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
}

const alice: AuthSession = { status: "authenticated", subject: "user-1", displayName: "Alice Admin", roles: [], permissions: [] };

describe("shell user menu", () => {
  let container: HTMLDivElement;
  let root: Root;
  let session: AuthSession;
  let manager: AuthProviderManager;

  beforeEach(() => {
    localStorage.clear();
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    session = alice;
    manager = {
      getSession: vi.fn(() => session),
      getCapabilities: vi.fn(async () => ({ ownershipMode: "foundation-owned" as const, providers: [] })),
      initialize: vi.fn(async () => session),
      login: vi.fn(async () => undefined),
      handleCallback: vi.fn(async () => session),
      logout: vi.fn(async () => {
        session = { status: "anonymous", roles: [], permissions: [] };
      }),
      getAccessToken: vi.fn(async () => "token"),
      refresh: vi.fn(async () => session)
    };
  });

  afterEach(async () => {
    await act(() => root.unmount());
    container.remove();
    document.body.innerHTML = "";
  });

  async function renderShell(authManager: AuthProviderManager | null) {
    await act(() => root.render(
      <StudioAuthBoundary manager={authManager}>
        <ThemeProvider>
          <ShellFrame navigation={[]} panels={[]} path="/" title="Home" backendBaseUrl="https://backend.example/" onNavigate={() => {}}>
            <div>content</div>
          </ShellFrame>
        </ThemeProvider>
      </StudioAuthBoundary>
    ));
  }

  const trigger = () => container.querySelector<HTMLButtonElement>(".sidebar-account .user-menu-button, .topbar-actions .user-menu-button");
  const signOutItem = () => document.querySelector<HTMLElement>('[role="menu"] [role="menuitem"]');

  /** Opens the menu from the keyboard, as Radix does for Enter on its trigger. */
  async function openMenu() {
    await act(() => trigger()!.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
  }

  it("shows no account control when sign-in is off", async () => {
    await renderShell(null);

    expect(container.querySelector(".topbar")).not.toBeNull();
    expect(trigger()).toBeNull();
  });

  it("shows the configured backend host without claiming a live health check", async () => {
    await renderShell(null);

    const environment = container.querySelector<HTMLElement>(".sidebar-environment")!;
    expect(environment.textContent).toContain("Backend API");
    expect(environment.textContent).toContain("backend.example");
    expect(environment.title).toContain("connectivity is not measured");
    expect(container.querySelector(".sidebar-account")?.textContent).toBe("");
  });

  it("keeps the signed-in user in the left-rail footer for desktop layouts", async () => {
    await renderShell(manager);

    expect(trigger()!.textContent).toContain("Alice Admin");
    expect(container.querySelector(".sidebar-account .user-menu-button")).toBe(trigger());
    // CSS exposes this second rendering only at responsive widths; the stylesheet policy is asserted in navMode.test.
    expect(container.querySelector(".topbar-actions .user-menu-button")).not.toBeNull();
    expect(trigger()!.getAttribute("aria-label")).toBe("Account: Alice Admin");
    expect(trigger()!.getAttribute("aria-haspopup")).toBe("menu");

    await openMenu();

    expect(trigger()!.getAttribute("aria-expanded")).toBe("true");
    expect(document.querySelector('[role="menu"]')!.textContent).toContain("Signed in asAlice Admin");
    expect(signOutItem()!.textContent).toBe("Sign out");
    expect(document.activeElement).toBe(signOutItem());
  });

  it("keeps the account control in the top bar when navigation is at the top", async () => {
    localStorage.setItem("elsa-studio-nav-mode", "top");
    await renderShell(manager);

    expect(document.documentElement.getAttribute("data-nav-mode")).toBe("top");
    expect(container.querySelector(".topbar-actions .user-menu-button")).not.toBeNull();
    expect(container.querySelector(".sidebar-account .user-menu-button")).toBeNull();
  });

  it("keeps the account control available in the collapsed icon rail", async () => {
    localStorage.setItem("elsa-studio-sidebar-collapsed", "true");
    await renderShell(manager);

    expect(container.querySelector(".studio-shell.sidebar-collapsed")).not.toBeNull();
    expect(container.querySelector(".sidebar-account .user-menu-button")).not.toBeNull();
  });

  it("falls back to the subject when the session carries no display name", async () => {
    session = { ...alice, displayName: null };
    await renderShell(manager);

    expect(trigger()!.textContent).toContain("user-1");
  });

  it("takes whole code points for the avatar initials, so a non-BMP first letter is not split", async () => {
    session = { ...alice, displayName: "\u{1D49C}da Lovelace" };
    await renderShell(manager);

    expect(trigger()!.querySelector(".user-menu-avatar")!.textContent).toBe("\u{1D49C}L");
  });

  it("signs out through the provider, then hands the anonymous session to the login redirect", async () => {
    await renderShell(manager);
    await openMenu();

    await act(() => signOutItem()!.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));

    expect(manager.logout).toHaveBeenCalledTimes(1);
    // RequireAuth answers the anonymous session by starting the provider's challenge, the backend login page.
    expect(manager.login).toHaveBeenCalledTimes(1);
    expect(trigger()).toBeNull();
    expect(container.textContent).toContain("Signing in");
  });

  it("keeps the menu open and reports a failed sign-out", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    manager.logout = vi.fn(async () => {
      throw new Error("Sign-out failed with 500.");
    });
    await renderShell(manager);
    await openMenu();

    await act(() => signOutItem()!.click());

    expect(document.querySelector('[role="alert"]')!.textContent).toBe("Sign-out failed. Try again.");
    expect(signOutItem()!.hasAttribute("data-disabled")).toBe(false);
    expect(manager.login).not.toHaveBeenCalled();
    expect(trigger()!.textContent).toContain("Alice Admin");
  });
});
