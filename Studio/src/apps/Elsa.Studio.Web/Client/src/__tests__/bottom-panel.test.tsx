import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { ShellFrame } from "../app/App";
import { ThemeProvider } from "../app/components/ThemeProvider";
import { AppearancePage } from "../app/settings/AppearancePage";
import { appearanceResetStorageKeys } from "../app/themes/appearance";

const collapsedKey = "elsa-studio-bottom-panel-collapsed";
const maximizedKey = "elsa-studio-bottom-panel-maximized";
const originalHeight = window.innerHeight;

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  appearanceResetStorageKeys.forEach(key => window.localStorage.removeItem(key));
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  Object.defineProperty(window, "innerHeight", { configurable: true, value: originalHeight });
  appearanceResetStorageKeys.forEach(key => window.localStorage.removeItem(key));
});

function DummyPanel() {
  return <div>Console output</div>;
}

const panels = [
  { id: "console", title: "Console", component: DummyPanel },
  { id: "logs", title: "Structured Logs", component: DummyPanel }
];

function renderShell(children: React.ReactNode = <div>page</div>) {
  flushSync(() => root.render(
    <ThemeProvider>
      <ShellFrame
        navigation={[{ id: "dashboard", label: "Dashboard", path: "/dashboard" }]}
        panels={panels}
        path="/dashboard"
        title="Dashboard"
        backendBaseUrl="https://backend.example/"
        onNavigate={() => {}}
      >
        {children}
      </ShellFrame>
    </ThemeProvider>
  ));
}

function bottomPanel() {
  return container.querySelector(".bottom-panel");
}

describe("bottom panel default", () => {
  it("starts collapsed on a wide viewport when the user has never stored a choice", () => {
    Object.defineProperty(window, "innerWidth", { configurable: true, value: 1600 });
    renderShell();

    expect(bottomPanel()?.classList.contains("collapsed")).toBe(true);
    expect(container.querySelector('[aria-label="Expand bottom panel"]')).not.toBeNull();
    expect(container.querySelector(".bottom-panel-content")?.hasAttribute("inert")).toBe(true);
    expect(container.querySelector('[role="tablist"][aria-label="Bottom panels"]')).not.toBeNull();
  });

  it("stays expanded when the stored choice is open", () => {
    window.localStorage.setItem(collapsedKey, "false");
    renderShell();

    expect(bottomPanel()?.classList.contains("collapsed")).toBe(false);
    expect(container.querySelector('[aria-label="Collapse bottom panel"]')).not.toBeNull();
    expect(container.querySelector(".bottom-panel-content")?.hasAttribute("inert")).toBe(false);
  });

  it("stays collapsed when the stored choice is collapsed", () => {
    window.localStorage.setItem(collapsedKey, "true");
    renderShell();

    expect(bottomPanel()?.classList.contains("collapsed")).toBe(true);
  });

  it("opens when the stored maximized preference is set", () => {
    window.localStorage.setItem(maximizedKey, "true");
    renderShell();

    expect(bottomPanel()?.classList.contains("collapsed")).toBe(false);
    expect(bottomPanel()?.classList.contains("maximized")).toBe(true);
  });

  it("remembers an explicit expand across remount", () => {
    renderShell();
    const expand = container.querySelector<HTMLButtonElement>('[aria-label="Expand bottom panel"]')!;
    flushSync(() => expand.click());

    expect(bottomPanel()?.classList.contains("collapsed")).toBe(false);
    expect(window.localStorage.getItem(collapsedKey)).toBe("false");

    flushSync(() => root.unmount());
    root = createRoot(container);
    renderShell();

    expect(bottomPanel()?.classList.contains("collapsed")).toBe(false);
  });

  it("keeps Reset appearance preferences cleared after the mounted shell restores its defaults", async () => {
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 900 });
    const stored: Record<string, string> = {
      "elsa-studio-theme": "drift",
      "elsa-studio-theme-mode": "dark",
      "elsa-studio-nav-mode": "top",
      "elsa-studio-sidebar-collapsed": "true",
      "elsa-studio-bottom-panel-collapsed": "false",
      "elsa-studio-bottom-panel-height": "320",
      "elsa-studio-bottom-panel-maximized": "true",
      "elsa-studio-active-bottom-panel": "logs"
    };
    Object.entries(stored).forEach(([key, value]) => localStorage.setItem(key, value));
    renderShell(<AppearancePage onNavigate={() => {}} />);
    for (let tick = 0; tick < 3; tick++) await new Promise(resolve => setTimeout(resolve, 0));
    expect(bottomPanel()?.classList.contains("maximized")).toBe(true);
    expect(bottomPanel()?.getAttribute("style")).toContain("320px");

    const reset = Array.from(container.querySelectorAll("button"))
      .find(button => button.textContent?.trim() === "Reset appearance")!;
    flushSync(() => reset.click());
    for (let tick = 0; tick < 3; tick++) await new Promise(resolve => setTimeout(resolve, 0));

    expect(bottomPanel()?.classList.contains("collapsed")).toBe(true);
    expect(bottomPanel()?.classList.contains("maximized")).toBe(false);
    expect(bottomPanel()?.getAttribute("style")).toContain("260px");
    appearanceResetStorageKeys.forEach(key => expect(localStorage.getItem(key), key).toBeNull());

    flushSync(() => container.querySelector<HTMLButtonElement>('[aria-label="Expand bottom panel"]')!.click());
    const resize = container.querySelector('[aria-label="Resize bottom panel"]')!;
    flushSync(() => resize.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowUp", bubbles: true })));
    expect(localStorage.getItem("elsa-studio-bottom-panel-height")).toBe("276");
    flushSync(() => container.querySelector<HTMLButtonElement>('[data-tab-id="logs"]')!.click());
    expect(localStorage.getItem("elsa-studio-active-bottom-panel")).toBe("logs");
    expect(localStorage.getItem(collapsedKey)).toBe("false");
  });
});
