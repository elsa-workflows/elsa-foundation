import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { ShellFrame } from "../app/App";
import { ThemeProvider } from "../app/components/ThemeProvider";

const collapsedKey = "elsa-studio-bottom-panel-collapsed";
const maximizedKey = "elsa-studio-bottom-panel-maximized";

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  window.localStorage.removeItem(collapsedKey);
  window.localStorage.removeItem(maximizedKey);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  window.localStorage.removeItem(collapsedKey);
  window.localStorage.removeItem(maximizedKey);
});

function DummyPanel() {
  return <div>Console output</div>;
}

const panels = [
  { id: "console", title: "Console", component: DummyPanel },
  { id: "logs", title: "Structured Logs", component: DummyPanel }
];

function renderShell() {
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
        <div>page</div>
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
});
