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

  it("closes and clears search after a panel link navigates", async () => {
    const onNavigate = vi.fn();
    await renderShell(onNavigate);
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search modules"]')!;
    setSearch(search, "e");
    expect(moreToolsPanel()).not.toBeNull();

    await act(() => moreToolsPanel()!.querySelector<HTMLAnchorElement>('a[href="/reports"]')!.click());

    expect(onNavigate).toHaveBeenCalledWith("/reports");
    expect(moreToolsPanel()).toBeNull();
    expect(search.value).toBe("");
  });

  it("closes on path changes and keeps unchanged query results dismissed", async () => {
    await renderShell();
    await act(() => moreToolsButton().click());
    expect(moreToolsPanel()).not.toBeNull();

    await renderShell(vi.fn(), "/reports");
    expect(moreToolsPanel()).toBeNull();

    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search modules"]')!;
    setSearch(search, "e");
    expect(moreToolsPanel()).not.toBeNull();

    await renderShell(vi.fn(), "/home");

    expect(moreToolsPanel()).toBeNull();
    expect(search.value).toBe("e");

    setSearch(search, "ec");
    expect(moreToolsPanel()).not.toBeNull();
  });

  it("keeps focus within More tools open, then closes when focus leaves without swallowing navigation", async () => {
    const onNavigate = vi.fn();
    await renderShell(onNavigate);
    await act(() => moreToolsButton().click());

    const panelLink = moreToolsPanel()!.querySelector<HTMLAnchorElement>('a[href="/reports"]')!;
    const moreButton = moreToolsButton();
    const mainLink = container.querySelector<HTMLAnchorElement>('a[href="/home"]')!;
    await act(() => panelLink.focus());
    await act(() => moreButton.focus());
    expect(moreToolsPanel()).not.toBeNull();

    await act(() => mainLink.focus());
    expect(moreToolsPanel()).toBeNull();
    expect(document.activeElement).toBe(mainLink);
    await act(() => mainLink.click());

    expect(onNavigate).toHaveBeenCalledWith("/home");
  });

  it("clears search when a main-section link navigates after focus leaves the overlay", async () => {
    const onNavigate = vi.fn();
    await renderShell(onNavigate);
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search modules"]')!;
    setSearch(search, "e");
    expect(moreToolsPanel()).not.toBeNull();

    const panelLink = moreToolsPanel()!.querySelector<HTMLAnchorElement>('a[href="/reports"]')!;
    const mainLink = container.querySelector<HTMLAnchorElement>('a[href="/home"]')!;
    await act(() => panelLink.focus());
    await act(() => mainLink.focus());
    expect(moreToolsPanel()).toBeNull();

    await act(() => mainLink.click());

    expect(onNavigate).toHaveBeenCalledWith("/home");
    expect(search.value).toBe("");
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
