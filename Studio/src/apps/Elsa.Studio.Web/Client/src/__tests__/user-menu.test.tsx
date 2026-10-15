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

describe("app-bar user menu", () => {
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

  const trigger = () => container.querySelector<HTMLButtonElement>(".topbar-actions .user-menu-button");
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

  it("names the signed-in user in the app bar and opens a menu focused on Sign out", async () => {
    await renderShell(manager);

    expect(trigger()!.textContent).toContain("Alice Admin");
    expect(trigger()!.getAttribute("aria-label")).toBe("Account: Alice Admin");
    expect(trigger()!.getAttribute("aria-haspopup")).toBe("menu");

    await openMenu();

    expect(trigger()!.getAttribute("aria-expanded")).toBe("true");
    expect(document.querySelector('[role="menu"]')!.textContent).toContain("Signed in asAlice Admin");
    expect(signOutItem()!.textContent).toBe("Sign out");
    expect(document.activeElement).toBe(signOutItem());
  });

  it("falls back to the subject when the session carries no display name", async () => {
    session = { ...alice, displayName: null };
    await renderShell(manager);

    expect(trigger()!.textContent).toContain("user-1");
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
