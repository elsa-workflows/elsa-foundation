import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ShellFrame } from "../app/App";
import { ThemeProvider } from "../app/components/ThemeProvider";
import { moreToolsStorageKey } from "../app/navigation/placement";
import { navModeStorageKey } from "../app/themes/navMode";
import type { StudioNavigationContribution } from "../sdk";

const navigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/home", placement: "main" },
  { id: "reports", label: "Reports", path: "/reports", placement: "more" },
  { id: "recent-reports", label: "Recent reports", path: "/reports/recent", parentId: "reports" }
];

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem(navModeStorageKey, "top");
  localStorage.setItem(moreToolsStorageKey, "true");
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(() => root.unmount());
  container.remove();
  localStorage.clear();
  document.documentElement.removeAttribute("data-nav-mode");
});

describe("top navigation More tools overlay", () => {
  it("keeps the sidebar preference without restoring it as an open overlay", async () => {
    await renderShell();

    expect(document.documentElement.getAttribute("data-nav-mode")).toBe("top");
    expect(moreToolsButton().getAttribute("aria-expanded")).toBe("false");
    expect(moreToolsPanel()).toBeNull();

    await act(() => moreToolsButton().click());
    expect(moreToolsPanel()).not.toBeNull();
    expect(localStorage.getItem(moreToolsStorageKey)).toBe("true");
  });

  it("closes after a panel link navigates", async () => {
    const onNavigate = vi.fn();
    await renderShell(onNavigate);
    await act(() => moreToolsButton().click());

    await act(() => moreToolsPanel()!.querySelector<HTMLAnchorElement>('a[href="/reports"]')!.click());

    expect(onNavigate).toHaveBeenCalledWith("/reports");
    expect(moreToolsPanel()).toBeNull();
  });

  it("closes on Escape and returns focus to the More tools button", async () => {
    await renderShell();
    await act(() => moreToolsButton().click());
    const link = moreToolsPanel()!.querySelector<HTMLAnchorElement>('a[href="/reports"]')!;
    link.focus();
    const event = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true });

    await act(() => link.dispatchEvent(event));

    expect(event.defaultPrevented).toBe(true);
    expect(moreToolsPanel()).toBeNull();
    expect(document.activeElement).toBe(moreToolsButton());
  });

  it("dismisses query-opened results outside the panel and reopens when the query changes", async () => {
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search modules"]')!;
    setSearch(search, "Reports");
    expect(moreToolsPanel()).not.toBeNull();

    await act(() => container.querySelector("#page-content")!.dispatchEvent(new Event("pointerdown", { bubbles: true })));

    expect(moreToolsPanel()).toBeNull();
    expect(search.value).toBe("Reports");

    setSearch(search, "Recent");
    expect(moreToolsPanel()).not.toBeNull();
    expect(moreToolsPanel()?.textContent).toContain("Recent reports");
  });
});

async function renderShell(onNavigate = vi.fn()) {
  await act(() => root.render(
    <ThemeProvider>
      <ShellFrame
        navigation={navigation}
        panels={[]}
        path="/home"
        title="Home"
        backendBaseUrl="https://backend.example/"
        onNavigate={onNavigate}
      >
        <div id="page-content">Page content</div>
      </ShellFrame>
    </ThemeProvider>
  ));
}

async function act(update: () => void) {
  flushSync(update);
  for (let tick = 0; tick < 3; tick++) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
}

function moreToolsButton() {
  return container.querySelector<HTMLButtonElement>('button.nav-disclosure[aria-controls="studio-nav-more-tools"]')!;
}

function moreToolsPanel() {
  return container.querySelector<HTMLElement>("#studio-nav-more-tools");
}

function setSearch(input: HTMLInputElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
    setter.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
