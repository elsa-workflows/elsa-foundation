import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { UserMenu } from "../app/components/UserMenu";

const { sdkUseAuthContext } = vi.hoisted(() => ({ sdkUseAuthContext: vi.fn() }));
vi.mock("@elsa-workflows/studio-sdk", async importOriginal => ({
  ...await importOriginal<typeof import("@elsa-workflows/studio-sdk")>(),
  useAuthContext: sdkUseAuthContext
}));

describe("UserMenu auth boundary", () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    sdkUseAuthContext.mockReturnValue({
      session: { status: "authenticated", subject: "sdk-user", displayName: "SDK Alice", roles: [], permissions: [] },
      capabilities: null,
      login: vi.fn(async () => undefined),
      logout: vi.fn(async () => undefined),
      refresh: vi.fn(async () => ({ status: "authenticated", subject: "sdk-user", displayName: "SDK Alice", roles: [], permissions: [] }))
    });
  });

  afterEach(() => {
    flushSync(() => root.unmount());
    container.remove();
    sdkUseAuthContext.mockReset();
  });

  it("reads the SDK session without requiring the bundled local auth context", () => {
    flushSync(() => root.render(<UserMenu />));

    expect(sdkUseAuthContext).toHaveBeenCalledOnce();
    expect(container.querySelector<HTMLButtonElement>(".user-menu-button")?.getAttribute("aria-label")).toBe("Account: SDK Alice");
    expect(container.textContent).toContain("SDK Alice");
  });
});
