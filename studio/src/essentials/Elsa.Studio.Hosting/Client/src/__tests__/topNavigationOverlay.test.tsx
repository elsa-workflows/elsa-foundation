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

  it.each(["absent-destination", "Reports"])("dismisses input-to-outside focus for %s without stealing later Escape", async query => {
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    const outside = document.createElement("button");
    container.append(outside);
    await act(() => search.focus());
    setSearch(search, query);
    const results = groupPanel()!;
    expect(results).not.toBeNull();
    await act(() => outside.focus());
    expect(results.hidden).toBe(true);
    expect(search.value).toBe(query);
    expect(document.activeElement).toBe(outside);
    const escape = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true });
    await act(() => outside.dispatchEvent(escape));
    expect(escape.defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(outside);
    await act(() => search.focus());
    setSearch(search, query + " again");
    expect(groupPanel()).not.toBeNull();
    outside.remove();
  });

  it("keeps input/result focus transitions open in both directions", async () => {
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    await act(() => search.focus());
    setSearch(search, "Reports");
    const results = groupPanel()!;
    const result = results.querySelector<HTMLAnchorElement>('a[href="/reports"]')!;
    await act(() => result.focus());
    expect(results.hidden).toBe(false);
    await act(() => search.focus());
    expect(results.hidden).toBe(false);
    expect(search.value).toBe("Reports");
    await act(() => search.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
    expect(results.hidden).toBe(true);
    expect(document.activeElement).toBe(search);
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

  it("keeps top no-match feedback inside the dismissible results surface", async () => {
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    setSearch(search, "absent-destination");
    const results = groupPanel()!;
    expect(results.querySelector('[role="status"]')?.textContent).toBe('No destinations match "absent-destination".');
    expect(container.querySelectorAll('.sidebar-search-empty')).toHaveLength(1);
    search.focus();

    await act(() => search.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));

    expect(groupPanel()).toBeNull();
    expect(results.hidden).toBe(true);
    expect(document.activeElement).toBe(search);
    expect(search.value).toBe("absent-destination");
    setSearch(search, "absent-destination-again");
    expect(groupPanel()?.querySelector('[role="status"]')?.textContent).toContain("absent-destination-again");
    await act(() => container.querySelector("#page-content")!.dispatchEvent(new Event("pointerdown", { bubbles: true })));
    expect(groupPanel()).toBeNull();
    expect(search.value).toBe("absent-destination-again");
  });

  it("returns focus from a search result to Search Studio on Escape", async () => {
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    setSearch(search, "Reports");
    const results = groupPanel()!;
    const result = results.querySelector<HTMLAnchorElement>('a[href="/reports"]')!;
    await act(() => result.focus());
    expect(document.activeElement).toBe(result);

    await act(() => result.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));

    expect(results.hidden).toBe(true);
    expect(groupPanel()).toBeNull();
    expect(document.activeElement).toBe(search);
    expect(search.value).toBe("Reports");
  });

  it.each(["Escape", "outside pointer", "outside focus", "navigation"])("releases %s dismissal after editing away and back to the same query", async dismissal => {
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    await act(() => search.focus());
    setSearch(search, "absent-destination");
    const results = groupPanel()!;
    expect(results.querySelector('[role="status"]')?.textContent).toBe('No destinations match "absent-destination".');
    const outside = document.createElement("button");
    container.append(outside);

    if (dismissal === "Escape") {
      await act(() => search.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
      expect(document.activeElement).toBe(search);
    } else if (dismissal === "outside pointer") {
      await act(() => outside.dispatchEvent(new Event("pointerdown", { bubbles: true })));
    } else if (dismissal === "outside focus") {
      await act(() => outside.focus());
      expect(document.activeElement).toBe(outside);
    } else {
      await renderShell(vi.fn(), "/reports");
    }
    expect(results.hidden).toBe(true);
    expect(search.value).toBe("absent-destination");
    await renderShell(vi.fn(), dismissal === "navigation" ? "/reports" : "/home");
    expect(results.hidden).toBe(true);
    await act(() => search.focus());

    setSearch(search, "Reports");
    expect(groupPanel()).toBe(results);
    expect(results.querySelector('a[href="/reports"]')).not.toBeNull();
    setSearch(search, "absent-destination");
    expect(groupPanel()).toBe(results);
    expect(results.hidden).toBe(false);
    expect(results.querySelector('[role="status"]')?.textContent).toBe('No destinations match "absent-destination".');
    expect(document.activeElement).toBe(search);
    outside.remove();
  });

  it("reopens after a raw edit that leaves the filtering query unchanged", async () => {
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    await act(() => search.focus());
    setSearch(search, "Reports");
    const results = groupPanel()!;
    await act(() => search.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
    expect(results.hidden).toBe(true);
    setSearch(search, " Reports ");
    expect(groupPanel()).toBe(results);
    expect(results.querySelector('a[href="/reports"]')).not.toBeNull();
    expect(search.value).toBe(" Reports ");
    expect(document.activeElement).toBe(search);
  });

  it("preserves inline no-match feedback and Escape clearing in left navigation", async () => {
    localStorage.setItem(navModeStorageKey, "left");
    await renderShell();
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search Studio"]')!;
    setSearch(search, "absent-destination");
    const status = container.querySelector('.sidebar-search-empty')!;
    expect(status.textContent).toBe('No destinations match "absent-destination".');
    expect(status.closest('.nav-search-results')).toBeNull();
    expect(container.querySelector('.nav-search-results')).toBeNull();
    search.focus();

    await act(() => search.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));

    expect(search.value).toBe("");
    expect(container.querySelector('.sidebar-search-empty')).toBeNull();
    expect(document.activeElement).toBe(search);
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
