import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ShellFrame } from "../app/App";
import { ThemeProvider } from "../app/components/ThemeProvider";
import { navigationGroupsStorageKey } from "../app/navigation/placement";
import { navModeStorageKey } from "../app/themes/navMode";
import type { StudioNavigationContribution } from "../sdk";

const navigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/home", placement: "main" },
  { id: "reports", label: "Reports", path: "/reports", placement: "build" },
  { id: "recent-reports", label: "Recent reports", path: "/reports/recent", parentId: "reports" }
];

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem(navModeStorageKey, "top");
  localStorage.setItem(navigationGroupsStorageKey, JSON.stringify({ build: true }));
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

describe("top navigation Build overlay", () => {
  it("keeps the sidebar preference without restoring it as an open overlay", async () => {
    await renderShell();

    expect(document.documentElement.getAttribute("data-nav-mode")).toBe("top");
    expect(groupButton().getAttribute("aria-expanded")).toBe("false");
    expect(groupPanel()).toBeNull();

    await act(() => groupButton().click());
    expect(groupPanel()).not.toBeNull();
    expect(localStorage.getItem(navigationGroupsStorageKey)).toBe(JSON.stringify({ build: true }));
  });

  it("closes and clears search after a panel link navigates", async () => {
    const onNavigate = vi.fn();
    await renderShell(onNavigate);
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    setSearch(search, "e");
    expect(groupPanel()).not.toBeNull();

    await act(() => groupPanel()!.querySelector<HTMLAnchorElement>('a[href="/reports"]')!.click());

    expect(onNavigate).toHaveBeenCalledWith("/reports");
    expect(groupPanel()).toBeNull();
    expect(search.value).toBe("");
  });

  it("closes on path changes and keeps unchanged query results dismissed", async () => {
    await renderShell();
    await act(() => groupButton().click());
    expect(groupPanel()).not.toBeNull();

    await renderShell(vi.fn(), "/reports");
    expect(groupPanel()).toBeNull();

    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    setSearch(search, "e");
    expect(groupPanel()).not.toBeNull();

    await renderShell(vi.fn(), "/home");

    expect(groupPanel()).toBeNull();
    expect(search.value).toBe("e");

    setSearch(search, "ec");
    expect(groupPanel()).not.toBeNull();
  });

  it("keeps focus within Build open, then closes when focus leaves without swallowing navigation", async () => {
    const onNavigate = vi.fn();
    await renderShell(onNavigate);
    await act(() => groupButton().click());

    const panelLink = groupPanel()!.querySelector<HTMLAnchorElement>('a[href="/reports"]')!;
    const moreButton = groupButton();
    const mainLink = container.querySelector<HTMLAnchorElement>('a[href="/home"]')!;
    await act(() => panelLink.focus());
    await act(() => moreButton.focus());
    expect(groupPanel()).not.toBeNull();

    await act(() => mainLink.focus());
    expect(groupPanel()).toBeNull();
    expect(document.activeElement).toBe(mainLink);
    await act(() => mainLink.click());

    expect(onNavigate).toHaveBeenCalledWith("/home");
  });

  it("closes search results when focus leaves for a page control", async () => {
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    setSearch(search, "e");
    const panelLink = groupPanel()!.querySelector<HTMLAnchorElement>('a[href="/reports"]')!;
    await act(() => panelLink.focus());
    const outside = document.createElement("button");
    container.append(outside);
    await act(() => outside.focus());
    expect(groupPanel()).toBeNull();
    expect(document.activeElement).toBe(outside);
    expect(search.value).toBe("e");
    outside.remove();
  });

  it("closes on Escape and returns focus to the Build button", async () => {
    await renderShell();
    await act(() => groupButton().click());
    const link = groupPanel()!.querySelector<HTMLAnchorElement>('a[href="/reports"]')!;
    link.focus();
    const event = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true });

    await act(() => link.dispatchEvent(event));

    expect(event.defaultPrevented).toBe(true);
    expect(groupPanel()).toBeNull();
    expect(document.activeElement).toBe(groupButton());
  });

  it("dismisses query-opened results outside the panel and reopens when the query changes", async () => {
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    setSearch(search, "Reports");
    expect(groupPanel()).not.toBeNull();

    await act(() => container.querySelector("#page-content")!.dispatchEvent(new Event("pointerdown", { bubbles: true })));

    expect(groupPanel()).toBeNull();
    expect(search.value).toBe("Reports");

    setSearch(search, "Recent");
    expect(groupPanel()).not.toBeNull();
    expect(groupPanel()?.textContent).toContain("Recent reports");
  });
});

async function renderShell(onNavigate = vi.fn(), path = "/home") {
  await act(() => root.render(
    <ThemeProvider>
      <ShellFrame
        navigation={navigation}
        panels={[]}
        path={path}
        title={path === "/home" ? "Home" : "Reports"}
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

function groupButton() {
  return container.querySelector<HTMLButtonElement>('nav[aria-label="Build"] button.nav-group-toggle')!;
}

function groupPanel() {
  return container.querySelector<HTMLElement>('nav[aria-label="Build"] .nav-group-panel:not([hidden]), nav.nav-search-results:not([hidden])');
}

function setSearch(input: HTMLInputElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
    setter.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
